// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;

namespace DwarfMapper.Benchmarks
{
    /// <summary>
    ///     <b>Round 31 T15 go/no-go: would the in-memory projection twin gain from general collection mapping?</b>
    ///     <para>
    ///         T13's delegate twin reuses the projection's lambda text, so a nested collection in it is
    ///         <c>Enumerable.Select(…).ToList()</c>. T15 would swap that for the general mapping pipeline (pre-sizing,
    ///         blits, SIMD widening) where projection and map semantics are provably equal. This measures what the swap
    ///         could buy, per element kind, against the generated collection mappers that already exist in
    ///         <see cref="DwarfM" />: a same-type value list (the blit case), a widening value list, and a DTO list. The
    ///         round's threshold for becoming a task is ≥ 1.3x or ≥ 20 % allocation, consistently.
    ///     </para>
    /// </summary>
    [MemoryDiagnoser]
    public class Round31T15Benchmarks
    {
        private readonly DwarfM _dwarf = new();
        private NumListSrc _num = null!;
        private List<int> _ints = null!;
        private List<FlatSrc> _dtos = null!;

        [Params(16, 1024)]
        public int N { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _ints = Enumerable.Range(0, N).ToList();
            _num = new NumListSrc { V = _ints.ToArray() };
            _dtos = Enumerable.Range(0, N).Select(i => RealisticPayloads.One<FlatSrc>(9000 + i)).ToList();
        }

        // ── same-type value list: what the tree text does vs a copy ─────────────────────────────────────

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("IntList")]
        public List<int> IntList_SelectToList_Twin()
        {
            return _ints.Select(x => x).ToList();
        }

        [Benchmark]
        [BenchmarkCategory("IntList")]
        public List<int> IntList_GeneralMapping_Copy()
        {
            return new List<int>(_ints);
        }

        // ── widening value list (int -> long) ────────────────────────────────────────────────────────────

        [Benchmark]
        [BenchmarkCategory("Widen")]
        public List<long> Widen_SelectToList_Twin()
        {
            return _num.V.Select(x => (long)x).ToList();
        }

        [Benchmark]
        [BenchmarkCategory("Widen")]
        public NumListDst Widen_GeneralMapping_Generated()
        {
            return _dwarf.MapNumList(_num);
        }

        // ── DTO list ─────────────────────────────────────────────────────────────────────────────────────

        [Benchmark]
        [BenchmarkCategory("Dto")]
        public List<FlatDst> Dto_SelectToList_Twin()
        {
            return _dtos.Select(s => new FlatDst { Active = s.Active, Id = s.Id, Name = s.Name, Score = s.Score }).ToList();
        }

        [Benchmark]
        [BenchmarkCategory("Dto")]
        public List<FlatDst> Dto_GeneralMapping_PreSizedLoop()
        {
            var result = new List<FlatDst>(_dtos.Count);
            foreach (var s in _dtos) result.Add(_dwarf.MapFlat(s));
            return result;
        }
    }
}
