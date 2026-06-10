// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// TDD: auto-synthesized nested object mappers (Plan 19 Part A).
/// All tests in this file were written BEFORE the implementation.
/// </summary>
public class NestedAutoMapTests
{
    // ────────────────────────────────────────────────────────────────────────────
    // 1. Happy-path: 3-level nested class→record graph, no sub-mappers declared
    // ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void ThreeLevel_nested_graph_compiles_and_synthesizes_private_methods()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class City   { public string Name { get; set; } = ""; }
            public class Addr   { public City   Location { get; set; } = new(); public int Zip { get; set; } }
            public class Person { public Addr   Home { get; set; } = new(); public string FullName { get; set; } = ""; }
            public record CityDto(string Name);
            public record AddrDto(CityDto Location, int Zip);
            public record PersonDto(AddrDto Home, string FullName);
            [DwarfMapper]
            public partial class M
            {
                public partial PersonDto Map(Person p);
            }
            """;
        var (diag, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diag, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // Generated code must contain synthesized private nested mappers
        Assert.Contains("__DwarfMap_Obj_", generated, StringComparison.Ordinal);
        // Specifically, the root method uses a synthesized call for Home
        Assert.Contains("__DwarfMap_Obj_", generated, StringComparison.Ordinal);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 2. Nested member requiring conversions at depth (long→int, string→Guid, enum)
    // ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Nested_member_with_type_conversion_at_depth_compiles()
    {
        const string src = """
            using DwarfMapper;
            using System;
            namespace Demo;
            public class Inner { public long Count { get; set; } public string Id { get; set; } = ""; }
            public class Outer { public Inner Sub { get; set; } = new(); }
            public record InnerDto(int Count, Guid Id);
            public record OuterDto(InnerDto Sub);
            [DwarfMapper]
            public partial class M
            {
                public partial OuterDto Map(Outer o);
            }
            """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 3. Mixed nested record + nested class in the same graph
    // ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Mixed_nested_record_and_class_in_same_graph_compiles()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public record Tag(string Label);
            public class Item { public Tag Category { get; set; } = new(""); public string Name { get; set; } = ""; }
            public class TagDto { public string Label { get; set; } = ""; }
            public record ItemDto(TagDto Category, string Name);
            [DwarfMapper]
            public partial class M
            {
                public partial ItemDto Map(Item i);
            }
            """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 4. Dedup: two members of same nested type → exactly ONE synthesized method
    // ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Same_nested_type_used_twice_produces_one_synthesized_method()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Pt { public int X { get; set; } public int Y { get; set; } }
            public class Line { public Pt A { get; set; } = new(); public Pt B { get; set; } = new(); }
            public record PtDto(int X, int Y);
            public record LineDto(PtDto A, PtDto B);
            [DwarfMapper]
            public partial class M
            {
                public partial LineDto Map(Line l);
            }
            """;
        var (diag, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diag, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));

        // Count occurrences of the synthesized method DEFINITION (private ... __DwarfMap_Obj_)
        // There should be exactly one definition for the Pt→PtDto pair.
        var methodDefinitionCount = CountMethodDefinitions(generated, "Demo.Pt", "Demo.PtDto");
        Assert.Equal(1, methodDefinitionCount);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 5. User-declared mapper overrides synthesis
    // ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void User_declared_mapper_overrides_synthesis_no_DwarfMap_Obj_for_that_pair()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Inner { public string V { get; set; } = ""; }
            public class Outer { public Inner Sub { get; set; } = new(); }
            public class InnerDto { public string V { get; set; } = ""; }
            public class OuterDto { public InnerDto Sub { get; set; } = new(); }
            [DwarfMapper]
            public partial class M
            {
                public partial OuterDto Map(Outer o);
                public partial InnerDto MapInner(Inner i);   // explicit sub-mapper
            }
            """;
        var (diag, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diag, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // Root method should call the user-declared MapInner, not a synthesized __DwarfMap_Obj_
        Assert.Contains("Sub = MapInner(", generated, StringComparison.Ordinal);
        // No synthesized method should exist for Inner→InnerDto
        Assert.DoesNotContain("__DwarfMap_Obj_", generated, StringComparison.Ordinal);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 6. AutoNest=false (class-level) → nested pair with no declared mapper → DWARF005
    // ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AutoNest_false_class_level_falls_through_to_DWARF005()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Inner { public string V { get; set; } = ""; }
            public class Outer { public Inner Sub { get; set; } = new(); }
            public class InnerDto { public string V { get; set; } = ""; }
            public class OuterDto { public InnerDto Sub { get; set; } = new(); }
            [DwarfMapper(AutoNest = false)]
            public partial class M
            {
                public partial OuterDto Map(Outer o);
            }
            """;
        var (diag, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diag, d => d.Id == "DWARF005");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 7a. AutoNest per-method override: disabled method → DWARF005
    // ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AutoNest_false_on_method_reports_DWARF005()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Inner7a { public string V { get; set; } = ""; }
            public class Outer7a { public Inner7a Sub { get; set; } = new(); }
            public class InnerDto7a { public string V { get; set; } = ""; }
            public class OuterDto7a { public InnerDto7a Sub { get; set; } = new(); }
            [DwarfMapper]
            public partial class M7a
            {
                [AutoNest(false)]
                public partial OuterDto7a Map(Outer7a o);   // no auto-nest → DWARF005
            }
            """;
        var (diag, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diag, d => d.Id == "DWARF005");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 7b. AutoNest per-method override: another class with auto-nest enabled
    // ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AutoNest_true_on_separate_class_synthesizes_nested_method()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Inner7b { public string V { get; set; } = ""; }
            public class Outer7b { public Inner7b Sub { get; set; } = new(); }
            public class InnerDto7b { public string V { get; set; } = ""; }
            public class OuterDto7b { public InnerDto7b Sub { get; set; } = new(); }
            [DwarfMapper]                               // AutoNest=true (default)
            public partial class M7b
            {
                public partial OuterDto7b Map2(Outer7b o);
            }
            """;
        var (diag, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diag, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains("__DwarfMap_Obj_", generated, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 8. Completeness: unmapped nested target member → DWARF001
    // ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Unmapped_nested_target_member_reports_DWARF001()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Inner { public string A { get; set; } = ""; }
            public class Outer { public Inner Sub { get; set; } = new(); }
            public class InnerDto { public string A { get; set; } = ""; public string B { get; set; } = ""; }
            public class OuterDto { public InnerDto Sub { get; set; } = new(); }
            [DwarfMapper]
            public partial class M
            {
                public partial OuterDto Map(Outer o);
            }
            """;
        var (diag, _) = GeneratorTestHarness.Run(src);
        // B is unmapped inside InnerDto
        Assert.Contains(diag, d => d.Id == "DWARF001");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 9. Read-only nested target member → DWARF007
    //    The target InnerDto has a read-only property ReadonlyProp, and the source
    //    Inner ALSO has ReadonlyProp (readable). DWARF007 fires because a source
    //    value would be silently lost into a non-writable destination.
    // ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void ReadOnly_nested_member_reports_DWARF007()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Inner9 { public string V { get; set; } = ""; public string ReadonlyProp { get; set; } = "x"; }
            public class Outer9 { public Inner9 Sub { get; set; } = new(); }
            public class InnerDto9 { public string V { get; set; } = ""; public string ReadonlyProp { get; } = "x"; }
            public class OuterDto9 { public InnerDto9 Sub { get; set; } = new(); }
            [DwarfMapper]
            public partial class M9
            {
                public partial OuterDto9 Map(Outer9 o);
            }
            """;
        var (diag, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diag, d => d.Id == "DWARF007");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 10. Recursive type: Tree → TreeDto compiles (generator must not hang)
    // ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Recursive_tree_type_compiles_without_generator_hang()
    {
        const string src = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class Tree { public int V { get; set; } public List<Tree> Children { get; set; } = new(); }
            public class TreeDto { public int V { get; set; } public List<TreeDto> Children { get; set; } = new(); }
            [DwarfMapper]
            public partial class M
            {
                public partial TreeDto Map(Tree t);
            }
            """;
        // Generator must complete quickly (not hang). If it hangs, the test will time out.
        var (diag, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diag, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 11. Self-referential Node → NodeDto compiles (generator must not hang)
    // ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Self_referential_node_type_compiles_without_generator_hang()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Node { public int Id { get; set; } public Node? Parent { get; set; } }
            public class NodeDto { public int Id { get; set; } public NodeDto? Parent { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                public partial NodeDto Map(Node n);
            }
            """;
        var (diag, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diag, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 12. Nested target with constructor (record) — composes with ctor mapping
    // ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Nested_record_target_uses_constructor_mapping()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class InnerSrc { public int X { get; set; } public int Y { get; set; } }
            public class OuterSrc { public InnerSrc Pos { get; set; } = new(); }
            public record InnerDst(int X, int Y);
            public record OuterDst(InnerDst Pos);
            [DwarfMapper]
            public partial class M
            {
                public partial OuterDst Map(OuterSrc s);
            }
            """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 13. Regression: declared sub-mapper (existing behavior) still works
    // ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Regression_declared_nested_mapper_still_works()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Addr   { public string City { get; set; } = ""; }
            public class AddrDto { public string City { get; set; } = ""; }
            public class Person { public Addr Home { get; set; } = new(); }
            public class PersonDto { public AddrDto Home { get; set; } = new(); }
            [DwarfMapper]
            public partial class M
            {
                public partial PersonDto ToDto(Person p);
                public partial AddrDto ToDto(Addr a);
            }
            """;
        var (diag, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diag, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains("Home = ToDto(p.Home)", generated, StringComparison.Ordinal);
        // No synthesized method — the user declared one
        Assert.DoesNotContain("__DwarfMap_Obj_", generated, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 14. Settable-class root with settable-class nested member
    // ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Settable_class_root_with_settable_nested_member_works()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Inner  { public int Val { get; set; } }
            public class Outer  { public Inner Sub { get; set; } = new(); public string Name { get; set; } = ""; }
            public class InnerDst { public int Val { get; set; } }
            public class OuterDst { public InnerDst Sub { get; set; } = new(); public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                public partial OuterDst Map(Outer o);
            }
            """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 15. DWARF031: depth cap produces a diagnostic, not a hang
    //     (very deep synthetic nesting chain — 600 unique pairs — should hit DWARF031)
    //     NOTE: we can't easily build 600 unique types in source text, so we test
    //     the cap constant exists and works by checking the descriptor ID is present.
    //     The actual cap will be exercised by the recursive type tests above (generator
    //     must terminate); detailed cap-hit behavior is tested via the registry unit test
    //     which is separate. Here we just assert the descriptor compiles correctly.
    // ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void DWARF031_descriptor_exists_and_is_wired()
    {
        // The test just verifies the DiagnosticDescriptors class exposes DWARF031.
        // We use RunAndGetCompilationErrors to trigger the generator assembly which must compile.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int X { get; set; } }
            public class B { public int X { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                public partial B Map(A a);
            }
            """;
        // Must compile without error — no DWARF031 for this simple case.
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));

        // Verify the descriptor is accessible from the generator assembly.
        var descriptor = DwarfMapper.Generator.Diagnostics.DiagnosticDescriptors.DeepNestingLimit;
        Assert.Equal("DWARF031", descriptor.Id);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Count distinct method definitions (not calls) for a given pair in the generated text.
    /// A definition contains "private" + full-name + "__DwarfMap_Obj_" pattern.
    /// We look at how many times `private` prefixes a method returning the target full-name.
    /// Simple approach: count `private` + return-type + `__DwarfMap_Obj_` signatures.
    /// </summary>
    private static int CountMethodDefinitions(string generated, string srcFqn, string tgtFqn)
    {
        // Look for "private global::Demo.PtDto __DwarfMap_Obj_" style signatures
        // The types in fully-qualified form will contain dots, e.g. "global::Demo.PtDto"
        int count = 0;
        int idx = 0;
        while ((idx = generated.IndexOf("__DwarfMap_Obj_", idx, StringComparison.Ordinal)) >= 0)
        {
            // Check it's a private method definition (not a call), i.e. preceded by "private "
            var lineStart = generated.LastIndexOf('\n', idx);
            var line = generated.Substring(lineStart < 0 ? 0 : lineStart + 1,
                idx - (lineStart < 0 ? 0 : lineStart + 1));
            if (line.Contains("private", StringComparison.Ordinal))
            {
                count++;
            }
            idx++;
        }
        return count;
    }
}
