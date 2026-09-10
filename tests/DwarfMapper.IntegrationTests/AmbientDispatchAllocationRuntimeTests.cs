// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    public interface IAmbientAllocSrc
    {
        int V { get; }
    }

    public sealed class AmbientAllocSrc : IAmbientAllocSrc
    {
        public int V { get; set; }
    }

    public sealed class AmbientAllocDst
    {
        public int V { get; set; }
    }

    /// <summary>
    ///     Round-30 item F: the ambient registry's interface-resolution path must cost O(1) ALLOCATION per
    ///     call, not O(number of registered maps).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Found while investigating a consumer report of memory rising without bound in a long-lived
    ///         Blazor Server process. It is not a leak — <c>RetentionProbeRuntimeTests</c> proves retention is
    ///         flat and that its instrument can see a real one. It is CHURN, which under Server GC produces
    ///         the same rising sawtooth in a profiler and is why the report was credible.
    ///     </para>
    ///     <para>
    ///         The cause is that <c>InterfaceMaps</c> was a <see cref="System.Collections.Concurrent.ConcurrentBag{T}" />
    ///         and <c>Map</c> walks it with <c>foreach</c>. A bag's enumerator does not iterate in place: it
    ///         COPIES every element into a fresh list on each enumeration. So the per-call allocation of an
    ///         ambient interface map grew with the size of the consumer's whole map graph — measured at
    ///         56 KB per call against 2,740 registered pairs, versus 24 B on the exact-type path. A consumer
    ///         with a large map graph pays that on every single ambient call.
    ///     </para>
    ///     <para>
    ///         The bound below is deliberately generous — two orders of magnitude above the fixed cost and
    ///         two below the defect — because the point is the SHAPE of the cost, not a byte-exact pin. Any
    ///         return to a per-call copy of the registry blows it immediately.
    ///     </para>
    /// </remarks>
    [Collection("allocation-isolated")]
    public class AmbientDispatchAllocationRuntimeTests
    {
        [Fact]
        public void Interface_path_dispatch_does_not_allocate_a_copy_of_the_registry()
        {
            DwarfMapperRegistry.Register(typeof(IAmbientAllocSrc), typeof(AmbientAllocDst),
                s => new AmbientAllocDst { V = ((IAmbientAllocSrc)s).V });

            // A source whose RUNTIME type is not registered and whose base chain misses, so resolution is
            // forced all the way down to the interface walk — the path under test.
            object source = new AmbientAllocSrc { V = 7 };

            // Warm up: JIT the whole dispatch chain so the measured window is steady-state.
            for (var i = 0; i < 1_000; i++)
                DwarfMapperRegistry.Map(source, typeof(AmbientAllocDst));

            const int iterations = 10_000;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < iterations; i++)
                DwarfMapperRegistry.Map(source, typeof(AmbientAllocDst));
            var perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)iterations;

            // The registry is process-wide and every other test in this assembly has registered into it, so
            // this runs against a realistically large graph rather than a two-entry toy.
            var registered = DwarfMapperRegistry.Provided.Count;

            // VACUITY GUARD. The defect this test exists for costs 80 + 24*N bytes per call, where N is the
            // number of registered interface maps — it is a SLOPE, not a fixed cost. At N = 2 the old broken
            // code allocated ~130 B and would sail under the bound below; the bug only becomes visible at
            // the scale a real consumer runs at. So the measurement is only meaningful against a large
            // registry, and this test must FAIL rather than pass when it is run in isolation.
            //
            // That is exactly why the allocation benchmark gate missed this: it pins exact bytes at one
            // registry size, and a pin at a single point on a line cannot see the line's gradient.
            Assert.True(registered > 500,
                $"only {registered} pairs are registered, so this measurement cannot distinguish the " +
                "defect from correct behaviour — at small N the per-call registry copy is a few hundred " +
                "bytes and passes the bound below. Run the whole assembly, not this test alone.");

            Assert.True(perCall < 512,
                $"ambient interface-path dispatch allocated {perCall:F0} B/call against {registered:N0} " +
                "registered pairs. This path must not copy the registry per call — the allocation is " +
                "supposed to be the mapped result and nothing else.");
        }

        /// <summary>
        ///     The control: the exact-type path, which never touched the bag, is the reference cost. Without
        ///     it a passing bound above could mean the interface arm was never reached — the resolution order
        ///     is exact type, then base chain, then interfaces, so a registration mistake in the fixture
        ///     would silently measure the cheap path and go green.
        /// </summary>
        [Fact]
        public void The_interface_path_is_actually_the_path_being_measured()
        {
            DwarfMapperRegistry.Register(typeof(IAmbientAllocSrc), typeof(AmbientAllocDst),
                s => new AmbientAllocDst { V = ((IAmbientAllocSrc)s).V });

            object source = new AmbientAllocSrc { V = 11 };

            // If the concrete type were registered, resolution would stop before the interface walk and the
            // test above would be measuring the wrong thing.
            Assert.False(DwarfMapperRegistry.IsProvided(typeof(AmbientAllocSrc), typeof(AmbientAllocDst)),
                "the concrete source type is registered, so dispatch never reaches the interface walk and " +
                "the allocation bound above is measuring the exact-type path instead.");
            Assert.True(DwarfMapperRegistry.IsProvided(typeof(IAmbientAllocSrc), typeof(AmbientAllocDst)));

            // And it resolves, so the walk is exercised rather than throwing past the measured region.
            var mapped = Assert.IsType<AmbientAllocDst>(
                DwarfMapperRegistry.Map(source, typeof(AmbientAllocDst)));
            Assert.Equal(11, mapped.V);
        }
    }
}
