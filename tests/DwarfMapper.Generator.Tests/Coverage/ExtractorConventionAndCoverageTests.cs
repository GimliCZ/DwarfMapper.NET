// SPDX-License-Identifier: GPL-2.0-only

// Coverage suite for three MapperExtractor.cs arms that each need one more thing than the existing fixtures had:
//   - EmitSourceCoverageFromConsumed's IgnoreObsoleteMembers arm — the PROJECTION endpoint's source-coverage gate
//     (RequiredMapping = Both), which the create/update fixtures for the same option never reach;
//   - the convention-method nameof list's ordinal sort, which only runs its key selector with two or more methods;
//   - RenderConstantLiteral's null literal, reached by a MapConfig `.Value(…, null)` constant.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ExtractorConventionAndCoverageTests
    {
        [Fact]
        public void Projection_source_coverage_does_not_report_an_obsolete_source_member_under_IgnoreObsoleteMembers()
        {
            const string src = """
                               using System;
                               using System.Linq;
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int A { get; set; } [Obsolete] public int Legacy { get; set; } }
                               public class D { public int A { get; set; } }
                               [DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both, IgnoreObsoleteMembers = true)]
                               public partial class M { public partial IQueryable<D> Project(IQueryable<S> src); }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            GeneratorAssert.DoesNotReport(src, "DWARF039");
            Assert.Contains("A = __s.A", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("Legacy", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Convention_methods_are_nameof_referenced_in_ordinal_order_not_declaration_order()
        {
            // Declared Zeta before Alpha; the emitted references are sorted so the generated file is stable under a
            // reorder of the mapper's members.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int A { get; set; } public int Unused { get; set; } }
                               public class D { public int A { get; set; } }
                               public class S2 { public int B { get; set; } public int Extra { get; set; } }
                               public class D2 { public int B { get; set; } }
                               [DwarfMapper]
                               public partial class M
                               {
                                   private static void Zeta(MapConfig<S, D> c) => c.IgnoreSource(s => s.Unused);
                                   private static void Alpha(MapConfig<S2, D2> c) => c.IgnoreSource(s => s.Extra);
                                   public partial D Map(S s);
                                   public partial D2 Map(S2 s);
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            var alpha = generated.IndexOf("_ = nameof(Alpha);", StringComparison.Ordinal);
            var zeta = generated.IndexOf("_ = nameof(Zeta);", StringComparison.Ordinal);
            Assert.True(alpha >= 0 && zeta > alpha, "expected nameof(Alpha) before nameof(Zeta)");
        }

        [Fact]
        public void A_map_config_null_value_constant_renders_the_null_literal()
        {
            // The cast picks the constant overload of Value; without it, `null` is ambiguous with the provider overload
            // (CS0121 in the consumer's own file).
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int A { get; set; } }
                               public class D { public int A { get; set; } public string? Label { get; set; } }
                               [DwarfMapper]
                               [GenerateMap<S, D>]
                               public partial class M
                               {
                                   private static void Cfg(MapConfig<S, D> c) => c.Value(t => t.Label, (string?)null);
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("Label = null", generated, StringComparison.Ordinal);
        }
    }
}
