// SPDX-License-Identifier: GPL-2.0-only

using BenchmarkDotNet.Attributes;

namespace DwarfMapper.Benchmarks
{
    /// <summary>
    ///     <b>Round 33 probe: is the array path's null-element cost the TEST, or the STORE of a <c>?:</c> value?</b>
    ///     <para>
    ///         What is already settled, and not re-measured here: <c>NullCheckProbeBenchmarks</c> (2026-09-09) put the
    ///         <c>FlatSrc[] → FlatDst[]</c> gap to Mapperly at ~9 % at 1,000 elements and localised it to the per-element
    ///         null test; the callee's <c>ThrowIfNull</c> costs nothing (its <c>CoreCall</c> arm). The test itself is a
    ///         decided behaviour - a null element maps to null, where Mapperly's would throw (<c>6fa7308</c>,
    ///         <c>Issues/round30/FINDING-array-null-ternary.md</c>) - and stays.
    ///     </para>
    ///     <para>
    ///         The one shape that probe did not have: the same test with the store moved inside it. A new array is
    ///         all-null, so skipping the store for a null element leaves the same array, and every non-null store then
    ///         writes the call's result directly instead of a merged <c>?:</c> value - a value whose exact sealed type
    ///         the JIT may no longer know at the store. One new arm, <c>SkipNullStore</c>, between two controls
    ///         re-measured in the same process: the emitted shape (<c>Current</c>) and the test-free floor
    ///         (<c>Unguarded</c>, Mapperly's shape, NOT DwarfMapper's semantics).
    ///     </para>
    /// </summary>
    [MemoryDiagnoser]
    public class Round33ArrayLoopProbe
    {
        private readonly DwarfM _dwarf = new();
        private FlatSrc[] _items = null!;

        [Params(1000)]
        public int N { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _items = RealisticPayloads.Elements<FlatSrc>(N, 3);
        }

        // Control: the emitted helper body, verbatim, calling the generated element mapper.
        [Benchmark(Baseline = true)]
        public FlatDst[] Current()
        {
            var src = _items;
            var __r = new FlatDst[src.Length];
            for (int __i = 0; __i < src.Length; __i++) { var __item = src[__i]; __r[__i] = (__item is null ? null! : (FlatDst)_dwarf.MapFlat(__item)); }
            return __r;
        }

        // The new arm: the same null test, the store inside it. Same array out, same null-element behaviour.
        [Benchmark]
        public FlatDst[] SkipNullStore()
        {
            var src = _items;
            var __r = new FlatDst[src.Length];
            for (int __i = 0; __i < src.Length; __i++) { var __item = src[__i]; if (__item is not null) __r[__i] = _dwarf.MapFlat(__item); }
            return __r;
        }

        // Control: no test at all - the floor NullCheckProbe called Unguarded. Not an option, a bound.
        [Benchmark]
        public FlatDst[] Unguarded()
        {
            var src = _items;
            var __r = new FlatDst[src.Length];
            for (int __i = 0; __i < src.Length; __i++) { __r[__i] = _dwarf.MapFlat(src[__i]); }
            return __r;
        }
    }
}
