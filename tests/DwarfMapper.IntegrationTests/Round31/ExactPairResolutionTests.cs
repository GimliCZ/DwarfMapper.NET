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
    ///     The resolution rules of a two-type <c>Map&lt;TSource, TDestination&gt;</c> call that is NOT bound at compile
    ///     time - <c>Dwarf.Map</c>'s run-time body, which the facade's two-type overloads forward to. The exact
    ///     registered pair wins over the source instance's runtime type; a pair registered later is picked up; a
    ///     reader racing a writer never gets a wrong answer and never goes backwards.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         These were written in round 31 T12 against a per-pair cache, and they outlived it: T26 replaced the cache
    ///         with compile-time binding, and every property below is a property of the resolution itself. The
    ///         divergent registrations ("base" for the exact pair, "derived" for the runtime type) are what make the
    ///         exact-pair branch observable: without it the runtime-type walk would answer, and answer differently.
    ///     </para>
    ///     <para>
    ///         Private marker types per test: the registry is static and add-only for the process, so shared shapes
    ///         would make these tests order-dependent on each other. Private types are also never registered by the
    ///         generator, so no call here is bound - each one exercises the run-time path.
    ///     </para>
    /// </remarks>
    public sealed class ExactPairResolutionTests
    {
        [Fact]
        public void Static_pair_wins_over_runtime_type()
        {
            DwarfMapperRegistry.Register(typeof(PairBase), typeof(PairDst), _ => new PairDst("base"));
            DwarfMapperRegistry.Register(typeof(PairDerived), typeof(PairDst), _ => new PairDst("derived"));

            Assert.Equal("base", Dwarf.Map<PairBase, PairDst>(new PairDerived()).Tag);
            Assert.Equal("base", DwarfMapperFacade.Instance.Map<PairBase, PairDst>(new PairDerived()).Tag);
        }

        /// <remarks>
        ///     The source's runtime type is deliberately NOT <c>TSource</c>: only a source whose runtime type the
        ///     fallback resolves ELSEWHERE can tell "the exact pair was found" from "the fallback found something".
        /// </remarks>
        [Fact]
        public void An_exact_pair_registered_after_first_use_is_picked_up()
        {
            DwarfMapperRegistry.Register(typeof(LateDerived), typeof(LateDst), _ => new LateDst("derived"));

            // The exact (LateBase, LateDst) pair does not exist yet, so this resolves through the runtime-type
            // fallback and lands on the derived map.
            Assert.Equal("derived", Dwarf.Map<LateBase, LateDst>(new LateDerived()).Tag);

            DwarfMapperRegistry.Register(typeof(LateBase), typeof(LateDst), _ => new LateDst("base"));

            // Now the exact pair exists and must win over the runtime type.
            Assert.Equal("base", Dwarf.Map<LateBase, LateDst>(new LateDerived()).Tag);
            Assert.Equal("base", DwarfMapperFacade.Instance.Map<LateBase, LateDst>(new LateDerived()).Tag);
        }

        /// <summary>
        ///     Readers racing a writer never get a WRONG answer, and never go backwards: once a reader has seen the
        ///     exact pair it cannot see the fallback again (registration is add-only and first-wins).
        /// </summary>
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
                    var sawExact = false;
                    for (var i = 0; i < 2000; i++)
                    {
                        var tag = Dwarf.Map<RaceBase, RaceDst>(new RaceDerived()).Tag;
                        if (!string.Equals(tag, "derived", StringComparison.Ordinal) &&
                            !string.Equals(tag, "base", StringComparison.Ordinal))
                        {
                            lock (failures) { failures.Add("unknown tag: " + tag); }
                            return;
                        }

                        if (string.Equals(tag, "base", StringComparison.Ordinal))
                        {
                            sawExact = true;
                        }
                        else if (sawExact)
                        {
                            lock (failures) { failures.Add("went back to the fallback after seeing the exact pair"); }
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
            Assert.Equal("base", Dwarf.Map<RaceBase, RaceDst>(new RaceDerived()).Tag);
        }

        /// <summary>
        ///     Update-into, both arms: the absent-map throw comes from <c>Update</c>, the registered delegate is applied
        ///     to the caller's own instance, and null arguments are refused before anything is looked up.
        /// </summary>
        [Fact]
        public void Update_into_throws_before_registration_and_applies_after()
        {
            var destination = new UpDst();
            Assert.Throws<DwarfMapMissingException>(() => Dwarf.Map(new UpSrc { V = 7 }, destination));

            DwarfMapperRegistry.RegisterUpdate(
                typeof(UpSrc),
                typeof(UpDst),
                (s, d) => ((UpDst)d).V = ((UpSrc)s).V);

            Dwarf.Map(new UpSrc { V = 7 }, destination);
            Assert.Equal(7, destination.V);

            DwarfMapperFacade.Instance.Map(new UpSrc { V = 9 }, destination);
            Assert.Equal(9, destination.V);

            Assert.Throws<ArgumentNullException>(() => Dwarf.Map((UpSrc)null!, destination));
            Assert.Throws<ArgumentNullException>(() => Dwarf.Map(new UpSrc(), (UpDst)null!));
        }

        private sealed class UpSrc
        {
            public int V { get; set; }
        }

        private sealed class UpDst
        {
            public int V { get; set; }
        }

        private class PairBase;

        private sealed class PairDerived : PairBase;

        private sealed class PairDst(string tag)
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
