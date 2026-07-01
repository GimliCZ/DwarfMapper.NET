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
}
