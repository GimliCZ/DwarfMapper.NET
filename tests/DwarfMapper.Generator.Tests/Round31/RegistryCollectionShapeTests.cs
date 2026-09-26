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
