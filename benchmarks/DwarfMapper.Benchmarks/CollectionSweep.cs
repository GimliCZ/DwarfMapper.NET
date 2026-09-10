// SPDX-License-Identifier: GPL-2.0-only

using AutoMapper;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using Mapster;

namespace DwarfMapper.Benchmarks
{
    /// <summary>
    ///     <b>The usage-space sweep: the same collection shapes as <see cref="MapperBenchmarks" />, across seven
    ///     decades of element count, with every rival in the same process.</b>
    ///     <para>
    ///         Why it is a SECOND class rather than a <c>[Params]</c> on the first one. The allocation gate
    ///         (<c>Assert-BenchAllocationsPinned</c> in <c>scripts/housekeeping.ps1</c>) keys its exact pins by
    ///         BenchmarkDotNet's <c>Method</c> name and compares the row count against
    ///         <c>allocation-baseline.json</c>'s <c>totalBenchmarks</c>. Adding an N axis to the pinned class
    ///         would emit seven rows per method name — the pin lookup would silently keep whichever row came
    ///         last, and the count check would fail. It would also multiply the nightly smoke's cost by seven.
    ///         The gate reads <c>MapperBenchmarks-report-full.json</c> BY CLASS NAME, so this class is invisible
    ///         to it, which is the property this design wants rather than a problem to work around.
    ///     </para>
    ///     <para>
    ///         <b>Nothing here is pinned or gated, deliberately.</b> Allocations along this axis are
    ///         N-proportional by construction, so an exact byte pin would be a pin on N and prove nothing. This
    ///         class exists to give each size-dependent claim a MEASURED BOUNDARY instead of a single point;
    ///         the regression gate stays where it is.
    ///     </para>
    ///     <para>
    ///         It also retires a workaround. <c>Issues/round29/sweep-results.md</c> ran its decade sweep as a
    ///         standalone probe OUTSIDE the repository for exactly the gate reason above, and paid for it with
    ///         an in-process-toolchain caveat that bounds every number in that file. In-repo and out-of-process
    ///         removes the caveat; running the rivals in the same process removes the other one, because
    ///         cross-run comparison on this machine is invalid (±17 % measured variance — two branches with
    ///         byte-identical emitted code once measured 20 % apart).
    ///     </para>
    ///     <para>
    ///         <b>NOT ShortRun, and that is a measured correction rather than a preference.</b> The first run of
    ///         this class used <c>[ShortRunJob]</c> (1 launch, 3 warmup, 3 iterations) on the argument that the
    ///         question is the SHAPE of a ratio across decades rather than a third decimal at one of them. The
    ///         resulting rows could not carry that argument: <c>SweepArray_Dwarf</c> measured
    ///         <b>63,045 ns ± 66,874 ns</b> at N = 10,000 — an error LARGER than the mean — and
    ///         <b>6.49 ms ± 7.24 ms</b> at N = 100,000. A ratio between two such rows is arithmetic on noise.
    ///     </para>
    ///     <para>
    ///         The cause is visible in the GC counters and is structural to this axis, so it will not go away by
    ///         re-running: past roughly N = 10,000 one operation allocates megabytes, so each iteration includes
    ///         whole collections whose timing lands in the measurement. <c>SweepBlit_Dwarf</c> at N = 10,000
    ///         allocates 120,060 B, past the 85 KB <b>Large Object Heap</b> threshold, and reports
    ///         <c>Gen0 = Gen1 = Gen2 = 36.99</c> — every collection a gen2. That is why its mean jumps 132x
    ///         between N = 1,000 and N = 10,000 while the element count rises 10x. The cliff is REAL and every
    ///         library pays it (all four converge to 1.00-1.05x there), but resolving it needs iterations, not
    ///         three of them.
    ///     </para>
    ///     <para>
    ///         So: 1 launch, 5 warmup, 10 measured iterations. Roughly 2.5x ShortRun's cost and still far below
    ///         the default job's 15+15, which at seven decades and four libraries would run for hours.
    ///         Standard errors stay reported per row and the rule stands: <b>a difference smaller than the
    ///         combined error is not a finding.</b> Any single decade worth quoting precisely should be re-run
    ///         against <see cref="MapperBenchmarks" />, which is the default-job instrument.
    ///     </para>
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 5, iterationCount: 10)]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [CategoriesColumn]
    public class CollectionSweepBenchmarks
    {
        private readonly DwarfM _dwarf = new();
        private readonly MapperlyM _mapperly = new();
        private ArraySrc _array = null!;
        private IMapper _auto = null!;
        private BlitSrc _blit = null!;
        private ListSrc _list = null!;

        /// <summary>
        ///     Seven decades, 10^0 to 10^6. The owner asked for logarithmic coverage from 1 to 6 and this is it;
        ///     N=1 is included because the fixed per-call overhead a mapper cannot amortise is only visible
        ///     there, and every comparative claim this repository publishes was made at N=1000 alone.
        /// </summary>
        [Params(1, 10, 100, 1_000, 10_000, 100_000, 1_000_000)]
        public int N { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            // Element CONTENT from the fixture/fuzz factory, element COUNT pinned to N — the same contract
            // MapperBenchmarks.Setup states. The factory builds 1-3 element collections, so letting it size
            // these would quietly turn an N=1,000,000 benchmark into N≈2.
            //
            // ONE DRAW PER SHAPE rather than MapperBenchmarks' 512-payload ring, and this is the one place
            // the two harnesses deliberately differ. A ring exists so a SMALL payload cannot sit in L1 with
            // its branches memorised; at N=1000 that matters and at N=1,000,000 a single 48 MB draw already
            // defeats any cache. Building 512 rings at 10^6 would allocate tens of gigabytes and measure the
            // setup, not the mapper. The cost is real and is stated: at N=1 and N=10 these rows measure a
            // hot, perfectly-predicted payload, so they are a FLOOR on per-call overhead, not a realistic
            // small-collection workload. Every library pays the identical advantage.
            var items = RealisticPayloads.Elements<FlatSrc>(N, 3);
            _array = new ArraySrc { Items = items };
            _list = new ListSrc { Items = [.. items] };
            _blit = new BlitSrc { Items = RealisticPayloads.Elements<Vec3Src>(N, 7) };

            var cfg = new MapperConfiguration(c =>
            {
                c.CreateMap<FlatSrc, FlatDst>();
                c.CreateMap<ArraySrc, ArrayDst>();
                c.CreateMap<ListSrc, ListDst>();
                c.CreateMap<Vec3Src, Vec3Dst>();
                c.CreateMap<BlitSrc, BlitDst>();
            });
            _auto = cfg.CreateMapper();
        }

        // ── Array: reference elements, T[] -> T[] ────────────────────────────────────────────────────
        // The category the 2026-08-24 sweep read as parity (1.01x, inside the standard error) at N=1000.
        // Parity at one length says nothing about the other six.

        [Benchmark]
        [BenchmarkCategory("SweepArray")]
        public ArrayDst SweepArray_Dwarf()
        {
            return _dwarf.MapArray(_array);
        }

        [Benchmark]
        [BenchmarkCategory("SweepArray")]
        public ArrayDst SweepArray_Mapperly()
        {
            return _mapperly.MapArray(_array);
        }

        [Benchmark]
        [BenchmarkCategory("SweepArray")]
        public ArrayDst SweepArray_Mapster()
        {
            return _array.Adapt<ArrayDst>();
        }

        [Benchmark]
        [BenchmarkCategory("SweepArray")]
        public ArrayDst SweepArray_AutoMapper()
        {
            return _auto.Map<ArrayDst>(_array);
        }

        // ── List: reference elements, List<T> -> List<T> ─────────────────────────────────────────────
        // The category where DwarfMapper trailed Mapster by 1.13x at N=1000. H1 in
        // Issues/round30/DESIGN-collection-benchmarks.md: a fixed-overhead difference and a per-element
        // difference have opposite shapes across seven decades, and only one of them is worth acting on.

        [Benchmark]
        [BenchmarkCategory("SweepList")]
        public ListDst SweepList_Dwarf()
        {
            return _dwarf.MapList(_list);
        }

        [Benchmark]
        [BenchmarkCategory("SweepList")]
        public ListDst SweepList_Mapperly()
        {
            return _mapperly.MapList(_list);
        }

        [Benchmark]
        [BenchmarkCategory("SweepList")]
        public ListDst SweepList_Mapster()
        {
            return _list.Adapt<ListDst>();
        }

        [Benchmark]
        [BenchmarkCategory("SweepList")]
        public ListDst SweepList_AutoMapper()
        {
            return _auto.Map<ListDst>(_list);
        }

        // ── Blit: layout-identical structs, the path this round most relies on ───────────────────────
        // Against the rivals, which copy field-by-field. sweep-results.md already swept this against its
        // own scalar twin and found no crossover in either direction; what it never did is sweep it
        // against the competitors.

        [Benchmark]
        [BenchmarkCategory("SweepBlit")]
        public BlitDst SweepBlit_Dwarf()
        {
            return _dwarf.MapBlit(_blit);
        }

        [Benchmark]
        [BenchmarkCategory("SweepBlit")]
        public BlitDst SweepBlit_Mapperly()
        {
            return _mapperly.MapBlit(_blit);
        }

        [Benchmark]
        [BenchmarkCategory("SweepBlit")]
        public BlitDst SweepBlit_Mapster()
        {
            return _blit.Adapt<BlitDst>();
        }

        [Benchmark]
        [BenchmarkCategory("SweepBlit")]
        public BlitDst SweepBlit_AutoMapper()
        {
            return _auto.Map<BlitDst>(_blit);
        }

        // ── The blit against its OWN scalar twin, swept ──────────────────────────────────────────────
        // Same pair-selection discipline as BlitRatio in the gated class: identical payload, identical
        // process, so machine drift cancels inside the measurement. The gated pair is pinned at N=1000
        // with a 1.5x floor; this says whether that floor describes one length or all of them.

        [Benchmark]
        [BenchmarkCategory("SweepBlitRatio")]
        public BlitDst SweepBlitRatio_Fast()
        {
            return _dwarf.MapBlit(_blit);
        }

        [Benchmark]
        [BenchmarkCategory("SweepBlitRatio")]
        public BlitScalarDst SweepBlitRatio_Scalar()
        {
            return _dwarf.MapBlitScalar(_blit);
        }
    }
}
