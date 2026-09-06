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
    ///         is refused outright, since its width belongs to the machine rather than to the type. For the seven
    ///         types below it relies, because their size and alignment follow from a field set that is part of
    ///         each type's public contract: <c>Guid</c>'s documented 16 bytes, <c>DateTime</c>'s and
    ///         <c>DateTimeOffset</c>'s ticks (plus a minutes offset), <c>TimeSpan</c>'s and <c>TimeOnly</c>'s
    ///         <c>Ticks</c>, <c>DateOnly</c>'s <c>DayNumber</c>, <c>decimal</c>'s 128 bits.
    ///     </para>
    ///     <para>
    ///         "Documented" is not "verified", so this file verifies it. These tests execute on the real runtime
    ///         and fail the build the day any of those layouts moves — which is the only way a hard-coded table
    ///         inside a source generator can be allowed to exist. Deliberately in the INTEGRATION project: the
    ///         generator's own test project asserts what the generator BELIEVES; only a running test can assert
    ///         what the runtime DOES.
    ///     </para>
    ///     <para>
    ///         Measured on x64. The numbers are asserted, not predicted — nothing here restates the generator's
    ///         model, and every figure was read off a probe before it was written down (round 29, <c>T0.3b</c>
    ///         for the first four, <c>T0.3c</c> for <c>DateTimeOffset</c>, <c>DateOnly</c> and <c>TimeOnly</c>).
    ///     </para>
    /// </summary>
    public class BclLayoutFactsTests
    {
        /// <summary>
        ///     Sizes must match the table in <c>LayoutHygiene.FixedLayoutBclSize</c> exactly. A mismatch here
        ///     means the generator is printing byte counts that are wrong on this machine.
        /// </summary>
        [Fact]
        public void The_fixed_layout_BCL_types_have_the_sizes_the_generator_assumes()
        {
            Assert.Equal(16, Unsafe.SizeOf<Guid>());
            Assert.Equal(8, Unsafe.SizeOf<DateTime>());
            Assert.Equal(16, Unsafe.SizeOf<DateTimeOffset>());
            Assert.Equal(8, Unsafe.SizeOf<TimeSpan>());
            Assert.Equal(8, Unsafe.SizeOf<TimeOnly>());
            Assert.Equal(4, Unsafe.SizeOf<DateOnly>());
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
        [InlineData(typeof(AlignProbe<TimeOnly>), 8)]
        [InlineData(typeof(AlignProbe<DateOnly>), 4)]
        [InlineData(typeof(AlignProbe<decimal>), 8)]
        public void The_fixed_layout_BCL_types_have_the_alignments_the_generator_assumes(Type probe, int expected)
        {
            // The probe is {byte Pad; T Value}: the runtime rounds Value's offset up to T's alignment, so the
            // offset IS the alignment for any type aligned to more than one byte.
            var offset = Marshal.OffsetOf(probe, "Value").ToInt32();

            Assert.Equal(expected, offset);
        }

        /// <summary>
        ///     <c>DateTimeOffset</c>'s alignment, on the SAME <see cref="AlignProbe{T}" /> struct as the six
        ///     above but read from its size rather than through the marshaller, because the marshaller cannot
        ///     reach it.
        ///     <para>
        ///         <c>DateTimeOffset</c> is declared <c>[StructLayout(LayoutKind.Auto)]</c>, and
        ///         <see cref="Marshal.OffsetOf(Type, string)" /> refuses an auto-layout composite outright —
        ///         pinned by <see cref="Marshal_cannot_probe_an_auto_layout_composite" /> below so this fork is
        ///         self-explaining rather than folklore. That is not a reason to distrust the entry: the
        ///         generator models the MANAGED layout, which is what the runtime uses for a consumer's struct,
        ///         and the marshalled layout was only ever a convenient stand-in for it.
        ///     </para>
        ///     <para>
        ///         The reading: for <c>{byte Pad; T Value}</c> the struct's size is <c>alignof(T) + sizeof(T)</c>
        ///         under EITHER layout the CLR may choose — sequentially the byte is padded up to T's alignment,
        ///         and under a repack T sits at 0 with the byte after it, rounded to the same alignment — as long
        ///         as <c>sizeof(T)</c> is a multiple of <c>alignof(T)</c>, which holds for every entry in the
        ///         table. So <c>sizeof(probe) − sizeof(T)</c> is the alignment. This reading was checked against
        ///         <see cref="Marshal.OffsetOf(Type, string)" /> on all six marshalable entries and agrees with
        ///         every one of them (round 29, <c>T0.3c</c>).
        ///     </para>
        /// </summary>
        [Fact]
        public void DateTimeOffset_has_the_alignment_the_generator_assumes()
        {
            var alignment = Unsafe.SizeOf<AlignProbe<DateTimeOffset>>() - Unsafe.SizeOf<DateTimeOffset>();

            Assert.Equal(8, alignment);
        }

        /// <summary>
        ///     Why the test above exists: the marshaller cannot describe a composite holding an auto-layout
        ///     struct, so <c>Marshal.OffsetOf</c> is not available as the probe for <c>DateTimeOffset</c>. The
        ///     day that changes, this test fails and the fork can be collapsed back into the theory.
        /// </summary>
        [Fact]
        public void Marshal_cannot_probe_an_auto_layout_composite()
        {
            Assert.Throws<ArgumentException>(
                () => Marshal.OffsetOf<AlignProbe<DateTimeOffset>>(nameof(AlignProbe<DateTimeOffset>.Value)));
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

        /// <summary>
        ///     The same composite check for the shape this round's corpus found silenced: an audit row with two
        ///     <c>DateTimeOffset</c> timestamps interleaved with flags. It is the fixture
        ///     <c>LayoutHygieneTests.A_padded_DateTimeOffset_bearing_struct_is_now_reportable</c> pins at 64 with
        ///     21 wasted bytes, and it doubles as proof that a <c>Sequential</c> struct holding an AUTO-layout
        ///     member is still laid out in declaration order — a repack would have produced 48, not 64.
        /// </summary>
        [Fact]
        public void A_DateTimeOffset_bearing_DTO_is_the_size_the_generator_computes()
        {
            Assert.Equal(64, Unsafe.SizeOf<AuditRowProbe>());
        }

        /// <summary>
        ///     And the optional forms, which are a distinct path through <c>LayoutHygiene.AsOptional</c>:
        ///     <c>Nullable&lt;T&gt;</c> is <c>{bool hasValue; T value}</c>, so the flag is padded up to T's
        ///     alignment and the whole rounds up to it again. A <c>DateTimeOffset?</c> timestamp is at least as
        ///     common in a DTO as the bare form, and it would have been an easy half-fix to leave unmeasured.
        /// </summary>
        [Fact]
        public void The_optional_forms_are_the_sizes_the_generator_computes()
        {
            Assert.Equal(24, Unsafe.SizeOf<DateTimeOffset?>());
            Assert.Equal(8, Unsafe.SizeOf<DateOnly?>());
            Assert.Equal(16, Unsafe.SizeOf<TimeOnly?>());
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

        [StructLayout(LayoutKind.Sequential)]
        private struct AuditRowProbe
        {
            public byte Flag;
            public DateTimeOffset CreatedAt;
            public byte Kind;
            public DateTimeOffset UpdatedAt;
            public byte Extra;
            public double Rate;
        }
    }
}
