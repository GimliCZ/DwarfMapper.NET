// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using DwarfMapper.Generator.Tests.Framework;

namespace DwarfMapper.Generator.Tests.Golden;

/// <summary>
///     The refactoring safety net. Sub-projects 2-4 (shared engine core, emission layer, extractor split) are
///     behaviour-preserving restructurings; this is what proves it. Every pinned case's generated output and
///     diagnostics are fingerprinted and compared against the committed manifest.
/// </summary>
public class GoldenCorpusTests
{
    /// <summary>
    ///     Floor for the manifest size. Lowering it is a deliberate act needing justification — a corpus that
    ///     silently shrinks is the "green but checking nothing" failure this repo keeps finding.
    /// </summary>
    private const int MinimumCases = 850;

    // Per-axis / per-generator floors. A single total floor cannot catch one axis collapsing to a degenerate
    // case, because the ~910 combinatorial cases mask everything else. Actual counts observed when these floors
    // were chosen: total=971, cmb=910, syn=40, feat=21, DwarfGenerator=968, MapToGenerator=3. Floors are set a
    // little below the observed counts (MapToGenerator is kept at its observed floor: with only 3 registry
    // cases — Basic/Collection/Nested — going lower would mean losing an entire registry shape, not slack).
    private const int MinimumCombinatorialCases = 890;
    private const int MinimumSyntheticCases = 35;
    private const int MinimumFeatureCases = 18;
    private const int MinimumDwarfGeneratorCases = 950;
    private const int MinimumMapToGeneratorCases = 3;

    [Fact]
    public void Generated_output_matches_the_golden_manifest()
    {
        var cases = GoldenCorpus.Cases();
        var actual = cases.ToDictionary(c => c.Id, GoldenFingerprint.Compute, StringComparer.Ordinal);

        if (Environment.GetEnvironmentVariable(GoldenManifest.UpdateEnvVar) == "1")
        {
            Assert.False(IsRunningInCi(),
                $"{GoldenManifest.UpdateEnvVar}=1 was set in a CI environment. CI must not be able to bless a "
                + "regenerated manifest — that silently rubber-stamps whatever the generator currently produces. "
                + "Regenerate the manifest locally, review the diff, and commit it deliberately.");

            GoldenManifest.Write(actual);
            return;
        }

        var expected = GoldenManifest.Load();

        Assert.True(expected.Count > 0,
            $"No golden manifest at {GoldenManifest.Path}. It is never auto-created — generate it deliberately "
            + $"with {GoldenManifest.UpdateEnvVar}=1 and commit the result after reviewing it.");

        var added = actual.Keys.Where(k => !expected.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var removed = expected.Keys.Where(k => !actual.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var changed = actual.Where(kv => expected.TryGetValue(kv.Key, out var e) && e != kv.Value)
            .Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.True(added.Count == 0 && removed.Count == 0 && changed.Count == 0,
            BuildFailure(added, removed, changed));
    }

    [Fact]
    public void The_corpus_has_not_silently_shrunk()
    {
        var cases = GoldenCorpus.Cases();

        Assert.True(cases.Count >= MinimumCases,
            $"Golden corpus has {cases.Count} cases, floor is {MinimumCases}. A shrinking corpus passes every "
            + "other test while covering less — lower the floor only deliberately.");

        // Per-axis / per-generator floors: one axis collapsing to a single degenerate case must fail even
        // though the combinatorial axis alone dwarfs the total-count floor above.
        AssertAtLeast(cases.Count(c => c.Id.StartsWith("cmb:", StringComparison.Ordinal)),
            MinimumCombinatorialCases, "combinatorial (\"cmb:\")");
        AssertAtLeast(cases.Count(c => c.Id.StartsWith("syn:", StringComparison.Ordinal)),
            MinimumSyntheticCases, "synthetic (\"syn:\")");
        AssertAtLeast(cases.Count(c => c.Id.StartsWith("feat:", StringComparison.Ordinal)),
            MinimumFeatureCases, "feature (\"feat:\")");
        AssertAtLeast(cases.Count(c => c.GeneratorName == "DwarfGenerator"),
            MinimumDwarfGeneratorCases, "generator \"DwarfGenerator\"");
        AssertAtLeast(cases.Count(c => c.GeneratorName == "MapToGenerator"),
            MinimumMapToGeneratorCases, "generator \"MapToGenerator\"");
    }

    /// <summary>
    ///     Best-effort CI detection. These three env vars are set unconditionally by GitHub Actions, Azure
    ///     Pipelines, and effectively every other CI provider (the generic "CI" convention); checking all three
    ///     covers this repo's actual and plausible future CI hosts without depending on any one of them.
    /// </summary>
    private static bool IsRunningInCi()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
               || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD"))
               || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
    }

    private static void AssertAtLeast(int actual, int floor, string axisName)
    {
        Assert.True(actual >= floor,
            $"Axis {axisName} has {actual} cases, floor is {floor}. One axis collapsing hides behind a "
            + "healthy total count — lower the floor only deliberately.");
    }

    private static string BuildFailure(List<string> added, List<string> removed, List<string> changed)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"Golden output moved. {changed.Count} changed, {added.Count} added, {removed.Count} removed.\n");

        if (changed.Count > 0) sb.Append("CHANGED:\n  ").AppendJoin("\n  ", changed).Append('\n');
        if (added.Count > 0) sb.Append("ADDED (new case needs review):\n  ").AppendJoin("\n  ", added).Append('\n');
        if (removed.Count > 0) sb.Append("REMOVED (corpus shrank):\n  ").AppendJoin("\n  ", removed).Append('\n');

        sb.Append('\n')
            .Append("If this change is INTENTIONAL: review the curated snapshots in Snapshots/ (they show real\n")
            .Append("text; hashes do not diff readably), then regenerate with ")
            .Append(GoldenManifest.UpdateEnvVar).Append("=1 and commit.\n")
            .Append("If it is NOT intentional, a refactor changed observable output — that is the bug.\n");

        return sb.ToString();
    }
}
