// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
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
        GeneratorAssert.EmitsCompilableCode(src);
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
        GeneratorAssert.EmitsCompilableCode(src);
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
        GeneratorAssert.CompilesClean(src);
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
        GeneratorAssert.EmitsCompilableCode(src);
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
        var generated = GeneratorAssert.CompilesClean(src);
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
        GeneratorAssert.CompilesClean(src);
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
        GeneratorAssert.EmitsCompilableCode(src);
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
        var generated = GeneratorAssert.CompilesClean(src);
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
        GeneratorAssert.CompilesClean(src);

        // The control for DWARF087 below, and it has to stay a real one: two directives naming DIFFERENT
        // destination collections are what [FlattenGraph]'s AllowMultiple = true is FOR. CompilesClean already
        // fails on any error diagnostic, so an over-eager (unconditional) duplicate refusal breaks this test —
        // but say so explicitly, because a control nobody can see is a control nobody keeps.
        GeneratorAssert.DoesNotReport(src, "DWARF087");
    }

    // ── 13. DWARF087: two directives may not fill the same destination collection ─────────────────────────
    //
    // Round 20's only NON-silent defect. The generator accepted a repeated directive, appended a second
    // MemberMap for the same destination, and emitted `new RootDto { Nodes = …, Nodes = … }`:
    //
    //     CS1912: Duplicate initialization of member 'Nodes'
    //       @SourceFile(DwarfMapper.Generator\DwarfMapper.Generator.DwarfGenerator\Demo.M.g.cs[779..784))
    //
    // — reported against the GENERATED file, so the consumer could not build and the error named source they
    // never wrote. Found by the surface matrix's AllowMultiple ×2 axis, which is derived from
    // AttributeUsage rather than from anyone's list of scenarios; no hand-written suite had this case.

    private const string DuplicateTargetGraph = """
                                                using DwarfMapper;
                                                using System.Collections.Generic;
                                                namespace Demo;
                                                public class Node    { public string Name { get; set; } = ""; public Node? Next { get; set; } }
                                                public class NodeDto { public string Name { get; set; } = ""; public NodeDto? Next { get; set; } }
                                                public class Root    { public Node? Entry { get; set; } public Node? Other { get; set; } }
                                                public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                                                """;

    [Fact]
    public void FlattenGraph_duplicate_directive_is_refused_with_DWARF087()
    {
        const string src = DuplicateTargetGraph + """
                                                  [DwarfMapper]
                                                  public partial class M
                                                  {
                                                      [FlattenGraph("Entry", "Nodes")]
                                                      [FlattenGraph("Entry", "Nodes")]
                                                      public partial RootDto Map(Root r);
                                                  }
                                                  """;
        var d = Assert.Single(GeneratorAssert.Reports(src, "DWARF087"));
        Assert.Contains("Nodes", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        AssertNoDuplicateInitialization(src);
    }

    [Fact]
    public void FlattenGraph_two_directives_filling_one_collection_are_refused_even_when_not_identical()
    {
        // The defect is keyed on the DESTINATION, not on the directives being character-identical: these two
        // are different directives and emitted the very same CS1912. A check that only caught exact duplicates
        // would have left this half of the defect class in place.
        const string src = DuplicateTargetGraph + """
                                                  [DwarfMapper]
                                                  public partial class M
                                                  {
                                                      [FlattenGraph("Entry", "Nodes")]
                                                      [FlattenGraph("Other", "Nodes")]
                                                      public partial RootDto Map(Root r);
                                                  }
                                                  """;
        var d = Assert.Single(GeneratorAssert.Reports(src, "DWARF087"));
        Assert.Contains("Nodes", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        AssertNoDuplicateInitialization(src);
    }

    /// <summary>
    ///     The half of the DWARF087 fix that <see cref="GeneratorAssert.Reports" /> does not cover: the
    ///     duplicate-initialization error must be GONE, not merely accompanied by a diagnostic explaining it.
    ///     <para>
    ///         Deliberately not <c>EmitsCompilableCode</c>. Every DWARF Error in this generator suppresses the
    ///         emission, so a refused source has no implementation part for its partial method and reports
    ///         CS8795 — the ordinary refusal cascade DWARF078 signposts, and the same for DWARF087 as for
    ///         DWARF008 or DWARF011. What must never come back is CS1912, which was not a cascade: it was the
    ///         generator handing the consumer invalid C# in a file they never wrote.
    ///     </para>
    /// </summary>
    private static void AssertNoDuplicateInitialization(string source)
    {
        var compileErrors = GeneratorTestHarness.RunAndGetCompilationErrors(source);
        Assert.DoesNotContain(compileErrors, e => string.Equals(e.Id, "CS1912", StringComparison.Ordinal));
    }

    /// <summary>
    ///     The one legal shape a SOURCE-keyed duplicate check would wrongly reject (B4): one navigation
    ///     root walked into two different destination collections.
    ///     <para>
    ///         <c>DWARF087</c> is keyed on the DESTINATION because that is where the duplicate initializer
    ///         came from — <c>new Dst { Nodes = …, Nodes = … }</c>, CS1912. Keying it on the source instead
    ///         would look equally plausible from the defect report and would turn this shape, which emits
    ///         two perfectly distinct initializers, into a brand-new build-breaking Error in consumer code
    ///         that compiles today. The existing control next door (<c>EntryA→NodesA</c> beside
    ///         <c>EntryB→NodesB</c>) does not catch that mistake: both its source and its destination
    ///         differ, so it passes under either keying.
    ///     </para>
    /// </summary>
    [Fact]
    public void FlattenGraph_one_source_into_two_different_collections_is_accepted()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class Node    { public string Name { get; set; } = ""; public Node? Next { get; set; } }
                           public class NodeDto { public string Name { get; set; } = ""; public NodeDto? Next { get; set; } }
                           public class Root    { public Node? Entry { get; set; } }
                           public class RootDto { public List<NodeDto> NodesA { get; set; } = new(); public List<NodeDto> NodesB { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               [FlattenGraph("Entry", "NodesA")]
                               [FlattenGraph("Entry", "NodesB")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        GeneratorAssert.DoesNotReport(src, "DWARF087");
        GeneratorAssert.CompilesClean(src);

        // Both collections are actually filled — an "accepted" that emitted one initializer and dropped the
        // other would pass the two assertions above while losing half the directive.
        var (_, generated) = GeneratorTestHarness.Run(src);
        Assert.Contains("NodesA", generated, StringComparison.Ordinal);
        Assert.Contains("NodesB", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void FlattenGraph_single_directive_is_not_refused()
    {
        const string src = DuplicateTargetGraph + """
                                                  [DwarfMapper]
                                                  public partial class M
                                                  {
                                                      [FlattenGraph("Entry", "Nodes")]
                                                      public partial RootDto Map(Root r);
                                                  }
                                                  """;
        GeneratorAssert.DoesNotReport(src, "DWARF087");
        GeneratorAssert.CompilesClean(src);
    }
}
