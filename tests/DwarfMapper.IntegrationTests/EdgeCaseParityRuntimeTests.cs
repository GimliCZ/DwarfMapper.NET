// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

// Competitor-parity edge cases that compile-time checks cannot prove: narrowing overflow ACTUALLY
// throwing inside collections/nested/ctor/dict, null source collections becoming empty at runtime,
// dictionary key collisions, the nullable lift/unwrap matrix, deep nesting, and large collections.
// File-scoped domain types use an `Ec_` prefix to stay unique across the project.

// ── Overflow inside a collection element ─────────────────────────────────────
public class Ec_LongListSrc
{
    public List<long> V { get; set; } = new();
}

public class Ec_IntListDst
{
    public List<int> V { get; set; } = new();
}

[DwarfMapper]
public partial class Ec_ListNarrowMapper
{
    public partial Ec_IntListDst Map(Ec_LongListSrc s);
}

// ── Overflow inside a nested member ──────────────────────────────────────────
public class Ec_LongHolder
{
    public long V { get; set; }
}

public class Ec_IntHolder
{
    public int V { get; set; }
}

public class Ec_NestSrc
{
    public Ec_LongHolder Inner { get; set; } = new();
}

public class Ec_NestDst
{
    public Ec_IntHolder Inner { get; set; } = new();
}

[DwarfMapper]
public partial class Ec_NestNarrowMapper
{
    public partial Ec_NestDst Map(Ec_NestSrc s);
}

// ── Overflow inside a constructor parameter ──────────────────────────────────
public class Ec_CtorSrc
{
    public long V { get; set; }
}

public record Ec_CtorDst(int V);

[DwarfMapper]
public partial class Ec_CtorNarrowMapper
{
    public partial Ec_CtorDst Map(Ec_CtorSrc s);
}

// ── Overflow inside a dictionary value ───────────────────────────────────────
public class Ec_DictLongSrc
{
    public Dictionary<string, long> M { get; set; } = new();
}

public class Ec_DictIntDst
{
    public Dictionary<string, int> M { get; set; } = new();
}

[DwarfMapper]
public partial class Ec_DictNarrowMapper
{
    public partial Ec_DictIntDst Map(Ec_DictLongSrc s);
}

// ── Boundary values: widening exact + in-range narrowing succeeds ─────────────
public class Ec_BoundSrc
{
    public int I { get; set; }
    public byte B { get; set; }
    public long N { get; set; }
}

public class Ec_BoundDst
{
    public long I { get; set; }
    public int B { get; set; }
    public int N { get; set; }
}

[DwarfMapper]
public partial class Ec_BoundMapper
{
    public partial Ec_BoundDst Map(Ec_BoundSrc s);
}

// ── Additional parameter that needs a (narrowing) conversion to its destination ─
public class Ec_ApcSrc
{
    public int Id { get; set; }
}

public class Ec_ApcDst
{
    public int Id { get; set; }
    public int Seq { get; set; }
}

[DwarfMapper]
public partial class Ec_ApcMapper
{
    public partial Ec_ApcDst Map(Ec_ApcSrc s, long seq);
}

// ── Null source collections → empty (default AsEmpty) ────────────────────────
public class Ec_NullCollSrc
{
    public List<int>? L { get; set; }
    public HashSet<int>? H { get; set; }
    public int[]? A { get; set; }
    public Dictionary<string, int>? D { get; set; }
}

public class Ec_NullCollDst
{
    public List<int> L { get; set; } = new();
    public HashSet<int> H { get; set; } = new();
    public int[] A { get; set; } = Array.Empty<int>();
    public Dictionary<string, int> D { get; set; } = new();
}

[DwarfMapper]
public partial class Ec_NullCollMapper
{
    public partial Ec_NullCollDst Map(Ec_NullCollSrc s);
}

// ── Dictionary key collision (post-conversion) + key/value conversion ────────
public class Ec_DictKeySrc
{
    public Dictionary<string, long> M { get; set; } = new();
}

public class Ec_DictKeyDst
{
    public Dictionary<int, int> M { get; set; } = new();
}

[DwarfMapper]
public partial class Ec_DictKeyMapper
{
    public partial Ec_DictKeyDst Map(Ec_DictKeySrc s);
}

// ── Nullable lift/unwrap matrix ──────────────────────────────────────────────
public class Ec_NvSrc
{
    public int? V { get; set; }
}

public class Ec_NvDst
{
    public int V { get; set; }
}

[DwarfMapper]
public partial class Ec_NvThrowMapper
{
    public partial Ec_NvDst Map(Ec_NvSrc s);
}

[DwarfMapper(NullStrategy = NullStrategy.SetDefault)]
public partial class Ec_NvDefaultMapper
{
    public partial Ec_NvDst Map(Ec_NvSrc s);
}

public class Ec_NnDst
{
    public int? V { get; set; }
}

[DwarfMapper]
public partial class Ec_NnMapper
{
    public partial Ec_NnDst Map(Ec_NvSrc s);
}

// ── Deep nesting (auto-nest, 6 levels) ───────────────────────────────────────
public class Ec_D5
{
    public string Leaf { get; set; } = "";
}

public class Ec_D4
{
    public Ec_D5 N { get; set; } = new();
}

public class Ec_D3
{
    public Ec_D4 N { get; set; } = new();
}

public class Ec_D2
{
    public Ec_D3 N { get; set; } = new();
}

public class Ec_D1
{
    public Ec_D2 N { get; set; } = new();
}

public class Ec_D0
{
    public Ec_D1 N { get; set; } = new();
}

public class Ec_E5
{
    public string Leaf { get; set; } = "";
}

public class Ec_E4
{
    public Ec_E5 N { get; set; } = new();
}

public class Ec_E3
{
    public Ec_E4 N { get; set; } = new();
}

public class Ec_E2
{
    public Ec_E3 N { get; set; } = new();
}

public class Ec_E1
{
    public Ec_E2 N { get; set; } = new();
}

public class Ec_E0
{
    public Ec_E1 N { get; set; } = new();
}

[DwarfMapper]
public partial class Ec_DeepMapper
{
    public partial Ec_E0 Map(Ec_D0 s);
}

// ── Large collection ─────────────────────────────────────────────────────────
public class Ec_BigSrc
{
    public List<int> V { get; set; } = new();
}

public class Ec_BigDst
{
    public List<long> V { get; set; } = new();
}

[DwarfMapper]
public partial class Ec_BigMapper
{
    public partial Ec_BigDst Map(Ec_BigSrc s);
}

public class EdgeCaseParityRuntimeTests
{
    [Fact]
    public void Narrowing_overflow_in_collection_element_throws()
    {
        var src = new Ec_LongListSrc { V = new List<long> { 1, (long)int.MaxValue + 1 } };
        Assert.Throws<OverflowException>(() => new Ec_ListNarrowMapper().Map(src));
    }

    [Fact]
    public void Narrowing_overflow_in_nested_member_throws()
    {
        var src = new Ec_NestSrc { Inner = new Ec_LongHolder { V = (long)int.MaxValue + 5 } };
        Assert.Throws<OverflowException>(() => new Ec_NestNarrowMapper().Map(src));
    }

    [Fact]
    public void Narrowing_overflow_in_constructor_parameter_throws()
    {
        var src = new Ec_CtorSrc { V = (long)int.MaxValue + 1 };
        Assert.Throws<OverflowException>(() => new Ec_CtorNarrowMapper().Map(src));
    }

    [Fact]
    public void Narrowing_overflow_in_dictionary_value_throws()
    {
        var src = new Ec_DictLongSrc { M = new Dictionary<string, long> { ["a"] = (long)int.MaxValue + 1 } };
        Assert.Throws<OverflowException>(() => new Ec_DictNarrowMapper().Map(src));
    }

    [Fact]
    public void Boundary_widening_exact_and_in_range_narrowing_succeeds()
    {
        var d = new Ec_BoundMapper().Map(new Ec_BoundSrc { I = int.MaxValue, B = byte.MaxValue, N = 100 });
        Assert.Equal(int.MaxValue, d.I); // int → long widening, exact
        Assert.Equal(255, d.B); // byte → int widening, exact
        Assert.Equal(100, d.N); // long → int NARROWING, in range → succeeds (no overflow)

        var min = new Ec_BoundMapper().Map(new Ec_BoundSrc { I = int.MinValue, B = 0, N = int.MinValue });
        Assert.Equal(int.MinValue, min.I);
        Assert.Equal(0, min.B);
        Assert.Equal(int.MinValue, min.N); // long → int at the narrowing boundary, still in range
    }

    [Fact]
    public void Additional_parameter_with_conversion_applies_at_runtime()
    {
        // The extra `long seq` is narrowed (CreateChecked) to the int destination — in range succeeds...
        var d = new Ec_ApcMapper().Map(new Ec_ApcSrc { Id = 1 }, 42L);
        Assert.Equal(42, d.Seq);
        // ...and overflow throws, just like any other narrowing.
        Assert.Throws<OverflowException>(() =>
            new Ec_ApcMapper().Map(new Ec_ApcSrc { Id = 1 }, (long)int.MaxValue + 1));
    }

    [Fact]
    public void Null_source_collections_become_empty_not_null()
    {
        var d = new Ec_NullCollMapper().Map(new Ec_NullCollSrc()); // all null
        Assert.NotNull(d.L);
        Assert.Empty(d.L);
        Assert.NotNull(d.H);
        Assert.Empty(d.H);
        Assert.NotNull(d.A);
        Assert.Empty(d.A);
        Assert.NotNull(d.D);
        Assert.Empty(d.D);
    }

    [Fact]
    public void Dictionary_key_collision_collapses_to_one_entry()
    {
        // "1" and "01" both parse to the int key 1 → the entries collapse to ONE (overwrite via indexer,
        // not throw). Which value survives is enumeration-order dependent (not contractually guaranteed),
        // so we pin the guaranteed behaviour — collapse to a single entry — not which value wins.
        var src = new Ec_DictKeySrc { M = new Dictionary<string, long> { ["1"] = 10, ["01"] = 20 } };
        var d = new Ec_DictKeyMapper().Map(src);
        Assert.Single(d.M);
        Assert.True(d.M.ContainsKey(1));
        Assert.Contains(d.M[1], new[] { 10, 20 });
    }

    [Fact]
    public void Nullable_unwrap_throws_by_default()
    {
        Assert.Throws<InvalidOperationException>(() => new Ec_NvThrowMapper().Map(new Ec_NvSrc { V = null }));
        Assert.Equal(7, new Ec_NvThrowMapper().Map(new Ec_NvSrc { V = 7 }).V);
    }

    [Fact]
    public void Nullable_unwrap_uses_default_under_SetDefault()
    {
        Assert.Equal(0, new Ec_NvDefaultMapper().Map(new Ec_NvSrc { V = null }).V);
        Assert.Equal(7, new Ec_NvDefaultMapper().Map(new Ec_NvSrc { V = 7 }).V);
    }

    [Fact]
    public void Nullable_to_nullable_preserves_null_and_value()
    {
        Assert.Null(new Ec_NnMapper().Map(new Ec_NvSrc { V = null }).V);
        Assert.Equal(7, new Ec_NnMapper().Map(new Ec_NvSrc { V = 7 }).V);
    }

    [Fact]
    public void Deeply_nested_graph_maps_leaf_at_runtime()
    {
        var src = new Ec_D0();
        src.N.N.N.N.N.Leaf = "deep";
        var d = new Ec_DeepMapper().Map(src);
        Assert.Equal("deep", d.N.N.N.N.N.Leaf);
    }

    [Fact]
    public void Large_collection_maps_all_elements()
    {
        var src = new Ec_BigSrc { V = Enumerable.Range(0, 10_000).ToList() };
        var d = new Ec_BigMapper().Map(src);
        Assert.Equal(10_000, d.V.Count);
        Assert.Equal(0L, d.V[0]);
        Assert.Equal(9_999L, d.V[9_999]);
    }
}
