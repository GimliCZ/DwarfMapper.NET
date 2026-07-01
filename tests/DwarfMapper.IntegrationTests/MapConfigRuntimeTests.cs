// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests;

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
}
