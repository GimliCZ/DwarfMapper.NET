// SPDX-License-Identifier: GPL-2.0-only

using System.Text;
using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Proves I4's decontamination sweep (<c>Assert-NoMutatedProductBinaries</c> in
    ///     <c>scripts/gate-checks.ps1</c>) actually bites, and that every mutation leg calls it.
    ///     <para>
    ///         The hazard it closes: Stryker has no test sandbox and backs up only the files it OVERWRITES.
    ///         The analyzer-referencing test projects reference <c>DwarfMapper.Generator</c> with
    ///         <c>ReferenceOutputAssembly="false"</c>, so their bins hold no pre-run copy — Stryker plants a
    ///         MUTATED <c>DwarfMapper.Generator.dll</c> there, writes no <c>*.stryker-unchanged</c> marker
    ///         beside it, restore never touches it, and both existing post-leg proofs are structurally blind:
    ///         <c>git diff --exit-code</c> because <c>bin/</c> is git-ignored, and
    ///         <c>RepoWriteGuard</c>'s leftover-backup signal because there is no backup. P5 found six such
    ///         bins after a run that was otherwise clean, and swept them by hand.
    ///     </para>
    ///     <para>
    ///         Two halves, because either alone is decoration. The BEHAVIOURAL half is the house sabotage
    ///         demo made permanent: a marked file planted in a fake tree must red the sweep naming the file,
    ///         and the same tree without it must pass — plus both vacuity directions, since a sweep that
    ///         looks at nothing passes exactly like a clean tree. The WIRING half asserts the call sites
    ///         survive: three in <c>housekeeping.ps1</c> (one per leg) and one in the <c>ci.yml</c> mutation
    ///         matrix. A guard nobody calls is the same as no guard.
    ///     </para>
    /// </summary>
    public class MutationDecontaminationSweepTests
    {
        /// <summary>
        ///     Stryker's fingerprint in a mutated assembly. Kept as one constant so the test cannot drift from
        ///     the marker the gate scans for; the gate proves the marker still discriminates by checking that
        ///     the src-built originals do NOT carry it.
        /// </summary>
        private const string Marker = "Stryker";

        [Fact]
        public void The_sweep_reds_on_a_planted_mutant_passes_when_clean_and_refuses_to_pass_vacuously()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dwarfmapper-decontam-" + Guid.NewGuid().ToString("N"));
            try
            {
                // Four fake repo roots, each a two-line story the sweep must read correctly. The files are
                // not real assemblies: the gate is a byte scan over file CONTENT (never a timestamp — P5's
                // original detection rode the run's clock, which is the oracle H7 forbids), so plain bytes
                // with or without the marker are exactly the input it reads in production.
                MakeTree(Path.Combine(dir, "clean"), false, true, true);
                MakeTree(Path.Combine(dir, "planted"), true, true, true);
                MakeTree(Path.Combine(dir, "no-originals"), false, false, true);
                MakeTree(Path.Combine(dir, "no-copies"), false, true, false);

                // A fifth: the scanner's own premise broken — the src-built ORIGINAL already matches the
                // marker, so a green sweep would prove nothing at all.
                var dirtyOriginals = Path.Combine(dir, "dirty-originals");
                MakeTree(dirtyOriginals, false, true, true);
                WriteTemp(Path.Combine(dirtyOriginals,
                        "src",
                        "DwarfMapper.Generator",
                        "bin",
                        "Release",
                        "DwarfMapper.Generator.dll"),
                    "clean bytes " + Marker + " oops");

                WriteTemp(Path.Combine(dir, "battery.ps1"),
                    PwshBattery.ScriptPrelude +
                    """
                    Show 'clean'           { Assert-NoMutatedProductBinaries -Leg 'fake' -Root (Join-Path $WorkDir 'clean') }
                    Show 'planted'         { Assert-NoMutatedProductBinaries -Leg 'fake' -Root (Join-Path $WorkDir 'planted') }
                    Show 'no-originals'    { Assert-NoMutatedProductBinaries -Leg 'fake' -Root (Join-Path $WorkDir 'no-originals') }
                    Show 'no-copies'       { Assert-NoMutatedProductBinaries -Leg 'fake' -Root (Join-Path $WorkDir 'no-copies') }
                    Show 'dirty-originals' { Assert-NoMutatedProductBinaries -Leg 'fake' -Root (Join-Path $WorkDir 'dirty-originals') }
                    """);

                var cases = PwshBattery.Run(dir, 5, "the I4 decontamination sweep");

                // Clean tree: passes, and says how many assemblies it actually looked at — a count is the
                // difference between "nothing was mutated" and "nothing was examined".
                Assert.StartsWith("OK", cases["clean"], StringComparison.Ordinal);

                // The sabotage: reds, and NAMES the offending file. A guard that fails without saying which
                // file to delete sends the reader back to the manual sweep P5 had to do.
                Assert.StartsWith("THREW", cases["planted"], StringComparison.Ordinal);
                Assert.Contains("MUTATED", cases["planted"], StringComparison.Ordinal);
                Assert.Contains("DwarfMapper.Generator.dll", cases["planted"], StringComparison.Ordinal);
                Assert.Contains("CorpusTests", cases["planted"], StringComparison.Ordinal);

                // Both vacuity directions. A sweep with no reference set and a sweep with nothing to scan
                // both "find no mutants", which is how a gate that has stopped working reports success.
                Assert.StartsWith("THREW", cases["no-originals"], StringComparison.Ordinal);
                Assert.Contains("reference set", cases["no-originals"], StringComparison.Ordinal);
                Assert.StartsWith("THREW", cases["no-copies"], StringComparison.Ordinal);
                Assert.Contains("vacuous", cases["no-copies"], StringComparison.Ordinal);

                // The premise: if a clean build already matches, the marker has stopped discriminating.
                Assert.StartsWith("THREW", cases["dirty-originals"], StringComparison.Ordinal);
                Assert.Contains("ORIGINALS", cases["dirty-originals"], StringComparison.Ordinal);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        [Fact]
        public void Every_mutation_leg_calls_the_sweep_in_housekeeping_and_in_ci()
        {
            // The typeof reference makes deleting the battery a compile error here, the same way
            // RatchetInvariantScanTests anchors GateBandLogicTests: behaviour proven there, wiring proven here.
            _ = typeof(PwshBattery);

            var gateChecks = File.ReadAllText(Path.Combine(RepoPaths.Root, "scripts", "gate-checks.ps1"));
            Assert.Contains("function Assert-NoMutatedProductBinaries", gateChecks, StringComparison.Ordinal);

            var housekeeping = File.ReadAllText(Path.Combine(RepoPaths.Root, "scripts", "housekeeping.ps1"));
            var legCalls = Regex.Matches(housekeeping, @"Assert-NoMutatedProductBinaries ").Count;
            Assert.True(legCalls == 3,
                $"scripts/housekeeping.ps1 calls Assert-NoMutatedProductBinaries {legCalls} time(s), expected " + "exactly 3 (one per mutation leg) — a leg that is not swept can leave a mutated product " + "assembly in a test bin, and the next incremental build keeps it (I4).");

            // CI runs the SAME function rather than a re-implementation, so the ci.yml step must dot-source
            // the gate file. A bash re-write of the scan would be a second thing to keep in step, which is
            // the drift Assert-MutantsWereTested and Assert-StrykerConfigSane both had to be warned about.
            var ci = File.ReadAllText(Path.Combine(RepoPaths.Root, ".github", "workflows", "ci.yml"));
            Assert.True(ci.Contains("Assert-NoMutatedProductBinaries", StringComparison.Ordinal),
                ".github/workflows/ci.yml: the mutation matrix no longer sweeps for mutated product binaries. " + "`git diff --exit-code` cannot see them — bin/ is git-ignored (I4).");
            Assert.Contains("./scripts/gate-checks.ps1", ci, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A fake repo root shaped like this one: a src build that defines the product assembly names, and
        ///     copies of them in a test bin. <paramref name="plantedMarker" /> puts Stryker's fingerprint in
        ///     one test-bin copy, which is the sabotage.
        /// </summary>
        private static void MakeTree(string root, bool plantedMarker, bool withOriginals, bool withTestCopies)
        {
            if (withOriginals)
            {
                WriteTemp(Path.Combine(root, "src", "DwarfMapper", "bin", "Release", "DwarfMapper.dll"),
                    "clean runtime bytes");
                WriteTemp(Path.Combine(root,
                        "src",
                        "DwarfMapper.Generator",
                        "bin",
                        "Release",
                        "DwarfMapper.Generator.dll"),
                    "clean generator bytes");
            }

            if (!withTestCopies)
            {
                return;
            }

            // A test ASSEMBLY carrying the marker in its own source, present in every case: this is not
            // hypothetical — DwarfMapper.Generator.Tests.dll really does match, because its sources name
            // Stryker. The sweep must scan product assembly names only, or it reds on a clean tree forever.
            WriteTemp(Path.Combine(root,
                    "tests",
                    "DwarfMapper.Generator.Tests",
                    "bin",
                    "Debug",
                    "DwarfMapper.Generator.Tests.dll"),
                "test bytes mentioning " + Marker + " in a scan pin");

            WriteTemp(Path.Combine(root, "tests", "DwarfMapper.CorpusTests", "bin", "Debug", "DwarfMapper.dll"),
                "clean runtime bytes");
            WriteTemp(Path.Combine(root,
                    "tests",
                    "DwarfMapper.CorpusTests",
                    "bin",
                    "Debug",
                    "DwarfMapper.Generator.dll"),
                plantedMarker ? "mutated bytes " + Marker + "Namespace.MutantControl" : "clean generator bytes");
        }

        /// <summary>Temp-directory-only write; the single registered raw-write call site of this file.</summary>
        private static void WriteTemp(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, Encoding.ASCII);
        }
    }
}
