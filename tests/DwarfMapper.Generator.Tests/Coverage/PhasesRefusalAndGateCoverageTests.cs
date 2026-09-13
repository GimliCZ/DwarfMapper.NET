// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Coverage suite for MapperExtractor.Phases.cs arms that only a refusal or a gate reaches:
//   - a top-level collection return whose ELEMENT conversion fails, at the declared partial and at [GenerateMap];
//   - the block-copy gate's two-parameter [AfterMap] question;
//   - DWARF030's identity self-map pattern, for a recursion-capable class and for a value-type parameter.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class PhasesRefusalAndGateCoverageTests
    {
        private const string Dwarf005 = "DWARF005";
        private const string Dwarf030 = "DWARF030";

        private const string UnconstructibleElement = """
                                                      public class Src { public int A { get; set; } }
                                                      public class Dst { private Dst() { } public int A { get; set; } }
                                                      """;

        [Fact]
        public void Declared_list_return_whose_element_cannot_be_mapped_is_refused()
        {
            var src = "using System.Collections.Generic;\nusing DwarfMapper;\nnamespace Demo;\n" + UnconstructibleElement + """
                                                                                                                       [DwarfMapper]
                                                                                                                       public partial class M { public partial List<Dst> Map(List<Src> s); }
                                                                                                                       """;

            var message = Assert.Single(GeneratorAssert.Reports(src, Dwarf005)).GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("'Map'", message, StringComparison.Ordinal);
        }

        [Fact]
        public void Generate_map_list_pair_whose_element_cannot_be_mapped_is_refused()
        {
            var src = "using System.Collections.Generic;\nusing DwarfMapper;\nnamespace Demo;\n" + UnconstructibleElement + """
                                                                                                                       [DwarfMapper]
                                                                                                                       [GenerateMap<List<Src>, List<Dst>>]
                                                                                                                       public partial class M { }
                                                                                                                       """;

            var message = Assert.Single(GeneratorAssert.Reports(src, Dwarf005)).GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("'Map'", message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_two_parameter_after_hook_on_the_element_pair_keeps_a_struct_list_off_the_block_copy()
        {
            // P -> P2 is layout-identical and would otherwise block-copy, which cannot call the hook.
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public struct P { public int X; public int Y; }
                               public struct P2 { public int X; public int Y; }
                               public class Src { public List<P> Items { get; set; } = new(); }
                               public class Dst { public List<P2> Items { get; set; } = new(); }
                               [DwarfMapper]
                               public partial class M
                               {
                                   public partial Dst Map(Src s);
                                   [AfterMap] private static void Finish(P s, ref P2 d) { }
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("__d[__i++] = __DwarfMap_Obj_global__Demo_P_global__Demo_P2_", generated, StringComparison.Ordinal);
            Assert.Contains("Finish(s, ref __dwarf_target);", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Preserve_recursive_self_map_through_constructor_arguments_is_refused()
        {
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Node
                               {
                                   public Node(int v, List<Node> kids) { V = v; Kids = kids; }
                                   public int V { get; }
                                   public List<Node> Kids { get; }
                               }
                               [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                               public partial class M { public partial Node Map(Node n); }
                               """;

            var message = GeneratorAssert.Reports(src, Dwarf030)[0].GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("ReferenceHandling=Preserve", message, StringComparison.Ordinal);
        }

        [Fact]
        public void Preserve_value_type_self_map_with_constructor_arguments_is_not_a_cycle()
        {
            // A struct cannot be on a reference cycle, so the identity self-map pattern skips it outright.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public readonly record struct P(int X, int Y);
                               [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                               public partial class M { public partial P Map(P p); }
                               """;

            GeneratorAssert.DoesNotReport(src, Dwarf030);
        }
    }
}
