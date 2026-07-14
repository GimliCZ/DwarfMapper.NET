// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

public class FlatMappingTests
{
    [Fact]
    public void Emits_sorted_object_initializer_with_null_guard()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Person { public int Age { get; set; } public string Name { get; set; } = ""; }
                           public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }
                           [DwarfMapper]
                           public partial class PersonMapper
                           {
                               public partial PersonDto ToDto(Person p);
                           }
                           """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("partial global::Demo.PersonDto ToDto", generated, StringComparison.Ordinal);
        Assert.Contains("ArgumentNullException", generated, StringComparison.Ordinal);
        var ageIdx = generated.IndexOf("Age =", StringComparison.Ordinal);
        var nameIdx = generated.IndexOf("Name =", StringComparison.Ordinal);
        Assert.True(ageIdx > 0 && nameIdx > ageIdx);
    }
}
