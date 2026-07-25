// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Tests for co-located mapping: a class that carries <c>[GenerateMap&lt;&gt;]</c> but is NOT a
///     <c>[DwarfMapper]</c> (and need not be <c>partial</c>) gets its mapping emitted into a SEPARATE generated
///     <c>&lt;Host&gt;Mapper</c> type, consumed via the generated extension / DI.
/// </summary>
public sealed class CoLocatedMappingGeneratorTests
{
    [Fact]
    public void Plain_class_with_GenerateMap_emits_a_separate_mapper_and_compiles()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Person { public string Name { get; set; } public int Age { get; set; } }
                         [GenerateMap<Person, PersonDto>]
                         [MapProperty<Person, PersonDto>("Name", "FullName")]
                         public sealed class PersonDto { public string FullName { get; set; } public int Age { get; set; } }
                         """;

        // The whole compilation (host + generated PersonDtoMapper + facade) builds clean — no DWARF001, etc.
        GeneratorAssert.EmitsCompilableCode(s);

        // The mapping is emitted into a SEPARATE PersonDtoMapper type, not into PersonDto.
        var mapper = GeneratorTestHarness.RunAndGetSource(s, "PersonDtoMapper.g.cs");
        Assert.Contains("class PersonDtoMapper", mapper, StringComparison.Ordinal);
        Assert.Contains("Map(global::Demo.Person", mapper, StringComparison.Ordinal);
        Assert.Contains("FullName", mapper, StringComparison.Ordinal);
        Assert.Contains("src.Name", mapper, StringComparison.Ordinal);

        // The convenience extension is generated for the separate mapper.
        var facade = GeneratorTestHarness.RunAndGetSource(s, "DwarfMapper.Extensions.g.cs");
        Assert.Contains("ToPersonDto(this global::Demo.Person", facade, StringComparison.Ordinal);
    }

    [Fact]
    public void Host_needs_neither_partial_nor_DwarfMapper()
    {
        // No `partial`, no [DwarfMapper] on the DTO — only declarative attributes.
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Person { public string Name { get; set; } }
                         [GenerateMap<Person, PersonDto>]
                         [MapProperty<Person, PersonDto>("Name", "FullName")]
                         public sealed class PersonDto { public string FullName { get; set; } }
                         """;
        GeneratorAssert.EmitsCompilableCode(s);
    }

    [Fact]
    public void Dual_path_two_GenerateMaps_on_one_host_emit_both_directions()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Person { public string FullName { get; set; } }
                         [GenerateMap<Person, PersonDto>]
                         [GenerateMap<PersonDto, Person>]
                         public sealed class PersonDto { public string FullName { get; set; } }
                         """;
        GeneratorAssert.EmitsCompilableCode(s);

        var mapper = GeneratorTestHarness.RunAndGetSource(s, "PersonDtoMapper.g.cs");
        Assert.Contains("Map(global::Demo.Person", mapper, StringComparison.Ordinal);
        Assert.Contains("Map(global::Demo.PersonDto", mapper, StringComparison.Ordinal);
    }

    [Fact]
    public void A_DwarfMapper_class_with_GenerateMap_is_not_double_processed()
    {
        // M is the [DwarfMapper] host; the co-located pipeline must skip it (no separate MMapper).
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Person { public string Name { get; set; } }
                         public class PersonDto { public string Name { get; set; } }
                         [DwarfMapper]
                         [GenerateMap<Person, PersonDto>]
                         public partial class M { }
                         """;

        GeneratorAssert.EmitsCompilableCode(s);
        var extra = GeneratorTestHarness.RunAndGetSource(s, "MMapper.g.cs");
        Assert.True(string.IsNullOrEmpty(extra), "co-located pipeline must skip a [DwarfMapper] class");
    }

    [Fact]
    public void Generated_mapper_name_collision_reports_DWARF057_not_a_raw_error()
    {
        // The user already has a type named PersonDtoMapper; the co-located host PersonDto would generate one too.
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Person { public string Name { get; set; } }
                         public sealed class PersonDtoMapper { }
                         [GenerateMap<Person, PersonDto>]
                         public sealed class PersonDto { public string Name { get; set; } }
                         """;
        var (diags, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diags, d => d.Id == "DWARF057");
        // Nothing is emitted for the colliding mapper (blocking error), so there is no duplicate-type CS error.
        Assert.True(string.IsNullOrEmpty(GeneratorTestHarness.RunAndGetSource(s, "PersonDtoMapper.g.cs")));
    }

    [Fact]
    public void Generic_co_located_host_reports_DWARF054_not_silence()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Person { public string Name { get; set; } }
                         public class PersonDto { public string Name { get; set; } }
                         [GenerateMap<Person, PersonDto>]
                         public sealed class Box<T> { public T Value { get; set; } }
                         """;
        var (diags, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diags, d => d.Id == "DWARF054");
    }

    [Fact]
    public void Incomplete_co_located_mapping_still_reports_DWARF001()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Person { public string Name { get; set; } }
                         [GenerateMap<Person, PersonDto>]
                         public sealed class PersonDto { public string Name { get; set; } public int Extra { get; set; } }
                         """;
        var (diags, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diags, d => d.Id == "DWARF001"); // Extra has no source — completeness still enforced
    }

    [Fact]
    public void Co_located_GenerateMap_where_neither_arg_is_the_host_emits_a_target_named_extension()
    {
        // A legitimate "declaration site" pattern: the host C just hosts a [GenerateMap<A,B>]; the extension is
        // named after the TARGET (ToB), not the host. (Documents the contract — not a misuse.)
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class A { public int V { get; set; } }
                         public class B { public int V { get; set; } }
                         [GenerateMap<A, B>]
                         public sealed class C { public int W { get; set; } }
                         """;

        GeneratorAssert.EmitsCompilableCode(s);
        Assert.Contains("Map(global::Demo.A", GeneratorTestHarness.RunAndGetSource(s, "CMapper.g.cs"),
            StringComparison.Ordinal);
        Assert.Contains("ToB(this global::Demo.A",
            GeneratorTestHarness.RunAndGetSource(s, "DwarfMapper.Extensions.g.cs"), StringComparison.Ordinal);
    }
}
