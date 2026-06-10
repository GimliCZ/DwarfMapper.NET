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

    [Fact]
    public void Duplicate_explicit_target_reports_DWARF011_and_compiles()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public string A { get; set; } = ""; public string B { get; set; } = ""; }
            public class Target { public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("A", "Name")]
                [MapProperty("B", "Name")]
                public partial Target Map(Source s);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF011" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Name", System.StringComparison.Ordinal));
        // No duplicate object-initializer member (no CS1912).
        Assert.DoesNotContain(GeneratorTestHarness.RunAndGetCompilationErrors(src), d => d.Id == "CS1912");
    }

    [Fact]
    public void MapIgnore_and_MapProperty_same_target_reports_DWARF012()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public string Full { get; set; } = ""; }
            public class Target { public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapIgnore("Name")]
                [MapProperty("Full", "Name")]
                public partial Target Map(Source s);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF012" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Name", System.StringComparison.Ordinal));
    }

    [Fact]
    public void MapProperty_incompatible_types_reports_DWARF005()
    {
        // string→int now auto-resolves via IParsable<int>; use truly incompatible types
        // (a custom class that is not implicitly convertible and not IParsable/IFormattable).
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Box { public int Value { get; set; } }
            public class Source { public Box Full { get; set; } = new(); }
            public class Target { public int Name { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("Full", "Name")]
                public partial Target Map(Source s);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF005");
    }

    [Fact]
    public void MapProperty_to_readonly_target_reports_DWARF008()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public string Full { get; set; } = ""; }
            public class Target { public string Name { get; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("Full", "Name")]
                public partial Target Map(Source s);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        // read-only target is not in writableByName -> treated as unknown/un-writable target
        Assert.Contains(diagnostics, d => d.Id == "DWARF008");
    }
}
