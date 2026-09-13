// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Coverage suite for DwarfGenerator's SanitizeNamespace, which turns the assembly name into the namespace of the
// generated AddDwarfMappers() class. SanitizeNamespaceTests pins the keyword segments. These pin the spellings that
// suite never used:
//   - an underscore (kept);
//   - any other non-identifier character (replaced with '_');
//   - an empty segment from a doubled dot (a lone "_");
//   - a segment starting with a digit (prefixed "_");
//   - a compilation with no assembly name at all.
// The last is also the validation root's own-assembly read with nothing to read.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class AssemblyNameNamespaceCoverageTests
    {
        private const string Mapper = """
                                      using DwarfMapper;
                                      namespace Demo;
                                      public class A { public int X { get; set; } }
                                      public class B { public int X { get; set; } }
                                      [DwarfMapper]
                                      public partial class M { public partial B Map(A a); }
                                      """;

        private const string RootedProvider = """
                                              using DwarfMapper;
                                              [assembly: DwarfMapperValidationRoot]
                                              namespace Demo;
                                              public class A { public int X { get; set; } }
                                              public class B { public int X { get; set; } }
                                              [DwarfMapper]
                                              [GenerateMap<A, B>]
                                              public partial class P { }
                                              """;

        [Theory]
        [InlineData("My_Asm", "namespace My_Asm")]
        [InlineData("Acme-Data", "namespace Acme_Data")]
        [InlineData("Acme..Data", "namespace Acme._.Data")]
        [InlineData("Acme.2D", "namespace Acme._2D")]
        public void Assembly_name_spellings_become_a_valid_namespace(string assemblyName, string expected)
        {
            var di = GeneratorTestHarness.RunAndGetSource(Mapper, "DwarfMapper.ServiceCollectionExtensions.g.cs", assemblyName: assemblyName);

            Assert.Contains(expected, di, StringComparison.Ordinal);
            Assert.DoesNotContain(CSharpSyntaxTree.ParseText(di).GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        }

        private static GeneratorDriverRunResult RunRooted(string? assemblyName) =>
            CSharpGeneratorDriver.Create(new DwarfGenerator())
                .RunGenerators(GeneratorTestHarness.BuildCompilation(assemblyName!, RootedProvider))
                .GetRunResult();

        [Fact]
        public void A_nameless_validation_root_generates_what_a_named_one_does_under_a_fixed_namespace()
        {
            var nameless = RunRooted(null);
            var named = RunRooted("Named.Asm");

            var generated = Assert.Single(nameless.Results);
            Assert.Null(generated.Exception);
            Assert.Empty(nameless.Diagnostics);
            Assert.Equal(
                Assert.Single(named.Results).GeneratedSources.Select(static s => s.HintName),
                generated.GeneratedSources.Select(static s => s.HintName));

            var di = Assert.Single(generated.GeneratedSources, static s => s.HintName == "DwarfMapper.ServiceCollectionExtensions.g.cs");
            Assert.Contains("namespace DwarfMapperGenerated", di.SourceText.ToString(), StringComparison.Ordinal);
        }
    }
}
