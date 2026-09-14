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

        /// <summary>
        ///     The leg wrapper decontaminates on EVERY exit, and refuses to start on a contaminated tree.
        ///     <para>
        ///         Round 30 measured the hole this closes, twice in one day. Each leg used to run its sweep as the
        ///         LAST statements of its block, after Assert-LegScoreWithinBand — so a leg that failed R2 (as a
        ///         leg that beats its floor by a point must) threw past Remove-PlantedMutants and left six mutated
        ///         DwarfMapper.Generator.dll copies in tests/**/bin/Release. A harness-killed leg never reached the
        ///         sweep at all, and the next leg then measured on that contaminated tree and reported a number.
        ///     </para>
        /// </summary>
        [Fact]
        public void The_leg_wrapper_decontaminates_when_the_leg_throws_and_refuses_a_contaminated_start()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dwarfmapper-legwrap-" + Guid.NewGuid().ToString("N"));
            try
            {
                MakeTree(Path.Combine(dir, "leg-throws"), false, true, true);
                MakeTree(Path.Combine(dir, "already-planted"), true, true, true);
                MakeTree(Path.Combine(dir, "clean-leg"), false, true, true);

                WriteTemp(Path.Combine(dir, "battery.ps1"),
                    PwshBattery.ScriptPrelude +
                    """
                    $plantedCopy = 'tests/DwarfMapper.CorpusTests/bin/Debug/DwarfMapper.Generator.dll'
                    Show 'thrown-leg' {
                        Invoke-DecontaminatedMutationLeg -Leg 'fake' -Root (Join-Path $WorkDir 'leg-throws') -Body {
                            # What Stryker does to an analyzer-only reference, then an R2 failure.
                            Set-Content -LiteralPath (Join-Path (Join-Path $WorkDir 'leg-throws') $plantedCopy) `
                                -Value 'mutated bytes StrykerNamespace.MutantControl' -NoNewline
                            throw 'R2 boom'
                        }
                    }
                    Show 'pre-flight' {
                        Invoke-DecontaminatedMutationLeg -Leg 'fake' -Root (Join-Path $WorkDir 'already-planted') -Body {
                            Set-Content -LiteralPath (Join-Path $WorkDir 'already-planted-ran.txt') -Value 'ran'
                        }
                    }
                    Show 'clean-leg' {
                        Invoke-DecontaminatedMutationLeg -Leg 'fake' -Root (Join-Path $WorkDir 'clean-leg') -Body {
                            Set-Content -LiteralPath (Join-Path $WorkDir 'clean-leg-ran.txt') -Value 'ran'
                        }
                    }
                    """);

                var cases = PwshBattery.Run(dir, 3, "the mutation-leg decontamination wrapper");
                var corpusCopy = Path.Combine("tests", "DwarfMapper.CorpusTests", "bin", "Debug", "DwarfMapper.Generator.dll");

                // A throwing leg still surfaces ITS OWN failure — the wrapper must not swallow an R2 red — and the
                // mutant it planted is gone regardless.
                Assert.StartsWith("THREW", cases["thrown-leg"], StringComparison.Ordinal);
                Assert.Contains("R2 boom", cases["thrown-leg"], StringComparison.Ordinal);
                Assert.False(File.Exists(Path.Combine(dir, "leg-throws", corpusCopy)),
                    "the leg threw after planting a mutant, and the wrapper left the mutant in the test bin — the " + "round-30 contamination.");

                // A tree that is already contaminated is refused before the leg spends an hour measuring on it.
                Assert.StartsWith("THREW", cases["pre-flight"], StringComparison.Ordinal);
                Assert.Contains("MUTATED", cases["pre-flight"], StringComparison.Ordinal);
                Assert.Contains("CorpusTests", cases["pre-flight"], StringComparison.Ordinal);
                Assert.False(File.Exists(Path.Combine(dir, "already-planted-ran.txt")),
                    "the leg body ran on a tree that already carried a mutated product assembly.");

                // The control: a clean leg runs and passes, and a clean product copy is left where it is.
                Assert.StartsWith("OK", cases["clean-leg"], StringComparison.Ordinal);
                Assert.True(File.Exists(Path.Combine(dir, "clean-leg-ran.txt")), "the clean leg's body never ran.");
                Assert.True(File.Exists(Path.Combine(dir, "clean-leg", corpusCopy)),
                    "the wrapper removed a CLEAN product assembly — only marked copies may be deleted.");
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

            // The wrapper owns the sweep, and owns it in a finally: a leg that throws must still be swept.
            var wrapperStart = gateChecks.IndexOf("function Invoke-DecontaminatedMutationLeg", StringComparison.Ordinal);
            Assert.True(wrapperStart >= 0, "scripts/gate-checks.ps1 no longer defines Invoke-DecontaminatedMutationLeg.");
            var wrapperEnd = gateChecks.IndexOf("\nfunction ", wrapperStart + 1, StringComparison.Ordinal);
            var wrapper = wrapperEnd < 0 ? gateChecks[wrapperStart..] : gateChecks[wrapperStart..wrapperEnd];
            Assert.Contains("finally", wrapper, StringComparison.Ordinal);
            Assert.Contains("Remove-PlantedMutants", wrapper, StringComparison.Ordinal);
            Assert.Contains("Assert-NoMutatedProductBinaries", wrapper, StringComparison.Ordinal);

            var housekeeping = File.ReadAllText(Path.Combine(RepoPaths.Root, "scripts", "housekeeping.ps1"));
            // SIX legs since round 29 added the testing-toolkit verifier leg. Literals, not counts derived from
            // the leg list: deriving them would make these assertions agree with whatever housekeeping.ps1
            // happens to do, which is the one thing a sweep check must not do.
            //
            // Since round 30 every leg runs THROUGH the wrapper, so housekeeping calls the sweep functions
            // directly zero times: a direct call is a leg that has stepped outside the finally again.
            var wrapperCalls = Regex.Matches(housekeeping, @"Invoke-DecontaminatedMutationLeg -Leg ").Count;
            Assert.True(wrapperCalls == 6,
                $"scripts/housekeeping.ps1 calls Invoke-DecontaminatedMutationLeg {wrapperCalls} time(s), expected " + "exactly 6 (one per mutation leg) — a leg outside the wrapper is not swept when it throws, and " + "leaves a mutated product assembly in a test bin (I4).");
            Assert.True(Regex.Matches(housekeeping, @"(?m)^\s*(Remove-PlantedMutants|Assert-NoMutatedProductBinaries) ").Count == 0,
                "scripts/housekeeping.ps1 calls Remove-PlantedMutants / Assert-NoMutatedProductBinaries directly — " + "that leg sweeps only on success. Route it through Invoke-DecontaminatedMutationLeg.");

            // And each leg's Stryker run sits inside ITS OWN wrapper call, not beside it.
            foreach (var leg in new[] { "generator", "doc tooling", "runtime", "code fixes", "pipeline", "testing" })
            {
                var call = housekeeping.IndexOf($"Invoke-DecontaminatedMutationLeg -Leg '{leg}'", StringComparison.Ordinal);
                Assert.True(call >= 0, $"scripts/housekeeping.ps1: the '{leg}' leg is not run through Invoke-DecontaminatedMutationLeg.");
                var next = housekeeping.IndexOf("Invoke-DecontaminatedMutationLeg -Leg ", call + 1, StringComparison.Ordinal);
                var body = next < 0 ? housekeeping[call..] : housekeeping[call..next];
                Assert.Contains($"Invoke-StrykerLeg -Leg '{leg}'", body, StringComparison.Ordinal);
            }

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
