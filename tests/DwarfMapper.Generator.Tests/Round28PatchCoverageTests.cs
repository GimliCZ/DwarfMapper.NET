// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     Round-28 patch coverage: every branch the consumer-warning fixes and the blit-proof near-miss arms added
    ///     that no existing cell reached. Each test is the smallest pair that executes one of those branches, so a
    ///     regression in the branch fails a NAMED test rather than an aggregate coverage floor.
    ///     <para>
    ///         The shapes are consumer-plausible, not synthetic: a self-recursive tree keyed by a class, a flags
    ///         enum with only its zero member, a target enum that deprecated a value the source still names, a
    ///         projection whose source does not fit the target enum's underlying type.
    ///     </para>
    /// </summary>
    public class Round28PatchCoverageTests
    {
        private static void AssertWarningFree(string source, string label)
        {
            var warnings = GeneratorTestHarness.GeneratedCodeWarnings(source);
            Assert.True(warnings.Length == 0,
                label + ": the generated code carries " + warnings.Length.ToString(CultureInfo.InvariantCulture) +
                " compiler warning(s):\n  " +
                string.Join("\n  ", warnings.Select(w => w.Id + " " + w.GetMessage(CultureInfo.InvariantCulture))) +
                "\n\n--- generated ---\n" + GeneratorTestHarness.Run(source, NullableContextOptions.Enable).GeneratedSource);
        }

        private static string Reason(IEnumerable<Diagnostic> diagnostics, string id)
        {
            var d = diagnostics.FirstOrDefault(x => x.Id == id);
            Assert.NotNull(d);
            return d!.GetMessage(CultureInfo.InvariantCulture);
        }

        // ── Projection: the three DWARF028 refusal sites and the DWARF070 dedupe ─────────────────────────────

        [Fact]
        public void Projection_refuses_a_narrowing_integral_to_enum_conversion()
        {
            const string source = """
                using System.Linq;
                using DwarfMapper;
                namespace T
                {
                    public enum Level { Low, High }
                    public class Src { public long Code { get; set; } }
                    public class Dst { public Level Code { get; set; } }
                    [DwarfMapper] public partial class M { public partial IQueryable<Dst> Project(IQueryable<Src> q); }
                }
                """;

            var reason = Reason(GeneratorAssert.Reports(source, "DWARF028"), "DWARF028");
            Assert.Contains("narrowing", reason, StringComparison.Ordinal);
        }

        [Fact]
        public void Projection_refuses_a_pair_with_no_translatable_conversion()
        {
            // A nested target with a private constructor is not a nested projection candidate, and nothing
            // else converts Inner to InnerDto: the resolver runs off the end of its arms.
            const string source = """
                using System.Linq;
                using DwarfMapper;
                namespace T
                {
                    public class Inner { public int V { get; set; } }
                    public class InnerDto { private InnerDto() { } public int V { get; } }
                    public class Src { public Inner Item { get; set; } }
                    public class Dst { public InnerDto Item { get; set; } }
                    [DwarfMapper(AutoNest = true)] public partial class M { public partial IQueryable<Dst> Project(IQueryable<Src> q); }
                }
                """;

            var reason = Reason(GeneratorAssert.Reports(source, "DWARF028"), "DWARF028");
            Assert.Contains("no translatable conversion", reason, StringComparison.Ordinal);
        }

        [Fact]
        public void Projection_refuses_a_nested_target_with_no_writable_member_and_no_usable_constructor()
        {
            // Public parameterless constructor, so the selector answers it; zero writable members, so member-init
            // cannot carry the target; no parameterised constructor to fall back to.
            const string source = """
                using System.Linq;
                using DwarfMapper;
                namespace T
                {
                    public class Inner { public int V { get; set; } }
                    public class InnerDto { public int V { get; } }
                    public class Src { public Inner Item { get; set; } }
                    public class Dst { public InnerDto Item { get; set; } }
                    [DwarfMapper(AutoNest = true)] public partial class M { public partial IQueryable<Dst> Project(IQueryable<Src> q); }
                }
                """;

            var reason = Reason(GeneratorAssert.Reports(source, "DWARF028"), "DWARF028");
            Assert.Contains("no usable constructor", reason, StringComparison.Ordinal);
        }

        [Fact]
        public void Projection_reports_DWARF070_once_per_source_member_when_two_targets_read_it()
        {
            // Two non-nullable targets fed by the same nullable source member: each binding forgives and would
            // report, and the consumer would read one defect twice. The report is keyed on the source member.
            const string source = """
                using System.Linq;
                using DwarfMapper;
                namespace T
                {
                    public class Src { public string? Alias { get; set; } }
                    public class Dst { public string Alias { get; set; } = ""; public string Display { get; set; } = ""; }
                    [DwarfMapper]
                    public partial class M
                    {
                        [MapProperty(nameof(Src.Alias), nameof(Dst.Display))]
                        public partial IQueryable<Dst> Project(IQueryable<Src> q);
                    }
                }
                """;

            var (diagnostics, _) = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);
            Assert.Single(diagnostics, d => d.Id == "DWARF070");
        }

        [Fact]
        public void Projection_MapValue_target_with_no_source_twin_is_not_a_shadow()
        {
            const string source = """
                using System.Linq;
                using DwarfMapper;
                namespace T
                {
                    public class Src { public int A { get; set; } }
                    public class Dst { public int A { get; set; } public int Extra { get; set; } }
                    [DwarfMapper]
                    public partial class M
                    {
                        [MapValue(nameof(Dst.Extra), 5)]
                        public partial IQueryable<Dst> Project(IQueryable<Src> q);
                    }
                }
                """;

            var generated = GeneratorAssert.CompilesClean(source);
            Assert.Contains("Extra = 5", generated, StringComparison.Ordinal);
        }

        // ── BlittableProof: near-miss arms for partial declarations and pointer fields ───────────────────────

        [Fact]
        public void Near_miss_names_instance_fields_spread_over_partial_declarations()
        {
            const string source = """
                using DwarfMapper;
                namespace T
                {
                    public partial struct A { public int X; }
                    public partial struct A { public int Y; }
                    public struct B { public int X; public int Y; }
                    public class Src { public A[] Items { get; set; } }
                    public class Dst { public B[] Items { get; set; } }
                    [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
                }
                """;

            var (diagnostics, _) = GeneratorTestHarness.Run(source);
            var reason = Reason(diagnostics, "DWARF100");
            Assert.Contains("more than one partial declaration", reason, StringComparison.Ordinal);
        }

        [Fact]
        public void Near_miss_names_a_pointer_field_against_a_fixed_buffer()
        {
            const string source = """
                using DwarfMapper;
                namespace T
                {
                    public unsafe struct A { public int* Data; }
                    public unsafe struct B { public fixed int Data[1]; }
                    public class Src { public A[] Items { get; set; } }
                    public class Dst { public B[] Items { get; set; } }
                    [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
                }
                """;

            var (diagnostics, _) = GeneratorTestHarness.Run(source, allowUnsafe: true);
            var reason = Reason(diagnostics, "DWARF100");
            Assert.Contains("a pointer", reason, StringComparison.Ordinal);
            Assert.Contains("a fixed buffer of 1", reason, StringComparison.Ordinal);
        }

        // ── DictionaryConverter: the self-recursive re-synthesis with nullable values ────────────────────────

        private const string RecursiveNullableValueDictionary = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Node { public int V { get; set; } public Dictionary<string, Node?> Kids { get; set; } = new(); }
                public class NodeDto { public int V { get; set; } public Dictionary<string, NodeDto?> Kids { get; set; } = new(); }
                [DwarfMapper] public partial class M { public partial NodeDto Map(Node s); }
            }
            """;

        private const string RecursiveNullableValueDictionaryPreserve = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Node { public int V { get; set; } public Dictionary<string, Node?> Kids { get; set; } = new(); }
                public class NodeDto { public int V { get; set; } public Dictionary<string, NodeDto?> Kids { get; set; } = new(); }
                [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                public partial class M { public partial NodeDto Map(Node s); }
            }
            """;

        [Theory]
        [InlineData(RecursiveNullableValueDictionary, "recursive Dictionary<string, Node?>")]
        [InlineData(RecursiveNullableValueDictionaryPreserve, "recursive Dictionary<string, Node?> under Preserve")]
        public void Recursive_dictionary_with_nullable_values_is_warning_free(string source, string label)
        {
            GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);
            AssertWarningFree(source, label);
        }

        // ── EnumConverter: target-only obsolete, the bare [Obsolete] form, a flags enum with only its zero ──

        [Fact]
        public void Enum_map_guards_a_member_only_the_target_deprecated()
        {
            const string source = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    public enum Src { A, B }
                    public enum Tgt { A, [Obsolete("use A")] B }
                    public class S { public Src E { get; set; } }
                    public class D { public Tgt E { get; set; } }
                    [DwarfMapper] public partial class M { public partial D Map(S s); }
                }
                """;

            var generated = GeneratorAssert.CompilesClean(source);
            Assert.Contains("#pragma warning disable CS0612, CS0618", generated, StringComparison.Ordinal);
            AssertWarningFree(source, "target-only [Obsolete] enum member");
        }

        [Fact]
        public void Enum_map_guards_CS0612_for_the_bare_Obsolete_form()
        {
            const string source = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    public enum Src { A, [Obsolete] B }
                    public enum Tgt { A, B }
                    public class S { public Src E { get; set; } }
                    public class D { public Tgt E { get; set; } }
                    [DwarfMapper] public partial class M { public partial D Map(S s); }
                }
                """;

            var generated = GeneratorAssert.CompilesClean(source);
            // Bare [Obsolete] has no error flag: the member stays mapped, under a guard that names CS0612 — the id
            // the compiler uses for the message-less form, which a CS0618-only guard let through.
            Assert.Contains("global::T.Src.B => global::T.Tgt.B", generated, StringComparison.Ordinal);
            Assert.Contains("#pragma warning disable CS0612, CS0618", generated, StringComparison.Ordinal);
            AssertWarningFree(source, "bare [Obsolete] enum member");
        }

        [Fact]
        public void Flags_enum_map_guards_a_deprecated_source_flag()
        {
            const string source = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    [Flags] public enum SrcF { Read = 1, [Obsolete("use Read")] Legacy = 2 }
                    [Flags] public enum TgtF { Read = 1, Legacy = 2 }
                    public class S { public SrcF E { get; set; } }
                    public class D { public TgtF E { get; set; } }
                    [DwarfMapper] public partial class M { public partial D Map(S s); }
                }
                """;

            var generated = GeneratorAssert.CompilesClean(source);
            Assert.Contains("#pragma warning disable CS0612, CS0618", generated, StringComparison.Ordinal);
            AssertWarningFree(source, "[Flags] enum with a deprecated source flag");
        }

        [Fact]
        public void Enum_map_reads_an_explicit_error_false_Obsolete_as_warning_level()
        {
            const string source = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    public enum Src { A, [Obsolete("soon", false)] B }
                    public enum Tgt { A, B }
                    public class S { public Src E { get; set; } }
                    public class D { public Tgt E { get; set; } }
                    [DwarfMapper] public partial class M { public partial D Map(S s); }
                }
                """;

            var generated = GeneratorAssert.CompilesClean(source);
            Assert.Contains("global::T.Src.B => global::T.Tgt.B", generated, StringComparison.Ordinal);
            AssertWarningFree(source, "[Obsolete(message, error: false)] enum member");
        }

        [Fact]
        public void Empty_flags_enum_parses_from_string_by_refusing_every_name()
        {
            // No declared member, so the name switch has no arm and the throw stands alone, without an `else`.
            const string source = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    [Flags] public enum Perm { }
                    public class S { public string P { get; set; } }
                    public class D { public Perm P { get; set; } }
                    [DwarfMapper] public partial class M { public partial D Map(S s); }
                }
                """;

            var generated = GeneratorAssert.CompilesClean(source);
            Assert.Contains("throw new global::System.ArgumentOutOfRangeException(nameof(v), v, \"Unrecognized enum name\")", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("else throw", generated, StringComparison.Ordinal);
        }

        // ── MapperExtractor.Conversions: a member whose class IS a collection is not an object pair ─────────

        [Fact]
        public void A_source_member_whose_class_derives_from_a_collection_is_not_object_mapped()
        {
            const string source = """
                using System.Collections.Generic;
                using DwarfMapper;
                namespace T
                {
                    public class Bag : List<int> { }
                    public class Plain { public int Count { get; set; } }
                    public class Src { public Bag Items { get; set; } }
                    public class Dst { public Plain Items { get; set; } }
                    [DwarfMapper(AutoNest = true)] public partial class M { public partial Dst Map(Src s); }
                }
                """;

            // Refused as an unconvertible pair rather than auto-nested by its Count property.
            GeneratorAssert.Reports(source, "DWARF005");
        }

        [Fact]
        public void A_target_member_whose_class_derives_from_a_collection_is_refused_as_a_collection_target()
        {
            const string source = """
                using System.Collections.Generic;
                using DwarfMapper;
                namespace T
                {
                    public class Bag : List<int> { }
                    public class Plain { public int Count { get; set; } }
                    public class Src { public Plain Items { get; set; } }
                    public class Dst { public Bag Items { get; set; } }
                    [DwarfMapper(AutoNest = true)] public partial class M { public partial Dst Map(Src s); }
                }
                """;

            // The collection arm claims an enumerable TARGET before the object-pair predicate is asked.
            GeneratorAssert.Reports(source, "DWARF027");
        }

        // ── Conversions.Arms: a recursive dictionary keyed AND valued by mappable classes ────────────────────

        [Fact]
        public void Recursive_dictionary_keyed_by_a_mapped_class_re_resolves_both_key_and_value_helpers()
        {
            // Default (None) mode: the value recursion upgrades the dictionary helper to a context-threaded one,
            // and the upgrade re-resolves the KEY helper too, so a class key rides the same re-synthesis.
            const string source = """
                using System.Collections.Generic;
                using DwarfMapper;
                namespace T
                {
                    public class Key { public int Id { get; set; } }
                    public class KeyDto { public int Id { get; set; } }
                    public class Node { public int V { get; set; } public Dictionary<Key, Node> Kids { get; set; } = new(); }
                    public class NodeDto { public int V { get; set; } public Dictionary<KeyDto, NodeDto> Kids { get; set; } = new(); }
                    [DwarfMapper] public partial class M { public partial NodeDto Map(Node s); }
                }
                """;

            var generated = GeneratorAssert.CompilesClean(source);
            Assert.Contains("__kv.Key", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Dictionary_value_from_a_nullable_enum_into_a_converted_enum_throws_on_a_null_entry()
        {
            // Nullable<E1> values into non-nullable E2: the unwrap resolves the by-name enum conversion for the
            // underlying type and, under the default null strategy, a null entry throws rather than defaults.
            const string source = """
                using System.Collections.Generic;
                using DwarfMapper;
                namespace T
                {
                    public enum E1 { A, B }
                    public enum E2 { A, B }
                    public class Src { public Dictionary<string, E1?> Tags { get; set; } = new(); }
                    public class Dst { public Dictionary<string, E2> Tags { get; set; } = new(); }
                    [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
                }
                """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);
            Assert.Contains("Dictionary entry was null", generated, StringComparison.Ordinal);
            AssertWarningFree(source, "Dictionary<string, E1?> into Dictionary<string, E2>");
        }

        // ── CollectionConverter: nullable elements through the in-place (recursive) and Preserve syntheses ──

        [Fact]
        public void Self_recursive_collection_with_nullable_elements_is_warning_free()
        {
            // A tree whose children list admits null slots: the collection helper is re-synthesized in place
            // once the recursion is discovered, and that second synthesis has to forgive the element too.
            const string source = """
                using System.Collections.Generic;
                using DwarfMapper;
                namespace T
                {
                    public class Node { public int V { get; set; } public List<Node?> Kids { get; set; } = new(); }
                    public class NodeDto { public int V { get; set; } public List<NodeDto?> Kids { get; set; } = new(); }
                    [DwarfMapper] public partial class M { public partial NodeDto Map(Node s); }
                }
                """;

            GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);
            AssertWarningFree(source, "recursive List<Node?>");
        }

        [Fact]
        public void Nullable_element_collection_under_Preserve_is_warning_free()
        {
            const string source = """
                using DwarfMapper;
                namespace T
                {
                    public class Child { public int V { get; set; } }
                    public class ChildDto { public int V { get; set; } }
                    public class Src { public Child?[] Items { get; set; } = new Child?[8]; }
                    public class Dst { public ChildDto?[] Items { get; set; } = new ChildDto?[8]; }
                    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                    public partial class M { public partial Dst Map(Src s); }
                }
                """;

            GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);
            AssertWarningFree(source, "Child?[] under Preserve");
        }

        // ── Members.Phases: the shadow lookup's disowned candidate, under the source's REAL name ─────────────

        [Fact]
        public void MapValue_over_a_flexibly_matched_source_is_no_shadow_once_that_source_is_disowned()
        {
            // Under Flexible, `user_name` is the member auto-match would have bound to UserName; [MapIgnoreSource]
            // names it by its real spelling, and the shadow lookup must honour that spelling.
            const string source = """
                using DwarfMapper;
                namespace T
                {
                    public class Src { public string user_name { get; set; } public int A { get; set; } }
                    public class Dst { public string UserName { get; set; } public int A { get; set; } }
                    [DwarfMapper(NameConvention = NameConvention.Flexible)]
                    public partial class M
                    {
                        [MapIgnoreSource(nameof(Src.user_name))]
                        [MapValue(nameof(Dst.UserName), "fixed")]
                        public partial Dst Map(Src s);
                    }
                }
                """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(source);
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF064");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("UserName = \"fixed\"", generated, StringComparison.Ordinal);
        }
    }
}
