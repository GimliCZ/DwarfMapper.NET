// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace DwarfMapper.Benchmarks
{
    public sealed class W33ArraySrc
    {
        public int[] V { get; set; } = [];
    }

    public sealed class W33ListSrc
    {
        public List<int> V { get; set; } = [];
    }

    public sealed class W33ListDst
    {
        public List<long> V { get; set; } = [];
    }

    public sealed class W33ArrayDst
    {
        public long[] V { get; set; } = [];
    }

    [DwarfMapper]
    public partial class Round33WidenM
    {
        public partial W33ListDst MapArrayToList(W33ArraySrc s);

        public partial W33ListDst MapListToList(W33ListSrc s);

        public partial W33ArrayDst MapListToArray(W33ListSrc s);
    }

    /// <summary>
    ///     <b>Round 33 T02: a lossless primitive widen into or out of a <c>List&lt;T&gt;</c> takes the <c>Vector.Widen</c> loop.</b>
    ///     <para>
    ///         Round 31 T15 found the generated <c>int[] → List&lt;long&gt;</c> fill 1.6x slower than
    ///         <c>Select(x =&gt; (long)x).ToList()</c> (ShortRun), and this round's baseline on master reproduced it at
    ///         1.48x (DefaultJob): the SIMD widen was gated to array → array, so a List shape took the general value fill
    ///         <c>foreach … __d[__i++] = __item</c>, an index the JIT cannot prove in range.
    ///     </para>
    ///     <para>
    ///         Each shape has three arms in ONE process, on one payload (the LegacyAmbientFacade convention): the Before arm
    ///         is the previously emitted mapper and helper, verbatim; After calls the generated mapper; the LINQ twin is
    ///         the bar round 31 measured against. The round's threshold is ≥ 1.3x or ≥ 20 % allocation, Before against
    ///         After, consistently - at n ≈ 1,024, where round 25 found these kernels' win lives (at ~1 MB both arms
    ///         are bandwidth-bound and the copy strategy stops mattering).
    ///     </para>
    /// </summary>
    [MemoryDiagnoser]
    public class Round33WidenListBenchmarks
    {
        private readonly Round33WidenM _mapper = new();
        private W33ArraySrc _array = null!;
        private W33ListSrc _list = null!;

        [Params(16, 1024)]
        public int N { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _array = new W33ArraySrc { V = [.. Enumerable.Range(0, N)] };
            _list = new W33ListSrc { V = [.. Enumerable.Range(0, N)] };
        }

        // ── int[] → List<long> ───────────────────────────────────────────────────────────────────────────────
        [Benchmark(Baseline = true)]
        [BenchmarkCategory("ArrayToList")]
        public List<long> ArrayToList_LinqTwin()
        {
            return _array.V.Select(x => (long)x).ToList();
        }

        [Benchmark]
        [BenchmarkCategory("ArrayToList")]
        public W33ListDst ArrayToList_Before()
        {
            return BeforeMapArrayToList(_array);
        }

        [Benchmark]
        [BenchmarkCategory("ArrayToList")]
        public W33ListDst ArrayToList_After()
        {
            return _mapper.MapArrayToList(_array);
        }

        // ── List<int> → List<long> ───────────────────────────────────────────────────────────────────────────
        [Benchmark]
        [BenchmarkCategory("ListToList")]
        public List<long> ListToList_LinqTwin()
        {
            return _list.V.Select(x => (long)x).ToList();
        }

        [Benchmark]
        [BenchmarkCategory("ListToList")]
        public W33ListDst ListToList_Before()
        {
            return BeforeMapListToList(_list);
        }

        [Benchmark]
        [BenchmarkCategory("ListToList")]
        public W33ListDst ListToList_After()
        {
            return _mapper.MapListToList(_list);
        }

        // ── List<int> → long[] ───────────────────────────────────────────────────────────────────────────────
        [Benchmark]
        [BenchmarkCategory("ListToArray")]
        public long[] ListToArray_LinqTwin()
        {
            return _list.V.Select(x => (long)x).ToArray();
        }

        [Benchmark]
        [BenchmarkCategory("ListToArray")]
        public W33ArrayDst ListToArray_Before()
        {
            return BeforeMapListToArray(_list);
        }

        [Benchmark]
        [BenchmarkCategory("ListToArray")]
        public W33ArrayDst ListToArray_After()
        {
            return _mapper.MapListToArray(_list);
        }

        // ── The Before arms: the mappers and helpers as emitted before round 33 T02, verbatim ──────────────────
        private static W33ListDst BeforeMapArrayToList(W33ArraySrc s)
        {
            ArgumentNullException.ThrowIfNull(s);
            return new W33ListDst { V = BeforeArrayToList(s.V) };
        }

        private static W33ListDst BeforeMapListToList(W33ListSrc s)
        {
            ArgumentNullException.ThrowIfNull(s);
            return new W33ListDst { V = BeforeListToList(s.V) };
        }

        private static W33ArrayDst BeforeMapListToArray(W33ListSrc s)
        {
            ArgumentNullException.ThrowIfNull(s);
            return new W33ArrayDst { V = BeforeListToArray(s.V) };
        }

        private static List<long> BeforeArrayToList(int[]? src)
        {
            if (src is null) return new List<long>();
            var __n = src.Length;
            var __r = new List<long>(__n);
            CollectionsMarshal.SetCount(__r, __n);
            var __d = CollectionsMarshal.AsSpan(__r);
            var __i = 0;
            foreach (var __item in src) { __d[__i++] = __item; }
            return __r;
        }

        private static List<long> BeforeListToList(List<int>? src)
        {
            if (src is null) return new List<long>();
            var __n = src.Count;
            var __r = new List<long>(__n);
            CollectionsMarshal.SetCount(__r, __n);
            var __d = CollectionsMarshal.AsSpan(__r);
            var __i = 0;
            foreach (var __item in src) { __d[__i++] = __item; }
            return __r;
        }

        private static long[] BeforeListToArray(List<int>? src)
        {
            if (src is null) return Array.Empty<long>();
            var __r = new long[src.Count];
            var __i = 0;
            foreach (var __item in src) { __r[__i++] = __item; }
            return __r;
        }
    }
}
