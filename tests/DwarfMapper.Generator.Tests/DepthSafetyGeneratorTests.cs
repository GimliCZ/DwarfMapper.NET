// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     TDD: Plan 19 Part C1 — depth-safety for recursion-capable auto-synthesized mappers.
///     All tests written BEFORE the implementation (Red phase).
/// </summary>
public class DepthSafetyGeneratorTests
{
    // ── 1. Recursion-capable pair: synthesized method has DwarfRefContext + int depth params ─────
    [Fact]
    public void Recursion_capable_synthesized_method_has_ctx_and_depth_params()
    {
        // Node → NodeDto: Node.Next is of type Node → self-referential → recursion-capable
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public int V { get; set; } public Node? Next { get; set; } }
                           public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial NodeDto Map(Node n);
                           }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));

        // The synthesized method for Node→NodeDto must take a DwarfRefContext and int depth parameter
        Assert.Contains("DwarfRefContext", generated, StringComparison.Ordinal);
        Assert.Contains("int depth", generated, StringComparison.Ordinal);
    }

    // ── 2. Non-recursive nested graph: synthesized methods have NO ctx/depth params ─────────────
    [Fact]
    public void Non_recursive_nested_graph_synthesized_methods_have_no_ctx_param()
    {
        // Order → Customer → Address: no type repeats → NOT recursion-capable → zero overhead
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Address  { public string Street { get; set; } = ""; }
                           public class Customer { public Address Home { get; set; } = new(); public string Name { get; set; } = ""; }
                           public class Order    { public Customer Buyer { get; set; } = new(); public int Total { get; set; } }
                           public class AddressDto  { public string Street { get; set; } = ""; }
                           public class CustomerDto { public AddressDto Home { get; set; } = new(); public string Name { get; set; } = ""; }
                           public class OrderDto    { public CustomerDto Buyer { get; set; } = new(); public int Total { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial OrderDto Map(Order o);
                           }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));

        // Synthesized methods for the acyclic graph must NOT include DwarfRefContext parameter
        // They will still call __DwarfMap_Obj_ methods but those should not have ctx/depth
        var synthMethods = generated
            .Split('\n')
            .Where(line => line.Contains("__DwarfMap_Obj_", StringComparison.Ordinal))
            .ToList();
        Assert.True(synthMethods.Count > 0, "Should have synthesized methods");
        // None of the synthesized method DECLARATIONS should have DwarfRefContext
        var declarations = synthMethods.Where(l =>
            (l.Contains("private", StringComparison.Ordinal) ||
             l.Contains("Demo.OrderDto", StringComparison.Ordinal) ||
             l.Contains("Demo.CustomerDto", StringComparison.Ordinal) ||
             l.Contains("Demo.AddressDto", StringComparison.Ordinal))
            && l.Contains('(', StringComparison.Ordinal)).ToList();
        foreach (var decl in declarations) Assert.DoesNotContain("DwarfRefContext", decl, StringComparison.Ordinal);
    }

    // ── 3. Mutually-recursive pair (A↔B): both get ctx/depth ─────────────────────────────────────
    [Fact]
    public void Mutually_recursive_pair_both_get_ctx_and_depth()
    {
        // A has B member, B has A member → mutual cycle → both recursion-capable
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class A { public int V { get; set; } public B? Child { get; set; } }
                           public class B { public int W { get; set; } public A? Parent { get; set; } }
                           public class ADto { public int V { get; set; } public BDto? Child { get; set; } }
                           public class BDto { public int W { get; set; } public ADto? Parent { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial ADto Map(A a);
                           }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));

        // Both synthesized methods should carry DwarfRefContext + depth
        Assert.Contains("DwarfRefContext", generated, StringComparison.Ordinal);
        // The generated code should have depth guard
        Assert.Contains("DwarfMappingDepthException", generated, StringComparison.Ordinal);
    }

    // ── 4. Public Map method takes NO ctx param (unchanged signature) ─────────────────────────────
    [Fact]
    public void Public_Map_method_signature_is_unchanged_no_ctx_param()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public int V { get; set; } public Node? Next { get; set; } }
                           public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial NodeDto Map(Node n);
                           }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);

        // The public partial Map(Node n) must NOT have a DwarfRefContext parameter
        // Generated code uses fully-qualified type names (global::Demo.NodeDto).
        var publicMap = generated
            .Split('\n')
            .FirstOrDefault(l => l.Contains("public partial", StringComparison.Ordinal)
                                 && l.Contains("Map(", StringComparison.Ordinal)
                                 && l.Contains("NodeDto", StringComparison.Ordinal));
        Assert.NotNull(publicMap);
        Assert.DoesNotContain("DwarfRefContext", publicMap, StringComparison.Ordinal);
    }

    // ── 5. Depth guard is at the top of the recursion-capable method body ─────────────────────────
    [Fact]
    public void Recursion_capable_method_has_depth_guard_before_construction()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public int V { get; set; } public Node? Next { get; set; } }
                           public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial NodeDto Map(Node n);
                           }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);

        // The depth guard must be thrown BEFORE the object construction WITHIN the
        // depth-guarded companion method (__DwarfMap_Depth_Map). The companion is emitted
        // after the public Map method, so the depth guard index > first-NodeDto-new index.
        // We locate the depth guard first, then verify a NodeDto construction follows it.
        var depthIdx = generated.IndexOf("DwarfMappingDepthException", StringComparison.Ordinal);
        Assert.True(depthIdx >= 0, "Should contain depth guard");

        // Find a NodeDto construction that comes AFTER the depth guard (within the companion body).
        var newObjAfterDepth = generated.IndexOf("new global::Demo.NodeDto", depthIdx, StringComparison.Ordinal);
        if (newObjAfterDepth < 0)
            newObjAfterDepth = generated.IndexOf("new Demo.NodeDto", depthIdx, StringComparison.Ordinal);
        Assert.True(newObjAfterDepth >= 0, "Should contain object construction after depth guard");
        Assert.True(depthIdx < newObjAfterDepth, "Depth guard must appear before object construction");
    }

    // ── 6. MaxDepth attribute on [DwarfMapper] is recognized ─────────────────────────────────────
    [Fact]
    public void MaxDepth_attribute_compiles_without_error()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public int V { get; set; } public Node? Next { get; set; } }
                           public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                           [DwarfMapper(MaxDepth = 8)]
                           public partial class M
                           {
                               public partial NodeDto Map(Node n);
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ── 7. Generated code for recursion-capable method includes ctx.MaxDepth check ──────────────
    [Fact]
    public void Generated_depth_guard_references_ctx_MaxDepth()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public int V { get; set; } public Node? Next { get; set; } }
                           public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                           [DwarfMapper(MaxDepth = 8)]
                           public partial class M
                           {
                               public partial NodeDto Map(Node n);
                           }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);

        // The depth guard should compare depth > ctx.MaxDepth (or similar)
        Assert.Contains("ctx.MaxDepth", generated, StringComparison.Ordinal);
    }

    // ── 8. Recursive call passes depth + 1 ───────────────────────────────────────────────────────
    [Fact]
    public void Recursive_call_passes_depth_plus_one()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public int V { get; set; } public Node? Next { get; set; } }
                           public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial NodeDto Map(Node n);
                           }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);

        // Recursive calls must pass depth + 1 (not depth)
        Assert.Contains("depth + 1", generated, StringComparison.Ordinal);
    }

    // ── 9. DwarfRefContext is created in the public method entrypoint ─────────────────────────────
    [Fact]
    public void Public_method_creates_DwarfRefContext_for_recursion_capable_pair()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Node    { public int V { get; set; } public Node? Next { get; set; } }
                           public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial NodeDto Map(Node n);
                           }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);

        // The public method must create a DwarfRefContext (new DwarfRefContext(...))
        Assert.Contains("new global::DwarfMapper.DwarfRefContext(", generated, StringComparison.Ordinal);
    }

    // ── 10. Non-recursive non-partial synthesized method has NO ctx/depth (zero overhead proof) ──
    [Fact]
    public void Non_recursive_synthesized_method_emits_no_ctx_or_depth()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Address  { public string Street { get; set; } = ""; public int Zip { get; set; } }
                           public class Customer { public Address Home { get; set; } = new(); }
                           public class AddressDto  { public string Street { get; set; } = ""; public int Zip { get; set; } }
                           public class CustomerDto { public AddressDto Home { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial CustomerDto Map(Customer c);
                           }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));

        // Zero overhead: no DwarfRefContext anywhere in the generated code
        Assert.DoesNotContain("DwarfRefContext", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("DwarfMappingDepthException", generated, StringComparison.Ordinal);
    }
}
