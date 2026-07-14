// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

public class CaseInsensitiveMappingTests
{
    [Fact]
    public void CaseInsensitive_matches_differently_cased_names()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int count { get; set; } }
                           public class Target { public int Count { get; set; } }
                           [DwarfMapper(CaseInsensitive = true)]
                           public partial class M { public partial Target Map(Source s); }
                           """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Count = s.count", generated, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Default_is_case_sensitive_and_reports_DWARF001()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int count { get; set; } }
                           public class Target { public int Count { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial Target Map(Source s); }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF001");
    }

    [Fact]
    public void CaseInsensitive_ambiguous_source_reports_DWARF010()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int Count { get; set; } public int count { get; set; } }
                           public class Target { public int Count { get; set; } }
                           [DwarfMapper(CaseInsensitive = true)]
                           public partial class M { public partial Target Map(Source s); }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics,
            d => d.Id == "DWARF010" &&
                 d.GetMessage(CultureInfo.InvariantCulture).Contains("Count", StringComparison.Ordinal));
    }
}
