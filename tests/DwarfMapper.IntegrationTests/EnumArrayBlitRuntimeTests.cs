// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    public enum BlitStatus
    {
        A = 1,

        B = 2,

        C = 3,
    }

    public enum BlitStatusDto
    {
        A = 1,

        B = 2,

        C = 3,
    }

    public class EnumBlitSrc
    {
        public BlitStatus[] Values { get; set; } = [];

        public BlitStatus[] Underlying { get; set; } = [];
    }

    public class EnumBlitDst
    {
        public BlitStatusDto[] Values { get; set; } = [];

        public int[] Underlying { get; set; } = [];
    }

    [DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]
    public partial class EnumBlitMapper
    {
        public partial EnumBlitDst Map(EnumBlitSrc s);
    }

    /// <summary>
    ///     <c>R25-03</c> runtime oracle. The generator-side tests prove which pairs take the block copy; these
    ///     prove the block copy produces what the element loop would have.
    ///     <para>
    ///         The undefined-value case is the one that matters. It is the reason <c>ByName</c> is excluded, so
    ///         it is also the thing that must be shown to behave under <c>ByValue</c>: an enum variable may
    ///         legally hold any value of its underlying type, and both the identity <c>CreateChecked</c> and the
    ///         reinterpret carry such a value through untouched.
    ///     </para>
    /// </summary>
    public class EnumArrayBlitRuntimeTests
    {
        [Fact]
        public void The_blit_reproduces_every_declared_value()
        {
            var src = new EnumBlitSrc { Values = [BlitStatus.A, BlitStatus.B, BlitStatus.C, BlitStatus.A] };

            var dst = new EnumBlitMapper().Map(src);

            Assert.Equal([BlitStatusDto.A, BlitStatusDto.B, BlitStatusDto.C, BlitStatusDto.A], dst.Values);
        }

        [Fact]
        public void An_UNDEFINED_enum_value_survives_the_blit_unchanged()
        {
            // 99 names no member of either enum. Casting an arbitrary integer to an enum is legal C# and
            // legal IL, so this is a value real data can carry — from a database column, a wire format, or a
            // cast. Under ByValue the scalar path is `(Dto)int.CreateChecked((int)v)`, which passes it
            // through; the blit must agree, and does.
            var src = new EnumBlitSrc { Values = [(BlitStatus)99, BlitStatus.B] };

            var dst = new EnumBlitMapper().Map(src);

            Assert.Equal(99, (int)dst.Values[0]);
            Assert.Equal(BlitStatusDto.B, dst.Values[1]);
        }

        [Fact]
        public void An_enum_maps_onto_its_underlying_primitive_value_for_value()
        {
            var src = new EnumBlitSrc { Underlying = [BlitStatus.A, BlitStatus.C, (BlitStatus)77] };

            var dst = new EnumBlitMapper().Map(src);

            Assert.Equal([1, 3, 77], dst.Underlying);
        }

        [Fact]
        public void The_destination_is_an_independent_buffer()
        {
            // A reinterpret must COPY. If the destination aliased the source's storage, mutating one would
            // corrupt the other — the AutoMapper "assignable collection" bug this project already refuses
            // elsewhere. The element types differ here, so this pins the blit's own allocation.
            var src = new EnumBlitSrc { Values = [BlitStatus.A, BlitStatus.B] };

            var dst = new EnumBlitMapper().Map(src);
            src.Values[0] = BlitStatus.C;

            Assert.Equal(BlitStatusDto.A, dst.Values[0]);
        }

        [Fact]
        public void An_empty_array_maps_to_an_empty_array()
        {
            var dst = new EnumBlitMapper().Map(new EnumBlitSrc());

            Assert.Empty(dst.Values);
            Assert.Empty(dst.Underlying);
        }
    }
}
