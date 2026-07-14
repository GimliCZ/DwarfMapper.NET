// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

// Runtime coverage for the remaining feature attributes/enums that previously had generator tests only:
// Phase-1 source-coverage (RequiredMapping=Both + [MapIgnoreSource]), [Reinterpret] blit, an explicit
// [DwarfMapperConstructor] selection, and a method-level [AutoNest]. File-scoped `Rf_` prefixed types.

// ── Phase 1: RequiredMapping = Both + [MapIgnoreSource] ──────────────────────
public class Rf_ScSrc
{
    public int Id { get; set; }
    public string Audit { get; set; } = "";
}

public class Rf_ScDst
{
    public int Id { get; set; }
}

[DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]
[MapIgnoreSource(nameof(Rf_ScSrc.Audit))]
public partial class Rf_ScMapper
{
    public partial Rf_ScDst Map(Rf_ScSrc s);
}

// ── [Reinterpret] — force a layout-compatible blit on differently-named struct fields ──
public struct Rf_Px
{
    public int A;
    public int B;
}

public struct Rf_Qx
{
    public int X;
    public int Y;
}

public class Rf_ReSrc
{
    public Rf_Px[] V { get; set; } = Array.Empty<Rf_Px>();
}

public class Rf_ReDst
{
    public Rf_Qx[] V { get; set; } = Array.Empty<Rf_Qx>();
}

[DwarfMapper]
public partial class Rf_ReMapper
{
    [Reinterpret(nameof(Rf_ReDst.V))]
    public partial Rf_ReDst Map(Rf_ReSrc s);
}

// ── [DwarfMapperConstructor] — select a specific constructor on the target POCO ──
public class Rf_CtorSrc
{
    public int Id { get; set; }
}

public class Rf_CtorDst
{
    public Rf_CtorDst()
    {
        Id = -1;
    }

    [DwarfMapperConstructor]
    public Rf_CtorDst(int id)
    {
        Id = id;
    }

    public int Id { get; }
}

[DwarfMapper(CaseInsensitive = true)]
public partial class Rf_CtorMapper
{
    public partial Rf_CtorDst Map(Rf_CtorSrc s);
}

// ── Method-level [AutoNest] ──────────────────────────────────────────────────
public class Rf_Inner
{
    public int X { get; set; }
}

public class Rf_InnerD
{
    public int X { get; set; }
}

public class Rf_AnSrc
{
    public Rf_Inner Inner { get; set; } = new();
}

public class Rf_AnDst
{
    public Rf_InnerD Inner { get; set; } = new();
}

[DwarfMapper]
public partial class Rf_AnMapper
{
    [AutoNest()]
    public partial Rf_AnDst Map(Rf_AnSrc s);
}

public class RemainingFeaturesRuntimeTests
{
    [Fact]
    public void RequiredMapping_Both_with_MapIgnoreSource_still_maps()
    {
        var d = new Rf_ScMapper().Map(new Rf_ScSrc { Id = 5, Audit = "x" });
        Assert.Equal(5, d.Id);
    }

    [Fact]
    public void Reinterpret_blits_layout_compatible_struct_array()
    {
        var src = new Rf_ReSrc { V = new[] { new Rf_Px { A = 1, B = 2 }, new Rf_Px { A = 3, B = 4 } } };
        var d = new Rf_ReMapper().Map(src);
        Assert.Equal(2, d.V.Length);
        Assert.Equal(1, d.V[0].X);
        Assert.Equal(2, d.V[0].Y); // A→X, B→Y positionally
        Assert.Equal(3, d.V[1].X);
        Assert.Equal(4, d.V[1].Y);
    }

    [Fact]
    public void DwarfMapperConstructor_selects_the_annotated_constructor()
    {
        var d = new Rf_CtorMapper().Map(new Rf_CtorSrc { Id = 9 });
        Assert.Equal(9, d.Id); // used Rf_CtorDst(int), not the parameterless ctor (which sets -1)
    }

    [Fact]
    public void Method_level_AutoNest_maps_nested_object()
    {
        var d = new Rf_AnMapper().Map(new Rf_AnSrc { Inner = new Rf_Inner { X = 7 } });
        Assert.Equal(7, d.Inner.X);
    }
}
