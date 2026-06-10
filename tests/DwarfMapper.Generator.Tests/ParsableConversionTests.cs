// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Generator-level checks for automatic string↔T conversions via IParsable/IFormattable.
/// </summary>
public class ParsableConversionTests
{
    // ── string → T (IParsable) ────────────────────────────────────────────────

    [Fact]
    public void String_to_int_auto_resolves_no_error()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public string V { get; set; } = ""; }
            public class D { public int    V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("Parse", generated, StringComparison.Ordinal);
        Assert.Contains("InvariantCulture", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void String_to_Guid_auto_resolves_no_error()
    {
        const string src = """
            using DwarfMapper;
            using System;
            namespace Demo;
            public class S { public string V { get; set; } = ""; }
            public class D { public Guid   V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("Parse", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void String_to_bool_auto_resolves_no_error()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public string V { get; set; } = ""; }
            public class D { public bool   V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("Parse", generated, StringComparison.Ordinal);
    }

    // ── T → string (IFormattable) ─────────────────────────────────────────────

    [Fact]
    public void Int_to_string_auto_resolves_no_error()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int    V { get; set; } }
            public class D { public string V { get; set; } = ""; }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("ToString", generated, StringComparison.Ordinal);
        Assert.Contains("InvariantCulture", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Guid_to_string_auto_resolves_no_error()
    {
        const string src = """
            using DwarfMapper;
            using System;
            namespace Demo;
            public class S { public Guid   V { get; set; } }
            public class D { public string V { get; set; } = ""; }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("ToString", generated, StringComparison.Ordinal);
    }

    // ── Precedence: explicit Use= still wins over automatic IParsable ─────────

    [Fact]
    public void Explicit_Use_method_wins_over_automatic_Parse()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public string Amount { get; set; } = ""; }
            public class D { public int    Amount { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("Amount", "Amount", Use = nameof(Parse))]
                public partial D Map(S s);
                private static int Parse(string v) => int.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ── Enum↔string still routes by-name through EnumConverter ───────────────

    [Fact]
    public void String_to_enum_still_routes_by_name()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum Color { Red, Green, Blue }
            public class S { public string V { get; set; } = ""; }
            public class D { public Color  V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // EnumConverter emits a switch statement for by-name
        Assert.Contains("switch", generated, StringComparison.Ordinal);
        // Must NOT use Parse (IParsable path) for enums
        Assert.DoesNotContain(".Parse(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Enum_to_string_still_routes_by_name()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum Color { Red, Green, Blue }
            public class S { public Color  V { get; set; } }
            public class D { public string V { get; set; } = ""; }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // EnumConverter emits a switch for enum→string
        Assert.Contains("switch", generated, StringComparison.Ordinal);
        // Must NOT use IFormattable path for enums
        Assert.DoesNotContain("null, global::System.Globalization.CultureInfo.InvariantCulture", generated, StringComparison.Ordinal);
    }
}
