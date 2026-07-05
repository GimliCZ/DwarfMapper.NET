// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     DWARF055 (Info): a single mapper resolving a very large number of members gets a suppressible heads-up
///     that it may add IDE/compile latency (all extraction runs in the syntax transform). The threshold is high
///     (300 mapped members) so normal mappers never trip it.
/// </summary>
public class BuildBudgetDiagnosticTests
{
    private static string MapperWith(int memberCount)
    {
        var props = new StringBuilder();
        for (var i = 0; i < memberCount; i++)
            props.Append("public int P")
                .Append(i.ToString(CultureInfo.InvariantCulture))
                .Append(" { get; set; } ");

        return $$"""
                 using DwarfMapper;
                 namespace Demo;
                 public class Src { {{props}} }
                 public class Dst { {{props}} }
                 [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
                 """;
    }

    [Fact]
    public void Very_large_mapper_reports_DWARF055()
    {
        var (diags, _) = GeneratorTestHarness.Run(MapperWith(320));
        var d = diags.FirstOrDefault(x => x.Id == "DWARF055");
        Assert.NotNull(d);
        Assert.Equal(DiagnosticSeverity.Info, d!.Severity);
        // It is a heads-up, never a build break.
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(MapperWith(320)));
    }

    [Fact]
    public void Normal_sized_mapper_does_not_report_DWARF055()
    {
        var (diags, _) = GeneratorTestHarness.Run(MapperWith(20));
        Assert.DoesNotContain(diags, x => x.Id == "DWARF055");
    }
}
