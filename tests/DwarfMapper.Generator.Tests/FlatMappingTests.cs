// SPDX-License-Identifier: GPL-2.0-only
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

        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains("partial global::Demo.PersonDto ToDto", generated, System.StringComparison.Ordinal);
        Assert.Contains("ArgumentNullException", generated, System.StringComparison.Ordinal);
        var ageIdx = generated.IndexOf("Age =", System.StringComparison.Ordinal);
        var nameIdx = generated.IndexOf("Name =", System.StringComparison.Ordinal);
        Assert.True(ageIdx > 0 && nameIdx > ageIdx);
    }
}
