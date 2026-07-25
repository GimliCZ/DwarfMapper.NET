// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     [GenerateMap&lt;S,T&gt;] declares a mapper via a class-level attribute (no partial method per pair).
///     It reuses the full pipeline — completeness gate, conversions — and emits a public `T Map(S)` method.
/// </summary>
public class GenerateMapAttributeGeneratorTests
{
    [Fact]
    public void Generates_public_Map_method_without_partial_declaration()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class A { public int Id { get; set; } public string Name { get; set; } = ""; }
                           public class B { public int Id { get; set; } public string Name { get; set; } = ""; }
                           [DwarfMapper]
                           [GenerateMap<A, B>]
                           public partial class M { }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        // Emitted as a full public method (NOT `public partial`), since no partial was declared.
        Assert.Contains("public global::Demo.B Map(global::Demo.A src)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("public partial global::Demo.B Map", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Completeness_gate_still_applies_to_GenerateMap_pairs()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class A { public int Id { get; set; } }
                           public class B { public int Id { get; set; } public string Extra { get; set; } = ""; }
                           [DwarfMapper]
                           [GenerateMap<A, B>]
                           public partial class M { }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        // Extra has no source → completeness gate fires (build error), same as a declared mapper.
        Assert.Contains(diags, d => d.Id == "DWARF001");
    }

    [Fact]
    public void Multiple_GenerateMap_attributes_produce_overloads()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class A { public int X { get; set; } }
                           public class ADto { public int X { get; set; } }
                           public class C { public int Y { get; set; } }
                           public class CDto { public int Y { get; set; } }
                           [DwarfMapper]
                           [GenerateMap<A, ADto>]
                           [GenerateMap<C, CDto>]
                           public partial class M { }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        Assert.Contains("Map(global::Demo.A src)", generated, StringComparison.Ordinal);
        Assert.Contains("Map(global::Demo.C src)", generated, StringComparison.Ordinal);
    }
}
