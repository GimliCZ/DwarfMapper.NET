// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Generator-level checks for Nullable&lt;T&gt; → Nullable&lt;U&gt; where the inner T→U
/// requires a synthesized conversion (narrowing / enum). The emitted code must be
/// null-preserving: <c>src.HasValue ? Conv(src.Value) : null</c>.
/// </summary>
public class NullableToNullableConversionTests
{
    // ── long? → int? (numeric narrowing) ─────────────────────────────────────

    [Fact]
    public void LongNullable_to_IntNullable_compiles_clean()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public long? V { get; set; } }
            public class D { public int?  V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void LongNullable_to_IntNullable_emits_HasValue_and_CreateChecked()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public long? V { get; set; } }
            public class D { public int?  V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        // Must be null-preserving — must contain .HasValue
        Assert.Contains(".HasValue", generated, StringComparison.Ordinal);
        // Must still narrow via CreateChecked on the value path
        Assert.Contains("CreateChecked", generated, StringComparison.Ordinal);
        // Must NOT use throw-on-null pattern (that was the bug)
        Assert.DoesNotContain("?? throw", generated, StringComparison.Ordinal);
    }

    // ── E1? → E2? (enum by-name) ─────────────────────────────────────────────

    [Fact]
    public void NullableEnum_to_NullableEnum_compiles_clean()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum E1 { A, B, C }
            public enum E2 { A, B, C }
            public class S { public E1? V { get; set; } }
            public class D { public E2? V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void NullableEnum_to_NullableEnum_emits_HasValue()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum E1 { A, B, C }
            public enum E2 { A, B, C }
            public class S { public E1? V { get; set; } }
            public class D { public E2? V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains(".HasValue", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("?? throw", generated, StringComparison.Ordinal);
    }

    // ── int? → int? identity: still implicit, no HasValue needed ─────────────

    [Fact]
    public void IntNullable_to_IntNullable_identity_is_implicit_no_HasValue()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int? V { get; set; } }
            public class D { public int? V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // Identity path: no special handling needed
        Assert.DoesNotContain("?? throw", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("GetValueOrDefault", generated, StringComparison.Ordinal);
    }

    // ── T? → U (non-null target): still throw-on-null ────────────────────────

    [Fact]
    public void LongNullable_to_Int_non_null_target_still_throws_on_null()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public long? V { get; set; } }
            public class D { public int   V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // Non-nullable target: null source must throw
        Assert.Contains("?? throw", generated, StringComparison.Ordinal);
    }

    // ── T → U? (non-null source, nullable target): value flows ───────────────

    [Fact]
    public void Long_to_IntNullable_target_nullable_compiles_clean()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public long V { get; set; } }
            public class D { public int? V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // Source is non-null — no throw/default needed, just the converter
        Assert.DoesNotContain("?? throw", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("GetValueOrDefault", generated, StringComparison.Ordinal);
    }
}
