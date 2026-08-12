// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Plan 19D — TDD tests for the recursive, translatability-classifying projection resolver.
///     SAFE: nested object inline, collection inline, ctor inline, widening numeric, enum by-value.
///     UNSAFE: each → DWARF028 with appropriate reason; NO __DwarfMap_ helper calls in output.
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
        GeneratorAssert.EmitsCompilableCode(s);
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
        GeneratorAssert.EmitsCompilableCode(s);
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
        GeneratorAssert.EmitsCompilableCode(s);
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
        GeneratorAssert.EmitsCompilableCode(s);
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
        GeneratorAssert.EmitsCompilableCode(s);
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
        GeneratorAssert.EmitsCompilableCode(s);
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
        GeneratorAssert.EmitsCompilableCode(s);
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
        GeneratorAssert.EmitsCompilableCode(s);
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
        GeneratorAssert.EmitsCompilableCode(s);
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
        GeneratorAssert.EmitsCompilableCode(s);
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
        GeneratorAssert.EmitsCompilableCode(s);
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
        GeneratorAssert.EmitsCompilableCode(s);
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
        Assert.Contains("narrowing", d028[0].GetMessage(CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("parse", d028[0].GetMessage(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("by-name", d028[0].GetMessage(CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("converter", d028[0].GetMessage(CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("narrowing", d028[0].GetMessage(CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("translatable", d028[0].GetMessage(CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("reference handling", d028[0].GetMessage(CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);
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
            "using DwarfMapper; using System.Linq; namespace D; public class I{public int X{get;set;}} public class ID{public int X{get;set;}} public class S{public I? N{get;set;}} public class T{public ID? N{get;set;}} [DwarfMapper] public partial class M{public partial IQueryable<T> P(IQueryable<S> q);}"
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
        var gen = GeneratorAssert.CompilesClean(s);
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
        var gen = GeneratorAssert.CompilesClean(s);
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
        var gen = GeneratorAssert.CompilesClean(s);
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
        var gen = GeneratorAssert.CompilesClean(s);
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
        Assert.Contains("hook", d028[0].GetMessage(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    // ── Constructor targets under projection (R18-32) ────────────────────────────────────────────────────
    // Projection used to bind constructor parameters by NAME alone and to RETURN the moment it had a
    // constructor call. FOUR defects fell out of that, all found while fixing R18-31 and all fixed together
    // because they are one structural confusion: the constructor route and the member route could not
    // co-exist, so whichever ran first won and the other was discarded. The fourth — a camelCase parameter
    // refusing the PascalCase member .Map binds it to — was found by the test below it, not by reading.

    [Fact]
    public void Projection_binds_a_ctor_param_from_a_dotted_explicit_map()
    {
        // R18-32 proper. `ForCtorParam("Start", o => o.MapFrom(s => s.Window.Start))` translated to this and
        // was refused with a DWARF024 recommending the very attribute the author had written.
        const string s = """
                         using DwarfMapper; using System.Linq;
                         namespace D;
                         public sealed record Window(int Start, int End);
                         public class Src { public Window Window { get; set; } = new(0, 0); }
                         public sealed record Dst(int Start);
                         [DwarfMapper] public partial class M
                         {
                             [MapProperty("Window.Start", "Start")]
                             public partial IQueryable<Dst> Prj(IQueryable<Src> q);
                         }
                         """;
        var gen = GeneratorAssert.CompilesClean(s);

        Assert.Contains("new global::D.Dst(__s.Window.Start)", gen, StringComparison.Ordinal);
        // Expression trees forbid named arguments (CS0853), so the argument must stay positional.
        Assert.DoesNotContain("Start:", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_binds_a_ctor_param_of_a_type_with_no_matching_property()
    {
        // The half that could not even be diagnosed as a ctor problem: with no writable member named `code`,
        // the explicit map was refused as an UNKNOWN TARGET (DWARF014) before the constructor was consulted.
        const string s = """
                         using DwarfMapper; using System.Linq;
                         namespace D;
                         public class Src { public int Id { get; set; } public int LegacyCode { get; set; } }
                         public class Dst
                         {
                             public Dst(int id, int code) { Id = id; Code = code; }
                             public int Id { get; }
                             public int Code { get; }
                         }
                         [DwarfMapper] public partial class M
                         {
                             [MapProperty(nameof(Src.LegacyCode), "code")]
                             public partial IQueryable<Dst> Prj(IQueryable<Src> q);
                         }
                         """;
        var gen = GeneratorAssert.CompilesClean(s);

        Assert.Contains("new global::D.Dst(__s.Id, __s.LegacyCode)", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_assigns_an_init_member_the_constructor_did_NOT_take()
    {
        // The silent one, and the reason this went in rather than being filed: the resolver returned as soon
        // as it had a constructor call, so `Extra` — assigned correctly through .Map — was dropped from the
        // projection with no diagnostic on either side. Same mapper, two endpoints, different data.
        const string s = """
                         using DwarfMapper; using System.Linq;
                         namespace D;
                         public class Src { public int Start { get; set; } public int Extra { get; set; } }
                         public sealed record Dst(int Start) { public int Extra { get; init; } }
                         [DwarfMapper] public partial class M
                         {
                             public partial IQueryable<Dst> Prj(IQueryable<Src> q);
                         }
                         """;
        var gen = GeneratorAssert.CompilesClean(s);

        Assert.Contains("new global::D.Dst(__s.Start)", gen, StringComparison.Ordinal);
        Assert.Contains("Extra = __s.Extra", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_does_not_assign_a_positional_record_member_beside_its_own_argument()
    {
        // A positional parameter also surfaces as an init PROPERTY, so a rename onto it matched both routes:
        // the constructor took the by-name source and the explicit map was emitted as an initializer beside
        // it, producing `new Dst { Start = __s.Other,  = new Dst(__s.Start) }` — an empty left-hand side, no
        // diagnostic, and a CS error in generated code. CompilesClean is the assertion that matters here.
        const string s = """
                         using DwarfMapper; using System.Linq;
                         namespace D;
                         public class Src { public int Start { get; set; } public int Other { get; set; } }
                         public sealed record Dst(int Start);
                         [DwarfMapper] public partial class M
                         {
                             [MapProperty("Other", "Start")]
                             public partial IQueryable<Dst> Prj(IQueryable<Src> q);
                         }
                         """;
        var gen = GeneratorAssert.CompilesClean(s);

        Assert.Contains("new global::D.Dst(__s.Other)", gen, StringComparison.Ordinal);
        Assert.DoesNotContain("Start =", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Use_converter_into_a_ctor_param_reports_DWARF028_once_and_no_DWARF024()
    {
        // A converter is still untranslatable — that answer does not change. What changes is that the
        // refusal is not followed by a DWARF024 recommending `[MapProperty(src, "<paramName>")]`, which the
        // author has already written. One defect, one diagnostic.
        const string s = """
                         using DwarfMapper; using System.Linq;
                         namespace D;
                         public class Src { public int Raw { get; set; } }
                         public sealed record Dst(int Start);
                         [DwarfMapper] public partial class M
                         {
                             [MapProperty(nameof(Src.Raw), "Start", Use = nameof(Twice))]
                             public partial IQueryable<Dst> Prj(IQueryable<Src> q);
                             private static int Twice(int v) => v * 2;
                         }
                         """;
        var (diag, _) = GeneratorTestHarness.Run(s);

        Assert.Single(diag.Where(d => d.Id == "DWARF028"));
        Assert.DoesNotContain(diag, d => d.Id == "DWARF024");
    }

    [Fact]
    public void A_bad_path_segment_into_a_ctor_param_reports_the_path_and_no_DWARF024()
    {
        const string s = """
                         using DwarfMapper; using System.Linq;
                         namespace D;
                         public sealed record Window(int Start, int End);
                         public class Src { public Window Window { get; set; } = new(0, 0); }
                         public sealed record Dst(int Start);
                         [DwarfMapper] public partial class M
                         {
                             [MapProperty("Window.Middle", "Start")]
                             public partial IQueryable<Dst> Prj(IQueryable<Src> q);
                         }
                         """;
        var (diag, _) = GeneratorTestHarness.Run(s);

        Assert.Contains(diag, d => d.Id == "DWARF009");
        Assert.DoesNotContain(diag, d => d.Id == "DWARF024");
    }

    [Fact]
    public void A_map_naming_a_ctor_param_of_a_member_init_target_is_still_an_unknown_target()
    {
        // The negative half: this target HAS a public parameterless constructor, so projection builds it by
        // member-init and the constructor is never called. A map naming one of its parameters has nothing to
        // bind to and must stay DWARF014 — the parameter lookup is gated on the route actually being taken.
        const string s = """
                         using DwarfMapper; using System.Linq;
                         namespace D;
                         public class Src { public int Id { get; set; } }
                         public class Dst
                         {
                             public Dst() { }
                             public Dst(int seed) { Id = seed; }
                             public int Id { get; set; }
                         }
                         [DwarfMapper] public partial class M
                         {
                             [MapProperty(nameof(Src.Id), "seed")]
                             public partial IQueryable<Dst> Prj(IQueryable<Src> q);
                         }
                         """;
        var (diag, _) = GeneratorTestHarness.Run(s);

        Assert.Contains(diag, d => d.Id == "DWARF008");
    }

    [Fact]
    public void A_NESTED_ctor_projection_also_assigns_what_the_constructor_did_not_take()
    {
        // The same silent drop one level down, where it is harder to notice: the outer object initializer
        // looks complete, and the member that vanished is inside the nested `new`.
        const string s = """
                         using DwarfMapper; using System.Linq;
                         namespace D;
                         public class InnerSrc { public int Start { get; set; } public int Extra { get; set; } }
                         public class Src { public InnerSrc Inner { get; set; } = new(); }
                         public sealed record InnerDto(int Start) { public int Extra { get; init; } }
                         public class Dst { public InnerDto Inner { get; set; } = new(0); }
                         [DwarfMapper] public partial class M
                         {
                             public partial IQueryable<Dst> Prj(IQueryable<Src> q);
                         }
                         """;
        var gen = GeneratorAssert.CompilesClean(s);

        Assert.Contains("new global::D.InnerDto(__s.Inner.Start) { Extra = __s.Inner.Extra }", gen,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_binds_a_camelCase_ctor_param_to_a_PascalCase_source_member()
    {
        // A fifth member of the same divergence family, found by the test above rather than by reading:
        // ResolveConstructorArguments matches ctor parameters case-insensitively ALWAYS and says why — the
        // camelCase-parameter / PascalCase-member shape is the dominant one. Projection matched them under
        // the class comparer, Ordinal by default, so this pair bound through .Map and reported DWARF024
        // through .Project.
        const string s = """
                         using DwarfMapper; using System.Linq;
                         namespace D;
                         public class Src { public int Id { get; set; } public string Name { get; set; } = ""; }
                         public class Dst
                         {
                             public Dst(int id, string name) { Id = id; Name = name; }
                             public int Id { get; }
                             public string Name { get; }
                         }
                         [DwarfMapper] public partial class M
                         {
                             public partial Dst Map(Src s);
                             public partial IQueryable<Dst> Prj(IQueryable<Src> q);
                         }
                         """;
        var gen = GeneratorAssert.CompilesClean(s);

        Assert.Contains("new global::D.Dst(__s.Id, __s.Name)", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void A_SETTABLE_member_matching_a_ctor_param_only_by_case_is_not_assigned_twice()
    {
        // The other side of that rule. `id` the parameter and `Id` the property are the same destination, so
        // the property must count as already fed — otherwise the initializer assigns it a second time on top
        // of the constructor that just took it. The class model uses a case-insensitive `consumedParams` for
        // exactly this; the two sets have to agree.
        const string s = """
                         using DwarfMapper; using System.Linq;
                         namespace D;
                         public class Src { public int Id { get; set; } }
                         public class Dst
                         {
                             public Dst(int id) { Id = id; }
                             public int Id { get; set; }
                         }
                         [DwarfMapper] public partial class M
                         {
                             public partial IQueryable<Dst> Prj(IQueryable<Src> q);
                         }
                         """;
        var gen = GeneratorAssert.CompilesClean(s);

        Assert.Contains("new global::D.Dst(__s.Id)", gen, StringComparison.Ordinal);
        Assert.DoesNotContain("Id = __s.Id", gen, StringComparison.Ordinal);
    }
}
