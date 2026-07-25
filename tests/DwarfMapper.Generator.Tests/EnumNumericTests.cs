// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

public class EnumNumericTests
{
    [Fact]
    public void Enum_to_int_casts()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public enum Color { Red, Green }
                           public class Source { public Color Shade { get; set; } }
                           public class Target { public int Shade { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial Target Map(Source s); }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        Assert.Contains("Shade = ", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Int_to_enum_casts()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public enum Color { Red, Green }
                           public class Source { public int Shade { get; set; } }
                           public class Target { public Color Shade { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial Target Map(Source s); }
                           """;
        GeneratorAssert.CompilesClean(src);
    }
}
