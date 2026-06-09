// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Generator-level checks for integral narrowing / sign-change conversions
/// via INumberBase.CreateChecked.
/// </summary>
public class NumericConversionTests
{
    // ── Narrowing: compile + CreateChecked emitted ────────────────────────────

    [Fact]
    public void Long_to_int_narrowing_compiles_and_emits_CreateChecked()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public long V { get; set; } }
            public class D { public int  V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("CreateChecked", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Int_to_uint_narrowing_compiles_and_emits_CreateChecked()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int  V { get; set; } }
            public class D { public uint V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("CreateChecked", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Int_to_short_narrowing_compiles_and_emits_CreateChecked()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int   V { get; set; } }
            public class D { public short V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("CreateChecked", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Ulong_to_long_narrowing_compiles_and_emits_CreateChecked()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public ulong V { get; set; } }
            public class D { public long  V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("CreateChecked", generated, StringComparison.Ordinal);
    }

    // ── Widening: must NOT emit CreateChecked (stays on implicit direct-assign) ─

    [Fact]
    public void Byte_to_int_widening_does_NOT_emit_CreateChecked()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public byte V { get; set; } }
            public class D { public int  V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.DoesNotContain("CreateChecked", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Int_to_long_widening_does_NOT_emit_CreateChecked()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int  V { get; set; } }
            public class D { public long V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.DoesNotContain("CreateChecked", generated, StringComparison.Ordinal);
    }

    // ── Nullable composition: int? → short should compose ─────────────────────

    [Fact]
    public void Nullable_int_to_short_narrowing_compiles()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int?  V { get; set; } }
            public class D { public short V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("CreateChecked", generated, StringComparison.Ordinal);
    }
}
