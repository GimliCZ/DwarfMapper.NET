// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestCollectionOrderer("DwarfMapper.Generator.Tests.KillersFirstOrderer", "DwarfMapper.Generator.Tests")]
[assembly: TestCaseOrderer("DwarfMapper.Generator.Tests.KillersFirstOrderer", "DwarfMapper.Generator.Tests")]

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <b>The tests that have killed the most mutants run first.</b> Round 34 (E4).
    ///     <para>
    ///         Stryker runs a mutant's covering tests in one session and stops it at the first failure. xunit's default
    ///         order is stable but arbitrary, so a mutant covered by thousands of tests often ran most of them before
    ///         the one that kills it: in the generator leg, 40 sessions over 20 s took 54 % of the session time.
    ///         <c>MutationKillRanking.tsv</c> counts each class's and method's kills in the last mutation reports
    ///         (<c>scripts/kill-ranking.py</c> writes it), so the likely killers run first and a killed mutant's session
    ///         ends early. Measured on the generator leg in CI's container shape, concurrency 2, on top of E3 and E5:
    ///         mutation testing 29:11 to 20:25 and the leg 37:26 to 28:25, with every one of the 867 mutants' statuses
    ///         unchanged.
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
        ///     The order both use: most kills first, then the unranked in a stable order of their unique ids, as xunit's
        ///     own default orders them.
        /// </summary>
        internal static List<T> InRankOrder<T>(
            IEnumerable<T> items,
            Func<T, string> rankedName,
            IReadOnlyDictionary<string, int> kills,
            Func<T, string> uniqueId)
        {
            return items.OrderByDescending(i => kills.GetValueOrDefault(rankedName(i)))
                        .ThenBy(uniqueId, StringComparer.Ordinal)
                        .ToList();
        }

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
