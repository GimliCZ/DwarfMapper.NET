// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Generator-level checks for automatic string↔T conversions via IParsable/IFormattable.
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
        var generated = GeneratorAssert.CompilesClean(src);
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
        var generated = GeneratorAssert.CompilesClean(src);
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
        var generated = GeneratorAssert.CompilesClean(src);
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
        var generated = GeneratorAssert.CompilesClean(src);
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
        var generated = GeneratorAssert.CompilesClean(src);
        Assert.Contains("ToString", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Char_to_string_compiles()
    {
        // char implements IFormattable explicitly only; it has NO public
        // ToString(string, IFormatProvider) — the 2-arg call must NOT be emitted.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public char   V { get; set; } }
                           public class D { public string V { get; set; } = ""; }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        GeneratorAssert.EmitsCompilableCode(src);
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Bool_to_string_compiles_and_emits_ToString()
    {
        // bool does NOT implement IFormattable; TryCreate must special-case it.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public bool   V { get; set; } }
                           public class D { public string V { get; set; } = ""; }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        GeneratorAssert.EmitsCompilableCode(src);
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("ToString", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void DateTime_to_string_emits_round_trip_format()
    {
        // DateTime→string must use "o" format to avoid sub-second precision loss.
        const string src = """
                           using DwarfMapper;
                           using System;
                           namespace Demo;
                           public class S { public DateTime V { get; set; } }
                           public class D { public string   V { get; set; } = ""; }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        GeneratorAssert.EmitsCompilableCode(src);
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        // Must emit "o" format for lossless round-trip
        Assert.Contains("\"o\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void String_to_DateTime_emits_RoundtripKind()
    {
        // string→DateTime must use DateTimeStyles.RoundtripKind so Kind is preserved.
        const string src = """
                           using DwarfMapper;
                           using System;
                           namespace Demo;
                           public class S { public string   V { get; set; } = ""; }
                           public class D { public DateTime V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        GeneratorAssert.EmitsCompilableCode(src);
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("RoundtripKind", generated, StringComparison.Ordinal);
    }

    // ── Target-nullable composition ───────────────────────────────────────────

    [Fact]
    public void Int_to_nullable_long_still_uses_implicit_path()
    {
        // int→long? is implicit (widening) — should compile with NO synthesized method.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int   V { get; set; } }
                           public class D { public long? V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        GeneratorAssert.EmitsCompilableCode(src);
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("CreateChecked", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Long_to_nullable_int_emits_CreateChecked()
    {
        // long→int? must synthesize a CreateChecked narrowing method.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public long V { get; set; } }
                           public class D { public int? V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        GeneratorAssert.EmitsCompilableCode(src);
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("CreateChecked", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void String_to_nullable_int_compiles()
    {
        // string→int? — target is nullable, source is non-nullable string.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public string V { get; set; } = ""; }
                           public class D { public int?   V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        GeneratorAssert.EmitsCompilableCode(src);
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
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
        GeneratorAssert.CompilesClean(src);
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
        var generated = GeneratorAssert.CompilesClean(src);
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
        var generated = GeneratorAssert.CompilesClean(src);
        // EnumConverter emits a switch for enum→string
        Assert.Contains("switch", generated, StringComparison.Ordinal);
        // Must NOT use IFormattable path for enums
        Assert.DoesNotContain("null, global::System.Globalization.CultureInfo.InvariantCulture", generated,
            StringComparison.Ordinal);
    }

    // ── float/double → int must NOT be auto-handled (lossy, requires Use=) ─────

    [Fact]
    public void Float_to_int_is_not_auto_resolved_reports_DWARF005()
    {
        // float→int is lossy (truncates fraction). ParsableConverter does not trigger
        // (src is not string). NumericConverter does not trigger (float is not integral).
        // This MUST remain DWARF005 — the user must supply Use= for lossy conversions.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public float V { get; set; } }
                           public class D { public int   V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF005");
    }

    [Fact]
    public void Double_to_int_is_not_auto_resolved_reports_DWARF005()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public double V { get; set; } }
                           public class D { public int    V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF005");
    }

    [Fact]
    public void Decimal_to_int_is_not_auto_resolved_reports_DWARF005()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public decimal V { get; set; } }
                           public class D { public int     V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF005");
    }

    // ── string → string is identity (no Parse emitted) ────────────────────────

    [Fact]
    public void String_to_string_identity_does_NOT_emit_Parse()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public string V { get; set; } = ""; }
                           public class D { public string V { get; set; } = ""; }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        // string→string is implicit — no synthesized method
        Assert.DoesNotContain(".Parse(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("ToString(null", generated, StringComparison.Ordinal);
    }
}
