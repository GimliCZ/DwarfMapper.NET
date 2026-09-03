// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // Zero-alloc span map, blit fast path (round 29, T0.2): when the element pair is proven layout-identical
    // (BlittableProof.CanReinterpret), the generated body is one MemoryMarshal.Cast<S, D>(src).CopyTo(dst)
    // block copy rather than the per-element loop. Runtime behaviour must match the loop exactly — same
    // values, same length guard, same remainder handling for a non-power-of-two/non-multiple length.

    public struct BlitVec3Src
    {
        public float X;
        public float Y;
        public float Z;
    }

    public struct BlitVec3Dst
    {
        public float X;
        public float Y;
        public float Z;
    }

    [DwarfMapper]
    public partial class SpanMapBlitMapper
    {
        // layout-identical element pair (same fields, same names, same order) -> block copy
        public partial void Map(ReadOnlySpan<BlitVec3Src> src, Span<BlitVec3Dst> dst);
    }

    public class SpanMapBlitRuntimeTests
    {
        private static BlitVec3Src[] MakeSource(int count)
        {
            var src = new BlitVec3Src[count];
            for (var i = 0; i < count; i++)
            {
                src[i] = new BlitVec3Src
                {
                    X = i,
                    Y = i + 0.5f,
                    Z = -i
                };
            }

            return src;
        }

        [Fact]
        public void Layout_identical_span_map_copies_every_element()
        {
            const int count = 1000;
            var src = MakeSource(count);
            var dst = new BlitVec3Dst[count];

            new SpanMapBlitMapper().Map(src, dst);

            for (var i = 0; i < count; i++)
            {
                Assert.Equal(src[i].X, dst[i].X);
                Assert.Equal(src[i].Y, dst[i].Y);
                Assert.Equal(src[i].Z, dst[i].Z);
            }
        }

        [Fact]
        public void Destination_too_small_throws_not_silent_truncation()
        {
            var src = MakeSource(10);
            var dst = new BlitVec3Dst[9]; // too small

            Assert.Throws<ArgumentException>(() => new SpanMapBlitMapper().Map(src, dst));
        }

        [Fact]
        public void Non_multiple_length_round_trips_without_a_remainder_defect()
        {
            // 997: not a multiple of any vector width and not a power of two — the case a widening/vectorized
            // kernel drops on the floor if its remainder handling is wrong. MemoryMarshal.Cast + CopyTo has no
            // remainder to handle (it's a runtime memmove sized in bytes), but this pins that guarantee at
            // this specific, deliberately awkward length rather than only at round numbers.
            const int count = 997;
            var src = MakeSource(count);
            var dst = new BlitVec3Dst[count];

            new SpanMapBlitMapper().Map(src, dst);

            for (var i = 0; i < count; i++)
            {
                Assert.Equal(src[i].X, dst[i].X);
                Assert.Equal(src[i].Y, dst[i].Y);
                Assert.Equal(src[i].Z, dst[i].Z);
            }
        }
    }
}
