// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     TDD tests for Plan 20: [FlattenGraph] — graph-collapse with intentional topology degradation.
///     Written before / alongside implementation per project convention.
/// </summary>
public class FlattenGraphGeneratorTests
{
    // ── ISSUE-001: data-bearing complex leaves must not vanish ──────────────
    // A node's complex leaf (List<string> Tags — a DATA member, not a topology edge) used to be dropped by an
    // unconditional `continue`, leaving the DTO member at its default with no diagnostic. Edge members are
    // nulled on purpose; data leaves going missing silently is the exact failure mode this library forbids.

    private const string ComplexLeafGraph = """
                                            using DwarfMapper;
                                            using System.Collections.Generic;
                                            namespace Demo;
                                            public class Node    { public int Id { get; set; } public List<string> Tags { get; set; } = new(); public Node? Next { get; set; } }
                                            public class NodeDto { public int Id { get; set; } public List<string> Tags { get; set; } = new(); }
                                            public class Root    { public Node? Entry { get; set; } }
                                            public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                                            """;

    [Fact]
    public void FlattenGraph_complex_data_leaf_is_flattened_not_silently_dropped()
    {
        const string src = ComplexLeafGraph + """
                                              [DwarfMapper]
                                              public partial class M
                                              {
                                                  [FlattenGraph("Entry", "Nodes")]
                                                  public partial RootDto Map(Root r);
                                              }
                                              """;
        var (diags, generated) = GeneratorTestHarness.Run(src);

        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // The leaf is actually assigned in the flat-node helper (previously absent entirely).
        Assert.Contains("Tags = ", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void FlattenGraph_complex_leaf_under_Preserve_is_loud_DWARF075()
    {
        // Under Preserve the helper may be force-marked 3-param, so the leaf genuinely cannot be emitted —
        // but the user is told, instead of the member silently staying at its default.
        const string src = ComplexLeafGraph + """
                                              [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                                              public partial class M
                                              {
                                                  [FlattenGraph("Entry", "Nodes")]
                                                  public partial RootDto Map(Root r);
                                              }
                                              """;
        var (diags, _) = GeneratorTestHarness.Run(src);

        var d075 = Assert.Single(diags, d => d.Id == "DWARF075");
        Assert.Contains("Tags", d075.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ── 1. Basic: single-reference edge graph — compiles without error ──────

    [Fact]
    public void FlattenGraph_basic_compiles_without_error()
    {
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
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ── 2. BFS traversal helper and flat-node helper are emitted ────────────

    [Fact]
    public void FlattenGraph_emits_FlattenGraph_and_FlatNode_helpers()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class Node    { public string Name { get; set; } = ""; public Node? X { get; set; } }
                           public class NodeDto { public string Name { get; set; } = ""; public NodeDto? X { get; set; } }
                           public class Root    { public Node? Entry { get; set; } }
                           public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               [FlattenGraph("Entry", "Nodes")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("__DwarfMap_FlattenGraph_", generated, StringComparison.Ordinal);
        Assert.Contains("__DwarfMap_FlatNode_", generated, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ── 3. DWARF034: unknown source navigation ───────────────────────────────

    [Fact]
    public void FlattenGraph_DWARF034_unknown_source_navigation()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class Node    { public string Name { get; set; } = ""; }
                           public class NodeDto { public string Name { get; set; } = ""; }
                           public class Root    { public Node? Entry { get; set; } }
                           public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               [FlattenGraph("NonExistent", "Nodes")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diags, d => d.Id == "DWARF034");
    }

    // ── 4. DWARF034: target member does not exist ────────────────────────────

    [Fact]
    public void FlattenGraph_DWARF034_unknown_target_collection()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class Node    { public string Name { get; set; } = ""; }
                           public class NodeDto { public string Name { get; set; } = ""; }
                           public class Root    { public Node? Entry { get; set; } }
                           public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               [FlattenGraph("Entry", "NonExistentColl")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diags, d => d.Id == "DWARF034");
    }

    // ── 5. DWARF034: target member is not a collection ───────────────────────

    [Fact]
    public void FlattenGraph_DWARF034_target_not_a_collection()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public string Name { get; set; } = ""; }
                           public class NodeDto { public string Name { get; set; } = ""; }
                           public class Root    { public Node? Entry { get; set; } }
                           public class RootDto { public NodeDto Nodes { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               [FlattenGraph("Entry", "Nodes")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diags, d => d.Id == "DWARF034");
    }

    // ── 6. Array target emits .ToArray() wrapper ─────────────────────────────

    [Fact]
    public void FlattenGraph_array_target_compiles_and_emits_ToArray()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public string Name { get; set; } = ""; public Node? Next { get; set; } }
                           public class NodeDto { public string Name { get; set; } = ""; public NodeDto? Next { get; set; } }
                           public class Root    { public Node? Entry { get; set; } }
                           public class RootDto { public NodeDto[] Nodes { get; set; } = []; }
                           [DwarfMapper]
                           public partial class M
                           {
                               [FlattenGraph("Entry", "Nodes")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("ToArray()", generated, StringComparison.Ordinal);
    }

    // ── 7. IReadOnlyList<T> target compiles ──────────────────────────────────

    [Fact]
    public void FlattenGraph_IReadOnlyList_target_compiles()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class Node    { public string Name { get; set; } = ""; public Node? Next { get; set; } }
                           public class NodeDto { public string Name { get; set; } = ""; public NodeDto? Next { get; set; } }
                           public class Root    { public Node? Entry { get; set; } }
                           public class RootDto { public IReadOnlyList<NodeDto> Nodes { get; set; } = new List<NodeDto>(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               [FlattenGraph("Entry", "Nodes")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ── 8. Root's other members map normally alongside FlattenGraph ──────────

    [Fact]
    public void FlattenGraph_root_other_members_map_normally()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class Node    { public string Name { get; set; } = ""; public Node? Next { get; set; } }
                           public class NodeDto { public string Name { get; set; } = ""; public NodeDto? Next { get; set; } }
                           public class Root    { public Node? Entry { get; set; } public string Tag { get; set; } = ""; public int Count { get; set; } }
                           public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); public string Tag { get; set; } = ""; public int Count { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               [FlattenGraph("Entry", "Nodes")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Tag = r.Tag", generated, StringComparison.Ordinal);
        Assert.Contains("Count = r.Count", generated, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ── 9. Edge members are nulled in FlatNode helper ────────────────────────

    [Fact]
    public void FlattenGraph_edge_members_set_to_null_in_FlatNode()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class Node    { public string Name { get; set; } = ""; public Node? X { get; set; } public Node? Y { get; set; } }
                           public class NodeDto { public string Name { get; set; } = ""; public NodeDto? X { get; set; } public NodeDto? Y { get; set; } }
                           public class Root    { public Node? Entry { get; set; } }
                           public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               [FlattenGraph("Entry", "Nodes")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        // The FlatNode helper must null the X and Y edge members
        Assert.Contains("X = null", generated, StringComparison.Ordinal);
        Assert.Contains("Y = null", generated, StringComparison.Ordinal);
    }

    // ── 10. Collection edge (List<TNode>) enqueued in BFS ────────────────────

    [Fact]
    public void FlattenGraph_collection_edge_emitted_in_BFS()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class Node    { public string Name { get; set; } = ""; public List<Node> Children { get; set; } = new(); }
                           public class NodeDto { public string Name { get; set; } = ""; public List<NodeDto>? Children { get; set; } }
                           public class Root    { public Node? Entry { get; set; } }
                           public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               [FlattenGraph("Entry", "Nodes")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // BFS code must enumerate the Children collection edge
        Assert.Contains("foreach (var __", generated, StringComparison.Ordinal);
        Assert.Contains(".Children", generated, StringComparison.Ordinal);
    }

    // ── 11. BFS uses ReferenceEqualityComparer for cycle-safety ─────────────

    [Fact]
    public void FlattenGraph_BFS_uses_ReferenceEqualityComparer()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class Node    { public string Name { get; set; } = ""; public Node? X { get; set; } }
                           public class NodeDto { public string Name { get; set; } = ""; public NodeDto? X { get; set; } }
                           public class Root    { public Node? Entry { get; set; } }
                           public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               [FlattenGraph("Entry", "Nodes")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("ReferenceEqualityComparer", generated, StringComparison.Ordinal);
    }

    // ── 12. Multiple [FlattenGraph] on same method ───────────────────────────

    [Fact]
    public void FlattenGraph_multiple_directives_on_same_method_compiles()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class NodeA    { public string Name { get; set; } = ""; public NodeA? Next { get; set; } }
                           public class NodeADto { public string Name { get; set; } = ""; public NodeADto? Next { get; set; } }
                           public class NodeB    { public int Value { get; set; } public NodeB? Prev { get; set; } }
                           public class NodeBDto { public int Value { get; set; } public NodeBDto? Prev { get; set; } }
                           public class Root    { public NodeA? EntryA { get; set; } public NodeB? EntryB { get; set; } }
                           public class RootDto { public List<NodeADto> NodesA { get; set; } = new(); public List<NodeBDto> NodesB { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               [FlattenGraph("EntryA", "NodesA")]
                               [FlattenGraph("EntryB", "NodesB")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }
}
