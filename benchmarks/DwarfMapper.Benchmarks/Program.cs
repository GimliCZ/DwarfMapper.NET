// SPDX-License-Identifier: GPL-2.0-only
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

// Layout-identical struct pair → DwarfMapper emits the SIMD reinterpret blit; competitors copy field-by-field.
public struct Vec3Src { public float X { get; set; } public float Y { get; set; } public float Z { get; set; } }
public struct Vec3Dst { public float X { get; set; } public float Y { get; set; } public float Z { get; set; } }
public sealed class BlitSrc { public Vec3Src[] Items { get; set; } = System.Array.Empty<Vec3Src>(); }
public sealed class BlitDst { public Vec3Dst[] Items { get; set; } = System.Array.Empty<Vec3Dst>(); }

// Primitive widening array (int[] → long[]) → DwarfMapper emits Vector.Widen; competitors copy element-by-element.
public sealed class WidenSrc { public int[] V { get; set; } = System.Array.Empty<int>(); }
public sealed class WidenDst { public long[] V { get; set; } = System.Array.Empty<long>(); }

// ── DwarfMapper (compile-time, reflection-free, AOT-safe) ─────────────────────
[DwarfMapper]
public partial class DwarfM
{
    public partial FlatDst MapFlat(FlatSrc s);       // also used for NestedDst.Inner
    public partial NestedDst MapNested(NestedSrc s);
    public partial ArrayDst MapArray(ArraySrc s);
    public partial BlitDst MapBlit(BlitSrc s);       // Vec3[] → SIMD blit
    public partial WidenDst MapWiden(WidenSrc s);    // int[] → long[] → SIMD widen
}

// ── Mapperly (compile-time source gen) ────────────────────────────────────────
[Riok.Mapperly.Abstractions.Mapper]
public partial class MapperlyM
{
    public partial FlatDst MapFlat(FlatSrc s);
    public partial NestedDst MapNested(NestedSrc s);
    public partial ArrayDst MapArray(ArraySrc s);
    public partial BlitDst MapBlit(BlitSrc s);
    public partial WidenDst MapWiden(WidenSrc s);
}

// ── Benchmarks: DwarfMapper vs hand-written vs Mapperly vs Mapster vs AutoMapper 14 vs AgileMapper ─
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class MapperBenchmarks
{
    private readonly DwarfM _dwarf = new();
    private readonly MapperlyM _mapperly = new();
    private IMapper _auto = null!;
    private readonly AgileObjects.AgileMapper.IMapper _agile = AgileObjects.AgileMapper.Mapper.CreateNew();

    private FlatSrc _flat = null!;
    private NestedSrc _nested = null!;
    private ArraySrc _array = null!;
    private BlitSrc _blit = null!;
    private WidenSrc _widen = null!;

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

        var vecs = new Vec3Src[N];
        for (var i = 0; i < N; i++) vecs[i] = new Vec3Src { X = i, Y = i + 1, Z = i + 2 };
        _blit = new BlitSrc { Items = vecs };

        var ints = new int[N];
        for (var i = 0; i < N; i++) ints[i] = i - N / 2;
        _widen = new WidenSrc { V = ints };

        var cfg = new MapperConfiguration(c =>
        {
            c.CreateMap<FlatSrc, FlatDst>();
            c.CreateMap<NestedSrc, NestedDst>();
            c.CreateMap<ArraySrc, ArrayDst>();
            c.CreateMap<Vec3Src, Vec3Dst>();
            c.CreateMap<BlitSrc, BlitDst>();
            c.CreateMap<WidenSrc, WidenDst>();
        });
        _auto = cfg.CreateMapper();
        // Mapster + AgileMapper need no setup (zero-config).
    }

    // ── Flat ──────────────────────────────────────────────────────────────────
    [Benchmark(Baseline = true), BenchmarkCategory("Flat")] public FlatDst Flat_Hand() => new() { Id = _flat.Id, Name = _flat.Name, Score = _flat.Score, Active = _flat.Active };
    [Benchmark, BenchmarkCategory("Flat")] public FlatDst Flat_Dwarf() => _dwarf.MapFlat(_flat);
    [Benchmark, BenchmarkCategory("Flat")] public FlatDst Flat_Mapperly() => _mapperly.MapFlat(_flat);
    [Benchmark, BenchmarkCategory("Flat")] public FlatDst Flat_Mapster() => _flat.Adapt<FlatDst>();
    [Benchmark, BenchmarkCategory("Flat")] public FlatDst Flat_AutoMapper() => _auto.Map<FlatDst>(_flat);
    [Benchmark, BenchmarkCategory("Flat")] public FlatDst Flat_AgileMapper() => _agile.Map(_flat).ToANew<FlatDst>();

    // ── Nested ────────────────────────────────────────────────────────────────
    [Benchmark, BenchmarkCategory("Nested")] public NestedDst Nested_Dwarf() => _dwarf.MapNested(_nested);
    [Benchmark, BenchmarkCategory("Nested")] public NestedDst Nested_Mapperly() => _mapperly.MapNested(_nested);
    [Benchmark, BenchmarkCategory("Nested")] public NestedDst Nested_Mapster() => _nested.Adapt<NestedDst>();
    [Benchmark, BenchmarkCategory("Nested")] public NestedDst Nested_AutoMapper() => _auto.Map<NestedDst>(_nested);
    [Benchmark, BenchmarkCategory("Nested")] public NestedDst Nested_AgileMapper() => _agile.Map(_nested).ToANew<NestedDst>();

    // ── Collection (N objects) ──────────────────────────────────────────────────
    [Benchmark, BenchmarkCategory("Array")] public ArrayDst Array_Dwarf() => _dwarf.MapArray(_array);
    [Benchmark, BenchmarkCategory("Array")] public ArrayDst Array_Mapperly() => _mapperly.MapArray(_array);
    [Benchmark, BenchmarkCategory("Array")] public ArrayDst Array_Mapster() => _array.Adapt<ArrayDst>();
    [Benchmark, BenchmarkCategory("Array")] public ArrayDst Array_AutoMapper() => _auto.Map<ArrayDst>(_array);
    [Benchmark, BenchmarkCategory("Array")] public ArrayDst Array_AgileMapper() => _agile.Map(_array).ToANew<ArrayDst>();

    // ── Blittable struct array (DwarfMapper's SIMD reinterpret vs element copy) ──
    [Benchmark, BenchmarkCategory("Blit")] public BlitDst Blit_Dwarf() => _dwarf.MapBlit(_blit);
    [Benchmark, BenchmarkCategory("Blit")] public BlitDst Blit_Mapperly() => _mapperly.MapBlit(_blit);
    [Benchmark, BenchmarkCategory("Blit")] public BlitDst Blit_Mapster() => _blit.Adapt<BlitDst>();
    [Benchmark, BenchmarkCategory("Blit")] public BlitDst Blit_AutoMapper() => _auto.Map<BlitDst>(_blit);
    [Benchmark, BenchmarkCategory("Blit")] public BlitDst Blit_AgileMapper() => _agile.Map(_blit).ToANew<BlitDst>();

    // ── Primitive widening array (DwarfMapper's Vector.Widen vs element loop) ────
    [Benchmark, BenchmarkCategory("Widen")] public WidenDst Widen_Dwarf() => _dwarf.MapWiden(_widen);
    [Benchmark, BenchmarkCategory("Widen")] public WidenDst Widen_Mapperly() => _mapperly.MapWiden(_widen);
    [Benchmark, BenchmarkCategory("Widen")] public WidenDst Widen_Mapster() => _widen.Adapt<WidenDst>();
    [Benchmark, BenchmarkCategory("Widen")] public WidenDst Widen_AutoMapper() => _auto.Map<WidenDst>(_widen);
    [Benchmark, BenchmarkCategory("Widen")] public WidenDst Widen_AgileMapper() => _agile.Map(_widen).ToANew<WidenDst>();
}
