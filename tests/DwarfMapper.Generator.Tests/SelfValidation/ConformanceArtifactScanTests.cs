// SPDX-License-Identifier: GPL-2.0-only

using System.IO;
using System.Text.RegularExpressions;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Keeps the conformance sample, its published figure, and its dated artifacts honest about each other.
///     <para>
///         The README advertised "<b>48</b> runtime assertions" against a sample containing 47 — a
///         hand-maintained number that drifted the moment the sample changed, and that nothing could catch
///         because no test knew the two were supposed to agree. The gate script (<c>scripts/conformance-gate.sh</c>)
///         enforces the ratchet in CI; these tests enforce the same agreement locally, so the drift is caught
///         at the point it is introduced rather than one push later.
///     </para>
/// </summary>
public class ConformanceArtifactScanTests
{
    private static string ConformanceProgram =>
        Path.Combine(RepoRoot, "samples", "DwarfMapper.Conformance", "Program.cs");

    private static string ResultsDir => Path.Combine(RepoRoot, "conformance", "results");

    /// <summary>
    ///     Counts the assertion CALLS, excluding the helper's own definition. Getting this wrong in the
    ///     obvious direction is how "48" happened: a naive count of <c>Check(</c> also matches the method
    ///     declaration in the helper file.
    /// </summary>
    private static int ActualAssertionCount()
    {
        var text = File.ReadAllText(ConformanceProgram);
        return Regex.Count(text, @"\bR\.Check\s*\(");
    }

    [Fact]
    public void The_README_assertion_figure_matches_the_sample()
    {
        var readme = File.ReadAllText(Path.Combine(RepoRoot, "README.md"));
        var actual = ActualAssertionCount();

        Assert.True(actual > 0,
            "No R.Check( calls found in the conformance sample — this scan would compare against nothing.");

        var quoted = Regex.Matches(readme, @"\*\*(?<n>\d+) runtime assertions\*\*")
            .Select(m => int.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        Assert.True(quoted.Count > 0,
            "README no longer states a runtime-assertion figure. If the claim was removed deliberately, "
            + "remove this test with it; otherwise the sample's size is now unadvertised.");

        Assert.All(quoted, n => Assert.True(n == actual,
            $"README advertises {n} runtime assertions but the conformance sample contains {actual}. "
            + "The figure must be derived from the sample, not maintained by hand — it was wrong by one "
            + "until this test existed."));
    }

    [Fact]
    public void Every_conformance_artifact_is_well_formed()
    {
        // Artifacts are the gate's EVIDENCE. One that cannot be parsed cannot be ratcheted against, so a
        // malformed file would silently disable the "count may only grow" rule in conformance-gate.sh.
        if (!Directory.Exists(ResultsDir)) return; // nothing published yet — the gate creates it on first run

        var artifacts = Directory.EnumerateFiles(ResultsDir, "*.md").ToList();
        foreach (var artifact in artifacts)
        {
            var text = File.ReadAllText(artifact);
            var name = Path.GetFileName(artifact);

            Assert.True(Regex.IsMatch(name, @"^\d{4}-\d{2}-\d{2}-[a-z0-9-]+\.md$"),
                $"Artifact '{name}' is not named <date>-<rid>.md — the gate sorts by name to find the "
                + "previous run, so an off-pattern name breaks the ratchet.");

            Assert.True(Regex.IsMatch(text, @"^- \*\*Assertions:\*\* \d+\s*$", RegexOptions.Multiline),
                $"Artifact '{name}' has no parseable assertion count; conformance-gate.sh reads exactly this "
                + "line to enforce that coverage only grows.");

            Assert.True(text.Contains("- **Commit:**", StringComparison.Ordinal),
                $"Artifact '{name}' records no commit — the gate compares it against the build commit so a "
                + "stale artifact cannot stand in for a run that did not happen.");
        }
    }

    [Fact]
    public void The_latest_artifact_agrees_with_the_sample()
    {
        if (!Directory.Exists(ResultsDir)) return;

        var latest = Directory.EnumerateFiles(ResultsDir, "*.md")
            .OrderBy(p => p, StringComparer.Ordinal)
            .LastOrDefault();
        if (latest is null) return;

        var recorded = Regex.Match(File.ReadAllText(latest), @"- \*\*Assertions:\*\* (?<n>\d+)");
        Assert.True(recorded.Success, $"Could not read the assertion count from {Path.GetFileName(latest)}.");

        Assert.Equal(
            ActualAssertionCount(),
            int.Parse(recorded.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string? _repoRoot;

    private static string RepoRoot => _repoRoot ??= FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(ConformanceArtifactScanTests).Assembly.Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DwarfMapper.NET.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not locate the repository root (DwarfMapper.NET.sln).");
        return dir!.FullName;
    }
}
