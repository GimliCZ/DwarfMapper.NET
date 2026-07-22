// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Framework;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Readable counterparts to the golden manifest. The manifest gives breadth but its hashes do not diff, so
///     when an intentional emission change lands these are what a human actually reads to review it.
/// </summary>
public partial class SnapshotSuite
{
    private static string GoldenFeature(string featureId)
    {
        var c = GoldenCorpus.Cases().Single(x => x.Id == "feat:" + featureId);
        var generator = GeneratorRegistry.All.Single(g => g.Name == c.GeneratorName);

        return GeneratorRunner.Run(generator.Create(), c.Source).AllOutputsConcatenated;
    }

    [Fact] public Task Snap_Golden_Basic() { return Verify(GoldenFeature("Basic")); }
    [Fact] public Task Snap_Golden_UpdateInto() { return Verify(GoldenFeature("UpdateInto")); }
    [Fact] public Task Snap_Golden_Projection() { return Verify(GoldenFeature("Projection")); }
    [Fact] public Task Snap_Golden_SpanMap() { return Verify(GoldenFeature("SpanMap")); }
    [Fact] public Task Snap_Golden_AsyncStream() { return Verify(GoldenFeature("AsyncStream")); }
    [Fact] public Task Snap_Golden_FlattenGraph() { return Verify(GoldenFeature("FlattenGraph")); }
    [Fact] public Task Snap_Golden_Flatten() { return Verify(GoldenFeature("Flatten")); }
    [Fact] public Task Snap_Golden_ConstructorMapping() { return Verify(GoldenFeature("ConstructorMapping")); }
    [Fact] public Task Snap_Golden_EnumByName() { return Verify(GoldenFeature("EnumByName")); }
    [Fact] public Task Snap_Golden_EnumByValue() { return Verify(GoldenFeature("EnumByValue")); }
    [Fact] public Task Snap_Golden_FlagsEnumFromString() { return Verify(GoldenFeature("FlagsEnumFromString")); }
    [Fact] public Task Snap_Golden_NullStrategyThrow() { return Verify(GoldenFeature("NullStrategyThrow")); }
    [Fact] public Task Snap_Golden_NullStrategySetDefault() { return Verify(GoldenFeature("NullStrategySetDefault")); }
    [Fact] public Task Snap_Golden_PreserveReferences() { return Verify(GoldenFeature("PreserveReferences")); }
    [Fact] public Task Snap_Golden_DerivedTypes() { return Verify(GoldenFeature("DerivedTypes")); }
    [Fact] public Task Snap_Golden_Hooks() { return Verify(GoldenFeature("Hooks")); }
    [Fact] public Task Snap_Golden_ReverseMap() { return Verify(GoldenFeature("ReverseMap")); }
    [Fact] public Task Snap_Golden_CoLocatedGenerateMap() { return Verify(GoldenFeature("CoLocatedGenerateMap")); }
    [Fact] public Task Snap_Golden_RegistryBasic() { return Verify(GoldenFeature("RegistryBasic")); }
    [Fact] public Task Snap_Golden_RegistryCollection() { return Verify(GoldenFeature("RegistryCollection")); }
    [Fact] public Task Snap_Golden_RegistryNested() { return Verify(GoldenFeature("RegistryNested")); }
}
