// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Text.RegularExpressions;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestCollectionOrderer("DwarfMapper.Generator.Tests.KillersFirstOrderer", "DwarfMapper.Generator.Tests")]
[assembly: TestCaseOrderer("DwarfMapper.Generator.Tests.KillersFirstOrderer", "DwarfMapper.Generator.Tests")]

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <b>The tests most likely to settle a mutant run run first.</b> Round 34 (E4; cost-weighted since the round's
    ///     mutation study).
    ///     <para>
    ///         Stryker runs a mutant's covering tests in one session and stops it at the first failure. xunit's default
    ///         order is stable but arbitrary, so a mutant covered by thousands of tests often ran most of them before
    ///         the one that kills it: in the generator leg, 40 sessions over 20 s took 54 % of the session time.
    ///         <c>MutationKillRanking.tsv</c> ranks the classes and methods that killed mutants in the last mutation reports
    ///         (<c>scripts/kill-ranking.py</c> writes it), so the likely killers run first and a killed mutant's session
    ///         ends early. Measured on the generator leg in CI's container shape, concurrency 2, on top of E3 and E5:
    ///         mutation testing 29:11 to 20:25 and the leg 37:26 to 28:25, with every one of the 867 mutants' statuses
    ///         unchanged.
    ///     </para>
    ///     <para>
    ///         The rank is the min-sum set cover greedy, not the kill count: most not-yet-killed mutants per second of serial
    ///         runtime first. Stryker tests several mutants in one session when their covering tests do not overlap and ends
    ///         it only when every one is decided, and by kill count the golden and fuzz suites, whose rows each re-kill the
    ///         same broad mutants, ran ahead of the small unit classes that alone kill the narrow ones; one EquatableArray
    ///         session waited 205 s behind four FeatureCombinationFuzz parts for EquatableArrayCoverageTests.
    ///     </para>
    ///     <para>
    ///         The order changes no verdict: the same tests run, a killed mutant is killed by whichever fails first, and a
    ///         surviving one runs every covering test as before. A stale ranking only costs time; refresh it when a round
    ///         renames or adds test classes. <c>KillersFirstOrdererTests</c> holds the wiring, the file and the order.
    ///     </para>
    /// </summary>
    public sealed class KillersFirstOrderer : ITestCollectionOrderer, ITestCaseOrderer
    {
        private const string CollectionPrefix = "Test collection for ";

        private static readonly Lazy<Ranking> Ranks = new(Ranking.Load);

        /// <summary>A part class's suffix (<see cref="TheoryParts" />): "Part" and its number, ending the class name.</summary>
        private static readonly Regex PartSuffix = new(@"Part\d+(?=\.[^.]+$|$)", RegexOptions.CultureInvariant);

        public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
        {
            return InRankOrder(testCollections, c => ClassOf(c.DisplayName), Ranks.Value.Classes, c => c.UniqueID.ToString());
        }

        public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
            where TTestCase : ITestCase
        {
            return InRankOrder(testCases,
                t => t.TestMethod.TestClass.Class.Name + "." + t.TestMethod.Method.Name,
                Ranks.Value.Methods,
                t => t.UniqueID);
        }

        /// <summary>
        ///     The order both use: highest score first, then the unranked in a stable order of their unique ids, as xunit's
        ///     own default orders them.
        /// </summary>
        internal static List<T> InRankOrder<T>(
            IEnumerable<T> items,
            Func<T, string> rankedName,
            IReadOnlyDictionary<string, int> kills,
            Func<T, string> uniqueId)
        {
            return items.OrderByDescending(i => RankOf(rankedName(i), kills))
                        .ThenBy(uniqueId, StringComparer.Ordinal)
                        .ToList();
        }

        /// <summary>
        ///     A class's or method's kills. A part class takes its outer class's rank: round 34 (E12) deals a heavy theory's
        ///     rows into classes named after it with a part number (<see cref="TheoryParts" />), and until the ranking is
        ///     regenerated with their names, "Ns.OuterPart2" ranks as "Ns.Outer" and "Ns.OuterPart2.Method" as
        ///     "Ns.Outer.Method" instead of falling to the end.
        /// </summary>
        internal static int RankOf(string name, IReadOnlyDictionary<string, int> kills)
        {
            if (kills.TryGetValue(name, out var exact))
            {
                return exact;
            }

            var outer = PartSuffix.Replace(name, "", 1);
            return outer == name ? 0 : kills.GetValueOrDefault(outer);
        }

        /// <summary>The shipped ranking's class kills, for the test that holds every part class to its outer rank.</summary>
        internal static IReadOnlyDictionary<string, int> ShippedClassKills => Ranks.Value.Classes;

        /// <summary>The class a per-class collection runs: xunit names it "Test collection for &lt;class&gt;".</summary>
        internal static string ClassOf(string collectionDisplayName)
        {
            return collectionDisplayName.StartsWith(CollectionPrefix, StringComparison.Ordinal)
                ? collectionDisplayName[CollectionPrefix.Length..]
                : collectionDisplayName;
        }

        private sealed record Ranking(Dictionary<string, int> Classes, Dictionary<string, int> Methods)
        {
            internal static Ranking Load()
            {
                var classes = new Dictionary<string, int>(StringComparer.Ordinal);
                var methods = new Dictionary<string, int>(StringComparer.Ordinal);
                var path = Path.Combine(AppContext.BaseDirectory, "MutationKillRanking.tsv");
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadLines(path))
                    {
                        var parts = line.Split('\t');
                        if (parts.Length == 3 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var kills))
                        {
                            (parts[0] == "class" ? classes : methods)[parts[2]] = kills;
                        }
                    }
                }

                return new Ranking(classes, methods);
            }
        }
    }
}
