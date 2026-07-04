// SPDX-License-Identifier: GPL-2.0-only
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Plan 19D — TDD tests for the recursive, translatability-classifying projection resolver.
/// SAFE: nested object inline, collection inline, ctor inline, widening numeric, enum by-value.
/// UNSAFE: each → DWARF028 with appropriate reason; NO __DwarfMap_ helper calls in output.
/// </summary>
public class ProjectionDeepTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // SAFE MATRIX
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Projection_flat_regression_still_works_no_DwarfMap_helper()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Src { public int Age { get; set; } public string Name { get; set; } = ""; }
            public class Dst { public int Age { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper] public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Contains("Age = __s.Age", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_nested_object_2level_inlines_new_inner_no_DwarfMap_helper()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Inner { public int X { get; set; } }
            public class InnerDto { public int X { get; set; } }
            public class Outer { public int Id { get; set; } public Inner? Inner { get; set; } }
            public class OuterDto { public int Id { get; set; } public InnerDto? Inner { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("new global::D.InnerDto", gen, StringComparison.Ordinal);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_nested_object_null_nav_ternary_emitted_for_nullable_ref()
    {
        // Nullable reference src.Inner → ternary: __s.Inner == null ? null : new InnerDto{...}
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Inner { public int X { get; set; } }
            public class InnerDto { public int X { get; set; } }
            public class Outer { public int Id { get; set; } public Inner? Inner { get; set; } }
            public class OuterDto { public int Id { get; set; } public InnerDto? Inner { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        // Null-nav ternary must appear for the nullable inner
        Assert.Contains("== null ? null :", gen, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_nested_object_3level_inlines_all_no_DwarfMap_helper()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class L3 { public int Z { get; set; } }
            public class L3Dto { public int Z { get; set; } }
            public class L2 { public int Y { get; set; } public L3? Deep { get; set; } }
            public class L2Dto { public int Y { get; set; } public L3Dto? Deep { get; set; } }
            public class L1 { public int X { get; set; } public L2? Mid { get; set; } }
            public class L1Dto { public int X { get; set; } public L2Dto? Mid { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<L1Dto> Prj(IQueryable<L1> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("new global::D.L2Dto", gen, StringComparison.Ordinal);
        Assert.Contains("new global::D.L3Dto", gen, StringComparison.Ordinal);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_collection_list_to_list_inlines_Select_ToList_no_DwarfMap_helper()
    {
        const string s = """
            using DwarfMapper; using System.Linq; using System.Collections.Generic;
            namespace D;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            public class Outer { public List<Item> Items { get; set; } = new(); }
            public class OuterDto { public List<ItemDto> Items { get; set; } = new(); }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Enumerable.Select", gen, StringComparison.Ordinal);
        Assert.Contains("Enumerable.ToList", gen, StringComparison.Ordinal);
        Assert.Contains("new global::D.ItemDto", gen, StringComparison.Ordinal);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_dotted_source_path_flattens_a_value_member()
    {
        // [MapProperty("Colour.Code", ...)] flattens a nested/value-object member into a scalar in
        // projection — matching the class-model dotted-path feature. The emitted accessor is __s.Colour.Code.
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Colour { public string Code { get; set; } = ""; }
            public class Src { public int Id { get; set; } public Colour Colour { get; set; } = new(); }
            public class Dst { public int Id { get; set; } public string Colour { get; set; } = ""; }
            [DwarfMapper] public partial class M {
                [MapProperty("Colour.Code", nameof(Dst.Colour))]
                public partial IQueryable<Dst> Prj(IQueryable<Src> q);
            }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Colour.Code", gen, StringComparison.Ordinal);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_non_nullable_source_omits_null_guard()
    {
        // Honour the consumer's nullability: a NON-nullable reference source (nullable context enabled)
        // must NOT emit a null-navigation guard — guarding it would assign null to a non-nullable target
        // (false CS8601/CS8603 in strict-nullable hosts). Nullable sources still get the guard (see the
        // *_null_nav_ternary_emitted_for_nullable_ref test).
        const string s = """
            #nullable enable
            using DwarfMapper; using System.Linq; using System.Collections.Generic;
            namespace D;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            public class Outer { public List<Item> Items { get; set; } = new(); public Item Lead { get; set; } = new(); }
            public class OuterDto { public List<ItemDto> Items { get; set; } = new(); public ItemDto Lead { get; set; } = new(); }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(gen, "== null ? null :", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_collection_array_to_array_inlines_Select_ToArray_no_DwarfMap_helper()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            public class Outer { public Item[] Items { get; set; } = []; }
            public class OuterDto { public ItemDto[] Items { get; set; } = []; }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Enumerable.Select", gen, StringComparison.Ordinal);
        Assert.Contains("Enumerable.ToArray", gen, StringComparison.Ordinal);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_collection_IEnumerable_target_lazy_no_ToList_no_DwarfMap_helper()
    {
        const string s = """
            using DwarfMapper; using System.Linq; using System.Collections.Generic;
            namespace D;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            public class Outer { public IEnumerable<Item> Items { get; set; } = []; }
            public class OuterDto { public IEnumerable<ItemDto> Items { get; set; } = []; }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Enumerable.Select", gen, StringComparison.Ordinal);
        // IEnumerable target → no ToList/ToArray terminal
        Assert.DoesNotContain("Enumerable.ToList", gen, StringComparison.Ordinal);
        Assert.DoesNotContain("Enumerable.ToArray", gen, StringComparison.Ordinal);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_ctor_record_positional_inlines_new_ctor_no_DwarfMap_helper()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Src { public int X { get; set; } public string Y { get; set; } = ""; }
            public record DstRec(int X, string Y);
            [DwarfMapper] public partial class M { public partial IQueryable<DstRec> Prj(IQueryable<Src> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        // Constructor projection → new DstRec(...)
        Assert.Contains("new global::D.DstRec(", gen, StringComparison.Ordinal);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_widening_numeric_int_to_long_inline()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Src { public int X { get; set; } }
            public class Dst { public long X { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        // int→long is an implicit widening — direct assignment, no cast needed
        Assert.Contains("X = __s.X", gen, StringComparison.Ordinal);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_enum_by_value_same_type_direct_assign()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public enum Status { A, B }
            public class Src { public Status S { get; set; } }
            public class Dst { public Status S { get; set; } }
            [DwarfMapper(EnumStrategy = DwarfMapper.EnumStrategy.ByValue)]
            public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_enum_by_value_different_enum_type_cast_inline()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public enum E1 { A = 0, B = 1 }
            public enum E2 { A = 0, B = 1 }
            public class Src { public E1 E { get; set; } }
            public class Dst { public E2 E { get; set; } }
            [DwarfMapper(EnumStrategy = DwarfMapper.EnumStrategy.ByValue)]
            public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        // By-value enum: cast inline (E2)__s.E
        Assert.Contains("(global::D.E2)", gen, StringComparison.Ordinal);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // UNSAFE MATRIX — each → DWARF028 with right reason
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Projection_narrowing_numeric_long_to_int_reports_DWARF028_narrowing_reason()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Src { public long X { get; set; } }
            public class Dst { public int X { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        var d028 = diag.Where(d => d.Id == "DWARF028").ToList();
        Assert.NotEmpty(d028);
        Assert.Contains("narrowing", d028[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projection_string_to_int_parsable_reports_DWARF028_parse_reason()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Src { public string X { get; set; } = ""; }
            public class Dst { public int X { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        var d028 = diag.Where(d => d.Id == "DWARF028").ToList();
        Assert.NotEmpty(d028);
        Assert.Contains("parse", d028[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projection_enum_by_name_reports_DWARF028_byname_reason()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public enum E1 { A, B }
            public enum E2 { A, B }
            public class Src { public E1 E { get; set; } }
            public class Dst { public E2 E { get; set; } }
            [DwarfMapper(EnumStrategy = DwarfMapper.EnumStrategy.ByName)]
            public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        var d028 = diag.Where(d => d.Id == "DWARF028").ToList();
        Assert.NotEmpty(d028);
        Assert.Contains("by-name", d028[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projection_custom_Use_converter_reports_DWARF028_converter_reason()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Src { public int X { get; set; } }
            public class Dst { public string X { get; set; } = ""; }
            [DwarfMapper] public partial class M
            {
                [MapProperty("X", "X", Use = "Conv")]
                public partial IQueryable<Dst> Prj(IQueryable<Src> q);
                private static string Conv(int x) => x.ToString();
            }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        var d028 = diag.Where(d => d.Id == "DWARF028").ToList();
        Assert.NotEmpty(d028);
        Assert.Contains("converter", d028[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projection_enum_unsigned_underlying_to_signed_int_is_narrowing_DWARF028()
    {
        // enum : uint -> int is a lossy unsigned→signed narrowing (uint 3e9 wraps negative). A projection
        // can't emit a checked cast, so it must be DWARF028 — NOT a silent (int) cast. Regression for the
        // signedness-aware widening check (the old bit-rank check wrongly treated uint→int as widening).
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public enum EU : uint { A = 1 }
            public class Src { public EU C { get; set; } }
            public class Dst { public int C { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        var d028 = diag.Where(d => d.Id == "DWARF028").ToList();
        Assert.NotEmpty(d028);
        Assert.Contains("narrowing", d028[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projection_enum_short_underlying_to_int_is_safe_widening_plain_cast()
    {
        // enum : short -> int is genuine widening (same signedness, wider) → inline plain (int) cast, no error.
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public enum ES : short { A = 1 }
            public class Src { public ES C { get; set; } }
            public class Dst { public int C { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("(int)", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_HashSet_collection_target_reports_DWARF028_collection_reason()
    {
        const string s = """
            using DwarfMapper; using System.Linq; using System.Collections.Generic;
            namespace D;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            public class Outer { public HashSet<Item> Items { get; set; } = new(); }
            public class OuterDto { public HashSet<ItemDto> Items { get; set; } = new(); }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        var d028 = diag.Where(d => d.Id == "DWARF028").ToList();
        Assert.NotEmpty(d028);
        Assert.Contains("translatable", d028[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projection_ImmutableArray_collection_target_reports_DWARF028()
    {
        const string s = """
            using DwarfMapper; using System.Linq; using System.Collections.Immutable;
            namespace D;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            public class Outer { public ImmutableArray<Item> Items { get; set; } }
            public class OuterDto { public ImmutableArray<ItemDto> Items { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diag, d => d.Id == "DWARF028");
    }

    [Fact]
    public void Projection_Dictionary_collection_target_reports_DWARF028()
    {
        const string s = """
            using DwarfMapper; using System.Linq; using System.Collections.Generic;
            namespace D;
            public class Outer { public Dictionary<string, int> Tags { get; set; } = new(); }
            public class OuterDto { public Dictionary<string, int> Tags { get; set; } = new(); }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diag, d => d.Id == "DWARF028");
    }

    [Fact]
    public void Projection_reference_handling_preserve_reports_DWARF028_refhandling_reason()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Src { public int X { get; set; } }
            public class Dst { public int X { get; set; } }
            [DwarfMapper(ReferenceHandling = DwarfMapper.ReferenceHandlingStrategy.Preserve)]
            public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        var d028 = diag.Where(d => d.Id == "DWARF028").ToList();
        Assert.NotEmpty(d028);
        Assert.Contains("reference handling", d028[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    // ── Regression: non-assignable flat member ──
    [Fact]
    public void Projection_non_assignable_flat_member_reports_DWARF028()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Src { public string Age { get; set; } = ""; }
            public class Dst { public int Age { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        // string→int triggers the parsable converter, which is not provider-translatable → DWARF028.
        Assert.Contains(diag, d => d.Id == "DWARF028");
    }

    // ── Defensive: no __DwarfMap_ in any SAFE projection output ──
    [Fact]
    public void All_safe_projection_outputs_contain_no_DwarfMap_helper_calls()
    {
        var cases = new[]
        {
            // flat
            "using DwarfMapper; using System.Linq; namespace D; public class S{public int A{get;set;}} public class T{public int A{get;set;}} [DwarfMapper] public partial class M{public partial IQueryable<T> P(IQueryable<S> q);}",
            // nested
            "using DwarfMapper; using System.Linq; namespace D; public class I{public int X{get;set;}} public class ID{public int X{get;set;}} public class S{public I? N{get;set;}} public class T{public ID? N{get;set;}} [DwarfMapper] public partial class M{public partial IQueryable<T> P(IQueryable<S> q);}",
        };
        foreach (var src in cases)
        {
            var (_, gen) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        }
    }

    // ── Emitter uses inline exprs, not raw member assign ──
    [Fact]
    public void Projection_emitter_uses_ProjectionMembers_inline_expr_not_raw_member()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Inner { public int X { get; set; } }
            public class InnerDto { public int X { get; set; } }
            public class Outer { public Inner? Inner { get; set; } }
            public class OuterDto { public InnerDto? Inner { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("new global::D.InnerDto", gen, StringComparison.Ordinal);
        // Must not be a raw assignment of the object reference
        Assert.DoesNotContain("Inner = __s.Inner,", gen, StringComparison.Ordinal);
    }

    // ── MF2: nested positional record in projection → ctor projection, not member-init ─

    // MF2-a: exact repro — nested record with no parameterless ctor must use ctor projection
    [Fact]
    public void Projection_nested_positional_record_compiles_via_ctor_not_member_init()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Cust { public string Name { get; set; } = ""; }
            public record CustRec(string Name);
            public class Order { public Cust Customer { get; set; } = new(); }
            public class OrderDto { public CustRec Customer { get; set; } = null!; }
            [DwarfMapper] public partial class M { public partial IQueryable<OrderDto> P(IQueryable<Order> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
        // Must emit constructor call, NOT member-init (which would fail without parameterless ctor)
        Assert.Contains("new global::D.CustRec(", gen, StringComparison.Ordinal);
        Assert.DoesNotContain("new global::D.CustRec {", gen, StringComparison.Ordinal);
        Assert.DoesNotContain("__DwarfMap_", gen, StringComparison.Ordinal);
    }

    // MF2-b: nested settable class still uses member-init (regression guard)
    [Fact]
    public void Projection_nested_settable_class_still_uses_member_init()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Inner { public int X { get; set; } }
            public class InnerDto { public int X { get; set; } }
            public class Outer { public Inner Inner { get; set; } = new(); }
            public class OuterDto { public InnerDto Inner { get; set; } = null!; }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> P(IQueryable<Outer> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
        // Settable class → member-init form
        Assert.Contains("new global::D.InnerDto {", gen, StringComparison.Ordinal);
        Assert.DoesNotContain("__DwarfMap_", gen, StringComparison.Ordinal);
    }

    // MF2-c: 2-level nested record projection compiles
    [Fact]
    public void Projection_2level_nested_record_compiles_cleanly()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class City { public string Name { get; set; } = ""; }
            public record CityRec(string Name);
            public class Address { public City City { get; set; } = new(); }
            public record AddressRec(CityRec City);
            public class Person { public Address Addr { get; set; } = new(); }
            public class PersonDto { public AddressRec Addr { get; set; } = null!; }
            [DwarfMapper] public partial class M { public partial IQueryable<PersonDto> P(IQueryable<Person> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
        Assert.Contains("new global::D.CityRec(", gen, StringComparison.Ordinal);
        Assert.Contains("new global::D.AddressRec(", gen, StringComparison.Ordinal);
        Assert.DoesNotContain("__DwarfMap_", gen, StringComparison.Ordinal);
    }

    // ── VF4: nullable collection SOURCE member in projection → null guard on the collection ─

    [Fact]
    public void Projection_nullable_collection_member_gets_source_null_guard()
    {
        // If __s.Items is null, Enumerable.Select(null!, ...) throws at query evaluation time.
        // The source expression must be guarded: __s.Items == null ? null : Enumerable.Select(...)
        const string s = """
            using DwarfMapper; using System.Linq; using System.Collections.Generic;
            namespace D;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            public class Outer { public List<Item>? Items { get; set; } }
            public class OuterDto { public List<ItemDto>? Items { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> P(IQueryable<Outer> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
        // The COLLECTION source expression (__s.Items) must be null-guarded before Select call.
        // Pattern: "__s.Items == null ? null : Enumerable..."
        Assert.Contains("__s.Items == null ? null :", gen, StringComparison.Ordinal);
    }

    // ── VF5: projection method with applicable hook silently drops it → DWARF028 ─

    [Fact]
    public void Projection_method_with_applicable_hook_emits_DWARF028_not_silent()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Src { public int Id { get; set; } }
            public class Dst { public int Id { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                [BeforeMap] private static void Before(Src s) { }
                public partial IQueryable<Dst> Project(IQueryable<Src> q);
            }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        // Hook on a projection method is not supported → DWARF028
        var d028 = diag.Where(d => d.Id == "DWARF028").ToList();
        Assert.NotEmpty(d028);
        Assert.Contains("hook", d028[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }
}
