// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    /// <summary>Sealed, get-only: provable on its own, so the SHARE is what the attribute forces, not the element.</summary>
    public sealed class FsBadge
    {
        public FsBadge(int id)
        {
            Id = id;
        }

        public int Id { get; }
    }

    public sealed class FsSource
    {
        public int Id { get; set; }

        /// <summary>An interface the immutability proof deliberately refuses — the shape `[MapShare]` exists for.</summary>
        public IReadOnlyList<FsBadge> Badges { get; set; } = [];
    }

    public sealed class FsMiddle
    {
        public int Id { get; set; }

        public IReadOnlyList<FsBadge> Badges { get; set; } = [];
    }

    public sealed class FsTarget
    {
        public int Id { get; set; }

        public IReadOnlyList<FsBadge> Badges { get; set; } = [];
    }

    [DwarfMapper]
    public partial class FsMappers
    {
        [MapShare(nameof(FsMiddle.Badges))]
        public partial FsMiddle SourceToMiddle(FsSource s);

        [MapShare(nameof(FsTarget.Badges))]
        public partial FsTarget MiddleToTarget(FsMiddle m);

        /// <summary>What a fused emission would produce.</summary>
        [MapShare(nameof(FsTarget.Badges))]
        public partial FsTarget SourceToTarget(FsSource s);
    }

    /// <summary>
    ///     <b>PROVE row: <c>[MapShare]</c> / <c>[Reinterpret]</c> on a member of the intermediate.</b>
    ///     <para>
    ///         <c>Issues/round30/SPEC-fusion-refusal-list.md</c> parked this as PROVE with the worry: "Sharing
    ///         means the destination aliases the source's storage. If <c>C</c> shares from <c>B</c> and <c>B</c>
    ///         is elided, <c>C</c> would have to share from <c>A</c> directly — which may or may not be the same
    ///         object."
    ///     </para>
    ///     <para>
    ///         It is the same object exactly when sharing is TRANSITIVE, and that is a property of the emitted
    ///         code rather than of the argument, so it is measured here by reference identity. If
    ///         <c>C.Badges</c> is reference-equal to <c>A.Badges</c> in BOTH the chained and the direct form,
    ///         eliding <c>B</c> cannot change what <c>C</c> points at, and the row is SAFE. If the two forms
    ///         disagree, it is a REFUSE.
    ///     </para>
    ///     <para>
    ///         Reference identity is the right instrument and structural comparison is not: two lists with equal
    ///         contents would satisfy a structural check while being different objects, which is precisely the
    ///         distinction the whole row is about.
    ///     </para>
    /// </summary>
    public class FusionShareTransitivityTests
    {
        private static FsSource NewSource()
        {
            return new FsSource
            {
                Id = 7,
                Badges = new List<FsBadge> { new(1), new(2), new(3) }
            };
        }

        [Fact]
        public void Sharing_is_transitive_across_the_chain()
        {
            var m = new FsMappers();
            var src = NewSource();

            var mid = m.SourceToMiddle(src);
            var chained = m.MiddleToTarget(mid);

            // Each hop shares, so the reference must survive both.
            Assert.Same(src.Badges, mid.Badges);
            Assert.Same(src.Badges, chained.Badges);
        }

        [Fact]
        public void The_direct_map_shares_the_same_reference_the_chain_arrives_at()
        {
            var m = new FsMappers();
            var src = NewSource();

            var chained = m.MiddleToTarget(m.SourceToMiddle(src));
            var direct = m.SourceToTarget(src);

            // THE ROW'S ANSWER. Both forms land on the SAME object the source held, so removing the
            // intermediate cannot change what the target points at.
            Assert.Same(src.Badges, direct.Badges);
            Assert.Same(chained.Badges, direct.Badges);
        }

        [Fact]
        public void The_identity_check_is_not_vacuous_because_a_copy_would_fail_it()
        {
            // The control. Assert.Same passes trivially if the mapper handed back the source object itself, or
            // if both sides were the same empty singleton. This shows the instrument can tell a SHARE from a
            // COPY: a list with identical CONTENTS is structurally equal and reference-different, so a
            // copying implementation would fail the two tests above rather than sneaking past them.
            var src = NewSource();
            var copy = new List<FsBadge>(src.Badges);

            Assert.Equal(src.Badges.Count, copy.Count);
            Assert.Equal(src.Badges.Select(b => b.Id), copy.Select(b => b.Id));
            Assert.NotSame(src.Badges, copy);
        }
    }
}
