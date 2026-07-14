// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.Fuzzing;

/// <summary>
///     Generator DETERMINISM property: running the generator twice over the same source must produce
///     byte-for-byte identical output. Every other test in the suite runs the generator exactly once, so a
///     non-deterministic emission (unordered iteration over symbols/Dictionary/HashSet, parallel member
///     ordering, etc.) would be invisible to them yet would produce flaky Verify snapshots and
///     non-reproducible builds. This is the cheap, additive guard against that whole class of Roslyn-generator
///     bugs. It reuses the existing fuzz seed generators so it covers the same broad feature surface as the
///     behavioural/combinatorial fuzz, but asserts a different invariant (output stability, not value/compile).
/// </summary>
public class DeterminismFuzzTests
{
    // Broad-loop determinism: the same byte-identical double-run check across a wide contiguous seed range
    // (not just the hand-picked seeds above), so output stability is verified over the same broad surface the
    // compile/behavioural fuzz covers — not a thin sample.
    public static IEnumerable<object[]> BroadSeeds =>
        Enumerable.Range(0, 120).Select(i => new object[] { i });

    // GenerateBehavioral covers scalars, conversions, nullable, collections, dictionaries, enums, nesting.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(13)]
    [InlineData(29)]
    [InlineData(50)]
    [InlineData(101)]
    [InlineData(255)]
    public void Behavioral_schema_generates_byte_identical_output_twice(int seed)
    {
        var src = SyntheticSchema.GenerateBehavioral(seed);
        var (d1, g1) = GeneratorTestHarness.Run(src);
        var (d2, g2) = GeneratorTestHarness.Run(src);
        Assert.Equal(g1, g2);
        // Diagnostics order/content must also be stable run-to-run.
        Assert.Equal(
            string.Join("|", Enumerable.Select(d1, x => x.Id + "@" + x.Location.GetLineSpan().StartLinePosition)),
            string.Join("|", Enumerable.Select(d2, x => x.Id + "@" + x.Location.GetLineSpan().StartLinePosition)));
    }

    // GenerateWithAdvancedFeatures rotates through MapDerivedType, FlattenGraph, and top-level collections.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(11)]
    [InlineData(64)]
    public void Advanced_feature_schema_generates_byte_identical_output_twice(int seed)
    {
        var src = SyntheticSchema.GenerateWithAdvancedFeatures(seed);
        var (_, g1) = GeneratorTestHarness.Run(src);
        var (_, g2) = GeneratorTestHarness.Run(src);
        Assert.Equal(g1, g2);
    }

    // A reference-handling / cycle source: Preserve and SetNull thread per-call dictionaries and synthesize
    // helpers — the most likely place for unordered-iteration non-determinism to surface.
    [Theory]
    [InlineData("ReferenceHandling = ReferenceHandlingStrategy.Preserve")]
    [InlineData("OnCycle = OnCycleStrategy.SetNull")]
    public void Reference_handling_source_is_deterministic(string option)
    {
        var src = $$"""
                    using DwarfMapper;
                    namespace Demo;
                    public class Node { public int V { get; set; } public Node? Next { get; set; } public System.Collections.Generic.List<Node> Kids { get; set; } = new(); }
                    public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } public System.Collections.Generic.List<NodeDto> Kids { get; set; } = new(); }
                    [DwarfMapper({{option}})] public partial class M { public partial NodeDto Map(Node n); }
                    """;
        var (_, g1) = GeneratorTestHarness.Run(src);
        var (_, g2) = GeneratorTestHarness.Run(src);
        Assert.Equal(g1, g2);
    }

    [Theory]
    [MemberData(nameof(BroadSeeds))]
    public void Behavioral_schema_is_byte_identical_across_a_broad_seed_range(int seed)
    {
        var src = SyntheticSchema.GenerateBehavioral(seed);
        var (_, g1) = GeneratorTestHarness.Run(src);
        var (_, g2) = GeneratorTestHarness.Run(src);
        Assert.Equal(g1, g2);
    }
}
