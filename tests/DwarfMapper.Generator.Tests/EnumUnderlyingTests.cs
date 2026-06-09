// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Generator-level checks for enum underlying-type interactions.
/// Proves that safe paths (by-name across different widths, ulong) compile cleanly,
/// and characterizes the unsafe paths (narrowing enum→numeric, ByValue narrowing)
/// that produce no diagnostic despite silent value truncation.
/// </summary>
public class EnumUnderlyingTests
{
    // ── Safe paths: by-name across mismatched underlying widths ──────────────

    [Fact]
    public void ByName_byte_underlying_to_long_underlying_compiles_without_error()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum ESrc : byte { A, B, C }
            public enum EDst : long { A, B, C }
            public class S { public ESrc V { get; set; } }
            public class D { public EDst V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // By-name emits a switch statement
        Assert.Contains("switch", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ByName_long_underlying_to_byte_underlying_compiles_without_error()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum ESrc : long { A, B, C }
            public enum EDst : byte { A, B, C }
            public class S { public ESrc V { get; set; } }
            public class D { public EDst V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void ByName_ulong_underlying_compiles_without_error()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum Big : ulong { Lo = 0, Hi = 9223372036854775808 }
            public class S { public Big V { get; set; } }
            public class D { public Big V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ── Safe paths: enum → wider numeric ─────────────────────────────────────

    [Fact]
    public void Enum_byte_to_int_widening_compiles_without_error()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum E : byte { X = 200 }
            public class S { public E V { get; set; } }
            public class D { public int V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // Emits a plain cast
        Assert.Contains("(int)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Enum_int_to_long_widening_compiles_without_error()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum E : int { Y = 2147483647 }
            public class S { public E V { get; set; } }
            public class D { public long V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ── FINDING: enum → narrower numeric, no diagnostic emitted ──────────────

    /// <summary>
    /// FINDING: enum : long { Big = 4294967296L } mapped to int target generates
    /// a plain cast <c>(int)v</c> with NO diagnostic.  At runtime (int)4294967296L == 0
    /// — the value is silently truncated.  A DWARF diagnostic for narrowing
    /// enum→numeric casts (where the enum's underlying type is wider than the numeric
    /// target) should be considered.
    /// </summary>
    [Fact]
    public void FINDING_Enum_long_underlying_to_int_target_no_diagnostic_emitted()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum ELong : long { Big = 4294967296 }
            public class S { public ELong V { get; set; } }
            public class D { public int   V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);

        // Currently no diagnostic is emitted — this is the silent narrowing gap.
        // FINDING: no DWARF warning/error despite potential truncation.
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning
                                              || d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);

        // Confirm it compiles (i.e. the generator emits syntactically valid code)
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));

        // Confirm the generated code uses a plain cast — no range check, no diagnostic
        Assert.Contains("(int)", generated, StringComparison.Ordinal);
    }

    // ── FINDING: ByValue enum→enum narrowing, no diagnostic emitted ──────────

    /// <summary>
    /// FINDING: EnumStrategy.ByValue is user-reachable via
    /// [DwarfMapper(EnumStrategy = EnumStrategy.ByValue)].
    /// When the source enum has a wider underlying type than the target enum,
    /// the generator emits <c>(Tgt)(srcUnderlying)v</c> with NO diagnostic.
    /// E.g. enum L : long { X = 256 } → enum B : byte yields (B)(long)v;
    /// (byte)256L == 0 — silently lossy.
    /// </summary>
    [Fact]
    public void FINDING_ByValue_long_underlying_to_byte_underlying_no_diagnostic_emitted()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum LEnum : long { X = 256 }
            public enum BEnum : byte { }
            public class S { public LEnum V { get; set; } }
            public class D { public BEnum V { get; set; } }
            [DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);

        // FINDING: no diagnostic for ByValue narrowing either.
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));

        // ByValue emits a cast via the source underlying type: (BEnum)(long)v
        Assert.Contains("(long)", generated, StringComparison.Ordinal);
    }
}
