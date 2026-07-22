// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Framework;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Golden;

/// <summary>
///     Proves each feature case EXERCISES the feature it is named for. A case that silently generates nothing
///     still yields a valid fingerprint, so once Task 6 pins it the gap becomes permanent and invisible — the
///     corpus would advertise coverage it does not have.
/// </summary>
public class GoldenFeatureCoverageTests
{
    /// <summary>featureId -> a marker that can only appear if the feature actually fired.</summary>
    public static TheoryData<string, string> FeatureMarkers() => new()
    {
        { "Basic", "X = a.X" },
        { "UpdateInto", "void Update(" },
        { "Projection", "global::System.Linq.Queryable.Select(" },
        { "SpanMap", "for (int __i = 0; __i < src.Length; __i++)" },
        { "AsyncStream", "await foreach" },
        { "FlattenGraph", "__DwarfMap_FlattenGraph" },
        { "Flatten", "City = " },
        { "ConstructorMapping", "new global::Demo.B(" },
        { "EnumByName", "Unmapped enum value" },
        { "EnumByValue", ".CreateChecked(" },
        { "FlagsEnumFromString", "MemoryExtensions" },
        { "NullStrategyThrow", "throw" },
        { "NullStrategySetDefault", ".GetValueOrDefault()" },
        { "PreserveReferences", "TryGetReference" },
        { "DerivedTypes", "ADerived __s =>" },
        { "Hooks", "After(a, __dwarf_target);" },
        { "ReverseMap", "public void VerifyRoundTrip_ToB" },
        { "CoLocatedGenerateMap", "partial class BMapper" },
        { "RegistryBasic", "ToDto" },
        { "RegistryCollection", "__DwarfMapColl_" },
        { "RegistryNested", "__DwarfMapObj_" },
    };

    [Theory]
    [MemberData(nameof(FeatureMarkers))]
    public void Feature_case_generates_output_that_proves_the_feature_fired(string featureId, string marker)
    {
        var c = GoldenCorpus.Cases().Single(x => x.Id == "feat:" + featureId);
        var generator = GeneratorRegistry.All.Single(g => g.Name == c.GeneratorName);
        var run = GeneratorRunner.Run(generator.Create(), c.Source);
        var output = run.AllOutputsConcatenated;

        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.False(string.IsNullOrWhiteSpace(output),
            $"Feature case '{featureId}' generated NOTHING. Pinned into the manifest it would pass forever "
            + "while covering nothing.");
        Assert.True(output.Contains(marker, StringComparison.Ordinal),
            $"Feature case '{featureId}' generated output that does not contain '{marker}', so the feature "
            + "did not fire. Fix the CASE SOURCE (check the attribute/signature shape against the existing "
            + "dedicated tests for that feature) — do not delete this assertion.\n\n--- generated ---\n"
            + output);
    }

    [Fact]
    public void Every_feature_case_in_the_corpus_has_a_marker()
    {
        // Otherwise a new feature case could be added and never proved to fire.
        var corpusIds = GoldenCorpus.Cases()
            .Where(c => c.Id.StartsWith("feat:", StringComparison.Ordinal))
            .Select(c => c.Id["feat:".Length..])
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var markered = FeatureMarkers().Select(row => (string)row[0])
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.True(corpusIds.SequenceEqual(markered, StringComparer.Ordinal),
            "Feature cases and markers are out of sync.\n  corpus:  " + string.Join(", ", corpusIds)
            + "\n  markers: " + string.Join(", ", markered));
    }
}
