// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.InteropServices;
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
    ///     <para>
    ///         <b>THE JOB WAS WRONG AND IS FIXED HERE, because the way it was wrong is worth not repeating.</b> This
    ///         class shipped with <c>warmupCount: 3, iterationCount: 5, invocationCount: 16</c>, chosen when the only
    ///         comparison it made was a 0.45x-against-1.00x gap that no amount of noise could hide. Asked a finer
    ///         question later - a span read of a <c>List</c> source against a version-checked walk - that job did not
    ///         merely widen the error bars, it INVERTED the answer: it reported 31,549 ns against 42,780 ns at
    ///         N = 1,024 where MediumRun reports 8,193 against 6,084, with 99.9 % intervals of +/-17,590 and +/-13,365
    ///         against means differing by 11,000. A config that can flip a sign is worse than no config, because a
    ///         reader cannot tell. The job is now MediumRun's shape (10 warmup, 15 iterations, 2 launches, no
    ///         invocation pinning), and ALLOCATION was never affected - it is deterministic and agreed across every
    ///         job, which is why T09's actual claim (the pre-size, 0.83x of the old shape) stood while its time
    ///         column did not.
    ///     </para>
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(RunStrategy.Throughput, warmupCount: 10, iterationCount: 15, launchCount: 2)]
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

        /// <summary>
        ///     The <c>CollectionsMarshal.AsSpan</c> read of a <c>List</c> source that T09 shipped and round 31
        ///     REMOVED, kept here so the removal stays measurable rather than remembered.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         It was removed because <c>map</c> is a generated mapper and can run a user hook or converter that
        ///         mutates the source list: the list then swaps backing arrays while the span keeps reading the old
        ///         one, so the helper returned a silently stale result where the enumerator throws
        ///         <c>InvalidOperationException</c>. Pinned by
        ///         <c>RegistryCollectionShapeTests.A_source_list_mutated_during_the_map_throws_instead_of_returning_a_stale_result</c>.
        ///     </para>
        ///     <para>
        ///         The point of keeping it is that the safety turned out to be FREE, which is not what the first
        ///         measurement said. Under this class's original coarse job the span looked 1.36x faster; under
        ///         MediumRun the version-checked walk is faster at every N (0.83x / 0.74x / 0.94x) and allocates the
        ///         same bytes. <see cref="After_ToList_ListSource" /> is now that walk, so these two arms are the
        ///         before and after of the removal.
        ///     </para>
        /// </remarks>
        [Benchmark]
        public object Removed_ToList_ListSource_Span()
        {
            var list = (List<RegSrc>)_asList;
            var span = CollectionsMarshal.AsSpan(list);
            var r = new List<RegDst>(span.Length);
            for (var i = 0; i < span.Length; i++)
            {
                r.Add(Map(span[i]));
            }

            return r;
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
