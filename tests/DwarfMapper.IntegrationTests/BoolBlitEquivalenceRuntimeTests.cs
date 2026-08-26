// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DwarfMapper.IntegrationTests
{
    public struct BoolFlagSrc
    {
        public bool F;

        public byte Pad;
    }

    public struct BoolFlagDst
    {
        public bool F;

        public byte Pad;
    }

    // Same members, same types — but Auto layout, so the blit is refused and the ELEMENT LOOP is emitted.
    // The scalar twin, exactly as the T4 benchmark uses one.
    [StructLayout(LayoutKind.Auto)]
    public struct BoolFlagScalarDst
    {
        public bool F;

        public byte Pad;
    }

    public class BoolBlitSrc
    {
        public BoolFlagSrc[] Items { get; set; } = [];
    }

    public class BoolBlitDst
    {
        public BoolFlagDst[] Items { get; set; } = [];
    }

    public class BoolScalarDst
    {
        public BoolFlagScalarDst[] Items { get; set; } = [];
    }

    [DwarfMapper]
    public partial class BoolBlitMapper
    {
        public partial BoolBlitDst MapBlit(BoolBlitSrc s);

        public partial BoolScalarDst MapScalar(BoolBlitSrc s);
    }

    /// <summary>
    ///     The <c>bool</c> non-normalization question, settled as an EQUIVALENCE rather than as a value.
    ///     <para>
    ///         A <c>bool</c> occupies one byte and C# only ever produces 0 or 1, but the CLR does not enforce
    ///         that: a byte of 2 reaches a <c>bool</c> through interop, a reinterpreted buffer, or
    ///         <c>Unsafe.As</c>. Such a value is truthy yet is not <c>true</c> in the canonical sense. The
    ///         question this round raised was whether the block copy and the element loop treat it differently
    ///         — which would be a silent divergence, the one failure class this project refuses.
    ///     </para>
    ///     <para>
    ///         <b>What is asserted, and what deliberately is not.</b> These tests pin that the two paths AGREE.
    ///         They do NOT pin a particular byte value, because that would promise something the runtime does
    ///         not: the C# specification says nothing about non-canonical bools, so preserving byte 2 is
    ///         current JIT behaviour rather than a contract DwarfMapper is in a position to guarantee. Pinning
    ///         the value would turn an implementation detail of .NET into a promise DwarfMapper owes its
    ///         consumers forever; pinning the agreement catches the thing that would actually be a defect — the
    ///         two paths drifting apart, which is exactly how a future JIT change would surface.
    ///     </para>
    /// </summary>
    public class BoolBlitEquivalenceRuntimeTests
    {
        private static bool NonCanonical(byte raw)
        {
            return Unsafe.As<byte, bool>(ref raw);
        }

        private static byte RawOf(bool b)
        {
            return Unsafe.As<bool, byte>(ref b);
        }

        [Theory]
        [InlineData((byte)0)]
        [InlineData((byte)1)]
        [InlineData((byte)2)]
        [InlineData((byte)0xFF)]
        public void The_blit_and_the_element_loop_agree_on_every_byte_a_bool_can_hold(byte raw)
        {
            var src = new BoolBlitSrc { Items = [new BoolFlagSrc { F = NonCanonical(raw), Pad = 9 }] };
            var mapper = new BoolBlitMapper();

            var blitted = mapper.MapBlit(src);
            var looped = mapper.MapScalar(src);

            // The property that matters: whatever .NET does with a non-canonical bool, BOTH emitted paths must
            // do the same thing. A divergence here is a silent correctness bug, not a performance question.
            Assert.Equal(RawOf(looped.Items[0].F), RawOf(blitted.Items[0].F));
            Assert.Equal(looped.Items[0].F, blitted.Items[0].F);
            Assert.Equal(looped.Items[0].Pad, blitted.Items[0].Pad);
        }

        [Fact]
        public void The_two_fixtures_really_can_take_different_paths()
        {
            // Anti-vacuity. If both destinations were blit-eligible, the theory above would compare one path
            // with itself and pass while proving nothing — the failure mode this repository keeps finding in
            // its own tests.
            //
            // Asserted on the FIXTURES rather than on emitted source, deliberately: the emitted files are
            // written only in Debug (round 24's EmitCompilerGeneratedFiles), so a source-reading check would
            // pass vacuously in Release, which is where this suite runs. Layout kind is the exact property the
            // blit proof turns on, and the generator-side tests already pin that Auto layout is refused.
            Assert.Equal(LayoutKind.Sequential, typeof(BoolFlagDst).StructLayoutAttribute!.Value);
            Assert.Equal(LayoutKind.Auto, typeof(BoolFlagScalarDst).StructLayoutAttribute!.Value);

            // …and the pair really is otherwise identical, or the layout difference would not be the ONLY
            // thing separating the two paths.
            Assert.Equal(
                typeof(BoolFlagDst).GetFields().Select(f => f.Name + ":" + f.FieldType.Name),
                typeof(BoolFlagScalarDst).GetFields().Select(f => f.Name + ":" + f.FieldType.Name));
        }
    }
}
