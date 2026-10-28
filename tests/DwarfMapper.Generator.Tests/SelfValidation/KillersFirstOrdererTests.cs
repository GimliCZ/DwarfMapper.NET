// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>This assembly runs the tests that have killed the most mutants first</b> (round 34 E4,
    ///     <see cref="KillersFirstOrderer" />): the assembly names the orderer, its ranking ships beside the tests and names
    ///     classes that exist, and a ranked collection runs before an unranked one.
    /// </summary>
    public class KillersFirstOrdererTests
    {
        private const string OrdererName = "DwarfMapper.Generator.Tests.KillersFirstOrderer";

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
    }
}
