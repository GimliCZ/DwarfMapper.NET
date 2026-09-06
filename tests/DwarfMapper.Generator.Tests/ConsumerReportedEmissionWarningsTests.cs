// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Reflection;
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

        // ── 1b. nullable-VALUE-type element collection through the bounds-check-elision fast path (CS8629) ──
        // Round 29 T0.2d. `EmitArray`'s array→array fast path used to substitute the loop indexer textually into
        // the shared element expression, so a NullableProject element became
        // `src[__i].HasValue ? conv(src[__i].Value) : null` — TWO independent indexer reads, and the C# nullable
        // flow analysis does not carry the null-state of the first into the second: CS8629 on `.Value`, inside a
        // `.g.cs`, in every consumer mapping a `P?[]`. Nobody had ever declared a nullable-VALUE-type element in
        // the corpus (the round-28 holes were all nullable REFERENCE elements), so no oracle saw it.

        private const string NullableValueElementArray = """
            using DwarfMapper;
            namespace T
            {
                public struct Point { public int X { get; set; } public int Y { get; set; } }
                public struct PointDto { public int X { get; set; } public int Y { get; set; } }
                public class Src { public Point?[] Items { get; set; } = new Point?[4]; }
                public class Dst { public PointDto?[] Items { get; set; } = new PointDto?[4]; }
                [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
            }
            """;

        private const string NullableValueElementList = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public struct Point { public int X { get; set; } public int Y { get; set; } }
                public struct PointDto { public int X { get; set; } public int Y { get; set; } }
                public class Src { public List<Point?> Items { get; set; } = new(); }
                public class Dst { public List<PointDto?> Items { get; set; } = new(); }
                [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
            }
            """;

        [Fact]
        public void Nullable_value_element_array_maps_without_CS8629()
        {
            AssertWarningFree(NullableValueElementArray, "Point?[] -> PointDto?[]");
        }

        [Fact]
        public void Nullable_value_element_list_maps_without_CS8629()
        {
            AssertWarningFree(NullableValueElementList, "List<Point?> -> List<PointDto?>");
        }

        [Fact]
        public void Nullable_value_element_array_keeps_the_bounds_check_elision_loop()
        {
            // The fix binds the element once instead of indexing twice; the `for (int __i = 0; __i < src.Length; …)`
            // shape the JIT needs to elide BOTH bounds checks must survive it (that is the whole point of the arm).
            var generated = GeneratorAssert.CompilesClean(NullableValueElementArray, NullableContextOptions.Enable);
            Assert.Contains("for (int __i = 0; __i < src.Length; __i++) { var __item = src[__i];", generated, StringComparison.Ordinal);
        }

        // The OTHER arm the one binding rule covers: NullableProjectRef, `__item is null ? null : conv(__item)`.
        // Reaching it needs a possibly-null REFERENCE source element mapped to a Nullable<STRUCT> target element
        // (MapperExtractor.Conversions.Arms.HandleTargetNullableComposition): the synthesized helper returns a
        // value type and so has no way to answer "null", which is why the CALL SITE has to test first.
        //
        // `Child?[] → ChildDto?[]` — the obvious guess, and the shape the round-28 tests above use — does NOT
        // reach it: with a reference TARGET the helper null-guards internally, so that pair takes the plain arm
        // with a null-forgiving `!` (`conv(src[__i]!)`) and reads the element once. Verified against the emitted
        // text before this test was written rather than assumed, because a pin aimed at the wrong arm is worse
        // than no pin: it passes, and it guards nothing.
        private const string NullableRefElementToNullableStructArray = """
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public struct PointDto { public int V { get; set; } }
                public class Src { public Child?[] Items { get; set; } = new Child?[8]; }
                public class Dst { public PointDto?[] Items { get; set; } = new PointDto?[8]; }
                [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
            }
            """;

        [Fact]
        public void Nullable_reference_element_to_nullable_struct_array_keeps_the_bounds_check_elision_loop()
        {
            // Round 29 T0.2d review fix round 1. The struct arm (NullableProject) had a shape pin and this arm did
            // not, so it could have regressed to the foreach form — losing the store-index bounds-check elision the
            // arm exists for — with every warning-free and runtime test still green, because both forms map
            // identically and neither warns. A performance property no test names is one that regresses quietly.
            var generated = GeneratorAssert.CompilesClean(NullableRefElementToNullableStructArray, NullableContextOptions.Enable);

            // The elision loop, with the element bound ONCE — the whole point of the fix on this arm too.
            Assert.Contains("for (int __i = 0; __i < src.Length; __i++) { var __item = src[__i];", generated, StringComparison.Ordinal);
            Assert.Contains("__item is null ? null :", generated, StringComparison.Ordinal);

            // And not the foreach form, whose separate post-incremented write index the JIT cannot prove in bounds.
            Assert.DoesNotContain("foreach (var __item in src)", generated, StringComparison.Ordinal);

            // The element is read from the array exactly once: a second `src[__i]` inside the conditional is the
            // defect this task removed, and on THIS arm the null-forgiving `!` would hide it from the compiler.
            Assert.DoesNotContain("src[__i] is null", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Nullable_reference_element_to_nullable_struct_array_maps_null_slots_and_values()
        {
            // The behaviour the shape pin above is protecting, so a future reader can see the pin is not cosmetic.
            AssertWarningFree(NullableRefElementToNullableStructArray, "Child?[] -> PointDto?[]");
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

        // ── 4. the payload edge of a USER-DECLARED map method (CS8604) ─────────────────────────────────────
        // Round 29 task 2.6, found by the representative corpus. A nested edge whose converter is a method the
        // USER declared — which is what [GenerateWrapperMap] produces for every payload, because it expands over
        // exactly the pairs already declared as maps — got neither the null-forgiving '!' (that path is gated on a
        // NON-nullable destination, via ForgiveNestedNullableArg/DWARF070) nor the null-preserving lift (that one
        // only ever fired for a Nullable<U> VALUE destination). Nullable reference in, nullable reference out fell
        // between the two and was emitted bare: CS8604 in the consumer's .g.cs. Not wrapper-specific — the first
        // case below has no wrapper in it at all.

        private const string NullableNestedMemberViaDeclaredMap = """
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public Child? Inner { get; set; } }
                public class Dst { public ChildDto? Inner { get; set; } }
                [DwarfMapper] public partial class M { public partial Dst Map(Src s); public partial ChildDto ToDto(Child c); }
            }
            """;

        private const string NullableElementViaDeclaredMap = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public List<Child?> Items { get; set; } = new(); }
                public class Dst { public List<ChildDto?> Items { get; set; } = new(); }
                [DwarfMapper] public partial class M { public partial Dst Map(Src s); public partial ChildDto ToDto(Child c); }
            }
            """;

        private const string NullableWrapperPayload = """
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public sealed class Result<T> where T : class
                {
                    public Result(T? value, string? error) { Value = value; Error = error; }
                    public T? Value { get; }
                    public string? Error { get; }
                }
                [DwarfMapper]
                [GenerateWrapperMap(typeof(Result<>))]
                [GenerateMap<Child, ChildDto>]
                public partial class M { public partial ChildDto ToDto(Child c); }
            }
            """;

        private const string NonNullableWrapperPayload = """
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public sealed class Result<T> where T : class
                {
                    public Result(T value, string? error) { Value = value; Error = error; }
                    public T Value { get; }
                    public string? Error { get; }
                    public static Result<T> Fail(string error) => new Result<T>(default!, error);
                }
                [DwarfMapper]
                [GenerateWrapperMap(typeof(Result<>))]
                [GenerateMap<Child, ChildDto>]
                public partial class M { public partial ChildDto ToDto(Child c); }
            }
            """;

        [Fact]
        public void Nullable_nested_member_through_a_declared_map_emits_no_CS8604()
        {
            AssertWarningFree(NullableNestedMemberViaDeclaredMap, "Child? Inner -> ChildDto? Inner via the user's own ToDto");
        }

        [Fact]
        public void Nullable_collection_element_through_a_declared_map_emits_no_CS8604()
        {
            AssertWarningFree(NullableElementViaDeclaredMap, "List<Child?> -> List<ChildDto?> via the user's own ToDto");
        }

        [Fact]
        public void Nullable_wrapper_payload_emits_no_CS8604()
        {
            AssertWarningFree(NullableWrapperPayload, "[GenerateWrapperMap] Result<T> with a nullable payload");
        }

        [Fact]
        public void Non_nullable_wrapper_payload_emits_no_warning_either()
        {
            // The other dominant Result<T> spelling. It never warned — it threw at run time instead — so this is
            // the guard that the fix for that arm did not trade a silent throw for a loud compiler warning.
            AssertWarningFree(NonNullableWrapperPayload, "[GenerateWrapperMap] Result<T> with a non-nullable payload");
        }

        [Fact]
        public void The_nullable_payload_edge_preserves_null_rather_than_forcing_it_through_the_map()
        {
            var generated = GeneratorAssert.CompilesClean(NullableWrapperPayload, NullableContextOptions.Enable);
            Assert.Contains("src.Value is null ? null : ToDto(src.Value)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void The_non_nullable_payload_edge_preserves_null_with_the_forgiving_arm()
        {
            // Both annotations claim the payload cannot be null; `Fail` stores default! and makes one of them
            // false. The lift is the same, and the `!` is what keeps CS8601 out of a file the consumer cannot edit.
            var generated = GeneratorAssert.CompilesClean(NonNullableWrapperPayload, NullableContextOptions.Enable);
            Assert.Contains("src.Value is null ? null! : ToDto(src.Value)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_nullable_source_into_a_non_nullable_target_still_forgives_the_argument_and_reports_DWARF070()
        {
            // The third arm, deliberately NOT lifted: the destination cannot hold the null and DWARF070 already
            // names that against the user's own DTO. Silently lifting it here would swallow the one case the
            // mapper is supposed to be loud about, so this pins the pre-existing emission byte for byte.
            const string source = """
                using DwarfMapper;
                namespace T
                {
                    public class Child { public int V { get; set; } }
                    public class ChildDto { public int V { get; set; } }
                    public class Src { public Child? Inner { get; set; } }
                    public class Dst { public ChildDto Inner { get; set; } = new(); }
                    [DwarfMapper] public partial class M { public partial Dst Map(Src s); public partial ChildDto ToDto(Child c); }
                }
                """;
            var run = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);
            Assert.Contains("Inner = ToDto(s.Inner!)", run.GeneratedSource, StringComparison.Ordinal);
            Assert.Contains(run.Diagnostics, d => d.Id == "DWARF070");
        }

        // -- 5. the Phase-5 EXTRA PARAMETER dropped its nullability twice (CS8611, then CS8604/CS8601/CS0266) --
        // Round 29 task 2.7, found by task 2.6's audit of all twenty TryResolveConversion call sites.
        //
        // Two defects on one path, and the second is only visible once the first is fixed:
        //   * the signature fragment for an extra parameter was formatted with
        //     SymbolDisplayFormat.FullyQualifiedFormat, which DROPS the '?' off a nullable reference. The
        //     implementing half of the user's partial therefore declared `Child inner` against their
        //     `Child? inner` — CS8611.
        //   * the extra-parameter phase passed `out _` for the null-handling decision and handed the emitter a
        //     finished ValueExpression, which short-circuits AppendValueExpression before any null handling is
        //     read. With the '?' restored, that bare emission is CS8604 / CS8601 / CS0266 depending on the arm.
        // Every one of them lands inside the consumer's .g.cs, where no pragma of theirs reaches it.

        private const string NullableExtraParamIdentity = """
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class Src { public int Id { get; set; } }
                public class Dst { public int Id { get; set; } public Child? Inner { get; set; } }
                [DwarfMapper] public partial class M { public partial Dst Map(Src s, Child? inner); }
            }
            """;

        private const string NullableExtraParamViaDeclaredMap = """
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public int Id { get; set; } }
                public class Dst { public int Id { get; set; } public ChildDto? Inner { get; set; } }
                [DwarfMapper] public partial class M
                {
                    public partial Dst Map(Src s, Child? inner);
                    public partial ChildDto ToDto(Child c);
                }
            }
            """;

        private const string NullableExtraParamIntoNonNullableViaDeclaredMap = """
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public int Id { get; set; } }
                public class Dst { public int Id { get; set; } public ChildDto Inner { get; set; } = new(); }
                [DwarfMapper] public partial class M
                {
                    public partial Dst Map(Src s, Child? inner);
                    public partial ChildDto ToDto(Child c);
                }
            }
            """;

        private const string NullableExtraParamIntoNonNullableRef = """
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class Src { public int Id { get; set; } }
                public class Dst { public int Id { get; set; } public Child Inner { get; set; } = new(); }
                [DwarfMapper] public partial class M { public partial Dst Map(Src s, Child? inner); }
            }
            """;

        private const string NullableValueExtraParamIntoNonNullableValue = """
            using DwarfMapper;
            namespace T
            {
                public class Src { public int Id { get; set; } }
                public class Dst { public int Id { get; set; } public int Count { get; set; } }
                [DwarfMapper] public partial class M { public partial Dst Map(Src s, int? count); }
            }
            """;

        [Fact]
        public void Nullable_extra_parameter_keeps_its_annotation_in_the_emitted_signature()
        {
            // Defect 1 on its own: the identity assign needs no null handling at all, so the ONLY thing that can
            // warn here is the signature. RED before the fix with CS8611 and nothing else.
            AssertWarningFree(NullableExtraParamIdentity, "Map(Src s, Child? inner) -> Child? Inner");
        }

        [Fact]
        public void The_emitted_partial_declares_the_extra_parameter_exactly_as_the_user_did()
        {
            var generated = GeneratorAssert.CompilesClean(NullableExtraParamIdentity, NullableContextOptions.Enable);
            Assert.Contains("Map(global::T.Src s, global::T.Child? inner)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Nullable_extra_parameter_through_a_declared_map_emits_no_CS8604()
        {
            AssertWarningFree(NullableExtraParamViaDeclaredMap, "Child? inner -> ChildDto? Inner via the user's own ToDto");
        }

        [Fact]
        public void Nullable_extra_parameter_into_a_non_nullable_target_through_a_declared_map_emits_no_CS8604()
        {
            AssertWarningFree(NullableExtraParamIntoNonNullableViaDeclaredMap, "Child? inner -> ChildDto Inner via the user's own ToDto");
        }

        [Fact]
        public void Nullable_extra_parameter_raw_assigned_into_a_non_nullable_target_emits_no_CS8601()
        {
            AssertWarningFree(NullableExtraParamIntoNonNullableRef, "Child? inner -> Child Inner, raw assign");
        }

        [Fact]
        public void Nullable_value_extra_parameter_into_a_non_nullable_value_target_emits_no_CS0266()
        {
            // The worst arm: not a warning but a hard compile ERROR in the .g.cs, and pre-existing since Phase 5
            // was written — `int? count` was assigned straight into an `int` member. It survived because
            // Nullable<T> is a TYPE rather than an annotation, so it needed no nullable context to reproduce and
            // still nothing in the corpus declared it.
            AssertWarningFree(NullableValueExtraParamIntoNonNullableValue, "int? count -> int Count");
        }

        [Fact]
        public void The_extra_parameter_is_answered_exactly_as_the_equivalent_source_member_is()
        {
            // The point of the fix, and the thing a per-site re-spelling would drift away from: an extra
            // parameter is a place the value is READ FROM, not a different kind of edge. Each arm below is the
            // emission the same shape gets when it arrives as a source member instead — the lift, the forgiven
            // converter argument, the forgiven raw assign, and the nullable-value unwrap.
            var lifted = GeneratorAssert.CompilesClean(NullableExtraParamViaDeclaredMap, NullableContextOptions.Enable);
            Assert.Contains("Inner = inner is null ? null : ToDto(inner)", lifted, StringComparison.Ordinal);

            var forgivenArg = GeneratorTestHarness.Run(NullableExtraParamIntoNonNullableViaDeclaredMap, NullableContextOptions.Enable);
            Assert.Contains("Inner = ToDto(inner!)", forgivenArg.GeneratedSource, StringComparison.Ordinal);
            Assert.Contains(forgivenArg.Diagnostics, d => d.Id == "DWARF070");

            var forgivenAssign = GeneratorTestHarness.Run(NullableExtraParamIntoNonNullableRef, NullableContextOptions.Enable);
            Assert.Contains("Inner = inner!", forgivenAssign.GeneratedSource, StringComparison.Ordinal);
            Assert.Contains(forgivenAssign.Diagnostics, d => d.Id == "DWARF070");

            var unwrapped = GeneratorAssert.CompilesClean(NullableValueExtraParamIntoNonNullableValue, NullableContextOptions.Enable);
            Assert.Contains("Count = count ?? throw new global::System.InvalidOperationException(\"Mapping parameter 'count' was null\")", unwrapped, StringComparison.Ordinal);
        }

        private const string NullableExtraParamViaSynthesizedMap = """
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public int Id { get; set; } }
                public class Dst { public int Id { get; set; } public ChildDto? Auto { get; set; } public ChildDto AutoStrict { get; set; } = new(); }
                [DwarfMapper] public partial class M { public partial Dst Map(Src s, Child? auto, Child? autoStrict); }
            }
            """;

        [Fact]
        public void Nullable_extra_parameter_through_a_synthesized_map_is_forgiven_without_DWARF070()
        {
            // The arm the other tests do not reach, and the one a consumer meets FIRST: no ToDto on the class,
            // so the pair auto-nests to a __DwarfMap_Obj_ helper. That helper opens with
            // `if (s is null) return null!;`, so the emitter forgives the argument through its IsSynthesized
            // clause rather than ConverterParamIsNonNullableRef — null in, null out, and DWARF070 stays SILENT
            // even for the non-nullable destination, because nothing here can store a null it forbids.
            AssertWarningFree(NullableExtraParamViaSynthesizedMap, "Child? auto -> ChildDto?/ChildDto via an auto-nested helper");

            var run = GeneratorTestHarness.Run(NullableExtraParamViaSynthesizedMap, NullableContextOptions.Enable);
            Assert.Contains("(auto!)", run.GeneratedSource, StringComparison.Ordinal);
            Assert.Contains("(autoStrict!)", run.GeneratedSource, StringComparison.Ordinal);
            Assert.DoesNotContain(run.Diagnostics, d => d.Id == "DWARF070");
        }

        [Fact]
        public void DWARF070_names_the_extra_parameter_rather_than_an_empty_string()
        {
            // The member-side DWARF070 report prints MemberMap.SourceName, which an extra parameter does not have
            // (it is not a member of the source type). Without the fallback the consumer read "Source member ''".
            var run = GeneratorTestHarness.Run(NullableExtraParamIntoNonNullableRef, NullableContextOptions.Enable);
            var d = Assert.Single(run.Diagnostics.Where(x => x.Id == "DWARF070"));
            Assert.Contains("'inner'", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        // -- 6. an extra parameter named after a C# keyword did not PARSE (CS1001 and 26 more) --------------
        // Round 29 task 2.7 fix round 1. Both the signature fragment and the value expression were built from
        // the raw ISymbol.Name, which Roslyn hands over WITHOUT the `@` — the escape is syntax, not part of the
        // name. `Map(Src s, int @class)` therefore emitted `int class` and `Class = class`, and the consumer's
        // .g.cs stopped being C# at all: a 27-diagnostic parse cascade ending in CS0111/CS0756 against their
        // own partial. Identifiers already existed for exactly this (a DTO member called @class, R18-29); the
        // extra-parameter path was simply never routed through it.

        private const string KeywordNamedExtraParam = """
            using DwarfMapper;
            namespace T
            {
                public class Src { public int Id { get; set; } }
                public class Dst { public int Id { get; set; } public int Class { get; set; } }
                [DwarfMapper] public partial class M { public partial Dst Map(Src s, int @class); }
            }
            """;

        [Fact]
        public void Keyword_named_extra_parameter_still_produces_parsable_code()
        {
            AssertWarningFree(KeywordNamedExtraParam, "Map(Src s, int @class)");

            var generated = GeneratorAssert.CompilesClean(KeywordNamedExtraParam, NullableContextOptions.Enable);
            Assert.Contains("Map(global::T.Src s, int @class)", generated, StringComparison.Ordinal);
            Assert.Contains("Class = @class", generated, StringComparison.Ordinal);
        }

        // -- 7. the SOURCE parameter of a user-declared partial dropped its '?' too (CS8611) ----------------
        // Round 29 task 2.7 fix round 1, item 2. The sibling of the extra-parameter defect: `ParameterTypeFullName`
        // is built with the same annotation-stripping FullyQualifiedFormat and re-emitted as the implementing
        // half of the user's partial, so `partial Dst Map(Src? s)` was implemented as `Map(global::T.Src s)`.
        //
        // The obvious fix — annotate that string in place, as the extra-parameter fragment was — is WRONG, and
        // was measured wrong rather than argued wrong: ParameterTypeFullName is also the operand of `typeof(…)`
        // in the ambient registration, so annotating it turns CS8611 into CS8639 ("the typeof operator cannot be
        // used on a nullable reference type"), and the same move on the return string produces CS8628 ("cannot
        // use a nullable reference type in object creation") on the `new T` the emitter writes from it. Hence a
        // SEPARATE ParameterTypeSignature, read only where a signature must match a user's own declaration.

        private const string NullableSourceParameter = """
            using DwarfMapper;
            namespace T
            {
                public class Src { public int Id { get; set; } }
                public class Dst { public int Id { get; set; } }
                [DwarfMapper] public partial class M { public partial Dst Map(Src? s); }
            }
            """;

        private const string NullableSourceParameterOnUpdateInto = """
            using DwarfMapper;
            namespace T
            {
                public class Src { public int Id { get; set; } }
                public class Dst { public int Id { get; set; } }
                [DwarfMapper] public partial class M { public partial void Update(Src? s, Dst d); }
            }
            """;

        [Fact]
        public void Nullable_source_parameter_keeps_its_annotation_in_the_emitted_signature()
        {
            AssertWarningFree(NullableSourceParameter, "partial Dst Map(Src? s)");

            var generated = GeneratorAssert.CompilesClean(NullableSourceParameter, NullableContextOptions.Enable);
            Assert.Contains("Map(global::T.Src? s)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Nullable_source_parameter_on_an_update_into_keeps_its_annotation()
        {
            // A second signature-emitting branch, and the reason the fix is a model field rather than an edit at
            // one emitter site: update-into writes its own signature, as do the async-stream and span maps.
            AssertWarningFree(NullableSourceParameterOnUpdateInto, "partial void Update(Src? s, Dst d)");

            var generated = GeneratorAssert.CompilesClean(NullableSourceParameterOnUpdateInto, NullableContextOptions.Enable);
            Assert.Contains("Update(global::T.Src? s, global::T.Dst d)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void The_typeof_operand_stays_annotation_free_when_the_signature_gains_one()
        {
            // The guard on the whole design, and it PASSES RED by design — its job is to stay green across the
            // change, not to fail before it. The ambient registration writes `typeof(ParameterTypeFullName)`,
            // which is CS8639 on a nullable reference type — so the two strings must stay different, and this
            // fails the moment someone "simplifies" ParameterTypeSignature away by annotating the FullName.
            var run = GeneratorTestHarness.RunAll(NullableSourceParameter, NullableContextOptions.Enable);
            Assert.Contains("typeof(global::T.Src)", run.GeneratedSource, StringComparison.Ordinal);
            Assert.DoesNotContain("typeof(global::T.Src?)", run.GeneratedSource, StringComparison.Ordinal);
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

        // -- 8. a nullable RETURN type, which is three different defects wearing one name ---------------------
        // Round 29 task 2.8, defect A. Task 2.7 recorded this and deliberately did not fix it, because the
        // obvious patch is measurably wrong: ReturnTypeFullName is the pair's identity (the `new` target, the
        // typeof operand, the registry key), and annotating it in place trades CS8611 for CS8628. What the
        // shapes below measure is that the defect is not one thing:
        //
        //   * a GENERIC nullable return (`List<Dst?>`) is CS8819 on the partial itself, plus CS8619 on the value
        //     the collection helper returns — a signature defect, and the signature field answers it;
        //   * a SCALAR nullable return (`Dst?`) is SILENT on the partial (a stricter return is always safe) and
        //     lands 8 diagnostics in the two AGGREGATE files instead, which no signature field can reach;
        //   * the update-into DESTINATION parameter is spelled from the same string, so `void Update(Src s,
        //     Dst? d)` dropped its '?' the way task 2.7's source parameter did — CS8611, one parameter over.

        private const string NullableScalarReturn = """
            using DwarfMapper;
            namespace T
            {
                public class Src { public int V { get; set; } }
                public class Dst { public int V { get; set; } }
                [DwarfMapper] public partial class M { public partial Dst? Map(Src s); }
            }
            """;

        private const string NullableGenericReturn = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Src { public int V { get; set; } }
                public class Dst { public int V { get; set; } }
                [DwarfMapper] public partial class M { public partial List<Dst?> Many(List<Src> s); }
            }
            """;

        private const string NullableUpdateIntoTarget = """
            using DwarfMapper;
            namespace T
            {
                public class Src { public int V { get; set; } }
                public class Dst { public int V { get; set; } }
                [DwarfMapper] public partial class M { public partial void Update(Src s, Dst? d); }
            }
            """;

        private const string NullableUpdateIntoTargetWithNonNullableReturn = """
            using DwarfMapper;
            namespace T
            {
                public class Src { public int V { get; set; } }
                public class Dst { public int V { get; set; } }
                [DwarfMapper] public partial class M { public partial Dst Update(Src s, Dst? d); }
            }
            """;

        private const string NullableUpdateIntoReturn = """
            using DwarfMapper;
            namespace T
            {
                public class Src { public int V { get; set; } }
                public class Dst { public int V { get; set; } }
                [DwarfMapper] public partial class M { public partial Dst? Update(Src s, Dst d); }
            }
            """;

        [Fact]
        public void Nullable_scalar_return_emits_no_CS8603_or_CS8604_in_the_aggregate_files()
        {
            // RED: 8 diagnostics, none of them in the mapper's own file — CS8603 in DwarfMapper.Extensions.g.cs
            // (the facade declared `Dst ToDst(...)` while forwarding to a `Dst?`-returning map), CS8603 on the
            // registry lambda, and one CS8604 for each of the six collection shapes' `List<Dst>.Add`.
            AssertWarningFree(NullableScalarReturn, "partial Dst? Map(Src s)");
        }

        [Fact]
        public void Nullable_scalar_return_is_declared_by_the_facade_and_asserted_by_the_registry()
        {
            // The two aggregates answer the same fact differently, because their type systems differ: the facade
            // CAN express the nullability and does; the registry's delegate is the SHIPPED Func<object, object>
            // and cannot, so it asserts the contract at the boundary rather than dropping the registration (which
            // would silently delete an ambient map that works today for anyone not using TreatWarningsAsErrors).
            var all = GeneratorTestHarness.RunAll(NullableScalarReturn, NullableContextOptions.Enable).GeneratedSource;

            Assert.Contains("static global::T.Dst? ToDst(this global::T.Src source)", all, StringComparison.Ordinal);
            Assert.Contains("Map((global::T.Src)__s) ?? throw new global::System.InvalidOperationException(", all, StringComparison.Ordinal);
            Assert.Contains("Map(__e) ?? throw new global::System.InvalidOperationException(", all, StringComparison.Ordinal);
        }

        [Fact]
        public void A_non_nullable_return_registers_without_a_null_guard()
        {
            // The other half of the previous test: the guard is emitted for the declared shape ONLY, so no
            // existing registration moves. Without this, "coalesce every registration" would pass the suite too.
            var all = GeneratorTestHarness.RunAll(NullableSourceParameter, NullableContextOptions.Enable).GeneratedSource;

            Assert.Contains("global::DwarfMapper.DwarfMapperRegistry.Register(", all, StringComparison.Ordinal);
            Assert.DoesNotContain("?? throw", all, StringComparison.Ordinal);
        }

        [Fact]
        public void The_new_target_stays_annotation_free_when_the_return_slot_gains_an_annotation()
        {
            // The guard on the whole design, and the reason task 2.7 reverted the one-line patch: the emitter
            // writes `new ReturnTypeFullName { … }` and the registry writes `typeof(ReturnTypeFullName)`, both of
            // which are CS8628/CS8639 on a nullable reference type. This test PASSES RED by design — its job is
            // to stay green — and fails the moment someone "simplifies" ReturnTypeSignature away.
            var all = GeneratorTestHarness.RunAll(NullableScalarReturn, NullableContextOptions.Enable).GeneratedSource;

            Assert.Contains("return new global::T.Dst", all, StringComparison.Ordinal);
            Assert.DoesNotContain("new global::T.Dst?", all, StringComparison.Ordinal);
            Assert.Contains("typeof(global::T.Dst)", all, StringComparison.Ordinal);
            Assert.DoesNotContain("typeof(global::T.Dst?)", all, StringComparison.Ordinal);
        }

        [Fact]
        public void Nullable_generic_return_keeps_its_element_annotation_in_the_emitted_signature()
        {
            // RED: CS8819 on the partial plus CS8619 on `return __DwarfMapColl_…(s)`, since the synthesized
            // collection helper was already built for List<Dst?> while the emitted signature said List<Dst>.
            AssertWarningFree(NullableGenericReturn, "partial List<Dst?> Many(List<Src> s)");

            var generated = GeneratorAssert.CompilesClean(NullableGenericReturn, NullableContextOptions.Enable);
            Assert.Contains("global::System.Collections.Generic.List<global::T.Dst?> Many(", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Nullable_update_into_target_parameter_keeps_its_annotation()
        {
            // RED: CS8611 on parameter 'd'. The update-into destination is spelled from ReturnTypeFullName, so
            // task 2.7's ParameterTypeSignature — which covers parameter 0 only — could not reach it.
            AssertWarningFree(NullableUpdateIntoTarget, "partial void Update(Src s, Dst? d)");

            var generated = GeneratorAssert.CompilesClean(NullableUpdateIntoTarget, NullableContextOptions.Enable);
            Assert.Contains("Update(global::T.Src s, global::T.Dst? d)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void The_update_into_return_and_target_slots_are_annotated_independently()
        {
            // The anti-fold test. Update-into declares the destination type twice, from two different symbols,
            // and a user may annotate them independently. Writing ONE string into both slots turns the
            // parameter's CS8611 into a CS8819 on the return, so this pins the mixed form both ways.
            AssertWarningFree(NullableUpdateIntoTargetWithNonNullableReturn, "partial Dst Update(Src s, Dst? d)");
            AssertWarningFree(NullableUpdateIntoReturn, "partial Dst? Update(Src s, Dst d)");

            var mixed = GeneratorAssert.CompilesClean(NullableUpdateIntoTargetWithNonNullableReturn, NullableContextOptions.Enable);
            Assert.Contains("partial global::T.Dst Update(global::T.Src s, global::T.Dst? d)", mixed, StringComparison.Ordinal);

            var nullableReturn = GeneratorAssert.CompilesClean(NullableUpdateIntoReturn, NullableContextOptions.Enable);
            Assert.Contains("partial global::T.Dst? Update(global::T.Src s, global::T.Dst d)", nullableReturn, StringComparison.Ordinal);
        }

        // -- 9. the async-stream element edge read no null handling at all ------------------------------------
        // Round 29 task 2.8, defect B. Every other element edge routes its null decision through the single
        // TryResolveConversion + CollectionConverter.ElementExpr pair that 6fa7308 centralised; this loop built
        // `yield return Conv(__item)` by hand and applied none of it, so IAsyncEnumerable<S?> handed a
        // possibly-null element to a helper that cannot take one — CS8604, in the consumer's .g.cs.
        //
        // Ledger, stated plainly: for the consumer this shape is NET-NEUTRAL as of the previous release. The
        // CS8611 the signature used to emit was removed by task 2.7, which is what made this one visible; one
        // unsuppressible diagnostic became another. No working build can regress here, because every affected
        // shape already failed to compile clean. This closes a hole; it does not repair a regression.

        private const string AsyncStreamNullableElement = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Src { public int V { get; set; } }
                public class Dst { public int V { get; set; } }
                [DwarfMapper] public partial class M { public partial IAsyncEnumerable<Dst> Stream(IAsyncEnumerable<Src?> s); }
            }
            """;

        private const string AsyncStreamNullableElementBothSides = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Box { public List<Child?> Items { get; set; } = new(); }
                public class BoxDto { public List<ChildDto?> Items { get; set; } = new(); }
                [DwarfMapper] public partial class M
                {
                    public partial IAsyncEnumerable<ChildDto?> Stream(IAsyncEnumerable<Child?> s);
                    public partial BoxDto MapBox(Box b);
                    public partial ChildDto ToDto(Child c);
                }
            }
            """;

        private const string AsyncStreamNonNullableElement = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Src { public int V { get; set; } }
                public class Dst { public int V { get; set; } }
                [DwarfMapper] public partial class M { public partial IAsyncEnumerable<Dst> Stream(IAsyncEnumerable<Src> s); }
            }
            """;

        /// <summary>The text between <c>yield return </c> and the <c>;</c> that ends it.</summary>
        private static string YieldedElementExpression(string generated)
        {
            var start = generated.IndexOf("yield return ", StringComparison.Ordinal);
            Assert.True(start >= 0, "no `yield return` in the generated async iterator:\n" + generated);
            start += "yield return ".Length;
            return generated.Substring(start, generated.IndexOf(';', start) - start);
        }

        /// <summary>The text inside the collection helper's <c>__r.Add(…);</c>.</summary>
        private static string AddedElementExpression(string generated)
        {
            const string marker = "__r.Add(";
            var start = generated.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, "no `__r.Add(` in the generated collection helper:\n" + generated);
            start += marker.Length;
            return generated.Substring(start, generated.IndexOf(");", start, StringComparison.Ordinal) - start);
        }

        [Fact]
        public void Nullable_async_stream_element_emits_no_CS8604()
        {
            // RED: CS8604 — `__DwarfMap_Obj_…(__item)` with __item declared IAsyncEnumerable<Src?>'s element.
            AssertWarningFree(AsyncStreamNullableElement, "IAsyncEnumerable<Src?> -> IAsyncEnumerable<Dst>");

            var generated = GeneratorAssert.CompilesClean(AsyncStreamNullableElement, NullableContextOptions.Enable);
            Assert.Contains("(__item!)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void The_async_stream_element_is_answered_exactly_as_the_collection_element_is()
        {
            // The anti-drift test, and the reason the fix routes through the shared builder rather than adding a
            // fifth hand-written copy of the null / null! / ! switch: the same element pair reached through an
            // async stream and through a List<T> member must produce the SAME expression, character for
            // character. Here that is the lift — the destination element admits null, so the null is preserved
            // rather than forced through the converter — with the destination element type cast onto the
            // non-null arm so the conditional's type never depends on target-typing.
            AssertWarningFree(AsyncStreamNullableElementBothSides, "IAsyncEnumerable<Child?> + List<Child?>, one mapper");

            var generated = GeneratorAssert.CompilesClean(AsyncStreamNullableElementBothSides, NullableContextOptions.Enable);

            Assert.Equal(AddedElementExpression(generated), YieldedElementExpression(generated));
            Assert.Equal("(__item is null ? null : (global::T.ChildDto?)ToDto(__item))", YieldedElementExpression(generated));
        }

        [Fact]
        public void A_non_nullable_async_stream_element_keeps_the_expression_it_always_had()
        {
            // The guard on the other side: routing through the shared builder must not add null handling where
            // none was owed, or every existing async-stream consumer's output moves. NullHandling.None with a
            // non-nullable source element is the bare call, exactly as the hand-written loop wrote it.
            var generated = GeneratorAssert.CompilesClean(AsyncStreamNonNullableElement, NullableContextOptions.Enable);

            Assert.Equal("__DwarfMap_Obj_global__T_Src_global__T_Dst_025B94CC(__item)", YieldedElementExpression(generated));
        }

        // ── 5. a USER-DECLARED converter on an element edge, into a NON-nullable destination element ────────
        // Round 29 T2.9. 6fa7308 centralised the null decision for a user-declared converter at
        // TryResolveConversion and reported that member, element, dictionary value, flatten leaf and
        // constructor argument all read it. The DECISION they do read; the FORGIVENESS they did not.
        // CollectionConverter.ElementExpr and DictionaryConverter.Expr each kept their own
        // `srcElemIsNullableRef && GeneratedNames.IsSynthesized(conv)` — the very proxy 6fa7308 identified as
        // blind to a map method the user declared — so `List<Child?>` -> `List<ChildDto>` beside a declared
        // `ToDto` emitted `ToDto(__item)` bare: CS8604 inside the consumer's .g.cs, on EVERY element edge
        // (list, array, dictionary value, span, async stream, constructor argument, update-into) at once.
        //
        // Each shape below failed RED with the same message — "Possible null reference argument for parameter
        // 'c' in 'ChildDto M.ToDto(Child c)'" — and each is the smallest mapper that reaches its edge.

        private const string ElementViaDeclaredMapList = """
            #nullable enable
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public List<Child?> Items { get; set; } = new(); }
                public class Dst { public List<ChildDto> Items { get; set; } = new(); }
                [DwarfMapper] public partial class M
                {
                    public partial Dst Map(Src s);
                    public partial ChildDto ToDto(Child c);
                }
            }
            """;

        private const string ElementViaDeclaredMapArray = """
            #nullable enable
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public Child?[] Items { get; set; } = new Child?[2]; }
                public class Dst { public ChildDto[] Items { get; set; } = new ChildDto[2]; }
                [DwarfMapper] public partial class M
                {
                    public partial Dst Map(Src s);
                    public partial ChildDto ToDto(Child c);
                }
            }
            """;

        private const string ValueViaDeclaredMapDictionary = """
            #nullable enable
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public Dictionary<string, Child?> Lookup { get; set; } = new(); }
                public class Dst { public Dictionary<string, ChildDto> Lookup { get; set; } = new(); }
                [DwarfMapper] public partial class M
                {
                    public partial Dst Map(Src s);
                    public partial ChildDto ToDto(Child c);
                }
            }
            """;

        private const string ElementViaDeclaredMapSpan = """
            #nullable enable
            using System;
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                [DwarfMapper] public partial class M
                {
                    public partial void Copy(ReadOnlySpan<Child?> src, Span<ChildDto> dst);
                    public partial ChildDto ToDto(Child c);
                }
            }
            """;

        private const string ElementViaDeclaredMapAsyncStream = """
            #nullable enable
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                [DwarfMapper] public partial class M
                {
                    public partial IAsyncEnumerable<ChildDto> Stream(IAsyncEnumerable<Child?> s);
                    public partial ChildDto ToDto(Child c);
                }
            }
            """;

        private const string ElementViaDeclaredMapConstructorArgument = """
            #nullable enable
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public List<Child?> Items { get; set; } = new(); }
                public class Dst { public Dst(List<ChildDto> items) { Items = items; } public List<ChildDto> Items { get; } }
                [DwarfMapper] public partial class M
                {
                    public partial Dst Map(Src s);
                    public partial ChildDto ToDto(Child c);
                }
            }
            """;

        private const string ElementViaDeclaredMapUpdateInto = """
            #nullable enable
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public List<Child?> Items { get; set; } = new(); }
                public class Dst { public List<ChildDto> Items { get; set; } = new(); }
                [DwarfMapper] public partial class M
                {
                    public partial void Update(Src s, Dst d);
                    public partial ChildDto ToDto(Child c);
                }
            }
            """;

        /// <summary>The null-TOLERANT twin: the converter asked for the null, so it must keep receiving it.</summary>
        private const string ElementViaNullTolerantDeclaredMap = """
            #nullable enable
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public List<Child?> Items { get; set; } = new(); }
                public class Dst { public List<ChildDto> Items { get; set; } = new(); }
                [DwarfMapper] public partial class M
                {
                    public partial Dst Map(Src s);
                    public ChildDto ToDto(Child? c) => new ChildDto { V = c?.V ?? 0 };
                }
            }
            """;

        /// <summary>The NULLABLE-destination twin: the null is legal there, so it is lifted, not forgiven.</summary>
        private const string NullableDestinationElementViaDeclaredMap = """
            #nullable enable
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public List<Child?> Items { get; set; } = new(); }
                public class Dst { public List<ChildDto?> Items { get; set; } = new(); }
                [DwarfMapper] public partial class M
                {
                    public partial Dst Map(Src s);
                    public partial ChildDto ToDto(Child c);
                }
            }
            """;

        /// <summary>The [FlattenGraph] leaf, which forgave the same argument and reported nothing at all.</summary>
        private const string FlattenGraphLeafViaDeclaredMap = """
            #nullable enable
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class Leaf { public int V { get; set; } }
            public class LeafDto { public int V { get; set; } }
            public class Node    { public Leaf? Payload { get; set; } public Node? Next { get; set; } }
            public class NodeDto { public LeafDto Payload { get; set; } = new(); public NodeDto? Next { get; set; } }
            public class Root    { public IReadOnlyList<Node> Entries { get; set; } = new List<Node>(); }
            public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
            [DwarfMapper]
            public partial class M
            {
                [FlattenGraph("Entries", "Nodes")]
                public partial RootDto Map(Root r);
                public partial LeafDto ToDto(Leaf l);
            }
            """;

        private static string[] DwarfIds(string source)
        {
            return GeneratorTestHarness.Run(source, NullableContextOptions.Enable)
                .Diagnostics.Select(d => d.Id).ToArray();
        }

        private static string Dwarf070Message(string source)
        {
            var d = Assert.Single(GeneratorTestHarness.Run(source, NullableContextOptions.Enable)
                .Diagnostics.Where(x => x.Id == "DWARF070"));
            return d.GetMessage(CultureInfo.InvariantCulture);
        }

        /// <summary>
        ///     Resolves a schema constant by name, so an [InlineData] row can name one instead of repeating the
        ///     whole mapper. Attribute arguments must be compile-time constants and a <c>const string</c> field
        ///     would be inlined into the attribute anyway — this keeps the seven schemas readable as a group.
        /// </summary>
        private static string Schema(string fieldName)
        {
            var field = typeof(ConsumerReportedEmissionWarningsTests)
                .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field);
            return (string)field!.GetValue(null)!;
        }

        [Theory]
        [InlineData("List<Child?> -> List<ChildDto>", nameof(ElementViaDeclaredMapList))]
        [InlineData("Child?[] -> ChildDto[]", nameof(ElementViaDeclaredMapArray))]
        [InlineData("Dictionary<string, Child?> value", nameof(ValueViaDeclaredMapDictionary))]
        [InlineData("ReadOnlySpan<Child?> -> Span<ChildDto>", nameof(ElementViaDeclaredMapSpan))]
        [InlineData("IAsyncEnumerable<Child?> -> IAsyncEnumerable<ChildDto>", nameof(ElementViaDeclaredMapAsyncStream))]
        [InlineData("List<Child?> bound to a constructor parameter", nameof(ElementViaDeclaredMapConstructorArgument))]
        [InlineData("List<Child?> through update-into", nameof(ElementViaDeclaredMapUpdateInto))]
        public void An_element_edge_through_a_user_declared_converter_emits_no_CS8604(string label, string schemaField)
        {
            // RED on every row: CS8604 "Possible null reference argument for parameter 'c' in
            // 'ChildDto M.ToDto(Child c)'", inside the consumer's .g.cs.
            AssertWarningFree(Schema(schemaField), label);
        }

        [Theory]
        [InlineData(nameof(ElementViaDeclaredMapList), "The source element mapped into 'Items'")]
        [InlineData(nameof(ElementViaDeclaredMapArray), "The source element mapped into 'Items'")]
        [InlineData(nameof(ValueViaDeclaredMapDictionary), "The source value mapped into 'Lookup'")]
        [InlineData(nameof(ElementViaDeclaredMapSpan), "The source element mapped into 'Copy'")]
        [InlineData(nameof(ElementViaDeclaredMapAsyncStream), "The source element mapped into 'Stream'")]
        [InlineData(nameof(ElementViaDeclaredMapConstructorArgument), "The source element mapped into 'items'")]
        [InlineData(nameof(FlattenGraphLeafViaDeclaredMap), "Source member 'Payload'")]
        public void Every_element_edge_that_forgives_also_reports_DWARF070(string schemaField, string subject)
        {
            // The coupling the whole family turns on: no site may forgive a null without also signalling it.
            // Silencing CS8604 with a '!' and saying nothing trades an unsuppressible compiler diagnostic for a
            // silent wrong value, which is the wrong direction for this project. Each row was RED before this
            // task: six of the seven emitted no DWARF diagnostic at all, and the [FlattenGraph] leaf had been
            // forgiving in silence since audit R7.
            var message = Dwarf070Message(Schema(schemaField));

            Assert.Contains(subject, message, StringComparison.Ordinal);
            Assert.Contains("its destination is non-nullable", message, StringComparison.Ordinal);
        }

        [Fact]
        public void The_element_remedy_is_named_and_is_not_the_source_member_one()
        {
            // [MapProperty(NullSubstitute = …)] and SkipNullSourceMembers are source-MEMBER instruments; neither
            // reaches an element type. A message that offered only those would point the reader at two levers
            // that cannot touch their code — the same defect task 2.7 fixed for the mapping-parameter spelling.
            var message = Dwarf070Message(ElementViaDeclaredMapList);

            Assert.Contains("For a collection ELEMENT or a dictionary VALUE neither attribute reaches it either",
                message,
                StringComparison.Ordinal);
            Assert.Contains("make the destination element type nullable", message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_null_tolerant_user_converter_on_the_element_path_is_neither_forgiven_nor_reported()
        {
            // Passes RED by design, and that is the point: without it, "forgive every user converter" would pass
            // the rest of this section too. A converter declared `ToDto(Child? c)` was written to accept the
            // null and keeps receiving it — no '!', no DWARF070.
            AssertWarningFree(ElementViaNullTolerantDeclaredMap, "List<Child?> -> List<ChildDto> via ToDto(Child?)");

            var generated = GeneratorAssert.CompilesClean(ElementViaNullTolerantDeclaredMap, NullableContextOptions.Enable);

            Assert.Equal("ToDto(__item)", AddedElementExpression(generated));
            Assert.DoesNotContain("DWARF070", DwarfIds(ElementViaNullTolerantDeclaredMap));
        }

        [Fact]
        public void A_nullable_destination_element_lifts_the_null_instead_of_forgiving_it()
        {
            // The other guard, also passing RED by design: the destination element admits null, so the null is
            // PRESERVED by the lift and there is nothing to forgive and nothing to report. This is what stops
            // the fix from spraying a '!' onto every element edge — a '!' where none is needed is noise in a
            // file the reader cannot edit.
            AssertWarningFree(NullableDestinationElementViaDeclaredMap, "List<Child?> -> List<ChildDto?>");

            var generated = GeneratorAssert.CompilesClean(NullableDestinationElementViaDeclaredMap, NullableContextOptions.Enable);

            Assert.Equal("(__item is null ? null : (global::T.ChildDto?)ToDto(__item))", AddedElementExpression(generated));
            Assert.DoesNotContain("DWARF070", DwarfIds(NullableDestinationElementViaDeclaredMap));
        }

        [Fact]
        public void The_forgiven_element_argument_is_the_same_text_the_member_path_emits()
        {
            // The anti-drift assertion for this fix, in the same spirit as the async/collection agreement test
            // above: the element edge must not grow a second spelling of the member path's `Conv(x!)`.
            var generated = GeneratorAssert.CompilesClean(ElementViaDeclaredMapList, NullableContextOptions.Enable);

            Assert.Equal("ToDto(__item!)", AddedElementExpression(generated));
        }

        // ── 6. a user-declared converter's NULLABLE RETURN, into a destination that forbids null ────────────
        // Round 29 T2.9, the other half of the family and the more dangerous one. MemberMap carried
        // ConverterParamIsNonNullableRef for the ARGUMENT side and nothing at all for the RESULT side, so
        // `partial ChildDto? ToDto(Child c)` feeding a non-nullable `ChildDto Inner` emitted
        // `Inner = s.Inner is null ? null! : ToDto(s.Inner)` — the null ARM forgiven, the CALL not: CS8601 in
        // the consumer's .g.cs (task 2.8 concern 1). It reproduced on every edge that writes a converter call:
        // member, collection element, dictionary value, span, async stream, constructor argument and the
        // [FlattenGraph] leaf — CS8600 + CS8601, CS8600 + CS8603 or CS8604 depending on the arm.
        //
        // Reported as DWARF107 and NOT as DWARF070, because DWARF070 opens "{0} is a nullable reference" and
        // here NOTHING on the source side is: `Child Inner` -> `ChildDto Inner` is non-nullable end to end and
        // warns anyway. No noun phrase makes that sentence true, the remedies are disjoint, and a consumer who
        // suppressed DWARF070 accepted nullable SOURCES — a different decision from accepting a converter that
        // can hand back null.

        private const string NullableReturnConverterIntoMember = """
            #nullable enable
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public Child Inner { get; set; } = new(); }
                public class Dst { public ChildDto Inner { get; set; } = new(); }
                [DwarfMapper] public partial class M
                {
                    public partial Dst Map(Src s);
                    public partial ChildDto? ToDto(Child c);
                }
            }
            """;

        private const string NullableReturnConverterIntoMemberFromNullableSource = """
            #nullable enable
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public Child? Inner { get; set; } }
                public class Dst { public ChildDto Inner { get; set; } = new(); }
                [DwarfMapper] public partial class M
                {
                    public partial Dst Map(Src s);
                    public partial ChildDto? ToDto(Child c);
                }
            }
            """;

        private const string NullableReturnConverterIntoElement = """
            #nullable enable
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public List<Child> Items { get; set; } = new(); }
                public class Dst { public List<ChildDto> Items { get; set; } = new(); }
                [DwarfMapper] public partial class M
                {
                    public partial Dst Map(Src s);
                    public partial ChildDto? ToDto(Child c);
                }
            }
            """;

        private const string NullableReturnConverterIntoDictionaryValue = """
            #nullable enable
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public Dictionary<string, Child> Lookup { get; set; } = new(); }
                public class Dst { public Dictionary<string, ChildDto> Lookup { get; set; } = new(); }
                [DwarfMapper] public partial class M
                {
                    public partial Dst Map(Src s);
                    public partial ChildDto? ToDto(Child c);
                }
            }
            """;

        private const string NullableReturnConverterIntoSpanElement = """
            #nullable enable
            using System;
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                [DwarfMapper] public partial class M
                {
                    public partial void Copy(ReadOnlySpan<Child> src, Span<ChildDto> dst);
                    public partial ChildDto? ToDto(Child c);
                }
            }
            """;

        private const string NullableReturnConverterIntoAsyncStreamElement = """
            #nullable enable
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                [DwarfMapper] public partial class M
                {
                    public partial IAsyncEnumerable<ChildDto> Stream(IAsyncEnumerable<Child> s);
                    public partial ChildDto? ToDto(Child c);
                }
            }
            """;

        private const string NullableReturnConverterIntoConstructorParameter = """
            #nullable enable
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public Child Inner { get; set; } = new(); }
                public class Dst { public Dst(ChildDto inner) { Inner = inner; } public ChildDto Inner { get; } }
                [DwarfMapper] public partial class M
                {
                    public partial Dst Map(Src s);
                    public partial ChildDto? ToDto(Child c);
                }
            }
            """;

        private const string NullableReturnConverterIntoFlattenGraphLeaf = """
            #nullable enable
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class Leaf { public int V { get; set; } }
            public class LeafDto { public int V { get; set; } }
            public class Node    { public Leaf Payload { get; set; } = new(); public Node? Next { get; set; } }
            public class NodeDto { public LeafDto Payload { get; set; } = new(); public NodeDto? Next { get; set; } }
            public class Root    { public IReadOnlyList<Node> Entries { get; set; } = new List<Node>(); }
            public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
            [DwarfMapper]
            public partial class M
            {
                [FlattenGraph("Entries", "Nodes")]
                public partial RootDto Map(Root r);
                public partial LeafDto? ToDto(Leaf l);
            }
            """;

        /// <summary>
        ///     A HAND-WRITTEN converter that really can return null. The partial forms above are implemented by the
        ///     generator and always return a `new`, so on their own they would let "the annotation is just
        ///     over-declared" pass for an argument — this one proves the null is real and the report is owed.
        /// </summary>
        private const string HandWrittenNullableReturnConverter = """
            #nullable enable
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public Child Inner { get; set; } = new(); }
                public class Dst { public ChildDto Inner { get; set; } = new(); }
                [DwarfMapper] public partial class M
                {
                    public partial Dst Map(Src s);
                    public ChildDto? ToDto(Child c) => c.V < 0 ? null : new ChildDto { V = c.V };
                }
            }
            """;

        /// <summary>The NULLABLE-destination twin: the returned null is legal there, so nothing is forgiven.</summary>
        private const string NullableReturnConverterIntoNullableMember = """
            #nullable enable
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public Child Inner { get; set; } = new(); }
                public class Dst { public ChildDto? Inner { get; set; } }
                [DwarfMapper] public partial class M
                {
                    public partial Dst Map(Src s);
                    public partial ChildDto? ToDto(Child c);
                }
            }
            """;

        private static string Dwarf107Message(string source)
        {
            var d = Assert.Single(GeneratorTestHarness.Run(source, NullableContextOptions.Enable)
                .Diagnostics.Where(x => x.Id == "DWARF107"));
            return d.GetMessage(CultureInfo.InvariantCulture);
        }

        [Theory]
        [InlineData("member", nameof(NullableReturnConverterIntoMember))]
        [InlineData("member, nullable source", nameof(NullableReturnConverterIntoMemberFromNullableSource))]
        [InlineData("collection element", nameof(NullableReturnConverterIntoElement))]
        [InlineData("dictionary value", nameof(NullableReturnConverterIntoDictionaryValue))]
        [InlineData("span element", nameof(NullableReturnConverterIntoSpanElement))]
        [InlineData("async stream element", nameof(NullableReturnConverterIntoAsyncStreamElement))]
        [InlineData("constructor parameter", nameof(NullableReturnConverterIntoConstructorParameter))]
        [InlineData("[FlattenGraph] leaf", nameof(NullableReturnConverterIntoFlattenGraphLeaf))]
        [InlineData("hand-written converter", nameof(HandWrittenNullableReturnConverter))]
        public void A_converters_nullable_return_emits_no_nullable_warning_at_any_edge(string label, string schemaField)
        {
            // RED on every row, with the diagnostic the arm happens to produce: CS8601 on a member and a
            // [FlattenGraph] leaf, CS8600 + CS8604 on a collection element, CS8600 + CS8601 on a dictionary
            // value and a span element, CS8600 + CS8603 on an async stream, CS8604 on a constructor parameter.
            AssertWarningFree(Schema(schemaField), "nullable-return converter into a " + label);
        }

        [Theory]
        [InlineData(nameof(NullableReturnConverterIntoMember), "destination member 'Inner'")]
        [InlineData(nameof(NullableReturnConverterIntoElement), "the element type of 'Items'")]
        [InlineData(nameof(NullableReturnConverterIntoDictionaryValue), "the value type of 'Lookup'")]
        [InlineData(nameof(NullableReturnConverterIntoSpanElement), "the element type of 'Copy'")]
        [InlineData(nameof(NullableReturnConverterIntoAsyncStreamElement), "the element type of 'Stream'")]
        [InlineData(nameof(NullableReturnConverterIntoConstructorParameter), "destination member 'inner'")]
        [InlineData(nameof(NullableReturnConverterIntoFlattenGraphLeaf), "destination member 'Payload'")]
        [InlineData(nameof(HandWrittenNullableReturnConverter), "destination member 'Inner'")]
        public void Every_edge_that_forgives_a_nullable_return_reports_DWARF107(string schemaField, string destination)
        {
            // The same coupling as DWARF070, and it matters MORE here. Forgiving an ARGUMENT only defers to the
            // callee's own ArgumentNullException.ThrowIfNull, which is still loud at the right place; forgiving
            // a RETURN actually STORES the null in a slot whose type forbids it, and nothing complains until
            // something far away dereferences it. Every row RED before this task — no DWARF diagnostic at all.
            var message = Dwarf107Message(Schema(schemaField));

            Assert.Contains("'ToDto' is declared to return a nullable reference", message, StringComparison.Ordinal);
            Assert.Contains(destination, message, StringComparison.Ordinal);
        }

        [Fact]
        public void DWARF107_says_that_DWARF070s_suppression_does_not_cover_it()
        {
            // The reason this is a new id rather than a fifth noun on DWARF070: the suppressions are not
            // interchangeable. Someone who wrote dotnet_diagnostic.DWARF070.severity = none accepted a nullable
            // value going IN; that is not a decision to accept a converter handing back null, and a message
            // that let them believe otherwise would be the quiet failure this whole family is about.
            var message = Dwarf107Message(NullableReturnConverterIntoMember);

            Assert.Contains("dotnet_diagnostic.DWARF107.severity = none", message, StringComparison.Ordinal);
            Assert.Contains("DWARF070's suppression does NOT cover this", message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_wholly_non_nullable_pair_still_warns_which_is_why_DWARF070_could_not_carry_it()
        {
            // The measurement behind the design decision, pinned so a later "simplify by folding 107 into 070"
            // has to confront it: NOTHING on the source side of this mapper is nullable. `Child Inner` into
            // `ChildDto Inner`. DWARF070's opening clause — "{0} is a nullable reference" — has no honest {0}
            // here, and DWARF070 correctly does not fire.
            var ids = DwarfIds(NullableReturnConverterIntoMember);

            Assert.Contains("DWARF107", ids);
            Assert.DoesNotContain("DWARF070", ids);
        }

        [Fact]
        public void A_nullable_destination_member_accepts_the_returned_null_and_is_not_forgiven()
        {
            // Passes RED by design: the destination admits null, so the returned null is legal, no '!' is owed
            // and no report is owed. Without this, "always forgive a nullable return" would pass the section.
            AssertWarningFree(NullableReturnConverterIntoNullableMember, "ChildDto? ToDto into ChildDto? Inner");

            var generated = GeneratorAssert.CompilesClean(NullableReturnConverterIntoNullableMember, NullableContextOptions.Enable);

            Assert.Contains("Inner = s.Inner is null ? null : ToDto(s.Inner),", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("DWARF107", DwarfIds(NullableReturnConverterIntoNullableMember));
        }

        [Fact]
        public void Both_arms_of_the_member_lift_are_forgiven_not_just_the_null_one()
        {
            // The exact text task 2.8 measured and left in place: `Inner = s.Inner is null ? null! : ToDto(s.Inner)`
            // forgave the null ARM and left the CALL bare. Both halves now carry their own suppression, and the
            // assertion is on the whole expression so neither can be dropped without failing.
            var generated = GeneratorAssert.CompilesClean(NullableReturnConverterIntoMember, NullableContextOptions.Enable);

            Assert.Contains("Inner = s.Inner is null ? null! : ToDto(s.Inner)!,", generated, StringComparison.Ordinal);

            // And the OTHER member arm, where the two halves of the family meet on one assignment: a nullable
            // source (DWARF070, the argument bang) through a nullable-return converter (DWARF107, the result
            // bang). Two independent facts, two independent suppressions, two diagnostics.
            var both = GeneratorAssert.CompilesClean(NullableReturnConverterIntoMemberFromNullableSource, NullableContextOptions.Enable);

            Assert.Contains("Inner = ToDto(s.Inner!)!,", both, StringComparison.Ordinal);
            Assert.Contains("DWARF070", DwarfIds(NullableReturnConverterIntoMemberFromNullableSource));
            Assert.Contains("DWARF107", DwarfIds(NullableReturnConverterIntoMemberFromNullableSource));
        }

        [Fact]
        public void The_element_builders_answer_the_nullable_return_identically()
        {
            // The anti-drift pair for this half: the collection element and the async stream must produce the
            // same forgiven call, because they go through the one shared builder rather than two copies of it.
            var collection = AddedElementExpression(
                GeneratorAssert.CompilesClean(NullableReturnConverterIntoElement, NullableContextOptions.Enable));
            var stream = YieldedElementExpression(
                GeneratorAssert.CompilesClean(NullableReturnConverterIntoAsyncStreamElement, NullableContextOptions.Enable));

            Assert.Equal("(__item is null ? null! : (global::T.ChildDto)ToDto(__item)!)", collection);
            Assert.Equal(collection, stream);
        }

        // ── 7. the two sites the requirement-4 audit found, which neither named defect would have reached ──
        // The audit enumerated every emitted `!` in the generator (a literal in generator source is the only
        // way one can reach a .g.cs) and crossed that list with every IsSynthesized call, every ElementExpr /
        // Expr caller and every MemberMap construction. Two survivors:
        //
        //  * The dictionary KEY. DictionaryConverter.Expr was called for the key with EVERY nullability
        //    argument at its default — no source-annotation fact, no argument forgiveness, no result
        //    forgiveness — so a key routed through a declared converter answered none of the questions the
        //    value edge answers. Probed against 298deb3 and against the two commits above: CS8600 + CS8604 in
        //    all three, i.e. pre-existing and NOT closed by either named fix.
        //  * The [FlattenGraph] DIRECT-assign leaf. `Name = n.Name!` for a `string?` leaf into a non-nullable
        //    DTO member has been forgiving in silence since audit R7 introduced the `!`. The converter arm of
        //    the same method was fixed one commit ago; this is its other half.

        private const string DictionaryKeyViaNullableReturnConverter = """
            #nullable enable
            using System.Collections.Generic;
            using DwarfMapper;
            namespace T
            {
                public class Child { public int V { get; set; } }
                public class ChildDto { public int V { get; set; } }
                public class Src { public Dictionary<Child, int> Counts { get; set; } = new(); }
                public class Dst { public Dictionary<ChildDto, int> Counts { get; set; } = new(); }
                [DwarfMapper] public partial class M
                {
                    public partial Dst Map(Src s);
                    public partial ChildDto? ToDto(Child c);
                }
            }
            """;

        private const string FlattenGraphDirectAssignNullableLeaf = """
            #nullable enable
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class Node    { public string? Name { get; set; } public Node? Next { get; set; } }
            public class NodeDto { public string Name { get; set; } = ""; public NodeDto? Next { get; set; } }
            public class Root    { public IReadOnlyList<Node> Entries { get; set; } = new List<Node>(); }
            public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
            [DwarfMapper]
            public partial class M
            {
                [FlattenGraph("Entries", "Nodes")]
                public partial RootDto Map(Root r);
            }
            """;

        [Fact]
        public void A_dictionary_key_through_a_declared_converter_emits_no_nullable_warning()
        {
            // RED: CS8600 on the cast and CS8604 on `Dictionary<ChildDto, int>.this[ChildDto key]`.
            AssertWarningFree(DictionaryKeyViaNullableReturnConverter, "Dictionary<Child, int> key -> ChildDto");

            var message = Dwarf107Message(DictionaryKeyViaNullableReturnConverter);
            Assert.Contains("the element type of 'Counts'", message, StringComparison.Ordinal);
        }

        [Fact]
        public void The_flatten_graph_direct_assign_leaf_reports_the_null_it_forgives()
        {
            // The emitted text does not move — `Name = n.Name!` is what audit R7 put there and what keeps
            // CS8601 out of the .g.cs. What was missing is the other half of the coupling: the consumer was
            // never told which member the forgiven null lands in. RED: no DWARF diagnostic at all.
            var message = Dwarf070Message(FlattenGraphDirectAssignNullableLeaf);

            Assert.Contains("Source member 'Name'", message, StringComparison.Ordinal);

            var generated = GeneratorAssert.CompilesClean(FlattenGraphDirectAssignNullableLeaf, NullableContextOptions.Enable);
            Assert.Contains("Name = n.Name!,", generated, StringComparison.Ordinal);
        }
    }
}
