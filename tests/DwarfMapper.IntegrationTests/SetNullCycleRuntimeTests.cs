// SPDX-License-Identifier: GPL-2.0-only
using System;
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// ── Types for OnCycle = SetNull runtime tests ────────────────────────────────
// SetNull (≡ System.Text.Json IgnoreCycles): a re-entrant back-edge to a node already on the
// active mapping stack is set to null, yielding a finite acyclic projection. Shared-but-acyclic
// nodes (diamonds) are re-mapped (duplicated), not nulled — only true ancestor cycles break.

// Self-loop: n.Self = n
public class SnSelfNode    { public int V { get; set; } public SnSelfNode? Self { get; set; } }
public class SnSelfNodeDto { public int V { get; set; } public SnSelfNodeDto? Self { get; set; } }

[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
public partial class SnSelfMapper { public partial SnSelfNodeDto Map(SnSelfNode n); }

// 2-node cycle: a.Next=b, b.Next=a
public class SnTwo    { public int V { get; set; } public SnTwo? Next { get; set; } }
public class SnTwoDto { public int V { get; set; } public SnTwoDto? Next { get; set; } }

[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
public partial class SnTwoMapper { public partial SnTwoDto Map(SnTwo n); }

// 3-node cycle: a→b→c→a
public class SnTri    { public int V { get; set; } public SnTri? Next { get; set; } }
public class SnTriDto { public int V { get; set; } public SnTriDto? Next { get; set; } }

[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
public partial class SnTriMapper { public partial SnTriDto Map(SnTri n); }

// Cyclic diamond: root.Left and root.Right share the same child; child.Root = root (cycle).
public class SnDiaChild { public int V { get; set; } public SnDiaRoot? Root { get; set; } }
public class SnDiaRoot  { public SnDiaChild? Left { get; set; } public SnDiaChild? Right { get; set; } }
public class SnDiaChildDto { public int V { get; set; } public SnDiaRootDto? Root { get; set; } }
public class SnDiaRootDto  { public SnDiaChildDto? Left { get; set; } public SnDiaChildDto? Right { get; set; } }

[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
public partial class SnDiaMapper { public partial SnDiaRootDto Map(SnDiaRoot r); }

// Acyclic shared (NOT a cycle): two parents reference the same leaf, leaf has no back-edge.
public class SnLeaf      { public int V { get; set; } }
public class SnParents   { public SnLeaf? A { get; set; } public SnLeaf? B { get; set; } }
public class SnLeafDto   { public int V { get; set; } }
public class SnParentsDto { public SnLeafDto? A { get; set; } public SnLeafDto? B { get; set; } }

[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
public partial class SnSharedMapper { public partial SnParentsDto Map(SnParents p); }

// Deep ACYCLIC chain with a small MaxDepth → depth backstop still throws under SetNull.
public class SnChain    { public int V { get; set; } public SnChain? Next { get; set; } }
public class SnChainDto { public int V { get; set; } public SnChainDto? Next { get; set; } }

[DwarfMapper(OnCycle = OnCycleStrategy.SetNull, MaxDepth = 4)]
public partial class SnChainMapper { public partial SnChainDto Map(SnChain n); }

public class SetNullCycleRuntimeTests
{
    // ── Self-loop → back-edge nulled ────────────────────────────────────────────
    [Fact]
    public void SelfLoop_back_edge_is_null()
    {
        var n = new SnSelfNode { V = 42 };
        n.Self = n;
        var t = new SnSelfMapper().Map(n);
        Assert.Equal(42, t.V);
        Assert.Null(t.Self); // re-entrant back-edge to the node on the stack → null
    }

    // ── 2-node cycle → finite, deepest back-edge nulled ─────────────────────────
    [Fact]
    public void TwoNodeCycle_terminates_with_null_back_edge()
    {
        var a = new SnTwo { V = 1 };
        var b = new SnTwo { V = 2 };
        a.Next = b;
        b.Next = a;

        var ta = new SnTwoMapper().Map(a);
        Assert.Equal(1, ta.V);
        Assert.NotNull(ta.Next);
        Assert.Equal(2, ta.Next!.V);
        // b.Next points back to a, which is on the stack → nulled.
        Assert.Null(ta.Next.Next);
    }

    // ── 3-node cycle a→b→c→a → c.Next (back to a) nulled ────────────────────────
    [Fact]
    public void ThreeNodeCycle_terminates_with_null_back_edge()
    {
        var a = new SnTri { V = 1 };
        var b = new SnTri { V = 2 };
        var c = new SnTri { V = 3 };
        a.Next = b; b.Next = c; c.Next = a;

        var ta = new SnTriMapper().Map(a);
        Assert.Equal(1, ta.V);
        Assert.Equal(2, ta.Next!.V);
        Assert.Equal(3, ta.Next!.Next!.V);
        Assert.Null(ta.Next.Next.Next); // c→a is a back-edge → null
    }

    // ── Cyclic diamond → children duplicated, back-edges nulled ─────────────────
    [Fact]
    public void CyclicDiamond_children_duplicated_back_edges_null()
    {
        var root = new SnDiaRoot();
        var child = new SnDiaChild { V = 99, Root = root };
        root.Left = child;
        root.Right = child; // SAME child shared

        var troot = new SnDiaMapper().Map(root);
        Assert.NotNull(troot.Left);
        Assert.NotNull(troot.Right);
        Assert.Equal(99, troot.Left!.V);
        Assert.Equal(99, troot.Right!.V);
        // SetNull tracks the SOURCE stack, not identity → the shared child is mapped twice.
        Assert.False(ReferenceEquals(troot.Left, troot.Right), "SetNull duplicates shared nodes");
        // child.Root points back to root which is on the stack while mapping each child → null.
        Assert.Null(troot.Left.Root);
        Assert.Null(troot.Right.Root);
    }

    // ── Acyclic shared leaf → mapped on both paths (not nulled) ─────────────────
    [Fact]
    public void AcyclicSharedLeaf_mapped_on_both_paths()
    {
        var leaf = new SnLeaf { V = 7 };
        var p = new SnParents { A = leaf, B = leaf };

        var t = new SnSharedMapper().Map(p);
        Assert.NotNull(t.A);
        Assert.NotNull(t.B);
        Assert.Equal(7, t.A!.V);
        Assert.Equal(7, t.B!.V); // not a cycle → never nulled
    }

    // ── Deep acyclic chain beyond MaxDepth → depth backstop still throws ────────
    [Fact]
    public void DeepAcyclicChain_beyond_MaxDepth_throws_depth_exception()
    {
        // Build an acyclic chain longer than MaxDepth=4.
        var head = new SnChain { V = 0 };
        var cur = head;
        for (var i = 1; i < 10; i++)
        {
            cur.Next = new SnChain { V = i };
            cur = cur.Next;
        }
        Assert.Throws<DwarfMappingDepthException>(() => new SnChainMapper().Map(head));
    }

    // ── Determinism: same input twice → same shape ──────────────────────────────
    [Fact]
    public void SetNull_is_deterministic()
    {
        var a = new SnTwo { V = 1 };
        var b = new SnTwo { V = 2 };
        a.Next = b; b.Next = a;

        var t1 = new SnTwoMapper().Map(a);
        var t2 = new SnTwoMapper().Map(a);
        Assert.Equal(t1.V, t2.V);
        Assert.Equal(t1.Next!.V, t2.Next!.V);
        Assert.Null(t1.Next.Next);
        Assert.Null(t2.Next.Next);
    }
}

// ── Collection-edge "shared but acyclic" — supported (no cycle through the list) ─
// A cycle routed THROUGH a collection edge (e.g. child.Children contains the root) is a
// DEFERRED limitation: in None mode the depth counter / on-stack guard is not threaded into
// collection element mappers (they call the public entry, which allocates a fresh context),
// matching the documented None-mode "cyclic data" behaviour. SetNull breaks cycles formed by
// direct reference MEMBERS (Self/Next/Parent), which is what these tests cover. The acyclic
// collection case below confirms collections still map correctly under SetNull.
public class SnList    { public int V { get; set; } public System.Collections.Generic.List<SnLeaf>? Items { get; set; } }
public class SnListDto { public int V { get; set; } public System.Collections.Generic.List<SnLeafDto>? Items { get; set; } }

[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
public partial class SnListMapper { public partial SnListDto Map(SnList s); }

public class SetNullCollectionRuntimeTests
{
    [Fact]
    public void AcyclicCollection_maps_correctly_under_SetNull()
    {
        var s = new SnList { V = 1, Items = new() { new SnLeaf { V = 7 }, new SnLeaf { V = 8 } } };
        var t = new SnListMapper().Map(s);
        Assert.Equal(1, t.V);
        Assert.NotNull(t.Items);
        Assert.Equal(2, t.Items!.Count);
        Assert.Equal(7, t.Items[0].V);
        Assert.Equal(8, t.Items[1].V);
    }
}
