// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     NameConvention.Flexible (Phase 6): PascalCase / camelCase / snake_case / UPPER_CASE member names are
///     interchangeable (normalized by stripping '_' and lowercasing). A post-normalization collision — two
///     source members reducing to one target — is the build error DWARF048. Default (Exact) is unchanged.
/// </summary>
public class NamingConventionGeneratorTests
{
    private static Diagnostic? Find(IEnumerable<Diagnostic> diags, string id)
    {
        return diags.FirstOrDefault(d => d.Id == id);
    }

    [Theory]
    [InlineData("user_name", "UserName")] // snake → Pascal
    [InlineData("USER_NAME", "UserName")] // upper-snake → Pascal
    [InlineData("userName", "UserName")] // camel → Pascal
    [InlineData("UserName", "user_name")] // Pascal → snake (target side normalized too)
    public void Flexible_matches_across_casing_styles(string srcName, string tgtName)
    {
        var src = $$"""
                    using DwarfMapper;
                    namespace Demo;
                    public class S { public string {{srcName}} { get; set; } = ""; }
                    public class D { public string {{tgtName}} { get; set; } = ""; }
                    [DwarfMapper(NameConvention = NameConvention.Flexible)] public partial class M { public partial D Map(S s); }
                    """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Exact_default_does_not_match_snake_to_pascal()
    {
        // Without Flexible, snake_case and PascalCase do NOT match → DWARF001 (behaviour unchanged).
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public string user_name { get; set; } = ""; }
                           public class D { public string UserName { get; set; } = ""; }
                           [DwarfMapper] public partial class M { public partial D Map(S s); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF001"));
    }

    [Fact]
    public void Post_normalization_collision_reports_DWARF048()
    {
        // Source has both UserName and user_name → both normalize to "username" for target UserName.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public string UserName { get; set; } = ""; public string user_name { get; set; } = ""; }
                           public class D { public string UserName { get; set; } = ""; }
                           [DwarfMapper(NameConvention = NameConvention.Flexible)] public partial class M { public partial D Map(S s); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        var d = Find(diags, "DWARF048");
        Assert.NotNull(d);
        Assert.Equal(DiagnosticSeverity.Error, d!.Severity);
    }

    [Fact]
    public void Flexible_with_explicit_mapproperty_still_exact()
    {
        // Explicit [MapProperty] uses exact names even under Flexible; the rest auto-matches flexibly.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public string full_name { get; set; } = ""; public int user_id { get; set; } }
                           public class D { public string Name { get; set; } = ""; public int UserId { get; set; } }
                           [DwarfMapper(NameConvention = NameConvention.Flexible)] public partial class M
                           {
                               [MapProperty("full_name", nameof(D.Name))]
                               public partial D Map(S s);
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }
}
