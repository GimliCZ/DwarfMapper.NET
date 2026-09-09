// SPDX-License-Identifier: GPL-2.0-only

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;

namespace DwarfMapper.Benchmarks
{
    public sealed class FzA
    {
        public int Id { get; set; }

        public long Score { get; set; }

        public double Weight { get; set; }

        public int Extra { get; set; }
    }

    /// <summary>The intermediate. If the JIT elides it, it never reaches the heap on the chained path.</summary>
    public sealed class FzB
    {
        public int Id { get; set; }

        public long Score { get; set; }

        public double Weight { get; set; }

        public int Extra { get; set; }
    }

    public sealed class FzC
    {
        public int Id { get; set; }

        public long Score { get; set; }

        public double Weight { get; set; }

        public int Extra { get; set; }
    }

    /// <summary>
    ///     Real generated maps, not hand-written stand-ins. <c>MapAB</c> and <c>MapBC</c> are the chain;
    ///     <c>MapAC</c> is what a fused emission would produce, expressed as an ordinary member-wise map.
    /// </summary>
    [DwarfMapper]
    public partial class FuseM
    {
        public partial FzB MapAB(FzA a);

        public partial FzC MapBC(FzB b);

        public partial FzC MapAC(FzA a);
    }

    /// <summary>
    ///     <b>Map fusion, re-measured against GENERATOR OUTPUT.</b>
    ///     <para>
    ///         <c>Issues/round29/SPIKE-map-fusion.md</c> answered the plan's question — does the JIT already elide
    ///         the intermediate, making a fused emission pointless — and found a boundary rather than a yes or a
    ///         no: <b>elided in straight-line code, NOT elided inside a per-element loop.</b> That matters because
    ///         every collection map the generator emits is a per-element loop.
    ///     </para>
    ///     <para>
    ///         But the spike measured <c>private static</c> methods, and said so as its first caveat: <i>"A real
    ///         emitted pair is a <c>public partial</c> method on a mapper class, and whether the inlining that
    ///         makes B a candidate happens identically there is unmeasured. This is the exact shape of the 'right
    ///         number, wrong population' error this round made four times."</i> This class closes that caveat by
    ///         running the identical arms through <see cref="FuseM" />, whose bodies the generator wrote.
    ///     </para>
    ///     <para>
    ///         <b>The instrument is ALLOCATION, not time</b>, for the reason the spike gives: allocated bytes are
    ///         deterministic per SDK and the answer is a yes/no. If the JIT elides <see cref="FzB" />, the chained
    ///         path allocates only the final <see cref="FzC" /> and its byte count EQUALS the direct path's. If it
    ///         does not, the difference is exactly one <c>FzB</c> per element. <c>ChainedEscaping</c> keeps every
    ///         intermediate alive in an array so the JIT cannot elide it — without that control, two equal numbers
    ///         would prove nothing about why.
    ///     </para>
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 5, iterationCount: 10)]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [CategoriesColumn]
    public class FusionProbeBenchmarks
    {
        private readonly FuseM _m = new();
        private FzA _one = null!;
        private FzA[] _src = null!;

        [Params(1, 1_000)]
        public int N { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _src = RealisticPayloads.Elements<FzA>(N, 11);
            _one = RealisticPayloads.One<FzA>(12);

            // THE ARMS MUST COMPUTE THE SAME THING, checked here rather than assumed. A fusion benchmark
            // whose two paths disagree is not measuring a saving — it is measuring the cost of computing
            // something else, which is exactly what the enum row did for months (a by-name switch against a
            // by-value cast on reordered enums, published as a 3.98x Mapperly win; see
            // benchmarks/results/2026-08-26-enum-strategy-like-for-like.md). Setup is not measured, so this
            // costs the benchmark nothing and removes that failure mode.
            //
            // MapFusionEquivalenceTests is the real oracle — fuzzed, structural, with a sabotage control.
            // This is the same premise re-checked against THIS harness's own payload draw, because the two
            // use different types and a benchmark that quietly stopped agreeing would not fail that test.
            foreach (var a in _src.Take(64).Append(_one))
            {
                var chained = this._m.MapBC(this._m.MapAB(a));
                var direct = this._m.MapAC(a);
                if (chained.Id != direct.Id || chained.Score != direct.Score ||
                    chained.Extra != direct.Extra || !chained.Weight.Equals(direct.Weight))
                {
                    throw new InvalidOperationException(
                        "FusionProbeBenchmarks: the chained and direct arms disagree, so every figure this " +
                        "class produces compares two different operations. Fix the maps before reading a " +
                        "single number from it.");
                }
            }
        }

        // ── In a per-element loop: the shape every collection map emits ──────────────────────────────

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("FuseLoop")]
        public FzC[] Fuse_Chained()
        {
            var r = new FzC[_src.Length];
            for (var i = 0; i < r.Length; i++) { r[i] = _m.MapBC(_m.MapAB(_src[i])); }

            return r;
        }

        [Benchmark]
        [BenchmarkCategory("FuseLoop")]
        public FzC[] Fuse_Direct()
        {
            var r = new FzC[_src.Length];
            for (var i = 0; i < r.Length; i++) { r[i] = _m.MapAC(_src[i]); }

            return r;
        }

        /// <summary>The control: every intermediate outlives the loop, so the JIT CANNOT elide it.</summary>
        [Benchmark]
        [BenchmarkCategory("FuseLoop")]
        public FzC[] Fuse_ChainedEscaping()
        {
            var r = new FzC[_src.Length];
            var keep = new FzB[_src.Length];
            for (var i = 0; i < r.Length; i++)
            {
                keep[i] = _m.MapAB(_src[i]);
                r[i] = _m.MapBC(keep[i]);
            }

            return r;
        }

        // ── Straight-line, one element: where the spike found the JIT DOES elide ─────────────────────
        // N does not enter these; they are listed under their own category so the loop arms' baseline is
        // not compared against them. Two rows per N is duplicate work and harmless.

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("FuseStraight")]
        public FzC Fuse_Chained_Straight()
        {
            return _m.MapBC(_m.MapAB(_one));
        }

        [Benchmark]
        [BenchmarkCategory("FuseStraight")]
        public FzC Fuse_Direct_Straight()
        {
            return _m.MapAC(_one);
        }
    }
}
