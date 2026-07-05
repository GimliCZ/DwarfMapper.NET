// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Exhaustive projection translatability matrix.
///     SAFE constructs: assert compile-clean (no DWARF028) and generated code contains
///     the expected inline pattern (no __DwarfMap_ helper).
///     UNSAFE constructs: assert DWARF028 is raised.
///     Extends the 3 tests in ProjectionTests.cs — focused on the Part D constructs not yet covered.
/// </summary>
public class ProjectionMatrixSafeTests
{
    // ── SAFE: nested object projection (2 levels) ─────────────────────────────

    [Fact]
    public void Safe_nested_object_projection_2levels_compiles_cleanly()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           namespace Demo;
                           public class City  { public string Name { get; set; } = ""; }
                           public class CityDto { public string Name { get; set; } = ""; }
                           public class Person { public City Home { get; set; } = new(); }
                           public class PersonDto { public CityDto Home { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M { public partial IQueryable<PersonDto> P(IQueryable<Person> q); }
                           """;
        var (diags, gen) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Id == "DWARF028");
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.DoesNotContain("__DwarfMap_", gen, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.CityDto", gen, StringComparison.Ordinal);
    }

    // ── SAFE: nested object projection (3 levels) ─────────────────────────────

    [Fact]
    public void Safe_nested_object_projection_3levels_compiles_cleanly()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           namespace Demo;
                           public class Zip { public string Code { get; set; } = ""; }
                           public class ZipDto { public string Code { get; set; } = ""; }
                           public class Addr { public Zip Location { get; set; } = new(); }
                           public class AddrDto { public ZipDto Location { get; set; } = new(); }
                           public class Person { public Addr Home { get; set; } = new(); }
                           public class PersonDto { public AddrDto Home { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M { public partial IQueryable<PersonDto> P(IQueryable<Person> q); }
                           """;
        var (diags, gen) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Id == "DWARF028");
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.DoesNotContain("__DwarfMap_", gen, StringComparison.Ordinal);
    }

    // ── SAFE: collection projection → inline Select().ToList() ───────────────

    [Fact]
    public void Safe_collection_projection_produces_select_tolist()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class Line { public int Qty { get; set; } }
                           public class LineDto { public int Qty { get; set; } }
                           public class Order { public List<Line> Lines { get; set; } = new(); }
                           public class OrderDto { public List<LineDto> Lines { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M { public partial IQueryable<OrderDto> P(IQueryable<Order> q); }
                           """;
        var (diags, gen) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Id == "DWARF028");
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.DoesNotContain("__DwarfMap_", gen, StringComparison.Ordinal);
        // The generator emits fully-qualified Enumerable.Select + Enumerable.ToList
        // (not extension-method syntax) to avoid requiring 'using System.Linq' in the generated file.
        Assert.Contains("Enumerable.Select<", gen, StringComparison.Ordinal);
        Assert.Contains("Enumerable.ToList<", gen, StringComparison.Ordinal);
    }

    // ── SAFE: array projection → inline Select().ToArray() ───────────────────

    [Fact]
    public void Safe_array_projection_produces_select_toarray()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           namespace Demo;
                           public class Tag { public string Value { get; set; } = ""; }
                           public class TagDto { public string Value { get; set; } = ""; }
                           public class Doc { public Tag[] Tags { get; set; } = System.Array.Empty<Tag>(); }
                           public class DocDto { public TagDto[] Tags { get; set; } = System.Array.Empty<TagDto>(); }
                           [DwarfMapper]
                           public partial class M { public partial IQueryable<DocDto> P(IQueryable<Doc> q); }
                           """;
        var (diags, gen) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Id == "DWARF028");
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.DoesNotContain("__DwarfMap_", gen, StringComparison.Ordinal);
    }

    // ── SAFE: widening numeric cast in projection ─────────────────────────────

    [Fact]
    public void Safe_widening_int_to_long_in_projection_compiles()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           namespace Demo;
                           public class S { public int V { get; set; } }
                           public class D { public long V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial IQueryable<D> P(IQueryable<S> q); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Id == "DWARF028");
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ── SAFE: enum by-value in projection (requires EnumStrategy.ByValue) ────

    [Fact]
    public void Safe_enum_byvalue_projection_compiles()
    {
        // Two different enum types with matching integer values require EnumStrategy.ByValue
        // so the generator emits a direct cast (TgtEnum)src — SQL-translatable.
        // Without ByValue the default ByName mapping uses a switch which is NOT translatable
        // (DWARF028). See: ResolveProjectionExpr enum-by-value branch in MapperExtractor.
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           namespace Demo;
                           public enum Status { Active, Inactive }
                           public enum StatusDto { Active, Inactive }
                           public class S { public Status St { get; set; } }
                           public class D { public StatusDto St { get; set; } }
                           [DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]
                           public partial class M { public partial IQueryable<D> P(IQueryable<S> q); }
                           """;
        var (diags, gen) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Id == "DWARF028");
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // Must emit a direct cast expression, not a switch helper.
        Assert.DoesNotContain("__DwarfMap_", gen, StringComparison.Ordinal);
        Assert.Contains("(global::Demo.StatusDto)", gen, StringComparison.Ordinal);
    }
}

public class ProjectionMatrixUnsafeTests
{
    // ── UNSAFE: narrowing numeric in projection → DWARF028 ───────────────────

    [Fact]
    public void Unsafe_narrowing_long_to_int_reports_DWARF028()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           namespace Demo;
                           public class S { public long V { get; set; } }
                           public class D { public int V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial IQueryable<D> P(IQueryable<S> q); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diags, d => d.Id == "DWARF028");
    }

    // ── UNSAFE: string parse (IParsable) in projection → DWARF028 ────────────

    [Fact]
    public void Unsafe_string_to_int_parse_reports_DWARF028()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           namespace Demo;
                           public class S { public string V { get; set; } = ""; }
                           public class D { public int V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial IQueryable<D> P(IQueryable<S> q); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diags, d => d.Id == "DWARF028");
    }

    // ── UNSAFE: int→Guid parse in projection → DWARF028 ─────────────────────

    [Fact]
    public void Unsafe_int_to_guid_reports_DWARF028()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           namespace Demo;
                           public class S { public string V { get; set; } = ""; }
                           public class D { public System.Guid V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial IQueryable<D> P(IQueryable<S> q); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diags, d => d.Id == "DWARF028");
    }

    // ── UNSAFE: HashSet target in projection → DWARF028 ─────────────────────

    [Fact]
    public void Unsafe_hashset_target_in_projection_reports_DWARF028()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class S { public List<int> Tags { get; set; } = new(); }
                           public class D { public HashSet<int> Tags { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M { public partial IQueryable<D> P(IQueryable<S> q); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diags, d => d.Id == "DWARF028");
    }

    // ── UNSAFE: ImmutableArray target in projection → DWARF028 ───────────────

    [Fact]
    public void Unsafe_immutable_collection_target_in_projection_reports_DWARF028()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           using System.Collections.Generic;
                           using System.Collections.Immutable;
                           namespace Demo;
                           public class S { public List<int> Tags { get; set; } = new(); }
                           public class D { public ImmutableArray<int> Tags { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial IQueryable<D> P(IQueryable<S> q); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diags, d => d.Id == "DWARF028");
    }

    // ── UNSAFE: Dictionary target in projection → DWARF028 ───────────────────

    [Fact]
    public void Unsafe_dictionary_target_in_projection_reports_DWARF028()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class S { public Dictionary<string,int> D { get; set; } = new(); }
                           public class D { public Dictionary<string,int> D { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M { public partial IQueryable<D> P(IQueryable<S> q); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diags, d => d.Id == "DWARF028");
    }

    // ── UNSAFE: ReferenceHandling=Preserve on projection → DWARF028 ──────────

    [Fact]
    public void Unsafe_preserve_reference_handling_on_projection_reports_DWARF028()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           namespace Demo;
                           public class S { public int V { get; set; } }
                           public class D { public int V { get; set; } }
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial IQueryable<D> P(IQueryable<S> q); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diags, d => d.Id == "DWARF028");
    }

    // ── UNSAFE: MapProperty(Use=) custom method in projection → DWARF028 ─────

    [Fact]
    public void Unsafe_use_custom_method_in_projection_reports_DWARF028()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           namespace Demo;
                           public class S { public int V { get; set; } }
                           public class D { public string V { get; set; } = ""; }
                           [DwarfMapper]
                           public partial class M
                           {
                               [MapProperty("V", "V", Use = nameof(ToStr))]
                               public partial IQueryable<D> P(IQueryable<S> q);
                               private static string ToStr(int v) => v.ToString();
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diags, d => d.Id == "DWARF028");
    }
}

/// <summary>
///     DWARF030 diagnostic: cyclic constructor parameter (cannot register-before-populate for
///     immutable targets participating in a reference cycle).
/// </summary>
public class Dwarf030CyclicCtorTests
{
    [Fact]
    public void Cyclic_ctor_param_reports_DWARF030()
    {
        // A record with a required ctor param pointing back to itself forms a cycle
        // that the generator cannot satisfy: register-before-populate fails because
        // the instance doesn't exist before ctor args are resolved.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Src { public Src? Self { get; set; } public int V { get; set; } }
                           // Immutable record: ctor arg 'self' is the back-edge → DWARF030
                           public record Dst(Dst? Self, int V);
                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                           public partial class M { public partial Dst Map(Src s); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diags, d => d.Id == "DWARF030");
    }
}
