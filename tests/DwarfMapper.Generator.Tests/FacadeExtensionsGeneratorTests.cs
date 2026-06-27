// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Tests for the assembly-wide convenience facade (<c>DwarfMapper.Extensions.g.cs</c>): the
/// <c>source.ToTarget()</c> extension methods generated from simple maps.
/// </summary>
public sealed class FacadeExtensionsGeneratorTests
{
    private const string FacadeHint = "DwarfMapper.Extensions.g.cs";

    [Fact]
    public void Facade_emits_extension_for_a_simple_map()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; set; } }
            [DwarfMapper] public partial class PersonMapper { public partial PersonDto ToDto(Person p); }
            """;

        var facade = GeneratorTestHarness.RunAndGetSource(s, FacadeHint);

        Assert.Contains("namespace DwarfMapper.Extensions", facade, StringComparison.Ordinal);
        Assert.Contains("ToPersonDto(this global::Demo.Person source)", facade, StringComparison.Ordinal);
        Assert.Contains("global::Demo.PersonDto", facade, StringComparison.Ordinal);
    }

    [Fact]
    public void Facade_extension_name_follows_target_type_not_method_name()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; set; } }
            [DwarfMapper] public partial class PersonMapper { public partial PersonDto Convert(Person p); }
            """;

        var facade = GeneratorTestHarness.RunAndGetSource(s, FacadeHint);

        // Named after the target type (ToPersonDto), forwarding to the user's method (Convert).
        Assert.Contains("ToPersonDto(this global::Demo.Person source)", facade, StringComparison.Ordinal);
        Assert.Contains(".Convert(source);", facade, StringComparison.Ordinal);
    }

    [Fact]
    public void Facade_respects_GenerateExtensions_false()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; set; } }
            [DwarfMapper(GenerateExtensions = false)] public partial class PersonMapper { public partial PersonDto ToDto(Person p); }
            """;

        var facade = GeneratorTestHarness.RunAndGetSource(s, FacadeHint);

        // The only mapper opts out → no facade file at all.
        Assert.True(string.IsNullOrEmpty(facade), "Expected no facade when the only mapper opts out.");
    }

    [Fact]
    public void Facade_skips_colliding_extension_signatures()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; set; } }
            [DwarfMapper] public partial class MapperA { public partial PersonDto ToDto(Person p); }
            [DwarfMapper] public partial class MapperB { public partial PersonDto ToDto(Person p); }
            """;

        var facade = GeneratorTestHarness.RunAndGetSource(s, FacadeHint);

        // Two mappers would both emit ToPersonDto(this Person) → ambiguous → neither is emitted.
        Assert.DoesNotContain("ToPersonDto", facade, StringComparison.Ordinal);
    }

    [Fact]
    public void Facade_skips_update_into_and_projection_methods()
    {
        const string s = """
            using DwarfMapper;
            using System.Linq;
            namespace Demo;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; set; } }
            [DwarfMapper] public partial class PersonMapper
            {
                public partial PersonDto ToDto(Person p);                 // eligible
                public partial void Update(Person p, PersonDto d);        // update-into: skipped
                public partial IQueryable<PersonDto> Project(IQueryable<Person> q); // projection: skipped
            }
            """;

        var facade = GeneratorTestHarness.RunAndGetSource(s, FacadeHint);

        Assert.Contains("ToPersonDto(this global::Demo.Person source)", facade, StringComparison.Ordinal);
        // Only one extension method (the simple map). Update/Project produce none.
        var occurrences = facade.Split(new[] { "(this " }, StringSplitOptions.None).Length - 1;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void Facade_code_compiles_clean()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class Person { public int Age { get; set; } public string Name { get; set; } }
            public class PersonDto { public int Age { get; set; } public string Name { get; set; } }
            [DwarfMapper] public partial class PersonMapper { public partial PersonDto ToDto(Person p); }
            """;

        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(s);
        Assert.Empty(errors);
    }
}
