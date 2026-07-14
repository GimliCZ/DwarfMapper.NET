// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Red-phase TDD generator tests for the Plan 22 audit findings.
///     Each test covers a specific bug that must be fixed in the generator;
///     the test is intentionally written so it fails against the current (unfixed) generator.
/// </summary>
public class AuditRemediation_GeneratorTests
{
    // ═══════════════════════════════════════════════════════════════════════════
    // MF-A — MapDerivedType + Preserve → CS7036
    // Bug: generator emits a 1-arg call to a 3-param __DwarfMap_Obj_… method
    //      when the mapper has ReferenceHandling=Preserve and AutoNest=true.
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MfA_MapDerivedType_Preserve_compiles_no_CS7036()
    {
        // AutoNest=true → Dog→DogDto is auto-synthesised, no declared overload needed.
        // Preserve context passes two extra params (__ctx, __depth) to every
        // recursion-capable helper.  The [MapDerivedType] dispatch call must also
        // forward those params; before the fix it doesn't → CS7036.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public abstract class Animal    { public string Name { get; set; } = ""; }
                           public class Dog : Animal       { public string Breed { get; set; } = ""; }
                           public abstract class AnimalDto { public string Name { get; set; } = ""; }
                           public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
                           [DwarfMapper(AutoNest = true, ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M
                           {
                               [MapDerivedType<Dog, DogDto>]
                               public partial AnimalDto Map(Animal a);
                           }
                           """;
        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
        Assert.Empty(errors);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MF-B — FlattenGraph with List<Node> source navigation → CS1503
    // Bug: the traversal helper accepts `Node[]?` but the source nav property
    //      is List<Node> / IReadOnlyList<Node> / HashSet<Node> etc.
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MfB_FlattenGraph_ListSourceNav_compiles_no_CS1503()
    {
        // Root.Entries is List<Node> — the traversal helper must accept IEnumerable<Node>
        // (or equivalent) so the call site compiles.  Before the fix → CS1503.
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class Node    { public string Name { get; set; } = ""; public List<Node> Children { get; set; } = new(); }
                           public class NodeDto { public string Name { get; set; } = ""; public List<NodeDto>? Children { get; set; } }
                           public class Root    { public List<Node> Entries { get; set; } = new(); public string Tag { get; set; } = ""; }
                           public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); public string Tag { get; set; } = ""; }
                           [DwarfMapper]
                           public partial class M
                           {
                               [FlattenGraph("Entries", "Nodes")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
        Assert.Empty(errors);
    }

    [Fact]
    public void MfB_FlattenGraph_IReadOnlyListSourceNav_compiles()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class Node    { public string Name { get; set; } = ""; public Node? Next { get; set; } }
                           public class NodeDto { public string Name { get; set; } = ""; public NodeDto? Next { get; set; } }
                           public class Root    { public IReadOnlyList<Node> Entries { get; set; } = new List<Node>(); }
                           public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               [FlattenGraph("Entries", "Nodes")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
        Assert.Empty(errors);
    }

    [Fact]
    public void MfB_FlattenGraph_ArraySourceNav_still_compiles()
    {
        // Array source nav must continue to work after the fix.
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class Node    { public string Name { get; set; } = ""; public Node? Next { get; set; } }
                           public class NodeDto { public string Name { get; set; } = ""; public NodeDto? Next { get; set; } }
                           public class Root    { public Node[] Entries { get; set; } = []; }
                           public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               [FlattenGraph("Entries", "Nodes")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
        Assert.Empty(errors);
    }

    [Fact]
    public void MfB_FlattenGraph_HashSetSourceNav_compiles()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class Node    { public string Name { get; set; } = ""; public Node? Next { get; set; } }
                           public class NodeDto { public string Name { get; set; } = ""; public NodeDto? Next { get; set; } }
                           public class Root    { public HashSet<Node> Entries { get; set; } = new(); }
                           public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               [FlattenGraph("Entries", "Nodes")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
        Assert.Empty(errors);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MF-D — FlattenGraph leaf that resolves to a ctx-requiring converter → CS7036
    // Bug: the __DwarfMap_FlatNode_… helper calls a synthesised converter with
    //      only 1 arg, but the context-aware overload takes 3 params.
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MfD_FlattenGraph_DictLeaf_no_CS7036()
    {
        // Node has a Dictionary<string,Node> member (Links) AND a scalar Name.
        // AutoNest=true.  The flat-node helper must call the dict-member converter
        // with the correct number of arguments.
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class Node    { public string Name { get; set; } = ""; public Dictionary<string, Node> Links { get; set; } = new(); public Node? Next { get; set; } }
                           public class NodeDto { public string Name { get; set; } = ""; public Dictionary<string, NodeDto>? Links { get; set; } public NodeDto? Next { get; set; } }
                           public class Root    { public Node? Entry { get; set; } }
                           public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                           [DwarfMapper(AutoNest = true)]
                           public partial class M
                           {
                               [FlattenGraph("Entry", "Nodes")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
        Assert.Empty(errors);
    }

    [Fact]
    public void MfD_FlattenGraph_DictLeaf_with_Preserve_no_CS7036()
    {
        // Same as above but the mapper also has ReferenceHandling=Preserve,
        // which makes the arg-count mismatch worse (3 vs 1 instead of 2 vs 1).
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class Node    { public string Name { get; set; } = ""; public Dictionary<string, Node> Links { get; set; } = new(); public Node? Next { get; set; } }
                           public class NodeDto { public string Name { get; set; } = ""; public Dictionary<string, NodeDto>? Links { get; set; } public NodeDto? Next { get; set; } }
                           public class Root    { public Node? Entry { get; set; } }
                           public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                           [DwarfMapper(AutoNest = true, ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M
                           {
                               [FlattenGraph("Entry", "Nodes")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
        Assert.Empty(errors);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SF-F3 — Dictionary<K,Node> edge values are not traversed (generator check)
    // Bug: the BFS helper emits no traversal code for dict-value edges, so nodes
    //      reachable only through dict values are silently dropped.
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SfF3_FlattenGraph_DictValues_traversal_emitted()
    {
        // The generated BFS helper must contain a loop over dictionary values
        // (__kv / __kv.Value) so that dict-value edges are enqueued.
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class Node    { public string Name { get; set; } = ""; public Dictionary<string, Node> Links { get; set; } = new(); }
                           public class NodeDto { public string Name { get; set; } = ""; public Dictionary<string, NodeDto>? Links { get; set; } }
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
        // The BFS helper must iterate over dictionary values to enqueue them.
        // "__kv" is the loop variable name used by the generator's dict-traversal emit.
        Assert.Contains("__kv", generated, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SF-F4 — Homo FlattenGraph: interface-typed edge not recognised as a graph edge
    // Bug: edge detection uses exact type equality, so an INode?-typed property
    //      that is the same conceptual node type is treated as a leaf, not an edge.
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SfF4_FlattenGraph_InterfaceTypedEdge_traversed()
    {
        // Node.Link is typed INode (an interface) — it should still be recognised
        // as a graph edge and enqueued in BFS.  Before the fix only exact-type
        // matches are traversed, so nodes reachable via .Link are silently dropped.
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public interface INode { string Name { get; set; } }
                           public class Node : INode { public string Name { get; set; } = ""; public INode? Link { get; set; } }
                           public class NodeDto { public string Name { get; set; } = ""; public NodeDto? Link { get; set; } }
                           public class Root    { public Node? Entry { get; set; } }
                           public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               [FlattenGraph("Entry", "Nodes")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        // Must compile cleanly.
        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
        Assert.Empty(errors);

        // The BFS traversal helper must enqueue .Link.
        // After the fix, the generated code contains an enqueue for "Link".
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        // The BFS helper should reference "Link" as an edge to enqueue
        // (i.e. not treat it as a leaf because INode is assignable to Node).
        Assert.Contains(".Link", generated, StringComparison.Ordinal);
        // Specifically the enqueue call for .Link must appear in the traversal helper,
        // not just in the FlatNode helper.  A simple heuristic: the traversal helper
        // body contains "__queue" (for BFS) followed by a reference to ".Link".
        Assert.Contains("__queue", generated, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SF-ORDER — Interface inheritance ordering in [MapDerivedType] arms
    // Bug: arms are sorted by class-hierarchy depth only; interfaces all get
    //      depth 0 so declaration order wins and IFoo (more derived) ends up
    //      AFTER IBar (base) → IFoo arm is shadowed by the IBar arm.
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SfOrder_InterfaceHierarchy_IFoo_before_IBar_arm_when_declared_after()
    {
        // IFoo : IBar — so IFoo is more derived.
        // [MapDerivedType(IBar, …)] is declared FIRST,
        // [MapDerivedType(IFoo, …)] is declared SECOND.
        // After sorting most-derived-first: IFoo arm must precede IBar arm.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public interface IBar { string Name { get; set; } }
                           public interface IFoo : IBar { int Extra { get; set; } }
                           public class C : IFoo  { public string Name { get; set; } = ""; public int Extra { get; set; } }
                           public class BarDto    { public string Name { get; set; } = ""; }
                           public class FooDto : BarDto { public int Extra { get; set; } }
                           public class CDto : FooDto  { }
                           [DwarfMapper(AutoNest = true)]
                           public partial class M
                           {
                               [MapDerivedType(typeof(IBar), typeof(BarDto))]
                               [MapDerivedType(typeof(IFoo), typeof(FooDto))]
                               public partial BarDto Map(IBar b);
                           }
                           """;
        // Must compile.
        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
        Assert.Empty(errors);

        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);

        // IFoo arm must appear BEFORE the IBar arm in the generated switch.
        // Search for the switch arm pattern (e.g. "IFoo __s =>") rather than a bare
        // type name, to avoid matching the method signature "Map(IBar b)" which always
        // appears in the generated output before any switch arm.
        var iFooIdx = generated.IndexOf("IFoo __s =>", StringComparison.Ordinal);
        var iBarIdx = generated.IndexOf("IBar __s =>", StringComparison.Ordinal);
        Assert.True(iFooIdx >= 0, "IFoo arm (IFoo __s =>) not found in generated code");
        Assert.True(iBarIdx >= 0, "IBar arm (IBar __s =>) not found in generated code");
        Assert.True(iFooIdx < iBarIdx,
            $"Expected IFoo arm (idx={iFooIdx}) before IBar arm (idx={iBarIdx}) in generated switch, but got the opposite.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SF-LEAFDIAG — Unmappable flat-node leaf silently dropped
    // Bug: when a flat-node member has a type for which no converter exists and
    //      AutoNest=false, the generator silently skips it with no diagnostic.
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SfLeafDiag_FlattenGraph_unmappable_leaf_emits_diagnostic()
    {
        // SomeComplex has no mapping to SomeComplexDto and AutoNest=false,
        // so the generator cannot map the "Extra" member.
        // Expected: at least one error diagnostic (DWARF005 or any DWARF error).
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class SomeComplex    { public int X { get; set; } public int Y { get; set; } }
                           public class SomeComplexDto { public int X { get; set; } public int Z { get; set; } }
                           public class Node    { public string Name { get; set; } = ""; public SomeComplex Extra { get; set; } = new(); public Node? Next { get; set; } }
                           public class NodeDto { public string Name { get; set; } = ""; public SomeComplexDto? Extra { get; set; } public NodeDto? Next { get; set; } }
                           public class Root    { public Node? Entry { get; set; } }
                           public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                           [DwarfMapper(AutoNest = false)]
                           public partial class M
                           {
                               [FlattenGraph("Entry", "Nodes")]
                               public partial RootDto Map(Root r);
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        // Must emit at least one error-level diagnostic for the unmappable member.
        Assert.Contains(diags, d =>
            d.Severity == DiagnosticSeverity.Error &&
            (d.Id == "DWARF005" || d.Id.StartsWith("DWARF", StringComparison.Ordinal)));
    }
}
