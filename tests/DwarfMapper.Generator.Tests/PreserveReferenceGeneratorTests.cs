// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     TDD: Plan 19 Part C2 — full Preserve object-graph reconstruction via reference identity.
///     All tests written BEFORE implementation (Red phase).
/// </summary>
public class PreserveReferenceGeneratorTests
{
    // ── 1. ReferenceHandling=Preserve compiles without error ─────────────────────
    [Fact]
    public void Preserve_attribute_compiles_without_error()
    {
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
    }

    // ── 2. Preserve mode emits TryGetReference check ─────────────────────────────
    [Fact]
    public void Preserve_mode_emits_TryGetReference_in_recursion_capable_method()
    {
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
        Assert.Contains("TryGetReference", generated, StringComparison.Ordinal);
    }

    // ── 3. Preserve mode emits SetReference BEFORE member population ─────────────
    [Fact]
    public void Preserve_mode_emits_SetReference_before_member_population()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public int V { get; set; } public Node? Next { get; set; } }
                           public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial NodeDto Map(Node n); }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        // SetReference must appear before "t.V =" or "t.Next ="
        var setRefIdx = generated.IndexOf("SetReference", StringComparison.Ordinal);
        Assert.True(setRefIdx >= 0, "Should contain SetReference");
        // Find first per-member assignment after SetReference (e.g. "t.V = " or "t.Next = ")
        var memberAssign = generated.IndexOf(".V = ", setRefIdx, StringComparison.Ordinal);
        if (memberAssign < 0)
            memberAssign = generated.IndexOf(".Next = ", setRefIdx, StringComparison.Ordinal);
        Assert.True(memberAssign > setRefIdx, "SetReference must appear before member population");
    }

    // ── 4. Preserve mode uses multi-statement form (not single-expression new T{}) ─
    [Fact]
    public void Preserve_mode_uses_multi_statement_construction_not_single_expression()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public int V { get; set; } public Node? Next { get; set; } }
                           public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial NodeDto Map(Node n); }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        // Multi-statement: "var __dwarf_t = new NodeDto(" or "var __dwarf_t = new global::Demo.NodeDto"
        // and separate member assignment lines
        Assert.Contains("var __dwarf_t", generated, StringComparison.Ordinal);
    }

    // ── 5. None mode does NOT emit TryGetReference (zero overhead) ───────────────
    [Fact]
    public void None_mode_does_not_emit_TryGetReference()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public int V { get; set; } public Node? Next { get; set; } }
                           public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial NodeDto Map(Node n); }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain("TryGetReference", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("SetReference", generated, StringComparison.Ordinal);
    }

    // ── 6. Acyclic pair under Preserve: compiles, ctx threaded for faithful topology ─
    // Under Preserve, ALL auto-synthesized object mappers receive ctx (uniform threading)
    // so that any shared (diamond) instance — even without back-edges — deduplicates to
    // one target. The old assertion "no TryGetReference for acyclic" was wrong because a
    // non-cyclic pair can still be shared (root.Left == root.Right, no back-edge).
    [Fact]
    public void Acyclic_pair_under_Preserve_compiles_cleanly_and_ctx_threaded()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Address    { public string Street { get; set; } = ""; }
                           public class Customer   { public Address Home { get; set; } = new(); }
                           public class AddressDto { public string Street { get; set; } = ""; }
                           public class CustomerDto{ public AddressDto Home { get; set; } = new(); }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial CustomerDto Map(Customer c); }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // Under Preserve, the synthesized Address→AddressDto mapper must thread ctx
        // (uniform threading for faithful topology). TryGetReference must be present.
        Assert.Contains("TryGetReference", generated, StringComparison.Ordinal);
        // The public Map method must create a DwarfRefContext.
        Assert.Contains("DwarfRefContext", generated, StringComparison.Ordinal);
    }

    // ── 7. DWARF030: cyclic ctor param emits DWARF030 ────────────────────────────
    [Fact]
    public void Cyclic_ctor_param_emits_DWARF030()
    {
        // ImmutableNode is a record (positional ctor): the Next param IS the cyclic member.
        // Under Preserve, register-before-populate is impossible → DWARF030.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public record ImmutableNode(int V, ImmutableNode? Next);
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial ImmutableNode Map(ImmutableNode n); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diags, d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Id == "DWARF030");
    }

    // ── 8. Preserve mode: DwarfRefContext constructed with identity map flag ──────
    [Fact]
    public void Preserve_mode_public_method_creates_DwarfRefContext_with_preserve_flag()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public int V { get; set; } public Node? Next { get; set; } }
                           public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial NodeDto Map(Node n); }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        // The public method should create DwarfRefContext with preserve=true
        Assert.Contains("new global::DwarfMapper.DwarfRefContext(", generated, StringComparison.Ordinal);
        // With Preserve mode, the second argument (or a boolean) should indicate preserve
        Assert.Contains("true", generated, StringComparison.Ordinal);
    }

    // ── 9. Preserve mode: generated code compiles without error ──────────────────
    [Fact]
    public void Preserve_mode_generated_code_compiles_cleanly()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public int V { get; set; } public Node? Next { get; set; } }
                           public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial NodeDto Map(Node n); }
                           """;
        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
        Assert.Empty(errors);
    }

    // ── 10. Mutual cycle A⇄B under Preserve: both methods get TryGetReference ────
    [Fact]
    public void Mutual_cycle_both_methods_get_TryGetReference_under_Preserve()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class A { public int V { get; set; } public B? Child { get; set; } }
                           public class B { public int W { get; set; } public A? Parent { get; set; } }
                           public class ADto { public int V { get; set; } public BDto? Child { get; set; } }
                           public class BDto { public int W { get; set; } public ADto? Parent { get; set; } }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial ADto Map(A a); }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // Both synthesized methods should have TryGetReference
        var count = 0;
        var start = 0;
        while (true)
        {
            var idx = generated.IndexOf("TryGetReference", start, StringComparison.Ordinal);
            if (idx < 0) break;
            count++;
            start = idx + 1;
        }

        Assert.True(count >= 2, $"Expected at least 2 TryGetReference calls for A⇄B, got {count}");
    }

    // ── 11. Preserve + shared object (non-cyclic): compiles, ctx threaded ────────
    // Root has two members of the same type (diamond — no back-edge). This was the
    // exact shape of the C2 bug: Holder was NOT recursion-capable (no type-graph cycle)
    // but its synthesized mapper required ctx (Preserve List helper). The public Map
    // method must create DwarfRefContext and pass it, producing no CS7036.
    [Fact]
    public void Preserve_shared_non_cyclic_object_compiles_without_CS7036()
    {
        const string src = """
                           using System.Collections.Generic;
                           using DwarfMapper;
                           namespace Demo;
                           public class Holder    { public List<int> Items { get; set; } = new(); }
                           public class HolderDto { public List<int> Items { get; set; } = new(); }
                           public class Root    { public Holder P1 { get; set; } = new(); public Holder P2 { get; set; } = new(); }
                           public class RootDto { public HolderDto P1 { get; set; } = new(); public HolderDto P2 { get; set; } = new(); }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial RootDto Map(Root r); }
                           """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        // The public Map(Root) must create a DwarfRefContext (to pass ctx to the Holder mapper).
        Assert.Contains("DwarfRefContext", generated, StringComparison.Ordinal);
    }

    // ── 12. Preserve + plain diamond (no back-edge): compiles cleanly ────────────
    [Fact]
    public void Preserve_plain_diamond_no_back_edge_compiles_cleanly()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Leaf    { public int V { get; set; } }
                           public class DiamondRoot { public Leaf? Left { get; set; } public Leaf? Right { get; set; } }
                           public class LeafDto    { public int V { get; set; } }
                           public class DiamondRootDto { public LeafDto? Left { get; set; } public LeafDto? Right { get; set; } }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial DiamondRootDto Map(DiamondRoot r); }
                           """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ── 13. Preserve + record target: compiles cleanly ───────────────────────────
    [Fact]
    public void Preserve_record_target_compiles_cleanly()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Src { public int X { get; set; } public string Y { get; set; } = ""; }
                           public record Dst(int X, string Y);
                           public class Outer { public Src? A { get; set; } public Src? B { get; set; } }
                           public class OuterDto { public Dst? A { get; set; } public Dst? B { get; set; } }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial OuterDto Map(Outer o); }
                           """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ── 14. Preserve + nested collection of objects: compiles cleanly ─────────────
    [Fact]
    public void Preserve_nested_collection_of_objects_compiles_cleanly()
    {
        const string src = """
                           using System.Collections.Generic;
                           using DwarfMapper;
                           namespace Demo;
                           public class Child    { public int V { get; set; } }
                           public class ChildDto { public int V { get; set; } }
                           public class Parent    { public List<Child> Kids { get; set; } = new(); }
                           public class ParentDto { public List<ChildDto> Kids { get; set; } = new(); }
                           public class Root    { public Parent? A { get; set; } public Parent? B { get; set; } }
                           public class RootDto { public ParentDto? A { get; set; } public ParentDto? B { get; set; } }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial RootDto Map(Root r); }
                           """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ── 15. None mode: acyclic mapper has NO DwarfRefContext param (zero overhead) ─
    [Fact]
    public void None_mode_acyclic_mapper_has_no_DwarfRefContext_param()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Src { public int V { get; set; } public string Label { get; set; } = ""; }
                           public class Dst { public int V { get; set; } public string Label { get; set; } = ""; }
                           [DwarfMapper]
                           public partial class M { public partial Dst Map(Src s); }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // None mode: no ctx parameter anywhere in the generated code.
        Assert.DoesNotContain("DwarfRefContext", generated, StringComparison.Ordinal);
    }

    // ── 16. None mode: acyclic with nested object mapper has NO DwarfRefContext ────
    [Fact]
    public void None_mode_nested_object_mapper_has_no_DwarfRefContext_param()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Inner    { public int X { get; set; } }
                           public class InnerDto { public int X { get; set; } }
                           public class Outer    { public Inner? A { get; set; } public Inner? B { get; set; } }
                           public class OuterDto { public InnerDto? A { get; set; } public InnerDto? B { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial OuterDto Map(Outer o); }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.DoesNotContain("DwarfRefContext", generated, StringComparison.Ordinal);
    }

    // ── MF1: scalar ctor params must NOT trigger DWARF030 under Preserve ─────

    // MF1-a: direct Src → record Dst(int, string) — scalars can never cycle → no DWARF030
    [Fact]
    public void Preserve_scalar_record_ctor_params_do_not_emit_DWARF030()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Src { public int Id { get; set; } public string Name { get; set; } = ""; }
                           public record Dst(int Id, string Name);
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial Dst Map(Src s); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Id == "DWARF030");
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // MF1-b: non-cyclic reference ctor param — acyclic AddressDto is safe, no DWARF030
    [Fact]
    public void Preserve_noncyclic_ref_record_ctor_param_does_not_emit_DWARF030()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Address    { public string Street { get; set; } = ""; }
                           public class AddressDto { public string Street { get; set; } = ""; }
                           public class Src { public int Id { get; set; } public Address Addr { get; set; } = new(); }
                           public record Dto(int Id, AddressDto Addr);
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial Dto Map(Src s); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Id == "DWARF030");
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // MF1-c: genuine S==T self-cycle with ctor → DWARF030 still fires (regression guard)
    [Fact]
    public void Preserve_self_cycle_record_ctor_still_emits_DWARF030()
    {
        // ImmutableNode maps to itself — the Next param IS a cyclic back-edge → DWARF030
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public record ImmutableNode(int V, ImmutableNode? Next);
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial ImmutableNode Map(ImmutableNode n); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diags, d => d.Id == "DWARF030");
    }
}
