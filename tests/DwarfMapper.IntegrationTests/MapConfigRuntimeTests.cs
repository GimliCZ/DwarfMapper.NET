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
}
