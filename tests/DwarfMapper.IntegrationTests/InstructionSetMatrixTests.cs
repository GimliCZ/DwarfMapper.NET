// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Numerics;

namespace DwarfMapper.IntegrationTests
{
    public class IsaWidenSrc
    {
        public int[] V { get; set; } = [];
    }

    public class IsaWidenDst
    {
        public long[] V { get; set; } = [];
    }

    public struct IsaVecSrc
    {
        public float X;

        public float Y;

        public float Z;
    }

    public struct IsaVecDst
    {
        public float X;

        public float Y;

        public float Z;
    }

    public class IsaBlitSrc
    {
        public IsaVecSrc[] V { get; set; } = [];
    }

    public class IsaBlitDst
    {
        public IsaVecDst[] V { get; set; } = [];
    }

    [DwarfMapper]
    public partial class IsaMapper
    {
        public partial IsaWidenDst MapWiden(IsaWidenSrc s);

        public partial IsaBlitDst MapBlit(IsaBlitSrc s);
    }

    /// <summary>
    ///     The SIMD paths under a RESTRICTED instruction set.
    ///     <para>
    ///         Until now every test run on every machine and every CI runner has had AVX2 available, and CI's
    ///         cross-platform leg varies the OS but not the ISA. So <c>Vector.Widen</c>'s
    ///         <c>Vector.IsHardwareAccelerated == false</c> fallback — a branch the generator emits on purpose —
    ///         may never have executed in a test. That is a corpus hole of the shape this repository keeps
    ///         finding: the bug hides in what the corpus cannot generate, not in the code.
    ///     </para>
    ///     <para>
    ///         <c>scripts/isa-matrix.ps1</c> runs this assembly once per instruction-set configuration
    ///         (native, 128-bit, no AVX2, no hardware intrinsics at all). These tests are written so their
    ///         RESULTS must not depend on which one is active — the whole point is that a narrower machine
    ///         computes the same answers, not merely that it does not crash.
    ///     </para>
    /// </summary>
    public class InstructionSetMatrixTests
    {
        /// <summary>
        ///     Anti-vacuity, and the test that makes the matrix mean anything. The script exports what it
        ///     expects the runtime to report; without this, four runs of the SAME configuration would look
        ///     exactly like four runs of four configurations, and the whole matrix would be theatre.
        /// </summary>
        [Fact]
        public void The_matrix_script_really_did_change_the_instruction_set()
        {
            var expectAccel = Environment.GetEnvironmentVariable("DWARF_ISA_EXPECT_HWACCEL");
            var expectWidth = Environment.GetEnvironmentVariable("DWARF_ISA_EXPECT_WIDTH");

            if (expectAccel is null && expectWidth is null)
            {
                // Not running under the matrix — nothing is claimed, so nothing is asserted.
                return;
            }

            if (expectAccel is not null)
            {
                Assert.Equal(
                    bool.Parse(expectAccel),
                    Vector.IsHardwareAccelerated);
            }

            if (expectWidth is not null)
            {
                Assert.Equal(
                    int.Parse(expectWidth, CultureInfo.InvariantCulture),
                    Vector<int>.Count);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(31)]
        [InlineData(33)]
        [InlineData(1000)]
        public void The_SIMD_widen_equals_the_scalar_widen_at_every_vector_boundary(int n)
        {
            // Lengths straddle 4, 8, 16 and 32 so that whichever width the active ISA gives Vector<int>, some
            // of these fall exactly on its boundary and some leave a tail. The tail is where a width change
            // does its damage, and it moves as the width moves.
            var src = new int[n];
            for (var i = 0; i < n; i++) src[i] = i % 2 == 0 ? i * 7 : -(i * 13);

            var dst = new IsaMapper().MapWiden(new IsaWidenSrc { V = src });

            Assert.Equal(n, dst.V.Length);
            for (var i = 0; i < n; i++)
            {
                // The scalar oracle, computed here rather than trusted from the mapper.
                Assert.Equal((long)src[i], dst.V[i]);
            }
        }

        [Fact]
        public void The_widen_carries_sign_extension_and_the_extremes()
        {
            int[] src = [int.MinValue, -1, 0, 1, int.MaxValue];

            var dst = new IsaMapper().MapWiden(new IsaWidenSrc { V = src });

            Assert.Equal([(long)int.MinValue, -1L, 0L, 1L, int.MaxValue], dst.V);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(8)]
        [InlineData(17)]
        [InlineData(1000)]
        public void The_blit_is_width_independent(int n)
        {
            // The blit goes through Buffer.Memmove, which dispatches on SIZE at run time rather than on
            // Vector<T>'s compile-time width — so it should be the one path a narrower ISA does not touch.
            // Asserted rather than assumed, because "should" is how holes get in.
            var src = new IsaVecSrc[n];
            for (var i = 0; i < n; i++) src[i] = new IsaVecSrc { X = i, Y = i * 0.5f, Z = -i };

            var dst = new IsaMapper().MapBlit(new IsaBlitSrc { V = src });

            Assert.Equal(n, dst.V.Length);
            for (var i = 0; i < n; i++)
            {
                Assert.Equal(src[i].X, dst.V[i].X);
                Assert.Equal(src[i].Y, dst.V[i].Y);
                Assert.Equal(src[i].Z, dst.V[i].Z);
            }
        }

        [Fact]
        public void Float_bit_patterns_survive_the_blit_including_the_awkward_ones()
        {
            // A reinterpret must move BITS, not values. NaN, negative zero and the infinities are where a
            // path that quietly went through arithmetic would give itself away.
            IsaVecSrc[] src =
            [
                new() { X = float.NaN, Y = float.NegativeInfinity, Z = float.PositiveInfinity },
                new() { X = -0.0f, Y = float.Epsilon, Z = float.MaxValue },
            ];

            var dst = new IsaMapper().MapBlit(new IsaBlitSrc { V = src });

            Assert.Equal(
                BitConverter.SingleToInt32Bits(-0.0f),
                BitConverter.SingleToInt32Bits(dst.V[1].X));
            Assert.True(float.IsNaN(dst.V[0].X));
            Assert.True(float.IsNegativeInfinity(dst.V[0].Y));
            Assert.True(float.IsPositiveInfinity(dst.V[0].Z));
            Assert.Equal(float.Epsilon, dst.V[1].Y);
            Assert.Equal(float.MaxValue, dst.V[1].Z);
        }
    }
}
