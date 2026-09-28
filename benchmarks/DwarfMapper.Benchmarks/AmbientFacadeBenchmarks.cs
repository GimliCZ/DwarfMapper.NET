// SPDX-License-Identifier: GPL-2.0-only

using AutoMapper;
using BenchmarkDotNet.Attributes;
using Mapster;

namespace DwarfMapper.Benchmarks
{
    /// <summary>
    ///     <b>Round 31 T12: the AMBIENT dispatch path, against every library that has one.</b>
    ///     <para>
    ///         Nothing in <see cref="MapperBenchmarks" /> touched <see cref="DwarfMapperFacade" /> or
    ///         <see cref="DwarfMapperRegistry" />: every DwarfMapper arm there calls the generated mapper class
    ///         directly. That is the right default and the wrong measurement to stop at, because the runtime facade
    ///         is the shape a team arriving from AutoMapper actually writes — one injected <c>IDwarfMapper</c>,
    ///         resolved by type — and it was the one shape nobody had ever timed. This class times it, and it is
    ///         also the honest place to compare against Mapster and AutoMapper, whose ONLY entry point is a runtime
    ///         facade. The direct arms stay in every category so the cost of ambient dispatch is readable as a
    ///         delta rather than asserted.
    ///     </para>
    ///     <para>
    ///         Mapperly has no arm here, and that is a fact about Mapperly rather than an omission: it generates
    ///         instance methods on a partial class and offers no type-keyed runtime dispatch at all. Its number in
    ///         this category would be its direct-call number, which <c>MapperBenchmarks</c> already reports.
    ///     </para>
    ///     <para>
    ///         <b>The A/B is in-process</b>, and it is <see cref="LegacyAmbientFacade" /> rather than a few inline
    ///         statements — which matters, because the first draft of this class WAS inline and the comparison was
    ///         wrong. Calling <see cref="DwarfMapperRegistry.TryGet" /> straight from the benchmark method skips the
    ///         <see cref="IDwarfMapper" /> dispatch that the real call pays, so the "before" arm was measuring a
    ///         strictly cheaper call shape and the change looked worth about a nanosecond. Both arms now enter
    ///         through an interface-typed field holding a facade — one with the pre-T12 bodies verbatim, one with the
    ///         shipped ones — so the only difference left between them is how the pair is resolved. Same payload,
    ///         same process, same ~190-entry registry, no build of the old branch needed. Same convention
    ///         <c>RegistryCollectionBenchmarks</c> uses for T09.
    ///     </para>
    ///     <para>
    ///         Payloads come from the fixture rings, for the round-26 reason: a single static object mapped
    ///         millions of times sits in L1 with its branches perfectly predicted, and in this repository that has
    ///         already hidden a real 3.7x gap. Not allocation-gated, for <c>CollectionSweepBenchmarks</c>' reason —
    ///         the gate keys its exact pins by method name off <see cref="MapperBenchmarks" /> and counts its rows.
    ///     </para>
    /// </summary>
    [MemoryDiagnoser]
    public class AmbientFacadeBenchmarks
    {
        private const int RingSize = 512;

        private readonly DwarfM _dwarf = new();
        private readonly IDwarfMapper _facade = DwarfMapperFacade.Instance;
        // CA1859 asks for the concrete type here, which would delete the thing being measured: the interface
        // dispatch is part of what an ambient call costs, and the shipped arm pays it through
        // DwarfMapperFacade.Instance (declared IDwarfMapper). Narrowing this field would devirtualise one arm of
        // the A/B and not the other.
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance",
            "CA1859:Use concrete types when possible for improved performance",
            Justification = "The interface dispatch is deliberate and is part of the measurement; see above.")]
        private readonly IDwarfMapper _legacy = new LegacyAmbientFacade();

        private IMapper _auto = null!;
        private FlatSrc[] _flat = null!;
        private NestedSrc[] _nested = null!;
        private int _ring;

        // Update-into destinations, allocated in [GlobalSetup] so no arm also measures an allocation. One each,
        // so no two arms interleave writes into the same object.
        private FlatDst _intoDirect = null!;
        private FlatDst _intoBefore = null!;
        private FlatDst _intoSlot = null!;
        private FlatDst _intoAuto = null!;
        private FlatDst _intoMapster = null!;

        [GlobalSetup]
        public void Setup()
        {
            _flat = RingOf<FlatSrc>(41);
            _nested = RingOf<NestedSrc>(42);

            // The factory assigns through reflection and does not see nullable annotations, so it can null a
            // member NestedSrc declares non-nullable. Same materialisation MapperBenchmarks does, same reason.
            for (var i = 0; i < RingSize; i++) { _nested[i].Inner ??= RealisticPayloads.One<FlatSrc>(4200 + i); }

            _intoDirect = new FlatDst();
            _intoBefore = new FlatDst();
            _intoSlot = new FlatDst();
            _intoAuto = new FlatDst();
            _intoMapster = new FlatDst();

            var cfg = new MapperConfiguration(c =>
            {
                c.CreateMap<FlatSrc, FlatDst>();
                c.CreateMap<NestedSrc, NestedDst>();
            });
            _auto = cfg.CreateMapper();
        }

        private static T[] RingOf<T>(int salt)
        {
            var ring = new T[RingSize];
            for (var i = 0; i < RingSize; i++) { ring[i] = RealisticPayloads.One<T>((salt * 100000) + i); }
            return ring;
        }

        /// <summary>Next payload in a ring. Masked rather than modulo; RingSize is a power of two.</summary>
        private T Next<T>(T[] ring)
        {
            return ring[this._ring++ & (RingSize - 1)];
        }

        // ── Flat, single object: the row T12 is about ──────────────────────────────

        /// <summary>The floor: no dispatch of any kind.</summary>
        [Benchmark(Baseline = true)]
        [BenchmarkCategory("FacadeFlat")]
        public FlatDst FacadeFlat_Hand()
        {
            var s = Next(_flat);
            return new FlatDst { Id = s.Id, Name = s.Name, Score = s.Score, Active = s.Active };
        }

        /// <summary>The compile-time path: what ambient dispatch is added on top of.</summary>
        [Benchmark]
        [BenchmarkCategory("FacadeFlat")]
        public FlatDst FacadeFlat_DwarfDirect()
        {
            return _dwarf.MapFlat(Next(_flat));
        }

        /// <summary>The pre-T12 facade, through the same interface: hash two Types, probe the dictionary, invoke.</summary>
        [Benchmark]
        [BenchmarkCategory("FacadeFlat")]
        public FlatDst FacadeFlat_DwarfPair_Before()
        {
            return _legacy.Map<FlatSrc, FlatDst>(Next(_flat));
        }

        /// <summary>T12: the same answer, read off the closed generic type.</summary>
        [Benchmark]
        [BenchmarkCategory("FacadeFlat")]
        public FlatDst FacadeFlat_DwarfPair()
        {
            return _facade.Map<FlatSrc, FlatDst>(Next(_flat));
        }

        /// <summary>
        ///     The one-type overload, UNCHANGED by T12 and measured so that stays visible: it resolves on the source
        ///     instance's runtime type, which no slot may cache, so it still pays a lookup on every call.
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("FacadeFlat")]
        public FlatDst FacadeFlat_DwarfObject()
        {
            return _facade.Map<FlatDst>(Next(_flat));
        }

        [Benchmark]
        [BenchmarkCategory("FacadeFlat")]
        public FlatDst FacadeFlat_Mapster()
        {
            return Next(_flat).Adapt<FlatDst>();
        }

        [Benchmark]
        [BenchmarkCategory("FacadeFlat")]
        public FlatDst FacadeFlat_AutoMapper()
        {
            return _auto.Map<FlatDst>(Next(_flat));
        }

        // ── Nested: the same dispatch over more work per call ──────────────────────
        // The slot saves a CONSTANT, so the interesting number here is what share of the call that constant was: a
        // row whose mapping costs more must show a smaller relative win, and a "win" that does not shrink is
        // measuring something other than the lookup.

        [Benchmark]
        [BenchmarkCategory("FacadeNested")]
        public NestedDst FacadeNested_DwarfDirect()
        {
            return _dwarf.MapNested(Next(_nested));
        }

        [Benchmark]
        [BenchmarkCategory("FacadeNested")]
        public NestedDst FacadeNested_DwarfPair_Before()
        {
            return _legacy.Map<NestedSrc, NestedDst>(Next(_nested));
        }

        [Benchmark]
        [BenchmarkCategory("FacadeNested")]
        public NestedDst FacadeNested_DwarfPair()
        {
            return _facade.Map<NestedSrc, NestedDst>(Next(_nested));
        }

        [Benchmark]
        [BenchmarkCategory("FacadeNested")]
        public NestedDst FacadeNested_DwarfObject()
        {
            return _facade.Map<NestedDst>(Next(_nested));
        }

        [Benchmark]
        [BenchmarkCategory("FacadeNested")]
        public NestedDst FacadeNested_Mapster()
        {
            return Next(_nested).Adapt<NestedDst>();
        }

        [Benchmark]
        [BenchmarkCategory("FacadeNested")]
        public NestedDst FacadeNested_AutoMapper()
        {
            return _auto.Map<NestedDst>(Next(_nested));
        }

        // ── Update-into: the direction a tracked entity or a PATCH endpoint needs ───
        // The shape AutoMapper users reach for, and the one a ~300-map migration found was not a verbatim swap.

        [Benchmark]
        [BenchmarkCategory("FacadeUpdate")]
        public void FacadeUpdate_DwarfDirect()
        {
            _dwarf.UpdateFlat(Next(_flat), _intoDirect);
        }

        /// <summary>The pre-T12 update body, through the same interface: the registry's own probe on every call.</summary>
        [Benchmark]
        [BenchmarkCategory("FacadeUpdate")]
        public void FacadeUpdate_DwarfPair_Before()
        {
            _legacy.Map(Next(_flat), _intoBefore);
        }

        [Benchmark]
        [BenchmarkCategory("FacadeUpdate")]
        public void FacadeUpdate_DwarfPair()
        {
            _facade.Map(Next(_flat), _intoSlot);
        }

        [Benchmark]
        [BenchmarkCategory("FacadeUpdate")]
        public void FacadeUpdate_Mapster()
        {
            Next(_flat).Adapt(_intoMapster);
        }

        [Benchmark]
        [BenchmarkCategory("FacadeUpdate")]
        public void FacadeUpdate_AutoMapper()
        {
            _auto.Map(Next(_flat), _intoAuto);
        }
    }

    /// <summary>
    ///     <see cref="DwarfMapperFacade" /> as it stood BEFORE round-31 T12, kept only so the change can be measured
    ///     against it in one process.
    /// </summary>
    /// <remarks>
    ///     The three bodies are copied verbatim from the shipped facade at commit <c>d177479</c>, comments stripped.
    ///     It implements <see cref="IDwarfMapper" /> rather than exposing static methods because the interface
    ///     dispatch is part of what the real call costs: leaving it out is what made the first version of this
    ///     comparison measure a cheaper call shape and report a smaller win than the change actually delivers.
    /// </remarks>
    internal sealed class LegacyAmbientFacade : IDwarfMapper
    {
        public TDestination Map<TDestination>(object source)
        {
            return (TDestination)DwarfMapperRegistry.Map(source, typeof(TDestination));
        }

        public TDestination Map<TSource, TDestination>(TSource source)
        {
            if (DwarfMapperRegistry.TryGet(typeof(TSource), typeof(TDestination), out var map) && map is not null)
            {
                return (TDestination)map(source!);
            }

            return (TDestination)DwarfMapperRegistry.Map(source!, typeof(TDestination));
        }

        public void Map<TSource, TDestination>(TSource source, TDestination destination)
        {
            DwarfMapperRegistry.Update(source!, destination!, typeof(TSource), typeof(TDestination));
        }
    }
}
