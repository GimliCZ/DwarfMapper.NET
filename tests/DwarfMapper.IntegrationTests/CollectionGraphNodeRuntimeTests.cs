// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

// Plan 19 C2b: collections-as-graph-nodes under ReferenceHandling=Preserve
// TDD: written FIRST (RED) — encode the required correct behaviour.

// ─── Shared List<int> between two parents ────────────────────────────────────

public class CgnListOwnerSrc
{
    public string Tag { get; set; } = "";
    public CgnListOwnerSrc? Sibling { get; set; }
    public List<int> Nums { get; set; } = new();
}

public class CgnListOwnerDst
{
    public string Tag { get; set; } = "";
    public CgnListOwnerDst? Sibling { get; set; }
    public List<int> Nums { get; set; } = new();
}

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class CgnListMapper
{
    public partial CgnListOwnerDst Map(CgnListOwnerSrc s);
}

// ─── Cycle through a collection ──────────────────────────────────────────────

public class CgnTreeNode
{
    public string Name { get; set; } = "";
    public List<CgnTreeNode> Children { get; set; } = new();
}

public class CgnTreeNodeDto
{
    public string Name { get; set; } = "";
    public List<CgnTreeNodeDto> Children { get; set; } = new();
}

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class CgnTreeMapper
{
    public partial CgnTreeNodeDto Map(CgnTreeNode n);
}

// ─── Shared Dictionary<string,int> between two parents ───────────────────────

public class CgnDictOwnerSrc
{
    public string Tag { get; set; } = "";
    public CgnDictOwnerSrc? Sibling { get; set; }
    public Dictionary<string, int> Data { get; set; } = new();
}

public class CgnDictOwnerDst
{
    public string Tag { get; set; } = "";
    public CgnDictOwnerDst? Sibling { get; set; }
    public Dictionary<string, int> Data { get; set; } = new();
}

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class CgnDictMapper
{
    public partial CgnDictOwnerDst Map(CgnDictOwnerSrc s);
}

// ─── Nested object inside a shared list preserves element identity ────────────

public class CgnChildSrc
{
    public string Name { get; set; } = "";
    public CgnChildSrc? Ref { get; set; }
}

public class CgnParentSrc
{
    public string Tag { get; set; } = "";
    public CgnParentSrc? Sibling { get; set; }
    public List<CgnChildSrc> Kids { get; set; } = new();
}

public class CgnChildDst
{
    public string Name { get; set; } = "";
    public CgnChildDst? Ref { get; set; }
}

public class CgnParentDst
{
    public string Tag { get; set; } = "";
    public CgnParentDst? Sibling { get; set; }
    public List<CgnChildDst> Kids { get; set; } = new();
}

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class CgnParentMapper
{
    public partial CgnParentDst Map(CgnParentSrc s);
}

// ─── Tests ───────────────────────────────────────────────────────────────────

public class CollectionGraphNodeRuntimeTests
{
    [Fact]
    public void Shared_list_produces_same_target_list_instance_under_Preserve()
    {
        var shared = new List<int> { 10, 20, 30 };
        var a = new CgnListOwnerSrc { Tag = "A", Nums = shared };
        var b = new CgnListOwnerSrc { Tag = "B", Nums = shared };
        a.Sibling = b;

        var dstA = new CgnListMapper().Map(a);
        var dstB = dstA.Sibling!;

        Assert.Same(dstA.Nums, dstB.Nums);
        Assert.Equal(new[] { 10, 20, 30 }, dstA.Nums);
        Assert.Equal("A", dstA.Tag);
        Assert.Equal("B", dstB.Tag);
    }

    [Fact]
    public void Cycle_through_collection_reconstructs_without_depth_exception()
    {
        var parent = new CgnTreeNode { Name = "root" };
        parent.Children.Add(parent);

        var dst = new CgnTreeMapper().Map(parent);

        Assert.Equal("root", dst.Name);
        Assert.Single(dst.Children);
        Assert.Same(dst, dst.Children[0]);
    }

    [Fact]
    public void Deep_linear_tree_through_collection_eventually_depth_caps()
    {
        var nodes = new List<CgnTreeNode>();
        for (var i = 0; i < 200; i++)
            nodes.Add(new CgnTreeNode { Name = $"n{i}" });
        for (var i = 0; i < 199; i++)
            nodes[i].Children.Add(nodes[i + 1]);

        var ex = Record.Exception(() => new CgnTreeMapper().Map(nodes[0]));
        Assert.NotNull(ex);
        Assert.IsType<DwarfMappingDepthException>(ex);
    }

    [Fact]
    public void Shared_dictionary_produces_same_target_dict_instance_under_Preserve()
    {
        var shared = new Dictionary<string, int> { ["x"] = 1, ["y"] = 2 };
        var a = new CgnDictOwnerSrc { Tag = "A", Data = shared };
        var b = new CgnDictOwnerSrc { Tag = "B", Data = shared };
        a.Sibling = b;

        var dstA = new CgnDictMapper().Map(a);
        var dstB = dstA.Sibling!;

        Assert.Same(dstA.Data, dstB.Data);
        Assert.Equal(1, dstA.Data["x"]);
        Assert.Equal(2, dstA.Data["y"]);
    }

    [Fact]
    public void Nested_object_inside_shared_list_preserves_element_identity()
    {
        var sharedChild = new CgnChildSrc { Name = "shared" };
        var a = new CgnParentSrc { Tag = "A", Kids = new List<CgnChildSrc> { sharedChild } };
        var b = new CgnParentSrc { Tag = "B", Kids = new List<CgnChildSrc> { sharedChild } };
        a.Sibling = b;

        var dstA = new CgnParentMapper().Map(a);
        var dstB = dstA.Sibling!;

        Assert.Single(dstA.Kids);
        Assert.Single(dstB.Kids);
        Assert.Same(dstA.Kids[0], dstB.Kids[0]);
        Assert.Equal("shared", dstA.Kids[0].Name);
    }

    [Fact]
    public void Distinct_source_lists_produce_distinct_target_list_instances()
    {
        var a = new CgnListOwnerSrc { Tag = "A", Nums = new List<int> { 1, 2 } };
        var b = new CgnListOwnerSrc { Tag = "B", Nums = new List<int> { 3, 4 } };
        a.Sibling = b;

        var dstA = new CgnListMapper().Map(a);
        var dstB = dstA.Sibling!;

        Assert.NotSame(dstA.Nums, dstB.Nums);
        Assert.Equal(new[] { 1, 2 }, dstA.Nums);
        Assert.Equal(new[] { 3, 4 }, dstB.Nums);
    }
}

// ─── None-mode regression: collections are independent copies, no ctx overhead ──

public class NoneListOwnerSrc
{
    public string Tag { get; set; } = "";
    public List<int> Nums { get; set; } = new();
}

public class NoneListOwnerDst
{
    public string Tag { get; set; } = "";
    public List<int> Nums { get; set; } = new();
}

// Default ReferenceHandling = None — no DwarfRefContext, no graph tracking.
[DwarfMapper]
public partial class NoneListMapper
{
    public partial NoneListOwnerDst Map(NoneListOwnerSrc s);
}

public class NoneModeCollectionRegressionTests
{
    [Fact]
    public void None_mode_shared_list_produces_two_independent_copies()
    {
        // Under None mode a shared source list MUST produce independent target lists
        // (no identity tracking) — this is the expected behavior for None mode.
        var shared = new List<int> { 10, 20, 30 };
        var src1 = new NoneListOwnerSrc { Tag = "A", Nums = shared };
        var src2 = new NoneListOwnerSrc { Tag = "B", Nums = shared };

        var dst1 = new NoneListMapper().Map(src1);
        var dst2 = new NoneListMapper().Map(src2);

        // Content must be correct.
        Assert.Equal(new[] { 10, 20, 30 }, dst1.Nums);
        Assert.Equal(new[] { 10, 20, 30 }, dst2.Nums);
        // Identity is NOT preserved (None mode — expected independent copies).
        Assert.NotSame(dst1.Nums, dst2.Nums);
        Assert.Equal("A", dst1.Tag);
        Assert.Equal("B", dst2.Tag);
    }

    [Fact]
    public void None_mode_collection_maps_correctly()
    {
        var src = new NoneListOwnerSrc { Tag = "X", Nums = new List<int> { 5, 6, 7 } };
        var dst = new NoneListMapper().Map(src);
        Assert.Equal("X", dst.Tag);
        Assert.Equal(new[] { 5, 6, 7 }, dst.Nums);
        Assert.NotSame(src.Nums, dst.Nums); // always a new list, even in None mode
    }
}
