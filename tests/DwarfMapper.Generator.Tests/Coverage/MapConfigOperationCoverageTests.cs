// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Coverage suite for MapperExtractor.MapConfig.cs's ReadMapConfig. MapConfig's runtime behaviour is exercised by
// DwarfMapper.IntegrationTests, whose generator run happens at that project's build time and is invisible to this
// assembly's coverage — so in Generator.Tests the successful MapWhen / Ignore / Construct / converter-Map paths, the
// unsupported-call and foreign-receiver skips, the Value-vs-Value conflict and several selector refusals had never run.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class MapConfigOperationCoverageTests
    {
        private static string Cfg(string body, string extraMembers = "", string classAttrs = "", string methods = "    public partial D Map(S s);\n") =>
            "using System;\nusing DwarfMapper;\nnamespace Demo;\n" +
            "public class S { public int A { get; set; } public int? N { get; set; } }\n" +
            "public class D { public int A { get; set; } public string Label { get; set; } = \"\"; public int Extra { get; set; } public int? N { get; set; } }\n" +
            "[DwarfMapper]\n" + classAttrs + "public partial class M\n{\n" + extraMembers +
            "    private static void Cfg(MapConfig<S, D> c)" + body + "\n" + methods + "}\n";

        private static string Message068(string src) =>
            Assert.Single(GeneratorAssert.Reports(src, "DWARF068")).GetMessage(CultureInfo.InvariantCulture);

        [Fact]
        public void Map_with_a_converter_method_group_calls_the_converter()
        {
            var src = Cfg(" => c.Map(t => t.Label, s => s.A, ToLabel).Ignore(t => t.Extra).Ignore(t => t.N);",
                "    private static string ToLabel(int a) => a.ToString(System.Globalization.CultureInfo.InvariantCulture);\n");

            Assert.Contains("Label = ToLabel(s.A),", GeneratorAssert.EmitsCompilableCode(src), StringComparison.Ordinal);
        }

        [Fact]
        public void MapWhen_guards_the_assignment_with_the_predicate()
        {
            var src = Cfg(" => c.MapWhen(t => t.A, s => s.A, Keep).Ignore(t => t.Label).Ignore(t => t.Extra);",
                "    private static bool Keep(S s) => s.A > 0;\n");

            Assert.Contains("if (Keep(s)) __dwarf_target.A = s.A;", GeneratorAssert.EmitsCompilableCode(src), StringComparison.Ordinal);
        }

        [Fact]
        public void Ignore_suppresses_completeness_for_the_selected_members()
        {
            var src = Cfg(" => c.Ignore(t => t.Label).Ignore(t => t.Extra);");

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            GeneratorAssert.DoesNotReport(src, "DWARF001");
            Assert.DoesNotContain("Label =", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Construct_builds_the_destination_through_the_factory()
        {
            var src = Cfg(" => c.Construct(Make).Ignore(t => t.Label).Ignore(t => t.Extra);",
                "    private static D Make(S s) => new D();\n", "[GenerateMap<S, D>]\n", "");

            Assert.Contains("var __dwarf_target = Make(src);", GeneratorAssert.EmitsCompilableCode(src), StringComparison.Ordinal);
        }

        [Fact]
        public void An_unsupported_call_on_the_config_is_refused_and_a_foreign_call_is_ignored()
        {
            // c.ToString() is a call on the MapConfig value that is no configuration op; GC.KeepAlive(c) is a call
            // whose receiver is not the config at all and must be skipped without a diagnostic of its own.
            var src = Cfg(" { c.Ignore(t => t.Label).Ignore(t => t.Extra); _ = c.ToString(); GC.KeepAlive(c); }");

            Assert.Contains("unsupported configuration call 'ToString' with 0 argument(s)", Message068(src), StringComparison.Ordinal);
        }

        [Fact]
        public void The_same_member_given_two_values_is_a_conflict()
        {
            var src = Cfg(" => c.Value(t => t.Extra, 1).Value(t => t.Extra, 2).Ignore(t => t.Label);");

            var message = Assert.Single(GeneratorAssert.Reports(src, "DWARF069")).GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("'Extra'", message, StringComparison.Ordinal);
        }

        [Fact]
        public void Map_with_a_computed_source_selector_is_refused()
        {
            var src = Cfg(" => c.Map(t => t.A, s => Twice(s.A)).Ignore(t => t.Label).Ignore(t => t.Extra);",
                "    private static int Twice(int a) => a * 2;\n");

            Assert.Contains("MapConfig Map: expected a member-access selector", Message068(src), StringComparison.Ordinal);
        }

        [Fact]
        public void MapWhen_with_an_inline_lambda_predicate_is_refused()
        {
            var src = Cfg(" => c.MapWhen(t => t.A, s => s.A, s => true).Ignore(t => t.Label).Ignore(t => t.Extra);");

            Assert.Contains("MapConfig MapWhen: expected member-access selectors and a predicate method group", Message068(src), StringComparison.Ordinal);
        }

        [Fact]
        public void MapOr_with_a_computed_source_selector_is_refused()
        {
            var src = Cfg(" => c.MapOr(t => t.N, s => Id(s.N), 0).Ignore(t => t.Label).Ignore(t => t.Extra);",
                "    private static int? Id(int? n) => n;\n");

            Assert.Contains("MapConfig MapOr: expected member-access selectors", Message068(src), StringComparison.Ordinal);
        }

        [Fact]
        public void A_block_bodied_selector_lambda_is_refused()
        {
            var src = Cfg(" => c.Ignore(t => { return t.Extra; }).Ignore(t => t.Label);");

            Assert.Contains("found 't => { return t.Extra; }'", Message068(src), StringComparison.Ordinal);
        }

        [Fact]
        public void A_selector_passed_as_a_delegate_field_is_refused()
        {
            var src = Cfg(" => c.Ignore(Sel).Ignore(t => t.Label);",
                "    private static readonly Func<D, int> Sel = t => t.Extra;\n");

            Assert.Contains("found 'Sel'", Message068(src), StringComparison.Ordinal);
        }
    }
}
