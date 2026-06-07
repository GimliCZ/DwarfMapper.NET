// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

public class MapPropertyTests
{
    [Fact]
    public void Rename_maps_differently_named_members()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public string FullName { get; set; } = ""; }
            public class Target { public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("FullName", "Name")]
                public partial Target Map(Source s);
            }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains("Name = s.FullName", generated, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Rename_suppresses_DWARF001_for_the_target()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public string FullName { get; set; } = ""; }
            public class Target { public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("FullName", "Name")]
                public partial Target Map(Source s);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF001");
    }

    [Fact]
    public void Unknown_target_reports_DWARF008()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public string FullName { get; set; } = ""; }
            public class Target { public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("FullName", "Nope")]
                public partial Target Map(Source s);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF008" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Nope", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_source_reports_DWARF009()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public string FullName { get; set; } = ""; }
            public class Target { public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("Ghost", "Name")]
                public partial Target Map(Source s);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF009" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Ghost", StringComparison.Ordinal));
    }
}
