// SPDX-License-Identifier: GPL-2.0-only

using AutoMapper;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using DwarfMapper;
using Mapster;

// `args` MUST be forwarded. Without it BenchmarkRunner silently ignores every command-line switch, so
// `--filter`, `--anyCategories` and `--job` do nothing and the FULL suite runs every time — a targeted
// re-measurement of one category quietly becomes a ~40-minute sweep, and the operator has no signal that
// their filter was dropped. This cost several timed-out runs before it was spotted.
BenchmarkRunner.Run<MapperBenchmarks>(args: args);

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

// Dictionary copy (Dictionary<string,int> → Dictionary<string,int>).
public sealed class DictSrc
{
    public Dictionary<string, int> M { get; set; } = new();
}

public sealed class DictDst
{
    public Dictionary<string, int> M { get; set; } = new();
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
    public partial DictDst MapDict(DictSrc s); // dictionary copy
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
}

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class MapperBenchmarks
{
    private readonly DwarfM _dwarf = new();
    private readonly MapperlyM _mapperly = new();
    private ArraySrc _array = null!;
    private SeqSrc _seq = null!;
    private IMapper _auto = null!;
    private BlitSrc _blit = null!;
    private DictSrc _dict = null!;
    private EnumSrc _enum = null!;

    private FlatSrc _flat = null!;
    private FlOrder _flOrder = null!;
    private ListSrc _list = null!;
    private NestedSrc _nested = null!;
    private WidenSrc _widen = null!;

    [Params(1000)] public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Payloads come from ObjectFactoryV2 — the same fixture/fuzz source the test suites use — so the
        // measured distribution includes nulls, boundary numerics and varied string lengths instead of the
        // uniform literals this setup used to hand-build. Each shape gets a distinct salt so categories are
        // not correlated draws of one another. Setup is not measured by BenchmarkDotNet.
        _flat = RealisticPayloads.One<FlatSrc>(1);

        // The factory assigns through reflection, which does not see nullable annotations — it can null ANY
        // reference member below the root. For the two shapes whose nested reference is declared non-nullable
        // (see the NestedSrc note), materialise it so the benchmark measures nesting rather than dying on an
        // NRE. Their MEMBERS still carry the factory's nulls and boundary values.
        _nested = RealisticPayloads.One<NestedSrc>(2);
        _nested.Inner ??= RealisticPayloads.One<FlatSrc>(21);

        // Element CONTENT is factory-drawn; element COUNT stays pinned to N. The factory builds 1-3 element
        // collections, so letting it size these would quietly turn an N=1000 benchmark into N≈2.
        var items = RealisticPayloads.Elements<FlatSrc>(N, 3);
        _array = new ArraySrc { Items = items };
        _list = new ListSrc { Items = new List<FlatSrc>(items) };
        // Statically IEnumerable<T>, a List at runtime — the probe hits, which is the common real-world case.
        _seq = new SeqSrc { Items = new List<FlatSrc>(items) };

        _blit = new BlitSrc { Items = RealisticPayloads.Elements<Vec3Src>(N, 4) };
        _widen = new WidenSrc { V = RealisticPayloads.Elements<int>(N, 5) };

        _flOrder = RealisticPayloads.One<FlOrder>(6);
        _flOrder.Customer ??= RealisticPayloads.One<FlCustomer>(61);
        _enum = RealisticPayloads.One<EnumSrc>(7);
        _dict = new DictSrc { M = RealisticPayloads.Map(N, 8) };

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
        });
        _auto = cfg.CreateMapper();
    }

    // ── Flat ──────────────────────────────────────────────────────────────────
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Flat")]
    public FlatDst Flat_Hand()
    {
        return new FlatDst { Id = _flat.Id, Name = _flat.Name, Score = _flat.Score, Active = _flat.Active };
    }

    [Benchmark]
    [BenchmarkCategory("Flat")]
    public FlatDst Flat_Dwarf()
    {
        return _dwarf.MapFlat(_flat);
    }

    [Benchmark]
    [BenchmarkCategory("Flat")]
    public FlatDst Flat_Mapperly()
    {
        return _mapperly.MapFlat(_flat);
    }

    [Benchmark]
    [BenchmarkCategory("Flat")]
    public FlatDst Flat_Mapster()
    {
        return _flat.Adapt<FlatDst>();
    }

    [Benchmark]
    [BenchmarkCategory("Flat")]
    public FlatDst Flat_AutoMapper()
    {
        return _auto.Map<FlatDst>(_flat);
    }

    // ── Nested ────────────────────────────────────────────────────────────────
    [Benchmark]
    [BenchmarkCategory("Nested")]
    public NestedDst Nested_Dwarf()
    {
        return _dwarf.MapNested(_nested);
    }

    [Benchmark]
    [BenchmarkCategory("Nested")]
    public NestedDst Nested_Mapperly()
    {
        return _mapperly.MapNested(_nested);
    }

    [Benchmark]
    [BenchmarkCategory("Nested")]
    public NestedDst Nested_Mapster()
    {
        return _nested.Adapt<NestedDst>();
    }

    [Benchmark]
    [BenchmarkCategory("Nested")]
    public NestedDst Nested_AutoMapper()
    {
        return _auto.Map<NestedDst>(_nested);
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
        return _dwarf.MapFlatten(_flOrder);
    }

    [Benchmark]
    [BenchmarkCategory("Flatten")]
    public FlOrderDto Flatten_Mapperly()
    {
        return _mapperly.MapFlatten(_flOrder);
    }

    [Benchmark]
    [BenchmarkCategory("Flatten")]
    public FlOrderDto Flatten_Mapster()
    {
        return _flOrder.Adapt<FlOrderDto>();
    }

    [Benchmark]
    [BenchmarkCategory("Flatten")]
    public FlOrderDto Flatten_AutoMapper()
    {
        return _auto.Map<FlOrderDto>(_flOrder);
    }

    // ── Enum by-name ────────────────────────────────────────────────────────────
    [Benchmark]
    [BenchmarkCategory("Enum")]
    public EnumDst Enum_Dwarf()
    {
        return _dwarf.MapEnum(_enum);
    }

    [Benchmark]
    [BenchmarkCategory("Enum")]
    public EnumDst Enum_Mapperly()
    {
        return _mapperly.MapEnum(_enum);
    }

    [Benchmark]
    [BenchmarkCategory("Enum")]
    public EnumDst Enum_Mapster()
    {
        return _enum.Adapt<EnumDst>();
    }

    [Benchmark]
    [BenchmarkCategory("Enum")]
    public EnumDst Enum_AutoMapper()
    {
        return _auto.Map<EnumDst>(_enum);
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
}
