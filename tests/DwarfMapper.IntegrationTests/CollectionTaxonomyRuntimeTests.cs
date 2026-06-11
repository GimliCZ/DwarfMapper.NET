// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

// ─── Record helpers ───────────────────────────────────────────────────────────

public class TaxSrc  { public string Name { get; set; } = ""; public int Age { get; set; } }
public class TaxDst  { public string Name { get; set; } = ""; public int Age { get; set; } }

// ─── Interface targets → concrete ────────────────────────────────────────────

public class TaxInterfaceSrc
{
    public List<int> Nums { get; set; } = new();
}

public class TaxIListDst
{
    public IList<int> Nums { get; set; } = new List<int>();
}

public class TaxICollectionDst
{
    public ICollection<int> Nums { get; set; } = new List<int>();
}

public class TaxIReadOnlyListDst
{
    public IReadOnlyList<int> Nums { get; set; } = new List<int>();
}

public class TaxIReadOnlyCollectionDst
{
    public IReadOnlyCollection<int> Nums { get; set; } = new List<int>();
}

public class TaxISetDst
{
    public ISet<int> Nums { get; set; } = new HashSet<int>();
}

public class TaxIReadOnlySetDst
{
    public IReadOnlySet<int> Nums { get; set; } = new HashSet<int>();
}

[DwarfMapper]
public partial class TaxInterfaceMapper
{
    public partial TaxIListDst          ToIList(TaxInterfaceSrc s);
    public partial TaxICollectionDst    ToICollection(TaxInterfaceSrc s);
    public partial TaxIReadOnlyListDst  ToIReadOnlyList(TaxInterfaceSrc s);
    public partial TaxIReadOnlyCollectionDst ToIReadOnlyCollection(TaxInterfaceSrc s);
    public partial TaxISetDst           ToISet(TaxInterfaceSrc s);
    public partial TaxIReadOnlySetDst   ToIReadOnlySet(TaxInterfaceSrc s);
}

public class InterfaceCollectionRuntimeTests
{
    private readonly TaxInterfaceMapper _m = new();
    private readonly TaxInterfaceSrc _src = new() { Nums = new List<int> { 1, 2, 3 } };

    [Fact] public void IList_target_roundtrips()         => Assert.Equal(new[] { 1,2,3 }, _m.ToIList(_src).Nums);
    [Fact] public void ICollection_target_roundtrips()   => Assert.Equal(3, _m.ToICollection(_src).Nums.Count);
    [Fact] public void IReadOnlyList_target_roundtrips() => Assert.Equal(new[] { 1,2,3 }, _m.ToIReadOnlyList(_src).Nums);
    [Fact] public void IReadOnlyCollection_roundtrips()  => Assert.Equal(3, _m.ToIReadOnlyCollection(_src).Nums.Count);
    [Fact] public void ISet_target_roundtrips()          => Assert.Equal(new HashSet<int>{1,2,3}, _m.ToISet(_src).Nums);
    [Fact] public void IReadOnlySet_roundtrips()         => Assert.Equal(new HashSet<int>{1,2,3}, _m.ToIReadOnlySet(_src).Nums);

    [Fact]
    public void IList_null_source_yields_empty()
    {
        var r = _m.ToIList(new TaxInterfaceSrc { Nums = null! });
        Assert.NotNull(r.Nums);
        Assert.Empty(r.Nums);
    }
}

// ─── Immutable collection targets ────────────────────────────────────────────

public class TaxImmutableSrc
{
    public List<int> Nums { get; set; } = new();
}

public class TaxImmutableArrayDst
{
    public ImmutableArray<int> Nums { get; set; }
}

public class TaxImmutableListDst
{
    public ImmutableList<int> Nums { get; set; } = ImmutableList<int>.Empty;
}

public class TaxIImmutableListDst
{
    public IImmutableList<int> Nums { get; set; } = ImmutableList<int>.Empty;
}

public class TaxImmutableHashSetDst
{
    public ImmutableHashSet<int> Nums { get; set; } = ImmutableHashSet<int>.Empty;
}

public class TaxIImmutableSetDst
{
    public IImmutableSet<int> Nums { get; set; } = ImmutableHashSet<int>.Empty;
}

[DwarfMapper]
public partial class TaxImmutableMapper
{
    public partial TaxImmutableArrayDst    ToImmutableArray(TaxImmutableSrc s);
    public partial TaxImmutableListDst     ToImmutableList(TaxImmutableSrc s);
    public partial TaxIImmutableListDst    ToIImmutableList(TaxImmutableSrc s);
    public partial TaxImmutableHashSetDst  ToImmutableHashSet(TaxImmutableSrc s);
    public partial TaxIImmutableSetDst     ToIImmutableSet(TaxImmutableSrc s);
}

public class ImmutableCollectionRuntimeTests
{
    private readonly TaxImmutableMapper _m = new();
    private readonly TaxImmutableSrc _src = new() { Nums = new List<int> { 10, 20, 30 } };

    [Fact] public void ImmutableArray_roundtrips()
    {
        var r = _m.ToImmutableArray(_src);
        Assert.Equal(new[] { 10, 20, 30 }, r.Nums);
    }

    [Fact] public void ImmutableList_roundtrips()
    {
        var r = _m.ToImmutableList(_src);
        Assert.Equal(new[] { 10, 20, 30 }, r.Nums);
    }

    [Fact] public void IImmutableList_roundtrips()
    {
        var r = _m.ToIImmutableList(_src);
        Assert.Equal(new[] { 10, 20, 30 }, r.Nums);
    }

    [Fact] public void ImmutableHashSet_roundtrips()
    {
        var r = _m.ToImmutableHashSet(_src);
        Assert.True(r.Nums.SetEquals(new[] { 10, 20, 30 }));
    }

    [Fact] public void IImmutableSet_roundtrips()
    {
        var r = _m.ToIImmutableSet(_src);
        Assert.True(r.Nums.SetEquals(new[] { 10, 20, 30 }));
    }

    [Fact] public void ImmutableArray_null_source_yields_empty()
    {
        var r = _m.ToImmutableArray(new TaxImmutableSrc { Nums = null! });
        Assert.True(r.Nums.IsEmpty);
    }
}

// ─── Dictionary interface / immutable dict targets ───────────────────────────

public class TaxDictSrc
{
    public Dictionary<string, int> D { get; set; } = new();
}

public class TaxIDictDst
{
    public IDictionary<string, int> D { get; set; } = new Dictionary<string, int>();
}

public class TaxIReadOnlyDictDst
{
    public IReadOnlyDictionary<string, int> D { get; set; } = new Dictionary<string, int>();
}

public class TaxImmutableDictDst
{
    public ImmutableDictionary<string, int> D { get; set; } = ImmutableDictionary<string, int>.Empty;
}

public class TaxIImmutableDictDst
{
    public IImmutableDictionary<string, int> D { get; set; } = ImmutableDictionary<string, int>.Empty;
}

[DwarfMapper]
public partial class TaxDictMapper
{
    public partial TaxIDictDst           ToIDictionary(TaxDictSrc s);
    public partial TaxIReadOnlyDictDst   ToIReadOnlyDictionary(TaxDictSrc s);
    public partial TaxImmutableDictDst   ToImmutableDictionary(TaxDictSrc s);
    public partial TaxIImmutableDictDst  ToIImmutableDictionary(TaxDictSrc s);
}

public class DictionaryTaxonomyRuntimeTests
{
    private readonly TaxDictMapper _m = new();
    private readonly TaxDictSrc _src = new() { D = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 } };

    [Fact] public void IDictionary_roundtrips()
    {
        var r = _m.ToIDictionary(_src);
        Assert.Equal(2, r.D.Count);
        Assert.Equal(1, r.D["a"]);
    }

    [Fact] public void IReadOnlyDictionary_roundtrips()
    {
        var r = _m.ToIReadOnlyDictionary(_src);
        Assert.Equal(2, r.D.Count);
        Assert.Equal(2, r.D["b"]);
    }

    [Fact] public void ImmutableDictionary_roundtrips()
    {
        var r = _m.ToImmutableDictionary(_src);
        Assert.Equal(2, r.D.Count);
        Assert.Equal(1, r.D["a"]);
    }

    [Fact] public void IImmutableDictionary_roundtrips()
    {
        var r = _m.ToIImmutableDictionary(_src);
        Assert.Equal(2, r.D.Count);
        Assert.Equal(2, r.D["b"]);
    }

    [Fact] public void IDictionary_null_source_yields_empty()
    {
        var r = _m.ToIDictionary(new TaxDictSrc { D = null! });
        Assert.NotNull(r.D);
        Assert.Empty(r.D);
    }
}

// ─── Lazy IEnumerable<T> target — runtime verify ─────────────────────────────

public class TaxLazyEnumSrc
{
    public List<int> Nums { get; set; } = new();
}

public class TaxLazyEnumDst
{
    public IEnumerable<int> Nums { get; set; } = Array.Empty<int>();
}

public class TaxLazyEnumLongSrc
{
    public List<int> Nums { get; set; } = new();
}

public class TaxLazyEnumLongDst
{
    // different element type to force Select path
    public IEnumerable<long> Nums { get; set; } = Array.Empty<long>();
}

[DwarfMapper]
public partial class TaxLazyMapper
{
    public partial TaxLazyEnumDst    ToLazyEnum(TaxLazyEnumSrc s);
    public partial TaxLazyEnumLongDst ToLazyEnumLong(TaxLazyEnumLongSrc s);
}

public class LazyIEnumerableRuntimeTests
{
    private readonly TaxLazyMapper _m = new();

    [Fact]
    public void LazyEnum_identity_enumerates_correctly()
    {
        var src = new TaxLazyEnumSrc { Nums = new List<int> { 7, 8, 9 } };
        var dst = _m.ToLazyEnum(src);
        Assert.Equal(new[] { 7, 8, 9 }, dst.Nums);
    }

    [Fact]
    public void LazyEnum_with_conversion_enumerates_correctly()
    {
        var src = new TaxLazyEnumLongSrc { Nums = new List<int> { 1, 2, 3 } };
        var dst = _m.ToLazyEnumLong(src);
        Assert.Equal(new long[] { 1L, 2L, 3L }, dst.Nums);
    }

    [Fact]
    public void LazyEnum_null_source_yields_empty_not_throw()
    {
        var dst = _m.ToLazyEnum(new TaxLazyEnumSrc { Nums = null! });
        Assert.NotNull(dst.Nums);
        Assert.Empty(dst.Nums);
    }
}

// ─── NullCollections = AsNull ─────────────────────────────────────────────────

public class TaxNullAsSrc
{
    public List<int>? Nums { get; set; }
}

public class TaxNullAsDst
{
    public List<int>? Nums { get; set; }
}

[DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
public partial class TaxNullAsNullMapper
{
    public partial TaxNullAsDst Map(TaxNullAsSrc s);
}

public class TaxNullAsEmptySrc
{
    public List<int>? Nums { get; set; }
}

public class TaxNullAsEmptyDst
{
    public List<int> Nums { get; set; } = new();
}

[DwarfMapper] // default AsEmpty
public partial class TaxNullAsEmptyMapper
{
    public partial TaxNullAsEmptyDst Map(TaxNullAsEmptySrc s);
}

public class NullCollectionsRuntimeTests
{
    [Fact]
    public void AsNull_propagates_null_source()
    {
        var dst = new TaxNullAsNullMapper().Map(new TaxNullAsSrc { Nums = null });
        Assert.Null(dst.Nums);
    }

    [Fact]
    public void AsNull_propagates_non_null_source()
    {
        var dst = new TaxNullAsNullMapper().Map(new TaxNullAsSrc { Nums = new List<int> { 5 } });
        Assert.NotNull(dst.Nums);
        Assert.Equal(new[] { 5 }, dst.Nums);
    }

    [Fact]
    public void Default_AsEmpty_null_source_yields_empty()
    {
        var dst = new TaxNullAsEmptyMapper().Map(new TaxNullAsEmptySrc { Nums = null });
        Assert.NotNull(dst.Nums);
        Assert.Empty(dst.Nums);
    }
}

// ─── Nested collections round-trips ──────────────────────────────────────────

public class TaxNestedSrc
{
    public List<List<int>>    NestedList { get; set; } = new();
    public int[][]            NestedArray { get; set; } = Array.Empty<int[]>();
    public Dictionary<string, List<int>> DictOfList { get; set; } = new();
    public List<int[]>        ListOfArray { get; set; } = new();
}

public class TaxNestedDst
{
    public List<List<int>>    NestedList { get; set; } = new();
    public int[][]            NestedArray { get; set; } = Array.Empty<int[]>();
    public Dictionary<string, List<int>> DictOfList { get; set; } = new();
    public List<int[]>        ListOfArray { get; set; } = new();
}

[DwarfMapper]
public partial class TaxNestedMapper
{
    public partial TaxNestedDst Map(TaxNestedSrc s);
}

public class NestedCollectionRuntimeTests
{
    [Fact]
    public void List_of_list_int_roundtrips()
    {
        var src = new TaxNestedSrc
        {
            NestedList = new List<List<int>> { new() { 1, 2 }, new() { 3, 4 } }
        };
        var dst = new TaxNestedMapper().Map(src);
        Assert.Equal(2, dst.NestedList.Count);
        Assert.Equal(new[] { 1, 2 }, dst.NestedList[0]);
        Assert.Equal(new[] { 3, 4 }, dst.NestedList[1]);
    }

    [Fact]
    public void Array_of_array_int_roundtrips()
    {
        var src = new TaxNestedSrc
        {
            NestedArray = new[] { new[] { 1, 2 }, new[] { 3, 4 } }
        };
        var dst = new TaxNestedMapper().Map(src);
        Assert.Equal(2, dst.NestedArray.Length);
        Assert.Equal(new[] { 1, 2 }, dst.NestedArray[0]);
        Assert.Equal(new[] { 3, 4 }, dst.NestedArray[1]);
    }

    [Fact]
    public void Dictionary_with_list_value_roundtrips()
    {
        var src = new TaxNestedSrc
        {
            DictOfList = new Dictionary<string, List<int>> { ["k"] = new() { 9, 8 } }
        };
        var dst = new TaxNestedMapper().Map(src);
        Assert.True(dst.DictOfList.ContainsKey("k"));
        Assert.Equal(new[] { 9, 8 }, dst.DictOfList["k"]);
    }

    [Fact]
    public void List_of_array_int_roundtrips()
    {
        var src = new TaxNestedSrc
        {
            ListOfArray = new List<int[]> { new[] { 5, 6 } }
        };
        var dst = new TaxNestedMapper().Map(src);
        Assert.Single(dst.ListOfArray);
        Assert.Equal(new[] { 5, 6 }, dst.ListOfArray[0]);
    }
}

// ─── Cross-type conversions: List<long>→ImmutableArray<int>, Dict→ImmutableDict ─

public class TaxConvSrc
{
    public List<long>               LongNums   { get; set; } = new();
    public Dictionary<string, long> LongDict   { get; set; } = new();
}

public class TaxConvDst
{
    public ImmutableArray<int>                LongNums   { get; set; }
    public ImmutableDictionary<string, int>   LongDict   { get; set; } = ImmutableDictionary<string, int>.Empty;
}

[DwarfMapper]
public partial class TaxConvMapper
{
    public partial TaxConvDst Map(TaxConvSrc s);
}

public class CrossTypeConversionRuntimeTests
{
    [Fact]
    public void List_long_to_ImmutableArray_int_roundtrips_inrange()
    {
        var src = new TaxConvSrc { LongNums = new List<long> { 1L, 2L, 3L } };
        var dst = new TaxConvMapper().Map(src);
        // ImmutableArray<T> is a value type; xUnit equality comparison needs explicit cast to IEnumerable
        Assert.Equal(new[] { 1, 2, 3 }, dst.LongNums);
    }

    [Fact]
    public void List_long_to_ImmutableArray_int_throws_on_overflow()
    {
        var src = new TaxConvSrc { LongNums = new List<long> { long.MaxValue } };
        Assert.Throws<OverflowException>(() => new TaxConvMapper().Map(src));
    }

    [Fact]
    public void Dict_string_long_to_ImmutableDict_string_int_roundtrips()
    {
        var src = new TaxConvSrc { LongDict = new Dictionary<string, long> { ["x"] = 42L } };
        var dst = new TaxConvMapper().Map(src);
        Assert.Equal(42, dst.LongDict["x"]);
    }
}

// ─── Part A + Part B: List<SrcRec> → List<DstRec> via auto-nest ──────────────

public class TaxSrcObjSrc  { public string City { get; set; } = ""; }
public class TaxSrcObjDst  { public string City { get; set; } = ""; }

public class TaxCompSrc { public List<TaxSrcObjSrc> Items { get; set; } = new(); }
public class TaxCompDst { public List<TaxSrcObjDst> Items { get; set; } = new(); }

[DwarfMapper]
public partial class TaxCompMapper
{
    public partial TaxCompDst Map(TaxCompSrc s);
}

public class CollectionAutoNestRuntimeTests
{
    [Fact]
    public void List_of_SrcRec_to_List_of_DstRec_roundtrips()
    {
        var src = new TaxCompSrc
        {
            Items = new List<TaxSrcObjSrc>
            {
                new() { City = "Erebor" },
                new() { City = "Moria" },
            }
        };
        var dst = new TaxCompMapper().Map(src);
        Assert.Equal(2, dst.Items.Count);
        Assert.Equal("Erebor", dst.Items[0].City);
        Assert.Equal("Moria", dst.Items[1].City);
    }
}
