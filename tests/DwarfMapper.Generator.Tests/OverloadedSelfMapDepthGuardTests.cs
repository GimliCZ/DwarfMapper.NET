// SPDX-License-Identifier: GPL-2.0-only

// An overloaded self-map must get the same depth guard as a uniquely named one.
//
// DetectDeclaredMethodsOnRecursionCycle keyed call-graph edges by the converter's bare NAME. For an overloaded name it
// fanned the edge out to every overload EXCEPT the caller — the only safe reading of a name that does not say which
// overload it meant. For `NodeDto Map(Node)` beside `OtherDto Map(Other)` that excluded exactly the real edge
// (Map(Node) → Map(Node) through `Next`), so no cycle was found, no depth companion was synthesized, and the emitted
// `Next = Map(n.Next)` recursed without a bound: a StackOverflow on a cyclic graph instead of the
// DwarfMappingDepthException the uniquely named `Map(Node)` + `Convert(Other)` has always thrown. Resolution now
// records which overload it adopted (MemberMap.ConverterParamTypeFqn) and the edge points at that one.
namespace DwarfMapper.Generator.Tests
{
    public class OverloadedSelfMapDepthGuardTests
    {
        private const string Types = """
                                     using DwarfMapper;
                                     namespace Demo;
                                     public class Node { public int V { get; set; } public Node? Next { get; set; } }
                                     public class Other { public int B { get; set; } }
                                     public class OtherDto { public int B { get; set; } }

                                     """;

        private const string Companion =
            "private global::Demo.NodeDto __DwarfMap_Depth_Map(global::Demo.Node n, global::DwarfMapper.DwarfRefContext ctx, int depth)";

        [Fact]
        public void An_overloaded_self_map_through_a_member_is_depth_guarded()
        {
            var src = Types + """
                              public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                              [DwarfMapper]
                              public partial class M
                              {
                                  public partial NodeDto Map(Node n);
                                  public partial OtherDto Map(Other o);
                              }
                              """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains(Companion, generated, StringComparison.Ordinal);
            Assert.Contains("Next = __DwarfMap_Depth_Map(n.Next!, __dwarf_ctx, 0),", generated, StringComparison.Ordinal);
            Assert.Contains("Next = __DwarfMap_Depth_Map(n.Next!, ctx, depth + 1),", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("Next = Map(n.Next)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void An_overloaded_self_map_through_a_constructor_argument_is_depth_guarded()
        {
            var src = Types + """
                              public record NodeDto(int V, NodeDto? Next);
                              [DwarfMapper]
                              public partial class M
                              {
                                  public partial NodeDto Map(Node n);
                                  public partial OtherDto Map(Other o);
                              }
                              """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains(Companion, generated, StringComparison.Ordinal);
            Assert.Contains("__DwarfMap_Depth_Map(n.Next!, ctx, depth + 1)", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("Map(n.Next)", generated.Replace("__DwarfMap_Depth_Map(n.Next", "", StringComparison.Ordinal), StringComparison.Ordinal);
        }

        [Fact]
        public void An_overloaded_self_map_through_an_unflatten_leaf_is_depth_guarded()
        {
            // The unflatten leaf was the one member site c56b9e5 did not give the adopted overload: it discarded it
            // (`out _`), so the leaf's edge was the bare name and fanned out to every overload but the caller.
            // Emitted `__dwarf_target.Wrap.Inner = Map(s.Child);` — unbounded on a cyclic graph — while the
            // distinct-name twin was guarded.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Other { public int B { get; set; } }
                               public class OtherDto { public int B { get; set; } }
                               public class Src { public int V { get; set; } public Src? Child { get; set; } }
                               public class Wrap { public Dst? Inner { get; set; } }
                               public class Dst { public int V { get; set; } public Wrap Wrap { get; set; } = new(); }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapProperty("Child", "Wrap.Inner", Use = "Map")]
                                   public partial Dst Map(Src s);
                                   public partial OtherDto Map(Other o);
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("private global::Demo.Dst __DwarfMap_Depth_Map(global::Demo.Src s, global::DwarfMapper.DwarfRefContext ctx, int depth)", generated, StringComparison.Ordinal);
            Assert.Contains("__dwarf_target.Wrap.Inner = __DwarfMap_Depth_Map(s.Child!, __dwarf_ctx, 0);", generated, StringComparison.Ordinal);
            Assert.Contains("__dwarf_target.Wrap.Inner = __DwarfMap_Depth_Map(s.Child!, ctx, depth + 1);", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("Wrap.Inner = Map(", generated, StringComparison.Ordinal);
        }
    }
}
