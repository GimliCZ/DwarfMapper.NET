// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DwarfMapper.IntegrationTests
{
    /// <summary>
    ///     The runtime oracle for the BCL layouts <c>LayoutHygiene</c> states as facts.
    ///     <para>
    ///         The generator runs on netstandard2.0 against a compilation's SYMBOLS; it cannot execute the
    ///         consumer's runtime, so for a metadata struct with no source declaration it has two honest options:
    ///         refuse, or rely on a documented layout. It refuses by default — that is why a native-sized integer
    ///         is refused outright, since its width belongs to the machine rather than to the type. For
    ///         <c>Guid</c>, <c>DateTime</c>, <c>TimeSpan</c> and <c>decimal</c> it relies, because their layouts
    ///         are fixed by the platform ABI and part of the types' contracts.
    ///     </para>
    ///     <para>
    ///         "Documented" is not "verified", so this file verifies it. These tests execute on the real runtime
    ///         and fail the build the day any of those layouts moves — which is the only way a hard-coded table
    ///         inside a source generator can be allowed to exist. Deliberately in the INTEGRATION project: the
    ///         generator's own test project asserts what the generator BELIEVES; only a running test can assert
    ///         what the runtime DOES.
    ///     </para>
    /// </summary>
    public class BclLayoutFactsTests
    {
        /// <summary>
        ///     Sizes must match the table in <c>LayoutHygiene.FixedLayoutBclTypes</c> exactly. A mismatch here
        ///     means the generator is printing byte counts that are wrong on this machine.
        /// </summary>
        [Fact]
        public void The_fixed_layout_BCL_types_have_the_sizes_the_generator_assumes()
        {
            Assert.Equal(16, Unsafe.SizeOf<Guid>());
            Assert.Equal(8, Unsafe.SizeOf<DateTime>());
            Assert.Equal(8, Unsafe.SizeOf<TimeSpan>());
            Assert.Equal(16, Unsafe.SizeOf<decimal>());
        }

        /// <summary>
        ///     Alignment is the half a size check cannot see, and it is what decides where the NEXT field lands:
        ///     a <c>Guid</c> is 16 bytes but only 4-aligned, so a struct that assumed 16-alignment would compute
        ///     every subsequent offset wrongly. Measured the way alignment is observable from managed code —
        ///     place a <c>byte</c> in front of the type and read the offset the runtime chooses for it.
        /// </summary>
        [Theory]
        [InlineData(typeof(AlignProbe<Guid>), 4)]
        [InlineData(typeof(AlignProbe<DateTime>), 8)]
        [InlineData(typeof(AlignProbe<TimeSpan>), 8)]
        [InlineData(typeof(AlignProbe<decimal>), 8)]
        public void The_fixed_layout_BCL_types_have_the_alignments_the_generator_assumes(Type probe, int expected)
        {
            // The probe is {byte Pad; T Value}: the runtime rounds Value's offset up to T's alignment, so the
            // offset IS the alignment for any type aligned to more than one byte.
            var offset = Marshal.OffsetOf(probe, "Value").ToInt32();

            Assert.Equal(expected, offset);
        }

        /// <summary>
        ///     The composite check: a DTO shaped like a real one, whose size the generator computes from the
        ///     table above plus its own sequential-layout arithmetic. If the parts are right but the composition
        ///     is wrong, this is what catches it — and it is the exact fixture
        ///     <c>LayoutHygieneTests.Measure_sizes_a_DTO_whose_first_field_is_a_Guid</c> pins at 40.
        /// </summary>
        [Fact]
        public void A_Guid_bearing_DTO_is_the_size_the_generator_computes()
        {
            Assert.Equal(40, Unsafe.SizeOf<OrderDtoProbe>());
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AlignProbe<T>
            where T : struct
        {
            public byte Pad;
            public T Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct OrderDtoProbe
        {
            public Guid Id;
            public long Amount;
            public byte Flag;
            public DateTime When;
        }
    }
}
