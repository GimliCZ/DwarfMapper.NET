// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

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
        var src = FsNodeSource + """
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
        var src = FsNodeSource + """
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
        var src = FsNodeSource + """
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
        var src = FsNodeSource + """
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
        var src = FsNodeSource + """
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
        var src = FsNodeSource + """
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
        var src = FsNodeSource + """
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
        var src = FsNodeSource + """
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
        var src = FsNodeSource + """
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
        var src = FsNodeSource + """
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
        var src = FsNodeSource + """
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
}
