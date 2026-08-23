// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;

namespace DwarfMapper.IntegrationTests
{
    public struct IaSrc
    {
        public long A;

        public int B;

        public int C;
    }

    public struct IaDst
    {
        public long A;

        public int B;

        public int C;
    }

    public class ImmutableBlitSrc
    {
        public IaSrc[] FromArray { get; set; } = [];

        public ImmutableArray<IaSrc> FromImmutable { get; set; } = [];

        public ImmutableArray<IaSrc> ImmutableToArray { get; set; } = [];
    }

    public class ImmutableBlitDst
    {
        public ImmutableArray<IaDst> FromArray { get; set; }

        public ImmutableArray<IaDst> FromImmutable { get; set; }

        public IaDst[] ImmutableToArray { get; set; } = [];
    }

    [DwarfMapper]
    public partial class ImmutableBlitMapper
    {
        public partial ImmutableBlitDst Map(ImmutableBlitSrc s);
    }

    /// <summary>
    ///     <c>T7</c> runtime oracle for the <c>ImmutableArray&lt;T&gt;</c> blit.
    ///     <para>
    ///         The hazard unique to this shape is aliasing.
    ///         <c>ImmutableCollectionsMarshal.AsImmutableArray</c> WRAPS the array it is given rather than
    ///         copying it, so wrapping the source's own storage would hand two immutable values one buffer —
    ///         and "immutable" would then be a lie that a later write to the source proves. The generator-side
    ///         test pins the emitted shape; these pin the behaviour.
    ///     </para>
    /// </summary>
    public class ImmutableArrayBlitRuntimeTests
    {
        private static ImmutableBlitSrc Sample()
        {
            var items = new[]
            {
                new IaSrc { A = 1, B = 2, C = 3 },
                new IaSrc { A = long.MinValue, B = int.MaxValue, C = -1 },
            };

            return new ImmutableBlitSrc
            {
                FromArray = items,
                FromImmutable = [.. items],
                ImmutableToArray = [.. items],
            };
        }

        [Fact]
        public void Every_element_survives_in_each_direction()
        {
            var dst = new ImmutableBlitMapper().Map(Sample());

            Assert.Equal(2, dst.FromArray.Length);
            Assert.Equal(2, dst.FromImmutable.Length);
            Assert.Equal(2, dst.ImmutableToArray.Length);

            Assert.Equal(long.MinValue, dst.FromArray[1].A);
            Assert.Equal(int.MaxValue, dst.FromImmutable[1].B);
            Assert.Equal(-1, dst.ImmutableToArray[1].C);
        }

        [Fact]
        public void The_immutable_result_does_not_alias_a_mutable_source()
        {
            // If the blit had wrapped the source array instead of a fresh copy, this write would be visible
            // through an ImmutableArray — the exact failure the emitted-shape pin exists to prevent.
            var src = Sample();
            var dst = new ImmutableBlitMapper().Map(src);

            src.FromArray[0].A = 999;

            Assert.Equal(1, dst.FromArray[0].A);
        }

        [Fact]
        public void A_default_ImmutableArray_source_maps_to_empty_rather_than_faulting()
        {
            // default(ImmutableArray<T>) wraps a NULL array. It is not null itself, so a null check would
            // miss it and AsSpan() would fault. IsDefaultOrEmpty is the correct guard.
            var src = new ImmutableBlitSrc
            {
                FromArray = [],
                FromImmutable = default,
                ImmutableToArray = default,
            };

            var dst = new ImmutableBlitMapper().Map(src);

            Assert.True(dst.FromImmutable.IsEmpty);
            Assert.Empty(dst.ImmutableToArray);
        }

        [Fact]
        public void An_empty_ImmutableArray_maps_to_empty()
        {
            var dst = new ImmutableBlitMapper().Map(new ImmutableBlitSrc());

            Assert.True(dst.FromArray.IsEmpty);
            Assert.True(dst.FromImmutable.IsEmpty);
            Assert.Empty(dst.ImmutableToArray);
        }
    }
}
