// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Pins <see cref="RepoWriteGuard" /> — the ARCH-06 gate that keeps a Stryker mutation run from writing
///     into the real repository (T3-H1: a mutated DocTooling renderer emptied README.md, CONTRIBUTING.md and
///     docs/diagnostics.md through the doc tests' heal-or-fail write-back). Both halves are exercised against
///     fabricated directories, so no actual Stryker run is needed to prove either: the DETECTION must fire on
///     the two marker shapes Stryker leaves, and the REFUSAL must leave the target untouched when it does.
///     Deleting the guard fails this file at compile time; hollowing it out fails it at run time.
/// </summary>
public class RepoWriteGuardTests
{
    // ── Detection ─────────────────────────────────────────────────────────────

    [Fact]
    public void A_stryker_unchanged_backup_beside_the_assembly_is_detected()
    {
        // The shape Stryker 4.16 actually produces here: the in-place assembly swap keeps the original as
        // "X.dll.stryker-unchanged" in the test project's REAL bin folder — there is no sandbox directory at
        // all, which is exactly why the doc write-back reached the real repository.
        InTempDir(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "Some.Assembly.dll.stryker-unchanged"), "backup");
            var marker = RepoWriteGuard.FindMutationMarker(dir);
            Assert.NotNull(marker);
            Assert.EndsWith(".stryker-unchanged", marker, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("StrykerOutput")]
    [InlineData(".stryker-tmp")]
    public void A_sandbox_shaped_ancestor_is_detected(string ancestorName)
    {
        // The copy-sandbox shape other Stryker versions/configurations use. Also covers the general case of
        // a repo-root walk escaping upward OUT of a sandbox into the real checkout: the base directory it
        // walked from still carries the sandbox-named ancestor.
        InTempDir(dir =>
        {
            var baseDir = Path.Combine(dir, ancestorName, "sandbox", "bin", "Debug");
            Directory.CreateDirectory(baseDir);
            Assert.NotNull(RepoWriteGuard.FindMutationMarker(baseDir));
        });
    }

    [Fact]
    public void A_clean_base_directory_is_not_detected()
    {
        InTempDir(dir =>
        {
            var baseDir = Path.Combine(dir, "bin", "Debug");
            Directory.CreateDirectory(baseDir);
            Assert.Null(RepoWriteGuard.FindMutationMarker(baseDir));
        });
    }

    // ── Refusal ───────────────────────────────────────────────────────────────

    [Fact]
    public void Under_mutation_the_write_back_refuses_and_the_target_is_untouched()
    {
        InTempDir(dir =>
        {
            var target = Path.Combine(dir, "README.md");
            File.WriteAllText(target, "original");

            Assert.False(RepoWriteGuard.WriteBack(target, "mutant garbage", mutationRun: true));
            Assert.Equal("original", File.ReadAllText(target));

            Assert.False(RepoWriteGuard.WriteBackLines(target, ["mutant", "garbage"], mutationRun: true));
            Assert.Equal("original", File.ReadAllText(target));
        });
    }

    [Fact]
    public void Under_mutation_the_write_back_does_not_even_create_the_directory()
    {
        // AssertCurrent used to Directory.CreateDirectory before comparing, so a Stryker run left
        // docs/generated/ behind even when it wrote nothing. The side effect belongs behind the guard too.
        InTempDir(dir =>
        {
            var target = Path.Combine(dir, "docs", "generated", "index.md");
            Assert.False(RepoWriteGuard.WriteBack(target, "content", mutationRun: true));
            Assert.False(Directory.Exists(Path.Combine(dir, "docs")));
        });
    }

    [Fact]
    public void Outside_mutation_the_write_back_writes_and_creates_the_directory()
    {
        InTempDir(dir =>
        {
            var target = Path.Combine(dir, "docs", "generated", "index.md");
            Assert.True(RepoWriteGuard.WriteBack(target, "content", mutationRun: false));
            Assert.Equal("content", File.ReadAllText(target));
        });
    }

    // ── ARCH-06: repo mutation from tests requires a registered pattern ──────

    /// <summary>
    ///     Every raw file-writing API use in the test tree must be enumerated here with its reason. The four
    ///     historical repo writers (the two heal-or-fail doc tests, the DWARF_SELF_HEAL append, the golden
    ///     manifest) all route through <see cref="RepoWriteGuard" /> now; a fifth writer that lands with a
    ///     raw call — guarded by nothing — is exactly what ARCH-06 exists to catch.
    /// </summary>
    [Fact]
    public void Every_raw_write_api_use_in_the_test_tree_is_a_registered_pattern()
    {
        // Occurrence counts, not just file names: an allowlisted file gaining a SECOND raw write is a new
        // writer hiding behind an old registration.
        var allowed = new Dictionary<string, (int Count, string Reason)>(StringComparer.Ordinal)
        {
            ["DwarfMapper.Generator.Tests/Contracts/RepoWriteGuard.cs"] =
                (2, "the guard itself — the only place a guarded write is performed"),
            ["DwarfMapper.Generator.Tests/SelfValidation/GateBandLogicTests.cs"] =
                (1, "WriteTemp helper: fake Stryker reports + the pwsh battery script, temp directory only"),
            ["DwarfMapper.Generator.Tests/SelfValidation/GeneratedDocsAreCurrentTests.cs"] =
                (1, "HasGitMarker probe writing a fake .git file into a temp directory"),
            ["DwarfMapper.Generator.Tests/SelfValidation/RepoWriteGuardTests.cs"] =
                (2, "this file's temp-directory fixtures (backup marker, pre-existing target)")
        };

        string[] rawWriteApis =
        [
            "File.WriteAllText(", "File.WriteAllLines(", "File.WriteAllBytes(",
            "File.AppendAllText(", "File.AppendAllLines(",
            "File.Copy(", "File.Move(", "File.Replace(",
            "new StreamWriter(", "new FileStream("
        ];

        var offenders = new List<string>();
        foreach (var file in RepoPaths.SourceFiles(RepoPaths.Tests))
        {
            var text = File.ReadAllText(file);
            var hits = rawWriteApis.Sum(api => Occurrences(text, api));
            if (hits == 0) continue;

            var relative = Path.GetRelativePath(RepoPaths.Tests, file).Replace('\\', '/');
            if (allowed.TryGetValue(relative, out var entry))
            {
                // This file declares its needles as string literals, which the raw scan of its own text
                // cannot tell apart from calls — subtract them so the pinned count stays honest.
                if (string.Equals(relative,
                        "DwarfMapper.Generator.Tests/SelfValidation/RepoWriteGuardTests.cs",
                        StringComparison.Ordinal))
                    hits -= rawWriteApis.Length;

                if (hits != entry.Count)
                    offenders.Add($"{relative}: {hits} raw write call(s), {entry.Count} registered");
            }
            else
            {
                offenders.Add($"{relative}: {hits} raw write call(s), not registered at all");
            }
        }

        Assert.True(offenders.Count == 0,
            "Raw file-writing API use outside the registered ARCH-06 allowlist. Route repo writes through "
            + "RepoWriteGuard.WriteBack (which refuses them under Stryker — T3-H1), or register a genuinely "
            + "temp-only write here with its reason:\n  " + string.Join("\n  ", offenders));
    }

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static void InTempDir(Action<string> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dwarfmapper-repowriteguard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            body(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
