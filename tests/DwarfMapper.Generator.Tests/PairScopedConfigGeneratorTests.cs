// SPDX-License-Identifier: GPL-2.0-only

// Scan4 coverage: exercises MapPropertyAttribute<,> and MapIgnoreAttribute<> (the pair-scoped generic variants).
using System.Linq;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Tests for pair-scoped, class-level member config — <c>[MapProperty&lt;S,T&gt;]</c> / <c>[MapIgnore&lt;T&gt;]</c> —
/// which lets a <c>[GenerateMap]</c> pair (and nested pairs) be configured with no <c>partial</c> method.
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
        Assert.Contains("FullName", gen, System.StringComparison.Ordinal);
        Assert.Contains("src.Name", gen, System.StringComparison.Ordinal);
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
        Assert.Contains("FullName", gen, System.StringComparison.Ordinal);
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
}
