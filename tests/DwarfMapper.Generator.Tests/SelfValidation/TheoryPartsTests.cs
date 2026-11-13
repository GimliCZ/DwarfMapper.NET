// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using System.Globalization;
using System.Reflection;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>A theory split across classes still runs every row exactly once.</b> Round 34 (E12).
    ///     <para>
    ///         <see cref="TheoryParts" /> deals a heavy theory's rows across classes so they run in parallel. A part class
    ///         left out drops its rows and a part index written twice doubles them, both with every test green. So
    ///         each split theory's parts are read the way xunit reads them, through their <c>[MemberData]</c>, and their rows
    ///         together must be the unsplit member's rows, each once.
    ///     </para>
    /// </summary>
    public class TheoryPartsTests
    {
        [Fact]
        public void Every_split_theorys_parts_deal_each_row_exactly_once()
        {
            var splits = SplitTheories();

            // Non-vacuity: round 34 split seven theories.
            Assert.True(splits.Count >= 7, $"found {splits.Count} split theories: [{string.Join(", ", splits.Select(s => s.Name))}].");

            var faults = new List<string>();
            foreach (var split in splits)
            {
                var whole = Rows(split.MemberType, split.MemberName, []);
                var dealt = split.Parts.SelectMany(p => Rows(split.MemberType, split.MemberName, [p])).ToList();

                if (split.Parts.Count < 2)
                {
                    faults.Add($"{split.Name} has one part.");
                }

                if (split.Parts.Distinct().Count() != split.Parts.Count)
                {
                    faults.Add($"{split.Name} declares a part index twice: [{string.Join(", ", split.Parts)}].");
                }

                var missing = whole.Except(dealt, StringComparer.Ordinal).ToList();
                if (missing.Count > 0 || dealt.Count != whole.Count)
                {
                    faults.Add($"{split.Name}: the unsplit member has {whole.Count} rows, the parts deal {dealt.Count}; " +
                               $"not dealt: [{string.Join("; ", missing.Take(5))}].");
                }
            }

            Assert.True(faults.Count == 0, string.Join("\n", faults));
        }

        /// <summary>
        ///     Every theory whose <c>[MemberData]</c> passes one int (a part number) to a member of another class, grouped
        ///     by that class, the method and the data member.
        /// </summary>
        private static List<(string Name, Type MemberType, string MemberName, List<int> Parts)> SplitTheories()
        {
            return typeof(TheoryPartsTests).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods()
                                  .Where(m => m.GetCustomAttribute<TheoryAttribute>() is not null)
                                  .SelectMany(m => m.GetCustomAttributes<MemberDataAttribute>(), (m, d) => (Type: t, Method: m, Data: d)))
                .Where(x => x.Data is { Parameters: [int] } && x.Data.MemberType is not null && x.Data.MemberType != x.Type)
                .GroupBy(x => (MemberType: x.Data.MemberType!, x.Method.Name, x.Data.MemberName))
                .Select(g => (Name: g.Key.MemberType.Name + "." + g.Key.Name, g.Key.MemberType, g.Key.MemberName,
                              Parts: g.Select(x => (int)x.Data.Parameters[0]).ToList()))
                .ToList();
        }

        private static List<string> Rows(Type type, string member, object[] args)
        {
            var method = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                             .Single(m => m.Name == member && m.GetParameters().Length == args.Length);
            return ((IEnumerable)method.Invoke(null, args)!).Cast<object[]>().Select(Key).ToList();
        }

        private static string Key(object[] row)
        {
            return string.Join("|", row.Select(v => v is int[] a
                ? "[" + string.Join(",", a) + "]"
                : Convert.ToString(v, CultureInfo.InvariantCulture)));
        }
    }
}
