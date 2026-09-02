// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.DifferentialTests
{
    /// <summary>
    ///     One payload, three mappers, compared member by member.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every real defect found in Round 18 hid behind a corpus hole rather than behind subtle generator
    ///         code, and this repository's corpus is self-authored — so it shares its author's blind spots by
    ///         construction. The fuzzer and <c>[RoundTrip]</c> prove self-consistency, which is a different and
    ///         weaker property. Mapperly's semantics were designed by other people; AutoMapper's by others again.
    ///         Agreement with them is evidence from outside.
    ///     </para>
    ///     <para>
    ///         A disagreement is not automatically a DwarfMapper bug. It is a question with three answers:
    ///         DwarfMapper is wrong, the oracle is wrong, or the two differ deliberately — and the third belongs in
    ///         <see cref="AcceptedDivergences" /> with its reason written down. That list, not these passing
    ///         tests, is what this project produces.
    ///     </para>
    /// </remarks>
    public class ShapeAgreementTests
    {
        public static TheoryData<string, string> Comparisons()
        {
            var data = new TheoryData<string, string>();
            foreach (var c in ShapeCatalog.All()) data.Add(c.Shape, c.Oracle);
            return data;
        }

        [Theory]
        [MemberData(nameof(Comparisons))]
        public void DwarfMapper_and_the_oracle_produce_the_same_object(string shape, string oracle)
        {
            var comparison = ShapeCatalog.All().Single(c =>
                string.Equals(c.Shape, shape, StringComparison.Ordinal) && string.Equals(c.Oracle, oracle, StringComparison.Ordinal));

            var unexplained = MemberComparer.Differences(comparison.Dwarf, comparison.OracleValue)
                .Where(d => !AcceptedDivergences.IsAccepted(shape, oracle, d.Split(':')[0]))
                .ToList();

            Assert.True(unexplained.Count == 0,
                $"Shape '{shape}': DwarfMapper and {oracle} produced different objects.\n\n  " +
                string.Join("\n  ", unexplained) +
                $"\n\n(left = DwarfMapper, right = {oracle})\n\n" +
                "One of three things is true, and the harness cannot tell you which:\n" +
                "  1. DwarfMapper is wrong — the case this project exists to find.\n" +
                $"  2. {oracle} is wrong — it happens; say so in the accepted-divergence entry.\n" +
                "  3. They differ deliberately — add an AcceptedDivergence naming the axis AND the reason. " +
                "An entry with no reason makes this green while proving nothing.");
        }

        [Fact]
        public void The_catalogue_is_not_empty_and_covers_both_oracles()
        {
            // An empty or half-wired catalogue would make every theory above vacuous while the suite stayed
            // green — the "skipped and passed look identical" failure this repository refuses elsewhere too.
            var all = ShapeCatalog.All().ToList();

            Assert.True(all.Count >= 10, $"Only {all.Count} comparisons — the catalogue looks half-wired.");
            Assert.Contains(all, c => string.Equals(c.Oracle, Oracles.Mapperly, StringComparison.Ordinal));
            Assert.Contains(all, c => string.Equals(c.Oracle, Oracles.AutoMapper, StringComparison.Ordinal));
        }
    }
}
