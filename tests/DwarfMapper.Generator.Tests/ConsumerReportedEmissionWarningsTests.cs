// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     Three shapes a consuming solution reported against 1.1.0-rc5, each a C# compiler warning emitted from
    ///     inside the generated file — where the consumer cannot suppress it: neither <c>#pragma</c> nor an
    ///     <c>.editorconfig</c> <c>[*.g.cs]</c> section reaches a diagnostic the compiler raises in a generated tree,
    ///     so the only consumer-side lever is a project-wide <c>NoWarn</c> that also hides genuine warnings in
    ///     hand-written code. Every one of them was a corpus hole: <see cref="GeneratedCodeIsWarningFreeTests" /> is
    ///     the right oracle and had run since it was written, but no cell in the combinatorial or fuzz schemas
    ///     declares a nullable-ELEMENT collection, binds a nullable string to a non-nullable CONSTRUCTOR parameter,
    ///     or maps an enum with an <c>[Obsolete]</c> member.
    ///     <para>
    ///         Each test is the consumer's shape reduced to the smallest class pair that reproduces the warning, and
    ///         each failed against the unfixed generator with the same diagnostic id the consumer saw.
    ///     </para>
    /// </summary>
    public class ConsumerReportedEmissionWarningsTests
    {
        private static void AssertWarningFree(string source, string label)
        {
            var warnings = GeneratorTestHarness.GeneratedCodeWarnings(source);
            Assert.True(warnings.Length == 0,
                label + ": the generated code carries " + warnings.Length.ToString(CultureInfo.InvariantCulture) +
                " compiler warning(s) the consumer cannot suppress:\n  " +
                string.Join("\n  ", warnings.Select(w => w.Id + " " + w.GetMessage(CultureInfo.InvariantCulture))) +
                "\n\n--- generated ---\n" + GeneratorTestHarness.Run(source, NullableContextOptions.Enable).GeneratedSource);
        }

        // ── 1. nullable-element collection through a synthesized object helper (CS8604) ─────────────────────
        // The consumer's ButtonSlots is `ProfileButtonData?[]` initialised to `new ProfileButtonData?[8]`, where a
        // null element means "empty slot" — the NORMAL case, not an edge. The collection helper was correctly typed
        // for nullable elements; the object helper it called per element takes a non-nullable parameter.

        private const string NullableElementArray = """
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public Child?[] Items { get; set; } = new Child?[8]; }
                public class Dst { public ChildDto?[] Items { get; set; } = new ChildDto?[8]; }
                [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
            }
            """;

        private const string NullableElementList = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public List<Child?> Items { get; set; } = new(); }
                public class Dst { public List<ChildDto?> Items { get; set; } = new(); }
                [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
            }
            """;

        [Fact]
        public void Nullable_element_array_maps_without_CS8604()
        {
            AssertWarningFree(NullableElementArray, "Child?[] -> ChildDto?[]");
        }

        [Fact]
        public void Nullable_element_list_maps_without_CS8604()
        {
            AssertWarningFree(NullableElementList, "List<Child?> -> List<ChildDto?>");
        }

        [Fact]
        public void Nullable_element_array_still_maps_null_slots_as_null_and_values_as_values()
        {
            // The runtime contract the annotation gap was hiding: a null slot stays null, a value maps.
            var generated = GeneratorAssert.CompilesClean(NullableElementArray, NullableContextOptions.Enable);
            Assert.Contains("__DwarfMapColl", generated, StringComparison.Ordinal);
        }

        // ── 2. constructor argument: nullable source into a non-nullable parameter (CS8604) ──────────────────
        // The member path null-forgives `Alias = source.Alias!` and reports DWARF070 against the DTO; the
        // constructor-argument path bound the same source member to `string alias` bare.

        private const string CtorArgFromNullableString = """
            using DwarfMapper;
            namespace T
            {
                public class Src { public string Format { get; set; } = ""; public string? Alias { get; set; } }
                public class Dst
                {
                    public Dst(string format, string alias) { Format = format; Alias = alias; }
                    public string Format { get; }
                    public string Alias { get; }
                }
                [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
            }
            """;

        [Fact]
        public void Nullable_string_bound_to_non_nullable_ctor_parameter_emits_no_CS8604()
        {
            AssertWarningFree(CtorArgFromNullableString, "string? Alias -> ctor(string alias)");
        }

        [Fact]
        public void Nullable_string_bound_to_non_nullable_ctor_parameter_reports_DWARF070_like_the_member_path()
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(CtorArgFromNullableString, NullableContextOptions.Enable);
            var d = Assert.Single(diagnostics, x => x.Id == "DWARF070");
            Assert.Contains("'Alias'", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        [Fact]
        public void Member_path_and_ctor_path_treat_the_same_nullable_source_identically()
        {
            // Positive control for the asymmetry the consumer saw: one source member, one method, two bindings.
            const string source = """
                using DwarfMapper;
                namespace T
                {
                    public class Src { public string? Alias { get; set; } }
                    public class Dst
                    {
                        public Dst(string alias) { Alias = alias; }
                        public string Alias { get; set; }
                    }
                    [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
                }
                """;
            AssertWarningFree(source, "string? Alias -> ctor(string alias) + settable Alias");
        }

        // ── 3. enum switches naming [Obsolete] members (CS0618) ──────────────────────────────────────────────
        // An exhaustive enum map must name every member; a domain enum keeps deprecated values for backward
        // compatibility, so the consumer cannot un-deprecate them to silence the generated file.

        private const string ObsoleteEnumToString = """
            using System;
            using DwarfMapper;
            namespace T
            {
                public enum Platform { YouTube, Twitch, [Obsolete("gone")] Trovo, Kick, [Obsolete("gone")] DLive }
                public class Src { public Platform P { get; set; } }
                public class Dst { public string P { get; set; } = ""; }
                [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
            }
            """;

        private const string ObsoleteStringToEnum = """
            using System;
            using DwarfMapper;
            namespace T
            {
                public enum Platform { YouTube, Twitch, [Obsolete("gone")] Trovo, Kick }
                public class Src { public string P { get; set; } = ""; }
                public class Dst { public Platform P { get; set; } }
                [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
            }
            """;

        private const string ObsoleteEnumToEnumByName = """
            using System;
            using DwarfMapper;
            namespace T
            {
                public enum A { X, [Obsolete("gone")] Y, Z }
                public enum B { X, [Obsolete("gone")] Y, Z }
                public class Src { public A P { get; set; } }
                public class Dst { public B P { get; set; } }
                [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
            }
            """;

        private const string ObsoleteFlagsByName = """
            using System;
            using DwarfMapper;
            namespace T
            {
                [Flags] public enum A { None = 0, X = 1, [Obsolete("gone")] Y = 2, Z = 4 }
                [Flags] public enum B { None = 0, X = 1, [Obsolete("gone")] Y = 2, Z = 4 }
                public class Src { public A P { get; set; } public string Q { get; set; } = ""; }
                public class Dst { public B P { get; set; } public A Q { get; set; } }
                [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
            }
            """;

        [Theory]
        [InlineData(ObsoleteEnumToString, "enum -> string")]
        [InlineData(ObsoleteStringToEnum, "string -> enum")]
        [InlineData(ObsoleteEnumToEnumByName, "enum -> enum by name")]
        [InlineData(ObsoleteFlagsByName, "[Flags] enum -> enum by name, string -> [Flags] enum")]
        public void Obsolete_enum_members_are_mapped_without_CS0618(string source, string label)
        {
            AssertWarningFree(source, label);
        }

        [Fact]
        public void Obsolete_enum_member_is_still_mapped_not_dropped()
        {
            // The remedy must keep the deprecated value mappable: dropping it would turn a legal (deprecated) input
            // into a runtime ArgumentOutOfRangeException. The emitted switch still names it.
            var generated = GeneratorAssert.CompilesClean(ObsoleteEnumToString, NullableContextOptions.Enable);
            Assert.Contains("Platform.Trovo => \"Trovo\"", generated, StringComparison.Ordinal);
            Assert.Contains("#pragma warning disable CS0612, CS0618", generated, StringComparison.Ordinal);
            Assert.Contains("#pragma warning restore CS0612, CS0618", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Error_level_obsolete_member_is_skipped_so_the_emission_still_compiles()
        {
            // [Obsolete(error: true)] is CS0619, an ERROR no pragma can lift: the member is unreferenceable by
            // anyone, so the map simply does not name it.
            const string source = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    public enum Platform { YouTube, [Obsolete("removed", true)] Trovo, Kick }
                    public class Src { public Platform P { get; set; } }
                    public class Dst { public string P { get; set; } = ""; }
                    [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
                }
                """;
            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);
            Assert.DoesNotContain("Platform.Trovo", generated, StringComparison.Ordinal);
            AssertWarningFree(source, "enum with an error-level [Obsolete] member -> string");
        }

        // ── the "1 of N sites" sweep: every other emitter that binds the same two shapes ─────────────────────
        // F11 in this same audit was a fix applied to one endpoint and not the other. These are the siblings.

        [Fact]
        public void Ctor_parameter_bound_through_explicit_MapProperty_from_nullable_string_emits_no_CS8604()
        {
            // The consumer's actual binding: different names, so it went through the [MapProperty] branch.
            const string source = """
                using DwarfMapper;
                namespace T
                {
                    public class Src { public string Format { get; set; } = ""; public string? Alias { get; set; } }
                    public class Dst
                    {
                        public Dst(string format, string fullCommandToExecute) { Format = format; FullCommandToExecute = fullCommandToExecute; }
                        public string Format { get; }
                        public string FullCommandToExecute { get; }
                    }
                    [DwarfMapper] public partial class M
                    {
                        [MapProperty("Alias", "fullCommandToExecute")]
                        public partial Dst Map(Src s);
                    }
                }
                """;
            AssertWarningFree(source, "string? Alias -> [MapProperty] ctor(string fullCommandToExecute)");
            var (diagnostics, _) = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);
            Assert.Contains(diagnostics, d => d.Id == "DWARF070");
        }

        [Fact]
        public void Projection_ctor_parameter_from_nullable_string_emits_no_CS8604()
        {
            const string source = """
                using System.Linq;
                using DwarfMapper;
                namespace T
                {
                    public class Src { public string Format { get; set; } = ""; public string? Alias { get; set; } }
                    public class Dst
                    {
                        public Dst(string format, string alias) { Format = format; Alias = alias; }
                        public string Format { get; }
                        public string Alias { get; }
                    }
                    [DwarfMapper] public partial class M
                    {
                        public partial Dst Map(Src s);
                        public partial IQueryable<Dst> Project(IQueryable<Src> q);
                    }
                }
                """;
            AssertWarningFree(source, "string? Alias -> ctor(string alias), .Map and .Project");
        }

        [Fact]
        public void Nullable_dictionary_value_maps_without_CS8604()
        {
            const string source = """
                using System.Collections.Generic;
                using DwarfMapper;
                namespace T
                {
                    public class Child { public int V { get; set; } }
                    public class ChildDto { public int V { get; set; } }
                    public class Src { public Dictionary<string, Child?> Items { get; set; } = new(); }
                    public class Dst { public Dictionary<string, ChildDto?> Items { get; set; } = new(); }
                    [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
                }
                """;
            AssertWarningFree(source, "Dictionary<string, Child?> -> Dictionary<string, ChildDto?>");
        }

        [Fact]
        public void Registry_MapTo_nullable_element_collection_and_nullable_member_emit_no_CS8604()
        {
            // The [MapTo] generator has its own element loop and object helpers; the oracle runs only the class
            // generator by default, so this shape was doubly invisible.
            const string source = """
                using DwarfMapper;
                namespace T
                {
                    [MapTo(typeof(ChildDto))] public class Child { public int V { get; set; } }
                    public class ChildDto { public int V { get; set; } }
                    [MapTo(typeof(Dst))] public class Src { public Child?[] Items { get; set; } = new Child?[8]; public Child? One { get; set; } }
                    public class Dst { public ChildDto?[] Items { get; set; } = new ChildDto?[8]; public ChildDto? One { get; set; } }
                }
                """;
            var warnings = GeneratorTestHarness.GeneratedCodeWarnings(source, includeRegistry: true);
            Assert.True(warnings.Length == 0,
                "[MapTo] Child?[] / Child? member: " + string.Join("\n  ", warnings.Select(w => w.Id + " " + w.GetMessage(CultureInfo.InvariantCulture))));
        }

        [Fact]
        public void Enum_without_obsolete_members_emits_no_pragma()
        {
            // No blanket suppression: the pragma appears only around a switch that has to name a deprecated member.
            const string source = """
                using DwarfMapper;
                namespace T
                {
                    public enum Platform { YouTube, Twitch }
                    public class Src { public Platform P { get; set; } }
                    public class Dst { public string P { get; set; } = ""; }
                    [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
                }
                """;
            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);
            Assert.DoesNotContain("CS0618", generated, StringComparison.Ordinal);
        }
    }
}
