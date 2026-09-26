// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     The [FlattenGraph] shapes that are accepted but were never exercised: destination collections declared
    ///     as interfaces, a dictionary-valued source navigation, and the heterogeneous traversal's array,
    ///     enumerable and dictionary entry points.
    /// </summary>
    /// <remarks>
    ///     Every existing FlattenGraph test declares its destination as a concrete <c>List&lt;T&gt;</c> and its
    ///     source navigation as a single reference. The resolver accepts four more destination shapes and three
    ///     more navigation shapes, and a round-27 coverage measurement found all of them executed by nothing —
    ///     each one a separate emission path, written and compiled and never run.
    /// </remarks>
    public class FlattenGraphShapeCoverageTests
    {
        private const string Models = """
                                      using DwarfMapper;
                                      using System.Collections.Generic;
                                      namespace Demo;
                                      public class Node { public int Id { get; set; } public Node? Next { get; set; } }
                                      public class NodeDto { public int Id { get; set; } }

                                      """;

        private const string FsModels = """
                                        using DwarfMapper;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public abstract class FsNode { public string Name { get; set; } = ""; }
                                        public class Folder : FsNode { public List<FsNode> Children { get; set; } = new(); }
                                        public class FileNode : FsNode { public long Size { get; set; } }
                                        public abstract class FsNodeDto { public string Name { get; set; } = ""; }
                                        public class FolderDto : FsNodeDto { public List<FsNodeDto>? Children { get; set; } }
                                        public class FileNodeDto : FsNodeDto { public long Size { get; set; } }
                                        public class TreeDto { public List<FsNodeDto> Nodes { get; set; } = new(); }

                                        """;

        /// <summary>A homogeneous mapper whose destination collection member is declared as <paramref name="dest" />.</summary>
        private static string Mapper(string dest)
        {
            return Models
                   + "public class Root { public Node? Entry { get; set; } }\n"
                   + "public class RootDto { " + dest + " }\n"
                   + "[DwarfMapper]\n"
                   + "public partial class M\n"
                   + "{\n"
                   + "    [FlattenGraph(nameof(Root.Entry), nameof(RootDto.Nodes))]\n"
                   + "    public partial RootDto Map(Root r);\n"
                   + "}\n";
        }

        /// <summary>A heterogeneous mapper whose entry-point member is declared as <paramref name="entry" />.</summary>
        private static string HeteroMapper(string entry)
        {
            return FsModels
                   + "public class Tree { " + entry + " }\n"
                   + "[DwarfMapper]\n"
                   + "public partial class M\n"
                   + "{\n"
                   + "    [FlattenGraph(nameof(Tree.Roots), nameof(TreeDto.Nodes))]\n"
                   + "    [MapDerivedType<Folder, FolderDto>]\n"
                   + "    [MapDerivedType<FileNode, FileNodeDto>]\n"
                   + "    public partial TreeDto Map(Tree t);\n"
                   + "}\n";
        }

        // ── Destination declared as an interface rather than List<T> ────────────

        [Fact]
        public void Destination_declared_as_ICollection_is_accepted()
        {
            GeneratorAssert.EmitsCompilableCode(
                Mapper("public ICollection<NodeDto> Nodes { get; set; } = new List<NodeDto>();"));
        }

        [Fact]
        public void Destination_declared_as_IReadOnlyCollection_is_accepted()
        {
            GeneratorAssert.EmitsCompilableCode(
                Mapper("public IReadOnlyCollection<NodeDto> Nodes { get; set; } = new List<NodeDto>();"));
        }

        [Fact]
        public void Destination_declared_as_IList_is_accepted()
        {
            GeneratorAssert.EmitsCompilableCode(
                Mapper("public IList<NodeDto> Nodes { get; set; } = new List<NodeDto>();"));
        }

        [Fact]
        public void Destination_declared_as_IEnumerable_is_accepted()
        {
            GeneratorAssert.EmitsCompilableCode(
                Mapper("public IEnumerable<NodeDto> Nodes { get; set; } = new List<NodeDto>();"));
        }

        // ── Source navigation shapes ────────────────────────────────────────────

        /// <summary>A navigation that is a DICTIONARY: its values are the graph entry points.</summary>
        [Fact]
        public void A_dictionary_valued_navigation_is_traversed_by_its_values()
        {
            var src = Models
                      + "public class Root { public Dictionary<string, Node> Entries { get; set; } = new(); }\n"
                      + "public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }\n"
                      + "[DwarfMapper]\n"
                      + "public partial class M\n"
                      + "{\n"
                      + "    [FlattenGraph(nameof(Root.Entries), nameof(RootDto.Nodes))]\n"
                      + "    public partial RootDto Map(Root r);\n"
                      + "}\n";

            GeneratorAssert.EmitsCompilableCode(src);
        }

        /// <summary>A navigation that is neither a reference nor a collection is refused, not mis-emitted.</summary>
        [Fact]
        public void A_value_typed_navigation_reports_DWARF034()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Generic;
                               namespace Demo;
                               public class NodeDto { public int Id { get; set; } }
                               public class Root { public int Entry { get; set; } }
                               public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [FlattenGraph(nameof(Root.Entry), nameof(RootDto.Nodes))]
                                   public partial RootDto Map(Root r);
                               }
                               """;

            GeneratorAssert.Reports(src, "DWARF034");
        }

        // ── Heterogeneous traversal entry points ────────────────────────────────

        [Fact]
        public void Hetero_traversal_accepts_an_array_entry_point()
        {
            GeneratorAssert.EmitsCompilableCode(
                HeteroMapper("public FsNode[] Roots { get; set; } = System.Array.Empty<FsNode>();"));
        }

        [Fact]
        public void Hetero_traversal_accepts_an_enumerable_entry_point()
        {
            GeneratorAssert.EmitsCompilableCode(
                HeteroMapper("public IEnumerable<FsNode> Roots { get; set; } = new List<FsNode>();"));
        }

        [Fact]
        public void Hetero_traversal_accepts_a_dictionary_entry_point()
        {
            GeneratorAssert.EmitsCompilableCode(
                HeteroMapper("public Dictionary<string, FsNode> Roots { get; set; } = new();"));
        }

        /// <summary>Two [MapDerivedType] arms naming the SAME source: a duplicate, not a widening, and refused.</summary>
        [Fact]
        public void Duplicate_MapDerivedType_source_reports_DWARF035()
        {
            var src = FsModels
                      + "public class Tree { public FsNode? Root { get; set; } }\n"
                      + "[DwarfMapper]\n"
                      + "public partial class M\n"
                      + "{\n"
                      + "    [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]\n"
                      + "    [MapDerivedType<Folder, FolderDto>]\n"
                      + "    [MapDerivedType<Folder, FolderDto>]\n"
                      + "    public partial TreeDto Map(Tree t);\n"
                      + "}\n";

            GeneratorAssert.Reports(src, "DWARF035");
        }

        // ── Homogeneous navigation: the remaining two entry-point shapes ────────

        [Fact]
        public void An_array_navigation_is_traversed_element_by_element()
        {
            var src = Models
                      + "public class Root { public Node[] Entries { get; set; } = System.Array.Empty<Node>(); }\n"
                      + "public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }\n"
                      + "[DwarfMapper]\n"
                      + "public partial class M\n"
                      + "{\n"
                      + "    [FlattenGraph(nameof(Root.Entries), nameof(RootDto.Nodes))]\n"
                      + "    public partial RootDto Map(Root r);\n"
                      + "}\n";

            GeneratorAssert.EmitsCompilableCode(src);
        }

        /// <summary>
        ///     An ARRAY destination wraps the traversal in a ToArray helper whose parameter must match the traversal
        ///     helper's own: an array navigation hands over <c>TNode[]?</c> and any other collection
        ///     <c>IEnumerable&lt;TNode&gt;?</c>. Every array-destination fixture navigated from a single reference,
        ///     so neither collection form of the wrapper was emitted.
        /// </summary>
        [Theory]
        [InlineData("public Node[]? Entry { get; set; }", "(global::Demo.Node[]? entry)")]
        [InlineData("public List<Node>? Entry { get; set; }", "(global::System.Collections.Generic.IEnumerable<global::Demo.Node>? entry)")]
        public void An_array_destination_wraps_a_collection_navigation_with_a_matching_parameter(string navigation, string wrapperParameters)
        {
            var src = Models
                      + "public class Root { " + navigation + " }\n"
                      + "public class RootDto { public NodeDto[] Nodes { get; set; } = System.Array.Empty<NodeDto>(); }\n"
                      + "[DwarfMapper]\n"
                      + "public partial class M\n"
                      + "{\n"
                      + "    [FlattenGraph(nameof(Root.Entry), nameof(RootDto.Nodes))]\n"
                      + "    public partial RootDto Map(Root r);\n"
                      + "}\n";

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            Assert.Matches(@"__DwarfMap_FlattenGraphArr_\w+" + System.Text.RegularExpressions.Regex.Escape(wrapperParameters), generated);
        }

        /// <summary>
        ///     The node DTO gate refuses a type the flat-node helper cannot <c>new</c>, and it asks two questions: is
        ///     it a class or struct at all, and is it one of the special types. An interface fails the first; a
        ///     <c>string</c> is a class that fails the second.
        /// </summary>
        [Theory]
        [InlineData("public interface NodeDto { int Id { get; set; } }", "NodeDto", "List<NodeDto>")]
        [InlineData("", "String", "List<string>")]
        public void A_node_dto_that_is_not_a_plain_class_or_struct_reports_DWARF034(string dtoDeclaration, string reportedName, string destination)
        {
            var src = """
                      using DwarfMapper;
                      using System.Collections.Generic;
                      namespace Demo;
                      public class Node { public int Id { get; set; } public Node? Next { get; set; } }

                      """
                      + dtoDeclaration + "\n"
                      + "public class Root { public Node? Entry { get; set; } }\n"
                      + "public class RootDto { public " + destination + " Nodes { get; set; } = new(); }\n"
                      + "[DwarfMapper]\n"
                      + "public partial class M\n"
                      + "{\n"
                      + "    [FlattenGraph(nameof(Root.Entry), nameof(RootDto.Nodes))]\n"
                      + "    public partial RootDto Map(Root r);\n"
                      + "}\n";

            var (diagnostics, _) = GeneratorTestHarness.Run(src);

            var dwarf034 = Assert.Single(diagnostics, d => d.Id == "DWARF034");
            Assert.Contains($"node DTO type '{reportedName}' is not constructible",
                dwarf034.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        [Fact]
        public void An_enumerable_navigation_is_traversed_element_by_element()
        {
            var src = Models
                      + "public class Root { public IEnumerable<Node> Entries { get; set; } = new List<Node>(); }\n"
                      + "public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }\n"
                      + "[DwarfMapper]\n"
                      + "public partial class M\n"
                      + "{\n"
                      + "    [FlattenGraph(nameof(Root.Entries), nameof(RootDto.Nodes))]\n"
                      + "    public partial RootDto Map(Root r);\n"
                      + "}\n";

            GeneratorAssert.EmitsCompilableCode(src);
        }

        /// <summary>
        ///     A STRUCT node DTO is constructible — DWARF034 accepts "a class or struct with a public constructor" —
        ///     so the flat-node helper must compile for it. Its null guard answered <c>return null!;</c>
        ///     unconditionally, which a value type rejects with CS0037 in a file the consumer did not write.
        /// </summary>
        [Fact]
        public void A_struct_node_dto_emits_a_flat_node_helper_that_compiles()
        {
            var src = Models.Replace("public class NodeDto", "public struct NodeDto", StringComparison.Ordinal)
                      + "public class Root { public Node? Entry { get; set; } }\n"
                      + "public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }\n"
                      + "[DwarfMapper]\n"
                      + "public partial class M\n"
                      + "{\n"
                      + "    [FlattenGraph(nameof(Root.Entry), nameof(RootDto.Nodes))]\n"
                      + "    public partial RootDto Map(Root r);\n"
                      + "}\n";

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            Assert.Contains("if (n is null) return default;", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A node DTO the generated code could not construct is refused up front, rather than emitting a
        ///     <c>new</c> the consumer's compiler would reject in a file they did not write.
        /// </summary>
        [Fact]
        public void A_non_constructible_node_dto_reports_DWARF034()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Generic;
                               namespace Demo;
                               public class Node { public int Id { get; set; } public Node? Next { get; set; } }
                               public class NodeDto { private NodeDto() { } public int Id { get; set; } }
                               public class Root { public Node? Entry { get; set; } }
                               public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [FlattenGraph(nameof(Root.Entry), nameof(RootDto.Nodes))]
                                   public partial RootDto Map(Root r);
                               }
                               """;

            GeneratorAssert.Reports(src, "DWARF034");
        }

        /// <summary>
        ///     When the node pair ALREADY has a declared mapping method, the directive reuses it rather than
        ///     synthesizing a second one — otherwise the author's own configuration would be silently bypassed.
        /// </summary>
        [Fact]
        public void A_declared_node_mapper_is_reused_rather_than_synthesized_again()
        {
            var src = Models
                      + "public class Root { public Node? Entry { get; set; } }\n"
                      + "public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }\n"
                      + "[DwarfMapper]\n"
                      + "public partial class M\n"
                      + "{\n"
                      + "    [FlattenGraph(nameof(Root.Entry), nameof(RootDto.Nodes))]\n"
                      + "    public partial RootDto Map(Root r);\n"
                      + "    public partial NodeDto MapNode(Node n);\n"
                      + "}\n";

            GeneratorAssert.EmitsCompilableCode(src);
        }

        // ── Heterogeneous: the edge shapes a DERIVED node can carry ─────────────
        //
        // The hetero arm partitions each derived type's members separately from the homogeneous one, in code
        // that reads almost identically. Every existing test gives its derived types one shape — a
        // List<FsNode> — so the single-reference, dictionary and array branches were never run.

        private static string HeteroWithFileNodeEdge(string extraMember)
        {
            return FsModels.Replace(
                       "public class FileNode : FsNode { public long Size { get; set; } }",
                       "public class FileNode : FsNode { public long Size { get; set; } " + extraMember + " }",
                       StringComparison.Ordinal)
                   + "public class Tree { public FsNode? Root { get; set; } }\n"
                   + "[DwarfMapper]\n"
                   + "public partial class M\n"
                   + "{\n"
                   + "    [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]\n"
                   + "    [MapDerivedType<Folder, FolderDto>]\n"
                   + "    [MapDerivedType<FileNode, FileNodeDto>]\n"
                   + "    public partial TreeDto Map(Tree t);\n"
                   + "}\n";
        }

        [Fact]
        public void Hetero_derived_node_with_a_single_reference_edge_is_traversed()
        {
            GeneratorAssert.EmitsCompilableCode(
                HeteroWithFileNodeEdge("public FsNode? Link { get; set; }"));
        }

        [Fact]
        public void Hetero_derived_node_with_a_dictionary_edge_is_traversed()
        {
            GeneratorAssert.EmitsCompilableCode(
                HeteroWithFileNodeEdge("public Dictionary<string, FsNode> Refs { get; set; } = new();"));
        }

        [Fact]
        public void Hetero_derived_node_with_an_array_edge_is_traversed()
        {
            GeneratorAssert.EmitsCompilableCode(
                HeteroWithFileNodeEdge("public FsNode[] Siblings { get; set; } = System.Array.Empty<FsNode>();"));
        }
    }
}
