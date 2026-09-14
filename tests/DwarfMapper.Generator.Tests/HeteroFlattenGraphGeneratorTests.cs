// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     TDD tests for Plan 22: heterogeneous [FlattenGraph] — graph-collapse over a polymorphic
    ///     node hierarchy using [MapDerivedType] dispatch per concrete node type.
    /// </summary>
    public class HeteroFlattenGraphGeneratorTests
    {
        // Common models used across multiple tests
        private const string FsNodeSource = """
                                            using DwarfMapper;
                                            using System.Collections.Generic;
                                            namespace Demo;
                                            public abstract class FsNode { public string Name { get; set; } = ""; }
                                            public class Folder : FsNode { public List<FsNode> Children { get; set; } = new(); }
                                            public class File   : FsNode { public long Size { get; set; } }
                                            public abstract class FsNodeDto { public string Name { get; set; } = ""; }
                                            public class FolderDto : FsNodeDto { public List<FsNodeDto>? Children { get; set; } }
                                            public class FileDto   : FsNodeDto { public long Size { get; set; } }
                                            public class Tree    { public FsNode? Root { get; set; } public string Label { get; set; } = ""; }
                                            public class TreeDto { public List<FsNodeDto> Nodes { get; set; } = new(); public string Label { get; set; } = ""; }
                                            """;

        // ── 1. Heterogeneous [FlattenGraph] compiles without error ──────────────

        [Fact]
        public void HeteroFlattenGraph_basic_compiles_without_error()
        {
            var src = FsNodeSource +
                      """
                      [DwarfMapper]
                      public partial class M
                      {
                          [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
                          [MapDerivedType<Folder, FolderDto>]
                          [MapDerivedType<File, FileDto>]
                          public partial TreeDto Map(Tree t);
                      }
                      """;
            GeneratorAssert.CompilesClean(src);
        }

        // ── 2. Hetero: emits FlattenGraph traversal helper, dispatch helper, per-type helpers ──

        [Fact]
        public void HeteroFlattenGraph_emits_FlattenGraph_dispatch_and_FlatNode_helpers()
        {
            var src = FsNodeSource +
                      """
                      [DwarfMapper]
                      public partial class M
                      {
                          [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
                          [MapDerivedType<Folder, FolderDto>]
                          [MapDerivedType<File, FileDto>]
                          public partial TreeDto Map(Tree t);
                      }
                      """;
            var (diags, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
            // Traversal helper
            Assert.Contains("__DwarfMap_FlattenGraph_", generated, StringComparison.Ordinal);
            // Dispatch helper
            Assert.Contains("__DwarfMap_FlatNodeDispatch_", generated, StringComparison.Ordinal);
            // Per-type flat-node helpers
            Assert.Contains("__DwarfMap_FlatNode_", generated, StringComparison.Ordinal);
        }

        // ── 3. Hetero: emits a runtime-type switch in the traversal helper ────────

        [Fact]
        public void HeteroFlattenGraph_traversal_helper_has_runtime_type_switch()
        {
            var src = FsNodeSource +
                      """
                      [DwarfMapper]
                      public partial class M
                      {
                          [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
                          [MapDerivedType<Folder, FolderDto>]
                          [MapDerivedType<File, FileDto>]
                          public partial TreeDto Map(Tree t);
                      }
                      """;
            var (diags, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
            // The traversal helper must switch on __n by type for edge enumeration
            Assert.Contains("switch (__n)", generated, StringComparison.Ordinal);
        }

        // ── 4. Hetero: dispatch helper throws for unregistered runtime type ───────

        [Fact]
        public void HeteroFlattenGraph_dispatch_helper_throws_for_unregistered_type()
        {
            var src = FsNodeSource +
                      """
                      [DwarfMapper]
                      public partial class M
                      {
                          [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
                          [MapDerivedType<Folder, FolderDto>]
                          [MapDerivedType<File, FileDto>]
                          public partial TreeDto Map(Tree t);
                      }
                      """;
            var (diags, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
            // Dispatch must have a wildcard arm that throws ArgumentException
            Assert.Contains("ArgumentException", generated, StringComparison.Ordinal);
        }

        // ── 5. Hetero: edge members nulled in per-type flat-node helpers ──────────

        [Fact]
        public void HeteroFlattenGraph_edge_members_nulled_in_FlatNode_helpers()
        {
            var src = FsNodeSource +
                      """
                      [DwarfMapper]
                      public partial class M
                      {
                          [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
                          [MapDerivedType<Folder, FolderDto>]
                          [MapDerivedType<File, FileDto>]
                          public partial TreeDto Map(Tree t);
                      }
                      """;
            var (diags, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
            // FolderDto.Children must be set to null (edge degradation)
            Assert.Contains("Children = null", generated, StringComparison.Ordinal);
        }

        // ── 6. Hetero: ReferenceEqualityComparer used in traversal ───────────────

        [Fact]
        public void HeteroFlattenGraph_BFS_uses_ReferenceEqualityComparer()
        {
            var src = FsNodeSource +
                      """
                      [DwarfMapper]
                      public partial class M
                      {
                          [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
                          [MapDerivedType<Folder, FolderDto>]
                          [MapDerivedType<File, FileDto>]
                          public partial TreeDto Map(Tree t);
                      }
                      """;
            var (diags, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("ReferenceEqualityComparer", generated, StringComparison.Ordinal);
        }

        // ── 7. DWARF034: abstract node base with no [MapDerivedType] ─────────────

        [Fact]
        public void HeteroFlattenGraph_DWARF034_abstract_node_no_MapDerivedType()
        {
            var src = FsNodeSource +
                      """
                      [DwarfMapper]
                      public partial class M
                      {
                          [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
                          public partial TreeDto Map(Tree t);
                      }
                      """;
            var (diags, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diags, d => d.Id == "DWARF034");
        }

        // ── 8. DWARF035: derived DTO not assignable to base DTO ──────────────────

        [Fact]
        public void HeteroFlattenGraph_DWARF035_derived_dto_not_assignable_to_base_dto()
        {
            // FileDto2 does NOT inherit from FsNodeDto → should emit DWARF035
            var src = FsNodeSource +
                      """
                      public class FileDto2 { public long Size { get; set; } } // NOT FsNodeDto
                      [DwarfMapper]
                      public partial class M
                      {
                          [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
                          [MapDerivedType<Folder, FolderDto>]
                          [MapDerivedType<File, FileDto2>]
                          public partial TreeDto Map(Tree t);
                      }
                      """;
            var (diags, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diags, d => d.Id == "DWARF035" || d.Id == "DWARF034");
        }

        // ── 9. DWARF035: derived source not assignable to node base ──────────────

        [Fact]
        public void HeteroFlattenGraph_DWARF035_derived_source_not_assignable_to_node_base()
        {
            // Unrelated class used as derived source
            var src = FsNodeSource +
                      """
                      public class Unrelated { public string Name { get; set; } = ""; }
                      public class UnrelatedDto : FsNodeDto { }
                      [DwarfMapper]
                      public partial class M
                      {
                          [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
                          [MapDerivedType<Folder, FolderDto>]
                          [MapDerivedType<Unrelated, UnrelatedDto>]
                          public partial TreeDto Map(Tree t);
                      }
                      """;
            var (diags, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diags, d => d.Id == "DWARF035" || d.Id == "DWARF034");
        }

        // ── 10. Hetero: leaf members preserved in per-type helpers ───────────────

        [Fact]
        public void HeteroFlattenGraph_leaf_members_preserved_in_FlatNode_helpers()
        {
            var src = FsNodeSource +
                      """
                      [DwarfMapper]
                      public partial class M
                      {
                          [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
                          [MapDerivedType<Folder, FolderDto>]
                          [MapDerivedType<File, FileDto>]
                          public partial TreeDto Map(Tree t);
                      }
                      """;
            var (diags, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
            // Name (from base) and Size (leaf of File) must appear
            Assert.Contains("Name = ", generated, StringComparison.Ordinal);
            Assert.Contains("Size = ", generated, StringComparison.Ordinal);
        }

        // ── 11. Hetero: root other members map normally alongside hetero FlattenGraph ──

        [Fact]
        public void HeteroFlattenGraph_root_other_members_mapped_normally()
        {
            var src = FsNodeSource +
                      """
                      [DwarfMapper]
                      public partial class M
                      {
                          [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
                          [MapDerivedType<Folder, FolderDto>]
                          [MapDerivedType<File, FileDto>]
                          public partial TreeDto Map(Tree t);
                      }
                      """;
            var (diags, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
            // Label must be in the root method's initializer (not a flat-node helper)
            Assert.Contains("Label = ", generated, StringComparison.Ordinal);
        }

        // ── 12. Hetero: interface node base is also heterogeneous mode ────────────

        [Fact]
        public void HeteroFlattenGraph_interface_node_base_triggers_hetero_mode()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Generic;
                               namespace Demo;
                               public interface IWidget { string Id { get; set; } }
                               public class Button : IWidget { public string Id { get; set; } = ""; public string Text { get; set; } = ""; }
                               public class Panel  : IWidget { public string Id { get; set; } = ""; public List<IWidget> Children { get; set; } = new(); }
                               public class WidgetDto { public string Id { get; set; } = ""; }
                               public class ButtonDto : WidgetDto { public string Text { get; set; } = ""; }
                               public class PanelDto  : WidgetDto { public List<WidgetDto>? Children { get; set; } }
                               public class Screen    { public IWidget? Root { get; set; } }
                               public class ScreenDto { public List<WidgetDto> Widgets { get; set; } = new(); }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [FlattenGraph(nameof(Screen.Root), nameof(ScreenDto.Widgets))]
                                   [MapDerivedType<Button, ButtonDto>]
                                   [MapDerivedType<Panel, PanelDto>]
                                   public partial ScreenDto Map(Screen s);
                               }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("__DwarfMap_FlatNodeDispatch_", generated, StringComparison.Ordinal);
        }

        // ── 13. Hetero: cross-type edge (base-class edge on derived types) ─────────

        [Fact]
        public void HeteroFlattenGraph_base_edge_member_traversed_for_all_types()
        {
            // FsNode has a Parent? (base edge); must be in edge-enum switch for each arm
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Generic;
                               namespace Demo;
                               public abstract class FsNode { public string Name { get; set; } = ""; public FsNode? Parent { get; set; } }
                               public class Folder : FsNode { public List<FsNode> Children { get; set; } = new(); }
                               public class File   : FsNode { public long Size { get; set; } }
                               public abstract class FsNodeDto { public string Name { get; set; } = ""; }
                               public class FolderDto : FsNodeDto { public List<FsNodeDto>? Children { get; set; } public FsNodeDto? Parent { get; set; } }
                               public class FileDto   : FsNodeDto { public long Size { get; set; } public FsNodeDto? Parent { get; set; } }
                               public class Tree    { public FsNode? Root { get; set; } }
                               public class TreeDto { public List<FsNodeDto> Nodes { get; set; } = new(); }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
                                   [MapDerivedType<Folder, FolderDto>]
                                   [MapDerivedType<File, FileDto>]
                                   public partial TreeDto Map(Tree t);
                               }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            // Parent edge should appear in the switch arms (edge nulled + traversed)
            Assert.Contains("Parent", generated, StringComparison.Ordinal);
        }

        // ── 14. Hetero: array target produces .ToArray() wrapper ─────────────────

        [Fact]
        public void HeteroFlattenGraph_array_target_emits_ToArray()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Generic;
                               namespace Demo;
                               public abstract class FsNode { public string Name { get; set; } = ""; }
                               public class File : FsNode { public long Size { get; set; } }
                               public abstract class FsNodeDto { public string Name { get; set; } = ""; }
                               public class FileDto : FsNodeDto { public long Size { get; set; } }
                               public class Tree    { public FsNode? Root { get; set; } }
                               public class TreeDto { public FsNodeDto[] Nodes { get; set; } = []; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
                                   [MapDerivedType<File, FileDto>]
                                   public partial TreeDto Map(Tree t);
                               }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("ToArray()", generated, StringComparison.Ordinal);
        }

        // ── 15. Regression: homogeneous [FlattenGraph] unaffected ────────────────

        [Fact]
        public void HomogeneousFlattenGraph_unchanged_after_hetero_implementation()
        {
            // Concrete (non-abstract) node type with no [MapDerivedType] → homogeneous path
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Generic;
                               namespace Demo;
                               public class Node    { public string Name { get; set; } = ""; public Node? Next { get; set; } }
                               public class NodeDto { public string Name { get; set; } = ""; public NodeDto? Next { get; set; } }
                               public class Root    { public Node? Entry { get; set; } public string Tag { get; set; } = ""; }
                               public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); public string Tag { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [FlattenGraph("Entry", "Nodes")]
                                   public partial RootDto Map(Root r);
                               }
                               """;
            var (diags, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
            // Homogeneous path: flat-node helper without dispatch
            Assert.Contains("__DwarfMap_FlatNode_", generated, StringComparison.Ordinal);
            // Must NOT emit dispatch helper for homogeneous path
            Assert.DoesNotContain("__DwarfMap_FlatNodeDispatch_", generated, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(src);
        }

        /// <summary>
        ///     The FsNode fixture with extra members on the derived node and DTO types, and any supporting types.
        /// </summary>
        private static string FsNodeSourceWith(string folderExtra, string fileExtra, string folderDtoExtra, string fileDtoExtra, string types = "", string mapperOptions = "")
        {
            return $$"""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     {{types}}
                     public abstract class FsNode { public string Name { get; set; } = ""; }
                     public class Folder : FsNode { public List<FsNode> Children { get; set; } = new(); {{folderExtra}} }
                     public class File   : FsNode { public long Size { get; set; } {{fileExtra}} }
                     public abstract class FsNodeDto { public string Name { get; set; } = ""; }
                     public class FolderDto : FsNodeDto { public List<FsNodeDto>? Children { get; set; } {{folderDtoExtra}} }
                     public class FileDto   : FsNodeDto { public long Size { get; set; } {{fileDtoExtra}} }
                     public class Tree    { public FsNode? Root { get; set; } public string Label { get; set; } = ""; }
                     public class TreeDto { public List<FsNodeDto> Nodes { get; set; } = new(); public string Label { get; set; } = ""; }
                     [DwarfMapper{{mapperOptions}}]
                     public partial class M
                     {
                         [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
                         [MapDerivedType<Folder, FolderDto>]
                         [MapDerivedType<File, FileDto>]
                         public partial TreeDto Map(Tree t);
                     }
                     """;
        }

        // ── 16. A Nullable<struct> member under an interface node base is an edge ───────

        /// <summary>
        ///     A struct node implementing the interface base, held as <c>Leaf?</c> on a derived node, is an EDGE: C#
        ///     boxes <c>Leaf?</c> implicitly to the interface, so the single-ref edge check classifies it, and the
        ///     derived DTO's same-named member is nulled like every other edge.
        /// </summary>
        [Fact]
        public void HeteroFlattenGraph_nullable_struct_member_under_an_interface_base_is_an_edge()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Generic;
                               namespace Demo;
                               public interface INode { string Name { get; } }
                               public struct Leaf : INode { public string Name { get; set; } }
                               public class Folder : INode { public string Name { get; set; } = ""; public List<INode> Children { get; set; } = new(); public Leaf? Opt { get; set; } }
                               public class File   : INode { public string Name { get; set; } = ""; public long Size { get; set; } }
                               public interface INodeDto { string Name { get; } }
                               public class FolderDto : INodeDto { public string Name { get; set; } = ""; public List<INodeDto>? Children { get; set; } public INodeDto? Opt { get; set; } }
                               public class FileDto   : INodeDto { public string Name { get; set; } = ""; public long Size { get; set; } }
                               public class Tree    { public INode? Root { get; set; } }
                               public class TreeDto { public List<INodeDto> Nodes { get; set; } = new(); }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
                                   [MapDerivedType<Folder, FolderDto>]
                                   [MapDerivedType<File, FileDto>]
                                   public partial TreeDto Map(Tree t);
                               }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("Opt = null,", generated, StringComparison.Ordinal);
        }

        // ── 17. A derived-node leaf with no DTO counterpart is not assigned ────────────

        [Fact]
        public void HeteroFlattenGraph_derived_leaf_without_a_dto_counterpart_is_not_assigned()
        {
            var src = FsNodeSourceWith("", "public int Extra { get; set; }", "", "");
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.DoesNotContain("Extra =", generated, StringComparison.Ordinal);
        }

        // ── 18. A derived-node leaf with no conversion is reported, not dropped ─────────

        [Fact]
        public void HeteroFlattenGraph_derived_leaf_without_a_conversion_reports_DWARF005()
        {
            var src = FsNodeSourceWith("", "public System.IO.Stream? Blob { get; set; }", "", "public int Blob { get; set; }");
            var reported = GeneratorAssert.Reports(src, "DWARF005");
            Assert.Contains(reported,
                d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("'Blob'", StringComparison.Ordinal));
        }

        // ── 19. A complex data leaf on a derived node: flattened, or DWARF075 under Preserve ─────────────

        private const string AddressTypes =
            "public class Address { public string City { get; set; } = \"\"; } public class AddressDto { public string City { get; set; } = \"\"; }";

        /// <summary>
        ///     3ade4ee (ISSUE-001) ruled on the homogeneous flat-node helper: a data leaf whose conversion is a COMPLEX
        ///     synthesized helper (object, collection, dictionary) is FLATTENED outside Preserve, and reported as
        ///     DWARF075 under Preserve, where the helper may become recursion-capable and the one-argument call would
        ///     not compile. The derived-node helper built for each [MapDerivedType] arm has the same leaf loop and was
        ///     not changed with it: it still skipped the member in every mode, with no diagnostic — the MF-D shape this
        ///     test used to pin as left unassigned.
        /// </summary>
        [Theory]
        [InlineData("public Address Addr { get; set; } = new();", "public AddressDto Addr { get; set; } = new();", "Addr = ")]
        [InlineData("public List<Address> Addrs { get; set; } = new();", "public List<AddressDto> Addrs { get; set; } = new();", "Addrs = ")]
        public void HeteroFlattenGraph_complex_leaf_on_a_derived_node_is_flattened_outside_Preserve(string fileMember, string fileDtoMember, string assignment)
        {
            var src = FsNodeSourceWith("", fileMember, "", fileDtoMember, AddressTypes);
            var (diags, _) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diags, d => d.Id == "DWARF075");

            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains(assignment, generated, StringComparison.Ordinal);
        }

        [Fact]
        public void HeteroFlattenGraph_complex_leaf_on_a_derived_node_reports_DWARF075_under_Preserve()
        {
            var src = FsNodeSourceWith("",
                "public Address Addr { get; set; } = new();",
                "",
                "public AddressDto Addr { get; set; } = new();",
                AddressTypes,
                "(ReferenceHandling = ReferenceHandlingStrategy.Preserve)");

            var (diags, generated) = GeneratorTestHarness.Run(src);
            var dwarf075 = Assert.Single(diags, d => d.Id == "DWARF075");
            Assert.Equal(
                "[FlattenGraph] cannot flatten member 'Addr' of type 'Demo.Address' under ReferenceHandling = Preserve; " +
                "it is left at the destination's default. Map the member explicitly, or use ReferenceHandling = None " +
                "for this mapper.",
                dwarf075.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
            Assert.DoesNotContain("Addr = ", generated, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(src);
        }

        // ── 20. The same simple leaf helper on two arms is synthesized once ───────────

        [Fact]
        public void HeteroFlattenGraph_same_enum_leaf_on_two_arms_shares_one_helper()
        {
            var src = FsNodeSourceWith("public Kind K { get; set; }",
                "public Kind K { get; set; }",
                "public KindDto K { get; set; }",
                "public KindDto K { get; set; }",
                "public enum Kind { A, B } public enum KindDto { A, B }");
            var generated = GeneratorAssert.CompilesClean(src);
            var lines = generated.Split('\n');
            Assert.Equal(2, lines.Count(l => l.Contains("K = __DwarfMap_EnumName_", StringComparison.Ordinal)));
            Assert.Single(lines, l => l.Contains("__DwarfMap_EnumName_", StringComparison.Ordinal) &&
                                      l.Contains("(global::Demo.Kind ", StringComparison.Ordinal));
        }

        /// <summary>
        ///     The FsNode fixture with the node base's modifier, the source navigation member, the target collection
        ///     member and the directive's arms all chosen by the caller.
        /// </summary>
        private static string FlattenGraphSource(string nodeModifier, string rootMember, string nodesMember, string arms)
        {
            return $$"""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class Unrelated { public int X { get; set; } }
                     public class UnrelatedDto { public int X { get; set; } }
                     public {{nodeModifier}} class FsNode { public string Name { get; set; } = ""; }
                     public class Folder : FsNode { public List<FsNode> Children { get; set; } = new(); }
                     public class File   : FsNode { public long Size { get; set; } }
                     public {{nodeModifier}} class FsNodeDto { public string Name { get; set; } = ""; }
                     public class FolderDto : FsNodeDto { public List<FsNodeDto>? Children { get; set; } }
                     public class FileDto   : FsNodeDto { public long Size { get; set; } }
                     public class Tree    { {{rootMember}} }
                     public class TreeDto { {{nodesMember}} }
                     [DwarfMapper]
                     public partial class M
                     {
                         [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
                         {{arms}}
                         public partial TreeDto Map(Tree t);
                     }
                     """;
        }

        private const string BothArms = "[MapDerivedType<Folder, FolderDto>] [MapDerivedType<File, FileDto>]";

        // ── 21. A concrete node base with [MapDerivedType] arms takes the heterogeneous path ─────

        /// <summary>
        ///     A node base that is neither abstract nor an interface still dispatches per concrete type when the
        ///     directive declares arms. Every other heterogeneous fixture's node base was abstract.
        /// </summary>
        [Fact]
        public void HeteroFlattenGraph_concrete_node_base_with_arms_emits_per_type_helpers()
        {
            var src = FlattenGraphSource("", "public FsNode? Root { get; set; }", "public List<FsNodeDto> Nodes { get; set; } = new();", BothArms);
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("__DwarfMap_FlatNode_", generated, StringComparison.Ordinal);
        }

        // ── 22. Every arm refused skips the directive ─────────────────────────────────

        /// <summary>
        ///     When no declared arm survives validation there is nothing to dispatch to, so the directive is skipped
        ///     after the refusal is reported, rather than emitting a traversal with no arms.
        /// </summary>
        [Fact]
        public void HeteroFlattenGraph_every_arm_refused_reports_DWARF035_and_emits_no_traversal()
        {
            var src = FlattenGraphSource("abstract", "public FsNode? Root { get; set; }", "public List<FsNodeDto> Nodes { get; set; } = new();",
                "[MapDerivedType<Unrelated, UnrelatedDto>]");
            var (diagnostics, generated) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics,
                d => d.Id == "DWARF035" &&
                     d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("'global::Demo.Unrelated'", StringComparison.Ordinal));
            Assert.DoesNotContain("__DwarfMap_FlattenGraph_", generated, StringComparison.Ordinal);
        }

        // ── 23. The array-target wrapper takes the source navigation's own shape ─────────

        /// <summary>
        ///     An array target is filled through a <c>.ToArray()</c> wrapper whose parameter must match the traversal
        ///     helper's: an array of nodes, or any enumerable of them. Every array-target fixture navigated from a
        ///     single node.
        /// </summary>
        [Theory]
        [InlineData("public FsNode[]? Root { get; set; }", "global::Demo.FsNode[]? entry)")]
        [InlineData("public List<FsNode>? Root { get; set; }", "global::System.Collections.Generic.IEnumerable<global::Demo.FsNode>? entry)")]
        public void HeteroFlattenGraph_array_target_wrapper_takes_the_source_navigation_shape(string rootMember, string wrapperParameter)
        {
            var src = FlattenGraphSource("abstract", rootMember, "public FsNodeDto[] Nodes { get; set; } = [];", BothArms);
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains(generated.Split('\n'),
                l => l.Contains("__DwarfMap_FlattenGraphArr_", StringComparison.Ordinal) &&
                     l.Contains("(" + wrapperParameter, StringComparison.Ordinal));
        }
    }
}
