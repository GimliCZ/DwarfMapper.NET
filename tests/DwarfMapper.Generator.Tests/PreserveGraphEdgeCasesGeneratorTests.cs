// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     TDD: Backlog B — Preserve graph edge cases (B1–B4).
///     All tests written BEFORE implementation (Red phase).
/// </summary>
public class PreserveGraphEdgeCasesGeneratorTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // B1 — Depth guard fires BEFORE identity-map check (spurious throw)
    // Fix: reorder so TryGetReference comes BEFORE the depth guard.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Verify that TryGetReference appears BEFORE the depth-guard throw in the generated code
    ///     for a synthesized recursion-capable method under Preserve.
    /// </summary>
    [Fact]
    public void B1_TryGetReference_precedes_depth_guard_in_synthesized_method()
    {
        // A cycle-capable pair: Node→NodeDto. Under Preserve the synthesized method must check
        // the identity map before checking depth so that a shared node already mapped doesn't throw.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public int V { get; set; } public Node? Next { get; set; } }
                           public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial NodeDto Map(Node n); }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));

        // In the synthesized method body the TryGetReference must appear before the depth-guard throw.
        var tryGetIdx = generated.IndexOf("TryGetReference", StringComparison.Ordinal);
        var depthThrow = generated.IndexOf("DwarfMappingDepthException", StringComparison.Ordinal);
        Assert.True(tryGetIdx >= 0, "TryGetReference must be present");
        Assert.True(depthThrow >= 0, "DwarfMappingDepthException must be present");
        Assert.True(tryGetIdx < depthThrow,
            $"TryGetReference (pos {tryGetIdx}) must come BEFORE DwarfMappingDepthException (pos {depthThrow})");
    }

    /// <summary>
    ///     Genuinely-deep acyclic chain (NOT in the identity map) must still throw DwarfMappingDepthException.
    /// </summary>
    [Fact]
    public void B1_Generated_code_compiles_cleanly_deep_acyclic_guard_still_fires()
    {
        // We only need to verify that the generated code compiles (runtime test covers the throw).
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public int V { get; set; } public Node? Next { get; set; } }
                           public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve, MaxDepth = 4)]
                           public partial class M { public partial NodeDto Map(Node n); }
                           """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // B2 — Before-hooks run on EVERY visit to a shared node (should fire once)
    // Fix: emit BeforeMap hooks AFTER the TryGetReference early-return.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Verify that BeforeMap hooks are emitted AFTER the TryGetReference check in the generated
    ///     code for a public Preserve method. A cached node should not re-run the hook.
    /// </summary>
    [Fact]
    public void B2_BeforeHook_emitted_after_TryGetReference_in_public_Preserve_method()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public int V { get; set; } public Node? Next { get; set; } }
                           public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M
                           {
                               public partial NodeDto Map(Node n);
                               [BeforeMap] private static void Before(Node n) { }
                           }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));

        // The TryGetReference (identity check) must appear BEFORE the Before(n) hook call.
        var tryGetIdx = generated.IndexOf("TryGetReference", StringComparison.Ordinal);
        var beforeIdx = generated.IndexOf("Before(n);", StringComparison.Ordinal);
        Assert.True(tryGetIdx >= 0, "TryGetReference must be present");
        Assert.True(beforeIdx >= 0, "Before(n) call must be present");
        Assert.True(tryGetIdx < beforeIdx,
            $"TryGetReference (pos {tryGetIdx}) must come BEFORE Before(n) (pos {beforeIdx})");
    }

    /// <summary>
    ///     AfterMap hooks must only fire on the construct path (not on the cache-return path).
    ///     Verify they appear after SetReference (i.e., post-construction), not before TryGetReference.
    /// </summary>
    [Fact]
    public void B2_AfterHook_emitted_after_SetReference_not_before_TryGetReference()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public int V { get; set; } public Node? Next { get; set; } }
                           public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M
                           {
                               public partial NodeDto Map(Node n);
                               [AfterMap] private static void After(Node n, NodeDto d) { }
                           }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));

        var tryGetIdx = generated.IndexOf("TryGetReference", StringComparison.Ordinal);
        var setRefIdx = generated.IndexOf("SetReference", StringComparison.Ordinal);
        var afterIdx = generated.IndexOf("After(", StringComparison.Ordinal);
        Assert.True(tryGetIdx >= 0, "TryGetReference must be present");
        Assert.True(setRefIdx >= 0, "SetReference must be present");
        Assert.True(afterIdx >= 0, "After( must be present");
        // After comes after SetReference (on the construct path):
        Assert.True(afterIdx > setRefIdx,
            $"After( (pos {afterIdx}) must come AFTER SetReference (pos {setRefIdx})");
        // After does NOT appear before TryGetReference:
        Assert.True(afterIdx > tryGetIdx,
            $"After( (pos {afterIdx}) must come AFTER TryGetReference (pos {tryGetIdx})");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // B3 — Unknown-count array source under Preserve: SetReference AFTER fill
    // Fix: add TryGetReference before fill + move SetReference to after ToArray.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     In the generated array helper for an unknown-count source under Preserve, TryGetReference
    ///     must appear before the fill loop, and SetReference must appear after ToArray.
    /// </summary>
    [Fact]
    public void B3_Array_unknown_count_helper_has_TryGetReference_before_fill_and_SetReference_after_ToArray()
    {
        // IEnumerable<int> source → int[] target: count is unknown (IEnumerable has no .Count).
        const string src = """
                           using System.Collections.Generic;
                           using DwarfMapper;
                           namespace Demo;
                           public class Src { public IEnumerable<int> Nums { get; set; } = new List<int>(); }
                           public class Dst { public int[] Nums { get; set; } = System.Array.Empty<int>(); }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial Dst Map(Src s); }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));

        // Should contain TryGetReference in the array helper (before the buffer loop).
        Assert.Contains("TryGetReference", generated, StringComparison.Ordinal);
        // SetReference should appear after ToArray() in the generated helper.
        var toArrayIdx = generated.IndexOf("ToArray()", StringComparison.Ordinal);
        var setRefIdx = generated.LastIndexOf("SetReference", StringComparison.Ordinal);
        Assert.True(toArrayIdx >= 0, "ToArray() must be present");
        Assert.True(setRefIdx >= 0, "SetReference must be present");
        Assert.True(setRefIdx > toArrayIdx,
            $"SetReference (pos {setRefIdx}) must come AFTER ToArray() (pos {toArrayIdx})");
    }

    /// <summary>
    ///     Two parents sharing an IEnumerable&lt;int&gt; source → one target int[] (Assert.Same).
    ///     This verifies that the registration-before-fill fix works at generator level (compiles).
    /// </summary>
    [Fact]
    public void B3_Shared_unknown_count_array_compiles_cleanly()
    {
        const string src = """
                           using System.Collections.Generic;
                           using DwarfMapper;
                           namespace Demo;
                           public class Container    { public string Tag { get; set; } = ""; public Container? Sibling { get; set; } public IEnumerable<int> Nums { get; set; } = new List<int>(); }
                           public class ContainerDto { public string Tag { get; set; } = ""; public ContainerDto? Sibling { get; set; } public int[] Nums { get; set; } = System.Array.Empty<int>(); }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial ContainerDto Map(Container c); }
                           """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // B4 — Use= on a reference-type member under Preserve → DWARF032
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     [MapProperty(Use=)] on a REFERENCE-TYPE member under ReferenceHandling=Preserve
    ///     must emit DWARF032 (the user method can't receive ctx, so dedup is impossible).
    /// </summary>
    [Fact]
    public void B4_Use_on_reference_type_member_under_Preserve_emits_DWARF032()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Inner    { public int V { get; set; } }
                           public class InnerDto { public int V { get; set; } }
                           public class Outer    { public Inner Child { get; set; } = new(); }
                           public class OuterDto { public InnerDto Child { get; set; } = new(); }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M
                           {
                               [MapProperty("Child", "Child", Use = "MyConvert")]
                               public partial OuterDto Map(Outer o);
                               private static InnerDto MyConvert(Inner i) => new InnerDto { V = i.V };
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diags, d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Id == "DWARF032");
    }

    /// <summary>
    ///     [MapProperty(Use=)] on a SCALAR (value-type) member under Preserve must NOT emit DWARF032
    ///     (scalars are never tracked; Use= on a scalar is fine).
    /// </summary>
    [Fact]
    public void B4_Use_on_scalar_member_under_Preserve_does_not_emit_DWARF032()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Src { public long BigVal { get; set; } }
                           public class Dst { public int SmallVal { get; set; } }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M
                           {
                               [MapProperty("BigVal", "SmallVal", Use = "Shrink")]
                               public partial Dst Map(Src s);
                               private static int Shrink(long v) => (int)v;
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Id == "DWARF032");
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    /// <summary>
    ///     [MapProperty(Use=)] on a REFERENCE-TYPE member under ReferenceHandling=None (default)
    ///     must NOT emit DWARF032 (identity tracking is not active).
    /// </summary>
    [Fact]
    public void B4_Use_on_reference_type_member_under_None_does_not_emit_DWARF032()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Inner    { public int V { get; set; } }
                           public class InnerDto { public int V { get; set; } }
                           public class Outer    { public Inner Child { get; set; } = new(); }
                           public class OuterDto { public InnerDto Child { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               [MapProperty("Child", "Child", Use = "MyConvert")]
                               public partial OuterDto Map(Outer o);
                               private static InnerDto MyConvert(Inner i) => new InnerDto { V = i.V };
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Id == "DWARF032");
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    /// <summary>
    ///     DWARF032 must also fire when Use= is on a constructor parameter of reference type under Preserve.
    /// </summary>
    [Fact]
    public void B4_Use_on_reference_type_ctor_param_under_Preserve_emits_DWARF032()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Inner    { public int V { get; set; } }
                           public class InnerDto { public int V { get; set; } }
                           public class Outer    { public Inner Child { get; set; } = new(); }
                           public record OuterDto(InnerDto Child);
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M
                           {
                               [MapProperty("Child", "Child", Use = "MyConvert")]
                               public partial OuterDto Map(Outer o);
                               private static InnerDto MyConvert(Inner i) => new InnerDto { V = i.V };
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diags, d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Id == "DWARF032");
    }
}
