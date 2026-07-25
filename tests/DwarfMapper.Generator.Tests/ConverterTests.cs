// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

public class ConverterTests
{
    [Fact]
    public void Use_method_bridges_incompatible_types()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public string Amount { get; set; } = ""; }
                           public class Target { public int Amount { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               [MapProperty("Amount", "Amount", Use = nameof(ParseInt))]
                               public partial Target Map(Source s);
                               private static int ParseInt(string v) => int.Parse(v);
                           }
                           """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Amount = ParseInt(s.Amount)", generated, StringComparison.Ordinal);
        GeneratorAssert.EmitsCompilableCode(src);
    }

    [Fact]
    public void Unknown_Use_method_reports_DWARF014()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public string Amount { get; set; } = ""; }
                           public class Target { public int Amount { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               [MapProperty("Amount", "Amount", Use = "Nope")]
                               public partial Target Map(Source s);
                           }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics,
            d => d.Id == "DWARF014" &&
                 d.GetMessage(CultureInfo.InvariantCulture).Contains("Nope", StringComparison.Ordinal));
    }

    [Fact]
    public void Use_method_with_wrong_signature_reports_DWARF014()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public string Amount { get; set; } = ""; }
                           public class Target { public int Amount { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               [MapProperty("Amount", "Amount", Use = nameof(Bad))]
                               public partial Target Map(Source s);
                               private static string Bad(int v) => v.ToString();
                           }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF014");
    }
}
