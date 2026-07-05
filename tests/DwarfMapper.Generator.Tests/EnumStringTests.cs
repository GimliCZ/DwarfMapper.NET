// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

public class EnumStringTests
{
    [Fact]
    public void Enum_to_string_maps()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public enum Color { Red, Green }
                           public class A { public Color V { get; set; } }
                           public class B { public string V { get; set; } = ""; }
                           [DwarfMapper]
                           public partial class M { public partial B Map(A a); }
                           """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("\"Red\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void String_to_enum_maps()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public enum Color { Red, Green }
                           public class A { public string V { get; set; } = ""; }
                           public class B { public Color V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial B Map(A a); }
                           """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("\"Red\" =>", generated, StringComparison.Ordinal);
    }
}
