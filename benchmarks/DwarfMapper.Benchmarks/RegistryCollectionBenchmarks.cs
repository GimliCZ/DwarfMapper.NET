// SPDX-License-Identifier: GPL-2.0-only

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace DwarfMapper.Benchmarks
{
    /// <summary>
    ///     <b>Round 31 T09: what the registry's auto-registered collection shapes cost, before and after.</b>
    ///     <para>
    ///         Every mapped pair registers six collection shapes keyed on <c>IEnumerable&lt;S&gt;</c>, so a source
    ///         reaching one arrives as an INTERFACE. The emitted lambda used to walk it with a boxed enumerator into
    ///         an un-sized <c>List</c> and then copy it; T09 moved the walk into
    ///         <see cref="DwarfCollectionMap" />, which takes an indexable fast path for an array or a
    ///         <c>List</c> and pre-sizes from <c>TryGetNonEnumeratedCount</c> otherwise.
    ///     </para>
    ///     <para>
    ///         BOTH SHAPES RUN IN THIS PROCESS, against the same payload, so the comparison needs no build of the
    ///         old generator: <see cref="Before_Emitted_Lambda" /> is the exact code the emitter used to write.
    ///         That is what makes this an A/B rather than a reading taken against a remembered number.
    ///     </para>
    ///     <para>
    ///         Not pinned and not gated, for <c>CollectionSweepBenchmarks</c>' reason: the allocation gate keys its
    ///         exact pins by method name off <c>MapperBenchmarks</c>, and allocation here is N-proportional by
    ///         construction, so a byte pin would be a pin on N.
    ///     </para>
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(RunStrategy.Throughput, warmupCount: 3, iterationCount: 5, invocationCount: 16)]
    public class RegistryCollectionBenchmarks
    {
        private object _asList = null!;
        private object _asArray = null!;

        [Params(16, 1024, 65536)]
        public int N { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            var items = new List<RegSrc>(N);
            for (var i = 0; i < N; i++)
            {
                items.Add(new RegSrc { V = i, Name = "n" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            }

            _asList = items;
            _asArray = items.ToArray();
        }

        /// <summary>The pre-T09 emitted lambda, verbatim: un-sized list, boxed interface enumerator, then a copy.</summary>
        [Benchmark(Baseline = true)]
        public object Before_Emitted_Lambda()
        {
            var r = new List<RegDst>();
            foreach (var e in (IEnumerable<RegSrc>)_asList)
            {
                r.Add(Map(e));
            }

            return r;
        }

        [Benchmark]
        public object After_ToList_ListSource()
        {
            return DwarfCollectionMap.ToList<RegSrc, RegDst>(_asList, Map);
        }

        [Benchmark]
        public object After_ToList_ArraySource()
        {
            return DwarfCollectionMap.ToList<RegSrc, RegDst>(_asArray, Map);
        }

        [Benchmark]
        public object After_ToArray_ListSource()
        {
            return DwarfCollectionMap.ToArray<RegSrc, RegDst>(_asList, Map);
        }

        /// <summary>The pre-T09 array shape: build an un-sized list, then copy it into an array.</summary>
        [Benchmark]
        public object Before_Emitted_Lambda_ToArray()
        {
            var r = new List<RegDst>();
            foreach (var e in (IEnumerable<RegSrc>)_asList)
            {
                r.Add(Map(e));
            }

            return r.ToArray();
        }

        private static RegDst Map(RegSrc s)
        {
            return new RegDst { V = s.V, Name = s.Name };
        }

    }

    /// <summary>Payload for <see cref="RegistryCollectionBenchmarks" />; top-level because CA1034 refuses a
    /// publicly nested type and BenchmarkDotNet needs these reachable.</summary>
    public sealed class RegSrc
    {
        public int V { get; set; }

        public string Name { get; set; } = "";
    }

    /// <summary>Destination twin of <see cref="RegSrc" />.</summary>
    public sealed class RegDst
    {
        public int V { get; set; }

        public string Name { get; set; } = "";
    }
}
