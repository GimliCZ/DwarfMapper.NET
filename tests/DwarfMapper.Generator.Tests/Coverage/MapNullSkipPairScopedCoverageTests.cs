// SPDX-License-Identifier: GPL-2.0-only

// ReadPairNullSkips reads the pair-scoped [MapNullSkip<TSource, TTarget>] / [MapNullSkip<TSource, TTarget>(bool)] on a
// mapper class: no argument means enabled, a bool means itself. As for the method-scoped form, two outcomes had never
// executed: an explicit `false` that turns the pair off under a mapper-wide SkipNullSourceMembers, and a value that is
// not a bool — mid-edit, with CS1503 already in the compilation — which must fall back to the constructor's default.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class MapNullSkipPairScopedCoverageTests
    {
        private const string Types = """
                                     using DwarfMapper;
                                     namespace Demo;
                                     public class Src { public string? Name { get; set; } }
                                     public class Dst { public string? Name { get; set; } = "keep"; }
                                     """;

        [Fact]
        public void An_explicit_false_turns_the_pair_off_under_a_mapper_wide_null_skip()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(Types + """
                                                                       [DwarfMapper(SkipNullSourceMembers = true)]
                                                                       [MapNullSkip<Src, Dst>(false)]
                                                                       public partial class M { public partial Dst Map(Src s); }
                                                                       """);

            Assert.Contains("Name = s.Name,", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("if (s.Name is not null)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_value_that_is_not_a_bool_falls_back_to_the_constructor_default_of_enabled()
        {
            var (_, generated) = GeneratorTestHarness.Run(Types + """
                                                                  [DwarfMapper]
                                                                  [MapNullSkip<Src, Dst>("x")]
                                                                  public partial class M { public partial Dst Map(Src s); }
                                                                  """);

            Assert.Contains("if (s.Name is not null) __dwarf_target.Name = s.Name;", generated, StringComparison.Ordinal);
        }
    }
}
