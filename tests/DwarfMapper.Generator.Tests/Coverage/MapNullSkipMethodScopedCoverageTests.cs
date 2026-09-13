// SPDX-License-Identifier: GPL-2.0-only

// ReadMapNullSkip reads a method-scoped [MapNullSkip] / [MapNullSkip(bool)]: no argument means enabled (the constructor's
// default), a bool means itself. Two outcomes had never executed: an explicit `false` that carves one method out of a
// mapper-wide SkipNullSourceMembers, and a value that is not a bool at all — what a consumer has on screen mid-edit,
// with CS1503 already in the compilation. That one must fall back to the constructor's default rather than cast.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class MapNullSkipMethodScopedCoverageTests
    {
        private const string Types = """
                                     using DwarfMapper;
                                     namespace Demo;
                                     public class Src { public string? Name { get; set; } }
                                     public class Dst { public string? Name { get; set; } = "keep"; }
                                     """;

        [Fact]
        public void An_explicit_false_carves_the_method_out_of_a_mapper_wide_null_skip()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(Types + """
                                                                       [DwarfMapper(SkipNullSourceMembers = true)]
                                                                       public partial class M { [MapNullSkip(false)] public partial Dst Map(Src s); }
                                                                       """);

            Assert.Contains("Name = s.Name,", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("if (s.Name is not null)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_value_that_is_not_a_bool_falls_back_to_the_constructor_default_of_enabled()
        {
            var (_, generated) = GeneratorTestHarness.Run(Types + """
                                                                  [DwarfMapper]
                                                                  public partial class M { [MapNullSkip("x")] public partial Dst Map(Src s); }
                                                                  """);

            Assert.Contains("if (s.Name is not null) __dwarf_target.Name = s.Name;", generated, StringComparison.Ordinal);
        }
    }
}
