// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

// ─── Golden 1: Dictionary with enum keys ─────────────────────────────────────

public enum GoldKeyEnum { Alpha = 1, Beta = 2, Gamma = 3 }
public enum GoldKeyEnumDto { Alpha = 1, Beta = 2, Gamma = 3 }

public class GoldEnumKeySrc { public Dictionary<GoldKeyEnum, string> Map { get; set; } = new(); }
public class GoldEnumKeyDst { public Dictionary<GoldKeyEnumDto, string> Map { get; set; } = new(); }

[DwarfMapper]
public partial class GoldEnumKeyMapper { public partial GoldEnumKeyDst Map(GoldEnumKeySrc s); }

public class GoldenDictEnumKeyTests
{
    [Fact]
    public void Dictionary_with_enum_key_roundtrips()
    {
        var src = new GoldEnumKeySrc
        {
            Map = new Dictionary<GoldKeyEnum, string>
            {
                [GoldKeyEnum.Alpha] = "a",
                [GoldKeyEnum.Beta]  = "b",
                [GoldKeyEnum.Gamma] = "c",
            }
        };
        var dst = new GoldEnumKeyMapper().Map(src);
        Assert.Equal(3, dst.Map.Count);
        Assert.Equal("a", dst.Map[GoldKeyEnumDto.Alpha]);
        Assert.Equal("b", dst.Map[GoldKeyEnumDto.Beta]);
        Assert.Equal("c", dst.Map[GoldKeyEnumDto.Gamma]);
    }

    [Fact]
    public void Dictionary_with_enum_key_null_source_yields_empty()
    {
        var src = new GoldEnumKeySrc { Map = null! };
        var dst = new GoldEnumKeyMapper().Map(src);
        Assert.NotNull(dst.Map);
        Assert.Empty(dst.Map);
    }
}

// ─── Golden 2: Nested List<List<RecordDto>> ───────────────────────────────────

public class GoldItemSrc { public string Name { get; set; } = ""; public int Count { get; set; } }
public record GoldItemDst(string Name, int Count);

public class GoldGroupSrc { public List<GoldItemSrc> Items { get; set; } = new(); }
public record GoldGroupDst(List<GoldItemDst> Items);

public class GoldSheetSrc { public List<GoldGroupSrc> Groups { get; set; } = new(); }
public class GoldSheetDst { public List<GoldGroupDst> Groups { get; set; } = new(); }

[DwarfMapper]
public partial class GoldSheetMapper { public partial GoldSheetDst Map(GoldSheetSrc s); }

public class GoldenNestedListOfListRecordTests
{
    [Fact]
    public void Nested_list_of_list_of_record_roundtrips()
    {
        var src = new GoldSheetSrc
        {
            Groups = new List<GoldGroupSrc>
            {
                new()
                {
                    Items = new List<GoldItemSrc>
                    {
                        new() { Name = "Axe",   Count = 3 },
                        new() { Name = "Sword",  Count = 1 },
                    }
                },
                new()
                {
                    Items = new List<GoldItemSrc>
                    {
                        new() { Name = "Shield", Count = 2 },
                    }
                },
            }
        };
        var dst = new GoldSheetMapper().Map(src);
        Assert.Equal(2, dst.Groups.Count);
        Assert.Equal(2, dst.Groups[0].Items.Count);
        Assert.Equal("Axe",    dst.Groups[0].Items[0].Name);
        Assert.Equal(3,        dst.Groups[0].Items[0].Count);
        Assert.Equal("Sword",  dst.Groups[0].Items[1].Name);
        Assert.Single(dst.Groups[1].Items);
        Assert.Equal("Shield", dst.Groups[1].Items[0].Name);
    }

    [Fact]
    public void Nested_list_of_list_null_group_items_yields_empty()
    {
        var src = new GoldSheetSrc
        {
            Groups = new List<GoldGroupSrc>
            {
                new() { Items = null! }
            }
        };
        var dst = new GoldSheetMapper().Map(src);
        Assert.Single(dst.Groups);
        // Items should be a non-null, empty list (AsEmpty is default)
        Assert.NotNull(dst.Groups[0].Items);
        Assert.Empty(dst.Groups[0].Items);
    }

    [Fact]
    public void Nested_list_of_list_empty_source_roundtrips()
    {
        var src = new GoldSheetSrc { Groups = new List<GoldGroupSrc>() };
        var dst = new GoldSheetMapper().Map(src);
        Assert.Empty(dst.Groups);
    }
}

// ─── Golden 3: Shared list (topology) ─────────────────────────────────────────
// Plan 19 C2b: under ReferenceHandling=Preserve, collection instances participate in the
// reference-identity graph. Synthesized collection helpers (__DwarfMapColl_*_p) register
// in DwarfRefContext BEFORE filling, so a shared List<T> maps to ONE shared target list.

public class GoldListOwnerSrc
{
    public string Tag { get; set; } = "";
    public GoldListOwnerSrc? Sibling { get; set; }
    public List<int> Shared { get; set; } = new();
}

public class GoldListOwnerDst
{
    public string Tag { get; set; } = "";
    public GoldListOwnerDst? Sibling { get; set; }
    public List<int> Shared { get; set; } = new();
}

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class GoldSharedListMapper { public partial GoldListOwnerDst Map(GoldListOwnerSrc s); }

public class GoldenSharedListTests
{
    // Plan 19 C2b: collection identity IS preserved under ReferenceHandling=Preserve.
    // Two source nodes sharing the same List<int> produce two target nodes sharing the SAME
    // target List<int> instance.
    [Fact]
    public void Shared_list_same_target_instance_under_Preserve()
    {
        var shared = new List<int> { 10, 20, 30 };
        var a = new GoldListOwnerSrc { Tag = "A", Shared = shared };
        var b = new GoldListOwnerSrc { Tag = "B", Shared = shared };
        a.Sibling = b;

        var dstA = new GoldSharedListMapper().Map(a);
        var dstB = dstA.Sibling!;

        // Content is preserved.
        Assert.Equal(new[] { 10, 20, 30 }, dstA.Shared);
        Assert.Equal("A", dstA.Tag);
        Assert.Equal("B", dstB.Tag);

        // Identity IS preserved — same source list → same target list.
        Assert.Same(dstA.Shared, dstB.Shared);
    }

    [Fact]
    public void Non_shared_lists_are_different_instances()
    {
        var a = new GoldListOwnerSrc { Tag = "A", Shared = new List<int> { 1, 2 } };
        var b = new GoldListOwnerSrc { Tag = "B", Shared = new List<int> { 3, 4 } };
        a.Sibling = b;

        var dstA = new GoldSharedListMapper().Map(a);
        var dstB = dstA.Sibling!;

        // Distinct source lists → distinct target lists.
        Assert.NotSame(dstA.Shared, dstB.Shared);
        Assert.Equal(new[] { 1, 2 }, dstA.Shared);
        Assert.Equal(new[] { 3, 4 }, dstB.Shared);
    }
}

// ─── Golden 4: MaxDepth boundary chain ───────────────────────────────────────
// Already covered in DepthSafetyRuntimeTests — this extends with a Preserve-mode chain.

public class GoldChainNode    { public int V { get; set; } public GoldChainNode? Next { get; set; } }
public class GoldChainNodeDto { public int V { get; set; } public GoldChainNodeDto? Next { get; set; } }

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve, MaxDepth = 10)]
public partial class GoldChainMapper { public partial GoldChainNodeDto Map(GoldChainNode n); }

public class GoldenMaxDepthBoundaryTests
{
    private static GoldChainNode BuildChain(int n)
    {
        GoldChainNode? head = null;
        for (var i = 0; i < n; i++)
            head = new GoldChainNode { V = i, Next = head };
        return head!;
    }

    [Fact]
    public void Chain_exactly_at_MaxDepth_maps_ok_with_Preserve()
    {
        var dst = new GoldChainMapper().Map(BuildChain(8));
        Assert.Equal(7, dst.V); // head is the last-pushed node with V=n-1
    }

    [Fact]
    public void Chain_beyond_MaxDepth_throws_depth_exception_with_Preserve()
    {
        var ex = Record.Exception(() => new GoldChainMapper().Map(BuildChain(200)));
        Assert.NotNull(ex);
        Assert.IsType<DwarfMappingDepthException>(ex);
    }
}

// ─── Golden 5: Empty and null collection edge cases across all shapes ─────────

public class GoldAllCollSrc
{
    public List<int>?           ListProp        { get; set; }
    public int[]?               ArrayProp       { get; set; }
    public Dictionary<string,int>? DictProp     { get; set; }
}

public class GoldAllCollDst
{
    public List<int>?           ListProp        { get; set; }
    public int[]?               ArrayProp       { get; set; }
    public Dictionary<string,int>? DictProp     { get; set; }
}

[DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
public partial class GoldNullCollMapper { public partial GoldAllCollDst Map(GoldAllCollSrc s); }

[DwarfMapper]  // default AsEmpty
public partial class GoldEmptyCollMapper { public partial GoldAllCollDst Map(GoldAllCollSrc s); }

public class GoldenNullEmptyCollectionTests
{
    [Fact]
    public void AsNull_mode_passes_null_through_all_shapes()
    {
        var src = new GoldAllCollSrc();  // all null
        var dst = new GoldNullCollMapper().Map(src);
        Assert.Null(dst.ListProp);
        Assert.Null(dst.ArrayProp);
        Assert.Null(dst.DictProp);
    }

    [Fact]
    public void AsEmpty_mode_converts_null_to_empty_all_shapes()
    {
        var src = new GoldAllCollSrc();  // all null
        var dst = new GoldEmptyCollMapper().Map(src);
        Assert.NotNull(dst.ListProp);
        Assert.Empty(dst.ListProp!);
        Assert.NotNull(dst.ArrayProp);
        Assert.Empty(dst.ArrayProp!);
        Assert.NotNull(dst.DictProp);
        Assert.Empty(dst.DictProp!);
    }

    [Fact]
    public void AsNull_mode_preserves_populated_collections()
    {
        var src = new GoldAllCollSrc
        {
            ListProp  = new List<int> { 1, 2 },
            ArrayProp = new[] { 3, 4 },
            DictProp  = new Dictionary<string, int> { ["k"] = 99 },
        };
        var dst = new GoldNullCollMapper().Map(src);
        Assert.Equal(new[] { 1, 2 }, dst.ListProp);
        Assert.Equal(new[] { 3, 4 }, dst.ArrayProp);
        Assert.Equal(99, dst.DictProp!["k"]);
    }
}
