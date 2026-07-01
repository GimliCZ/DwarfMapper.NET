// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// Differential-parity fixtures for MapConfig_Map_rename_matches_attribute_form (below): the SAME (S -> T) rename
// is declared once via a MapConfig<S,T> convention method and once via the pair-scoped [MapProperty<S,T>]
// attribute, on two separate mapper classes over the same domain types. Both are compiled for real by this
// project's normal build (the generator runs as an analyzer, same as every other IntegrationTests file) — there
// is no need for a separate dynamic-compilation/reflection harness (none exists in this test project; the
// reflect-and-invoke `GeneratorTestHarness` lives in DwarfMapper.Generator.Tests as an `internal` type and isn't
// reachable from here). Distinct class names avoid the collision a shared name like both being "M" would cause
// within one compilation.
public sealed class McS { public int MessagesCount { get; set; } }
public sealed class McT { public int NumberOfMessages { get; set; } }

[DwarfMapper]
[GenerateMap<McS, McT>]
public partial class McConfigMapper
{
    private static void Cfg(MapConfig<McS, McT> c) => c.Map(t => t.NumberOfMessages, s => s.MessagesCount);
}

[DwarfMapper]
[GenerateMap<McS, McT>]
[MapProperty<McS, McT>(nameof(McS.MessagesCount), nameof(McT.NumberOfMessages))]
public partial class McAttrMapper { }

// Op 0 fixtures: proves F2 (parenthesized single-parameter lambdas `(t) => t.X` are accepted, not just
// `t => t.X`) using a fresh pair so it doesn't collide with McS/McT above (all fixtures share one compilation).
public sealed class PmS { public int MessagesCount { get; set; } }
public sealed class PmT { public int NumberOfMessages { get; set; } }

[DwarfMapper]
[GenerateMap<PmS, PmT>]
public partial class PmConfigMapper
{
    private static void Cfg(MapConfig<PmS, PmT> c) => c.Map((t) => t.NumberOfMessages, (s) => s.MessagesCount);
}

[DwarfMapper]
[GenerateMap<PmS, PmT>]
[MapProperty<PmS, PmT>(nameof(PmS.MessagesCount), nameof(PmT.NumberOfMessages))]
public partial class PmAttrMapper { }

// Op 1 fixtures: proves `.Map` flatten — a dotted source selector (s => s.Author.Name) reaching into a nested
// object — already works via TryReadMemberPath's chain-walk, with no collector change needed.
public sealed class Author { public string? Name { get; set; } }
public sealed class FlS { public Author Author { get; set; } = new(); }
public sealed class FlT { public string? AuthorName { get; set; } }

[DwarfMapper]
[GenerateMap<FlS, FlT>]
public partial class FlConfigMapper
{
    private static void Cfg(MapConfig<FlS, FlT> c) => c.Map(t => t.AuthorName, s => s.Author.Name);
}

[DwarfMapper]
[GenerateMap<FlS, FlT>]
[MapProperty<FlS, FlT>("Author.Name", "AuthorName")]
public partial class FlAttrMapper { }

// Op 2 fixtures: proves `.Map` 3-arg converter method group form — the 3rd arg is read as `Use`, already
// handled by the existing 3-arg branch, with no collector change needed.
public sealed class CvS { public string? Raw { get; set; } }
public sealed class CvT { public string? Clean { get; set; } }

[DwarfMapper]
[GenerateMap<CvS, CvT>]
public partial class CvConfigMapper
{
    private static void Cfg(MapConfig<CvS, CvT> c) => c.Map(t => t.Clean, s => s.Raw, Norm);
    private static string? Norm(string? v) => v?.Trim();
}

[DwarfMapper]
[GenerateMap<CvS, CvT>]
[MapProperty<CvS, CvT>("Raw", "Clean", Use = nameof(Norm))]
public partial class CvAttrMapper
{
    private static string? Norm(string? v) => v?.Trim();
}

// Op 3 fixtures: proves `.MapWhen` — conditional assignment gated by a predicate method group over the source.
public sealed class WhS { public int V { get; set; } public bool Ok { get; set; } }
public sealed class WhT { public int V { get; set; } }

[DwarfMapper]
[GenerateMap<WhS, WhT>]
public partial class WhConfigMapper
{
    private static void Cfg(MapConfig<WhS, WhT> c) => c.MapWhen(t => t.V, s => s.V, Gate);
    private static bool Gate(WhS s) => s.Ok;
}

[DwarfMapper]
[GenerateMap<WhS, WhT>]
[MapProperty<WhS, WhT>("V", "V", When = nameof(Gate))]
public partial class WhAttrMapper
{
    private static bool Gate(WhS s) => s.Ok;
}

// Op 4 fixtures: proves `.Ignore` suppresses DWARF001 completeness for a destination member with no source
// counterpart. Without the Ignore, IgT.Extra has no matching source member and the whole test project fails
// to build with DWARF001 — that build failure IS the RED for this op.
public sealed class IgS { public int A { get; set; } }
public sealed class IgT { public int A { get; set; } public int Extra { get; set; } }

[DwarfMapper]
[GenerateMap<IgS, IgT>]
public partial class IgConfigMapper
{
    private static void Cfg(MapConfig<IgS, IgT> c) => c.Ignore(t => t.Extra);
}

// Op 6 fixtures: proves `.Construct` — a factory method group builds the destination instead of the default
// constructor-argument resolution. (Named CnAlias, not "Alias", to avoid CA1716's reserved-keyword rule.)
// The ctor param "display" intentionally has no name/case match on CnS (ctor binding is always
// case-insensitive by name, so e.g. a "fmt"/"Fmt" pair would already bind without any factory at all) —
// only the factory method group can plausibly build a CnAlias, so this genuinely exercises `.Construct`.
public sealed class CnS { public string Fmt { get; set; } = ""; public string Name { get; set; } = ""; }
public sealed class CnAlias : System.IEquatable<CnAlias>
{
    public CnAlias(string display) { Display = display; }
    public string Display { get; }
    public bool Equals(CnAlias? other) => other is not null && Display == other.Display;
    public override bool Equals(object? obj) => Equals(obj as CnAlias);
    public override int GetHashCode() => Display.GetHashCode(System.StringComparison.Ordinal);
}

[DwarfMapper]
[GenerateMap<CnS, CnAlias>]
public partial class CnConfigMapper
{
    private static void Cfg(MapConfig<CnS, CnAlias> c) => c.Construct(Make);
    private static CnAlias Make(CnS s) => new($"{s.Fmt}:{s.Name}");
}

[DwarfMapper]
[GenerateMap<CnS, CnAlias>]
[MapConstructor<CnS, CnAlias>(nameof(Make))]
public partial class CnAttrMapper
{
    private static CnAlias Make(CnS s) => new($"{s.Fmt}:{s.Name}");
}

// Op 7 fixtures: proves `.MapOr` value-type (int?) null-substitute parity against [MapProperty(NullSubstitute=)].
public sealed class MoS { public int? V { get; set; } }
public sealed class MoT { public int V { get; set; } }

[DwarfMapper]
[GenerateMap<MoS, MoT>]
public partial class MoConfigMapper
{
    private static void Cfg(MapConfig<MoS, MoT> c) => c.MapOr(t => t.V, s => s.V, 5);
}

[DwarfMapper]
[GenerateMap<MoS, MoT>]
[MapProperty<MoS, MoT>("V", "V", NullSubstitute = 5)]
public partial class MoAttrMapper { }

// Op 7 fixtures (reference-type overload): proves `.MapOr` string? null-substitute parity — closes the Task-1
// gap where the class-constrained MapOr overload was untested.
public sealed class MoRS { public string? Name { get; set; } }
public sealed class MoRT { public string? Name { get; set; } }

[DwarfMapper]
[GenerateMap<MoRS, MoRT>]
public partial class MoRConfigMapper
{
    private static void Cfg(MapConfig<MoRS, MoRT> c) => c.MapOr(t => t.Name, s => s.Name, "none");
}

[DwarfMapper]
[GenerateMap<MoRS, MoRT>]
[MapProperty<MoRS, MoRT>("Name", "Name", NullSubstitute = "none")]
public partial class MoRAttrMapper { }

// Op 8 fixtures: proves `.Value` constant parity against [MapValue<T>(target, constant)].
public sealed class VcS { public int A { get; set; } }
public sealed class VcT { public int A { get; set; } public int Tag { get; set; } }

[DwarfMapper]
[GenerateMap<VcS, VcT>]
public partial class VcConfigMapper
{
    private static void Cfg(MapConfig<VcS, VcT> c) => c.Value(t => t.Tag, 99);
}

[DwarfMapper]
[GenerateMap<VcS, VcT>]
[MapValue<VcT>(nameof(VcT.Tag), 99)]
public partial class VcAttrMapper { }

// Op 8 fixtures (compute overload): proves `.Value` method-group parity against [MapValue<T>(target){ Use = }].
public sealed class VcT2 { public int A { get; set; } public int Tag { get; set; } }

[DwarfMapper]
[GenerateMap<VcS, VcT2>]
public partial class VcComputeConfigMapper
{
    private static void Cfg(MapConfig<VcS, VcT2> c) => c.Value(t => t.Tag, Make);
    private static int Make() => 7;
}

[DwarfMapper]
[GenerateMap<VcS, VcT2>]
[MapValue<VcT2>(nameof(VcT2.Tag), Use = nameof(Make))]
public partial class VcComputeAttrMapper
{
    private static int Make() => 7;
}

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Combination matrix (plan Task 14): every one of the 9 MapConfig ops proven in THREE modes —
// attribute-only, config-only, and MIXED (the op via config on one member while a second member
// on the SAME mapper class uses the attribute form) — with all three shown to agree. Each op gets
// its own small type pair (prefixed "Mx" + a short op tag) with TWO members so the mixed mapper
// has somewhere to put the config-form and the attribute-form side by side. Reuses the Batch A/B
// single-member Config/Attr fixtures above for ops 0-2/7/8 is not straightforward (those pairs only
// carry one relevant member each), so a self-contained two-member pair is used per op below —
// this also directly proves attribute-form == config-form for the SAME op on fresh fixtures,
// independent of the Batch A/B parity tests.

// Op: rename (`.Map` 2-arg) — MxRnX via config, MxRnY via attribute, in the same MixedMapper.
public sealed class MxRnS { public int A { get; set; } public int B { get; set; } }
public sealed class MxRnT { public int X { get; set; } public int Y { get; set; } }

[DwarfMapper]
[GenerateMap<MxRnS, MxRnT>]
public partial class MxRnConfigMapper
{
    private static void Cfg(MapConfig<MxRnS, MxRnT> c) => c.Map(t => t.X, s => s.A).Map(t => t.Y, s => s.B);
}

[DwarfMapper]
[GenerateMap<MxRnS, MxRnT>]
[MapProperty<MxRnS, MxRnT>("A", "X")]
[MapProperty<MxRnS, MxRnT>("B", "Y")]
public partial class MxRnAttrMapper { }

[DwarfMapper]
[GenerateMap<MxRnS, MxRnT>]
[MapProperty<MxRnS, MxRnT>("B", "Y")]
public partial class MxRnMixedMapper
{
    private static void Cfg(MapConfig<MxRnS, MxRnT> c) => c.Map(t => t.X, s => s.A);
}

// Op: flatten (`.Map` dotted source selector) — MxFlAName via config, MxFlBNick via attribute.
public sealed class MxFlA { public string? Name { get; set; } }
public sealed class MxFlB { public string? Nick { get; set; } }
public sealed class MxFlS { public MxFlA A { get; set; } = new(); public MxFlB B { get; set; } = new(); }
public sealed class MxFlT { public string? AName { get; set; } public string? BNick { get; set; } }

[DwarfMapper]
[GenerateMap<MxFlS, MxFlT>]
public partial class MxFlConfigMapper
{
    private static void Cfg(MapConfig<MxFlS, MxFlT> c) => c.Map(t => t.AName, s => s.A.Name).Map(t => t.BNick, s => s.B.Nick);
}

[DwarfMapper]
[GenerateMap<MxFlS, MxFlT>]
[MapProperty<MxFlS, MxFlT>("A.Name", "AName")]
[MapProperty<MxFlS, MxFlT>("B.Nick", "BNick")]
public partial class MxFlAttrMapper { }

[DwarfMapper]
[GenerateMap<MxFlS, MxFlT>]
[MapProperty<MxFlS, MxFlT>("B.Nick", "BNick")]
public partial class MxFlMixedMapper
{
    private static void Cfg(MapConfig<MxFlS, MxFlT> c) => c.Map(t => t.AName, s => s.A.Name);
}

// Op: convert (`.Map` 3-arg converter method group) — Clean1 via config, Clean2 via attribute.
public sealed class MxCvS { public string? Raw1 { get; set; } public string? Raw2 { get; set; } }
public sealed class MxCvT { public string? Clean1 { get; set; } public string? Clean2 { get; set; } }

[DwarfMapper]
[GenerateMap<MxCvS, MxCvT>]
public partial class MxCvConfigMapper
{
    private static void Cfg(MapConfig<MxCvS, MxCvT> c) => c.Map(t => t.Clean1, s => s.Raw1, Norm).Map(t => t.Clean2, s => s.Raw2, Norm);
    private static string? Norm(string? v) => v?.Trim();
}

[DwarfMapper]
[GenerateMap<MxCvS, MxCvT>]
[MapProperty<MxCvS, MxCvT>("Raw1", "Clean1", Use = nameof(Norm))]
[MapProperty<MxCvS, MxCvT>("Raw2", "Clean2", Use = nameof(Norm))]
public partial class MxCvAttrMapper
{
    private static string? Norm(string? v) => v?.Trim();
}

[DwarfMapper]
[GenerateMap<MxCvS, MxCvT>]
[MapProperty<MxCvS, MxCvT>("Raw2", "Clean2", Use = nameof(Norm))]
public partial class MxCvMixedMapper
{
    private static void Cfg(MapConfig<MxCvS, MxCvT> c) => c.Map(t => t.Clean1, s => s.Raw1, Norm);
    private static string? Norm(string? v) => v?.Trim();
}

// Op: when (`.MapWhen`) — V1 gated via config, V2 gated via attribute.
public sealed class MxWhS { public int V1 { get; set; } public bool Ok1 { get; set; } public int V2 { get; set; } public bool Ok2 { get; set; } }
public sealed class MxWhT { public int V1 { get; set; } public int V2 { get; set; } }

[DwarfMapper]
[GenerateMap<MxWhS, MxWhT>]
public partial class MxWhConfigMapper
{
    private static void Cfg(MapConfig<MxWhS, MxWhT> c) => c.MapWhen(t => t.V1, s => s.V1, Gate1).MapWhen(t => t.V2, s => s.V2, Gate2);
    private static bool Gate1(MxWhS s) => s.Ok1;
    private static bool Gate2(MxWhS s) => s.Ok2;
}

[DwarfMapper]
[GenerateMap<MxWhS, MxWhT>]
[MapProperty<MxWhS, MxWhT>("V1", "V1", When = nameof(Gate1))]
[MapProperty<MxWhS, MxWhT>("V2", "V2", When = nameof(Gate2))]
public partial class MxWhAttrMapper
{
    private static bool Gate1(MxWhS s) => s.Ok1;
    private static bool Gate2(MxWhS s) => s.Ok2;
}

[DwarfMapper]
[GenerateMap<MxWhS, MxWhT>]
[MapProperty<MxWhS, MxWhT>("V2", "V2", When = nameof(Gate2))]
public partial class MxWhMixedMapper
{
    private static void Cfg(MapConfig<MxWhS, MxWhT> c) => c.MapWhen(t => t.V1, s => s.V1, Gate1);
    private static bool Gate1(MxWhS s) => s.Ok1;
    private static bool Gate2(MxWhS s) => s.Ok2;
}

// Op: ignore (`.Ignore`) — Extra1 ignored via config, Extra2 ignored via attribute.
public sealed class MxIgS { public int A { get; set; } }
public sealed class MxIgT { public int A { get; set; } public int Extra1 { get; set; } public int Extra2 { get; set; } }

[DwarfMapper]
[GenerateMap<MxIgS, MxIgT>]
public partial class MxIgConfigMapper
{
    private static void Cfg(MapConfig<MxIgS, MxIgT> c) => c.Ignore(t => t.Extra1).Ignore(t => t.Extra2);
}

[DwarfMapper]
[GenerateMap<MxIgS, MxIgT>]
[MapIgnore<MxIgT>("Extra1")]
[MapIgnore<MxIgT>("Extra2")]
public partial class MxIgAttrMapper { }

[DwarfMapper]
[GenerateMap<MxIgS, MxIgT>]
[MapIgnore<MxIgT>("Extra2")]
public partial class MxIgMixedMapper
{
    private static void Cfg(MapConfig<MxIgS, MxIgT> c) => c.Ignore(t => t.Extra1);
}

// Op: ignoresource (`.IgnoreSource`) — Unused1 silenced via config, Unused2 via attribute. Needs the
// explicit-partial-method front end (RequiredMapping=Both source-coverage only runs there — see the
// IgnoreSource_on_config_method_silences_DWARF039 generator test's note above).
public sealed class MxIsS { public int A { get; set; } public int Unused1 { get; set; } public int Unused2 { get; set; } }
public sealed class MxIsT { public int A { get; set; } }

[DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]
public partial class MxIsConfigMapper
{
    private static void Cfg(MapConfig<MxIsS, MxIsT> c) => c.IgnoreSource(s => s.Unused1).IgnoreSource(s => s.Unused2);
    public partial MxIsT Map(MxIsS s);
}

[DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]
public partial class MxIsAttrMapper
{
    [MapIgnoreSource(nameof(MxIsS.Unused1))]
    [MapIgnoreSource(nameof(MxIsS.Unused2))]
    public partial MxIsT Map(MxIsS s);
}

[DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]
public partial class MxIsMixedMapper
{
    private static void Cfg(MapConfig<MxIsS, MxIsT> c) => c.IgnoreSource(s => s.Unused1);
    [MapIgnoreSource(nameof(MxIsS.Unused2))]
    public partial MxIsT Map(MxIsS s);
}

// Op: value (`.Value` constant) — Tag1 via config, Tag2 via attribute.
public sealed class MxVcS { public int A { get; set; } }
public sealed class MxVcT { public int A { get; set; } public int Tag1 { get; set; } public int Tag2 { get; set; } }

[DwarfMapper]
[GenerateMap<MxVcS, MxVcT>]
public partial class MxVcConfigMapper
{
    private static void Cfg(MapConfig<MxVcS, MxVcT> c) => c.Value(t => t.Tag1, 99).Value(t => t.Tag2, 77);
}

[DwarfMapper]
[GenerateMap<MxVcS, MxVcT>]
[MapValue<MxVcT>("Tag1", 99)]
[MapValue<MxVcT>("Tag2", 77)]
public partial class MxVcAttrMapper { }

[DwarfMapper]
[GenerateMap<MxVcS, MxVcT>]
[MapValue<MxVcT>("Tag2", 77)]
public partial class MxVcMixedMapper
{
    private static void Cfg(MapConfig<MxVcS, MxVcT> c) => c.Value(t => t.Tag1, 99);
}

// Op: construct (`.Construct`) — a type-level (not per-member) op: the "mixed" mapper carries TWO
// GenerateMap pairs in one class (a supported pattern — see AsyncSpanQueryablePropertyTests' Item19Mapper),
// one built via config, the other via [MapConstructor<,>], proving the two mechanisms coexist without
// interference in a single mapper. Reuses CnS/CnAlias/CnConfigMapper/CnAttrMapper (config-form / attribute-
// form comparators) declared earlier in this file; adds a second attribute-only pair (CnS2/CnAlias2) plus
// the combined MixedMapper.
public sealed class CnS2 { public string Fmt { get; set; } = ""; public string Name { get; set; } = ""; }
public sealed class CnAlias2 : System.IEquatable<CnAlias2>
{
    public CnAlias2(string display) { Display = display; }
    public string Display { get; }
    public bool Equals(CnAlias2? other) => other is not null && Display == other.Display;
    public override bool Equals(object? obj) => Equals(obj as CnAlias2);
    public override int GetHashCode() => Display.GetHashCode(System.StringComparison.Ordinal);
}

[DwarfMapper]
[GenerateMap<CnS2, CnAlias2>]
[MapConstructor<CnS2, CnAlias2>(nameof(Make2))]
public partial class MxCnAttrMapper2
{
    private static CnAlias2 Make2(CnS2 s) => new($"{s.Fmt}:{s.Name}");
}

[DwarfMapper]
[GenerateMap<CnS, CnAlias>]
[GenerateMap<CnS2, CnAlias2>]
[MapConstructor<CnS2, CnAlias2>(nameof(Make2))]
public partial class MxCnMixedMapper
{
    private static void Cfg(MapConfig<CnS, CnAlias> c) => c.Construct(Make);
    private static CnAlias Make(CnS s) => new($"{s.Fmt}:{s.Name}");
    private static CnAlias2 Make2(CnS2 s) => new($"{s.Fmt}:{s.Name}");
}

// Op: nullsub (`.MapOr`) — V1 (value-type overload) via config, V2 (reference-type overload) via attribute.
public sealed class MxMoS { public int? V1 { get; set; } public string? V2 { get; set; } }
public sealed class MxMoT { public int V1 { get; set; } public string? V2 { get; set; } }

[DwarfMapper]
[GenerateMap<MxMoS, MxMoT>]
public partial class MxMoConfigMapper
{
    private static void Cfg(MapConfig<MxMoS, MxMoT> c) => c.MapOr(t => t.V1, s => s.V1, 5).MapOr(t => t.V2, s => s.V2, "none");
}

[DwarfMapper]
[GenerateMap<MxMoS, MxMoT>]
[MapProperty<MxMoS, MxMoT>("V1", "V1", NullSubstitute = 5)]
[MapProperty<MxMoS, MxMoT>("V2", "V2", NullSubstitute = "none")]
public partial class MxMoAttrMapper { }

[DwarfMapper]
[GenerateMap<MxMoS, MxMoT>]
[MapProperty<MxMoS, MxMoT>("V2", "V2", NullSubstitute = "none")]
public partial class MxMoMixedMapper
{
    private static void Cfg(MapConfig<MxMoS, MxMoT> c) => c.MapOr(t => t.V1, s => s.V1, 5);
}

// Step 4b fixtures: `.Value` int-literal-into-float-member — exercises RenderConstantLiteral's
// float/double/decimal target cast path through the MapConfig collector (widening int -> float).
public sealed class NwS { public int A { get; set; } }
public sealed class NwT { public int A { get; set; } public float F { get; set; } }

[DwarfMapper]
[GenerateMap<NwS, NwT>]
public partial class NwConfigMapper
{
    private static void Cfg(MapConfig<NwS, NwT> c) => c.Value(t => t.F, 5);
}

[DwarfMapper]
[GenerateMap<NwS, NwT>]
[MapValue<NwT>(nameof(NwT.F), 5)]
public partial class NwAttrMapper { }

public class MapConfigRuntimeTests
{
    private sealed class S { public string? Name { get; set; } public int Count { get; set; } }
    private sealed class T { public string? Full { get; set; } public int Total { get; set; } }

    [Fact]
    public void MapConfig_surface_compiles_and_chains()
    {
        // The type exists purely so selector lambdas type-check. It is never used by the generator at runtime;
        // this test only proves the fluent surface compiles and chains.
        var c = System.Activator.CreateInstance(typeof(MapConfig<S, T>), nonPublic: true) as MapConfig<S, T>;
        Assert.NotNull(c);
        var back = c!
            .Map(t => t.Total, s => s.Count)
            .Map(t => t.Full, s => s.Name)
            .Ignore(t => t.Full)
            .IgnoreSource(s => s.Name)
            .Value(t => t.Total, 7)
            .MapOr(t => t.Total, s => s.Count, 0);
        Assert.Same(c, back);
    }

    // Parity proof: a MapConfig<S,T> `.Map` rename must produce the same PairProp IR — and therefore the same
    // generated behaviour — as the pair-scoped [MapProperty<S,T>] attribute form for the identical rename.
    [Fact]
    public void MapConfig_Map_rename_matches_attribute_form()
    {
        var configResult = new McConfigMapper().Map(new McS { MessagesCount = 42 });
        var attrResult = new McAttrMapper().Map(new McS { MessagesCount = 42 });

        Assert.Equal(42, attrResult.NumberOfMessages);
        Assert.Equal(attrResult.NumberOfMessages, configResult.NumberOfMessages);
    }

    // Proves F2: a parenthesized single-parameter lambda `(t) => t.X` must be accepted by the collector,
    // not just the bare-parameter form `t => t.X`. Also exercises F1's receiver-type gate on a real config
    // method (the config body's `.Map(...)` receiver must resolve to MapConfig<PmS,PmT>).
    [Fact]
    public void MapConfig_parenthesized_lambda_matches_attribute_form()
    {
        var configResult = new PmConfigMapper().Map(new PmS { MessagesCount = 42 });
        var attrResult = new PmAttrMapper().Map(new PmS { MessagesCount = 42 });

        Assert.Equal(42, attrResult.NumberOfMessages);
        Assert.Equal(attrResult.NumberOfMessages, configResult.NumberOfMessages);
    }

    // Parity proof for `.Map` flatten: a dotted source selector (s => s.Author.Name) must produce the same
    // flattened PairProp IR as the pair-scoped [MapProperty<S,T>("Author.Name","AuthorName")] attribute form.
    [Fact]
    public void MapConfig_Map_flatten_matches_attribute_form()
    {
        var src = new FlS { Author = new Author { Name = "dwarf" } };
        var configResult = new FlConfigMapper().Map(src);
        var attrResult = new FlAttrMapper().Map(src);

        Assert.Equal("dwarf", attrResult.AuthorName);
        Assert.Equal(attrResult.AuthorName, configResult.AuthorName);
    }

    // Parity proof for `.Map` converter method group: config-form 3-arg `.Map(t => .., s => .., Norm)` must
    // produce the same converted output as [MapProperty<S,T>("Raw","Clean", Use = nameof(Norm))].
    [Fact]
    public void MapConfig_Map_converter_matches_attribute_form()
    {
        var configResult = new CvConfigMapper().Map(new CvS { Raw = "  x  " });
        var attrResult = new CvAttrMapper().Map(new CvS { Raw = "  x  " });

        Assert.Equal("x", attrResult.Clean);
        Assert.Equal(attrResult.Clean, configResult.Clean);
    }

    // Parity proof for `.MapWhen`: config-form conditional assignment must match [MapProperty<S,T>("V","V",
    // When = nameof(Gate))] in both the gated (false) and ungated (true) cases.
    [Fact]
    public void MapConfig_MapWhen_matches_attribute_form()
    {
        var configOff = new WhConfigMapper().Map(new WhS { V = 9, Ok = false });
        var attrOff = new WhAttrMapper().Map(new WhS { V = 9, Ok = false });
        Assert.Equal(0, attrOff.V);
        Assert.Equal(attrOff.V, configOff.V);

        var configOn = new WhConfigMapper().Map(new WhS { V = 9, Ok = true });
        var attrOn = new WhAttrMapper().Map(new WhS { V = 9, Ok = true });
        Assert.Equal(9, attrOn.V);
        Assert.Equal(attrOn.V, configOn.V);
    }

    // Proves `.Ignore`: without it, IgT.Extra has no matching source member and the project fails to build
    // with DWARF001 (that is this op's RED). With the branch, the class compiles and Extra defaults to 0.
    [Fact]
    public void MapConfig_Ignore_suppresses_completeness_and_defaults_member()
    {
        var result = new IgConfigMapper().Map(new IgS { A = 3 });
        Assert.Equal(3, result.A);
        Assert.Equal(0, result.Extra);
    }

    // Parity proof for `.Construct`: config-form factory method group must build the same destination as
    // [MapConstructor<S,T>(nameof(Make))].
    [Fact]
    public void MapConfig_Construct_matches_attribute_form()
    {
        var src = new CnS { Fmt = "F", Name = "N" };
        var configResult = new CnConfigMapper().Map(src);
        var attrResult = new CnAttrMapper().Map(src);

        Assert.Equal("F:N", attrResult.Display);
        Assert.Equal(attrResult, configResult);
    }

    // Parity proof for `.MapOr` (value-type / struct-constrained overload): config-form null-substitute must
    // match [MapProperty<S,T>("V","V", NullSubstitute = 5)] for both a null source and a present value.
    [Fact]
    public void MapConfig_MapOr_value_type_matches_attribute_form()
    {
        var configNull = new MoConfigMapper().Map(new MoS { V = null });
        var attrNull = new MoAttrMapper().Map(new MoS { V = null });
        Assert.Equal(5, attrNull.V);
        Assert.Equal(attrNull.V, configNull.V);

        var configVal = new MoConfigMapper().Map(new MoS { V = 9 });
        var attrVal = new MoAttrMapper().Map(new MoS { V = 9 });
        Assert.Equal(9, attrVal.V);
        Assert.Equal(attrVal.V, configVal.V);
    }

    // Parity proof for `.MapOr` (reference-type / class-constrained overload) — closes the Task-1 gap where
    // only the struct-constrained overload was exercised.
    [Fact]
    public void MapConfig_MapOr_reference_type_matches_attribute_form()
    {
        var configNull = new MoRConfigMapper().Map(new MoRS { Name = null });
        var attrNull = new MoRAttrMapper().Map(new MoRS { Name = null });
        Assert.Equal("none", attrNull.Name);
        Assert.Equal(attrNull.Name, configNull.Name);

        var configVal = new MoRConfigMapper().Map(new MoRS { Name = "x" });
        var attrVal = new MoRAttrMapper().Map(new MoRS { Name = "x" });
        Assert.Equal("x", attrVal.Name);
        Assert.Equal(attrVal.Name, configVal.Name);
    }

    // Parity proof for `.Value` constant form: config-form constant must match [MapValue<T>(target, constant)].
    [Fact]
    public void MapConfig_Value_constant_matches_attribute_form()
    {
        var configResult = new VcConfigMapper().Map(new VcS { A = 1 });
        var attrResult = new VcAttrMapper().Map(new VcS { A = 1 });
        Assert.Equal(99, attrResult.Tag);
        Assert.Equal(attrResult.Tag, configResult.Tag);
    }

    // Parity proof for `.Value` compute (method-group) form: config-form must match [MapValue<T>(target){ Use = }].
    [Fact]
    public void MapConfig_Value_compute_matches_attribute_form()
    {
        var configResult = new VcComputeConfigMapper().Map(new VcS { A = 1 });
        var attrResult = new VcComputeAttrMapper().Map(new VcS { A = 1 });
        Assert.Equal(7, attrResult.Tag);
        Assert.Equal(attrResult.Tag, configResult.Tag);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // Combination matrix (plan Task 14): attribute-only, config-only, and MIXED must all agree,
    // for every one of the 9 ops. See the Mx*-prefixed fixtures declared earlier in this file.
    // ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Matrix_rename_attribute_config_and_mixed_agree()
    {
        var src = new MxRnS { A = 11, B = 22 };
        var cfg = new MxRnConfigMapper().Map(src);
        var attr = new MxRnAttrMapper().Map(src);
        var mixed = new MxRnMixedMapper().Map(src);

        Assert.Equal(11, cfg.X);
        Assert.Equal(22, cfg.Y);
        Assert.Equal(cfg.X, attr.X);
        Assert.Equal(cfg.Y, attr.Y);
        Assert.Equal(cfg.X, mixed.X);
        Assert.Equal(cfg.Y, mixed.Y);
    }

    [Fact]
    public void Matrix_flatten_attribute_config_and_mixed_agree()
    {
        var src = new MxFlS { A = new MxFlA { Name = "dwarf" }, B = new MxFlB { Nick = "gimli" } };
        var cfg = new MxFlConfigMapper().Map(src);
        var attr = new MxFlAttrMapper().Map(src);
        var mixed = new MxFlMixedMapper().Map(src);

        Assert.Equal("dwarf", cfg.AName);
        Assert.Equal("gimli", cfg.BNick);
        Assert.Equal(cfg.AName, attr.AName);
        Assert.Equal(cfg.BNick, attr.BNick);
        Assert.Equal(cfg.AName, mixed.AName);
        Assert.Equal(cfg.BNick, mixed.BNick);
    }

    [Fact]
    public void Matrix_convert_attribute_config_and_mixed_agree()
    {
        var src = new MxCvS { Raw1 = "  x  ", Raw2 = "  y  " };
        var cfg = new MxCvConfigMapper().Map(src);
        var attr = new MxCvAttrMapper().Map(src);
        var mixed = new MxCvMixedMapper().Map(src);

        Assert.Equal("x", cfg.Clean1);
        Assert.Equal("y", cfg.Clean2);
        Assert.Equal(cfg.Clean1, attr.Clean1);
        Assert.Equal(cfg.Clean2, attr.Clean2);
        Assert.Equal(cfg.Clean1, mixed.Clean1);
        Assert.Equal(cfg.Clean2, mixed.Clean2);
    }

    [Fact]
    public void Matrix_when_attribute_config_and_mixed_agree()
    {
        var srcOn = new MxWhS { V1 = 9, Ok1 = true, V2 = 7, Ok2 = true };
        var cfgOn = new MxWhConfigMapper().Map(srcOn);
        var attrOn = new MxWhAttrMapper().Map(srcOn);
        var mixedOn = new MxWhMixedMapper().Map(srcOn);
        Assert.Equal(9, cfgOn.V1);
        Assert.Equal(7, cfgOn.V2);
        Assert.Equal(cfgOn.V1, attrOn.V1);
        Assert.Equal(cfgOn.V2, attrOn.V2);
        Assert.Equal(cfgOn.V1, mixedOn.V1);
        Assert.Equal(cfgOn.V2, mixedOn.V2);

        var srcOff = new MxWhS { V1 = 9, Ok1 = false, V2 = 7, Ok2 = false };
        var cfgOff = new MxWhConfigMapper().Map(srcOff);
        var attrOff = new MxWhAttrMapper().Map(srcOff);
        var mixedOff = new MxWhMixedMapper().Map(srcOff);
        Assert.Equal(0, cfgOff.V1);
        Assert.Equal(0, cfgOff.V2);
        Assert.Equal(cfgOff.V1, attrOff.V1);
        Assert.Equal(cfgOff.V2, attrOff.V2);
        Assert.Equal(cfgOff.V1, mixedOff.V1);
        Assert.Equal(cfgOff.V2, mixedOff.V2);
    }

    // Ignore/IgnoreSource per the brief: the assertion is "all three compile with no DWARF001/DWARF039
    // and the mapped members agree" — the very fact these fixtures build (no DWARF001/DWARF039 build
    // failure) is the compile-time half of that assertion; the runtime half follows.
    [Fact]
    public void Matrix_ignore_attribute_config_and_mixed_agree()
    {
        var src = new MxIgS { A = 3 };
        var cfg = new MxIgConfigMapper().Map(src);
        var attr = new MxIgAttrMapper().Map(src);
        var mixed = new MxIgMixedMapper().Map(src);

        Assert.Equal(3, cfg.A);
        Assert.Equal(cfg.A, attr.A);
        Assert.Equal(cfg.A, mixed.A);
        Assert.Equal(0, cfg.Extra1);
        Assert.Equal(0, cfg.Extra2);
        Assert.Equal(0, attr.Extra1);
        Assert.Equal(0, attr.Extra2);
        Assert.Equal(0, mixed.Extra1);
        Assert.Equal(0, mixed.Extra2);
    }

    [Fact]
    public void Matrix_ignoresource_attribute_config_and_mixed_agree()
    {
        var src = new MxIsS { A = 5, Unused1 = 1, Unused2 = 2 };
        var cfg = new MxIsConfigMapper().Map(src);
        var attr = new MxIsAttrMapper().Map(src);
        var mixed = new MxIsMixedMapper().Map(src);

        Assert.Equal(5, cfg.A);
        Assert.Equal(cfg.A, attr.A);
        Assert.Equal(cfg.A, mixed.A);
    }

    [Fact]
    public void Matrix_value_attribute_config_and_mixed_agree()
    {
        var src = new MxVcS { A = 1 };
        var cfg = new MxVcConfigMapper().Map(src);
        var attr = new MxVcAttrMapper().Map(src);
        var mixed = new MxVcMixedMapper().Map(src);

        Assert.Equal(99, cfg.Tag1);
        Assert.Equal(77, cfg.Tag2);
        Assert.Equal(cfg.Tag1, attr.Tag1);
        Assert.Equal(cfg.Tag2, attr.Tag2);
        Assert.Equal(cfg.Tag1, mixed.Tag1);
        Assert.Equal(cfg.Tag2, mixed.Tag2);
    }

    // Construct is type-level (the whole destination comes from the factory), so "mixed" here means the
    // SAME mapper class carries one pair built via config and a second, distinct pair built via attribute
    // — see the comment above MxCnMixedMapper's declaration.
    [Fact]
    public void Matrix_construct_attribute_config_and_mixed_agree()
    {
        var src = new CnS { Fmt = "F", Name = "N" };
        var cfgOnly = new CnConfigMapper().Map(src);
        var mixedFirstPair = new MxCnMixedMapper().Map(src);
        Assert.Equal("F:N", cfgOnly.Display);
        Assert.Equal(cfgOnly, mixedFirstPair);

        var src2 = new CnS2 { Fmt = "G", Name = "M" };
        var attrOnly = new MxCnAttrMapper2().Map(src2);
        var mixedSecondPair = new MxCnMixedMapper().Map(src2);
        Assert.Equal("G:M", attrOnly.Display);
        Assert.Equal(attrOnly, mixedSecondPair);
    }

    [Fact]
    public void Matrix_nullsub_attribute_config_and_mixed_agree()
    {
        var srcNull = new MxMoS { V1 = null, V2 = null };
        var cfgNull = new MxMoConfigMapper().Map(srcNull);
        var attrNull = new MxMoAttrMapper().Map(srcNull);
        var mixedNull = new MxMoMixedMapper().Map(srcNull);
        Assert.Equal(5, cfgNull.V1);
        Assert.Equal("none", cfgNull.V2);
        Assert.Equal(cfgNull.V1, attrNull.V1);
        Assert.Equal(cfgNull.V2, attrNull.V2);
        Assert.Equal(cfgNull.V1, mixedNull.V1);
        Assert.Equal(cfgNull.V2, mixedNull.V2);

        var srcVal = new MxMoS { V1 = 9, V2 = "x" };
        var cfgVal = new MxMoConfigMapper().Map(srcVal);
        var attrVal = new MxMoAttrMapper().Map(srcVal);
        var mixedVal = new MxMoMixedMapper().Map(srcVal);
        Assert.Equal(9, cfgVal.V1);
        Assert.Equal("x", cfgVal.V2);
        Assert.Equal(cfgVal.V1, attrVal.V1);
        Assert.Equal(cfgVal.V2, attrVal.V2);
        Assert.Equal(cfgVal.V1, mixedVal.V1);
        Assert.Equal(cfgVal.V2, mixedVal.V2);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // Step 4b: numeric-widening parity for the renderer target-type reuse (RenderConstantLiteral
    // called with the constant's OWN type reused as the destination member's type — see the comment
    // at both `.MapOr`/`.Value` collector call sites in MapperExtractor.MapConfig.cs). An int literal
    // (5) assigned into a `float` member exercises exactly the float-target cast path.
    // ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MapConfig_Value_int_literal_into_float_member_matches_attribute_form()
    {
        var configResult = new NwConfigMapper().Map(new NwS { A = 1 });
        var attrResult = new NwAttrMapper().Map(new NwS { A = 1 });
        Assert.Equal(5f, attrResult.F);
        Assert.Equal(attrResult.F, configResult.F);
    }
}
