// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// ReadMapConfig returns early when the compilation does not contain DwarfMapper.MapConfig<S,T>. The test harness always
// references the runtime, so that answer never ran. A compilation that declares its own DwarfMapperAttribute and
// references nothing else reaches the extractor without the runtime.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class MapConfigWithoutRuntimeTests
    {
        [Fact]
        public void A_mapper_in_a_compilation_without_the_runtime_is_extracted_without_reading_MapConfig()
        {
            var compilation = CSharpCompilation.Create("NoRuntime",
                [CSharpSyntaxTree.ParseText("""
                                            namespace DwarfMapper
                                            {
                                                [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class DwarfMapperAttribute : System.Attribute { }
                                            }
                                            namespace Demo
                                            {
                                                public class S { public int A { get; set; } }
                                                public class D { public int A { get; set; } }
                                                [DwarfMapper.DwarfMapper] public partial class M { public partial D Map(S s); }
                                            }
                                            """)],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            CSharpGeneratorDriver.Create(new DwarfGenerator())
                .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains(output.SyntaxTrees, t => t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) &&
                                                     t.ToString().Contains("A = s.A", StringComparison.Ordinal));
        }
    }
}
