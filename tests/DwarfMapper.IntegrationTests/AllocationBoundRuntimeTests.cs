// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    public struct AllocPointSrc
    {
        public int X { get; set; }

        public int Y { get; set; }

        public long Z { get; set; }
    }

    public struct AllocPointDst
    {
        public int X { get; set; }

        public int Y { get; set; }

        public long Z { get; set; }
    }

    [DwarfMapper]
    [GenerateMap<AllocPointSrc, AllocPointDst>]
    public partial class AllocStructMapper
    {
    }

    /// <summary>
    ///     IMPROVEMENT-PLAN item 16 (allocation half): a value-type (struct) map must allocate ZERO heap bytes — no
    ///     boxing, no hidden capture, no temporary. Measured with GC.GetAllocatedBytesForCurrentThread() on a thread of
    ///     its own (<see cref="CostContract.ExactBytesOnFreshThread" />), so neither a parallel test nor a background
    ///     collection can move the reading. Complements the static AOT-token meta-test
    ///     (ReflectionFreeMetaTests) with a dynamic zero-allocation guarantee.
    /// </summary>
    [Collection("allocation-isolated")]
    public class AllocationBoundRuntimeTests
    {
        [Fact]
        public void Blittable_struct_map_allocates_zero_bytes()
        {
            var mapper = new AllocStructMapper();
            var src = new AllocPointSrc
            {
                X = 1,
                Y = 2,
                Z = 3
            };

            // Warm up: force JIT of Map + this method so the measured window is steady-state.
            AllocPointDst sink = default;
            for (var i = 0; i < 200; i++) sink = mapper.Map(src);

            // One window, exactly zero, read on a thread that owns no allocation buffer: on this thread, which has
            // allocated, a background collection can move the counter (CostContract.ExactBytesOnFreshThread).
            var reading = CostContract.ExactBytesOnFreshThread(() => sink = mapper.Map(src), 10_000);

            // Keep the result observable so the JIT cannot elide the loop.
            Assert.Equal(3, sink.Z);
            Assert.Equal(0, reading.BeforeWindow);
            Assert.Equal(0, reading.InWindow);
        }

        // The oracle's own two halves: a zero that could not read an allocation would pass everything above.
        [Fact]
        public void The_fresh_thread_reading_sees_one_object_allocated_in_the_window()
        {
            object? planted = null;

            var reading = CostContract.ExactBytesOnFreshThread(() => planted = new object(), 1);

            Assert.NotNull(planted);
            Assert.Equal(0, reading.BeforeWindow);
            Assert.True(reading.InWindow > 0, $"one object allocated in the window read as {reading.InWindow} bytes");
        }

        [Fact]
        public void The_fresh_thread_reading_rethrows_what_the_operation_threw_on_the_calling_thread()
        {
            var thrown = Assert.Throws<InvalidOperationException>(() =>
                CostContract.ExactBytesOnFreshThread(() => throw new InvalidOperationException("from the window"), 1));

            Assert.Equal("from the window", thrown.Message);
        }
    }

    [CollectionDefinition("allocation-isolated", DisableParallelization = true)]
    public sealed class AllocationIsolatedCollection
    {
    }

    public class AllocSeqSrc
    {
        public IEnumerable<int> Values { get; set; } = new List<int>();
    }

    public class AllocSeqDst
    {
        public int[] Values { get; set; } = Array.Empty<int>();
    }

    [DwarfMapper]
    [GenerateMap<AllocSeqSrc, AllocSeqDst>]
    public partial class AllocSeqMapper
    {
    }

    /// <summary>
    ///     ISSUE-019: an <c>IEnumerable&lt;T&gt;</c> source has no statically-known count, so the array target was
    ///     filled through a growing <c>List&lt;T&gt;</c> and then copied out with <c>ToArray()</c> — two buffers and
    ///     a copy on every map. The runtime value is almost always a counted collection, so the emitted body now
    ///     probes for the count and fills an exactly-sized array.
    ///     <para>
    ///         Asserted as an allocation BOUND rather than a timing, per the audit's own guidance. A 1,000-element
    ///         <c>int[]</c> is ~4 KB; the old List-growth path reaches that capacity through doubling (which allocates
    ///         roughly 2x in total) and then allocates the 4 KB result on top. The ceiling below sits between the two,
    ///         so it fails on the old path and passes on the new one.
    ///     </para>
    /// </summary>
    [Collection("allocation-isolated")]
    public class UnknownCountArrayAllocationTests
    {
        [Fact]
        public void Enumerable_to_array_fills_a_single_exactly_sized_buffer()
        {
            var mapper = new AllocSeqMapper();
            // Typed as IEnumerable<int> statically (so the generator cannot know the count) but a List at runtime
            // (so the probe succeeds) — precisely the shape the fix targets.
            var src = new AllocSeqSrc
            {
                Values = new List<int>(Enumerable.Range(0, 1000))
            };

            var sink = mapper.Map(src);
            for (var i = 0; i < 50; i++) sink = mapper.Map(src);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) sink = mapper.Map(src);
            var allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;

            Assert.Equal(1000, sink.Values.Length);
            Assert.Equal(999, sink.Values[999]);
            Assert.True(allocated < 6_000, $"allocated {allocated} bytes per map; expected a single ~4KB buffer");
        }

        [Fact]
        public void Enumerable_to_array_is_correct_for_a_genuinely_uncounted_source()
        {
            // The probe must FAIL over here and fall back to the buffered path — correctness cannot depend on the
            // source's runtime type.
            var mapper = new AllocSeqMapper();
            var src = new AllocSeqSrc
            {
                Values = Uncounted()
            };

            var dst = mapper.Map(src);

            Assert.Equal(new[]
                {
                    10, 11, 12
                },
                dst.Values);

            static IEnumerable<int> Uncounted()
            {
                yield return 10;
                yield return 11;
                yield return 12;
            }
        }
    }
}
