// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Hardening coverage for value-conversion resolution — the audit found the `Use=`-on-constructor-parameter
///     path and the user-defined-operator DIAGNOSTIC half (DWARF038) untested, despite the recent fix that made
///     the generator honor user-defined implicit/explicit operators (was a silent DWARF005 failure).
/// </summary>
public class ConverterHardeningTests
{
    // ── Use= converter on a constructor parameter (distinct path from a settable member) ──
    [Fact]
    public void Use_converter_on_constructor_parameter_compiles_clean()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public string Amount { get; set; } = ""; }
                         public record Target(int Amount);
                         [DwarfMapper]
                         [GenerateMap<Src, Target>]
                         [MapProperty<Src, Target>("Amount", "Amount", Use = nameof(Parse))]
                         public partial class M { private static int Parse(string s) => int.Parse(s); }
                         """;

        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(s);
        Assert.Empty(errors);
    }

    [Fact]
    public void Use_converter_invalid_on_constructor_parameter_reports_DWARF014()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public string Amount { get; set; } = ""; }
                         public record Target(int Amount);
                         [DwarfMapper]
                         [GenerateMap<Src, Target>]
                         [MapProperty<Src, Target>("Amount", "Amount", Use = "DoesNotExist")]
                         public partial class M { }
                         """;

        var (diags, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diags, d => d.Id == "DWARF014");
    }

    // ── User-defined conversion operators: the DIAGNOSTIC half of the recent fix ──
    [Fact]
    public void User_defined_explicit_operator_emits_DWARF038_info()
    {
        var (diags, _) = GeneratorTestHarness.Run(MoneyMap("explicit", false));
        Assert.Contains(diags, d => d.Id == "DWARF038" && d.Severity == DiagnosticSeverity.Info);
        Assert.DoesNotContain(diags, d => d.Id == "DWARF005");
    }

    [Fact]
    public void User_defined_implicit_operator_emits_no_conversion_diagnostic()
    {
        var (diags, _) = GeneratorTestHarness.Run(MoneyMap("implicit", false));
        Assert.DoesNotContain(diags, d => d.Id == "DWARF038");
        Assert.DoesNotContain(diags, d => d.Id == "DWARF005");
    }

    [Fact]
    public void User_defined_explicit_operator_is_error_under_strict_conversions()
    {
        var diags = GeneratorTestHarness.Run(MoneyMap("explicit", true)).Diagnostics;
        Assert.Contains(diags, d => d.Id == "DWARF038" && d.Severity == DiagnosticSeverity.Error);
    }

    private static string MoneyMap(string op, bool strict)
    {
        var attr = strict ? "[DwarfMapper(ImplicitConversions = false)]" : "[DwarfMapper]";
        return $$"""
                 using DwarfMapper;
                 namespace Demo;
                 public struct Money
                 {
                     public int Cents;
                     public static {{op}} operator int(Money m) => m.Cents;
                 }
                 public class Src { public Money Amount { get; set; } }
                 public class Dst { public int Amount { get; set; } }
                 {{attr}}
                 [GenerateMap<Src, Dst>]
                 public partial class M { }
                 """;
    }
}
