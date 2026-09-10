// Hardware-mode probe: where does the time go, and what do the candidate strategies buy?
// Shapes mirror the repo benchmark payloads (Vec3 = 12 B struct; a 5-field unmanaged DTO ≈ 32 B object body).
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Probe).Assembly).Run(args, DefaultConfig.Instance.AddJob(Job.ShortRun.WithId("short")));

public struct Vec3S { public float X, Y, Z; }
public struct Vec3D { public float X, Y, Z; }

// Layout-identical classes with ONLY unmanaged fields: Sequential is honoured by the CLR for such classes.
[StructLayout(LayoutKind.Sequential)]
public sealed class DtoSrc { public int Id; public long Score; public bool Active; public float Ratio; public double Weight; }
[StructLayout(LayoutKind.Sequential)]
public sealed class DtoDst { public int Id; public long Score; public bool Active; public float Ratio; public double Weight; }
public struct DtoSStruct { public int Id; public long Score; public bool Active; public float Ratio; public double Weight; }
public struct DtoDStruct { public int Id; public long Score; public bool Active; public float Ratio; public double Weight; }

// Object body access: the CLR object is [MethodTable*][fields...]; RawData.Data is the first field's address.
[StructLayout(LayoutKind.Sequential)]
internal sealed class RawData { public byte Data; }

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class Probe
{
    [Params(1_000, 100_000, 1_000_000)]
    public int N;

    private Vec3S[] _vecs = null!;
    private DtoSrc[] _objs = null!;
    private DtoSStruct[] _dtoStructs = null!;
    private static readonly int BodySize = 4 + 8 + 1 + 3 /*pad*/ + 4 + 4 /*pad*/ + 8; // sequential layout of DtoSrc's fields = 32 B

    [GlobalSetup]
    public void Setup()
    {
        var rnd = new Random(42);
        _vecs = new Vec3S[N];
        for (var i = 0; i < N; i++) _vecs[i] = new Vec3S { X = i, Y = i + 0.5f, Z = rnd.NextSingle() };
        _objs = new DtoSrc[N];
        _dtoStructs = new DtoSStruct[N];
        for (var i = 0; i < N; i++)
        {
            _objs[i] = new DtoSrc { Id = i, Score = i * 3L, Active = (i & 1) == 0, Ratio = i * 0.25f, Weight = rnd.NextDouble() };
            _dtoStructs[i] = new DtoSStruct { Id = i, Score = i * 3L, Active = (i & 1) == 0, Ratio = i * 0.25f, Weight = rnd.NextDouble() };
        }
        // sanity: the class body copy must reproduce the fields
        var probe = new DtoDst();
        CopyBody(_objs[7], probe);
        if (probe.Id != 7 || probe.Score != 21 || probe.Weight != _objs[7].Weight) throw new InvalidOperationException("class body copy is wrong on this runtime");
    }

    // ── A. today's struct blit vs the same blit into an UNINITIALIZED array (skips the zeroing) ─────────
    [Benchmark(Baseline = true), BenchmarkCategory("A_structblit")]
    public Vec3D[] Blit_NewArray()
    {
        var r = new Vec3D[_vecs.Length];
        MemoryMarshal.Cast<Vec3S, Vec3D>(new ReadOnlySpan<Vec3S>(_vecs)).CopyTo(r);
        return r;
    }

    [Benchmark, BenchmarkCategory("A_structblit")]
    public Vec3D[] Blit_UninitializedArray()
    {
        var r = GC.AllocateUninitializedArray<Vec3D>(_vecs.Length);
        MemoryMarshal.Cast<Vec3S, Vec3D>(new ReadOnlySpan<Vec3S>(_vecs)).CopyTo(r);
        return r;
    }

    [Benchmark, BenchmarkCategory("A_structblit")]
    public Vec3D[] Blit_Uninit_NonTemporal()
    {
        var r = GC.AllocateUninitializedArray<Vec3D>(_vecs.Length);
        var src = MemoryMarshal.AsBytes(new ReadOnlySpan<Vec3S>(_vecs));
        var dst = MemoryMarshal.AsBytes(new Span<Vec3D>(r));
        NonTemporalCopy(src, dst);
        return r;
    }

    [Benchmark, BenchmarkCategory("A_structblit")]
    public Vec3D[] Blit_Uninit_Parallel()
    {
        var r = GC.AllocateUninitializedArray<Vec3D>(_vecs.Length);
        if (_vecs.Length < 200_000)
        {
            MemoryMarshal.Cast<Vec3S, Vec3D>(new ReadOnlySpan<Vec3S>(_vecs)).CopyTo(r);
            return r;
        }
        var src = _vecs; var chunks = Environment.ProcessorCount;
        var per = (src.Length + chunks - 1) / chunks;
        Parallel.For(0, chunks, c =>
        {
            var start = c * per; var len = Math.Min(per, src.Length - start);
            if (len <= 0) return;
            MemoryMarshal.Cast<Vec3S, Vec3D>(new ReadOnlySpan<Vec3S>(src, start, len)).CopyTo(new Span<Vec3D>(r, start, len));
        });
        return r;
    }

    // ── B. class pair: field copy (what every mapper emits) vs a per-object BODY memcpy ───────────────────
    [Benchmark(Baseline = true), BenchmarkCategory("B_classpair")]
    public DtoDst[] Class_FieldCopy()
    {
        var r = new DtoDst[_objs.Length];
        for (var i = 0; i < _objs.Length; i++)
        {
            var s = _objs[i];
            r[i] = new DtoDst { Id = s.Id, Score = s.Score, Active = s.Active, Ratio = s.Ratio, Weight = s.Weight };
        }
        return r;
    }

    [Benchmark, BenchmarkCategory("B_classpair")]
    public DtoDst[] Class_BodyMemcpy()
    {
        var r = new DtoDst[_objs.Length];
        for (var i = 0; i < _objs.Length; i++)
        {
            var d = new DtoDst();
            CopyBody(_objs[i], d);
            r[i] = d;
        }
        return r;
    }

    [Benchmark, BenchmarkCategory("B_classpair")]
    public DtoDst[] Class_BodyMemcpy_Uninit()
    {
        var r = new DtoDst[_objs.Length];
        for (var i = 0; i < _objs.Length; i++)
        {
            var d = (DtoDst)RuntimeHelpers.GetUninitializedObject(typeof(DtoDst));
            CopyBody(_objs[i], d);
            r[i] = d;
        }
        return r;
    }

    // ── C. the storage question: the same 5 fields as class→class, class→struct (one allocation), struct→struct ──
    [Benchmark, BenchmarkCategory("B_classpair")]
    public DtoDStruct[] Class_To_StructArray_Gather()
    {
        var r = GC.AllocateUninitializedArray<DtoDStruct>(_objs.Length);
        for (var i = 0; i < _objs.Length; i++)
        {
            var s = _objs[i];
            r[i] = new DtoDStruct { Id = s.Id, Score = s.Score, Active = s.Active, Ratio = s.Ratio, Weight = s.Weight };
        }
        return r;
    }

    [Benchmark, BenchmarkCategory("B_classpair")]
    public DtoDStruct[] Struct_To_Struct_Blit()
    {
        var r = GC.AllocateUninitializedArray<DtoDStruct>(_dtoStructs.Length);
        MemoryMarshal.Cast<DtoSStruct, DtoDStruct>(new ReadOnlySpan<DtoSStruct>(_dtoStructs)).CopyTo(r);
        return r;
    }

    [Benchmark, BenchmarkCategory("B_classpair")]
    public DtoDst[] StructArray_To_Classes_Unpack()
    {
        var r = new DtoDst[_dtoStructs.Length];
        for (var i = 0; i < _dtoStructs.Length; i++)
        {
            ref readonly var s = ref _dtoStructs[i];
            r[i] = new DtoDst { Id = s.Id, Score = s.Score, Active = s.Active, Ratio = s.Ratio, Weight = s.Weight };
        }
        return r;
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyBody(DtoSrc src, DtoDst dst)
    {
        ref var s = ref Unsafe.As<RawData>(src).Data;
        ref var d = ref Unsafe.As<RawData>(dst).Data;
        Unsafe.CopyBlockUnaligned(ref d, ref s, (uint)BodySize);
    }

    private static unsafe void NonTemporalCopy(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        if (!Avx.IsSupported || dst.Length < 4096)
        {
            src.CopyTo(dst);
            return;
        }
        fixed (byte* ps = src)
        fixed (byte* pd = dst)
        {
            var n = dst.Length;
            var i = 0;
            // head: scalar until dst is 32-byte aligned
            var mis = (int)((32 - ((nuint)pd & 31)) & 31);
            for (; i < mis; i++) pd[i] = ps[i];
            for (; i + 32 <= n; i += 32)
            {
                Avx.StoreAlignedNonTemporal(pd + i, Avx.LoadVector256(ps + i));
            }
            for (; i < n; i++) pd[i] = ps[i];
            Sse.StoreFence();
        }
    }
}
