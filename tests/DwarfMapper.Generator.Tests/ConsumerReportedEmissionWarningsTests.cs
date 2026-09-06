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
    }
}
