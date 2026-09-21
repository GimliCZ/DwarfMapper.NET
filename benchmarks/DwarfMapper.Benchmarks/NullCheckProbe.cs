// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;

namespace DwarfMapper.Benchmarks
{
    /// <summary>
    ///     <b>Does the redundant per-element null check cost anything?</b> Hand-written arms only — this probe
    ///     changes no emission and proves nothing about the generator until it says the cost is real.
    ///     <para>
    ///         What it isolates. Reading the emitted code for <c>FlatSrc[] -&gt; FlatDst[]</c> against Mapperly's
    ///         for the identical shape leaves exactly one structural difference:
    ///     </para>
    ///     <code>
    ///     // Mapperly:      target[i] = MapFlat(source[i]);          // MapFlat has NO null guard
    ///     // DwarfMapper:   var __item = src[__i];
    ///     //                __r[__i] = (__item is null ? null! : MapFlat(__item));
    ///     //                          // ...and MapFlat opens with ArgumentNullException.ThrowIfNull(s)
    ///     </code>
    ///     <para>
    ///         So DwarfMapper performs TWO null tests per element where Mapperly performs none. The first is a
    ///         deliberate safety property and stays. The second is redundant by construction: the loop has just
    ///         established the element is non-null. Whether the JIT already removes it depends on whether it
    ///         inlines a public partial method that also allocates — which is a question for a measurement, not
    ///         for reasoning.
    ///     </para>
    ///     <para>
    ///         Four arms over ONE payload draw, same process, so machine drift cancels inside the comparison:
    ///     </para>
    ///     <list type="number">
    ///         <item><description><c>Current</c> — the emitted shape, replicated exactly.</description></item>
    ///         <item><description><c>CoreCall</c> — the loop's null test kept, the callee's removed
    ///             (an inlinable non-validating core). This is the ONLY change a fix would make.</description></item>
    ///         <item><description><c>Unguarded</c> — Mapperly's shape, no null test anywhere. The FLOOR: it
    ///             abandons the safety property, so it is a bound on what is available, not an option.</description></item>
    ///         <item><description><c>GuardOnly</c> — the callee's guard kept, the loop's removed. Separates
    ///             which of the two checks is the expensive one, if either is.</description></item>
    ///     </list>
    ///     <para>
    ///         <b>Read it as: CoreCall - Current is the win a generator change could deliver; Unguarded is the
    ///         price of the safety property itself.</b> If Current and CoreCall are within their combined error,
    ///         the JIT is already removing it and the emission should not change.
    ///     </para>
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 5, iterationCount: 10)]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [CategoriesColumn]
    public class NullCheckProbeBenchmarks
    {
        private FlatSrc[] _src = null!;

        // Decades that are NOT dominated by the large-object heap: the sweep measured the destination array
        // crossing 85 KB between N=1,000 and N=10,000, after which every operation includes gen2 collections
        // and a per-element difference of a few nanoseconds is unresolvable. This probe asks a per-element
        // question, so it stays where a per-element answer survives.
        [Params(100, 1_000, 10_000)]
        public int N { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _src = RealisticPayloads.Elements<FlatSrc>(N, 3);
        }

        /// <summary>The emitted shape, replicated line for line from <c>DwarfM.g.cs</c>.</summary>
        [Benchmark(Baseline = true)]
        [BenchmarkCategory("NullCheck")]
        public FlatDst[] NullCheck_Current()
        {
            var src = _src;
            var r = new FlatDst[src.Length];
            for (var i = 0; i < src.Length; i++)
            {
                var item = src[i];
                r[i] = item is null ? null! : Guarded(item);
            }

            return r;
        }

        /// <summary>The loop's null test kept, the callee's removed. The only change a fix would make.</summary>
        [Benchmark]
        [BenchmarkCategory("NullCheck")]
        public FlatDst[] NullCheck_CoreCall()
        {
            var src = _src;
            var r = new FlatDst[src.Length];
            for (var i = 0; i < src.Length; i++)
            {
                var item = src[i];
                r[i] = item is null ? null! : Core(item);
            }

            return r;
        }

        /// <summary>Mapperly's shape. A FLOOR, not an option: it drops the null guarantee entirely.</summary>
        [Benchmark]
        [BenchmarkCategory("NullCheck")]
        public FlatDst[] NullCheck_Unguarded()
        {
            var src = _src;
            var r = new FlatDst[src.Length];
            for (var i = 0; i < src.Length; i++) { r[i] = Core(src[i]); }

            return r;
        }

        /// <summary>The callee's guard only — which of the two checks carries the cost, if either does.</summary>
        [Benchmark]
        [BenchmarkCategory("NullCheck")]
        public FlatDst[] NullCheck_GuardOnly()
        {
            var src = _src;
            var r = new FlatDst[src.Length];
            for (var i = 0; i < src.Length; i++) { r[i] = Guarded(src[i]); }

            return r;
        }

        // NoInlining on NEITHER: the question is what the JIT does with the code as emitted, so forcing its
        // hand in either direction would answer a different question. Guarded mirrors the emitted public
        // partial method (ThrowIfNull, then an object initializer); Core is what a private non-validating
        // core would look like.
        private static FlatDst Guarded(FlatSrc s)
        {
            ArgumentNullException.ThrowIfNull(s);
            return new FlatDst
            {
                Active = s.Active,
                Id = s.Id,
                Name = s.Name,
                Score = s.Score
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static FlatDst Core(FlatSrc s)
        {
            return new FlatDst
            {
                Active = s.Active,
                Id = s.Id,
                Name = s.Name,
                Score = s.Score
            };
        }
    }
}
