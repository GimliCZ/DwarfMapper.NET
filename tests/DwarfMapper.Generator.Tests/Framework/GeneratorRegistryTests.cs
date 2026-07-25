// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.Framework;

public class GeneratorRegistryTests
{
    [Fact]
    public void Both_generators_are_registered()
    {
        Assert.Equal(2, GeneratorRegistry.All.Count);
        Assert.Contains(GeneratorRegistry.All, g => g.Name == "DwarfGenerator");
        Assert.Contains(GeneratorRegistry.All, g => g.Name == "MapToGenerator");
    }

    [Fact]
    public void Every_registered_generator_declares_at_least_one_tracking_name()
    {
        // A generator with no tracking name cannot be addressed by a cacheability test at all — which is
        // exactly why MapToGenerator had zero caching coverage while DwarfGenerator had six tests.
        Assert.All(GeneratorRegistry.All, g => Assert.NotEmpty(g.TrackingNames));
    }

    [Fact]
    public void Every_declared_tracking_name_actually_appears_in_a_run()
    {
        // Also includes a co-located [GenerateMap<>] host (CoLoc) so DwarfGenerator's co-located extraction
        // step actually runs: ForAttributeWithMetadataName's fast path never tracks a step at all — not even
        // with zero outputs — when the compilation has no syntax node bearing that attribute anywhere.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class A { public int X { get; set; } }
                           public class B { public int X { get; set; } }
                           [DwarfMapper] public partial class M { public partial B Map(A a); }
                           [MapTo(typeof(B))] public class Src { public int X { get; set; } }
                           [GenerateMap<A, CoLoc>] public sealed class CoLoc { public int X { get; set; } }
                           """;

        foreach (var g in GeneratorRegistry.All)
        {
            var compilation = GeneratorTestHarness.BuildCompilation("TrackAsm", src);
            var driver = GeneratorRunner.RunTracked(g.Create()).RunGenerators(compilation);
            var tracked = driver.GetRunResult().Results[0].TrackedSteps;

            foreach (var name in g.TrackingNames)
                Assert.True(tracked.ContainsKey(name),
                    $"{g.Name} declares tracking name '{name}' but no such step ran. Either the WithTrackingName "
                    + "call is missing or the name is stale.");
        }
    }
}
