// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>[MapTo]</c> on a <c>struct</c>. Its <c>AttributeUsage</c> admits
    ///     <c>AttributeTargets.Class | AttributeTargets.Struct</c> and the target check admits
    ///     <c>TypeKind.Struct</c>, so a value-type source is a legal placement — but the registry wrote
    ///     <c>if (source is null) throw …</c> into every extension method it emitted, without ever asking whether
    ///     the source could BE null. Against a non-nullable value type that pattern is <c>CS0037</c>, so every
    ///     such placement produced a generated file the compiler rejects: finding <b>A11-F1</b>, surfaced by the
    ///     surface matrix once it gained a Struct site, and invisible until then because no source in
    ///     <c>samples/</c> or <c>tests/</c> put <c>[MapTo]</c> on a struct.
    ///     <para>
    ///         Pinned in both directions here — a value-type source must LOSE the guard, a reference-type source
    ///         must KEEP it — because a fix that only deletes the guard passes the first half and silently breaks
    ///         the second. The third direction, a <c>Nullable&lt;T&gt;</c> source, cannot reach this generator at
    ///         all (an attribute sits on a declaration, and there is no declaration of <c>T?</c>) and is pinned on
    ///         the predicate itself in <see cref="Core.TypeFactsTests" />.
    ///     </para>
    /// </summary>
    public class RegistryValueTypeSourceTests
    {
        private const string StructSource = """
                                            using DwarfMapper;
                                            namespace Demo;
                                            [MapTo(typeof(Dto))]
                                            public struct Src { public int Id { get; set; } public string Name { get; set; } }
                                            public class Dto { public int Id { get; set; } public string Name { get; set; } }
                                            """;

        private const string RecordStructSource = """
                                                  using DwarfMapper;
                                                  namespace Demo;
                                                  [MapTo(typeof(Dto))]
                                                  public record struct Src { public int Id { get; set; } public string Name { get; set; } }
                                                  public class Dto { public int Id { get; set; } public string Name { get; set; } }
                                                  """;

        private const string ClassSource = """
                                           using DwarfMapper;
                                           namespace Demo;
                                           [MapTo(typeof(Dto))]
                                           public class Src { public int Id { get; set; } public string Name { get; set; } }
                                           public class Dto { public int Id { get; set; } public string Name { get; set; } }
                                           """;

        [Theory]
        [InlineData(nameof(StructSource))]
        [InlineData(nameof(RecordStructSource))]
        public void A_value_type_source_emits_no_null_guard(string which)
        {
            var generated = GeneratorTestHarness.RunMapToWithSource(Pick(which)).GeneratedSource;

            Assert.DoesNotContain("source is null", generated, StringComparison.Ordinal);
            // The methods themselves must still be there — "no guard" says nothing unless something was emitted.
            Assert.Contains("MapTo<TTarget>(this global::Demo.Src source)", generated, StringComparison.Ordinal);
            Assert.Contains("ToDto(this global::Demo.Src source)", generated, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(nameof(StructSource))]
        [InlineData(nameof(RecordStructSource))]
        public void A_value_type_source_produces_code_the_compiler_accepts(string which)
        {
            // The CS0037 that A11 measured, fourteen times over. Asserted against the FINAL compilation (the
            // user's source plus every generated file), which is the only place a defect in emitted text is
            // visible at all — the generator itself reported nothing and was right not to.
            GeneratorAssert.EmitsCompilableCode(Pick(which));
        }

        [Fact]
        public void A_reference_type_source_keeps_its_null_guard()
        {
            // The other direction. A class source is reachable from callers with no nullable annotations at all,
            // so the ArgumentNullException is contract rather than decoration — deleting the guard outright would
            // have turned the struct cells green and quietly removed it. Two methods, two guards.
            //
            // The FORM is ArgumentNullException.ThrowIfNull, not an inline `if (source is null) throw`, and that
            // is pinned deliberately rather than incidentally: round 24 found the [MapTo] emitter still writing
            // the inline form while MapEmitter had moved to the throw-helper, so one product emitted two idioms
            // for one contract. Nothing could see it — both compile, and a snapshot pins whatever it is given.
            // Counting the helper form here is what keeps the two emitters from drifting apart again.
            var generated = GeneratorTestHarness.RunMapToWithSource(ClassSource).GeneratedSource;

            Assert.Equal(2, CountOccurrences(generated, "ArgumentNullException.ThrowIfNull(source)"));
            Assert.DoesNotContain("if (source is null) throw", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A nested value-type member goes through the synthesized-helper path, which already HAD the
        ///     discrimination the extension methods lacked. It must still have it now that both read one
        ///     predicate — the regression a shared helper makes possible, and the reason this is pinned rather
        ///     than assumed.
        /// </summary>
        [Fact]
        public void A_nested_value_type_member_is_mapped_without_a_null_test()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  [MapTo(typeof(Dto))]
                                  public struct Src { public int Id { get; set; } public Leaf Node { get; set; } }
                                  public struct Leaf { public int Value { get; set; } }
                                  public class LeafDto { public int Value { get; set; } }
                                  public class Dto { public int Id { get; set; } public LeafDto Node { get; set; } }
                                  """;

            var (_, generated) = GeneratorTestHarness.RunMapToWithSource(source);

            Assert.Contains("__DwarfMapObj_", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("s is null ?", generated, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(source);
        }

        /// <summary>
        ///     A nested REFERENCE-type member still null-propagates. The same helper, the opposite answer,
        ///     recorded beside the case above so the pair reads as one contract instead of two coincidences.
        /// </summary>
        [Fact]
        public void A_nested_reference_type_member_still_null_propagates()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  [MapTo(typeof(Dto))]
                                  public struct Src { public int Id { get; set; } public Leaf Node { get; set; } }
                                  public class Leaf { public int Value { get; set; } }
                                  public class LeafDto { public int Value { get; set; } }
                                  public class Dto { public int Id { get; set; } public LeafDto Node { get; set; } }
                                  """;

            var (_, generated) = GeneratorTestHarness.RunMapToWithSource(source);

            Assert.Contains("s is null ?", generated, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(source);
        }

        /// <summary>
        ///     The THIRD inline instance of the same notion, sixty lines above the two the first fix unified, and
        ///     the worst of them: the synthesized COLLECTION helper wrote <c>if (s is null) return …</c>
        ///     unconditionally, and it is reachable from an ordinary CLASS source.
        ///     <para>
        ///         <c>CollectionConverter.TryGetEnumerableElement</c> admits any type implementing
        ///         <c>IEnumerable&lt;T&gt;</c>, value types included, and <c>Resolve</c> reaches <c>TryCollection</c>
        ///         before the nested-object branch. So a plain class with an <c>ImmutableArray&lt;T&gt;</c> member
        ///         mapped to a <c>List&lt;U&gt;</c> destination emitted <c>s is null</c> against a non-nullable
        ///         value type — the same <c>CS0037</c>, with no <c>struct</c> anywhere in the caller's declaration.
        ///         Nothing reached it: the one struct-<c>IEnumerable</c> fixture in the corpus sits on the
        ///         <c>[DwarfMapper]</c> path, which refuses that shape as <c>DWARF027</c>, and the registry has no
        ///         such exclusion. The corpus hole again, one level along from the one A11 found.
        ///     </para>
        /// </summary>
        [Fact]
        public void A_value_type_collection_member_on_a_class_source_is_mapped_without_a_null_test()
        {
            // A user-declared value-type enumerable rather than ImmutableArray<T>, which trips a SEPARATE and
            // pre-existing registry defect on the very next emitted line (the pre-sizing argument binds `s.Count`
            // on a type whose ICollection<T>.Count is an explicit implementation). Reported, not fixed here: a
            // fixture that fails for two reasons cannot show which one this task closed.
            const string source = """
                                  using System.Collections;
                                  using System.Collections.Generic;
                                  using DwarfMapper;
                                  namespace Demo;
                                  [MapTo(typeof(Dto))]
                                  public class Src { public Leaves Items { get; set; } }
                                  public struct Leaf { public int Value { get; set; } }
                                  public readonly struct Leaves : IEnumerable<Leaf>
                                  {
                                      private readonly Leaf[] _items;
                                      public Leaves(Leaf[] items) { _items = items; }
                                      public IEnumerator<Leaf> GetEnumerator() =>
                                          ((IEnumerable<Leaf>)(_items ?? new Leaf[0])).GetEnumerator();
                                      IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                                  }
                                  public class LeafDto { public int Value { get; set; } }
                                  public class Dto { public List<LeafDto> Items { get; set; } }
                                  """;

            var (_, generated) = GeneratorTestHarness.RunMapToWithSource(source);

            // Compilability first: the CS0037 IS the defect, the missing text is only its mechanism.
            GeneratorAssert.EmitsCompilableCode(source);
            Assert.Contains("__DwarfMapColl_", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("s is null", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     And the other direction for that same helper: a REFERENCE-type collection source keeps the guard,
        ///     because a null <c>List&lt;T&gt;</c> member must map to an empty destination rather than throwing
        ///     inside a <c>foreach</c>.
        /// </summary>
        [Fact]
        public void A_reference_type_collection_member_keeps_its_null_test()
        {
            const string source = """
                                  using DwarfMapper;
                                  using System.Collections.Generic;
                                  namespace Demo;
                                  [MapTo(typeof(Dto))]
                                  public class Src { public List<Leaf> Items { get; set; } }
                                  public class Leaf { public int Value { get; set; } }
                                  public class LeafDto { public int Value { get; set; } }
                                  public class Dto { public List<LeafDto> Items { get; set; } }
                                  """;

            var (_, generated) = GeneratorTestHarness.RunMapToWithSource(source);

            Assert.Contains("__DwarfMapColl_", generated, StringComparison.Ordinal);
            Assert.Contains("if (s is null) return", generated, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(source);
        }

        private static string Pick(string which)
        {
            return which switch
            {
                nameof(StructSource) => StructSource,
                nameof(RecordStructSource) => RecordStructSource,
                _ => throw new ArgumentOutOfRangeException(nameof(which), which, "No such fixture.")
            };
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            var n = 0;
            for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
                 i >= 0;
                 i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
                n++;
            return n;
        }
    }
}
