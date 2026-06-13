// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using DwarfMapper;

BenchmarkRunner.Run<MapperBenchmarks>();

// ── Benchmark types ───────────────────────────────────────────────────────────
public sealed class FlatSrc { public int Id { get; set; } public string Name { get; set; } = ""; public long Score { get; set; } public bool Active { get; set; } }
public sealed class FlatDst { public int Id { get; set; } public string Name { get; set; } = ""; public long Score { get; set; } public bool Active { get; set; } }

public sealed class NestedSrc { public int Id { get; set; } public FlatSrc Inner { get; set; } = new(); }
public sealed class NestedDst { public int Id { get; set; } public FlatDst Inner { get; set; } = new(); }

// Blittable struct pair (identical layout → DwarfMapper emits the SIMD reinterpret fast-path).
public struct Vec3Src { public float X; public float Y; public float Z; }
public struct Vec3Dst { public float X; public float Y; public float Z; }

// Collection containers (array members exercise the element loop + blit fast-path).
public sealed class ArraySrc { public FlatSrc[] Items { get; set; } = System.Array.Empty<FlatSrc>(); }
public sealed class ArrayDst { public FlatDst[] Items { get; set; } = System.Array.Empty<FlatDst>(); }
public sealed class BlitSrc { public Vec3Src[] Items { get; set; } = System.Array.Empty<Vec3Src>(); }
public sealed class BlitDst { public Vec3Dst[] Items { get; set; } = System.Array.Empty<Vec3Dst>(); }

[DwarfMapper]
public partial class Mapper
{
    public partial FlatDst MapFlat(FlatSrc s);           // also used for NestedDst.Inner
    public partial NestedDst MapNested(NestedSrc s);
    public partial ArrayDst MapArray(ArraySrc s);
    public partial BlitDst MapBlit(BlitSrc s);           // Vec3Src[]→Vec3Dst[]: SIMD reinterpret fast-path
}

// ── Benchmarks: DwarfMapper (compile-time) vs hand-written baseline ────────────
[MemoryDiagnoser]
public class MapperBenchmarks
{
    private readonly Mapper _mapper = new();
    private FlatSrc _flat = null!;
    private NestedSrc _nested = null!;
    private ArraySrc _array = null!;
    private BlitSrc _blit = null!;

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
    }

    [Benchmark(Baseline = true)] public FlatDst Flat_HandWritten() => HandFlat(_flat);
    [Benchmark] public FlatDst Flat_DwarfMapper() => _mapper.MapFlat(_flat);

    [Benchmark] public NestedDst Nested_DwarfMapper() => _mapper.MapNested(_nested);

    [Benchmark] public ArrayDst Array_DwarfMapper() => _mapper.MapArray(_array);

    [Benchmark] public BlitDst Blit_DwarfMapper() => _mapper.MapBlit(_blit);

    private static FlatDst HandFlat(FlatSrc s)
        => new FlatDst { Id = s.Id, Name = s.Name, Score = s.Score, Active = s.Active };
}
