// SPDX-License-Identifier: GPL-2.0-only
using System;

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
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
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
        Assert.Contains(diagnostics, d => d.Id == "DWARF015" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Blue", StringComparison.Ordinal));
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
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }
}
