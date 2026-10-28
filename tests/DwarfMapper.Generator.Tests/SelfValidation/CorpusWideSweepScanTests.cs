// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Contracts;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>No single test method runs the generator over the whole golden corpus.</b>
    ///     <para>
    ///         Round 34 (E5). The golden manifest check and the banned-API scan were facts that each ran the generator over
    ///         all 1,021 cases. To Stryker a fact is one test, and it can only run whole tests: every mutant either touched
    ///         ran both whole loops in its session, which made them the two most expensive tests of the generator and
    ///         pipeline legs (153 and 173 mutants covered, ~1.5 s each, warm). They are rows of <c>GoldenCaseTests</c> now,
    ///         one per case, so a mutant's session runs only the cases it reaches. A new sweep would bring the cost back.
    ///     </para>
    ///     <para>
    ///         A sweep is a test method that reads <c>GoldenCorpus.Cases()</c> and runs a generator for a case. A method
    ///         that needs one case looks it up with <c>GoldenCorpus.Case(id)</c>; a method that reads the whole corpus
    ///         without running anything (ids, shapes, floors) is no sweep.
    ///     </para>
    /// </summary>
    public class CorpusWideSweepScanTests
    {
        /// <summary>Calls that run a generator for one input.</summary>
        private static readonly string[] GeneratorRuns =
        [
            "GeneratorRunner.Run", "GoldenFingerprint.Compute", "GoldenFingerprint.Run", "GeneratorTestHarness.Run",
            "GeneratorTestHarness.EmitAssembly", "GeneratorTestHarness.GeneratedCodeWarnings", "GeneratorAssert.",
        ];

        /// <summary>The sweeps that stay, each with its reason.</summary>
        private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
        {
            ["GoldenCorpusTests.The_manifest_names_exactly_the_corpus_cases"] =
                "fingerprints the corpus only under DWARF_GOLDEN_UPDATE=1, the deliberate local regeneration",
        };

        [Fact]
        public void No_test_method_runs_the_generator_over_the_whole_golden_corpus()
        {
            var testsDir = Path.Combine(RepoPaths.Root, "tests", "DwarfMapper.Generator.Tests");
            var scanned = 0;
            var offenders = new List<string>();
            foreach (var file in Directory.GetFiles(testsDir, "*.cs", SearchOption.AllDirectories).Where(f => !IsBuildOutput(f)))
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
                foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (!method.AttributeLists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString() is "Fact" or "Theory"))
                    {
                        continue;
                    }

                    scanned++;
                    var accesses = method.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Select(m => m.ToString()).ToList();
                    var readsWholeCorpus = accesses.Contains("GoldenCorpus.Cases", StringComparer.Ordinal);
                    var runsAGenerator = accesses.Any(a => GeneratorRuns.Any(r => a.StartsWith(r, StringComparison.Ordinal)));
                    var name = (method.Parent as TypeDeclarationSyntax)?.Identifier.Text + "." + method.Identifier.Text;
                    if (readsWholeCorpus && runsAGenerator && !Allowed.ContainsKey(name))
                    {
                        offenders.Add(name + " (" + Path.GetRelativePath(RepoPaths.Root, file).Replace('\\', '/') + ")");
                    }
                }
            }

            // Non-vacuity: the project holds thousands of test methods; a scan that found few is reading the wrong tree.
            Assert.True(scanned > 1_000, $"scanned only {scanned} test methods under {testsDir}.");
            Assert.True(offenders.Count == 0,
                "Test method(s) run the generator over the whole golden corpus in one test, so every mutant they reach " +
                "runs the whole loop in its Stryker session: [" + string.Join(", ", offenders) + "]. Make the check a row " +
                "per case (GoldenCaseTests is the pattern: a TheoryData of case ids, GoldenCorpus.Case(id) in the row).");
        }

        private static bool IsBuildOutput(string path)
        {
            var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return parts.Contains("bin", StringComparer.Ordinal) || parts.Contains("obj", StringComparer.Ordinal);
        }
    }
}
