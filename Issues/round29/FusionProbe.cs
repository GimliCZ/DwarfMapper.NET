// Round-29 Phase 4 spike: map fusion. Does A -> B -> C need a generator feature, or has the JIT already
// removed B?
//
// The plan's instruction was explicit about the order: "when A->B and B->C are both [GenerateMap] pairs in
// one class, emit A->C composed; MEASURE WHETHER THE JIT ALREADY ELIDES B (escape analysis in .NET 10 may)
// BEFORE BUILDING IT."
//
// .NET 10 widened escape analysis considerably — small arrays of value AND reference types, local struct
// fields, delegates — and once an object is stack-allocated the JIT may go further and replace it with its
// scalar values outright. An intermediate DTO that is constructed, read once and discarded is the textbook
// shape for that.
//
// THE MEASUREMENT IS ALLOCATION, NOT TIME. This repository's timing runs are three iterations and several
// have error bars approaching their means; its allocation numbers are exact bytes and are gated as such.
// If the JIT elides B, the chained path allocates ONLY the final C and its byte count equals the fused
// path's. If it does not, the chained path allocates B as well and the difference is exactly sizeof(B)'s
// heap footprint. That is a yes/no answer with no noise in it, which is a better instrument than a ratio
// with a 60 % error bar.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

public sealed class FuseA
{
    public int Id { get; set; }
    public long Score { get; set; }
    public double Weight { get; set; }
    public int Extra { get; set; }
}

// The intermediate. If the JIT elides it, this type never reaches the heap in the chained benchmark.
public sealed class FuseB
{
    public int Id { get; set; }
    public long Score { get; set; }
    public double Weight { get; set; }
    public int Extra { get; set; }
}

public sealed class FuseC
{
    public int Id { get; set; }
    public long Score { get; set; }
    public double Weight { get; set; }
    public int Extra { get; set; }
}

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class Fusion
{
    // N=1 isolates the question: does ONE intermediate reach the heap? Larger N only multiplies the same
    // answer and adds GC noise. The loop variants exist because escape analysis behaves differently when
    // the intermediate is created inside a loop body.
    [Params(1, 1_000)]
    public int N;

    private FuseA[] _src = null!;

    [GlobalSetup]
    public void Setup()
    {
        _src = new FuseA[N];
        for (var i = 0; i < N; i++)
        {
            _src[i] = new FuseA { Id = i, Score = i * 3L, Weight = i * 0.5, Extra = i ^ 7 };
        }
    }

    // The shape the generator emits today: two declared pairs, called in sequence. B is a local, passed to
    // the second map, and never stored.
    [Benchmark(Baseline = true), BenchmarkCategory("fuse")]
    public FuseC[] Chained()
    {
        var r = new FuseC[_src.Length];
        for (var i = 0; i < r.Length; i++)
        {
            r[i] = MapBToC(MapAToB(_src[i]));
        }

        return r;
    }

    // The shape a fusion feature would emit: one map, no intermediate. If Chained matches this on
    // allocation, the feature buys nothing the JIT is not already giving away.
    [Benchmark, BenchmarkCategory("fuse")]
    public FuseC[] Fused()
    {
        var r = new FuseC[_src.Length];
        for (var i = 0; i < r.Length; i++)
        {
            var a = _src[i];
            r[i] = new FuseC { Id = a.Id, Score = a.Score, Weight = a.Weight, Extra = a.Extra };
        }

        return r;
    }

    // A control: the intermediate deliberately escapes, so the JIT cannot elide it. If Chained's allocation
    // equals Fused's while this one is higher by exactly one FuseB per element, the elision is proven rather
    // than inferred from two numbers being equal for some other reason.
    [Benchmark, BenchmarkCategory("fuse")]
    public FuseC[] ChainedEscaping()
    {
        var r = new FuseC[_src.Length];
        var keep = new FuseB[_src.Length];
        for (var i = 0; i < r.Length; i++)
        {
            var b = MapAToB(_src[i]);
            keep[i] = b;                       // escapes: stored in an array that outlives the iteration
            r[i] = MapBToC(b);
        }

        GC.KeepAlive(keep);
        return r;
    }

    // Written as the generator writes them: small, non-virtual, trivially inlineable.
    private static FuseB MapAToB(FuseA a)
    {
        return new FuseB { Id = a.Id, Score = a.Score, Weight = a.Weight, Extra = a.Extra };
    }

    private static FuseC MapBToC(FuseB b)
    {
        return new FuseC { Id = b.Id, Score = b.Score, Weight = b.Weight, Extra = b.Extra };
    }
}
