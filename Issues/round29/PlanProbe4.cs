// Research batch 4: "spannable classes" — view a class's field block as a span (MemoryMarshal.CreateSpan, no
// `unsafe` keyword) and blit it into a struct array element, instead of converting the declaration.
// Two questions: (1) is the class layout what the struct expects? (2) does the copy beat the field gather?
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

[StructLayout(LayoutKind.Sequential)]
public sealed class SeqC { public int A; public long B; public bool C; public float D; public double E; }   // layout honoured: no reference fields
public sealed class AutoC { public int A; public long B; public bool C; public float D; public double E; }  // auto layout: the CLR may reorder
public struct S5 { public int A; public long B; public bool C; public float D; public double E; }         // sequential (C# default for structs)

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class PlanProbe4
{
    [Params(1_000, 100_000)]
    public int N;

    private SeqC[] _seq = null!; private AutoC[] _auto = null!;
    private static readonly int BodyInts = (4 + 8 + 1 + 3 + 4 + 4 + 8) / 4; // 32 B body = 8 ints, as the sequential struct lays them out

    [GlobalSetup]
    public void Setup()
    {
        _seq = new SeqC[N]; _auto = new AutoC[N];
        for (var i = 0; i < N; i++)
        {
            _seq[i] = new SeqC { A = i, B = i * 3L, C = (i & 1) == 0, D = i * 0.25f, E = i * 0.5 };
            _auto[i] = new AutoC { A = i, B = i * 3L, C = (i & 1) == 0, D = i * 0.25f, E = i * 0.5 };
        }
        var g = Gather_Sequential(); var s = SpanCopy_Sequential(); var a = SpanCopy_Auto();
        var seqOk = true; var autoOk = true;
        for (var i = 0; i < N; i++)
        {
            if (g[i].A != s[i].A || g[i].B != s[i].B || g[i].C != s[i].C || g[i].D != s[i].D || g[i].E != s[i].E) seqOk = false;
            if (g[i].A != a[i].A || g[i].B != a[i].B || g[i].C != a[i].C || g[i].D != a[i].D || g[i].E != a[i].E) autoOk = false;
        }
        Console.WriteLine($"// LAYOUT CHECK: sequential class span-copy correct = {seqOk}; auto-layout class span-copy correct = {autoOk}; auto A/B/E read back as {a[7].A}/{a[7].B}/{a[7].E} (expected {g[7].A}/{g[7].B}/{g[7].E})");
        if (!seqOk) throw new InvalidOperationException("sequential class span copy is wrong on this runtime");
    }

    [Benchmark(Baseline = true), BenchmarkCategory("span")]
    public S5[] Gather_Sequential()
    {
        var r = new S5[_seq.Length];
        for (var i = 0; i < r.Length; i++) { var s = _seq[i]; r[i] = new S5 { A = s.A, B = s.B, C = s.C, D = s.D, E = s.E }; }
        return r;
    }

    [Benchmark, BenchmarkCategory("span")]
    public S5[] SpanCopy_Sequential()
    {
        // A span over the object's field block, starting at the first field, 8 ints = 32 bytes. No `unsafe`
        // keyword — but no bounds either: the span's length is the caller's claim about the object's layout.
        var r = new S5[_seq.Length];
        var dst = MemoryMarshal.AsBytes(new Span<S5>(r));
        for (var i = 0; i < r.Length; i++)
        {
            var body = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref _seq[i].A, BodyInts));
            body.CopyTo(dst.Slice(i * 32, 32));
        }
        return r;
    }

    [Benchmark, BenchmarkCategory("span")]
    public S5[] SpanCopy_Auto()
    {
        var r = new S5[_auto.Length];
        var dst = MemoryMarshal.AsBytes(new Span<S5>(r));
        for (var i = 0; i < r.Length; i++)
        {
            var body = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref _auto[i].A, BodyInts));
            body.CopyTo(dst.Slice(i * 32, 32));
        }
        return r;
    }
}
