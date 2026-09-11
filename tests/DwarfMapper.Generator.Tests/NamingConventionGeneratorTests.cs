// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
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
            GeneratorAssert.CompilesClean(src);
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
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            // The whole sentence, because it is built from three literals and a join: the target, the
            // candidates in declaration order SEPARATED so the reader can tell them apart, and the remedy.
            // The mutation leg blanked each piece (and the ", " between the names) without a failure.
            Assert.Contains("target 'UserName' matches multiple source members under NameConvention.Flexible (UserName, user_name); disambiguate with [MapProperty]",
                d.GetMessage(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        [Fact]
        public void A_collision_is_refused_before_either_candidate_is_resolved()
        {
            // The ambiguity arm `continue`s before the resolver sees a candidate. Without that, the FIRST
            // candidate would be resolved as if it had won — and here it is a class that cannot become a
            // string, so the caller would read DWARF005 about a conversion they never asked for on top of the
            // collision that is the real problem.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Blob { }
                               public class S { public Blob UserName { get; set; } = new(); public string user_name { get; set; } = ""; }
                               public class D { public string UserName { get; set; } = ""; }
                               [DwarfMapper(NameConvention = NameConvention.Flexible)] public partial class M { public partial D Map(S s); }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src);
            Assert.NotNull(Find(diags, "DWARF048"));
            Assert.Equal(["DWARF048"], diags.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).Distinct().ToArray());
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
            GeneratorAssert.CompilesClean(src);
        }
    }
}
