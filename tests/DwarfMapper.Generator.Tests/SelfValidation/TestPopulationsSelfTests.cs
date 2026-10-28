// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Contracts;
using DwarfMapper.TestInfrastructure;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Pins every test population's size, and keeps the fast/deep switch from coming back.
    ///     <para>
    ///         Round 34: one size per population, in every run (see <see cref="TestPopulation" />). Three failure shapes
    ///         are pinned. (1) A catalog edit that changes a size would lengthen or shorten every run, Stryker's sessions
    ///         included, without anyone deciding that; the sizes here are an independent copy, so such an edit fails
    ///         loudly. (2) A size taken from anywhere else, an environment variable above all, the way <c>DWARF_DEEP</c>
    ///         chose between two sizes until round 34, would make two ways to run the suite again: no test, project
    ///         file, script, workflow or settings file may name that variable. (3) A catalog entry no call site uses
    ///         would report a size no test runs with.
    ///     </para>
    /// </summary>
    public class TestPopulationsSelfTests
    {
        /// <summary>
        ///     Every size, pinned INDEPENDENTLY of the catalog: each population's deep count until round 34 (trx-verified
        ///     2026-08-21 for the populations that existed then; the round-23 entries at the count each was measured at).
        ///     Changing one is a decision about every run, made here, in the same commit, on purpose.
        /// </summary>
        private static readonly Dictionary<TestPopulation, int> PinnedCounts = new()
        {
            [TestPopulation.FeatureCombinationSubsetOrder] = 3,
            [TestPopulation.BehavioralSeeds] = 600,
            [TestPopulation.ExtendedBehavioralSeeds] = 400,
            [TestPopulation.AllEmitPathsSeeds] = 250,
            [TestPopulation.CompilesAlwaysSeeds] = 1000,
            [TestPopulation.CompilesAlwaysAdvancedSeeds] = 250,
            [TestPopulation.DeterminismBroadSeeds] = 1200,
            [TestPopulation.IndependenceSeeds] = 600,
            [TestPopulation.MetamorphicSeeds] = 250,
            [TestPopulation.TopologyGraphSeeds] = 120,
            [TestPopulation.FlattenHomoSeeds] = 250,
            [TestPopulation.FlattenHeteroSeeds] = 150,
            [TestPopulation.CrossConfigUpdateSeeds] = 200,
            [TestPopulation.CultureBehavioralSeeds] = 80,
            [TestPopulation.CultureAdvancedSeeds] = 40,
            [TestPopulation.DocPipelineIters] = 5000,
            [TestPopulation.DocPipelineScanIters] = 2000,
            [TestPopulation.TortureCreateRounds] = 240,
            [TestPopulation.TortureUpdateRounds] = 960,
            [TestPopulation.PolymorphicGraphSeeds] = 45,
            [TestPopulation.ObjectFactoryDistributionSeeds] = 4000,
            [TestPopulation.RegistryPropertyIters] = 2000,
            [TestPopulation.CompilerGraphSmokeSeeds] = 250,
            [TestPopulation.CompilerProjectionSmokeSeeds] = 1000,
            [TestPopulation.CompilerOracleSeeds] = 1000,
            [TestPopulation.CompilerProjectionAgreementSeeds] = 1000,
            [TestPopulation.CompilerMrMemberOrderSeeds] = 250,
            [TestPopulation.CompilerMrUnmappedMemberSeeds] = 250,
            [TestPopulation.CompilerMrRekindSeeds] = 150,
            // Round-23 S3: it scales the SIZE of a single compilation rather than a case count, which makes pinning it
            // matter more than usual: raising it lengthens every run by a whole larger compile, not by one more sample.
            [TestPopulation.CompilerCostCorpusMappers] = 1000
        };

        /// <summary>The files that decide how the suite runs: tests and their projects, the scripts, CI, and the settings.</summary>
        private static List<string> RunDefiningFiles()
        {
            var root = RepoPaths.Root;
            return RepoPaths.SourceFiles(RepoPaths.Tests)
                            .Concat(Directory.GetFiles(RepoPaths.Tests, "*.csproj", SearchOption.AllDirectories))
                            .Concat(Directory.GetFiles(Path.Combine(root, "scripts"), "*.ps1", SearchOption.AllDirectories))
                            .Concat(Directory.GetFiles(Path.Combine(root, "scripts"), "*.sh", SearchOption.AllDirectories))
                            .Concat(Directory.GetFiles(Path.Combine(root, ".github", "workflows"), "*.yml"))
                            .Concat(Directory.GetFiles(root, "*.runsettings"))
                            .Concat(new[] { "Directory.Build.props", "Directory.Build.targets", "CONTRIBUTING.md", "README.md" }
                                        .Select(f => Path.Combine(root, f)))
                            .Where(File.Exists)
                            .ToList();
        }

        [Fact]
        public void Every_population_is_registered_and_pinned()
        {
            var declared = Enum.GetValues<TestPopulation>();

            Assert.Equal(declared.Length, TestPopulations.Populations.Count);
            Assert.Equal(declared.Length, PinnedCounts.Count);
            foreach (var population in declared)
            {
                Assert.Contains(population, TestPopulations.Populations);
                Assert.True(PinnedCounts.ContainsKey(population),
                    $"{population} has no pinned size in this self-test — pin it in the same commit that adds it.");
            }
        }

        [Fact]
        public void Every_population_runs_its_pinned_size()
        {
            foreach (var (population, pinned) in PinnedCounts)
                Assert.True(TestPopulations.Count(population) == pinned,
                    $"{population}: size {TestPopulations.Count(population)} != pinned {pinned}. Every run uses this size, " +
                    "Stryker's sessions included; changing it is a deliberate decision made HERE, not a side effect of a " +
                    "catalog edit.");
        }

        [Fact]
        public void Nothing_selects_a_test_population_size_from_the_environment()
        {
            // The variable that chose between the fast and deep sizes until round 34. Built by concatenation so this scan
            // cannot match its own source; TestPopulations.cs records its history in prose and is the one file allowed to.
            var variable = "DWARF_" + "DEEP";
            var files = RunDefiningFiles();

            // Non-vacuity: hundreds of test sources plus the scripts and workflows.
            Assert.True(files.Count > 300, $"read only {files.Count} files.");
            Assert.Contains(files, f => f.EndsWith("ci.yml", StringComparison.Ordinal));
            Assert.Contains(files, f => f.EndsWith("housekeeping.ps1", StringComparison.Ordinal));

            var offenders = files.Where(f => Path.GetFileName(f) is not ("TestPopulations.cs" or "TestPopulationsSelfTests.cs") &&
                                             File.ReadAllText(f).Contains(variable, StringComparison.Ordinal))
                                 .Select(f => Path.GetRelativePath(RepoPaths.Root, f))
                                 .ToList();

            Assert.True(offenders.Count == 0,
                "These files name " + variable + ", the switch between a fast and a deep test population that round 34 " +
                "removed so the suite runs one way everywhere: " + string.Join(", ", offenders) + ". A population's size " +
                "is its TestPopulations entry, in every run.");
        }

        [Fact]
        public void Every_registered_population_has_a_call_site()
        {
            // An entry nobody consumes would report a size that no test actually runs with.
            var sources = RepoPaths.SourceFiles(RepoPaths.Tests)
                .Where(f => Path.GetFileName(f) is not ("TestPopulations.cs" or "TestPopulationsSelfTests.cs"))
                .Select(File.ReadAllText)
                .ToList();

            var unused = Enum.GetValues<TestPopulation>()
                .Where(population => !sources.Any(s => s.Contains(
                    "TestPopulation." + population,
                    StringComparison.Ordinal)))
                .ToList();

            Assert.True(unused.Count == 0,
                "These TestPopulation entries have no call site (no test passes them to TestPopulations.Count): " + string.Join(", ", unused));
        }
    }
}
