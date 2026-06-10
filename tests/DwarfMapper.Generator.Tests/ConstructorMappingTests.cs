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
    // ── 18.1: basic model plumbing — no emit yet ─────────────────────────────

    /// <summary>
    /// Positional record target: today emits DWARF006 (no parameterless ctor). After 18.2+ it must compile clean.
    /// This test currently expects the DWARF006 error — it becomes GREEN after 18.2 removes the gate.
    /// We keep it as a placeholder marker here so the test file compiles.
    /// </summary>
    [Fact]
    public void Positional_record_target_currently_emits_NoParameterlessCtor()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int X { get; set; } public string Y { get; set; } = ""; }
            public record R(int X, string Y);
            [DwarfMapper]
            public partial class M { public partial R Map(S s); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        // Before 18.2 the gate is still in place → DWARF006
        Assert.Contains(diagnostics, d => d.Id == "DWARF006");
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
