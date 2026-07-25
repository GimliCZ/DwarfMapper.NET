// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

public class InheritanceMappingTests
{
    private static readonly string[] IdEqualsToken = ["Id ="];

    [Fact]
    public void Hidden_destination_member_does_not_emit_duplicate_assignment()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class BaseDto { public int Age { get; set; } }
                           public class PersonDto : BaseDto { public new int Age { get; set; } }
                           public class Person { public int Age { get; set; } }
                           [DwarfMapper]
                           public partial class PersonMapper { public partial PersonDto ToDto(Person p); }
                           """;

        var compileErrors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
        Assert.DoesNotContain(compileErrors, d => d.Id == "CS1912"); // duplicate member init
        Assert.Empty(compileErrors);
    }

    [Fact]
    public void Inherited_settable_member_is_mapped_once()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class BaseDto { public int Id { get; set; } }
                           public class PersonDto : BaseDto { public string Name { get; set; } = ""; }
                           public class Person { public int Id { get; set; } public string Name { get; set; } = ""; }
                           [DwarfMapper]
                           public partial class PersonMapper { public partial PersonDto ToDto(Person p); }
                           """;

        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        // 'Id' (inherited) assigned exactly once
        var occurrences = generated.Split(IdEqualsToken, StringSplitOptions.None).Length - 1;
        Assert.Equal(1, occurrences);
        GeneratorAssert.EmitsCompilableCode(src);
    }
}
