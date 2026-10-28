// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Framework;
using DwarfMapper.Generator.Tests.SelfValidation;

namespace DwarfMapper.Generator.Tests.Golden
{
    /// <summary>
    ///     <b>Each pinned case, from ONE generator run: its output matches its manifest line, and it names no banned API.</b>
    ///     <para>
    ///         Round 34 (E5). Both checks were single facts over all 1,021 cases, each running the generator over the
    ///         whole corpus: 2,042 runs for 1,021 inputs. To Stryker a fact is one test, so every mutant either touched
    ///         ran both whole loops in its session; they covered 153 of the generator leg's mutants and 173 of the
    ///         pipeline leg's, at ~1.5 s each warm. One row per case runs the generator once for both checks, and a
    ///         mutant's session runs only the rows of the cases it reaches.
    ///     </para>
    ///     <para>
    ///         The corpus-wide checks that need no generator run stay facts in <see cref="GoldenCorpusTests" />: the
    ///         manifest names exactly the corpus's cases, the corpus has not shrunk, and the deliberate regeneration
    ///         under <c>DWARF_GOLDEN_UPDATE=1</c>.
    ///     </para>
    /// </summary>
    public class GoldenCaseTests
    {
        private static readonly Lazy<IReadOnlyDictionary<string, string>> Manifest = new(GoldenManifest.Load);

        public static TheoryData<string> CaseIds()
        {
            var rows = new TheoryData<string>();
            foreach (var c in GoldenCorpus.Cases())
            {
                rows.Add(c.Id);
            }

            return rows;
        }

        [Theory]
        [MemberData(nameof(CaseIds))]
        public void The_case_matches_its_manifest_line_and_names_no_banned_api(string id)
        {
            ArgumentNullException.ThrowIfNull(id);
            var run = GoldenFingerprint.Run(GoldenCorpus.Case(id));

            // The round 29 rule (Issues/round29/RESEARCH-hardware-mode.md §1): no unsafe, no uninitialized memory, no
            // parallelism in generated code. Scanned over EVERY file the case's generator emitted, aggregates included.
            // An empty scan passes every "no banned token" check while checking nothing, so it fails instead.
            var generated = run.AllOutputsConcatenated;
            Assert.True(generated.Length != 0, $"{id} emitted no file, so the banned-API scan would check nothing.");
            var named = EmittedCodePolicyScanTests.Banned.Where(b => generated.Contains(b, StringComparison.Ordinal)).ToList();
            Assert.True(named.Count == 0, $"{id}: the emitted code names a banned API: {string.Join(", ", named)}");

            // A deliberate regeneration: GoldenCorpusTests rewrites the manifest from the same fingerprints.
            if (Environment.GetEnvironmentVariable(GoldenManifest.UpdateEnvVar) == "1")
            {
                return;
            }

            Assert.True(Manifest.Value.TryGetValue(id, out var expected),
                $"{id} has no line in {GoldenManifest.Path}: a new case needs review. Regenerate the manifest with " +
                $"{GoldenManifest.UpdateEnvVar}=1 and commit it deliberately.");
            var actual = GoldenFingerprint.Compute(run);
            Assert.True(actual == expected,
                $"Golden output moved for {id} (manifest {expected}, now {actual}). If this change is INTENTIONAL: review " +
                "the curated snapshots in Snapshots/ (they show real text; hashes do not diff readably), then regenerate " +
                $"with {GoldenManifest.UpdateEnvVar}=1 and commit. If it is NOT intentional, a refactor changed " +
                "observable output - that is the bug.");
        }
    }
}
