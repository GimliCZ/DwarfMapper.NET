// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Generic;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// Regression guard for the CVE class that hit AutoMapper (GHSA-rvv3-g6hj-g44x / CVE-2026-32933, CVSS 7.5):
// a deeply nested — but ACYCLIC — self-referential graph exhausts the stack. A StackOverflowException is
// uncatchable in .NET: it kills the process. Attacker-supplied deep JSON is therefore a DoS.
//
// The lesson from that CVE is NOT "have a depth guard" — AutoMapper HAD MaxDepth for years. It was OPT-IN,
// so nobody set it. The guard must be ON BY DEFAULT. These tests deliberately declare NO MaxDepth, so they
// fail if the default ever regresses to unguarded.
//
// Note this is a distinct failure mode from cycle handling: a long chain has no cycle, so a visited-set
// never trips. Only a depth counter catches it. And it must trip via a catchable exception BEFORE the
// generated code's own recursive frames blow the stack.

public sealed class DeepChain
{
    public int V { get; set; }

    public DeepChain? Next { get; set; }
}

public sealed class DeepChainDto
{
    public int V { get; set; }

    public DeepChainDto? Next { get; set; }
}

// The nastier variant: the recursion is mediated by a COLLECTION edge rather than a direct self-reference.
// AutoMapper's protection did not generalise to this shape — it is exactly the class this project's own
// notes flag as the non-obvious one.
public sealed class DeepCollChain
{
    public int V { get; set; }

    public List<DeepCollChain> Kids { get; set; } = new();
}

public sealed class DeepCollChainDto
{
    public int V { get; set; }

    public List<DeepCollChainDto> Kids { get; set; } = new();
}

[DwarfMapper] // deliberately NO MaxDepth — proves the guard is on by DEFAULT
[GenerateMap<DeepChain, DeepChainDto>]
public partial class DeepChainMapper;

// Declared via [GenerateMap<S,T>] (no partial method) AND with no MaxDepth — the exact shape that used to
// emit uncompilable code: the object helper became recursion-capable in place, but the collection helper
// calling it was never re-synthesized, so it passed one argument to a 3-parameter method (CS7036). The
// equivalent partial-method mapper always worked, which is what hid this.
[DwarfMapper]
[GenerateMap<DeepCollChain, DeepCollChainDto>]
public partial class DeepCollChainMapper;

public class DeepRecursionGuardRuntimeTests
{
    private static DeepChain BuildChain(int depth)
    {
        var head = new DeepChain { V = 0 };
        var cur = head;
        for (var i = 1; i < depth; i++)
        {
            cur.Next = new DeepChain { V = i };
            cur = cur.Next;
        }

        return head;
    }

    private static DeepCollChain BuildCollChain(int depth)
    {
        var head = new DeepCollChain { V = 0 };
        var cur = head;
        for (var i = 1; i < depth; i++)
        {
            var next = new DeepCollChain { V = i };
            cur.Kids.Add(next);
            cur = next;
        }

        return head;
    }

    [Fact]
    public void Deep_acyclic_chain_throws_a_catchable_depth_exception_not_a_stack_overflow()
    {
        var deep = BuildChain(500); // far beyond any sane default depth cap

        // The whole point: a CATCHABLE exception. If the guard were off, this would either succeed (silently
        // unbounded) or, at greater depth, kill the test process outright with a StackOverflowException.
        Assert.Throws<DwarfMappingDepthException>(() => new DeepChainMapper().Map(deep));
    }

    [Fact]
    public void Deep_chain_through_a_COLLECTION_edge_is_also_guarded()
    {
        // The shape AutoMapper's guard missed: recursion mediated by a collection element rather than a
        // direct self-reference.
        var deep = BuildCollChain(500);

        Assert.Throws<DwarfMappingDepthException>(() => new DeepCollChainMapper().Map(deep));
    }

    [Fact]
    public void A_shallow_chain_still_maps_normally()
    {
        // Guard must not be trigger-happy: ordinary depths keep working.
        var shallow = BuildChain(8);

        var dto = new DeepChainMapper().Map(shallow);

        var n = 0;
        for (var cur = dto; cur is not null; cur = cur.Next)
        {
            Assert.Equal(n++, cur.V);
        }

        Assert.Equal(8, n);
    }
}
