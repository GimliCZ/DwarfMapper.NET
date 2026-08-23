// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;
using DwarfMapper.TestInfrastructure;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Proves the <c>DWARF_DEEP</c> knob is neither a silent fast-tier change nor a vacuous deep tier.
    ///     <para>
    ///         Three failure shapes are pinned. (1) A catalog edit that changes a FAST count would speed up or
    ///         slow down every routine <c>dotnet test</c> without anyone deciding that — the fast column here is
    ///         an independent copy of the counts the suite ran with before the knob existed, so such an edit
    ///         fails loudly. (2) A deep value ≤ its fast value would make <c>-Deep</c> silently run the shallow
    ///         tier for that population. (3) A population that reads the environment variable directly, or a
    ///         catalog entry no call site uses, would drift outside the registry — both are scan-banned, the
    ///         same way <c>DeterminismSourceScanTests</c> bans culture-sensitive constructs.
    ///     </para>
    /// </summary>
    public class DeepTierSelfTests
    {
        /// <summary>
        ///     The fast tier, pinned INDEPENDENTLY of the catalog: these are the literal counts the populations
        ///     ran with before the knob existed (trx-verified 2026-08-21). Changing a fast count is a decision
        ///     about every routine test run and must be made here, in the same commit, on purpose.
        /// </summary>
        private static readonly Dictionary<DeepPopulation, int> PinnedFastCounts = new()
        {
            [DeepPopulation.FeatureCombinationSubsetOrder] = 2,
            [DeepPopulation.BehavioralSeeds] = 60,
            [DeepPopulation.ExtendedBehavioralSeeds] = 40,
            [DeepPopulation.AllEmitPathsSeeds] = 50,
            [DeepPopulation.CompilesAlwaysSeeds] = 200,
            [DeepPopulation.CompilesAlwaysAdvancedSeeds] = 50,
            [DeepPopulation.DeterminismBroadSeeds] = 120,
            [DeepPopulation.IndependenceSeeds] = 60,
            [DeepPopulation.MetamorphicSeeds] = 50,
            [DeepPopulation.TopologyGraphSeeds] = 12,
            [DeepPopulation.FlattenHomoSeeds] = 25,
            [DeepPopulation.FlattenHeteroSeeds] = 15,
            [DeepPopulation.CrossConfigUpdateSeeds] = 20,
            [DeepPopulation.CultureBehavioralSeeds] = 8,
            [DeepPopulation.CultureAdvancedSeeds] = 4,
            [DeepPopulation.DocPipelineIters] = 500,
            [DeepPopulation.DocPipelineScanIters] = 200,
            [DeepPopulation.TortureCreateRounds] = 60,
            [DeepPopulation.TortureUpdateRounds] = 240,
            [DeepPopulation.PolymorphicGraphSeeds] = 5,
            [DeepPopulation.ObjectFactoryDistributionSeeds] = 400,
            [DeepPopulation.RegistryPropertyIters] = 200,
            [DeepPopulation.CompilerGraphSmokeSeeds] = 25,
            // Round-23 I18: new population, so "the historical count" is the count it was MEASURED at and
            // entered the catalog with, not a pre-knob count. Pinned here for the same reason as every other
            // row — raising it later is a decision about every routine `dotnet test`, made in this file.
            [DeepPopulation.CompilerProjectionSmokeSeeds] = 100,
            [DeepPopulation.CompilerOracleSeeds] = 20,
            // Round-23 I18, same note as the projection smoke entry above: a new population's "historical"
            // count is the one it was measured at and entered the catalog with.
            [DeepPopulation.CompilerProjectionAgreementSeeds] = 100,
            [DeepPopulation.CompilerMrMemberOrderSeeds] = 10,
            [DeepPopulation.CompilerMrUnmappedMemberSeeds] = 10,
            [DeepPopulation.CompilerMrRekindSeeds] = 8,
            // Round-23 S3: a new population, so its "historical" fast count is the one it was measured at and
            // entered the catalog with. It scales the SIZE of a single compilation rather than a case count,
            // which makes pinning it here matter more than usual — raising it lengthens every routine
            // `dotnet test` by a whole extra compile, not by one more cheap sample.
            [DeepPopulation.CompilerCostCorpusMappers] = 40
        };

        [Fact]
        public void Every_population_is_registered_and_no_pin_is_stale()
        {
            var declared = Enum.GetValues<DeepPopulation>();

            Assert.Equal(declared.Length, DeepTier.Populations.Count);
            Assert.Equal(declared.Length, PinnedFastCounts.Count);
            foreach (var population in declared)
            {
                Assert.Contains(population, DeepTier.Populations);
                Assert.True(PinnedFastCounts.ContainsKey(population),
                    $"{population} has no pinned fast count in this self-test — pin it in the same commit that adds it.");
            }
        }

        [Fact]
        public void The_fast_tier_is_byte_for_byte_the_historical_counts()
        {
            foreach (var (population, pinned) in PinnedFastCounts)
                Assert.True(DeepTier.CountFor(population, deep: false) == pinned,
                    $"{population}: fast count {DeepTier.CountFor(population, deep: false)} != pinned {pinned}. " + "The fast tier is time-capped; changing it is a deliberate decision made HERE, not a side " + "effect of a catalog edit.");
        }

        [Fact]
        public void The_deep_tier_raises_every_population()
        {
            foreach (var population in DeepTier.Populations)
            {
                var fast = DeepTier.CountFor(population, deep: false);
                var deep = DeepTier.CountFor(population, deep: true);
                Assert.True(deep > fast,
                    $"{population}: deep {deep} must exceed fast {fast}, or DWARF_DEEP=1 is vacuous for it.");
            }
        }

        [Fact]
        public void No_test_reads_the_environment_variable_outside_the_helper()
        {
            // Built by concatenation so this scan cannot match its own source.
            var read = new Regex(@"GetEnvironmentVariable\s*\(\s*""" + "DWARF_" + "DEEP" + '"');
            var offenders = new List<string>();

            foreach (var file in RepoPaths.SourceFiles(RepoPaths.Tests))
            {
                if (Path.GetFileName(file) == "DeepTier.cs")
                {
                    continue;
                }

                if (read.IsMatch(File.ReadAllText(file)))
                {
                    offenders.Add(Path.GetRelativePath(RepoPaths.Tests, file));
                }
            }

            Assert.True(offenders.Count == 0,
                "These test files read the deep-tier environment variable directly instead of registering a " + "DeepPopulation and calling DeepTier.Count — the self-test cannot see an unregistered " + "population: " + string.Join(", ", offenders));
        }

        [Fact]
        public void Every_registered_population_has_a_call_site()
        {
            // An entry nobody consumes is a knob wired to nothing: the deep tier would REPORT a raised count
            // that no test actually runs with.
            var sources = RepoPaths.SourceFiles(RepoPaths.Tests)
                .Where(f => Path.GetFileName(f) is not ("DeepTier.cs" or "DeepTierSelfTests.cs"))
                .Select(File.ReadAllText)
                .ToList();

            var unused = Enum.GetValues<DeepPopulation>()
                .Where(population => !sources.Any(s => s.Contains(
                    "DeepPopulation." + population,
                    StringComparison.Ordinal)))
                .ToList();

            Assert.True(unused.Count == 0,
                "These DeepPopulation entries have no call site (no test passes them to DeepTier.Count): " + string.Join(", ", unused));
        }
    }
}
