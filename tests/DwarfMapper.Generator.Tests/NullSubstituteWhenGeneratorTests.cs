// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     [MapProperty] NullSubstitute / When (Phase 8). NullSubstitute coalesces a null source to a constant
///     (`source ?? value`, direct members only; type-checked → DWARF049). When guards the assignment with a
///     bool predicate taking the source (`if (Pred(src)) target = …;`, member keeps its default otherwise;
///     invalid predicate → DWARF050).
/// </summary>
public class NullSubstituteWhenGeneratorTests
{
    private static Diagnostic? Find(IEnumerable<Diagnostic> diags, string id)
    {
        return diags.FirstOrDefault(d => d.Id == id);
    }

    private static int Count(string h, string n)
    {
        int c = 0, i = 0;
        while ((i = h.IndexOf(n, i, StringComparison.Ordinal)) >= 0)
        {
            c++;
            i += n.Length;
        }

        return c;
    }

    [Fact]
    public void NullSubstitute_emits_coalesce_and_compiles()
    {
        const string src = """
                           using DwarfMapper;
                           #nullable enable
                           namespace Demo;
                           public class S { public string? Name { get; set; } }
                           public class D { public string Name { get; set; } = ""; }
                           [DwarfMapper] public partial class M
                           {
                               [MapProperty(nameof(S.Name), nameof(D.Name), NullSubstitute = "(none)")]
                               public partial D Map(S s);
                           }
                           """;
        var gen = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);
        Assert.Contains("s.Name ?? \"(none)\"", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void When_emits_guarded_assignment_not_in_initializer()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int Tier { get; set; } public int Bonus { get; set; } }
                           public class D { public int Tier { get; set; } public int Bonus { get; set; } }
                           [DwarfMapper] public partial class M
                           {
                               [MapProperty(nameof(S.Bonus), nameof(D.Bonus), When = nameof(Eligible))]
                               public partial D Map(S s);
                               private static bool Eligible(S s) => s.Tier > 0;
                           }
                           """;
        var gen = GeneratorAssert.CompilesClean(src);
        Assert.Contains("if (Eligible(s)) __dwarf_target.Bonus = s.Bonus", gen, StringComparison.Ordinal);
        // The guarded member must NOT also be assigned unconditionally in the initializer.
        Assert.Equal(1, Count(gen, "Bonus = s.Bonus"));
    }

    [Fact]
    public void NullSubstitute_type_mismatch_reports_DWARF049()
    {
        const string src = """
                           using DwarfMapper;
                           #nullable enable
                           namespace Demo;
                           public class S { public string? Name { get; set; } }
                           public class D { public int Name { get; set; } }
                           [DwarfMapper] public partial class M
                           {
                               [MapProperty(nameof(S.Name), nameof(D.Name), NullSubstitute = "x")]
                               public partial D Map(S s);
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
        Assert.NotNull(Find(diags, "DWARF049"));
    }

    [Fact]
    public void NullSubstitute_with_converter_reports_DWARF049()
    {
        const string src = """
                           using DwarfMapper;
                           #nullable enable
                           namespace Demo;
                           public class S { public string? Code { get; set; } }
                           public class D { public int Code { get; set; } }
                           [DwarfMapper] public partial class M
                           {
                               [MapProperty(nameof(S.Code), nameof(D.Code), Use = nameof(Conv), NullSubstitute = 0)]
                               public partial D Map(S s);
                               private static int Conv(string? v) => v is null ? 0 : v.Length;
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
        Assert.NotNull(Find(diags, "DWARF049"));
    }

    [Theory]
    [InlineData("private static int Pred(S s) => 1;")] // non-bool return
    [InlineData("private static bool Pred(int x) => true;")] // param not source-assignable
    [InlineData("")] // missing method
    public void Invalid_when_predicate_reports_DWARF050(string member)
    {
        var src = $$"""
                    using DwarfMapper;
                    namespace Demo;
                    public class S { public int Bonus { get; set; } }
                    public class D { public int Bonus { get; set; } }
                    [DwarfMapper] public partial class M
                    {
                        [MapProperty(nameof(S.Bonus), nameof(D.Bonus), When = "Pred")]
                        public partial D Map(S s);
                        {{member}}
                    }
                    """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF050"));
    }
}
