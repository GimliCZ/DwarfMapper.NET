// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    public struct LsSrc
    {
        public long A;

        public int B;

        public int C;
    }

    public struct LsDst
    {
        public long A;

        public int B;

        public int C;
    }

    public class ListBlitSrc
    {
        public LsSrc[] FromArray { get; set; } = [];

        public List<LsSrc> FromList { get; set; } = [];

        public List<LsSrc> ListToList { get; set; } = [];
    }

    public class ListBlitDst
    {
        public List<LsDst> FromArray { get; set; } = [];

        public LsDst[] FromList { get; set; } = [];

        public List<LsDst> ListToList { get; set; } = [];
    }

    [DwarfMapper]
    public partial class ListBlitMapper
    {
        public partial ListBlitDst Map(ListBlitSrc s);
    }

    /// <summary>
    ///     <c>R25-02</c> / T2 runtime oracle — the block copy must produce exactly what the element loop would.
    ///     <para>
    ///         The <c>List&lt;T&gt;</c> destination carries a hazard an array does not:
    ///         <c>CollectionsMarshal.SetCount</c> makes the list report a <c>Count</c> before anything has been
    ///         written into it. If the count and the copied region ever disagreed, the caller would read
    ///         uninitialised memory as data — so <c>Count</c> is asserted alongside the values, not instead of
    ///         them.
    ///     </para>
    /// </summary>
    public class ListShapeBlitRuntimeTests
    {
        private static ListBlitSrc Sample()
        {
            var items = new[]
            {
                new LsSrc { A = 1, B = 2, C = 3 },
                new LsSrc { A = long.MaxValue, B = int.MinValue, C = 0 },
                new LsSrc { A = -7, B = 42, C = -42 },
            };

            return new ListBlitSrc
            {
                FromArray = items,
                FromList = [.. items],
                ListToList = [.. items],
            };
        }

        [Fact]
        public void Array_to_List_copies_every_element_and_reports_the_right_Count()
        {
            var dst = new ListBlitMapper().Map(Sample());

            Assert.Equal(3, dst.FromArray.Count);
            Assert.Equal(long.MaxValue, dst.FromArray[1].A);
            Assert.Equal(int.MinValue, dst.FromArray[1].B);
            Assert.Equal(-42, dst.FromArray[2].C);
        }

        [Fact]
        public void List_to_array_copies_every_element()
        {
            var dst = new ListBlitMapper().Map(Sample());

            Assert.Equal(3, dst.FromList.Length);
            Assert.Equal(1, dst.FromList[0].A);
            Assert.Equal(42, dst.FromList[2].B);
        }

        [Fact]
        public void List_to_List_copies_every_element()
        {
            var dst = new ListBlitMapper().Map(Sample());

            Assert.Equal(3, dst.ListToList.Count);
            Assert.Equal([1L, long.MaxValue, -7L], dst.ListToList.Select(x => x.A));
        }

        [Fact]
        public void The_destination_never_aliases_the_source()
        {
            // A reinterpret must COPY. Mutating the source after the map must not reach the destination —
            // the "assignable collection" bug this project refuses elsewhere.
            var src = Sample();
            var dst = new ListBlitMapper().Map(src);

            src.FromArray[0].A = 999;
            src.FromList[0] = new LsSrc { A = 999, B = 999, C = 999 };
            src.ListToList[0] = new LsSrc { A = 999, B = 999, C = 999 };

            Assert.Equal(1, dst.FromArray[0].A);
            Assert.Equal(1, dst.FromList[0].A);
            Assert.Equal(1, dst.ListToList[0].A);
        }

        [Fact]
        public void Empty_sources_produce_empty_destinations()
        {
            var dst = new ListBlitMapper().Map(new ListBlitSrc());

            Assert.Empty(dst.FromArray);
            Assert.Empty(dst.FromList);
            Assert.Empty(dst.ListToList);
        }

        [Fact]
        public void A_single_element_and_an_odd_length_are_copied_whole()
        {
            // Lengths that are not a multiple of any vector width — the classic place a block copy loses its
            // tail. Memmove has no tail, but the property is cheap to pin and expensive to discover.
            foreach (var n in new[] { 1, 3, 5, 7, 9, 17, 33 })
            {
                var items = new LsSrc[n];
                for (var i = 0; i < n; i++) items[i] = new LsSrc { A = i, B = i * 2, C = i * 3 };

                var dst = new ListBlitMapper().Map(new ListBlitSrc { FromArray = items, FromList = [.. items] });

                Assert.Equal(n, dst.FromArray.Count);
                Assert.Equal(n, dst.FromList.Length);
                for (var i = 0; i < n; i++)
                {
                    Assert.Equal(i, dst.FromArray[i].A);
                    Assert.Equal(i * 3, dst.FromList[i].C);
                }
            }
        }
    }
}
