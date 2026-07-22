// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

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
        Assert.Contains(diagnostics, d => d.Id == "DWARF024"
                                          && d.GetMessage(CultureInfo.InvariantCulture)
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
        Assert.True((usage!.ValidOn & AttributeTargets.Constructor) != 0);
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
            afterNew.StartsWith('{') || afterNew.StartsWith("\r\n{", StringComparison.Ordinal) ||
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
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
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
        Assert.Contains(diagnostics, d => d.Id == "DWARF001"
                                          && d.GetMessage(CultureInfo.InvariantCulture)
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
}
