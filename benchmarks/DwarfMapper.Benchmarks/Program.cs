// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using AutoMapper;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using DwarfMapper;
using DwarfMapper.Benchmarks;
using Mapster;

// `args` MUST be forwarded. Without it BenchmarkRunner silently ignores every command-line switch, so
// `--filter`, `--anyCategories` and `--job` do nothing and the FULL suite runs every time — a targeted
// re-measurement of one category quietly becomes a ~40-minute sweep, and the operator has no signal that
// their filter was dropped. This cost several timed-out runs before it was spotted.
BenchmarkRunner.Run<MapperBenchmarks>(
    Environment.GetEnvironmentVariable("DWARF_BENCH_SMOKE") == "1" ? SmokeConfig.Create() : null,
    args);

/// <summary>
///     Deep-tier smoke configuration (round-21 T8), activated by <c>DWARF_BENCH_SMOKE=1</c> — an env var
///     rather than a CLI switch so the arg-forwarding contract above stays untouched and a stray extra
///     argument cannot half-apply the mode. <c>scripts/housekeeping.ps1 -BenchSmoke</c> sets it.
///     <para>
///         ShortRun (1 launch, 3 warmup, 3 measured iterations) runs every benchmark end-to-end against
///         current source in single-digit minutes. TIMING numbers from a smoke run are explicitly NON-GATES:
///         three short iterations measure nothing reliable about speed, and throughput is platform-dependent
///         anyway (see benchmarks/results/). The ONLY numbers the smoke gate reads are MemoryDiagnoser's
///         allocated bytes per op — deterministic for a fixed SDK — which the gate compares byte-exactly
///         against allocation-baseline.json. Full measured runs use the default job: run WITHOUT the env var.
///     </para>
/// </summary>
internal static class SmokeConfig
{
    /// <summary>Default config + the ShortRun job + the full JSON exporter the gate parses.</summary>
    public static IConfig Create()
    {
        return DefaultConfig.Instance
            .AddJob(Job.ShortRun.WithId("Smoke"))
            .AddExporter(JsonExporter.Full);
    }
}

// ── Shared benchmark types (auto-properties → every mapper handles them) ───────
// Nullable on BOTH sides. The payload factory draws null for a nullable position ~15% of the time; a
// non-nullable annotation would be a lie reflection assigns straight through, and the mapper would emit no
// null handling — i.e. we would measure the branch-free path while feeding it nullable data.
public sealed class FlatSrc
{
    public int Id { get; set; }

    public string? Name { get; set; } = "";

    public long Score { get; set; }

    public bool Active { get; set; }
}

public sealed class FlatDst
{
    public int Id { get; set; }

    public string? Name { get; set; } = "";

    public long Score { get; set; }

    public bool Active { get; set; }
}

// Inner stays NON-nullable, and the payload builder guarantees it. Making it nullable is what a realistic
// graph would do, but it makes the generated MapNested pass `s.Inner` into the user-declared
// `MapFlat(FlatSrc s)` — CS8604, with no DWARF diagnostic explaining it. That is a mapper-contract question
// (what should a nullable nested reference into a non-nullable partial-method parameter do?) and does not
// belong in a throughput benchmark. Null MEMBERS still flow: FlatSrc.Name inside Inner can be null.
public sealed class NestedSrc
{
    public int Id { get; set; }

    public FlatSrc Inner { get; set; } = new();
}

public sealed class NestedDst
{
    public int Id { get; set; }

    public FlatDst Inner { get; set; } = new();
}

public sealed class ArraySrc
{
    public FlatSrc[] Items { get; set; } = Array.Empty<FlatSrc>();
}

public sealed class ArrayDst
{
    public FlatDst[] Items { get; set; } = Array.Empty<FlatDst>();
}

// ISSUE-019: the source member is typed IEnumerable<T>, so the count is not knowable at compile time. The
// old emission buffered into a growing List<T> and copied it out with ToArray(); the fix probes the RUNTIME
// count and fills one exactly-sized array. The runtime value here is a List, so the probe succeeds. No
// existing benchmark covered this shape — the Array category uses an ARRAY source, whose count is static.
public sealed class SeqSrc
{
    public IEnumerable<FlatSrc> Items { get; set; } = Array.Empty<FlatSrc>();
}

public sealed class SeqDst
{
    public FlatDst[] Items { get; set; } = Array.Empty<FlatDst>();
}

// List<T> target with element conversion → DwarfMapper's plain-fill path (now pre-sized from
// src.Count). Isolates the capacity win: Add() into a pre-sized List never re-grows the backing array.
public sealed class ListSrc
{
    public List<FlatSrc> Items { get; set; } = new();
}

public sealed class ListDst
{
    public List<FlatDst> Items { get; set; } = new();
}

// Layout-identical struct pair → DwarfMapper emits the SIMD reinterpret blit; competitors copy field-by-field.
public struct Vec3Src
{
    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }
}

public struct Vec3Dst
{
    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }
}

public sealed class BlitSrc
{
    public Vec3Src[] Items { get; set; } = Array.Empty<Vec3Src>();
}

public sealed class BlitDst
{
    public Vec3Dst[] Items { get; set; } = Array.Empty<Vec3Dst>();
}

// ── Round 25 T4: the SCALAR TWIN, so the blit can be measured against what it replaced ────────────────
// Same member names and types as Vec3Dst, so it maps cleanly by name — but declared [StructLayout(Auto)],
// which lets the runtime reorder fields and therefore makes the layout unprovable. The blit is refused and
// the element loop is emitted instead: the SAME bytes moved by a different strategy, which is exactly the
// comparison the ratio gate needs. Competitor libraries are not involved — this measures one emission
// against its own alternative, in one process, so shared machine noise cancels.
//
// Renaming the members would have been the obvious way to defeat the proof, and it is wrong: it defeats the
// MAPPING too (DWARF001), so the mapper generates nothing and the "scalar" arm would measure an empty
// method. Auto layout defeats only the fast path.
[StructLayout(LayoutKind.Auto)]
public struct Vec3Ren
{
    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }
}

public sealed class BlitScalarDst
{
    public Vec3Ren[] Items { get; set; } = Array.Empty<Vec3Ren>();
}

public sealed class BlitListDst
{
    public List<Vec3Dst> Items { get; set; } = [];
}

public sealed class BlitListScalarDst
{
    public List<Vec3Ren> Items { get; set; } = [];
}

// Round 26: a VALUE-element List destination with an element conversion (int → long). The existing List
// category uses REFERENCE elements, where allocating the destination objects dominates and the fill strategy
// cannot show through; this is the shape where it can. DwarfMapper fills through
// CollectionsMarshal.SetCount + a span, skipping List.Add's per-element _version++, capacity check and
// _size++; the competitors Add element-by-element.
public sealed class NumListSrc
{
    public int[] V { get; set; } = Array.Empty<int>();
}

public sealed class NumListDst
{
    public List<long> V { get; set; } = [];
}

// Round 26: a REALISTIC nested graph that mixes both fill strategies in one map — an order whose Lines are
// reference elements (Add path) and whose Totals and per-line Quantities are value elements (span fill). The
// flat NumList category shows the fill strategy in isolation; this shows what is left of it once the cost of
// allocating the nested destination objects is in the same measurement.
public sealed class NfLine
{
    public string? Sku { get; set; } = "";

    public int[] Quantities { get; set; } = Array.Empty<int>();
}

public sealed class NfOrder
{
    public int Id { get; set; }

    public List<NfLine> Lines { get; set; } = [];

    public int[] Totals { get; set; } = Array.Empty<int>();
}

public sealed class NfLineDto
{
    public string? Sku { get; set; } = "";

    public List<long> Quantities { get; set; } = [];
}

public sealed class NfOrderDto
{
    public int Id { get; set; }

    public List<NfLineDto> Lines { get; set; } = [];

    public List<long> Totals { get; set; } = [];
}

// Primitive widening array (int[] → long[]) → DwarfMapper emits Vector.Widen; competitors copy element-by-element.
public sealed class WidenSrc
{
    public int[] V { get; set; } = Array.Empty<int>();
}

public sealed class WidenDst
{
    public long[] V { get; set; } = Array.Empty<long>();
}

// ── Feature categories (corpus-derived) — every library supports these ─────────
// Flatten: Order.Customer.Name → OrderDto.CustomerName (real-world Entity→DTO; eShopOnWeb-style).
public sealed class FlCustomer
{
    public string? Name { get; set; } = "";

    public string? Email { get; set; } = "";
}

public sealed class FlOrder
{
    public int Id { get; set; }

    public FlCustomer Customer { get; set; } = new();

    public decimal Amount { get; set; }
}

public sealed class FlOrderDto
{
    public int Id { get; set; }

    public string? CustomerName { get; set; } = "";

    public decimal Amount { get; set; }
}

// Enum by-name (Status → StatusDto), different declaration order to force name (not value) matching.
public enum BenchStatus
{
    Pending,
    Active,
    Closed
}

public enum BenchStatusDto
{
    Closed,
    Pending,
    Active
}

public sealed class EnumSrc
{
    public int Id { get; set; }

    public BenchStatus Status { get; set; }
}

public sealed class EnumDst
{
    public int Id { get; set; }

    public BenchStatusDto Status { get; set; }
}

// Dictionary with a VALUE-TYPE CHANGE (Dictionary<string,int> → Dictionary<string,long>). The value change is
// deliberate: with identical types Mapperly returns the SOURCE dictionary by reference (aliasing), so the
// old same-type row measured aliasing against copying and could not be read as a like-for-like comparison.
// int→long forces EVERY mapper to allocate a new dictionary and convert each value, so the four are finally
// measured doing the same work.
public sealed class DictSrc
{
    public Dictionary<string, int> M { get; set; } = new();
}

public sealed class DictDst
{
    public Dictionary<string, long> M { get; set; } = new();
}

// ── nullable_ref_mismatch: string? source → string target — the commonest real DTO shape, and DWARF070's
// reason for existing. The generator does NOT silently store the null: DWARF070 is a warning that a strict
// project (warnings-as-errors) escalates to a build error, forcing the author to choose a resolution. The
// realistic choice benchmarked here is NullSubstitute, which emits a genuine `s.Name ?? "<sub>"` coalesce on
// the hot path — so this measures the null-CHECK cost, not a branch-free copy. DwarfMapper-only coverage.
// Payload draws null ~15% of the time (ObjectFactoryV2), so the coalesce actually fires.
public sealed class NmSrc
{
    public int Id { get; set; }

    public string? Name { get; set; } = "";
}

public sealed class NmDst
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

// ── Set target (int[] → HashSet<int>): distinct allocation profile — hashing + dedup, not a linear fill.
// DwarfMapper-only coverage (competitors need per-library set config). ObjectFactoryV2 draws boundary ints, so
// duplicates and edge values both occur.
public sealed class SetSrc
{
    public int[] V { get; set; } = Array.Empty<int>();
}

public sealed class SetDst
{
    public HashSet<int> V { get; set; } = new();
}

// ── Immutable target (int[] → ImmutableArray<int>): builder + freeze, again a different allocation shape from
// List/array. DwarfMapper-only coverage.
public sealed class ImmSrc
{
    public int[] V { get; set; } = Array.Empty<int>();
}

public sealed class ImmDst
{
    public ImmutableArray<int> V { get; set; }
}

// ── DwarfMapper (compile-time, reflection-free, AOT-safe) ─────────────────────
[DwarfMapper]
public partial class DwarfM
{
    public partial FlatDst MapFlat(FlatSrc s); // also used for NestedDst.Inner
    public partial NestedDst MapNested(NestedSrc s);
    public partial ArrayDst MapArray(ArraySrc s);
    public partial SeqDst MapSeq(SeqSrc s); // IEnumerable<T> source → unknown count (ISSUE-019)
    public partial ListDst MapList(ListSrc s); // List<T> → List<T> (pre-sized plain fill)
    public partial BlitDst MapBlit(BlitSrc s); // Vec3[] → SIMD blit
    public partial WidenDst MapWiden(WidenSrc s); // int[] → long[] → SIMD widen

    [MapProperty("Customer.Name", nameof(FlOrderDto.CustomerName))]
    public partial FlOrderDto MapFlatten(FlOrder s); // deep source path (explicit; others auto-flatten)

    public partial EnumDst MapEnum(EnumSrc s); // enum by-name
    public partial DictDst MapDict(DictSrc s); // dictionary copy + value widen (int→long)

    [MapProperty(nameof(NmSrc.Name), nameof(NmDst.Name), NullSubstitute = "")]
    public partial NmDst MapNullMismatch(NmSrc s); // string? → string via NullSubstitute (DWARF070 shape)

    public partial NumListDst MapNumList(NumListSrc s); // int[] → List<long> (value-element span fill)
    public partial NfOrderDto MapNestedFill(NfOrder s); // nested graph mixing both fill strategies
    public partial SetDst MapSet(SetSrc s); // int[] → HashSet<int>
    public partial ImmDst MapImmutable(ImmSrc s); // int[] → ImmutableArray<int>

    // Round 25 T4 — the four halves of the two ratio pairs. Each *Blit method takes a reinterpret; each
    // *Scalar method is the same shape with renamed members, so the by-name proof fails and the element
    // loop is emitted. One pair per blit EMITTER: SynthesizeBlit (array→array, which enum arrays also use)
    // and SynthesizeBlitListShape (array→List, which List and ImmutableArray shapes also use).
    public partial BlitListDst MapBlitList(BlitSrc s);

    public partial BlitScalarDst MapBlitScalar(BlitSrc s);
    public partial BlitListScalarDst MapBlitListScalar(BlitSrc s);
}

// ── Mapperly (compile-time source gen) ────────────────────────────────────────
[Riok.Mapperly.Abstractions.Mapper]
public partial class MapperlyM
{
    public partial FlatDst MapFlat(FlatSrc s);
    public partial NestedDst MapNested(NestedSrc s);
    public partial ArrayDst MapArray(ArraySrc s);
    public partial ListDst MapList(ListSrc s);
    public partial BlitDst MapBlit(BlitSrc s);
    public partial WidenDst MapWiden(WidenSrc s);
    public partial FlOrderDto MapFlatten(FlOrder s); // Mapperly auto-flattens Customer.Name → CustomerName
    public partial EnumDst MapEnum(EnumSrc s);
    public partial DictDst MapDict(DictSrc s);
    public partial NumListDst MapNumList(NumListSrc s);
    public partial NfOrderDto MapNestedFill(NfOrder s);
}

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class MapperBenchmarks
{
    private readonly DwarfM _dwarf = new();
    private readonly MapperlyM _mapperly = new();
    private ArraySrc _array = null!;
    private IMapper _auto = null!;
    private BlitSrc _blit = null!;
    private DictSrc _dict = null!;
    private EnumSrc[] _enum = null!;
    private FlOrder[] _flOrder = null!;

    private int _ring;

    private FlatSrc[] _flat = null!;
    private ImmSrc _imm = null!;
    private ListSrc _list = null!;
    private NestedSrc[] _nested = null!;
    private NmSrc[] _nm = null!;
    private SeqSrc _seq = null!;
    private NfOrder _nestedFill = null!;
    private NumListSrc _numList = null!;
    private SetSrc _set = null!;
    private WidenSrc _widen = null!;

    [Params(1000)]
    public int N { get; set; }

    /// <summary>Length of every payload ring. A power of two so the cycling index is a mask.</summary>
    private const int RingSize = 512;

    /// <summary>
    ///     A ring of DISTINCT payloads from the fixture factory — one draw per slot, each with its own salt,
    ///     so consecutive iterations see different data and the branch predictor cannot memorise one object.
    /// </summary>
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

    [GlobalSetup]
    public void Setup()
    {
        // Payloads come from ObjectFactoryV2 — the same fixture/fuzz source the test suites use — so the
        // measured distribution includes nulls, boundary numerics and varied string lengths instead of the
        // uniform literals this setup used to hand-build. Each shape gets a distinct salt so categories are
        // not correlated draws of one another. Setup is not measured by BenchmarkDotNet.
        // ARCHITECTURAL RULE (round 26): no benchmark maps a STATIC payload. A single object mapped
        // millions of times sits permanently in L1 with its branches perfectly predicted — that measures an
        // idealised hot loop, not mapping. It also HID A REAL DEFECT: the enum row read 4.8 ns on one value
        // and 12.5 ns once all three were cycled, while Mapperly stayed flat at 3.4 — a 1.4x gap that was
        // really 3.7x. Rings are drawn from RealisticPayloads, the fuzzer/fixture source the test suites
        // use, one distinct salt per slot.
        _flat = RingOf<FlatSrc>(1);

        // The factory assigns through reflection, which does not see nullable annotations — it can null ANY
        // reference member below the root. For the two shapes whose nested reference is declared non-nullable
        // (see the NestedSrc note), materialise it so the benchmark measures nesting rather than dying on an
        // NRE. Their MEMBERS still carry the factory's nulls and boundary values.
        _nested = RingOf<NestedSrc>(2);
        for (var i = 0; i < RingSize; i++) { _nested[i].Inner ??= RealisticPayloads.One<FlatSrc>(21 + i); }

        // Element CONTENT is factory-drawn; element COUNT stays pinned to N. The factory builds 1-3 element
        // collections, so letting it size these would quietly turn an N=1000 benchmark into N≈2.
        var items = RealisticPayloads.Elements<FlatSrc>(N, 3);
        _array = new ArraySrc
        {
            Items = items
        };
        _list = new ListSrc
        {
            Items = new List<FlatSrc>(items)
        };
        // Statically IEnumerable<T>, a List at runtime — the probe hits, which is the common real-world case.
        _seq = new SeqSrc
        {
            Items = new List<FlatSrc>(items)
        };

        _blit = new BlitSrc
        {
            Items = RealisticPayloads.Elements<Vec3Src>(N, 4)
        };
        _widen = new WidenSrc
        {
            V = RealisticPayloads.Elements<int>(N, 5)
        };

        _flOrder = RingOf<FlOrder>(6);
        for (var i = 0; i < RingSize; i++) { _flOrder[i].Customer ??= RealisticPayloads.One<FlCustomer>(61 + i); }
        _enum = RingOf<EnumSrc>(7);
        // Every declared member represented, so the by-name switch takes all its arms rather than one.
        for (var i = 0; i < RingSize; i++) { _enum[i].Status = (BenchStatus)(i % 3); }
        _dict = new DictSrc
        {
            M = RealisticPayloads.Map(N, 8)
        };
        _nm = RingOf<NmSrc>(9);
        _set = new SetSrc
        {
            V = RealisticPayloads.Elements<int>(N, 10)
        };
        _imm = new ImmSrc
        {
            V = RealisticPayloads.Elements<int>(N, 11)
        };
        // Distinct salt (12) so this draw is not a correlated copy of the Set/Imm draws above.
        _numList = new NumListSrc
        {
            V = RealisticPayloads.Elements<int>(N, 12)
        };
        // A graph rather than a flat draw: 50 lines each owning a short value collection, plus a value
        // collection on the root. Uneven per-line lengths so a shared index would desynchronise.
        _nestedFill = new NfOrder { Id = 1, Totals = RealisticPayloads.Elements<int>(64, 13) };
        for (var i = 0; i < 50; i++)
        {
            _nestedFill.Lines.Add(new NfLine { Sku = "s" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), Quantities = RealisticPayloads.Elements<int>(i % 7, 14 + i) });
        }

        // Fail loudly if the draw came back degenerate. Without this, a change to the factory's probabilities
        // (or an unlucky seed) would silently restore the old flat distribution while every benchmark still
        // reported a healthy-looking number.
        RealisticPayloads.AssertRealistic(items, nameof(FlatSrc));

        var cfg = new MapperConfiguration(c =>
        {
            c.CreateMap<FlatSrc, FlatDst>();
            c.CreateMap<NestedSrc, NestedDst>();
            c.CreateMap<ArraySrc, ArrayDst>();
            c.CreateMap<ListSrc, ListDst>();
            c.CreateMap<Vec3Src, Vec3Dst>();
            c.CreateMap<BlitSrc, BlitDst>();
            c.CreateMap<WidenSrc, WidenDst>();
            c.CreateMap<FlOrder, FlOrderDto>(); // AutoMapper auto-flattens Customer.Name → CustomerName
            c.CreateMap<EnumSrc, EnumDst>();
            c.CreateMap<DictSrc, DictDst>();
            c.CreateMap<NumListSrc, NumListDst>();
            c.CreateMap<NfLine, NfLineDto>();
            c.CreateMap<NfOrder, NfOrderDto>();
        });
        _auto = cfg.CreateMapper();
    }

    // ── Flat ──────────────────────────────────────────────────────────────────
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Flat")]
    public FlatDst Flat_Hand()
    {
        // The hand-written baseline draws from the same ring as every other arm, or it would be measuring
        // a cached object against everyone else's varied one.
        var s = Next(_flat);
        return new FlatDst
        {
            Id = s.Id,
            Name = s.Name,
            Score = s.Score,
            Active = s.Active
        };
    }

    [Benchmark]
    [BenchmarkCategory("Flat")]
    public FlatDst Flat_Dwarf()
    {
        return _dwarf.MapFlat(Next(_flat));
    }

    [Benchmark]
    [BenchmarkCategory("Flat")]
    public FlatDst Flat_Mapperly()
    {
        return _mapperly.MapFlat(Next(_flat));
    }

    [Benchmark]
    [BenchmarkCategory("Flat")]
    public FlatDst Flat_Mapster()
    {
        return Next(_flat).Adapt<FlatDst>();
    }

    [Benchmark]
    [BenchmarkCategory("Flat")]
    public FlatDst Flat_AutoMapper()
    {
        return _auto.Map<FlatDst>(Next(_flat));
    }

    // ── Nested ────────────────────────────────────────────────────────────────
    [Benchmark]
    [BenchmarkCategory("Nested")]
    public NestedDst Nested_Dwarf()
    {
        return _dwarf.MapNested(Next(_nested));
    }

    [Benchmark]
    [BenchmarkCategory("Nested")]
    public NestedDst Nested_Mapperly()
    {
        return _mapperly.MapNested(Next(_nested));
    }

    [Benchmark]
    [BenchmarkCategory("Nested")]
    public NestedDst Nested_Mapster()
    {
        return Next(_nested).Adapt<NestedDst>();
    }

    [Benchmark]
    [BenchmarkCategory("Nested")]
    public NestedDst Nested_AutoMapper()
    {
        return _auto.Map<NestedDst>(Next(_nested));
    }

    // ── Collection (N objects) ──────────────────────────────────────────────────
    [Benchmark]
    [BenchmarkCategory("Array")]
    public ArrayDst Array_Dwarf()
    {
        return _dwarf.MapArray(_array);
    }

    [Benchmark]
    [BenchmarkCategory("Seq")]
    public SeqDst Seq_Dwarf()
    {
        return _dwarf.MapSeq(_seq);
    }

    [Benchmark]
    [BenchmarkCategory("Array")]
    public ArrayDst Array_Mapperly()
    {
        return _mapperly.MapArray(_array);
    }

    [Benchmark]
    [BenchmarkCategory("Array")]
    public ArrayDst Array_Mapster()
    {
        return _array.Adapt<ArrayDst>();
    }

    [Benchmark]
    [BenchmarkCategory("Array")]
    public ArrayDst Array_AutoMapper()
    {
        return _auto.Map<ArrayDst>(_array);
    }

    // ── List<T> with element conversion (pre-sized plain fill vs Add-and-grow) ──
    [Benchmark]
    [BenchmarkCategory("List")]
    public ListDst List_Dwarf()
    {
        return _dwarf.MapList(_list);
    }

    [Benchmark]
    [BenchmarkCategory("List")]
    public ListDst List_Mapperly()
    {
        return _mapperly.MapList(_list);
    }

    [Benchmark]
    [BenchmarkCategory("List")]
    public ListDst List_Mapster()
    {
        return _list.Adapt<ListDst>();
    }

    [Benchmark]
    [BenchmarkCategory("List")]
    public ListDst List_AutoMapper()
    {
        return _auto.Map<ListDst>(_list);
    }

    // ── Round 26: value-element List destination (int[] → List<long>) ──
    // The existing List category uses REFERENCE elements, where allocating the destination objects dominates
    // and the fill strategy cannot show through. Here the elements are values, so what is measured is the
    // fill itself: DwarfMapper writes through a span after SetCount; the others Add element-by-element.
    [Benchmark]
    [BenchmarkCategory("NumList")]
    public NumListDst NumList_Dwarf()
    {
        return _dwarf.MapNumList(_numList);
    }

    [Benchmark]
    [BenchmarkCategory("NumList")]
    public NumListDst NumList_Mapperly()
    {
        return _mapperly.MapNumList(_numList);
    }

    [Benchmark]
    [BenchmarkCategory("NumList")]
    public NumListDst NumList_Mapster()
    {
        return _numList.Adapt<NumListDst>();
    }

    [Benchmark]
    [BenchmarkCategory("NumList")]
    public NumListDst NumList_AutoMapper()
    {
        return _auto.Map<NumListDst>(_numList);
    }

    // ── Round 26: nested graph, both fill strategies in one map ──
    [Benchmark]
    [BenchmarkCategory("NestedFill")]
    public NfOrderDto NestedFill_Dwarf()
    {
        return _dwarf.MapNestedFill(_nestedFill);
    }

    [Benchmark]
    [BenchmarkCategory("NestedFill")]
    public NfOrderDto NestedFill_Mapperly()
    {
        return _mapperly.MapNestedFill(_nestedFill);
    }

    [Benchmark]
    [BenchmarkCategory("NestedFill")]
    public NfOrderDto NestedFill_Mapster()
    {
        return _nestedFill.Adapt<NfOrderDto>();
    }

    [Benchmark]
    [BenchmarkCategory("NestedFill")]
    public NfOrderDto NestedFill_AutoMapper()
    {
        return _auto.Map<NfOrderDto>(_nestedFill);
    }

    // ── Round 25 T4: blit vs its OWN scalar twin, same process, same payload ──
    //
    // The gate reads these four. Ratios, never absolute times: on a shared runner a 2% absolute gate yields
    // roughly 45% false positives, while a same-process ratio cancels the contention both arms feel. Pinned
    // at N=1000, the in-cache regime — NOT at large n, where both arms are bandwidth-bound and the ratio
    // collapses toward 1.0 (measured locally: at n=65536 array→List runs BELOW 1.0).
    [Benchmark]
    [BenchmarkCategory("BlitRatio")]
    public BlitDst BlitRatio_Array_Fast()
    {
        return _dwarf.MapBlit(_blit);
    }

    [Benchmark]
    [BenchmarkCategory("BlitRatio")]
    public BlitScalarDst BlitRatio_Array_Scalar()
    {
        return _dwarf.MapBlitScalar(_blit);
    }

    [Benchmark]
    [BenchmarkCategory("BlitRatio")]
    public BlitListDst BlitRatio_List_Fast()
    {
        return _dwarf.MapBlitList(_blit);
    }

    [Benchmark]
    [BenchmarkCategory("BlitRatio")]
    public BlitListScalarDst BlitRatio_List_Scalar()
    {
        return _dwarf.MapBlitListScalar(_blit);
    }

    // ── Blittable struct array (DwarfMapper's SIMD reinterpret vs element copy) ──
    [Benchmark]
    [BenchmarkCategory("Blit")]
    public BlitDst Blit_Dwarf()
    {
        return _dwarf.MapBlit(_blit);
    }

    [Benchmark]
    [BenchmarkCategory("Blit")]
    public BlitDst Blit_Mapperly()
    {
        return _mapperly.MapBlit(_blit);
    }

    [Benchmark]
    [BenchmarkCategory("Blit")]
    public BlitDst Blit_Mapster()
    {
        return _blit.Adapt<BlitDst>();
    }

    [Benchmark]
    [BenchmarkCategory("Blit")]
    public BlitDst Blit_AutoMapper()
    {
        return _auto.Map<BlitDst>(_blit);
    }

    // ── Primitive widening array (DwarfMapper's Vector.Widen vs element loop) ────
    [Benchmark]
    [BenchmarkCategory("Widen")]
    public WidenDst Widen_Dwarf()
    {
        return _dwarf.MapWiden(_widen);
    }

    [Benchmark]
    [BenchmarkCategory("Widen")]
    public WidenDst Widen_Mapperly()
    {
        return _mapperly.MapWiden(_widen);
    }

    [Benchmark]
    [BenchmarkCategory("Widen")]
    public WidenDst Widen_Mapster()
    {
        return _widen.Adapt<WidenDst>();
    }

    [Benchmark]
    [BenchmarkCategory("Widen")]
    public WidenDst Widen_AutoMapper()
    {
        return _auto.Map<WidenDst>(_widen);
    }

    // ── Flatten (Order.Customer.Name → CustomerName) ────────────────────────────
    [Benchmark]
    [BenchmarkCategory("Flatten")]
    public FlOrderDto Flatten_Dwarf()
    {
        return _dwarf.MapFlatten(Next(_flOrder));
    }

    [Benchmark]
    [BenchmarkCategory("Flatten")]
    public FlOrderDto Flatten_Mapperly()
    {
        return _mapperly.MapFlatten(Next(_flOrder));
    }

    [Benchmark]
    [BenchmarkCategory("Flatten")]
    public FlOrderDto Flatten_Mapster()
    {
        return Next(_flOrder).Adapt<FlOrderDto>();
    }

    [Benchmark]
    [BenchmarkCategory("Flatten")]
    public FlOrderDto Flatten_AutoMapper()
    {
        return _auto.Map<FlOrderDto>(Next(_flOrder));
    }

    // ── Enum by-name ────────────────────────────────────────────────────────────
    [Benchmark]
    [BenchmarkCategory("Enum")]
    public EnumDst Enum_Dwarf()
    {
        return _dwarf.MapEnum(Next(_enum));
    }

    [Benchmark]
    [BenchmarkCategory("Enum")]
    public EnumDst Enum_Mapperly()
    {
        return _mapperly.MapEnum(Next(_enum));
    }

    [Benchmark]
    [BenchmarkCategory("Enum")]
    public EnumDst Enum_Mapster()
    {
        return Next(_enum).Adapt<EnumDst>();
    }

    [Benchmark]
    [BenchmarkCategory("Enum")]
    public EnumDst Enum_AutoMapper()
    {
        return _auto.Map<EnumDst>(Next(_enum));
    }

    // ── Dictionary copy (N entries) ─────────────────────────────────────────────
    [Benchmark]
    [BenchmarkCategory("Dict")]
    public DictDst Dict_Dwarf()
    {
        return _dwarf.MapDict(_dict);
    }

    [Benchmark]
    [BenchmarkCategory("Dict")]
    public DictDst Dict_Mapperly()
    {
        return _mapperly.MapDict(_dict);
    }

    [Benchmark]
    [BenchmarkCategory("Dict")]
    public DictDst Dict_Mapster()
    {
        return _dict.Adapt<DictDst>();
    }

    [Benchmark]
    [BenchmarkCategory("Dict")]
    public DictDst Dict_AutoMapper()
    {
        return _auto.Map<DictDst>(_dict);
    }

    // ── Coverage-only categories (DwarfMapper alone): shapes with distinct allocation profiles that the
    // cross-library categories above do not exercise. No competitor rows — competitors need per-library config
    // for sets/immutables, and the point here is to guard DwarfMapper's own emitted path against regression.
    [Benchmark]
    [BenchmarkCategory("NullMismatch")]
    public NmDst NullMismatch_Dwarf()
    {
        return _dwarf.MapNullMismatch(Next(_nm));
    }

    [Benchmark]
    [BenchmarkCategory("Set")]
    public SetDst Set_Dwarf()
    {
        return _dwarf.MapSet(_set);
    }

    [Benchmark]
    [BenchmarkCategory("Immutable")]
    public ImmDst Immutable_Dwarf()
    {
        return _dwarf.MapImmutable(_imm);
    }
}
