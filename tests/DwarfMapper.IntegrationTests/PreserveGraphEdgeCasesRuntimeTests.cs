// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// ═══════════════════════════════════════════════════════════════════════════
// B1 — Depth guard fires before identity-map check under Preserve
//       Fix: TryGetReference → depth guard (not depth guard → TryGetReference)
// ═══════════════════════════════════════════════════════════════════════════

// Deep + shared: a node reachable via a long path AND a short path.
// If the identity map check comes first, the shared node is found on the
// short path (depth < MaxDepth), registered, then found again on the long
// path (depth > MaxDepth) — must NOT throw.
public class B1Node { public int V { get; set; } public B1Node? Next { get; set; } }
public class B1NodeDto { public int V { get; set; } public B1NodeDto? Next { get; set; } }

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve, MaxDepth = 4)]
public partial class B1Mapper { public partial B1NodeDto Map(B1Node n); }

// ═══════════════════════════════════════════════════════════════════════════
// B2 — BeforeMap hook fires on every visit to a shared node (should fire once)
// ═══════════════════════════════════════════════════════════════════════════

public class B2Node { public int V { get; set; } public B2Node? Next { get; set; } }
public class B2NodeDto { public int V { get; set; } public B2NodeDto? Next { get; set; } }

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class B2Mapper
{
    public partial B2NodeDto Map(B2Node n);

    // Observable side-effect: counts how many times the hook fires.
    internal static int BeforeCallCount;
    [BeforeMap] private static void CountBefore(B2Node _) => ++BeforeCallCount;
}

// ═══════════════════════════════════════════════════════════════════════════
// B3 — Unknown-count array source under Preserve: two parents sharing an
//      IEnumerable<int> source → same target int[] instance (Assert.Same)
// ═══════════════════════════════════════════════════════════════════════════

public class B3Container
{
    public string Tag { get; set; } = "";
    public B3Container? Sibling { get; set; }
    public IEnumerable<int> Nums { get; set; } = new List<int>();
}

public class B3ContainerDto
{
    public string Tag { get; set; } = "";
    public B3ContainerDto? Sibling { get; set; }
    public int[] Nums { get; set; } = Array.Empty<int>();
}

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class B3Mapper { public partial B3ContainerDto Map(B3Container c); }

// Depth guard still fires for a genuinely-deep acyclic chain through Preserve.
public class B3DeepNode { public int V { get; set; } public B3DeepNode? Next { get; set; } }
public class B3DeepNodeDto { public int V { get; set; } public B3DeepNodeDto? Next { get; set; } }

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve, MaxDepth = 4)]
public partial class B3DeepMapper { public partial B3DeepNodeDto Map(B3DeepNode n); }

// ═══════════════════════════════════════════════════════════════════════════
// Tests
// ═══════════════════════════════════════════════════════════════════════════

public class PreserveGraphEdgeCasesRuntimeTests
{
    // ── B1 ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// A shared node that is first reached via a short path (depth 0, the entry point) and
    /// then reachable again via a path that reaches exactly MaxDepth must NOT throw — the
    /// identity map check must return the cached instance BEFORE the depth guard fires.
    ///
    /// MaxDepth = 4. The public Map(root) registers root at depth 0 (no depth guard for public).
    /// The synthesized helper is called with depth values 0, 1, 2, 3, 4 for the chain.
    /// At depth 4 it tries to map root again → bug: depth guard fires before TryGetReference.
    ///   root → n1 → n2 → n3 → n4 → root  (root encountered at depth 4 by the synthesized method)
    /// </summary>
    [Fact]
    public void B1_Shared_node_reachable_via_long_path_does_not_throw_when_cached()
    {
        // MaxDepth = 4. The synthesized helper receives depth 0,1,2,3,4 along the chain.
        // At depth 4 (>= MaxDepth) the bug fires BEFORE TryGetReference → throws.
        // With the fix TryGetReference fires first → root found cached → returns.
        var root = new B1Node { V = 0 };
        var n1   = new B1Node { V = 1 };
        var n2   = new B1Node { V = 2 };
        var n3   = new B1Node { V = 3 };
        var n4   = new B1Node { V = 4 };
        root.Next = n1;
        n1.Next   = n2;
        n2.Next   = n3;
        n3.Next   = n4;
        n4.Next   = root;   // back-edge: root encountered when synthesized depth = 4

        // With the bug: depth guard at depth=4 fires BEFORE TryGetReference → DwarfMappingDepthException.
        // With the fix: TryGetReference fires first → root cached → returns immediately.
        var dto = new B1Mapper().Map(root);

        Assert.NotNull(dto);
        Assert.Equal(0, dto.V);
        // The cycle must be closed: root is the same target instance at the end of the chain.
        Assert.Same(dto, dto.Next!.Next!.Next!.Next!.Next);
    }

    /// <summary>
    /// A genuinely-deep ACYCLIC chain (not in the identity map) must still throw
    /// DwarfMappingDepthException even with Preserve mode active.
    /// </summary>
    [Fact]
    public void B1_Genuinely_deep_acyclic_chain_still_throws_depth_exception()
    {
        // MaxDepth = 4. Build a chain of 10 distinct nodes (no shared node, no cycle).
        var head = new B1Node { V = 0 };
        var curr = head;
        for (var i = 1; i <= 9; i++) { var n = new B1Node { V = i }; curr.Next = n; curr = n; }

        var ex = Record.Exception(() => new B1Mapper().Map(head));
        Assert.IsType<DwarfMappingDepthException>(ex);
    }

    // ── B2 ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// A shared source node (reached from two distinct paths) must fire its BeforeMap hook
    /// exactly ONCE — on the first (construct) path — not on the cache-return path.
    /// </summary>
    [Fact]
    public void B2_BeforeMap_fires_once_for_shared_node_not_per_inbound_edge()
    {
        // Build a 2-node cycle: a.Next = b, b.Next = a.
        // From the entry point (a), the map logic will:
        //   Map(a) → cache-miss → before-hook fires → construct → Set → Map(b) → Map(a) → cache-HIT
        // With the fix: cache-hit returns immediately without re-running the hook.
        // With the bug: the hook fires for every call to Map(a), including the cache-hit.
        B2Mapper.BeforeCallCount = 0;

        var a = new B2Node { V = 1 };
        var b = new B2Node { V = 2 };
        a.Next = b;
        b.Next = a;  // cycle: a appears twice (once as entry, once as back-edge from b)

        var dto = new B2Mapper().Map(a);

        Assert.NotNull(dto);
        Assert.Equal(1, dto.V);
        // The [BeforeMap] hook is declared on the PUBLIC mapper class and only fires for the
        // entry-point call (Map(a)). Node b is mapped via the synthesized private method which
        // has no hooks. When the cycle brings us back to 'a' via the synthesized method,
        // TryGetReference returns the cached instance before any hook could fire.
        // Total count = 1: the hook fires once for 'a' (the entry), never re-fires for cache hits.
        Assert.Equal(1, B2Mapper.BeforeCallCount);
    }

    // ── B3 ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two parents sharing the SAME IEnumerable&lt;int&gt; source (unknown-count)
    /// must produce ONE target int[] instance (Assert.Same).
    /// </summary>
    [Fact]
    public void B3_Two_parents_sharing_unknown_count_array_produce_one_target_array()
    {
        var sharedNums = new List<int> { 10, 20, 30 }.AsEnumerable(); // IEnumerable<int>, unknown count
        var a = new B3Container { Tag = "A", Nums = sharedNums };
        var b = new B3Container { Tag = "B", Nums = sharedNums };
        a.Sibling = b;

        var dtoA = new B3Mapper().Map(a);
        var dtoB = dtoA.Sibling!;

        Assert.Same(dtoA.Nums, dtoB.Nums);
        Assert.Equal(new[] { 10, 20, 30 }, dtoA.Nums);
        Assert.Equal("A", dtoA.Tag);
        Assert.Equal("B", dtoB.Tag);
    }

    /// <summary>
    /// Genuinely deep acyclic chain under Preserve still throws DwarfMappingDepthException.
    /// (Regression guard: the B1 fix for shared-node-at-depth-boundary must not break this.)
    /// </summary>
    [Fact]
    public void B3_Deep_acyclic_still_throws_with_Preserve()
    {
        var head = new B3DeepNode { V = 0 };
        var curr = head;
        for (var i = 1; i <= 9; i++) { var n = new B3DeepNode { V = i }; curr.Next = n; curr = n; }

        var ex = Record.Exception(() => new B3DeepMapper().Map(head));
        Assert.IsType<DwarfMappingDepthException>(ex);
    }
}
