// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests
{
    public sealed class ShareBadge
    {
        public ShareBadge(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    public sealed class ShareSource
    {
        public ImmutableList<ShareBadge> Proven { get; init; } = ImmutableList<ShareBadge>.Empty;

        public IReadOnlyList<ShareBadge> Asserted { get; init; } = Array.Empty<ShareBadge>();

        public IReadOnlyList<ShareBadge> Copied { get; init; } = Array.Empty<ShareBadge>();

        public ImmutableArray<ShareBadge> Sequence { get; init; }
    }

    public sealed class ShareTarget
    {
        public ImmutableList<ShareBadge> Proven { get; init; } = ImmutableList<ShareBadge>.Empty;

        public IReadOnlyList<ShareBadge> Asserted { get; init; } = Array.Empty<ShareBadge>();

        public IReadOnlyList<ShareBadge> Copied { get; init; } = Array.Empty<ShareBadge>();

        public ImmutableArray<ShareBadge> Sequence { get; init; }
    }

    [DwarfMapper]
    public partial class ShareMapper
    {
        [MapShare("Asserted")]
        public partial ShareTarget Map(ShareSource s);
    }

    /// <summary>
    ///     What the share actually does at run time. The emission tests pin the TEXT; these pin the only thing
    ///     a consumer can observe — that the destination holds the very object the source held, and that the
    ///     shapes the proof refused still hold a different one.
    /// </summary>
    public class MapShareRuntimeTests
    {
        [Fact]
        public void A_proven_member_holds_the_source_instance_itself()
        {
            var src = new ShareSource
            {
                Proven = ImmutableList.Create(new ShareBadge("a"))
            };

            var dst = new ShareMapper().Map(src);

            Assert.True(ReferenceEquals(src.Proven, dst.Proven),
                "an ImmutableList of a proven element is shared, so the destination holds the source's own instance");
        }

        [Fact]
        public void An_asserted_member_holds_the_source_instance_itself()
        {
            // The [MapShare] tier. A List<T> behind IReadOnlyList<T> is exactly the instance the ruling says
            // the AUTOMATIC path must never take on trust — and exactly what the attribute exists to let a
            // caller take on their own.
            var badges = new List<ShareBadge> { new("a") };
            var src = new ShareSource { Asserted = badges };

            var dst = new ShareMapper().Map(src);

            Assert.True(ReferenceEquals(badges, dst.Asserted),
                "[MapShare] assigns the source reference, so no new collection is built");
        }

        [Fact]
        public void An_interface_without_the_attribute_is_still_copied()
        {
            // The control, and the more important half: without this the two assertions above would pass just
            // as happily if the generator had started sharing everything.
            var badges = new List<ShareBadge> { new("a") };
            var src = new ShareSource { Copied = badges };

            var dst = new ShareMapper().Map(src);

            Assert.False(ReferenceEquals(badges, dst.Copied),
                "an interface is not a guarantee, so the automatic path copies it");
            Assert.Equal(badges, dst.Copied);
        }

        [Fact]
        public void A_default_ImmutableArray_source_still_produces_an_empty_destination()
        {
            // The guard the share carries instead of the helper it replaced. `default(ImmutableArray<T>)` is a
            // struct that is never null and wraps a null array, so a bare assignment would hand the consumer a
            // collection that throws on enumeration. NullCollectionStrategy.AsEmpty says empty, and empty is
            // what comes out — from a cached singleton, so the guard allocates nothing.
            var dst = new ShareMapper().Map(new ShareSource());

            Assert.False(dst.Sequence.IsDefault);
            Assert.Empty(dst.Sequence);
        }

        [Fact]
        public void A_null_source_collection_still_produces_an_empty_destination()
        {
            var src = new ShareSource
            {
                Proven = null!,
                Asserted = null!
            };

            var dst = new ShareMapper().Map(src);

            Assert.Empty(dst.Proven);
            Assert.Empty(dst.Asserted);
        }

        [Fact]
        public void A_shared_ImmutableArray_holds_the_source_instance_itself()
        {
            var sequence = ImmutableArray.Create(new ShareBadge("a"));
            var src = new ShareSource { Sequence = sequence };

            var dst = new ShareMapper().Map(src);

            // ImmutableArray<T> is a struct, so reference identity is asked of the BACKING array — which is
            // the storage the share exists to stop duplicating.
            Assert.True(ReferenceEquals(
                System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(sequence),
                System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(dst.Sequence)));
        }
    }
}
