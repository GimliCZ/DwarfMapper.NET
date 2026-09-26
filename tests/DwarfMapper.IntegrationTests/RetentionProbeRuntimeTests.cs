// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.CompilerServices;

namespace DwarfMapper.IntegrationTests
{
    // Round-30 item F. A consumer reported memory growing without bound in a long-lived Blazor Server
    // process using the ambient facade. Reading the runtime says it cannot happen: the registry is
    // first-wins ConcurrentDictionary keyed by (Type, Type) so it is bounded by the type graph, and
    // DwarfRefContext's identity map and on-stack set are INSTANCE fields of a per-invocation object.
    //
    // Reading is not measuring, and this suite had no retention test of any kind — the one gap where a
    // leak could live indefinitely without a red build. These tests are the instrument, and they are
    // written to FAIL if the reasoning above ever stops being true.
    //
    // Two distinct properties, because they fail differently and only one of them is a leak:
    //   * RETENTION — bytes still reachable after a full blocking collect. Growth here is a real leak.
    //   * CHURN     — bytes allocated then immediately garbage. Growth here inflates a memory graph
    //                 under Server GC and READS like a leak in a profiler, but collects fine.
    // A report of "memory keeps rising" is consistent with either, so the probe measures both and the
    // failure message says which one moved.

    public sealed class RetainNode
    {
        public int V { get; set; }

        public string Payload { get; set; } = string.Empty;

        public RetainNode? Next { get; set; }
    }

    public sealed class RetainNodeDto
    {
        public int V { get; set; }

        public string Payload { get; set; } = string.Empty;

        public RetainNodeDto? Next { get; set; }
    }

    // Preserve mode is the arm that ALLOCATES the identity dictionary (DwarfRefContext._identity). If any
    // map is going to root its source graph, it is this one: the dictionary holds source -> target for the
    // whole invocation, so a context that outlived the call would pin every node it walked.
    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    [GenerateMap<RetainNode, RetainNodeDto>]
    public partial class RetainPreserveMapper
    {
    }

    // The None-mode twin. Same graph, no identity dictionary — the control that attributes any retention
    // the Preserve arm shows to the identity map rather than to mapping in general.
    [DwarfMapper]
    [GenerateMap<RetainNode, RetainNodeDto>]
    public partial class RetainPlainMapper
    {
    }

    /// <summary>
    ///     Retention and churn probes for the mapping runtime. Own collection: these force full blocking
    ///     collections and read process-wide heap size, so a parallel test allocating on another thread
    ///     would show up as noise in the measurement.
    /// </summary>
    [Collection("allocation-isolated")]
    public class RetentionProbeRuntimeTests
    {
        private const int GraphLength = 50;

        /// <summary>
        ///     The sharpest instrument available, and the one that answers the consumer's question directly:
        ///     after a map returns, is the SOURCE graph collectable?
        /// </summary>
        /// <remarks>
        ///     A <see cref="WeakReference" /> that survives a full blocking collect proves something still
        ///     roots the source — which is exactly what "unreturned memory" means. This is stronger than any
        ///     byte-counting assertion: it names the object rather than a number, so a failure points at the
        ///     graph rather than at a threshold someone can retune until it passes.
        ///     <para>
        ///         The graph is built and mapped inside a <c>NoInlining</c> helper that returns only the weak
        ///         handle. Without that, the JIT is entitled to keep the local alive to the end of the
        ///         enclosing method (it always does in Debug), and the test would fail for a reason that has
        ///         nothing to do with the mapper.
        ///     </para>
        /// </remarks>
        [Fact]
        public void A_mapped_source_graph_is_collectable_once_the_call_returns()
        {
            var (weakSource, weakResult) = MapAndForget(new RetainPreserveMapper());

            Collect();

            Assert.False(weakSource.IsAlive,
                "the SOURCE graph is still reachable after a full blocking collect — something in the " +
                "mapping runtime rooted it. DwarfRefContext's identity map is the only structure that " +
                "holds source references, and it is an instance field of a per-invocation object, so a " +
                "failure here means that context outlived its call.");
            Assert.False(weakResult.IsAlive,
                "the mapped RESULT is still reachable after a full blocking collect, though the caller " +
                "dropped it — the runtime is holding mapped outputs.");
        }

        /// <summary>
        ///     The None-mode control for the test above. If both arms leaked, the identity map would be
        ///     exonerated and the fault would lie in the emitted map itself; if only Preserve leaked, the
        ///     context is the culprit. A test that measures one arm cannot tell those apart.
        /// </summary>
        [Fact]
        public void A_mapped_source_graph_is_collectable_in_none_mode_too()
        {
            var (weakSource, weakResult) = MapAndForget(new RetainPlainMapper());

            Collect();

            Assert.False(weakSource.IsAlive, "None-mode map rooted its source graph.");
            Assert.False(weakResult.IsAlive, "None-mode map rooted its result.");
        }

        /// <summary>
        ///     Steady-state retention across decades of call count. Flat means no leak; a slope means every
        ///     call keeps something.
        /// </summary>
        /// <remarks>
        ///     Measured as the retained heap AFTER a full blocking collect, so ordinary garbage does not
        ///     count. The comparison is between two equal-sized batches rather than against an absolute
        ///     budget: absolute heap size depends on the test host, the GC flavour and whatever ran before,
        ///     none of which this test controls. A slope between two batches measured the same way in the
        ///     same process is a property of the mapper.
        ///     <para>
        ///         The tolerance is deliberately loose. This test exists to catch UNBOUNDED growth — a real
        ///         per-call leak of a 50-node graph over 20,000 calls would retain megabytes and blow any
        ///         threshold. A tight bound here would buy nothing and would flake on GC scheduling.
        ///     </para>
        /// </remarks>
        [Fact]
        public void Repeated_mapping_does_not_grow_the_retained_heap()
        {
            var mapper = new RetainPreserveMapper();

            // Warm up so JIT, statics and the first-touch of any lazily built structure are outside the
            // measured window. Without this the first batch pays one-time costs and reads as a leak.
            RunBatch(mapper, 2_000);
            Collect();
            var afterFirst = GC.GetTotalMemory(true);

            RunBatch(mapper, 20_000);
            Collect();
            var afterSecond = GC.GetTotalMemory(true);

            var growth = afterSecond - afterFirst;

            // 20,000 calls each mapping a 50-node graph. If a single node (or its context entry) were
            // retained per call, this would be in the megabytes. 4 MB leaves generous room for host noise
            // while still being far below any real per-call retention.
            Assert.True(growth < 4L * 1024 * 1024,
                $"retained heap grew by {growth:N0} bytes across 20,000 additional maps (from " +
                $"{afterFirst:N0} to {afterSecond:N0}). Retention is supposed to be flat: every structure " +
                "the mapper allocates is per-invocation. A slope here is a leak.");
        }

        /// <summary>
        ///     Churn, not retention: how many bytes a single map call allocates. This is the measurement that
        ///     distinguishes "the profiler graph rises because we leak" from "it rises because we allocate
        ///     hard and Server GC collects late".
        /// </summary>
        /// <remarks>
        ///     Per-thread counter, so a parallel test cannot contaminate it. No upper bound is asserted on the
        ///     graph map — a 50-node graph legitimately allocates 50 DTOs plus the identity dictionary. What
        ///     is asserted is that the per-call cost does not depend on how many calls came before it, which
        ///     is the property that separates churn from accumulation.
        /// </remarks>
        [Fact]
        public void Per_call_allocation_does_not_grow_with_call_count()
        {
            var mapper = new RetainPreserveMapper();
            var source = BuildGraph();

            RunOnce(mapper, source, 500);

            var early = MeasurePerCallBytes(mapper, source, 500);
            RunOnce(mapper, source, 20_000);
            var late = MeasurePerCallBytes(mapper, source, 500);

            // Allow a wide band: the counter is per-thread but the JIT may still tier up between samples.
            Assert.True(late <= early * 1.5 + 256,
                $"per-call allocation rose from {early:N0} to {late:N0} bytes after 20,000 intervening " +
                "calls. A per-call cost that grows with history means a structure the mapper walks is " +
                "getting longer — accumulation inside the mapping path.");
        }

        /// <summary>
        ///     The control. Everything above asserts that something is NOT alive, and a test of that shape
        ///     passes just as happily when the instrument is broken — if <see cref="Collect" /> did not really
        ///     collect, or a <see cref="WeakReference" /> never reported liveness, every retention test here
        ///     would go green while measuring nothing.
        /// </summary>
        /// <remarks>
        ///     This repository has produced six mechanisms that reported success while measuring nothing, so a
        ///     control is not ceremony here. It roots a graph deliberately, in the same shape the leak would
        ///     take (a static list, which is what an accumulating registry would be), and requires the probe
        ///     to SEE it. If this test fails, the four above are vacuous and their green means nothing.
        /// </remarks>
        [Fact]
        public void The_probe_detects_retention_when_retention_is_real()
        {
            var weak = RootAGraphDeliberately();

            Collect();

            Assert.True(weak.IsAlive,
                "the deliberately-rooted graph was reported collectable, so this probe cannot detect a " +
                "real leak and every other test in this class is vacuous.");

            DeliberateRoots.Clear();
            Collect();

            Assert.False(weak.IsAlive,
                "the graph stayed alive after its only root was cleared — the probe reports retention that " +
                "is not there, which would make the other tests flaky rather than vacuous.");
        }

        /// <summary>Stands in for whatever an accumulating runtime structure would be.</summary>
        private static readonly List<RetainNode> DeliberateRoots = [];

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference RootAGraphDeliberately()
        {
            var graph = BuildGraph();
            DeliberateRoots.Add(graph);
            return new WeakReference(graph);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long MeasurePerCallBytes(object mapper, RetainNode source, int iterations)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            RunOnce(mapper, source, iterations);
            return (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunOnce(object mapper, RetainNode source, int iterations)
        {
            for (var i = 0; i < iterations; i++)
            {
                RetainNodeDto dto = mapper is RetainPreserveMapper p
                    ? p.Map(source)
                    : ((RetainPlainMapper)mapper).Map(source);
                GC.KeepAlive(dto);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunBatch(RetainPreserveMapper mapper, int iterations)
        {
            for (var i = 0; i < iterations; i++)
            {
                var dto = mapper.Map(BuildGraph());
                GC.KeepAlive(dto);
            }
        }

        /// <summary>
        ///     Builds the graph, maps it, and returns ONLY weak handles — no strong reference to either the
        ///     source or the result escapes this frame.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (WeakReference Source, WeakReference Result) MapAndForget(object mapper)
        {
            var source = BuildGraph();
            RetainNodeDto result = mapper is RetainPreserveMapper p
                ? p.Map(source)
                : ((RetainPlainMapper)mapper).Map(source);

            return (new WeakReference(source), new WeakReference(result));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static RetainNode BuildGraph()
        {
            var head = new RetainNode { V = 0, Payload = "node-0" };
            var cursor = head;
            for (var i = 1; i < GraphLength; i++)
            {
                cursor.Next = new RetainNode { V = i, Payload = "node-" + i };
                cursor = cursor.Next;
            }

            return head;
        }

        /// <summary>
        ///     Two collect passes with a finalizer drain between them: the first pass queues finalizable
        ///     objects, the second reclaims what the finalizers released. One pass alone leaves anything
        ///     with a finalizer alive and would produce a false leak report.
        /// </summary>
        private static void Collect()
        {
            for (var i = 0; i < 2; i++)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
            }

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        }
    }
}
