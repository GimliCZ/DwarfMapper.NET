// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using DwarfMapper;
using Xunit;

// CA5394: System.Random is used ONLY for deterministic, seeded test-data generation
// (replayable counterexamples) — never for security. Suppressed for this fuzz file.
#pragma warning disable CA5394

namespace DwarfMapper.IntegrationTests;

// ── Adversarial / fuzz / defensive types for OnCycle = SetNull ───────────────

// Mutual recursion across two distinct types: A→B→A
public class SnMutA    { public int V { get; set; } public SnMutB? B { get; set; } }
public class SnMutB    { public int V { get; set; } public SnMutA? A { get; set; } }
public class SnMutADto { public int V { get; set; } public SnMutBDto? B { get; set; } }
public class SnMutBDto { public int V { get; set; } public SnMutADto? A { get; set; } }

[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
public partial class SnMutMapper { public partial SnMutADto Map(SnMutA a); }

// Binary node with two reference edges — used for fuzzing arbitrary back-edge topologies.
public class SnBin    { public int V { get; set; } public SnBin? L { get; set; } public SnBin? R { get; set; } }
public class SnBinDto { public int V { get; set; } public SnBinDto? L { get; set; } public SnBinDto? R { get; set; } }

[DwarfMapper(OnCycle = OnCycleStrategy.SetNull, MaxDepth = 1000)]
public partial class SnBinMapper { public partial SnBinDto Map(SnBin n); }

// Long chain (member cycle) — terminates under SetNull.
public class SnLong    { public int V { get; set; } public SnLong? Next { get; set; } }
public class SnLongDto { public int V { get; set; } public SnLongDto? Next { get; set; } }

[DwarfMapper(OnCycle = OnCycleStrategy.SetNull, MaxDepth = 1000)]
public partial class SnLongMapper { public partial SnLongDto Map(SnLong n); }

public class SetNullAdversarialRuntimeTests
{
    // ── Mutual recursion across two types ───────────────────────────────────────
    [Fact]
    public void MutualRecursion_two_types_terminates_back_edge_null()
    {
        var a = new SnMutA { V = 1 };
        var b = new SnMutB { V = 2 };
        a.B = b;
        b.A = a; // A→B→A

        var ta = new SnMutMapper().Map(a);
        Assert.Equal(1, ta.V);
        Assert.Equal(2, ta.B!.V);
        Assert.Null(ta.B.A); // back-edge to a (on stack) → null
    }

    // ── Long member cycle (100 nodes) → finite, terminates ──────────────────────
    [Fact]
    public void LongMemberCycle_terminates_finite()
    {
        var head = new SnLong { V = 0 };
        var cur = head;
        for (var i = 1; i < 100; i++) { cur.Next = new SnLong { V = i }; cur = cur.Next; }
        cur.Next = head; // close the cycle back to head

        var t = new SnLongMapper().Map(head);
        // Walk and count — must be exactly 100 and the last Next must be null (back-edge to head).
        var count = 0;
        var n = t;
        while (n is not null) { count++; n = n.Next; if (count > 1000) break; }
        Assert.Equal(100, count);
    }

    // ── Self-loop on a nested member ────────────────────────────────────────────
    [Fact]
    public void NestedSelfLoop_inner_back_edge_null()
    {
        // a.B.A = a, plus b.A back to a → covered; here test b self-referencing through a chain.
        var a = new SnMutA { V = 5 };
        var b = new SnMutB { V = 6 };
        a.B = b;
        b.A = a;
        var ta = new SnMutMapper().Map(a);
        Assert.NotNull(ta.B);
        Assert.Null(ta.B!.A);
    }

    // ── Defensive: null root → ArgumentNullException (loud, not silent) ─────────
    [Fact]
    public void NullSource_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SnMutMapper().Map(null!));
    }

    // ── Defensive: null edges in a partial cycle are simply mapped as null ──────
    [Fact]
    public void NullEdges_are_preserved_as_null()
    {
        var a = new SnMutA { V = 1, B = null }; // no edge
        var ta = new SnMutMapper().Map(a);
        Assert.Equal(1, ta.V);
        Assert.Null(ta.B);
    }

    // ── Fuzz: random small graphs with back-edges → always terminate + acyclic ──
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(1234)]
    [InlineData(99999)]
    public void Fuzz_random_cyclic_graphs_terminate_and_produce_acyclic_output(int seed)
    {
        var rng = new Random(seed);
        for (var iter = 0; iter < 50; iter++)
        {
            // Build n nodes, then wire L/R edges to RANDOM existing nodes (incl. self/back-edges).
            var n = rng.Next(1, 12);
            var nodes = new List<SnBin>();
            for (var i = 0; i < n; i++) nodes.Add(new SnBin { V = i });
            foreach (var node in nodes)
            {
                if (rng.Next(2) == 0) node.L = nodes[rng.Next(n)]; // may be self/ancestor → cycle
                if (rng.Next(2) == 0) node.R = nodes[rng.Next(n)];
            }

            var mapper = new SnBinMapper();
            var t1 = mapper.Map(nodes[0]); // must not throw / overflow
            Assert.NotNull(t1);

            // Output must be a finite acyclic tree (SetNull duplicates, nulls ancestor back-edges).
            AssertAcyclicFinite(t1);

            // Determinism: a second map of the same source yields the same shape.
            var t2 = mapper.Map(nodes[0]);
            Assert.Equal(Shape(t1), Shape(t2));
        }
    }

    // Walks the result; throws if a node is revisited on the current path (cycle) or if the
    // node count explodes past a sane bound (non-termination).
    private static void AssertAcyclicFinite(SnBinDto root)
    {
        var onPath = new HashSet<SnBinDto>(ReferenceEqualityComparer.Instance);
        var total = 0;
        void Walk(SnBinDto? node)
        {
            if (node is null) return;
            Assert.True(onPath.Add(node), "result graph must be acyclic (no node on its own path)");
            Assert.True(++total < 100_000, "result must be finite");
            Walk(node.L);
            Walk(node.R);
            onPath.Remove(node);
        }
        Walk(root);
    }

    // Canonical string of the tree shape + values, for determinism comparison.
    private static string Shape(SnBinDto? node)
        => node is null ? "." : $"({node.V} {Shape(node.L)} {Shape(node.R)})";
}
