// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Coverage suite for MapperExtractor.Flatten.cs:
//   - ResolveUnflattenTarget's two source refusals — a dotted source path with a missing segment (DWARF043) and an
//     unknown simple source (DWARF009) — which every unflatten fixture had supplied a valid source for;
//   - ResolveFlattenInfos' refusal of a [Flatten] root that has no readable sub-members (DWARF016);
//   - the [FlattenGraph] flat-node leaf emitter's null-handling arms. The emitter's own comment called reaching them
//     "a guard-inheritance fix rather than a measured repro"; a nullable value leaf under each NullStrategy, with and
//     without a converter, reaches every one of them through the public surface.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class FlattenAndFlatNodeCoverageTests
    {
        private static string SingleMessage(string src, string id) =>
            Assert.Single(GeneratorAssert.Reports(src, id)).GetMessage(CultureInfo.InvariantCulture);

        [Fact]
        public void Unflatten_from_a_dotted_source_path_with_a_missing_segment_is_refused()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Addr { public string City { get; set; } = ""; }
                               public class Inner { public string Town { get; set; } = ""; }
                               public class S { public Inner Inner { get; set; } = new(); }
                               public class D { public Addr Address { get; set; } = new(); }
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty("Inner.Nope", "Address.City")]
                                   public partial D Map(S s);
                               }
                               """;

            Assert.Contains("source path 'Inner.Nope' has no member 'Nope'", SingleMessage(src, "DWARF043"), StringComparison.Ordinal);
        }

        [Fact]
        public void Unflatten_from_an_unknown_source_member_is_refused()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Addr { public string City { get; set; } = ""; }
                               public class S { public string Town { get; set; } = ""; }
                               public class D { public Addr Address { get; set; } = new(); }
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty("Nope", "Address.City")]
                                   public partial D Map(S s);
                               }
                               """;

            Assert.Contains("'Nope'", SingleMessage(src, "DWARF009"), StringComparison.Ordinal);
        }

        [Fact]
        public void Flatten_of_a_root_with_no_readable_members_is_refused()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Empty { }
                               public class S { public Empty E { get; set; } = new(); public int A { get; set; } }
                               public class D { public int A { get; set; } }
                               [DwarfMapper] public partial class M
                               {
                                   [Flatten(nameof(S.E))]
                                   public partial D Map(S s);
                               }
                               """;

            Assert.Contains("'E'", SingleMessage(src, "DWARF016"), StringComparison.Ordinal);
        }

        private static string Graph(string leafSrc, string leafDst, string options = "") =>
            "using DwarfMapper;\nusing System.Collections.Generic;\nnamespace Demo;\n" +
            "public enum E1 { A, B } public enum E2 { A, B }\n" +
            "public class Node { " + leafSrc + " public Node? Next { get; set; } }\n" +
            "public class NodeDto { " + leafDst + " }\n" +
            "public class Root { public List<Node> Entries { get; set; } = new(); }\n" +
            "public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }\n" +
            "[DwarfMapper" + options + "]\npublic partial class M\n{\n    [FlattenGraph(\"Entries\", \"Nodes\")]\n    public partial RootDto Map(Root r);\n}\n";

        [Fact]
        public void Flat_node_nullable_leaf_without_a_converter_throws_by_default()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(Graph("public int? V { get; set; }", "public int V { get; set; }"));
            Assert.Contains("V = n.V ?? throw new global::System.InvalidOperationException(\"Source member 'V' was null\"),", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Flat_node_nullable_leaf_without_a_converter_takes_the_default_under_SetDefault()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(
                Graph("public int? V { get; set; }", "public int V { get; set; }", "(NullStrategy = NullStrategy.SetDefault)"));
            Assert.Contains("V = n.V.GetValueOrDefault(),", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Flat_node_nullable_leaf_through_a_converter_throws_by_default()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(Graph("public E1? V { get; set; }", "public E2 V { get; set; }"));
            Assert.Contains("(n.V ?? throw new global::System.InvalidOperationException(\"Source member 'V' was null\")),", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Flat_node_nullable_leaf_through_a_converter_takes_the_default_under_SetDefault()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(
                Graph("public E1? V { get; set; }", "public E2 V { get; set; }", "(NullStrategy = NullStrategy.SetDefault)"));
            Assert.Contains("(n.V.GetValueOrDefault()),", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Flat_node_nullable_leaf_into_a_nullable_member_lifts_through_the_converter()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(Graph("public E1? V { get; set; }", "public E2? V { get; set; }"));
            Assert.Contains("V = n.V.HasValue ? __DwarfMap_EnumName_", generated, StringComparison.Ordinal);
            Assert.Contains("(n.V.Value) : null,", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Flat_node_nullable_reference_leaf_into_a_nullable_member_guards_the_declared_converter()
        {
            const string src = """
                               #nullable enable
                               using DwarfMapper;
                               using System.Collections.Generic;
                               namespace Demo;
                               public class Child { public int X { get; set; } }
                               public class ChildDto { public int X { get; set; } }
                               public class Node { public Child? C { get; set; } public Node? Next { get; set; } }
                               public class NodeDto { public ChildDto? C { get; set; } }
                               public class Root { public List<Node> Entries { get; set; } = new(); }
                               public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                               [DwarfMapper]
                               public partial class M
                               {
                                   public partial ChildDto ToChild(Child c);
                                   [FlattenGraph("Entries", "Nodes")]
                                   public partial RootDto Map(Root r);
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("C = n.C is null ? null : ToChild(n.C),", generated, StringComparison.Ordinal);
        }
    }
}
