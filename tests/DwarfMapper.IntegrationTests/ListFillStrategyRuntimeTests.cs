// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    public class LfSrc
    {
        public int[] Numbers { get; set; } = [];

        public List<int> FromList { get; set; } = [];
    }

    public class LfDst
    {
        public List<long> Numbers { get; set; } = [];

        public List<long> FromList { get; set; } = [];
    }

    [DwarfMapper]
    public partial class ListFillMapper
    {
        public partial LfDst Map(LfSrc s);
    }

    /// <summary>
    ///     Runtime oracle for the span-fill strategy (round 26). The generator-side tests pin WHICH shape is
    ///     emitted; these pin that it produces the same answer the <c>Add</c> loop did.
    ///     <para>
    ///         Order is the property most at risk. <c>Add</c> appends, so ordering is implicit; the span fill
    ///         writes to <c>__d[__i++]</c>, where an off-by-one or a mis-sized list would corrupt or throw
    ///         rather than merely slow things down.
    ///     </para>
    /// </summary>
    public class ListFillStrategyRuntimeTests
    {
        [Fact]
        public void Elements_arrive_in_source_order_with_the_right_count()
        {
            var src = new LfSrc { Numbers = [5, 3, 9, 1, 7], FromList = [2, 4, 6] };

            var dst = new ListFillMapper().Map(src);

            Assert.Equal([5L, 3L, 9L, 1L, 7L], dst.Numbers);
            Assert.Equal([2L, 4L, 6L], dst.FromList);
        }

        [Fact]
        public void An_empty_source_produces_an_empty_list_not_a_defaulted_one()
        {
            // The failure this guards: SetCount(n) on an empty source must leave Count == 0, not a list of
            // zeros. A mis-sized fill would surface here as phantom elements rather than as an exception.
            var dst = new ListFillMapper().Map(new LfSrc());

            Assert.Empty(dst.Numbers);
            Assert.Empty(dst.FromList);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(7)]
        [InlineData(64)]
        [InlineData(1000)]
        public void Every_length_round_trips_exactly(int n)
        {
            var numbers = new int[n];
            for (var i = 0; i < n; i++) numbers[i] = i * 3 - 1;

            var dst = new ListFillMapper().Map(new LfSrc { Numbers = numbers });

            Assert.Equal(n, dst.Numbers.Count);
            for (var i = 0; i < n; i++) Assert.Equal(i * 3 - 1, dst.Numbers[i]);
        }

        [Fact]
        public void Boundary_values_survive_the_widening_conversion()
        {
            var src = new LfSrc { Numbers = [int.MinValue, -1, 0, 1, int.MaxValue] };

            var dst = new ListFillMapper().Map(src);

            Assert.Equal([(long)int.MinValue, -1L, 0L, 1L, int.MaxValue], dst.Numbers);
        }

        [Fact]
        public void The_destination_does_not_alias_the_source()
        {
            var numbers = new[] { 1, 2, 3 };
            var dst = new ListFillMapper().Map(new LfSrc { Numbers = numbers });

            numbers[0] = 999;

            Assert.Equal(1L, dst.Numbers[0]);
        }
    }
}
