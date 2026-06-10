// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Generator-level checks for constructor-based mapping (Task 18.1 initial model/selection tests).
/// Full exhaustive coverage in Task 18.4.
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
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
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
            && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
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
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
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
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
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
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
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
    /// After 18.1, DWARF024/025/026 are registered in DiagnosticDescriptors.
    /// We verify this by checking the generator does NOT crash with unknown-ID warnings
    /// when those codes are raised (the actual trigger is 18.2+; here we just confirm
    /// the descriptors compile as part of the generator assembly).
    /// This is a compile-time check — the test itself just needs to run to prove the
    /// assembly loaded without missing-symbol errors.
    /// </summary>
    [Fact]
    public void DiagnosticDescriptors_DWARF024_025_026_exist()
    {
        // If any of these throw MissingMemberException / TypeLoadException → test fails.
        var d24 = DwarfMapper.Generator.Diagnostics.DiagnosticDescriptors.ConstructorParameterUnmapped;
        var d25 = DwarfMapper.Generator.Diagnostics.DiagnosticDescriptors.AmbiguousConstructor;
        var d26 = DwarfMapper.Generator.Diagnostics.DiagnosticDescriptors.NoMappableConstructor;

        Assert.Equal("DWARF024", d24.Id);
        Assert.Equal("DWARF025", d25.Id);
        Assert.Equal("DWARF026", d26.Id);
    }

    // ── [DwarfMapperConstructor] attribute exists ────────────────────────────

    /// <summary>
    /// The [DwarfMapperConstructor] attribute must be discoverable in the DwarfMapper assembly.
    /// </summary>
    [Fact]
    public void DwarfMapperConstructorAttribute_is_declared()
    {
        var attr = typeof(DwarfMapper.DwarfMapperConstructorAttribute);
        Assert.NotNull(attr);
        var usage = (AttributeUsageAttribute?)attr.GetCustomAttributes(typeof(AttributeUsageAttribute), false).FirstOrDefault();
        Assert.NotNull(usage);
        Assert.True((usage!.ValidOn & AttributeTargets.Constructor) != 0);
    }

    // ── MapMethodModel has ConstructorArguments ──────────────────────────────

    [Fact]
    public void MapMethodModel_has_ConstructorArguments_property()
    {
        // Structural check: the property must exist and be readable.
        var prop = typeof(DwarfMapper.Generator.Model.MapMethodModel)
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
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // Object-initializer form: no ctor args, has initializer
        Assert.Contains("new ", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("DWARF024", generated, StringComparison.Ordinal);
    }
}
