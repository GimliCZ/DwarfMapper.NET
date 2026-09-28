// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace DwarfMapper.Benchmarks
{
    /// <summary>A projection-only mapper: no create map, so it adds nothing to the ambient registration others measure.</summary>
    [DwarfMapper]
    public partial class Round31ProjectionMapper
    {
        public partial IQueryable<NestedDst> Project(IQueryable<NestedSrc> q);
    }

    /// <summary>
    ///     <b>Round 31 T19: the projection changes T10 and T13 made, each against the shape it replaced.</b>
    ///     <para>
    ///         The "before" arms are the OLD emission written out by hand — an inline lambda, which C# turns into a new
    ///         expression tree on every call, and a list-backed queryable handed the tree, which LINQ-to-objects compiles
    ///         on every enumeration. Same types, same payload, same process, so the only difference is the shape.
    ///     </para>
    /// </summary>
    [MemoryDiagnoser]
    public class Round31ProjectionBenchmarks
    {
        private readonly Round31ProjectionMapper _mapper = new();
        private IQueryable<NestedSrc> _provider = null!;
        private IQueryable<NestedSrc> _inMemory = null!;

        [Params(10, 1000)]
        public int Rows { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            var list = Enumerable.Range(0, Rows).Select(i =>
            {
                var s = RealisticPayloads.One<NestedSrc>(5000 + i);
                s.Inner ??= RealisticPayloads.One<FlatSrc>(6000 + i);
                return s;
            }).ToList();
            _provider = new ProviderQueryable<NestedSrc>(list);
            _inMemory = list.AsQueryable();
        }

        // ── T10: building the query a provider receives (no enumeration) ────────────────────────────────

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("Build")]
        public Expression Build_InlineTree_Before()
        {
            // .Expression: the built query object itself is what a provider receives; returning it materialised keeps
            // BenchmarkDotNet from rejecting a deferred IQueryable, and nothing here enumerates.
            return Queryable.Select(_provider, s => new NestedDst
            {
                Id = s.Id,
                Inner = new FlatDst { Active = s.Inner.Active, Id = s.Inner.Id, Name = s.Inner.Name, Score = s.Inner.Score },
            }).Expression;
        }

        [Benchmark]
        [BenchmarkCategory("Build")]
        public Expression Build_HoistedTree_After()
        {
            return _mapper.Project(_provider).Expression;
        }

        // ── T13: a list-backed IQueryable, enumerated ───────────────────────────────────────────────────

        [Benchmark]
        [BenchmarkCategory("InMemory")]
        public List<NestedDst> InMemory_TreeCompiledPerEnumeration_Before()
        {
            return Queryable.Select(_inMemory, Round31ProjectionMapper.ProjectExpression).ToList();
        }

        [Benchmark]
        [BenchmarkCategory("InMemory")]
        public List<NestedDst> InMemory_RoutedDelegate_After()
        {
            return _mapper.Project(_inMemory).ToList();
        }

        /// <summary>An IQueryable that is not an EnumerableQuery, so the generated method takes the tree path.</summary>
        private sealed class ProviderQueryable<T>(IEnumerable<T> source) : IQueryable<T>
        {
            public Type ElementType => typeof(T);

            public Expression Expression { get; } = source.AsQueryable().Expression;

            public IQueryProvider Provider { get; } = source.AsQueryable().Provider;

            public IEnumerator<T> GetEnumerator()
            {
                return source.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }

    /// <summary>
    ///     <b>Round 31 T19 / T11: what a module initializer's registration costs at startup.</b>
    ///     <para>
    ///         The registry is static and add-only, so a second registration in the same process measures the duplicate
    ///         path, not startup. <see cref="RunStrategy.ColdStart" /> with one invocation per launch gives every
    ///         measurement a fresh process — one registration of <see cref="Pairs" /> pairs, seven entries each (the pair
    ///         plus its six IEnumerable-keyed collection shapes), exactly the load a generated initializer carries.
    ///     </para>
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(RunStrategy.ColdStart, launchCount: 5, warmupCount: 0, iterationCount: 1, invocationCount: 1)]
    public class Round31RegistrationStartupBenchmarks
    {
        private (Type, Type, Func<object, object>)[] _entries = null!;

        [Params(100, 500, 1000)]
        public int Pairs { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            var markers = new[]
            {
                typeof(int), typeof(long), typeof(short), typeof(byte), typeof(sbyte), typeof(uint), typeof(ulong),
                typeof(ushort), typeof(float), typeof(double), typeof(decimal), typeof(char), typeof(bool),
                typeof(string), typeof(DateTime),
            };
            var shapes = new[] { typeof(List<>), typeof(HashSet<>), typeof(Queue<>), typeof(Stack<>), typeof(LinkedList<>), typeof(SortedSet<>) };
            Func<object, object> map = o => o;
            _entries = (from a in markers from b in markers from c in markers select (a, b, c))
                .Take(Pairs)
                .SelectMany(t =>
                {
                    var src = typeof(ValueTuple<,,>).MakeGenericType(t.a, t.b, t.c);
                    var dst = typeof(Tuple<,,>).MakeGenericType(t.a, t.b, t.c);
                    return new[] { (src, dst, map) }
                        .Concat(shapes.Select(shape => (typeof(IEnumerable<>).MakeGenericType(src), shape.MakeGenericType(dst), map)));
                })
                .ToArray();
        }

        /// <summary>Before T11: one Register per entry, each interface entry copying the whole list.</summary>
        [Benchmark(Baseline = true)]
        public void Register_OneByOne_Before()
        {
            foreach (var (s, d, m) in _entries) DwarfMapperRegistry.Register(s, d, m);
        }

        [Benchmark]
        public void RegisterMany_After()
        {
            DwarfMapperRegistry.RegisterMany(_entries);
        }
    }

    /// <summary>
    ///     <b>Round 31 T19: the P8 ideas, measured before any becomes a task</b> (the round's threshold: ≥ 1.3x, or ≥ 20 %
    ///     allocation, consistently). <c>[SkipLocalsInit]</c> is not here: it needs unsafe code and leaves locals
    ///     uninitialized, which the owner's standing policy excludes, so it is decided without a number.
    /// </summary>
    [MemoryDiagnoser]
    public class Round31P8Benchmarks
    {
        private NestedSrc[] _ring = null!;
        private object[] _nodes = null!;
        private int _i;

        [GlobalSetup]
        public void Setup()
        {
            _ring = Enumerable.Range(0, 512).Select(i =>
            {
                var s = RealisticPayloads.One<NestedSrc>(7000 + i);
                s.Inner ??= RealisticPayloads.One<FlatSrc>(8000 + i);
                return s;
            }).ToArray();
            _nodes = Enumerable.Range(0, 1000).Select(_ => new object()).ToArray();
        }

        // ── P8a: [MethodImpl(AggressiveInlining)] on a small generated nested mapper ─────────────────────
        // The bodies are the generator's own output for DwarfM.MapNested / MapFlat, copied verbatim.

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("Inlining")]
        public NestedDst NestedMap_AsGenerated()
        {
            return AsGenerated.MapNested(_ring[_i++ & 511]);
        }

        [Benchmark]
        [BenchmarkCategory("Inlining")]
        public NestedDst NestedMap_AggressiveInlining()
        {
            return WithInlining.MapNested(_ring[_i++ & 511]);
        }

        // ── P8b: a Preserve identity map sized from the source count (a MODEL of DwarfRefContext's map) ──

        [Benchmark]
        [BenchmarkCategory("PreserveMap")]
        public int IdentityMap_Unsized()
        {
            var map = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
            foreach (var n in _nodes) map[n] = n;
            return map.Count;
        }

        [Benchmark]
        [BenchmarkCategory("PreserveMap")]
        public int IdentityMap_PreSized()
        {
            var map = new Dictionary<object, object>(_nodes.Length, ReferenceEqualityComparer.Instance);
            foreach (var n in _nodes) map[n] = n;
            return map.Count;
        }

        private static class AsGenerated
        {
            public static NestedDst MapNested(NestedSrc s)
            {
                ArgumentNullException.ThrowIfNull(s);
                return new NestedDst { Id = s.Id, Inner = s.Inner is null ? null! : MapFlat(s.Inner) };
            }

            private static FlatDst MapFlat(FlatSrc s)
            {
                ArgumentNullException.ThrowIfNull(s);
                return new FlatDst { Active = s.Active, Id = s.Id, Name = s.Name, Score = s.Score };
            }
        }

        private static class WithInlining
        {
            public static NestedDst MapNested(NestedSrc s)
            {
                ArgumentNullException.ThrowIfNull(s);
                return new NestedDst { Id = s.Id, Inner = s.Inner is null ? null! : MapFlat(s.Inner) };
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static FlatDst MapFlat(FlatSrc s)
            {
                ArgumentNullException.ThrowIfNull(s);
                return new FlatDst { Active = s.Active, Id = s.Id, Name = s.Name, Score = s.Score };
            }
        }
    }
}
