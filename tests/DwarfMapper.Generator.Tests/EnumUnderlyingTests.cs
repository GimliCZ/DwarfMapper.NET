// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Generator-level checks for enum underlying-type interactions.
///     Proves that safe paths (by-name across different widths, ulong) compile cleanly,
///     and characterizes the unsafe paths (narrowing enum→numeric, ByValue narrowing)
///     that produce no diagnostic despite silent value truncation.
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
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
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
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
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
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
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
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // Emits CreateChecked (widening is safe; no overflow possible)
        Assert.Contains("CreateChecked", generated, StringComparison.Ordinal);
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
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ── Enum→numeric narrowing: now emits CreateChecked (throws on overflow) ──

    /// <summary>
    ///     enum : long { Big = 4294967296L } mapped to int target now emits
    ///     <c>global::System.Int32.CreateChecked((global::System.Int64)v)</c> with no diagnostic.
    ///     At runtime this throws <c>OverflowException</c> instead of silently truncating.
    /// </summary>
    [Fact]
    public void Enum_long_underlying_to_int_target_no_diagnostic_emits_CreateChecked()
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

        // No diagnostic emitted — the overflow is a runtime concern.
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Warning
                                                || d.Severity == DiagnosticSeverity.Error);

        // Confirm it compiles.
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));

        // Now emits CreateChecked, not a plain cast.
        Assert.Contains("CreateChecked", generated, StringComparison.Ordinal);
    }

    // ── ByValue enum→enum narrowing: now emits CreateChecked ─────────────────

    /// <summary>
    ///     EnumStrategy.ByValue with a wider source underlying now emits
    ///     <c>(BEnum)global::System.Byte.CreateChecked((global::System.Int64)v)</c>.
    ///     Runtime overflow throws instead of silently wrapping.
    /// </summary>
    [Fact]
    public void ByValue_long_underlying_to_byte_underlying_no_diagnostic_emits_CreateChecked()
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

        // No diagnostic emitted.
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));

        // Now emits CreateChecked instead of a plain cast.
        Assert.Contains("CreateChecked", generated, StringComparison.Ordinal);
    }
}
