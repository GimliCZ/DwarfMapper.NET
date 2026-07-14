// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

public class EnumToEnumTests
{
    [Fact]
    public void ByName_default_maps_matching_members()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public enum Src { Red, Green }
                           public enum Dst { Red, Green }
                           public class A { public Src V { get; set; } }
                           public class B { public Dst V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial B Map(A a); }
                           """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("switch", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ByName_missing_target_member_reports_DWARF015()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public enum Src { Red, Green, Blue }
                           public enum Dst { Red, Green }
                           public class A { public Src V { get; set; } }
                           public class B { public Dst V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial B Map(A a); }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics,
            d => d.Id == "DWARF015" &&
                 d.GetMessage(CultureInfo.InvariantCulture).Contains("Blue", StringComparison.Ordinal));
    }

    [Fact]
    public void Incomplete_enum_in_two_methods_reports_DWARF015_each()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public enum Src { A, B, Blue }
                         public enum Dst { A, B }
                         public class X1 { public Src V { get; set; } }
                         public class Y1 { public Dst V { get; set; } }
                         public class X2 { public Src V { get; set; } }
                         public class Y2 { public Dst V { get; set; } }
                         [DwarfMapper] public partial class M
                         {
                             public partial Y1 MapA(X1 x);
                             public partial Y2 MapB(X2 x);
                         }
                         """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.True(diagnostics.Count(d => d.Id == "DWARF015") >= 2);
    }

    [Fact]
    public void ByValue_casts_without_completeness_check()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public enum Src { Red, Green, Blue }
                           public enum Dst { Red, Green }
                           public class A { public Src V { get; set; } }
                           public class B { public Dst V { get; set; } }
                           [DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]
                           public partial class M { public partial B Map(A a); }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF015");
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }
}
