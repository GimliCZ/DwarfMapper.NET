// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using AutoMapper;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using DwarfMapper;
using Mapster;

BenchmarkRunner.Run<MapperBenchmarks>();

// ── Shared benchmark types (auto-properties → every mapper handles them) ───────
public sealed class FlatSrc { public int Id { get; set; } public string Name { get; set; } = ""; public long Score { get; set; } public bool Active { get; set; } }
public sealed class FlatDst { public int Id { get; set; } public string Name { get; set; } = ""; public long Score { get; set; } public bool Active { get; set; } }

public sealed class NestedSrc { public int Id { get; set; } public FlatSrc Inner { get; set; } = new(); }
public sealed class NestedDst { public int Id { get; set; } public FlatDst Inner { get; set; } = new(); }

public sealed class ArraySrc { public FlatSrc[] Items { get; set; } = System.Array.Empty<FlatSrc>(); }
public sealed class ArrayDst { public FlatDst[] Items { get; set; } = System.Array.Empty<FlatDst>(); }

// List<T> target with element conversion → DwarfMapper's plain-fill path (now pre-sized from
// src.Count). Isolates the capacity win: Add() into a pre-sized List never re-grows the backing array.
public sealed class ListSrc { public List<FlatSrc> Items { get; set; } = new(); }
public sealed class ListDst { public List<FlatDst> Items { get; set; } = new(); }

// Layout-identical struct pair → DwarfMapper emits the SIMD reinterpret blit; competitors copy field-by-field.
public struct Vec3Src { public float X { get; set; } public float Y { get; set; } public float Z { get; set; } }
public struct Vec3Dst { public float X { get; set; } public float Y { get; set; } public float Z { get; set; } }
public sealed class BlitSrc { public Vec3Src[] Items { get; set; } = System.Array.Empty<Vec3Src>(); }
public sealed class BlitDst { public Vec3Dst[] Items { get; set; } = System.Array.Empty<Vec3Dst>(); }

// Primitive widening array (int[] → long[]) → DwarfMapper emits Vector.Widen; competitors copy element-by-element.
public sealed class WidenSrc { public int[] V { get; set; } = System.Array.Empty<int>(); }
public sealed class WidenDst { public long[] V { get; set; } = System.Array.Empty<long>(); }

// ── Feature categories (corpus-derived) — every library supports these ─────────
// Flatten: Order.Customer.Name → OrderDto.CustomerName (real-world Entity→DTO; eShopOnWeb-style).
public sealed class FlCustomer { public string Name { get; set; } = ""; public string Email { get; set; } = ""; }
public sealed class FlOrder { public int Id { get; set; } public FlCustomer Customer { get; set; } = new(); public decimal Amount { get; set; } }
public sealed class FlOrderDto { public int Id { get; set; } public string CustomerName { get; set; } = ""; public decimal Amount { get; set; } }

// Enum by-name (Status → StatusDto), different declaration order to force name (not value) matching.
public enum BenchStatus { Pending, Active, Closed }
public enum BenchStatusDto { Closed, Pending, Active }
public sealed class EnumSrc { public int Id { get; set; } public BenchStatus Status { get; set; } }
public sealed class EnumDst { public int Id { get; set; } public BenchStatusDto Status { get; set; } }

// Dictionary copy (Dictionary<string,int> → Dictionary<string,int>).
public sealed class DictSrc { public Dictionary<string, int> M { get; set; } = new(); }
public sealed class DictDst { public Dictionary<string, int> M { get; set; } = new(); }

// ── DwarfMapper (compile-time, reflection-free, AOT-safe) ─────────────────────
[DwarfMapper]
public partial class DwarfM
{
    public partial FlatDst MapFlat(FlatSrc s);       // also used for NestedDst.Inner
    public partial NestedDst MapNested(NestedSrc s);
    public partial ArrayDst MapArray(ArraySrc s);
    public partial ListDst MapList(ListSrc s);       // List<T> → List<T> (pre-sized plain fill)
    public partial BlitDst MapBlit(BlitSrc s);       // Vec3[] → SIMD blit
    public partial WidenDst MapWiden(WidenSrc s);    // int[] → long[] → SIMD widen
    [MapProperty("Customer.Name", nameof(FlOrderDto.CustomerName))]
    public partial FlOrderDto MapFlatten(FlOrder s); // deep source path (explicit; others auto-flatten)
    public partial EnumDst MapEnum(EnumSrc s);       // enum by-name
    public partial DictDst MapDict(DictSrc s);       // dictionary copy
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
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class MapperBenchmarks
{
    private readonly DwarfM _dwarf = new();
    private readonly MapperlyM _mapperly = new();
    private IMapper _auto = null!;

    private FlatSrc _flat = null!;
    private NestedSrc _nested = null!;
    private ArraySrc _array = null!;
    private ListSrc _list = null!;
    private BlitSrc _blit = null!;
    private WidenSrc _widen = null!;
    private FlOrder _flOrder = null!;
    private EnumSrc _enum = null!;
    private DictSrc _dict = null!;

    [Params(1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _flat = new FlatSrc { Id = 7, Name = "vein", Score = 42, Active = true };
        _nested = new NestedSrc { Id = 1, Inner = _flat };

        var items = new FlatSrc[N];
        for (var i = 0; i < N; i++) items[i] = new FlatSrc { Id = i, Name = "n" + i, Score = i, Active = i % 2 == 0 };
        _array = new ArraySrc { Items = items };
        _list = new ListSrc { Items = new List<FlatSrc>(items) };

        var vecs = new Vec3Src[N];
        for (var i = 0; i < N; i++) vecs[i] = new Vec3Src { X = i, Y = i + 1, Z = i + 2 };
        _blit = new BlitSrc { Items = vecs };

        var ints = new int[N];
        for (var i = 0; i < N; i++) ints[i] = i - N / 2;
        _widen = new WidenSrc { V = ints };

        _flOrder = new FlOrder { Id = 9, Customer = new FlCustomer { Name = "Ada Lovelace", Email = "ada@x.io" }, Amount = 42.50m };
        _enum = new EnumSrc { Id = 1, Status = BenchStatus.Active };
        var dict = new Dictionary<string, int>();
        for (var i = 0; i < N; i++) dict["k" + i] = i;
        _dict = new DictSrc { M = dict };

        var cfg = new MapperConfiguration(c =>
        {
            c.CreateMap<FlatSrc, FlatDst>();
            c.CreateMap<NestedSrc, NestedDst>();
            c.CreateMap<ArraySrc, ArrayDst>();
            c.CreateMap<ListSrc, ListDst>();
            c.CreateMap<Vec3Src, Vec3Dst>();
            c.CreateMap<BlitSrc, BlitDst>();
            c.CreateMap<WidenSrc, WidenDst>();
            c.CreateMap<FlOrder, FlOrderDto>();   // AutoMapper auto-flattens Customer.Name → CustomerName
            c.CreateMap<EnumSrc, EnumDst>();
            c.CreateMap<DictSrc, DictDst>();
        });
        _auto = cfg.CreateMapper();
    }

    // ── Flat ──────────────────────────────────────────────────────────────────
    [Benchmark(Baseline = true), BenchmarkCategory("Flat")] public FlatDst Flat_Hand() => new() { Id = _flat.Id, Name = _flat.Name, Score = _flat.Score, Active = _flat.Active };
    [Benchmark, BenchmarkCategory("Flat")] public FlatDst Flat_Dwarf() => _dwarf.MapFlat(_flat);
    [Benchmark, BenchmarkCategory("Flat")] public FlatDst Flat_Mapperly() => _mapperly.MapFlat(_flat);
    [Benchmark, BenchmarkCategory("Flat")] public FlatDst Flat_Mapster() => _flat.Adapt<FlatDst>();
    [Benchmark, BenchmarkCategory("Flat")] public FlatDst Flat_AutoMapper() => _auto.Map<FlatDst>(_flat);

    // ── Nested ────────────────────────────────────────────────────────────────
    [Benchmark, BenchmarkCategory("Nested")] public NestedDst Nested_Dwarf() => _dwarf.MapNested(_nested);
    [Benchmark, BenchmarkCategory("Nested")] public NestedDst Nested_Mapperly() => _mapperly.MapNested(_nested);
    [Benchmark, BenchmarkCategory("Nested")] public NestedDst Nested_Mapster() => _nested.Adapt<NestedDst>();
    [Benchmark, BenchmarkCategory("Nested")] public NestedDst Nested_AutoMapper() => _auto.Map<NestedDst>(_nested);

    // ── Collection (N objects) ──────────────────────────────────────────────────
    [Benchmark, BenchmarkCategory("Array")] public ArrayDst Array_Dwarf() => _dwarf.MapArray(_array);
    [Benchmark, BenchmarkCategory("Array")] public ArrayDst Array_Mapperly() => _mapperly.MapArray(_array);
    [Benchmark, BenchmarkCategory("Array")] public ArrayDst Array_Mapster() => _array.Adapt<ArrayDst>();
    [Benchmark, BenchmarkCategory("Array")] public ArrayDst Array_AutoMapper() => _auto.Map<ArrayDst>(_array);

    // ── List<T> with element conversion (pre-sized plain fill vs Add-and-grow) ──
    [Benchmark, BenchmarkCategory("List")] public ListDst List_Dwarf() => _dwarf.MapList(_list);
    [Benchmark, BenchmarkCategory("List")] public ListDst List_Mapperly() => _mapperly.MapList(_list);
    [Benchmark, BenchmarkCategory("List")] public ListDst List_Mapster() => _list.Adapt<ListDst>();
    [Benchmark, BenchmarkCategory("List")] public ListDst List_AutoMapper() => _auto.Map<ListDst>(_list);

    // ── Blittable struct array (DwarfMapper's SIMD reinterpret vs element copy) ──
    [Benchmark, BenchmarkCategory("Blit")] public BlitDst Blit_Dwarf() => _dwarf.MapBlit(_blit);
    [Benchmark, BenchmarkCategory("Blit")] public BlitDst Blit_Mapperly() => _mapperly.MapBlit(_blit);
    [Benchmark, BenchmarkCategory("Blit")] public BlitDst Blit_Mapster() => _blit.Adapt<BlitDst>();
    [Benchmark, BenchmarkCategory("Blit")] public BlitDst Blit_AutoMapper() => _auto.Map<BlitDst>(_blit);

    // ── Primitive widening array (DwarfMapper's Vector.Widen vs element loop) ────
    [Benchmark, BenchmarkCategory("Widen")] public WidenDst Widen_Dwarf() => _dwarf.MapWiden(_widen);
    [Benchmark, BenchmarkCategory("Widen")] public WidenDst Widen_Mapperly() => _mapperly.MapWiden(_widen);
    [Benchmark, BenchmarkCategory("Widen")] public WidenDst Widen_Mapster() => _widen.Adapt<WidenDst>();
    [Benchmark, BenchmarkCategory("Widen")] public WidenDst Widen_AutoMapper() => _auto.Map<WidenDst>(_widen);

    // ── Flatten (Order.Customer.Name → CustomerName) ────────────────────────────
    [Benchmark, BenchmarkCategory("Flatten")] public FlOrderDto Flatten_Dwarf() => _dwarf.MapFlatten(_flOrder);
    [Benchmark, BenchmarkCategory("Flatten")] public FlOrderDto Flatten_Mapperly() => _mapperly.MapFlatten(_flOrder);
    [Benchmark, BenchmarkCategory("Flatten")] public FlOrderDto Flatten_Mapster() => _flOrder.Adapt<FlOrderDto>();
    [Benchmark, BenchmarkCategory("Flatten")] public FlOrderDto Flatten_AutoMapper() => _auto.Map<FlOrderDto>(_flOrder);

    // ── Enum by-name ────────────────────────────────────────────────────────────
    [Benchmark, BenchmarkCategory("Enum")] public EnumDst Enum_Dwarf() => _dwarf.MapEnum(_enum);
    [Benchmark, BenchmarkCategory("Enum")] public EnumDst Enum_Mapperly() => _mapperly.MapEnum(_enum);
    [Benchmark, BenchmarkCategory("Enum")] public EnumDst Enum_Mapster() => _enum.Adapt<EnumDst>();
    [Benchmark, BenchmarkCategory("Enum")] public EnumDst Enum_AutoMapper() => _auto.Map<EnumDst>(_enum);

    // ── Dictionary copy (N entries) ─────────────────────────────────────────────
    [Benchmark, BenchmarkCategory("Dict")] public DictDst Dict_Dwarf() => _dwarf.MapDict(_dict);
    [Benchmark, BenchmarkCategory("Dict")] public DictDst Dict_Mapperly() => _mapperly.MapDict(_dict);
    [Benchmark, BenchmarkCategory("Dict")] public DictDst Dict_Mapster() => _dict.Adapt<DictDst>();
    [Benchmark, BenchmarkCategory("Dict")] public DictDst Dict_AutoMapper() => _auto.Map<DictDst>(_dict);
}
