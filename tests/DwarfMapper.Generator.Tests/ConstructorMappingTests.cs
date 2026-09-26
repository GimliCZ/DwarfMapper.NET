// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     Generator-level checks for constructor-based mapping (Task 18.1 initial model/selection tests).
    ///     Full exhaustive coverage in Task 18.4.
    /// </summary>
    public class ConstructorMappingTests
    {
        // ── 18.2: record targets resolve via constructor ──────────────────────────

        [Fact]
        public void Positional_record_target_maps_via_ctor_no_error()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } public string Y { get; set; } = ""; }
                               public record R(int X, string Y);
                               [DwarfMapper]
                               public partial class M { public partial R Map(S s); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            // Must use named constructor args
            Assert.Contains("x:", generated, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("y:", generated, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DWARF024_emitted_when_ctor_param_has_no_source_member()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } }
                               public record R(int X, string Missing);
                               [DwarfMapper]
                               public partial class M { public partial R Map(S s); }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics,
                d => d.Id == "DWARF024" &&
                     d.GetMessage(CultureInfo.InvariantCulture)
                         .Contains("missing", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void DWARF025_emitted_when_two_ctors_have_same_max_arity()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } public string Y { get; set; } = ""; }
                               public class D
                               {
                                   public D(int X, string Y) { this.X = X; this.Y = Y; }
                                   public D(string Y, int X) { this.X = X; this.Y = Y; }
                                   public int X { get; }
                                   public string Y { get; } = "";
                               }
                               [DwarfMapper]
                               public partial class M { public partial D Map(S s); }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics, d => d.Id == "DWARF025");
        }

        [Fact]
        public void DwarfMapperConstructor_annotation_resolves_ambiguity()
        {
            // Two ctors: (int X, string Y) and (int X, string Y, int Z) — different arities (non-tie case).
            // Normally most-params wins, but with [DwarfMapperConstructor] on the first, that ctor is used.
            // Source only has X and Y, so using the 3-param ctor would fail on Z.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } public string Y { get; set; } = ""; }
                               public class D
                               {
                                   [DwarfMapperConstructor]
                                   public D(int X, string Y) { this.X = X; this.Y = Y; }
                                   public D(int X, string Y, int Z) { this.X = X; this.Y = Y; }
                                   public int X { get; }
                                   public string Y { get; } = "";
                               }
                               [DwarfMapper]
                               public partial class M { public partial D Map(S s); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("X:", generated, StringComparison.Ordinal);
            // The annotated 2-param ctor is selected, not the longer 3-param ctor
            Assert.DoesNotContain("Z:", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Copy_ctor_is_excluded_for_record_self_mapping()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public record R(int X, string Y);
                               [DwarfMapper]
                               public partial class M { public partial R Map(R s); }
                               """;
            var (diagnostics, generated) = GeneratorTestHarness.Run(src);
            // Must not pick the copy constructor — should use positional ctor
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("x:", generated, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Positional_member_not_in_object_initializer_when_used_as_ctor_param()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } public string Y { get; set; } = ""; public int Z { get; set; } }
                               public record R(int X, string Y) { public int Z { get; init; } }
                               [DwarfMapper]
                               public partial class M { public partial R Map(S s); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            // X and Y should appear as ctor args (named), Z in initializer
            Assert.Contains("x:", generated, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("y:", generated, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Z =", generated, StringComparison.Ordinal);
            // X and Y must NOT appear in the object-initializer section
            // (the generated code must not have "X =" after the closing paren)
            var ctorEnd = generated.IndexOf(')', StringComparison.Ordinal);
            var afterCtor = ctorEnd >= 0 ? generated.Substring(ctorEnd) : generated;
            Assert.DoesNotContain("X =", afterCtor, StringComparison.Ordinal);
            Assert.DoesNotContain("Y =", afterCtor, StringComparison.Ordinal);
        }

        // ── DWARF024 / DWARF025 / DWARF026 declared ──────────────────────────────

        /// <summary>
        ///     After 18.1, DWARF024/025/026 are registered in DiagnosticDescriptors.
        ///     We verify this by checking the generator does NOT crash with unknown-ID warnings
        ///     when those codes are raised (the actual trigger is 18.2+; here we just confirm
        ///     the descriptors compile as part of the generator assembly).
        ///     This is a compile-time check — the test itself just needs to run to prove the
        ///     assembly loaded without missing-symbol errors.
        /// </summary>
        [Fact]
        public void DiagnosticDescriptors_DWARF024_025_026_exist()
        {
            // If any of these throw MissingMemberException / TypeLoadException → test fails.
            var d24 = DiagnosticDescriptors.ConstructorParameterUnmapped;
            var d25 = DiagnosticDescriptors.AmbiguousConstructor;
            var d26 = DiagnosticDescriptors.NoMappableConstructor;

            Assert.Equal("DWARF024", d24.Id);
            Assert.Equal("DWARF025", d25.Id);
            Assert.Equal("DWARF026", d26.Id);
        }

        // ── [DwarfMapperConstructor] attribute exists ────────────────────────────

        /// <summary>
        ///     The [DwarfMapperConstructor] attribute must be discoverable in the DwarfMapper assembly.
        /// </summary>
        [Fact]
        public void DwarfMapperConstructorAttribute_is_declared()
        {
            var attr = typeof(DwarfMapperConstructorAttribute);
            Assert.NotNull(attr);
            var usage = (AttributeUsageAttribute?)attr.GetCustomAttributes(typeof(AttributeUsageAttribute), false)
                .FirstOrDefault();
            Assert.NotNull(usage);
            Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Constructor));
        }

        // ── MapMethodModel has ConstructorArguments ──────────────────────────────

        [Fact]
        public void MapMethodModel_has_ConstructorArguments_property()
        {
            // Structural check: the property must exist and be readable.
            var prop = typeof(MapMethodModel)
                .GetProperty("ConstructorArguments");
            Assert.NotNull(prop);
        }

        // ── Regression: plain settable class still works (no ctor change expected) ─

        [Fact]
        public void Settable_class_still_compiles_no_error()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } }
                               public class D { public int X { get; set; } }
                               [DwarfMapper]
                               public partial class M { public partial D Map(S s); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            // Object-initializer form: no ctor args, has initializer
            Assert.Contains("new ", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("DWARF024", generated, StringComparison.Ordinal);
        }

        // ── Regression: object-initializer form does NOT include ctor args ─────────

        [Fact]
        public void Settable_class_generated_code_has_no_ctor_args()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } public string Y { get; set; } = ""; }
                               public class D { public int X { get; set; } public string Y { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M { public partial D Map(S s); }
                               """;
            var (_, generated) = GeneratorTestHarness.Run(src);
            // Object-initializer pattern: "new <type>" followed by "{", not "("
            // The type is fully qualified in generated code.
            var newIdx = generated.IndexOf("new global::Demo.D", StringComparison.Ordinal);
            Assert.True(newIdx >= 0, $"Generated code should contain 'new global::Demo.D', got: {generated}");
            var afterNew = generated.Substring(newIdx + "new global::Demo.D".Length).TrimStart();
            // Should start with '{', not '('
            Assert.True(
                afterNew.StartsWith('{') ||
                afterNew.StartsWith("\r\n{", StringComparison.Ordinal) ||
                afterNew.StartsWith("\n{", StringComparison.Ordinal),
                $"Expected object-initializer (no ctor args), got: {afterNew.Substring(0, Math.Min(50, afterNew.Length))}");
        }

        // ── MapProperty → ctor param ──────────────────────────────────────────────

        [Fact]
        public void MapProperty_redirects_source_to_ctor_param()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int Age { get; set; } }
                               public record R(int Years);
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapProperty("Age", "Years")]
                                   public partial R Map(S s);
                               }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("Years:", generated, StringComparison.Ordinal);
            Assert.Contains("Age", generated, StringComparison.Ordinal);
        }

        // ── CaseInsensitive ctor param matching ──────────────────────────────────

        [Fact]
        public void CaseInsensitive_matches_ctor_param_with_different_case_source()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int myValue { get; set; } }
                               public class D
                               {
                                   public D(int MyValue) { this.MyValue = MyValue; }
                                   public int MyValue { get; }
                               }
                               [DwarfMapper(CaseInsensitive = true)]
                               public partial class M { public partial D Map(S s); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("MyValue:", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Ctor_param_matches_source_member_case_insensitively_by_default()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int myValue { get; set; } }
                               public class D
                               {
                                   public D(int MyValue) { this.MyValue = MyValue; }
                                   public int MyValue { get; }
                               }
                               [DwarfMapper]
                               public partial class M { public partial D Map(S s); }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            // Constructor parameters bind case-insensitively by default (C# camelCase-param convention):
            // source "myValue" -> ctor param "MyValue". No CaseInsensitive flag required.
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF024");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            GeneratorAssert.EmitsCompilableCode(src);
        }

        // ── Completeness still holds: unmapped settable non-ctor member → error ───

        [Fact]
        public void Completeness_still_errors_for_unmapped_settable_member()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } }
                               public record R(int X) { public string Extra { get; init; } = ""; }
                               [DwarfMapper]
                               public partial class M { public partial R Map(S s); }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            // 'Extra' is a settable (init) property not satisfied by ctor or source → DWARF001
            Assert.Contains(diagnostics,
                d => d.Id == "DWARF001" &&
                     d.GetMessage(CultureInfo.InvariantCulture)
                         .Contains("Extra", StringComparison.Ordinal));
        }

        // ── record struct no error ─────────────────────────────────────────────────

        [Fact]
        public void Record_struct_maps_via_ctor_no_error()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } public int Y { get; set; } }
                               public record struct RS(int X, int Y);
                               [DwarfMapper]
                               public partial class M { public partial RS Map(S s); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("X:", generated, StringComparison.OrdinalIgnoreCase);
        }

        // ── readonly record struct no error ───────────────────────────────────────

        [Fact]
        public void Readonly_record_struct_maps_via_ctor_no_error()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } }
                               public readonly record struct RRS(int X);
                               [DwarfMapper]
                               public partial class M { public partial RRS Map(S s); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("X:", generated, StringComparison.OrdinalIgnoreCase);
        }

        // ── Named ctor args in generated code ─────────────────────────────────────

        [Fact]
        public void Generated_ctor_call_uses_named_arguments()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } public string Y { get; set; } = ""; }
                               public record R(int X, string Y);
                               [DwarfMapper]
                               public partial class M { public partial R Map(S s); }
                               """;
            var (_, generated) = GeneratorTestHarness.Run(src);
            // Named args: "X: " and "Y: " (or "x: " and "y: " depending on C# convention)
            Assert.Contains(":", generated, StringComparison.Ordinal);
            // Must have new R( not just new R { (type is fully qualified in generated code)
            Assert.Contains("new global::Demo.R(", generated, StringComparison.Ordinal);
        }

        // ── MUST-FIX 1: required member that is also a ctor param → CS9035 ─────────

        /// <summary>
        ///     A required member that is ALSO satisfied by a ctor param must still appear
        ///     in the object initializer (unless ctor is annotated [SetsRequiredMembers]).
        ///     Without the fix the generator emits new C(X: s.X) which violates CS9035.
        /// </summary>
        [Fact]
        public void Required_member_that_is_ctor_param_without_SetsRequiredMembers_still_compiles()
        {
            // The ctor satisfies X at runtime, but C# also requires every `required` member to
            // be set in the object initializer OR the ctor must carry [SetsRequiredMembers].
            // Without the fix: generated "new C(X: s.X)" → CS9035.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } }
                               public class C
                               {
                                   public C(int X) { this.X = X; }
                                   public required int X { get; init; }
                               }
                               [DwarfMapper]
                               public partial class M { public partial C Map(S s); }
                               """;
            var errors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
            Assert.Empty(errors); // must not produce CS9035 or any compilation error
        }

        /// <summary>
        ///     The same CS9035 rule on the EXPLICIT path: a <c>[MapProperty]</c> whose target is a required
        ///     member the constructor also takes must still appear in the initializer. The auto-match path had
        ///     this test above; the explicit arm reads the same <c>RequiredMustInitialize</c> set at its own
        ///     skip site and had nothing pinning it.
        /// </summary>
        [Fact]
        public void Explicit_map_into_a_required_ctor_param_without_SetsRequiredMembers_still_initializes_it()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } }
                               public class C
                               {
                                   public C(int X) { this.X = X; }
                                   public required int X { get; init; }
                               }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapProperty("X", "X")]
                                   public partial C Map(S s);
                               }
                               """;
            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("X: s.X", generated, StringComparison.Ordinal);
            Assert.Contains("X = s.X", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     When the selected ctor IS annotated [SetsRequiredMembers], the required member
        ///     must NOT appear in the object initializer (no double-set).
        /// </summary>
        [Fact]
        public void Required_member_that_is_ctor_param_WITH_SetsRequiredMembers_no_initializer_redundancy()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Diagnostics.CodeAnalysis;
                               namespace Demo;
                               public class S { public int X { get; set; } }
                               public class C
                               {
                                   [SetsRequiredMembers]
                                   public C(int X) { this.X = X; }
                                   public required int X { get; init; }
                               }
                               [DwarfMapper]
                               public partial class M { public partial C Map(S s); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            // With [SetsRequiredMembers], X is consumed by ctor — should NOT appear in initializer.
            var parenClose = generated.IndexOf(')', StringComparison.Ordinal);
            var afterParen = parenClose >= 0 ? generated.Substring(parenClose) : generated;
            // There should be no "X =" in the initializer section.
            Assert.DoesNotContain("X =", afterParen, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Plain positional record must still emit ONLY ctor args — no double-set in initializer.
        ///     Record positional members are NOT `required`, so the new logic must not affect them.
        /// </summary>
        [Fact]
        public void Plain_positional_record_emits_only_ctor_args_no_initializer_redundancy()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } public string Y { get; set; } = ""; }
                               public record R(int X, string Y);
                               [DwarfMapper]
                               public partial class M { public partial R Map(S s); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            // X and Y in ctor args, must NOT appear in an object-initializer redundantly.
            var parenClose = generated.IndexOf(')', StringComparison.Ordinal);
            var afterParen = parenClose >= 0 ? generated.Substring(parenClose) : generated;
            Assert.DoesNotContain("X =", afterParen, StringComparison.Ordinal);
            Assert.DoesNotContain("Y =", afterParen, StringComparison.Ordinal);
        }

        // ── MUST-FIX 2: ref/out ctor param → CS1620 ──────────────────────────────

        /// <summary>
        ///     A ctor with a ref parameter must NOT be selected (would produce CS1620).
        ///     Expect DWARF026 (no mappable constructor). Because the generator skips the method,
        ///     a CS8795 (partial method needs implementation) is expected but CS1620 must NOT appear.
        /// </summary>
        [Fact]
        public void Ctor_with_ref_param_is_not_selected()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } }
                               public struct T
                               {
                                   public T(ref int x) { X = x; }
                                   public int X { get; }
                               }
                               [DwarfMapper]
                               public partial class M { public partial T Map(S s); }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            // Must emit DWARF026 — no mappable constructor.
            Assert.Contains(diagnostics, d => d.Id == "DWARF026");
            // The generator skips the method body (DWARF026 blocks it) → CS8795 is expected.
            // The critical assertion: no CS1620 (which would mean a bad ref/out ctor call was emitted).
            var compilationErrors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
            Assert.DoesNotContain(compilationErrors, e => e.Id == "CS1620");
            // CS8795 is expected because the partial method has no generated implementation.
            Assert.Contains(compilationErrors, e => e.Id == "CS8795");
        }

        /// <summary>
        ///     A ctor with an out parameter must NOT be selected (would produce CS1620).
        ///     Expect DWARF026 (no mappable constructor) and no CS1620 in compiled output.
        /// </summary>
        [Fact]
        public void Ctor_with_out_param_is_not_selected()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } }
                               public struct T
                               {
                                   public T(out int x) { x = 0; X = x; }
                                   public int X { get; }
                               }
                               [DwarfMapper]
                               public partial class M { public partial T Map(S s); }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics, d => d.Id == "DWARF026");
            var compilationErrors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
            Assert.DoesNotContain(compilationErrors, e => e.Id == "CS1620");
            Assert.Contains(compilationErrors, e => e.Id == "CS8795");
        }

        /// <summary>
        ///     A ctor with an `in` parameter IS callable with a plain named arg — must still be selected.
        /// </summary>
        [Fact]
        public void Ctor_with_in_param_is_selected_and_compiles()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } }
                               public struct T
                               {
                                   public T(in int X) { this.X = X; }
                                   public int X { get; }
                               }
                               [DwarfMapper]
                               public partial class M { public partial T Map(S s); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("X:", generated, StringComparison.OrdinalIgnoreCase);
        }

        // ── SHOULD-FIX 3: annotated-ctor ambiguity when parameterless ctor exists ──

        /// <summary>
        ///     When a parameterless ctor exists AND two ctors carry [DwarfMapperConstructor],
        ///     DWARF025 must be emitted (consistent with the no-parameterless path).
        ///     Previously FirstOrDefault silently picked the first annotated ctor.
        /// </summary>
        [Fact]
        public void Two_annotated_ctors_with_parameterless_ctor_emits_DWARF025()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } public string Y { get; set; } = ""; }
                               public class D
                               {
                                   public D() { }
                                   [DwarfMapperConstructor]
                                   public D(int X) { this.X = X; }
                                   [DwarfMapperConstructor]
                                   public D(string Y) { this.Y = Y; }
                                   public int X { get; set; }
                                   public string Y { get; set; } = "";
                               }
                               [DwarfMapper]
                               public partial class M { public partial D Map(S s); }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics, d => d.Id == "DWARF025");
        }

        // ── A14: [DwarfMapperConstructor] at the PROJECTION endpoint ──────────────

        /// <summary>
        ///     The projection endpoint constructs the destination, so the directive that says WHICH constructor
        ///     to construct it with has to reach it. It did not: the endpoint decided by a local widest-arity
        ///     pick, and its one call to <c>ConstructorSelector</c> discarded the answer — so an annotated
        ///     constructor on a target with a parameterless one and writable members was accepted, ignored, and
        ///     unreported (surface-matrix cell DwarfMapperConstructor @ Projection).
        /// </summary>
        [Fact]
        public void Annotated_ctor_is_honoured_at_the_projection_endpoint()
        {
            const string src = """
                               using System.Linq;
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } public string Y { get; set; } = ""; }
                               public class D
                               {
                                   public D() { }
                                   [DwarfMapperConstructor]
                                   public D(int x, string y) { X = x; Y = y; }
                                   public int X { get; set; }
                                   public string Y { get; set; } = "";
                               }
                               [DwarfMapper]
                               public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("new global::Demo.D(__s.X, __s.Y)", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The same directive on a NESTED projection target. That path had its own copy of the widest-arity
        ///     pick, under a comment telling the reader to keep it in step with the top-level one; the copy was
        ///     silent for exactly the same reason, and fixing only the site named in the finding would have left
        ///     it so. Also pins that the constructor's arguments are not ALSO assigned in a trailing object
        ///     initializer: the nested leftover filter matched parameter to member under the configured comparer
        ///     alone, which does not equate <c>x</c> with <c>X</c>.
        /// </summary>
        [Fact]
        public void Annotated_ctor_is_honoured_for_a_nested_projection_target()
        {
            const string src = """
                               using System.Linq;
                               using DwarfMapper;
                               namespace Demo;
                               public class Leaf { public int X { get; set; } public string Y { get; set; } = ""; }
                               public class LeafDto
                               {
                                   public LeafDto() { }
                                   [DwarfMapperConstructor]
                                   public LeafDto(int x, string y) { X = x; Y = y; }
                                   public int X { get; set; }
                                   public string Y { get; set; } = "";
                               }
                               public class S { public int Id { get; set; } public Leaf Child { get; set; } = new(); }
                               public class D { public int Id { get; set; } public LeafDto Child { get; set; } = new(); }
                               [DwarfMapper]
                               public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("new global::Demo.LeafDto(__s.Child.X, __s.Child.Y)", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("LeafDto(__s.Child.X, __s.Child.Y) {", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Two annotations on one target are DWARF025 at the projection endpoint too. Before the selector's
        ///     answer was used here, that check ran only on the constructor-projection branch — which a target
        ///     with a parameterless constructor and writable members never enters — so the malformed declaration
        ///     the create map refuses was accepted in silence through <c>.Project</c>.
        /// </summary>
        [Fact]
        public void Two_annotated_ctors_emit_DWARF025_at_the_projection_endpoint()
        {
            const string src = """
                               using System.Linq;
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } public string Y { get; set; } = ""; }
                               public class D
                               {
                                   public D() { }
                                   [DwarfMapperConstructor]
                                   public D(int x) { X = x; }
                                   [DwarfMapperConstructor]
                                   public D(string y) { Y = y; }
                                   public int X { get; set; }
                                   public string Y { get; set; } = "";
                               }
                               [DwarfMapper]
                               public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics, d => d.Id == "DWARF025");
        }

        /// <summary>
        ///     Annotating the PARAMETERLESS constructor selects it, so the projection maps by object initializer
        ///     — as the create map over the same pair does. The selector reports that pick with
        ///     <c>useObjectInitializerOnly = false</c>, and reading that flag rather than the chosen
        ///     constructor's arity sent the projection to the constructor branch, where it fell through to the
        ///     WIDEST overload: the annotation named one constructor and got another.
        /// </summary>
        [Fact]
        public void Annotated_parameterless_ctor_projects_by_object_initializer()
        {
            const string src = """
                               using System.Linq;
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } public string Y { get; set; } = ""; }
                               public class D
                               {
                                   [DwarfMapperConstructor]
                                   public D() { }
                                   public D(int x, string y) { X = x; Y = y; }
                                   public int X { get; set; }
                                   public string Y { get; set; } = "";
                               }
                               [DwarfMapper]
                               public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.DoesNotContain("new global::Demo.D(", generated, StringComparison.Ordinal);
            Assert.Contains("X = __s.X", generated, StringComparison.Ordinal);
        }

        // ── A14: what the UpdateInto narrowing actually claims ────────────────────

        /// <summary>
        ///     [DwarfMapperConstructor]'s <c>[DwarfSurfaceSite]</c> drops the UpdateInto endpoint because an
        ///     update-into writes into a destination the CALLER built, so for the pair it declares there is no
        ///     construction for the directive to direct. That narrowing is scoped to the endpoint's OWN
        ///     destination, and this pins the other half of it so nobody reads it as "inert at an update-into":
        ///     a nested destination member IS constructed there — the update-into replaces it wholesale
        ///     (DWARF065) — and the annotated constructor of that nested type is the one called. The measurement
        ///     the claim's stated reason rests on, kept executable rather than left as prose.
        /// </summary>
        [Fact]
        public void Annotated_ctor_is_honoured_for_a_nested_update_into_target()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Leaf { public int X { get; set; } public string Y { get; set; } = ""; }
                               public class LeafDto
                               {
                                   public LeafDto() { }
                                   [DwarfMapperConstructor]
                                   public LeafDto(int x, string y) { X = x; Y = y; }
                                   public int X { get; set; }
                                   public string Y { get; set; } = "";
                               }
                               public class S { public Leaf Child { get; set; } = new(); }
                               public class D { public LeafDto Child { get; set; } = new(); }
                               [DwarfMapper]
                               public partial class M { public partial void Update(S s, D d); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("new global::Demo.LeafDto(", generated, StringComparison.Ordinal);
            Assert.Contains("x: s.X", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The narrowing's own premise: the update-into does NOT construct the destination it maps into, so
        ///     the annotated constructor of the pair's own target is never called there. Asserted directly so the
        ///     claim cannot quietly become false — if this endpoint ever grows a construction step, this fails
        ///     and the site claim has to be revisited rather than left standing as documented behaviour.
        /// </summary>
        [Fact]
        public void An_update_into_does_not_construct_its_own_destination()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } public string Y { get; set; } = ""; }
                               public class D
                               {
                                   public D() { }
                                   [DwarfMapperConstructor]
                                   public D(int x, string y) { X = x; Y = y; }
                                   public int X { get; set; }
                                   public string Y { get; set; } = "";
                               }
                               [DwarfMapper]
                               public partial class M { public partial void Update(S s, D d); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.DoesNotContain("new global::Demo.D(", generated, StringComparison.Ordinal);
            Assert.Contains("d.X = s.X", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A knock-on of routing the projection through <c>ConstructorSelector</c>, pinned because it was
        ///     measured rather than predicted and because the full suite passing meant this shape was UNCOVERED,
        ///     not unchanged. A <c>struct</c> destination with an explicit non-parameterless constructor used to
        ///     project as <c>new Dst { X = …, Y = … }</c>: the local decision saw the struct's IMPLICIT
        ///     parameterless constructor and preferred member-init. The selector deliberately skips that
        ///     constructor for a struct that declares an explicit one — it is a zero-init no-op — so the
        ///     projection now calls the explicit constructor, which is what <c>.Map</c> over the same pair has
        ///     always done. Measured both ways at the commit that changed it: a Map/Project divergence closing,
        ///     not a new one opening.
        /// </summary>
        [Fact]
        public void A_struct_target_with_an_explicit_ctor_projects_through_that_ctor()
        {
            const string src = """
                               using System.Linq;
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } public string Y { get; set; } = ""; }
                               public struct D
                               {
                                   public D(int x, string y) { X = x; Y = y; }
                                   public int X { get; set; }
                                   public string Y { get; set; }
                               }
                               [DwarfMapper]
                               public partial class M
                               {
                                   public partial D Map(S s);
                                   public partial IQueryable<D> Project(IQueryable<S> q);
                               }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("new global::Demo.D(__s.X, __s.Y)", generated, StringComparison.Ordinal);
            // And the create map over the same pair still constructs it too — the two agreeing IS the point, so
            // this has to name text only the create map can produce. It emits NAMED arguments off its own
            // parameter (`x: s.X`); the projection emits positional ones off the lambda parameter (`__s.X`),
            // because expression trees reject named args (CS0853). A bare `new global::Demo.D(` would have been
            // satisfied by the projection line above it and asserted nothing.
            Assert.Contains("x: s.X", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The user-visible diagnostic-id flip the projection fix carried, pinned rather than left to the
        ///     CHANGELOG alone. A constructor parameter with no source used to draw <c>DWARF001</c> at this
        ///     endpoint — a complaint about the MEMBER the parameter feeds, raised by the completeness gate
        ///     because the constructor path was never entered — and now draws <c>DWARF024</c>, the constructor
        ///     diagnostic the create map has always raised for the same declaration. Both directions asserted:
        ///     the id a caller now suppresses or documents is the one this says it is, and the id they used to
        ///     get is gone.
        /// </summary>
        [Fact]
        public void An_unbindable_ctor_parameter_is_DWARF024_at_the_projection_endpoint()
        {
            const string src = """
                               using System.Linq;
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } }
                               public class D
                               {
                                   public D() { }
                                   [DwarfMapperConstructor]
                                   public D(int x, string nope) { X = x; Nope = nope; }
                                   public int X { get; set; }
                                   public string Nope { get; set; } = "";
                               }
                               [DwarfMapper]
                               public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics, d => d.Id == "DWARF024");
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF001");
        }
    }
}
