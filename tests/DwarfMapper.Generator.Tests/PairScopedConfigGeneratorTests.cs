// SPDX-License-Identifier: GPL-2.0-only

// Scan4 coverage: exercises MapPropertyAttribute<,>, MapIgnoreAttribute<>, and MapValueAttribute<> (the pair-scoped generic variants).

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Tests for pair-scoped, class-level member config — <c>[MapProperty&lt;S,T&gt;]</c> / <c>[MapIgnore&lt;T&gt;]</c> —
///     which lets a <c>[GenerateMap]</c> pair (and nested pairs) be configured with no <c>partial</c> method.
/// </summary>
public sealed class PairScopedConfigGeneratorTests
{
    [Fact]
    public void PairScoped_rename_applies_to_a_GenerateMap_pair()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Person { public string Name { get; set; } }
                         public class PersonDto { public string FullName { get; set; } }
                         [DwarfMapper]
                         [GenerateMap<Person, PersonDto>]
                         [MapProperty<Person, PersonDto>("Name", "FullName")]
                         public partial class M { }
                         """;

        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(s);
        Assert.Empty(errors); // no DWARF001 for FullName — the rename mapped it

        var (_, gen) = GeneratorTestHarness.Run(s);
        Assert.Contains("FullName", gen, StringComparison.Ordinal);
        Assert.Contains("src.Name", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void PairScoped_rename_applies_to_a_nested_collection_element()
    {
        // The Moria scenario: Place -> PlaceDto has List<Person> -> List<PersonDto>, and Person.Name must become
        // PersonDto.FullName. The nested Person->PersonDto pair is auto-synthesized, yet the pair-scoped rename
        // still applies — with zero partial methods.
        const string s = """
                         using System.Collections.Generic;
                         using DwarfMapper;
                         namespace Demo;
                         public class Person { public string Name { get; set; } }
                         public class PersonDto { public string FullName { get; set; } }
                         public class Place { public string Name { get; set; } public List<Person> People { get; set; } }
                         public class PlaceDto { public string Name { get; set; } public List<PersonDto> People { get; set; } }
                         [DwarfMapper]
                         [GenerateMap<Place, PlaceDto>]
                         [MapProperty<Person, PersonDto>("Name", "FullName")]
                         public partial class M { }
                         """;

        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(s);
        Assert.Empty(errors); // proves the nested synthesized PersonDto mapper applied the rename (no DWARF001)

        var (_, gen) = GeneratorTestHarness.Run(s);
        Assert.Contains("FullName", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void PairScoped_ignore_suppresses_DWARF001_on_a_GenerateMap_pair()
    {
        const string mapped = """
                              using DwarfMapper;
                              namespace Demo;
                              public class A { public int Id { get; set; } }
                              public class B { public int Id { get; set; } public string Extra { get; set; } }
                              [DwarfMapper]
                              [GenerateMap<A, B>]
                              [MapIgnore<B>("Extra")]
                              public partial class M { }
                              """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(mapped));

        // Without the pair-scoped ignore, B.Extra is unmapped → DWARF001.
        const string unmapped = """
                                using DwarfMapper;
                                namespace Demo;
                                public class A { public int Id { get; set; } }
                                public class B { public int Id { get; set; } public string Extra { get; set; } }
                                [DwarfMapper]
                                [GenerateMap<A, B>]
                                public partial class M { }
                                """;
        var (diags, _) = GeneratorTestHarness.Run(unmapped);
        Assert.Contains(diags, d => d.Id == "DWARF001");
    }

    [Fact]
    public void MapConstructor_factory_constructs_and_compiles_clean()
    {
        // [MapConstructor<S,T>] delegates construction to a factory (ConstructUsing); the get-only
        // Parsed member is populated only by the ctor, settable Cost afterward — no diagnostics.
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public string Format { get; set; } = ""; public int Cost { get; set; } }
                         public class Dst {
                             public Dst() { }
                             public Dst(string format) { Format = format; Parsed = format.ToUpperInvariant(); }
                             public string Format { get; } = "";
                             public string Parsed { get; } = "";
                             public int Cost { get; set; }
                         }
                         [DwarfMapper]
                         [GenerateMap<Src, Dst>]
                         [MapConstructor<Src, Dst>(nameof(Make))]
                         public partial class M {
                             private static Dst Make(Src s) => new(s.Format);
                         }
                         """;

        var (diags, _) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void MapConstructor_with_unknown_factory_reports_DWARF059()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int X { get; set; } }
                         public class Dst { public Dst(int x) { X = x; } public int X { get; } }
                         [DwarfMapper]
                         [GenerateMap<Src, Dst>]
                         [MapConstructor<Src, Dst>("DoesNotExist")]
                         public partial class M { }
                         """;

        var (diags, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diags, d => d.Id == "DWARF059");
    }

    [Fact]
    public void PairScoped_attribute_matching_no_pair_reports_DWARF056()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class A { public int Id { get; set; } }
                         public class B { public int Id { get; set; } }
                         public class X { public int P { get; set; } }
                         public class Y { public int Q { get; set; } }
                         [DwarfMapper]
                         [GenerateMap<A, B>]
                         [MapProperty<X, Y>("P", "Q")]
                         public partial class M { }
                         """;

        var (diags, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diags, d => d.Id == "DWARF056");
    }

    [Fact]
    public void PairScoped_attribute_consumed_by_a_declared_partial_method_does_not_report_DWARF056()
    {
        // The pair-scoped rename matches a DECLARED partial method's pair — it must apply there and stay quiet,
        // not advise "add [GenerateMap]" (which would create a colliding second map).
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Person { public string Name { get; set; } }
                         public class PersonDto { public string FullName { get; set; } }
                         [DwarfMapper]
                         [MapProperty<Person, PersonDto>("Name", "FullName")]
                         public partial class M
                         {
                             public partial PersonDto ToDto(Person p);
                         }
                         """;
        var (diags, _) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diags, d => d.Id == "DWARF056");
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s)); // rename applied → FullName mapped
    }

    [Fact]
    public void PairScoped_attribute_matching_only_a_nested_pair_does_not_report_DWARF056()
    {
        const string s = """
                         using System.Collections.Generic;
                         using DwarfMapper;
                         namespace Demo;
                         public class Person { public string Name { get; set; } }
                         public class PersonDto { public string FullName { get; set; } }
                         public class Place { public List<Person> People { get; set; } }
                         public class PlaceDto { public List<PersonDto> People { get; set; } }
                         [DwarfMapper]
                         [GenerateMap<Place, PlaceDto>]
                         [MapProperty<Person, PersonDto>("Name", "FullName")]
                         public partial class M { }
                         """;
        var (diags, _) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diags, d => d.Id == "DWARF056"); // consumed by the synthesized nested pair
    }

    [Fact]
    public void PairScoped_value_completes_a_source_less_member_with_no_method()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Person { public string Name { get; set; } }
                         public class PersonDto { public string Name { get; set; } public string Source { get; set; } }
                         [DwarfMapper]
                         [GenerateMap<Person, PersonDto>]
                         [MapValue<PersonDto>("Source", "api")]
                         public partial class M { }
                         """;

        Assert.Empty(GeneratorTestHarness
            .RunAndGetCompilationErrors(s)); // Source has no source member, but MapValue completes it
        var gen = GeneratorTestHarness.RunAndGetSource(s, "M.g.cs");
        Assert.Contains("Source = ", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void PairScoped_value_matching_no_pair_reports_DWARF056()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class A { public int Id { get; set; } }
                         public class B { public int Id { get; set; } }
                         public class Z { public string W { get; set; } }
                         [DwarfMapper]
                         [GenerateMap<A, B>]
                         [MapValue<Z>("W", "x")]
                         public partial class M { }
                         """;

        var (diags, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diags, d => d.Id == "DWARF056");
    }
}
