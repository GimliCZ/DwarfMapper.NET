// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;

namespace DwarfMapper.Benchmarks
{
    /// <summary>
    ///     <b>Round 31 T19 / the T14 go-no-go measurement: does the ambient COLLECTION path slow down as an application
    ///     adds maps?</b>
    ///     <para>
    ///         Research P4 modelled it and said yes: every mapped pair registers six collection shapes keyed on
    ///         <c>IEnumerable&lt;S&gt;</c>, a collection handed to <c>Map&lt;TDestination&gt;(object)</c> never hits an
    ///         exact key, and the lookup then scans every interface entry — 1.9 µs at 10 pairs to 15.4 µs at 500, in a
    ///         single-core container, on a MODEL of the registry loop. That model is the whole motivation for T14's
    ///         per-destination dispatchers, so it is measured here, in-repo, on the shipped registry, before anything
    ///         is built on it.
    ///     </para>
    ///     <para>
    ///         <see cref="Pairs" /> synthetic pairs are registered on top of this assembly's own (~190 entries), six
    ///         interface-keyed shapes each, exactly the load a real mapper puts on the list; their types are distinct
    ///         closed generics, so no entry can accidentally match the measured source. BenchmarkDotNet runs each
    ///         parameter in its own process, so the registry the arms see holds exactly that many.
    ///     </para>
    /// </summary>
    [MemoryDiagnoser]
    public class AmbientScanBenchmarks
    {
        private readonly IDwarfMapper _facade = DwarfMapperFacade.Instance;
        private readonly DwarfM _dwarf = new();
        private List<FlatSrc> _list = null!;
        private FlatSrc _one = null!;

        [Params(0, 100, 500, 1000)]
        public int Pairs { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _list = Enumerable.Range(0, 8).Select(i => RealisticPayloads.One<FlatSrc>(3100 + i)).ToList();
            _one = RealisticPayloads.One<FlatSrc>(3099);

            // Fifteen distinct BCL types; closed ValueTuples over them give up to 3,375 distinct sources that cannot
            // match the measured List<FlatSrc>.
            var markers = new[]
            {
                typeof(int), typeof(long), typeof(short), typeof(byte), typeof(sbyte), typeof(uint), typeof(ulong),
                typeof(ushort), typeof(float), typeof(double), typeof(decimal), typeof(char), typeof(bool),
                typeof(string), typeof(DateTime),
            };
            Func<object, object> never = _ => throw new InvalidOperationException("synthetic entry was dispatched");
            var shapes = new[] { typeof(List<>), typeof(HashSet<>), typeof(Queue<>), typeof(Stack<>), typeof(LinkedList<>), typeof(SortedSet<>) };
            var entries = (from a in markers from b in markers from c in markers select (a, b, c))
                .Take(Pairs)
                .SelectMany(t =>
                {
                    var src = typeof(ValueTuple<,,>).MakeGenericType(t.a, t.b, t.c);
                    return shapes.Select(shape => (typeof(IEnumerable<>).MakeGenericType(src), shape.MakeGenericType(src), never));
                })
                .ToArray();
            DwarfMapperRegistry.RegisterMany(entries);
        }

        /// <summary>The floor: the generated element mapper in a pre-sized loop — what the registered shape runs.</summary>
        [Benchmark(Baseline = true)]
        [BenchmarkCategory("Collection")]
        public List<FlatDst> Collection_Direct()
        {
            var result = new List<FlatDst>(_list.Count);
            foreach (var item in _list) result.Add(_dwarf.MapFlat(item));
            return result;
        }

        /// <summary>The path P4 says grows with the application: exact miss, base walk, interface scan.</summary>
        [Benchmark]
        [BenchmarkCategory("Collection")]
        public List<FlatDst> Collection_AmbientByRuntimeType()
        {
            return _facade.Map<List<FlatDst>>(_list);
        }

        /// <summary>Control: a single object resolves on the exact key and should NOT move with <see cref="Pairs" />.</summary>
        [Benchmark]
        [BenchmarkCategory("Single")]
        public FlatDst Single_AmbientByRuntimeType()
        {
            return _facade.Map<FlatDst>(_one);
        }
    }
}
