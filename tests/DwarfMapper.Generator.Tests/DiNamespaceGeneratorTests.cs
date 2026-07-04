// SPDX-License-Identifier: GPL-2.0-only
using Xunit;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Regression for the cross-assembly CS0121 fix: the generated <c>AddDwarfMappers()</c> DI registration must
/// live in the consuming assembly's OWN namespace, never in <c>Microsoft.Extensions.DependencyInjection</c>
/// (which would collide when two DwarfMapper assemblies are referenced together). A silent revert of this is
/// invisible to single-assembly runtime tests, so it must be asserted at the generated-source level.
/// </summary>
public class DiNamespaceGeneratorTests
{
    [Fact]
    public void AddDwarfMappers_is_emitted_in_the_assembly_namespace_not_Microsoft_DI()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int X { get; set; } }
            public class Dst { public int X { get; set; } }
            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            public partial class M { }
            """;

        var di = GeneratorTestHarness.RunAndGetSource(s, "DwarfMapper.ServiceCollectionExtensions.g.cs");

        // The DI aggregate is only emitted when Microsoft.Extensions.DependencyInjection is referenced.
        Assert.False(string.IsNullOrEmpty(di), "DI registration source was not generated (DI not referenced by the test harness).");
        Assert.Contains("namespace DwarfMapperTestAsm", di, System.StringComparison.Ordinal);
        Assert.DoesNotContain("namespace Microsoft.Extensions.DependencyInjection", di, System.StringComparison.Ordinal);
    }
}
