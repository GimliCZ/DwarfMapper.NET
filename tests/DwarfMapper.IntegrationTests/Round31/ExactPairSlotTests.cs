// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests.Round31
{
    /// <summary>
    ///     <c>Map&lt;TSource, TDestination&gt;</c> resolves a pair whose BOTH types are static at the call site, so the
    ///     dictionary lookup it did on every call was avoidable. T12 caches that one answer per closed generic pair.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The correctness question is what may be cached. ONLY the exact registered pair: the fallback resolves
    ///         on the source INSTANCE's runtime type, so caching that per <c>TSource</c> would hand a later call with
    ///         a different derived instance the wrong mapper. The slot therefore caches "exact delegate, or known
    ///         absent" and nothing else.
    ///     </para>
    ///     <para>
    ///         The staleness question is ordering. A reader takes the registry version BEFORE its lookup and stores
    ///         it with the answer; a writer bumps the version AFTER its table write. Reverse either and a slot can
    ///         cache an answer taken before a registration and stamp it with the version from after it — wrong
    ///         forever, silently.
    ///     </para>
    ///     <para>
    ///         Private marker types per test: the registry is static and add-only for the process, so shared shapes
    ///         would make these tests order-dependent on each other.
    ///     </para>
    /// </remarks>
    public sealed class ExactPairSlotTests
    {
        /// <summary>GUARD: the documented semantics of the two-type overload, which the slot must not change.</summary>
        [Fact]
        public void Static_pair_wins_over_runtime_type()
        {
            DwarfMapperRegistry.Register(typeof(SlotBase), typeof(SlotDst), _ => new SlotDst("base"));
            DwarfMapperRegistry.Register(typeof(SlotDerived), typeof(SlotDst), _ => new SlotDst("derived"));

            Assert.Equal("base", DwarfMapperFacade.Instance.Map<SlotBase, SlotDst>(new SlotDerived()).Tag);
            Assert.Equal("base", DwarfMapperFacade.Instance.Map<SlotBase, SlotDst>(new SlotDerived()).Tag);
        }

        /// <summary>
        ///     A pair registered AFTER the slot has already answered "absent" must be picked up.
        /// </summary>
        /// <remarks>
        ///     The source's runtime type is deliberately NOT <c>TSource</c>. A first draft of this test passed a
        ///     <c>TSource</c> instance, and then passed against a slot with no invalidation at all: the stale "absent"
        ///     answer sent the call into the runtime-type fallback, which resolved the very registration the slot had
        ///     missed, and the two paths agreed by accident. Only a source whose runtime type the fallback resolves
        ///     ELSEWHERE can tell the two apart — proven by planting the broken slot and watching this fail.
        /// </remarks>
        [Fact]
        public void An_exact_pair_registered_after_first_use_is_picked_up()
        {
            DwarfMapperRegistry.Register(typeof(LateDerived), typeof(LateDst), _ => new LateDst("derived"));

            // The exact (LateBase, LateDst) pair does not exist yet, so this resolves through the runtime-type
            // fallback and lands on the derived map.
            Assert.Equal("derived", DwarfMapperFacade.Instance.Map<LateBase, LateDst>(new LateDerived()).Tag);

            DwarfMapperRegistry.Register(typeof(LateBase), typeof(LateDst), _ => new LateDst("base"));

            // Now the exact pair exists and must win over the runtime type. A slot that cached the miss forever
            // answers "derived" here.
            Assert.Equal("base", DwarfMapperFacade.Instance.Map<LateBase, LateDst>(new LateDerived()).Tag);

            // Again, to exercise the cached-hit branch rather than only the resolve-and-store one.
            Assert.Equal("base", DwarfMapperFacade.Instance.Map<LateBase, LateDst>(new LateDerived()).Tag);
        }

        /// <summary>
        ///     Readers racing a writer never get a WRONG answer: every reader sees one of the two legitimately
        ///     registered maps, and once the writer has joined, the exact pair wins for good.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         It deliberately does NOT assert per-reader monotonicity — that once a reader has seen the exact
        ///         pair it never sees the fallback again. That property is stronger than the design provides, and
        ///         asserting it would make this test flaky. The registration is not published until the version is
        ///         bumped, and the bump happens AFTER the table write, so there is a window where the table already
        ///         holds the pair and the version does not say so: a reader that looked before the table write can
        ///         store its (older-but-still-current-version) "absent" answer over a reader that already found the
        ///         map, and the second reader then legitimately falls back again.
        ///     </para>
        ///     <para>
        ///         Both answers are correct during that window, so the fix is to assert the contract rather than to
        ///         make the slot monotonic — which would cost a CAS loop to buy a property nothing needs. Writing it
        ///         down matters for a second reason: a flaky test is how a mutation run manufactures phantom kills,
        ///         which is the exact contamination that cost round 30 a re-pin.
        ///     </para>
        /// </remarks>
        [Fact]
        public async Task Concurrent_register_and_map_never_returns_a_wrong_delegate()
        {
            DwarfMapperRegistry.Register(typeof(RaceDerived), typeof(RaceDst), _ => new RaceDst("derived"));

            using var start = new ManualResetEventSlim(false);
            var failures = new List<string>();
            var readers = new Task[8];
            for (var r = 0; r < readers.Length; r++)
            {
                readers[r] = Task.Run(() =>
                {
                    start.Wait();
                    for (var i = 0; i < 2000; i++)
                    {
                        var tag = DwarfMapperFacade.Instance.Map<RaceBase, RaceDst>(new RaceDerived()).Tag;
                        if (!string.Equals(tag, "derived", StringComparison.Ordinal) &&
                            !string.Equals(tag, "base", StringComparison.Ordinal))
                        {
                            lock (failures) { failures.Add("unknown tag: " + tag); }
                            return;
                        }
                    }
                });
            }

            var writer = Task.Run(() =>
            {
                start.Wait();
                Thread.Sleep(1);
                DwarfMapperRegistry.Register(typeof(RaceBase), typeof(RaceDst), _ => new RaceDst("base"));
            });

            start.Set();
            await Task.WhenAll([.. readers, writer]);

            Assert.True(failures.Count == 0, string.Join("; ", failures));

            // Once the writer has joined, the registration IS published, so the exact pair must win from here on.
            Assert.Equal("base", DwarfMapperFacade.Instance.Map<RaceBase, RaceDst>(new RaceDerived()).Tag);
        }

        /// <summary>
        ///     The update slot, both arms. Its miss is harmless by construction — the fallback is
        ///     <c>Update</c>, which repeats the identical exact-pair lookup — so this is a coverage-and-contract
        ///     guard rather than a race: the absent-map throw must still come from <c>Update</c>, and the found
        ///     delegate must still be applied to the caller's own instance.
        /// </summary>
        [Fact]
        public void The_update_slot_throws_before_registration_and_applies_after()
        {
            var destination = new UpDst();
            Assert.Throws<DwarfMapMissingException>(
                () => DwarfMapperFacade.Instance.Map(new UpSrc { V = 7 }, destination));

            DwarfMapperRegistry.RegisterUpdate(
                typeof(UpSrc),
                typeof(UpDst),
                (s, d) => ((UpDst)d).V = ((UpSrc)s).V);

            DwarfMapperFacade.Instance.Map(new UpSrc { V = 7 }, destination);
            Assert.Equal(7, destination.V);

            // Twice, for the cached-hit branch.
            DwarfMapperFacade.Instance.Map(new UpSrc { V = 9 }, destination);
            Assert.Equal(9, destination.V);

            // The null guards must stay ahead of the delegate, or a hit turns an ArgumentNullException into a
            // NullReferenceException raised from inside generated code.
            Assert.Throws<ArgumentNullException>(() => DwarfMapperFacade.Instance.Map((UpSrc)null!, destination));
            Assert.Throws<ArgumentNullException>(() => DwarfMapperFacade.Instance.Map(new UpSrc(), (UpDst)null!));
        }

        private sealed class UpSrc
        {
            public int V { get; set; }
        }

        private sealed class UpDst
        {
            public int V { get; set; }
        }

        private class SlotBase;

        private sealed class SlotDerived : SlotBase;

        private sealed class SlotDst(string tag)
        {
            public string Tag { get; } = tag;
        }

        private class LateBase;

        private sealed class LateDerived : LateBase;

        private sealed class LateDst(string tag)
        {
            public string Tag { get; } = tag;
        }

        private class RaceBase;

        private sealed class RaceDerived : RaceBase;

        private sealed class RaceDst(string tag)
        {
            public string Tag { get; } = tag;
        }
    }
}
