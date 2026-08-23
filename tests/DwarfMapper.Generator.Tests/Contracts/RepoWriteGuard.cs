// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.Contracts
{
    /// <summary>
    ///     The single gate every test-side write into the repository goes through. This is the registered pattern
    ///     ARCH-06 asked for ("repo mutation from tests requires a registered pattern"): the heal-or-fail doc
    ///     tests and the env-gated self-heal paths all resolve the REAL repo root and write REAL tracked files,
    ///     which is exactly right in a developer run and exactly wrong under mutation testing.
    ///     <para>
    ///         Why it is needed (T3-H1, measured 2026-08-19): Stryker 4.16 does not run tests from a sandbox
    ///         directory. It swaps the mutated assembly into each test project's real <c>bin/</c> folder in
    ///         place, keeping the original beside it as <c>*.dll.stryker-unchanged</c> for the duration of the
    ///         run — so <c>AppContext.BaseDirectory</c> is the real bin, every repo-root walk resolves the real
    ///         repository, and a mutated <c>DwarfMapper.DocTooling</c> renderer wrote its garbage straight over
    ///         README.md, CONTRIBUTING.md and docs/diagnostics.md (2,523 deleted lines across five files).
    ///     </para>
    ///     <para>
    ///         Under mutation the write-back is REFUSED but nothing else changes: the caller still compares and
    ///         still fails on staleness, so every mutant a doc test used to kill it still kills. That is the
    ///         binding ruling for this fix — no test is excluded from any mutation leg; only the repo write is.
    ///     </para>
    ///     <para>
    ///         The guard lives HERE, in the test project, and must never move into DwarfMapper.DocTooling: that
    ///         assembly is a mutation target, and a guard inside the mutation target can itself be mutated off.
    ///     </para>
    /// </summary>
    public static class RepoWriteGuard
    {
        /// <summary>
        ///     The evidence that this process is executing against Stryker-mutated assemblies, or null in a
        ///     normal run. Resolved once — the marker cannot appear or vanish mid-run.
        /// </summary>
        public static string? MutationMarker { get; } = FindMutationMarker(AppContext.BaseDirectory);

        /// <summary>True when the current test run is a Stryker mutation run (see <see cref="MutationMarker" />).</summary>
        public static bool IsMutationRun => MutationMarker is not null;

        /// <summary>
        ///     The sentence a refused write appends to its failure message, naming the evidence so a developer
        ///     who hits it in a NON-mutation run knows the tree is polluted and how to clean it.
        /// </summary>
        public static string RefusalNotice =>
            "Write-back was REFUSED because this run is executing against Stryker-mutated assemblies " +
            $"(marker: {MutationMarker}); the repository was not touched. If this is not a Stryker run, the " +
            "marker is a leftover from an interrupted one and the assemblies beside it are still mutants — " +
            "delete the *.stryker-unchanged backups and their sibling DLLs, then rebuild.";

        /// <summary>
        ///     Writes <paramref name="contents" /> to <paramref name="path" /> — unless this is a mutation run,
        ///     in which case nothing is touched. Returns whether the file was written, so heal-or-fail callers
        ///     can tell the truth about what happened in their failure message.
        /// </summary>
        public static bool WriteBack(string path, string contents)
        {
            return WriteBack(path, contents, IsMutationRun);
        }

        /// <summary>Seam for the pinning tests: the refusal must be provable without a real Stryker run.</summary>
        internal static bool WriteBack(string path, string contents, bool mutationRun)
        {
            if (mutationRun)
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            return true;
        }

        /// <summary>Line-based variant with identical refusal semantics (see <see cref="WriteBack(string,string)" />).</summary>
        public static bool WriteBackLines(string path, IReadOnlyCollection<string> lines)
        {
            return WriteBackLines(path, lines, IsMutationRun);
        }

        /// <summary>Seam for the pinning tests: the refusal must be provable without a real Stryker run.</summary>
        internal static bool WriteBackLines(string path, IReadOnlyCollection<string> lines, bool mutationRun)
        {
            if (mutationRun)
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, lines);
            return true;
        }

        /// <summary>
        ///     The detection itself, split out so the pinning tests can aim it at fabricated directories.
        ///     Two independent signals, filesystem-only (no reflection, no environment variables — Stryker's
        ///     env vars are per-testhost implementation details, and the initial no-active-mutant run must be
        ///     detected too):
        ///     <list type="number">
        ///         <item>
        ///             A <c>*.stryker-unchanged</c> file beside the test assembly — the in-place assembly swap
        ///             Stryker 4.16 actually performs here, present for the whole run. A LEFTOVER backup after an
        ///             interrupted run means the sibling DLLs may still be mutants, so refusing then is equally
        ///             correct.
        ///         </item>
        ///         <item>
        ///             An ancestor directory named <c>StrykerOutput</c> or <c>.stryker-tmp</c> — the copy-sandbox
        ///             shape other Stryker versions/configurations use. Covers the general "root resolution
        ///             escaped a sandbox upward into the real checkout" case.
        ///         </item>
        ///     </list>
        /// </summary>
        internal static string? FindMutationMarker(string baseDirectory)
        {
            if (Directory.Exists(baseDirectory))
            {
                var backup = Directory.EnumerateFiles(baseDirectory, "*.stryker-unchanged").FirstOrDefault();
                if (backup is not null)
                {
                    return backup;
                }
            }

            for (var dir = new DirectoryInfo(baseDirectory); dir is not null; dir = dir.Parent)
                if (string.Equals(dir.Name, "StrykerOutput", StringComparison.OrdinalIgnoreCase) || string.Equals(dir.Name, ".stryker-tmp", StringComparison.OrdinalIgnoreCase))
                {
                    return dir.FullName;
                }

            return null;
        }
    }
}
