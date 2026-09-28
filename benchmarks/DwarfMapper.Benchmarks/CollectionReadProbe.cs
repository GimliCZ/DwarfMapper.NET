// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace DwarfMapper.Benchmarks
{
    /// <summary>
    ///     <b>Round 31: two probes raised by the round-30/31 comparison run, before either becomes an emission
    ///     change.</b>
    ///     <para>
    ///         <b>Probe 1 — the source read for reference elements.</b> The emitter's list fill takes a
    ///         <c>SetCount</c> + span WRITE only when the element is a value type, because round 26 measured the
    ///         write side for reference elements at 1.00x and then 0.92x
    ///         (<c>Issues/round26/FINDING-list-fill-strategy.md</c>) — allocating the destination objects dominates.
    ///         What that study did not measure is the READ side: <c>foreach</c> over a <c>List&lt;T&gt;</c> pays a
    ///         version check per <c>MoveNext</c>, and an indexed span read does not. Round 31 T09 took the span read
    ///         for the registry's helper, so the question is whether the emitted walk should follow. The mechanism
    ///         predicts no; this measures it instead of arguing about it.
    ///     </para>
    ///     <para>
    ///         <b>Probe 2 — what the emitted null guards cost.</b> On <c>Array</c> the generated walk runs 1.235x /
    ///         1.246x slower than Mapperly's across two independent runs, and the loops are otherwise the same
    ///         shape. Two guards per element differ: the emitted element expression tests
    ///         <c>__item is null ? null! : Map(__item)</c>, and the generated element map opens with
    ///         <c>ArgumentNullException.ThrowIfNull</c>. Mapperly emits neither. The four arms attribute the gap to
    ///         one, the other, or both — so the price of the resilience-first default is a NUMBER rather than an
    ///         inference, and a decision about it can be made on evidence.
    ///     </para>
    ///     <para>
    ///         Neither probe is a proposal. A probe that measures ~1.0x is a reason not to touch the emitter, which
    ///         is worth as much as one that finds a win and costs a 1,014-case golden regeneration less. Not
    ///         allocation-gated, for <c>CollectionSweepBenchmarks</c>' reason.
    ///     </para>
    /// </summary>
    [MemoryDiagnoser]
    public class CollectionReadProbeBenchmarks
    {
        private FlatSrc[] _array = null!;
        private List<FlatSrc> _list = null!;

        [Params(1000)]
        public int N { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _array = RealisticPayloads.Elements<FlatSrc>(N, 3);
            _list = new List<FlatSrc>(_array);
        }

        // ── probe 1: how the SOURCE list is read, reference elements ───────────────
        // Both arms pre-size the destination and use Add, exactly as the emitter does for a reference element, so
        // the only difference between them is the read.

        /// <summary>What the emitter writes today: a struct enumerator over the source list.</summary>
        [Benchmark(Baseline = true)]
        [BenchmarkCategory("ListRead")]
        public List<FlatDst> ListRead_Foreach()
        {
            var r = new List<FlatDst>(_list.Count);
            foreach (var item in _list)
            {
                r.Add(item is null ? null! : MapGuarded(item));
            }

            return r;
        }

        /// <summary>The indexed span read T09 took for the registry helper.</summary>
        [Benchmark]
        [BenchmarkCategory("ListRead")]
        public List<FlatDst> ListRead_Span()
        {
            var span = CollectionsMarshal.AsSpan(_list);
            var r = new List<FlatDst>(span.Length);
            for (var i = 0; i < span.Length; i++)
            {
                var item = span[i];
                r.Add(item is null ? null! : MapGuarded(item));
            }

            return r;
        }

        // ── probe 2: attributing the Array gap against Mapperly ────────────────────
        // All four arms are the same indexed array loop over the same payload into the same destination shape. Only
        // the guards differ.

        /// <summary>The emitted shape, verbatim: the element ternary AND ThrowIfNull inside the element map.</summary>
        [Benchmark]
        [BenchmarkCategory("ArrayGuards")]
        public FlatDst[] ArrayGuards_Both()
        {
            var src = _array;
            var r = new FlatDst[src.Length];
            for (var i = 0; i < src.Length; i++)
            {
                var item = src[i];
                r[i] = item is null ? null! : MapGuarded(item);
            }

            return r;
        }

        /// <summary>The element ternary only.</summary>
        [Benchmark]
        [BenchmarkCategory("ArrayGuards")]
        public FlatDst[] ArrayGuards_TernaryOnly()
        {
            var src = _array;
            var r = new FlatDst[src.Length];
            for (var i = 0; i < src.Length; i++)
            {
                var item = src[i];
                r[i] = item is null ? null! : MapBare(item);
            }

            return r;
        }

        /// <summary>The element map's own guard only.</summary>
        [Benchmark]
        [BenchmarkCategory("ArrayGuards")]
        public FlatDst[] ArrayGuards_ThrowIfNullOnly()
        {
            var src = _array;
            var r = new FlatDst[src.Length];
            for (var i = 0; i < src.Length; i++)
            {
                r[i] = MapGuarded(src[i]);
            }

            return r;
        }

        /// <summary>Mapperly's shape: neither guard. The floor this comparison is against.</summary>
        [Benchmark]
        [BenchmarkCategory("ArrayGuards")]
        public FlatDst[] ArrayGuards_None()
        {
            var src = _array;
            var r = new FlatDst[src.Length];
            for (var i = 0; i < src.Length; i++)
            {
                r[i] = MapBare(src[i]);
            }

            return r;
        }

        /// <summary>The generated element map, guard included.</summary>
        private static FlatDst MapGuarded(FlatSrc s)
        {
            ArgumentNullException.ThrowIfNull(s);
            return new FlatDst { Active = s.Active, Id = s.Id, Name = s.Name, Score = s.Score };
        }

        /// <summary>The same map with no guard — what Mapperly emits.</summary>
        private static FlatDst MapBare(FlatSrc s)
        {
            return new FlatDst { Active = s.Active, Id = s.Id, Name = s.Name, Score = s.Score };
        }
    }
}
