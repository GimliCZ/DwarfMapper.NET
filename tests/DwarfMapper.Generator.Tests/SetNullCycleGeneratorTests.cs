// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Plan 19 Part C — OnCycle = SetNull (None-mode cycle breaking).
///     Generator-level assertions over the emitted on-stack guard and DWARF037.
/// </summary>
public class SetNullCycleGeneratorTests
{
    private const string SelfRefNode = """
                                       using DwarfMapper;
                                       namespace Demo;
                                       public class Node    { public int V { get; set; } public Node? Next { get; set; } }
                                       public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                                       """;

    // ── 1. Compiles clean ───────────────────────────────────────────────────────
    [Fact]
    public void SetNull_attribute_compiles_without_error()
    {
        var src = SelfRefNode + """
                                [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                                public partial class M { public partial NodeDto Map(Node n); }
                                """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ── 2. Emits the on-stack guard (TryEnterNode + ExitNode + try/finally) ──────
    [Fact]
    public void SetNull_emits_on_stack_guard_in_recursion_capable_method()
    {
        var src = SelfRefNode + """
                                [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                                public partial class M { public partial NodeDto Map(Node n); }
                                """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        Assert.Contains("TryEnterNode", generated, StringComparison.Ordinal);
        Assert.Contains("ExitNode", generated, StringComparison.Ordinal);
        Assert.Contains("finally", generated, StringComparison.Ordinal);
        // Back-edge breaks with null.
        Assert.Contains("return null!", generated, StringComparison.Ordinal);
    }

    // ── 3. Public entry allocates the context with setNull: true ────────────────
    [Fact]
    public void SetNull_public_entry_allocates_setNull_context()
    {
        var src = SelfRefNode + """
                                [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                                public partial class M { public partial NodeDto Map(Node n); }
                                """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        Assert.Contains("setNull: true", generated, StringComparison.Ordinal);
        // Must NOT allocate the Preserve identity map.
        Assert.DoesNotContain("SetReference", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetReference", generated, StringComparison.Ordinal);
    }

    // ── 4. None + Throw (default) does NOT emit the guard ────────────────────────
    [Fact]
    public void Default_OnCycle_Throw_does_not_emit_guard()
    {
        var src = SelfRefNode + """
                                [DwarfMapper]
                                public partial class M { public partial NodeDto Map(Node n); }
                                """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain("TryEnterNode", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("setNull: true", generated, StringComparison.Ordinal);
        // The depth guard is still present (None-mode backstop).
        Assert.Contains("DwarfMappingDepthException", generated, StringComparison.Ordinal);
    }

    // ── 5. Depth guard is preserved INSIDE the SetNull guarded body ─────────────
    [Fact]
    public void SetNull_keeps_depth_backstop_for_acyclic_chains()
    {
        var src = SelfRefNode + """
                                [DwarfMapper(OnCycle = OnCycleStrategy.SetNull, MaxDepth = 8)]
                                public partial class M { public partial NodeDto Map(Node n); }
                                """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        Assert.Contains("DwarfMappingDepthException", generated, StringComparison.Ordinal);
    }

    // ── 6. Non-recursive (acyclic) pair is unaffected — no guard, zero overhead ──
    [Fact]
    public void SetNull_acyclic_pair_has_no_guard()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Flat    { public int V { get; set; } public string S { get; set; } = ""; }
                           public class FlatDto { public int V { get; set; } public string S { get; set; } = ""; }
                           [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                           public partial class M { public partial FlatDto Map(Flat f); }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("TryEnterNode", generated, StringComparison.Ordinal);
    }

    // ── 7. DWARF037: OnCycle = SetNull together with Preserve → warning ─────────
    [Fact]
    public void SetNull_under_Preserve_reports_DWARF037_warning()
    {
        var src = SelfRefNode + """
                                [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve, OnCycle = OnCycleStrategy.SetNull)]
                                public partial class M { public partial NodeDto Map(Node n); }
                                """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        var d037 = diags.SingleOrDefault(d => d.Id == "DWARF037");
        Assert.NotNull(d037);
        Assert.Equal(DiagnosticSeverity.Warning, d037!.Severity);
        // No error — it's only a warning, and Preserve still reconstructs.
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        // Behaviour falls back to Preserve (identity map), NOT the on-stack guard.
        Assert.Contains("TryGetReference", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("TryEnterNode", generated, StringComparison.Ordinal);
    }

    // ── 8. None + Throw does NOT report DWARF037 ────────────────────────────────
    [Fact]
    public void None_mode_SetNull_does_not_report_DWARF037()
    {
        var src = SelfRefNode + """
                                [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                                public partial class M { public partial NodeDto Map(Node n); }
                                """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Id == "DWARF037");
    }

    // ── 9. SetNull threads ctx through a collection-element mapper (no fresh ctx) ─
    [Fact]
    public void SetNull_collection_element_threads_ctx_into_helper()
    {
        const string src = """
                           using System.Collections.Generic;
                           using DwarfMapper;
                           namespace Demo;
                           public class Tree    { public int V { get; set; } public List<Tree>? Children { get; set; } }
                           public class TreeDto { public int V { get; set; } public List<TreeDto>? Children { get; set; } }
                           [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                           public partial class M { public partial TreeDto Map(Tree t); }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // The collection helper must accept (ctx, depth) and forward them to the element mapper —
        // proving one shared context flows across the collection edge (no fresh-context re-entry).
        Assert.Contains("DwarfRefContext ctx, int depth", generated, StringComparison.Ordinal);
        Assert.Contains("ctx, depth + 1", generated, StringComparison.Ordinal);
        Assert.Contains("TryEnterNode", generated, StringComparison.Ordinal);
        // Must NOT register-before-fill (that is Preserve-only).
        Assert.DoesNotContain("SetReference", generated, StringComparison.Ordinal);
    }

    // ── 10. SetNull threads ctx through a dictionary-value mapper ────────────────
    [Fact]
    public void SetNull_dictionary_value_threads_ctx_into_helper()
    {
        const string src = """
                           using System.Collections.Generic;
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public int V { get; set; } public Dictionary<string, Node>? E { get; set; } }
                           public class NodeDto { public int V { get; set; } public Dictionary<string, NodeDto>? E { get; set; } }
                           [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                           public partial class M { public partial NodeDto Map(Node n); }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("DwarfRefContext ctx, int depth", generated, StringComparison.Ordinal);
        Assert.Contains("TryEnterNode", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("SetReference", generated, StringComparison.Ordinal);
    }
}
