// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     Regression for the cross-assembly CS0121 fix: the generated <c>AddDwarfMappers()</c> DI registration must
    ///     live in the consuming assembly's OWN namespace, never in <c>Microsoft.Extensions.DependencyInjection</c>
    ///     (which would collide when two DwarfMapper assemblies are referenced together). A silent revert of this is
    ///     invisible to single-assembly runtime tests, so it must be asserted at the generated-source level.
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
            Assert.False(string.IsNullOrEmpty(di),
                "DI registration source was not generated (DI not referenced by the test harness).");
            Assert.Contains("namespace DwarfMapperTestAsm", di, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace Microsoft.Extensions.DependencyInjection", di, StringComparison.Ordinal);
        }

        [Fact]
        public void No_mappers_at_all_emits_no_DI_registration_file()
        {
            // The test harness always references Microsoft.Extensions.DependencyInjection, so EmitServiceCollection
            // still runs — with an empty model list, since there is nothing anywhere resembling a [DwarfMapper]
            // class. Its own `mapperTypes.Count == 0` guard is what must answer null here, not the DI-referenced
            // check above (already covered) or a missing call.
            const string s = """
                             namespace Demo;
                             public class Plain { public int X { get; set; } }
                             """;

            var di = GeneratorTestHarness.RunAndGetSource(s, "DwarfMapper.ServiceCollectionExtensions.g.cs");
            Assert.Equal(string.Empty, di);
        }
    }
}
