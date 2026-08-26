// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    public class NfLine
    {
        public string Sku { get; set; } = "";

        public int[] Quantities { get; set; } = [];
    }

    public class NfOrder
    {
        public int Id { get; set; }

        public List<NfLine> Lines { get; set; } = [];

        public int[] Totals { get; set; } = [];
    }

    public class NfLineDto
    {
        public string Sku { get; set; } = "";

        public List<long> Quantities { get; set; } = [];
    }

    public class NfOrderDto
    {
        public int Id { get; set; }

        public List<NfLineDto> Lines { get; set; } = [];

        public List<long> Totals { get; set; } = [];
    }

    [DwarfMapper]
    public partial class NestedFillMapper
    {
        public partial NfOrderDto Map(NfOrder o);
    }

    /// <summary>
    ///     Nested decomposition, end to end (round 26). An object breaks down into its members, and a
    ///     collection can sit at any depth — so this graph deliberately mixes both fill strategies in one map:
    ///     <c>Lines</c> is a list of REFERENCE elements and takes the <c>Add</c> path, while <c>Totals</c> and
    ///     each line's <c>Quantities</c> are value elements and take the span fill.
    ///     <para>
    ///         The generator-side tests prove which shape is emitted where. This proves the graph still comes
    ///         out right when both run in the same call — the case where a shared index or a leaked span would
    ///         corrupt data rather than merely slow it down.
    ///     </para>
    /// </summary>
    public class ListFillNestedRuntimeTests
    {
        private static NfOrder Sample()
        {
            return new NfOrder
            {
                Id = 42,
                Totals = [10, 20, 30],
                Lines =
                [
                    new NfLine { Sku = "A", Quantities = [1, 2, 3] },
                    new NfLine { Sku = "B", Quantities = [] },
                    new NfLine { Sku = "C", Quantities = [7] },
                ],
            };
        }

        [Fact]
        public void Both_fill_strategies_produce_the_right_graph_in_one_call()
        {
            var dst = new NestedFillMapper().Map(Sample());

            Assert.Equal(42, dst.Id);
            Assert.Equal([10L, 20L, 30L], dst.Totals);

            Assert.Equal(3, dst.Lines.Count);
            Assert.Equal("A", dst.Lines[0].Sku);
            Assert.Equal([1L, 2L, 3L], dst.Lines[0].Quantities);
            Assert.Empty(dst.Lines[1].Quantities);
            Assert.Equal([7L], dst.Lines[2].Quantities);
        }

        [Fact]
        public void Sibling_nested_lists_do_not_share_state()
        {
            // The failure a shared index or a re-used span would produce: line 0's quantities bleeding into
            // line 2's, or a count taken from the wrong sibling. Deliberately uneven lengths — 3, 0, 1 — so a
            // shared index would desynchronise immediately.
            var dst = new NestedFillMapper().Map(Sample());

            Assert.Equal(3, dst.Lines[0].Quantities.Count);
            Assert.Empty(dst.Lines[1].Quantities);
            Assert.Single(dst.Lines[2].Quantities);
        }

        [Fact]
        public void A_wide_graph_round_trips_every_element()
        {
            // Enough lines that any per-element bookkeeping error accumulates into a visible mismatch.
            var src = new NfOrder { Id = 1, Totals = new int[64] };
            for (var i = 0; i < 64; i++) src.Totals[i] = i;
            for (var i = 0; i < 50; i++)
            {
                var q = new int[i % 7];
                for (var j = 0; j < q.Length; j++) q[j] = i * 100 + j;
                src.Lines.Add(new NfLine { Sku = "s" + i, Quantities = q });
            }

            var dst = new NestedFillMapper().Map(src);

            Assert.Equal(64, dst.Totals.Count);
            Assert.Equal(50, dst.Lines.Count);
            for (var i = 0; i < 64; i++) Assert.Equal(i, dst.Totals[i]);
            for (var i = 0; i < 50; i++)
            {
                Assert.Equal(i % 7, dst.Lines[i].Quantities.Count);
                for (var j = 0; j < i % 7; j++) Assert.Equal(i * 100 + j, dst.Lines[i].Quantities[j]);
            }
        }

        [Fact]
        public void An_empty_graph_produces_empty_collections_at_every_level()
        {
            var dst = new NestedFillMapper().Map(new NfOrder());

            Assert.Empty(dst.Totals);
            Assert.Empty(dst.Lines);
        }
    }
}
