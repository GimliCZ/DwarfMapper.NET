// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     [ReverseMap] (Phase 7): a forward method's simple [MapProperty] renames are inherited inverted (A→B
///     becomes B→A) by a separately-declared inverse partial method. Non-invertible forward config (Use=,
///     dotted paths, NullSubstitute/When) is DWARF051; a missing inverse method is DWARF052.
/// </summary>
public class ReverseMapGeneratorTests
{
    private static Diagnostic? Find(IEnumerable<Diagnostic> diags, string id)
    {
        return diags.FirstOrDefault(d => d.Id == id);
    }

    [Fact]
    public void Reverse_inverts_simple_rename_in_generated_code() // runtime round-trip: NewFeaturesRuntimeTests
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Entity { public int Id { get; set; } public string FullName { get; set; } = ""; }
                           public class Dto { public int Id { get; set; } public string Name { get; set; } = ""; }
                           [DwarfMapper] public partial class M
                           {
                               [ReverseMap]
                               [MapProperty(nameof(Entity.FullName), nameof(Dto.Name))]
                               public partial Dto ToDto(Entity e);
                               public partial Entity FromDto(Dto d);
                           }
                           """;
        var (diags, gen) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // The inverse inherited Name → FullName.
        Assert.Contains("FullName = d.Name", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Inverse_own_mapproperty_wins_over_inherited()
    {
        // FromDto declares its own rename matching the inherited inverse; the inheritance must not
        // duplicate it (no DWARF011) — the explicit one wins.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Entity { public int Id { get; set; } public string FullName { get; set; } = ""; }
                           public class Dto { public int Id { get; set; } public string Name { get; set; } = ""; }
                           [DwarfMapper] public partial class M
                           {
                               [ReverseMap]
                               [MapProperty(nameof(Entity.FullName), nameof(Dto.Name))]
                               public partial Dto ToDto(Entity e);

                               [MapProperty(nameof(Dto.Name), nameof(Entity.FullName))]
                               public partial Entity FromDto(Dto d);
                           }
                           """;
        var (diags, gen) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Id == "DWARF011"); // no DuplicateMapProperty
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("FullName = d.Name", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_inverse_method_reports_DWARF052()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Entity { public int Id { get; set; } }
                           public class Dto { public int Id { get; set; } }
                           [DwarfMapper] public partial class M
                           {
                               [ReverseMap]
                               public partial Dto ToDto(Entity e);
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF052"));
    }

    [Fact]
    public void Non_invertible_converter_reports_DWARF051_warning()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Entity { public int Id { get; set; } public int Code { get; set; } }
                           public class Dto { public int Id { get; set; } public string Code { get; set; } = ""; }
                           [DwarfMapper] public partial class M
                           {
                               [ReverseMap]
                               [MapProperty(nameof(Entity.Code), nameof(Dto.Code), Use = nameof(ToStr))]
                               public partial Dto ToDto(Entity e);
                               [MapProperty(nameof(Dto.Code), nameof(Entity.Code), Use = nameof(ToInt))]
                               public partial Entity FromDto(Dto d);
                               private static string ToStr(int v) => v.ToString();
                               private static int ToInt(string v) => int.Parse(v);
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        var d = Find(diags, "DWARF051");
        Assert.NotNull(d);
        Assert.Equal(DiagnosticSeverity.Warning, d!.Severity);
    }

    [Fact]
    public void Non_invertible_dotted_path_reports_DWARF051()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Inner { public string V { get; set; } = ""; }
                           public class Entity { public int Id { get; set; } public Inner Inner { get; set; } = new(); }
                           public class Dto { public int Id { get; set; } public string InnerV { get; set; } = ""; }
                           [DwarfMapper] public partial class M
                           {
                               [ReverseMap]
                               [MapProperty("Inner.V", nameof(Dto.InnerV))]
                               public partial Dto ToDto(Entity e);
                               [MapIgnore(nameof(Entity.Inner))]
                               public partial Entity FromDto(Dto d);
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF051"));
    }
}
