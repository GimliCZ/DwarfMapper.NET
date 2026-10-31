// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>This assembly runs the tests most likely to settle a mutant run first</b> (round 34 E4,
    ///     <see cref="KillersFirstOrderer" />): the assembly names the orderer, its ranking ships beside the tests and names
    ///     classes that exist, a cheap decisive class outranks an expensive redundant one, and a ranked collection runs before
    ///     an unranked one.
    /// </summary>
    public class KillersFirstOrdererTests
    {
        private const string OrdererName = "DwarfMapper.Generator.Tests.KillersFirstOrderer";

        private static readonly Regex PartNumber = new(@"Part\d+$", RegexOptions.CultureInvariant);

        [Theory]
        [InlineData("Xunit.TestCollectionOrdererAttribute")]
        [InlineData("Xunit.TestCaseOrdererAttribute")]
        public void The_assembly_orders_its_tests_with_the_killers_first_orderer(string attribute)
        {
            var declared = typeof(KillersFirstOrdererTests).Assembly.GetCustomAttributesData()
                                                             .Where(a => a.AttributeType.FullName == attribute)
                                                             .Select(a => a.ConstructorArguments[0].Value as string)
                                                             .ToList();

            Assert.Equal(new[] { OrdererName }, declared);
        }

        [Fact]
        public void The_ranking_ships_beside_the_tests_and_names_only_classes_that_exist()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "MutationKillRanking.tsv");
            Assert.True(File.Exists(path), $"{path} is missing: the project copies MutationKillRanking.tsv to its output.");

            var classes = File.ReadLines(path)
                              .Select(l => l.Split('\t'))
                              .Where(p => p.Length == 3 && p[0] == "class")
                              .Select(p => p[2])
                              .ToList();

            // Non-vacuity: the last generator, pipeline, codefixes and doctooling reports name about 180 classes.
            Assert.True(classes.Count >= 100, $"the ranking names only {classes.Count} test classes.");

            var assembly = typeof(KillersFirstOrdererTests).Assembly;
            var missing = classes.Where(c => assembly.GetType(c) is null).ToList();
            Assert.True(missing.Count == 0,
                "MutationKillRanking.tsv ranks test classes this assembly no longer has: [" + string.Join(", ", missing) +
                "]. Regenerate it from the latest mutation reports with scripts/kill-ranking.py.");
        }

        /// <summary>
        ///     Cost-weighted, not most kills (round 34 mutation study). Stryker tests mutants whose covering tests do not
        ///     overlap in one run and ends it only when every one is decided. In the EquatableArray trace leg, ranked by total
        ///     kills, a run killed its broad mutant 13 s in, then ran four FeatureCombinationFuzz parts (about 40 s each, every
        ///     row failing) before EquatableArrayCoverageTests, the only killer of the other two, at 218 s. A cheap class that
        ///     alone kills a mutant has to run before an expensive suite whose kills other classes already made.
        /// </summary>
        [Fact]
        public void A_cheap_class_that_alone_kills_a_mutant_runs_before_an_expensive_fuzz_suite()
        {
            var ranks = KillersFirstOrderer.ShippedClassKills;
            var cheap = typeof(Coverage.EquatableArrayCoverageTests).FullName!;
            var expensive = typeof(FeatureCombinationFuzzTests).FullName!;

            Assert.True(KillersFirstOrderer.RankOf(cheap, ranks) > KillersFirstOrderer.RankOf(expensive, ranks),
                $"MutationKillRanking.tsv runs {expensive} (score {KillersFirstOrderer.RankOf(expensive, ranks)}) before " +
                $"{cheap} (score {KillersFirstOrderer.RankOf(cheap, ranks)}). Regenerate it with scripts/kill-ranking.py " +
                "--durations, which ranks by not-yet-killed mutants per second instead of total kills.");
        }

        [Fact]
        public void More_kills_run_earlier_and_the_unranked_run_last()
        {
            var kills = new Dictionary<string, int>(StringComparer.Ordinal) { ["Ns.Most"] = 9, ["Ns.Fewest"] = 1 };

            var ordered = KillersFirstOrderer.InRankOrder(new[] { "Ns.Unranked", "Ns.Fewest", "Ns.Most" }, n => n, kills, n => n);

            Assert.Equal(new[] { "Ns.Most", "Ns.Fewest", "Ns.Unranked" }, ordered);
        }

        [Fact]
        public void A_collection_is_ranked_by_the_class_it_runs()
        {
            Assert.Equal("Ns.Some.Tests", KillersFirstOrderer.ClassOf("Test collection for Ns.Some.Tests"));
        }

        [Fact]
        public void A_part_class_ranks_as_the_class_it_was_split_from()
        {
            var kills = new Dictionary<string, int>(StringComparer.Ordinal) { ["Ns.Outer"] = 7, ["Ns.Outer.M"] = 3, ["Ns.OuterPart9"] = 9 };

            Assert.Equal(7, KillersFirstOrderer.RankOf("Ns.OuterPart1", kills));
            Assert.Equal(3, KillersFirstOrderer.RankOf("Ns.OuterPart1.M", kills));
            Assert.Equal(9, KillersFirstOrderer.RankOf("Ns.OuterPart9", kills));
            Assert.Equal(0, KillersFirstOrderer.RankOf("Ns.OtherPart1", kills));
            Assert.Equal(0, KillersFirstOrderer.RankOf("Ns.Unranked", kills));
        }

        /// <summary>
        ///     Round 34 (E12). A part class (<see cref="TheoryParts" />) with no rank of its own ranks as the class it was
        ///     split from does in the shipped ranking, so splitting a theory does not send its rows to the end of a
        ///     Stryker session.
        /// </summary>
        [Fact]
        public void Every_part_class_keeps_the_rank_of_the_class_it_was_split_from()
        {
            var kills = KillersFirstOrderer.ShippedClassKills;
            var assembly = typeof(KillersFirstOrdererTests).Assembly;
            var parts = assembly.GetTypes()
                .Select(t => (Part: t, Outer: assembly.GetType(PartNumber.Replace(t.FullName!, ""))))
                .Where(x => x.Outer is not null && x.Outer != x.Part && !kills.ContainsKey(x.Part.FullName!))
                .ToList();

            // Non-vacuity: round 34 split seven theories, among them ranked classes.
            Assert.True(parts.Count(x => kills.ContainsKey(x.Outer!.FullName!)) >= 5,
                $"found {parts.Count} part classes, few of them split from a ranked class.");

            var demoted = parts
                .Where(x => KillersFirstOrderer.RankOf(x.Part.FullName!, kills) != kills.GetValueOrDefault(x.Outer!.FullName!))
                .Select(x => x.Part.FullName)
                .ToList();

            Assert.True(demoted.Count == 0, "Part classes not ranked as the class they were split from: [" + string.Join(", ", demoted) + "].");
        }
    }
}
