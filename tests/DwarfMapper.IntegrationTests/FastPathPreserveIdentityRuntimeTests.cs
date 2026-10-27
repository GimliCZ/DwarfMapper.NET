// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // Under ReferenceHandling = Preserve, one source collection reached twice is ONE target collection (EmitArray's B3
    // guarantee). The block copies and the SIMD widen are static helpers with no context to register in, and before
    // round 33 they were reached under Preserve anyway, so each member below got its own copy of the shared source.

    public struct FpSrcV
    {
        public int X;

        public int Y;
    }

    public struct FpDstV
    {
        public int X;

        public int Y;
    }

    public enum FpSrcE
    {
        A = 1,
        B = 2
    }

    public enum FpDstE
    {
        A = 1,
        B = 2
    }

    public class FpSharedSrc
    {
        public FpSrcV[] Blit1 { get; set; } = [];

        public FpSrcV[] Blit2 { get; set; } = [];

        public FpSrcE[] Enum1 { get; set; } = [];

        public FpSrcE[] Enum2 { get; set; } = [];

        public FpSrcV[] ToList1 { get; set; } = [];

        public FpSrcV[] ToList2 { get; set; } = [];

        public List<FpSrcV> FromList1 { get; set; } = [];

        public List<FpSrcV> FromList2 { get; set; } = [];

        public int[] Widen1 { get; set; } = [];

        public int[] Widen2 { get; set; } = [];
    }

    public class FpSharedDst
    {
        public FpDstV[] Blit1 { get; set; } = [];

        public FpDstV[] Blit2 { get; set; } = [];

        public FpDstE[] Enum1 { get; set; } = [];

        public FpDstE[] Enum2 { get; set; } = [];

        public List<FpDstV> ToList1 { get; set; } = [];

        public List<FpDstV> ToList2 { get; set; } = [];

        public FpDstV[] FromList1 { get; set; } = [];

        public FpDstV[] FromList2 { get; set; } = [];

        public long[] Widen1 { get; set; } = [];

        public long[] Widen2 { get; set; } = [];
    }

    // ByValue makes the enum array a reinterpret, so it reaches its block copy (round 25 T1).
    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve, EnumStrategy = EnumStrategy.ByValue)]
    public partial class FpPreserveMapper
    {
        public partial FpSharedDst Map(FpSharedSrc s);
    }

    public class FastPathPreserveIdentityRuntimeTests
    {
        [Fact]
        public void Under_Preserve_one_source_reached_twice_is_one_target_through_every_fast_path()
        {
            FpSrcV[] structs = [new() { X = 1, Y = 2 }, new() { X = 3, Y = 4 }];
            FpSrcE[] enums = [FpSrcE.A, FpSrcE.B];
            FpSrcV[] toList = [new() { X = 5, Y = 6 }];
            List<FpSrcV> fromList = [new() { X = 7, Y = 8 }];
            int[] ints = [1, int.MaxValue, int.MinValue];

            var d = new FpPreserveMapper().Map(new FpSharedSrc
            {
                Blit1 = structs, Blit2 = structs,
                Enum1 = enums, Enum2 = enums,
                ToList1 = toList, ToList2 = toList,
                FromList1 = fromList, FromList2 = fromList,
                Widen1 = ints, Widen2 = ints
            });

            Assert.Same(d.Blit1, d.Blit2);
            Assert.Same(d.Enum1, d.Enum2);
            Assert.Same(d.ToList1, d.ToList2);
            Assert.Same(d.FromList1, d.FromList2);
            Assert.Same(d.Widen1, d.Widen2);

            // ...and the one target holds the right values: the registering helper is a correct copy, not just a shared one.
            Assert.Equal(3, d.Blit1[1].X);
            Assert.Equal(4, d.Blit1[1].Y);
            Assert.Equal([FpDstE.A, FpDstE.B], d.Enum1);
            Assert.Equal(6, d.ToList1[0].Y);
            Assert.Equal(7, d.FromList1[0].X);
            Assert.Equal([1L, int.MaxValue, int.MinValue], d.Widen1);
        }
    }
}
