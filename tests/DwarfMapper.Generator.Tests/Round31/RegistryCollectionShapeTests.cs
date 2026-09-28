// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DwarfMapper;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     The six collection shapes every mapped pair auto-registers are keyed on <c>IEnumerable&lt;S&gt;</c>, so a
    ///     source reaching them arrives as an interface and the walk used to go through a BOXED enumerator into an
    ///     un-sized <c>List</c> — while the DIRECT collection path had pre-sized since round 30. Research P1 measured
    ///     the gap at 1.07-1.95x for pre-sizing alone and 2.8-3.8x with a concrete fast path, at 17-29 % more
    ///     allocation.
    /// </summary>
    /// <remarks>
    ///     Three kinds of row, and the split is the point. EMISSION rows assert the registration delegates to the
    ///     runtime helper — red before the change. HELPER rows prove the fast paths and the exact pre-size directly,
    ///     which is where the behaviour now lives. BEHAVIOUR rows are guards, green before and after: an optimisation
    ///     that changed a RESULT would be a defect, so they pin equality across array, list, set, lazy-iterator and
    ///     empty sources rather than pinning speed.
    /// </remarks>
    public sealed class RegistryCollectionShapeTests
    {
        private const string Src = """
            #nullable enable
            using DwarfMapper;
            namespace T09;
            public class S { public int V { get; set; } }
            public class D { public int V { get; set; } }
            [DwarfMapper] public partial class M { public partial D Map(S s); }
            """;

        // ── emission ──────────────────────────────────────────────────────────────

        [Fact]
        public void The_list_registration_delegates_to_the_runtime_helper()
        {
            var gen = GeneratorTestHarness.RunAll(Src).GeneratedSource;
            Assert.Contains("DwarfCollectionMap.ToList<", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void The_array_registration_delegates_to_the_runtime_helper()
        {
            var gen = GeneratorTestHarness.RunAll(Src).GeneratedSource;
            Assert.Contains("DwarfCollectionMap.ToArray<", gen, StringComparison.Ordinal);
        }

        // ── the helper: exact pre-size, which is the whole optimisation ────────────

        /// <summary>
        ///     Capacity rather than an allocation byte count: a list whose capacity equals its count grew ZERO times,
        ///     which is the property being bought, stated deterministically instead of as a threshold that drifts
        ///     with the runtime.
        /// </summary>
        [Fact]
        public void ToList_sizes_exactly_for_an_array_source()
        {
            var source = new[] { 1, 2, 3, 4, 5 };
            var mapped = DwarfCollectionMap.ToList<int, string>(source, i => i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(source.Length, mapped.Count);
            Assert.Equal(source.Length, mapped.Capacity);
        }

        [Fact]
        public void ToList_sizes_exactly_for_a_list_source()
        {
            var source = new List<int> { 1, 2, 3 };
            var mapped = DwarfCollectionMap.ToList<int, int>(source, i => i * 2);
            Assert.Equal([2, 4, 6], mapped);
            Assert.Equal(source.Count, mapped.Capacity);
        }

        [Fact]
        public void ToList_sizes_exactly_for_any_source_that_knows_its_count_without_enumerating()
        {
            var source = new HashSet<int> { 1, 2, 3, 4 };
            var mapped = DwarfCollectionMap.ToList<int, int>(source, i => i);
            Assert.Equal(source.Count, mapped.Count);
            Assert.Equal(source.Count, mapped.Capacity);
        }

        [Fact] // GUARD: the null-source contract. The explicit ThrowIfNull(source) CA1062 requires is unobservable (the
               // enumerable path throws the same exception, same parameter name) - an equivalent mutant, pinned here.
        public void A_null_source_is_an_ArgumentNullException_naming_source()
        {
            Assert.Equal("source", Assert.Throws<ArgumentNullException>(() => DwarfCollectionMap.ToList<int, int>(null!, i => i)).ParamName);
            Assert.Equal("source", Assert.Throws<ArgumentNullException>(() => DwarfCollectionMap.ToArray<int, int>(null!, i => i)).ParamName);
            Assert.Equal("map", Assert.Throws<ArgumentNullException>(() => DwarfCollectionMap.ToList<int, int>(new[] { 1 }, null!)).ParamName);
        }

        [Fact]
        public void An_uncounted_source_still_maps_completely()
        {
            // A lazy iterator has no non-enumerated count, so it takes the buffered fall-through. Correctness is the
            // assertion here; the capacity deliberately is not, because nothing can know it in advance.
            var source = Enumerable.Range(1, 4).Where(i => i > 0);
            Assert.Equal([1, 2, 3, 4], DwarfCollectionMap.ToList<int, int>(source, i => i));
            Assert.Equal([1, 2, 3, 4], DwarfCollectionMap.ToArray<int, int>(source, i => i));
        }

        [Fact]
        public void ToArray_fills_the_array_directly_for_every_counted_shape()
        {
            Assert.Equal([1, 2, 3], DwarfCollectionMap.ToArray<int, int>(new[] { 1, 2, 3 }, i => i));
            Assert.Equal([1, 2, 3], DwarfCollectionMap.ToArray<int, int>(new List<int> { 1, 2, 3 }, i => i));
            Assert.Equal([1, 2], DwarfCollectionMap.ToArray<int, int>(new HashSet<int> { 1, 2 }, i => i));
            Assert.Empty(DwarfCollectionMap.ToArray<int, int>(Array.Empty<int>(), i => i));
        }

        /// <summary>
        ///     A source collection mutated WHILE it is being mapped must fail loudly, not return a stale result.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The element map is a generated mapper, so it can run a user <c>BeforeMap</c>/<c>AfterMap</c> hook
        ///         or a user-declared converter, and either can reach the source collection. The first version of
        ///         this helper read a <c>List</c> source through <c>CollectionsMarshal.AsSpan</c> and called the map
        ///         inside that loop: when the list grew, it swapped backing arrays and the span kept reading the old
        ///         one, so the helper returned a silently stale answer where the enumerator it replaced would have
        ///         thrown. Nothing was memory-unsafe - the old array is still a live managed object - but a loud
        ///         failure had become a quiet wrong one, which is the trade this library refuses (see the null-ternary
        ///         ruling, b25ae56).
        ///     </para>
        ///     <para>
        ///         Measured before removing it, so the safety is not paid for blindly: with a job that can resolve the
        ///         difference (MediumRun, not the coarse invocationCount=16 job this benchmark class ships), the
        ///         version-checked walk is FASTER at every element count - 0.83x at 16, 0.74x at 1,024, 0.94x at
        ///         65,536 - and allocates the same bytes, because the whole allocation win was the pre-size and that
        ///         is kept. See benchmarks/results/2026-09-26-round31-full-matrix.md.
        ///     </para>
        /// </remarks>
        [Fact]
        public void A_source_list_mutated_during_the_map_throws_instead_of_returning_a_stale_result()
        {
            var source = new List<int> { 1, 2, 3 };
            Assert.Throws<InvalidOperationException>(
                () => DwarfCollectionMap.ToList<int, int>(source, i =>
                {
                    source.Add(99);
                    return i;
                }));

            var forArray = new List<int> { 1, 2, 3 };
            Assert.Throws<InvalidOperationException>(
                () => DwarfCollectionMap.ToArray<int, int>(forArray, i =>
                {
                    forArray.Add(99);
                    return i;
                }));
        }

        /// <summary>
        ///     An ARRAY source keeps its indexed walk, and that is not an inconsistency: an array cannot grow, so
        ///     there is no backing-array swap to read stale, and element writes through the source are the caller's
        ///     own business. Stated as a test so the asymmetry reads as a decision rather than an oversight.
        /// </summary>
        [Fact]
        public void An_array_source_is_still_walked_by_index_and_sees_the_callers_own_writes()
        {
            var source = new[] { 1, 2, 3 };
            var mapped = DwarfCollectionMap.ToList<int, int>(source, i =>
            {
                source[2] = 30;
                return i;
            });

            Assert.Equal([1, 2, 30], mapped);
        }

        [Fact]
        public void The_helper_refuses_a_null_source_or_map_rather_than_dereferencing_it()
        {
            Assert.Throws<ArgumentNullException>(() => DwarfCollectionMap.ToList<int, int>(null!, i => i));
            Assert.Throws<ArgumentNullException>(() => DwarfCollectionMap.ToList<int, int>(new[] { 1 }, null!));
            Assert.Throws<ArgumentNullException>(() => DwarfCollectionMap.ToArray<int, int>(null!, i => i));
            Assert.Throws<ArgumentNullException>(() => DwarfCollectionMap.ToArray<int, int>(new[] { 1 }, null!));
        }

        // ── behaviour through the registry (guards: green before and after) ────────

        public static TheoryData<string> SourceShapes()
        {
            return new TheoryData<string> { "Array", "List", "HashSet", "Lazy", "Empty" };
        }

        [Theory]
        [MemberData(nameof(SourceShapes))]
        public void Every_source_shape_maps_to_the_same_elements(string shape)
        {
            var (asm, errors) = GeneratorTestHarness.EmitAssembly(Src);
            Assert.True(asm is not null, "emit failed: " + string.Join(",", errors.Select(e => e.Id)));
            var s = asm!.GetType("T09.S", true)!;
            var d = asm.GetType("T09.D", true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(asm.ManifestModule.ModuleHandle);

            var values = string.Equals(shape, "Empty", StringComparison.Ordinal) ? [] : new[] { 1, 2, 3 };
            var source = BuildSource(shape, s, values);

            foreach (var destination in new[] { typeof(List<>).MakeGenericType(d), d.MakeArrayType() })
            {
                var mapped = (IEnumerable)Map(source, destination)!;
                var got = mapped.Cast<object>().Select(o => (int)d.GetProperty("V")!.GetValue(o)!).ToArray();
                Assert.Equal(values, got);
            }
        }

        private static object BuildSource(string shape, Type s, int[] values)
        {
            var items = values.Select(v =>
            {
                var o = Activator.CreateInstance(s)!;
                s.GetProperty("V")!.SetValue(o, v);
                return o;
            }).ToArray();

            var typed = Array.CreateInstance(s, items.Length);
            items.CopyTo(typed, 0);

            switch (shape)
            {
                case "Array":
                    return typed;
                case "Lazy":
                    return typeof(Enumerable).GetMethods()
                        .First(m => string.Equals(m.Name, nameof(Enumerable.Where), StringComparison.Ordinal)
                                    && m.GetParameters()[1].ParameterType.GetGenericArguments().Length == 2)
                        .MakeGenericMethod(s)
                        .Invoke(null, [typed, AlwaysTrue(s)])!;
                case "HashSet":
                    return Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(s), [typed])!;
                default:
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(s), [typed])!;
            }
        }

        private static Delegate AlwaysTrue(Type s)
        {
            return typeof(RegistryCollectionShapeTests)
                .GetMethod(nameof(True), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(s)
                .CreateDelegate(typeof(Func<,>).MakeGenericType(s, typeof(bool)));
        }

        private static bool True<T>(T _)
        {
            return true;
        }

        private static object? Map(object source, Type destination)
        {
            var map = typeof(DwarfMapperRegistry).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => string.Equals(m.Name, "Map", StringComparison.Ordinal)
                            && m.GetParameters().Length == 2
                            && m.GetParameters()[1].ParameterType == typeof(Type));
            return map.Invoke(null, [source, destination]);
        }
    }
}
