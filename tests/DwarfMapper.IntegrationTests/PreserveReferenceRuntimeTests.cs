// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// ── Types for Preserve runtime tests ─────────────────────────────────────────

// Self-loop: n.Self = n
public class SelfLoopNode    { public int V { get; set; } public SelfLoopNode? Self { get; set; } }
public class SelfLoopNodeDto { public int V { get; set; } public SelfLoopNodeDto? Self { get; set; } }

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class SelfLoopMapper { public partial SelfLoopNodeDto Map(SelfLoopNode n); }

// 2-node cycle: a.Next=b, b.Next=a
public class TwoNode    { public int V { get; set; } public TwoNode? Next { get; set; } }
public class TwoNodeDto { public int V { get; set; } public TwoNodeDto? Next { get; set; } }

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class TwoCycleMapper { public partial TwoNodeDto Map(TwoNode n); }

// Diamond / shared node: root.Left and root.Right share same child.
// DiamondChild has a back-reference to root (making it recursion-capable),
// so the identity map is active and the diamond is reconstructed faithfully.
public class DiamondChild    { public int V { get; set; } public DiamondRoot? Root { get; set; } }
public class DiamondRoot     { public DiamondChild? Left { get; set; } public DiamondChild? Right { get; set; } }
public class DiamondChildDto { public int V { get; set; } public DiamondRootDto? Root { get; set; } }
public class DiamondRootDto  { public DiamondChildDto? Left { get; set; } public DiamondChildDto? Right { get; set; } }

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class DiamondMapper { public partial DiamondRootDto Map(DiamondRoot r); }

// Owner graph: A→B, B⇄C, C⇄D, B⇄D
public class OwnerA { public int Id { get; set; } public OwnerB? B { get; set; } }
public class OwnerB { public int Id { get; set; } public OwnerC? C { get; set; } public OwnerD? D { get; set; } }
public class OwnerC { public int Id { get; set; } public OwnerB? B { get; set; } public OwnerD? D { get; set; } }
public class OwnerD { public int Id { get; set; } public OwnerB? B { get; set; } public OwnerC? C { get; set; } }

public class OwnerADto { public int Id { get; set; } public OwnerBDto? B { get; set; } }
public class OwnerBDto { public int Id { get; set; } public OwnerCDto? C { get; set; } public OwnerDDto? D { get; set; } }
public class OwnerCDto { public int Id { get; set; } public OwnerBDto? B { get; set; } public OwnerDDto? D { get; set; } }
public class OwnerDDto { public int Id { get; set; } public OwnerBDto? B { get; set; } public OwnerCDto? C { get; set; } }

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve, MaxDepth = 64)]
public partial class OwnerGraphMapper { public partial OwnerADto Map(OwnerA a); }

// Records NOT merged: two distinct-but-equal record source nodes → two distinct target instances
public record RecordSrc(int X, int Y);
public record RecordDst(int X, int Y);

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class RecordContainerMapper { public partial RecordContainerDto Map(RecordContainer r); }

public class RecordContainer    { public RecordSrc? A { get; set; } public RecordSrc? B { get; set; } }
public class RecordContainerDto { public RecordDst? A { get; set; } public RecordDst? B { get; set; } }

// Acyclic under Preserve: normal acyclic graph still maps correctly
public class AcyclicSrc    { public int V { get; set; } public string Label { get; set; } = ""; }
public class AcyclicDst    { public int V { get; set; } public string Label { get; set; } = ""; }

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class AcyclicPreserveMapper { public partial AcyclicDst Map(AcyclicSrc s); }

// None mode (default): recursion-capable type still uses C1 depth-guard (not identity map)
public class NoneNode    { public int V { get; set; } public NoneNode? Next { get; set; } }
public class NoneNodeDto { public int V { get; set; } public NoneNodeDto? Next { get; set; } }

[DwarfMapper(MaxDepth = 4)]
public partial class NoneMapper { public partial NoneNodeDto Map(NoneNode n); }

public class PreserveReferenceRuntimeTests
{
    // ── Self-loop ─────────────────────────────────────────────────────────────────
    [Fact]
    public void SelfLoop_target_Self_ReferenceEquals_target_itself()
    {
        var n = new SelfLoopNode { V = 42 };
        n.Self = n; // self-loop
        var t = new SelfLoopMapper().Map(n);
        Assert.Equal(42, t.V);
        Assert.True(ReferenceEquals(t, t.Self), "t.Self must ReferenceEqual t");
    }

    // ── 2-node cycle ──────────────────────────────────────────────────────────────
    [Fact]
    public void TwoNodeCycle_back_edge_closed()
    {
        var a = new TwoNode { V = 1 };
        var b = new TwoNode { V = 2 };
        a.Next = b;
        b.Next = a;

        var ta = new TwoCycleMapper().Map(a);
        Assert.Equal(1, ta.V);
        Assert.NotNull(ta.Next);
        Assert.Equal(2, ta.Next!.V);
        // Back edge: ta.Next.Next must ReferenceEqual ta
        Assert.True(ReferenceEquals(ta, ta.Next.Next), "ta.Next.Next must ReferenceEqual ta");
    }

    // ── Diamond / shared node ─────────────────────────────────────────────────────
    // DiamondChild has a back-reference (Root) making it recursion-capable, so the identity
    // map is active and the diamond topology is reconstructed faithfully.
    [Fact]
    public void Diamond_shared_child_one_target_instance()
    {
        var root  = new DiamondRoot();
        var child = new DiamondChild { V = 99, Root = root }; // back-reference to root
        root.Left  = child;
        root.Right = child; // SAME child instance shared

        var troot = new DiamondMapper().Map(root);
        Assert.NotNull(troot.Left);
        Assert.NotNull(troot.Right);
        // Both must reference the SAME target child instance
        Assert.True(ReferenceEquals(troot.Left, troot.Right),
            "Diamond: troot.Left and troot.Right must ReferenceEqual the same target child");
        Assert.Equal(99, troot.Left!.V);
        // Root back-references should also be closed
        Assert.True(ReferenceEquals(troot, troot.Left.Root),
            "troot.Left.Root must ReferenceEqual troot");
    }

    // ── Owner graph A→B, B⇄C, C⇄D, B⇄D ─────────────────────────────────────────
    [Fact]
    public void OwnerGraph_topology_preserved()
    {
        // Build the source graph: A→B, B⇄C, C⇄D, B⇄D
        var d = new OwnerD { Id = 4 };
        var c = new OwnerC { Id = 3, D = d };
        var b = new OwnerB { Id = 2, C = c, D = d };
        var a = new OwnerA { Id = 1, B = b };
        // Set back-edges
        c.B = b;    // B⇄C
        d.B = b;    // B⇄D
        d.C = c;    // C⇄D

        var ta = new OwnerGraphMapper().Map(a);

        Assert.NotNull(ta.B);
        var tb = ta.B!;
        Assert.NotNull(tb.C);
        Assert.NotNull(tb.D);
        var tc = tb.C!;
        var td = tb.D!;

        // B⇄C: tc.B must ReferenceEqual tb
        Assert.True(ReferenceEquals(tb, tc.B), "tc.B must ReferenceEqual tb");

        // B⇄D: td.B must ReferenceEqual tb
        Assert.True(ReferenceEquals(tb, td.B), "td.B must ReferenceEqual tb");

        // C⇄D: td.C must ReferenceEqual tc AND tc.D must ReferenceEqual td
        Assert.True(ReferenceEquals(td, tc.D), "tc.D must ReferenceEqual td");
        Assert.True(ReferenceEquals(tc, td.C), "td.C must ReferenceEqual tc");

        // Values preserved
        Assert.Equal(1, ta.Id);
        Assert.Equal(2, tb.Id);
        Assert.Equal(3, tc.Id);
        Assert.Equal(4, td.Id);
    }

    // ── Records NOT merged (value equality must NOT collapse distinct instances) ──
    [Fact]
    public void Records_distinct_but_equal_not_merged()
    {
        // Two DISTINCT record instances that compare equal
        var srcA = new RecordSrc(1, 2);
        var srcB = new RecordSrc(1, 2); // same value, different reference
        Assert.NotSame(srcA, srcB); // precondition
        Assert.Equal(srcA, srcB);   // precondition: value-equal

        var container = new RecordContainer { A = srcA, B = srcB };
        var result = new RecordContainerMapper().Map(container);

        Assert.NotNull(result.A);
        Assert.NotNull(result.B);
        // Two distinct sources → two distinct target instances (NOT ReferenceEquals)
        Assert.False(ReferenceEquals(result.A, result.B),
            "Two distinct-but-equal record sources must produce two distinct target instances");
        Assert.Equal(1, result.A!.X);
        Assert.Equal(1, result.B!.X);
    }

    // ── Acyclic graph under Preserve still maps correctly ────────────────────────
    [Fact]
    public void Acyclic_Preserve_maps_correctly()
    {
        var src = new AcyclicSrc { V = 7, Label = "forge" };
        var dst = new AcyclicPreserveMapper().Map(src);
        Assert.Equal(7, dst.V);
        Assert.Equal("forge", dst.Label);
    }

    // ── None mode: cyclic data still throws DwarfMappingDepthException ────────────
    [Fact]
    public void None_mode_cyclic_data_throws_depth_exception()
    {
        var a = new NoneNode { V = 1 };
        var b = new NoneNode { V = 2 };
        a.Next = b; b.Next = a;
        var ex = Record.Exception(() => new NoneMapper().Map(a));
        Assert.IsType<DwarfMappingDepthException>(ex);
    }

    // ── None mode: no identity map — simple chain within cap still maps ───────────
    [Fact]
    public void None_mode_within_cap_maps_correctly()
    {
        var chain = new NoneNode { V = 1,
            Next = new NoneNode { V = 2,
                Next = new NoneNode { V = 3 } } };
        var dto = new NoneMapper().Map(chain);
        Assert.Equal(1, dto.V);
        Assert.Equal(2, dto.Next!.V);
        Assert.Equal(3, dto.Next.Next!.V);
    }
}
