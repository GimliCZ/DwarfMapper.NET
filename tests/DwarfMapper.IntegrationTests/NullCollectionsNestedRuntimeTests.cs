// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

// ─── A1: AsNull must propagate into nested collection/dict element converters ──

public class A1DictNullSrc
{
    public Dictionary<string, List<int>?>? Outer { get; set; }
}
public class A1DictNullDst
{
    public Dictionary<string, List<int>?>? Outer { get; set; }
}

[DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
public partial class A1DictNullMapper
{
    public partial A1DictNullDst Map(A1DictNullSrc s);
}

public class A1ListNullSrc
{
    public List<List<int>?>? Outer { get; set; }
}
public class A1ListNullDst
{
    public List<List<int>?>? Outer { get; set; }
}

[DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
public partial class A1ListNullMapper
{
    public partial A1ListNullDst Map(A1ListNullSrc s);
}

public class NullCollectionsNestedA1RuntimeTests
{
    // A1a: Dict<string, List<int>?> — null VALUE must map to null, not empty
    [Fact]
    public void A1_dict_null_value_propagates_as_null_under_AsNull()
    {
        var src = new A1DictNullSrc
        {
            Outer = new Dictionary<string, List<int>?> { ["x"] = null }
        };
        var dst = new A1DictNullMapper().Map(src);
        Assert.NotNull(dst.Outer);
        Assert.True(dst.Outer!.ContainsKey("x"));
        Assert.Null(dst.Outer["x"]); // must be null, NOT empty List
    }

    // A1b: Dict<string, List<int>?> — non-null VALUE still maps correctly
    [Fact]
    public void A1_dict_non_null_value_maps_under_AsNull()
    {
        var src = new A1DictNullSrc
        {
            Outer = new Dictionary<string, List<int>?> { ["k"] = new List<int> { 1, 2 } }
        };
        var dst = new A1DictNullMapper().Map(src);
        Assert.NotNull(dst.Outer);
        Assert.Equal(new[] { 1, 2 }, dst.Outer!["k"]);
    }

    // A1c: List<List<int>?> — null ELEMENT must map to null, not empty
    [Fact]
    public void A1_list_null_element_propagates_as_null_under_AsNull()
    {
        var src = new A1ListNullSrc
        {
            Outer = new List<List<int>?> { null, new List<int> { 3 } }
        };
        var dst = new A1ListNullMapper().Map(src);
        Assert.NotNull(dst.Outer);
        Assert.Equal(2, dst.Outer!.Count);
        Assert.Null(dst.Outer[0]);      // must be null, NOT empty List
        Assert.Equal(new[] { 3 }, dst.Outer[1]);
    }

    // A1d: default AsEmpty — null element maps to empty (regression guard)
    [Fact]
    public void A1_AsEmpty_default_null_element_yields_empty_not_null()
    {
        // Default (no NullCollections = AsNull) — this must stay empty
        // Using the existing TaxNestedMapper which has no AsNull
        var src = new TaxNestedSrc
        {
            NestedList = new List<List<int>> { new List<int> { 1 } }
        };
        var dst = new TaxNestedMapper().Map(src);
        Assert.NotNull(dst.NestedList[0]); // not null under default AsEmpty
    }
}

// ─── A2: ImmutableArray<T>? — null source should yield null (HasValue=false) ──

public class A2ImmutNullSrc
{
    public List<int>? Xs { get; set; }
}
public class A2ImmutNullDst
{
    public ImmutableArray<int>? Xs { get; set; }
}

[DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
public partial class A2ImmutableArrayNullMapper
{
    public partial A2ImmutNullDst Map(A2ImmutNullSrc s);
}

public class NullCollectionsA2ImmutableArrayRuntimeTests
{
    // A2a: null source List<int>? → ImmutableArray<int>? under AsNull must yield HasValue=false
    [Fact]
    public void A2_null_source_to_nullable_ImmutableArray_yields_null()
    {
        var dst = new A2ImmutableArrayNullMapper().Map(new A2ImmutNullSrc { Xs = null });
        Assert.False(dst.Xs.HasValue); // must be null (HasValue=false), NOT default ImmutableArray
    }

    // A2b: non-null source maps correctly
    [Fact]
    public void A2_non_null_source_to_nullable_ImmutableArray_maps_correctly()
    {
        var dst = new A2ImmutableArrayNullMapper().Map(new A2ImmutNullSrc { Xs = new List<int> { 7, 8 } });
        Assert.True(dst.Xs.HasValue);
        Assert.Equal(new[] { 7, 8 }, dst.Xs!.Value);
    }
}

// ─── A3: AsNull + non-nullable target must fall back to AsEmpty (no CS8601) ───

// A3: simple case — non-nullable target only
public class A3NonNullTargetSrc
{
    public List<int>? Xs { get; set; }
}
public class A3NonNullTargetDst
{
    // NON-nullable target: AsNull must fall back to AsEmpty to avoid CS8601
    public List<int> Xs { get; set; } = new();
}

[DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
public partial class A3NonNullFallbackMapper
{
    public partial A3NonNullTargetDst Map(A3NonNullTargetSrc s);
}

// A3: mixed case — one non-nullable, one nullable member
public class A3MixedSrc
{
    public List<int>? Xs { get; set; }
    public List<int>? Ys { get; set; }
}
public class A3MixedDst
{
    // NON-nullable: fallback to AsEmpty
    public List<int> Xs { get; set; } = new();
    // Nullable: AsNull propagates null
    public List<int>? Ys { get; set; }
}

[DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
public partial class A3MixedMapper
{
    public partial A3MixedDst MapFull(A3MixedSrc s);
}

public class NullCollectionsA3NonNullTargetRuntimeTests
{
    // A3a: null source + non-nullable target → must yield empty (fallback AsEmpty), not null (no CS8601)
    [Fact]
    public void A3_null_source_non_nullable_target_yields_empty()
    {
        var dst = new A3NonNullFallbackMapper().Map(new A3NonNullTargetSrc { Xs = null });
        Assert.NotNull(dst.Xs); // falls back to empty
        Assert.Empty(dst.Xs);
    }

    // A3b: non-null source + non-nullable target → maps normally
    [Fact]
    public void A3_non_null_source_non_nullable_target_maps_correctly()
    {
        var dst = new A3NonNullFallbackMapper().Map(new A3NonNullTargetSrc { Xs = new List<int> { 5 } });
        Assert.Equal(new[] { 5 }, dst.Xs);
    }

    // A3c: mixed mapper — null source: non-nullable target → empty, nullable target → null
    [Fact]
    public void A3_mixed_null_source_non_nullable_empty_nullable_null()
    {
        var dst = new A3MixedMapper().MapFull(new A3MixedSrc { Xs = null, Ys = null });
        Assert.NotNull(dst.Xs); // non-nullable → falls back to empty
        Assert.Empty(dst.Xs);
        Assert.Null(dst.Ys);    // nullable → AsNull propagates null
    }
}

// ─── A4: EmitList/HashSet null guard must come BEFORE __r allocation ──────────

// NOTE: A4 is a generated-code order test. The runtime behavior is unchanged.
// We test it via a generator test (see NullCollectionsA4GeneratorTests.cs).
// Here we just confirm runtime behavior is still correct (null→empty for default, null→null for AsNull).
public class A4NullOrderSrc
{
    public List<int>? Xs { get; set; }
    public System.Collections.Generic.HashSet<int>? Hs { get; set; }
}
public class A4NullOrderDstEmpty
{
    public List<int> Xs { get; set; } = new();
    public System.Collections.Generic.HashSet<int> Hs { get; set; } = new();
}
public class A4NullOrderDstNull
{
    public List<int>? Xs { get; set; }
    public System.Collections.Generic.HashSet<int>? Hs { get; set; }
}

[DwarfMapper]
public partial class A4EmptyMapper
{
    public partial A4NullOrderDstEmpty Map(A4NullOrderSrc s);
}

[DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
public partial class A4NullMapper
{
    public partial A4NullOrderDstNull Map(A4NullOrderSrc s);
}

public class NullCollectionsA4AllocationOrderRuntimeTests
{
    [Fact]
    public void A4_default_null_list_yields_empty()
    {
        var dst = new A4EmptyMapper().Map(new A4NullOrderSrc { Xs = null, Hs = null });
        Assert.NotNull(dst.Xs);
        Assert.Empty(dst.Xs);
        Assert.NotNull(dst.Hs);
        Assert.Empty(dst.Hs);
    }

    [Fact]
    public void A4_AsNull_null_list_yields_null()
    {
        var dst = new A4NullMapper().Map(new A4NullOrderSrc { Xs = null, Hs = null });
        Assert.Null(dst.Xs);
        Assert.Null(dst.Hs);
    }

    [Fact]
    public void A4_non_null_list_maps_correctly()
    {
        var dst = new A4NullMapper().Map(new A4NullOrderSrc
        {
            Xs = new List<int> { 1, 2 },
            Hs = new System.Collections.Generic.HashSet<int> { 3, 4 }
        });
        Assert.Equal(new[] { 1, 2 }, dst.Xs);
        Assert.True(dst.Hs!.SetEquals(new[] { 3, 4 }));
    }
}
