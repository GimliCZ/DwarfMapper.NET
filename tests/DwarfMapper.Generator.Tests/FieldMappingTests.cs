// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

public class FieldMappingTests
{
    [Fact]
    public void Public_field_is_mapped()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int Count; }
                           public class Target { public int Count; }
                           [DwarfMapper]
                           public partial class M { public partial Target Map(Source s); }
                           """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Count = s.Count", generated, StringComparison.Ordinal);
        GeneratorAssert.EmitsCompilableCode(src);
    }

    [Fact]
    public void Field_maps_to_property_of_same_name()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int Count; }
                           public class Target { public int Count { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial Target Map(Source s); }
                           """;
        GeneratorAssert.CompilesClean(src);
    }

    [Fact]
    public void Readonly_target_field_with_source_reports_DWARF007()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int Count { get; set; } }
                           public class Target { public readonly int Count; }
                           [DwarfMapper]
                           public partial class M { public partial Target Map(Source s); }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF007");
    }

    [Fact]
    public void Const_field_is_ignored()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int Count { get; set; } }
                           public class Target { public const int Tag = 5; public int Count { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial Target Map(Source s); }
                           """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("Tag =", generated, StringComparison.Ordinal);
    }
}
