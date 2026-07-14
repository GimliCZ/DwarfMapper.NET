// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Additional mapping parameters (Phase 5): a map method may declare parameters after the source
///     (e.g. Dto Map(Entity e, string tenant)); each is matched to a destination by name (case-insensitively),
///     with precedence explicit > extra parameter > by-name member. Unused parameters surface DWARF047.
///     Extra parameters are deliberately NOT propagated into nested mappings.
/// </summary>
public class AdditionalParameterGeneratorTests
{
    private static Diagnostic? Find(IEnumerable<Diagnostic> diags, string id)
    {
        return diags.FirstOrDefault(d => d.Id == id);
    }

    [Fact]
    public void Extra_parameter_fills_destination_and_compiles()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int Id { get; set; } }
                           public class D { public int Id { get; set; } public string Tenant { get; set; } = ""; }
                           [DwarfMapper] public partial class M { public partial D Map(S s, string tenant); }
                           """;
        var (diags, gen) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("Map(global::Demo.S s, string tenant)", gen, StringComparison.Ordinal);
        Assert.Contains("Tenant = tenant", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Extra_parameter_matches_case_insensitively()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int Id { get; set; } }
                           public class D { public int Id { get; set; } public string Tenant { get; set; } = ""; }
                           [DwarfMapper] public partial class M { public partial D Map(S s, string TENANT); }
                           """;
        var (_, gen) = GeneratorTestHarness.Run(src);
        Assert.Contains("Tenant = TENANT", gen, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Extra_parameter_wins_over_by_name_member()
    {
        // Source also has a member named Note, but the extra parameter takes precedence.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int Id { get; set; } public string Note { get; set; } = "from-source"; }
                           public class D { public int Id { get; set; } public string Note { get; set; } = ""; }
                           [DwarfMapper] public partial class M { public partial D Map(S s, string note); }
                           """;
        var (diags, gen) = GeneratorTestHarness.Run(src);
        Assert.Contains("Note = note", gen, StringComparison.Ordinal);
        Assert.DoesNotContain("Note = s.Note", gen, StringComparison.Ordinal);
        // S.Note is now unconsumed, but default RequiredMapping=Target does not flag it.
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Explicit_mapproperty_wins_over_extra_parameter()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int Id { get; set; } public string Real { get; set; } = ""; }
                           public class D { public int Id { get; set; } public string Tenant { get; set; } = ""; }
                           [DwarfMapper] public partial class M
                           {
                               [MapProperty(nameof(S.Real), nameof(D.Tenant))]
                               public partial D Map(S s, string tenant);
                           }
                           """;
        var (_, gen) = GeneratorTestHarness.Run(src);
        Assert.Contains("Tenant = s.Real", gen, StringComparison.Ordinal);
        // tenant is now unused → DWARF047 suggestion.
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF047"));
    }

    [Fact]
    public void Extra_parameter_with_conversion_compiles()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int Id { get; set; } }
                           public class D { public int Id { get; set; } public long Seq { get; set; } }
                           [DwarfMapper] public partial class M { public partial D Map(S s, int seq); }
                           """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Multiple_extra_parameters_all_apply()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int Id { get; set; } }
                           public class D { public int Id { get; set; } public string Tenant { get; set; } = ""; public int Version { get; set; } }
                           [DwarfMapper] public partial class M { public partial D Map(S s, string tenant, int version); }
                           """;
        var (_, gen) = GeneratorTestHarness.Run(src);
        Assert.Contains("Tenant = tenant", gen, StringComparison.Ordinal);
        Assert.Contains("Version = version", gen, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Unused_extra_parameter_reports_DWARF047_info()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int Id { get; set; } }
                           public class D { public int Id { get; set; } }
                           [DwarfMapper] public partial class M { public partial D Map(S s, string unusedParam); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        var d = Find(diags, "DWARF047");
        Assert.NotNull(d);
        Assert.Equal(DiagnosticSeverity.Info, d!.Severity);
    }

    [Fact]
    public void Extra_parameter_not_propagated_to_nested_mapper()
    {
        // The nested Inner mapper must keep its single-parameter signature — the tenant param is not threaded.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Inner { public int X { get; set; } }
                           public class InnerD { public int X { get; set; } }
                           public class S { public int Id { get; set; } public Inner Inner { get; set; } = new(); }
                           public class D { public int Id { get; set; } public InnerD Inner { get; set; } = new(); public string Tenant { get; set; } = ""; }
                           [DwarfMapper] public partial class M { public partial D Map(S s, string tenant); }
                           """;
        var (diags, gen) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("Tenant = tenant", gen, StringComparison.Ordinal);
        // The synthesized nested mapper takes only the Inner source — the tenant param is not threaded in.
        Assert.DoesNotContain("global::Demo.Inner s, string tenant", gen, StringComparison.Ordinal);
    }
}
