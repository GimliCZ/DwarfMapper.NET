// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Keeps the shared assertion fixture actually USED.
///     <para>
///     The "run, assert no errors, assert it compiles" idiom was open-coded 243 times. Four helper classes had
///     already been written to wrap it — and every one was declared <c>file static</c>, so nothing outside its
///     own file could reach it and the next test open-coded the idiom again. Deduplicating once fixes today; it
///     does not stop the same drift happening again, and a hand-rolled copy silently loses the fixture's
///     diagnosable failure message (which is the whole point of it).
///     </para>
///     These are ratchets, in the same spirit as the T4 allowlist-size gates: the hand-rolled forms may not come
///     back, and adoption may not quietly collapse.
/// </summary>
public class FixtureAdoptionScanTests
{
    // Files that legitimately contain the raw idiom: the harness itself, the fixture (its doc comment quotes
    // the very pattern it replaces), and the fixture's own self-tests.
    private static readonly string[] ExemptFiles =
    {
        "GeneratorTestHarness.cs",
        "GeneratorAssert.cs",
        "GeneratorAssertSelfTests.cs",
        "FixtureAdoptionScanTests.cs",
    };

    // Direct RunAndGetCompilationErrors calls that remain on purpose: they capture the errors and assert
    // something specific about them (a particular CS id, a count), which the fixture deliberately does not model.
    private const int DirectCompileErrorCallBaseline = 50;

    private static IEnumerable<(string File, string Text)> TestSources()
    {
        var dir = Path.Combine(RepoRoot(), "tests", "DwarfMapper.Generator.Tests");
        foreach (var path in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;
            if (ExemptFiles.Contains(Path.GetFileName(path), StringComparer.Ordinal)) continue;

            yield return (Path.GetFileName(path), File.ReadAllText(path));
        }
    }

    [Fact]
    public void The_hand_rolled_compiles_clean_idiom_has_not_come_back()
    {
        var offenders = new List<string>();
        foreach (var (file, text) in TestSources())
        {
            // "assert no error diagnostics" immediately followed by "assert the emission compiles".
            var matches = Regex.Matches(text,
                @"Assert\.DoesNotContain\(\s*\w+\s*,\s*\w+ => \w+\.Severity == DiagnosticSeverity\.Error\s*\);\s*"
                + @"Assert\.Empty\(\s*GeneratorTestHarness\.RunAndGetCompilationErrors");
            if (matches.Count > 0) offenders.Add($"{file} ({matches.Count})");
        }

        Assert.True(offenders.Count == 0,
            "The hand-rolled 'compiles clean' idiom is back in: " + string.Join(", ", offenders)
            + ".\nUse GeneratorAssert.CompilesClean(source) instead — it asserts both halves and, when it fails, "
            + "prints the diagnostics AND the generated code rather than a bare 'collection was not empty'.");
    }

    [Fact]
    public void Direct_compile_error_calls_have_not_grown()
    {
        var count = TestSources()
            .Sum(t => Regex.Matches(t.Text, @"GeneratorTestHarness\.RunAndGetCompilationErrors\(").Count);

        Assert.True(count <= DirectCompileErrorCallBaseline,
            $"Direct GeneratorTestHarness.RunAndGetCompilationErrors calls rose to {count} (baseline "
            + $"{DirectCompileErrorCallBaseline}). Prefer GeneratorAssert.EmitsCompilableCode(source); keep a "
            + "direct call only when the test asserts something specific about the errors (a particular CS id, "
            + "a count). If that is genuinely the case, lower/raise this baseline deliberately.");
    }

    [Fact]
    public void The_fixture_is_actually_used()
    {
        // The mirror image: a ratchet on the raw forms is satisfied trivially if everyone stops asserting at
        // all. This fails if adoption collapses (e.g. someone reverts the migration wholesale).
        var uses = TestSources()
            .Sum(t => Regex.Matches(t.Text,
                @"GeneratorAssert\.(CompilesClean|EmitsCompilableCode|Reports|DoesNotReport)\(").Count);

        Assert.True(uses >= 200,
            $"Only {uses} GeneratorAssert call sites found (expected 200+). The shared fixture was bypassed or "
            + "the migration was reverted — the raw-form ratchets above would still pass, so this is the check "
            + "that notices.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("DwarfMapper.NET.sln").Length == 0)
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not locate the repository root (DwarfMapper.NET.sln).");
        return dir!.FullName;
    }
}
