// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// TDD: Plan 19 Part C2 — full Preserve object-graph reconstruction via reference identity.
/// All tests written BEFORE implementation (Red phase).
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

    // ── 6. Acyclic pair under Preserve: no TryGetReference (scoped to recursion-capable) ─
    [Fact]
    public void Acyclic_pair_under_Preserve_emits_no_TryGetReference()
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
        // Acyclic pair should NOT use TryGetReference even under Preserve
        Assert.DoesNotContain("TryGetReference", generated, StringComparison.Ordinal);
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
}
