// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     The crux of the ambient registry: a consumer in one assembly uses a map declared in ANOTHER assembly it
    ///     references, and the validation root verifies the linkage at compile time across the real metadata boundary
    ///     (the provider's generated <c>[assembly: DwarfProvidesMap]</c> read from its emitted metadata).
    /// </summary>
    public sealed class AmbientCrossAssemblyTests
    {
        private const string RootSource = """
                                          [assembly: global::DwarfMapper.DwarfMapperValidationRoot]
                                          namespace App;
                                          public class Consumer
                                          {
                                              public global::Shared.Model Convert(global::DwarfMapper.IDwarfMapper m, global::Shared.Doc d)
                                                  => m.Map<global::Shared.Model>(d);
                                          }
                                          """;

        // Compiles source (running the generator) and returns its emitted assembly as a metadata reference,
        // so a downstream compilation references the REAL metadata (incl. the generated assembly manifests).
        private static PortableExecutableReference CompileToReference(string assemblyName, string source)
        {
            var compilation = GeneratorTestHarness.BuildCompilation(assemblyName, source);
            var driver = CSharpGeneratorDriver.Create(new DwarfGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

            using var ms = new MemoryStream();
            var result = output.Emit(ms);
            Assert.True(result.Success,
                "provider compilation failed:\n" +
                string.Join("\n",
                    result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            return MetadataReference.CreateFromImage(ms.ToArray());
        }

        private static ImmutableArray<Diagnostic> RunRoot(string source, params MetadataReference[] extraRefs)
        {
            var compilation = GeneratorTestHarness.BuildCompilation("AmbientRootAsm", source).AddReferences(extraRefs);
            var driver = CSharpGeneratorDriver.Create(new DwarfGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
            return diagnostics;
        }

        private static string Provider(string ns)
        {
            return $$"""
                     namespace {{ns}};
                     public class Doc { public int V { get; set; } }
                     public class Model { public int V { get; set; } }
                     [global::DwarfMapper.DwarfMapper]
                     [global::DwarfMapper.GenerateMap<Doc, Model>]
                     public partial class Mapper { }
                     """;
        }

        [Fact]
        public void Root_resolves_a_map_provided_by_a_referenced_assembly()
        {
            // Provider assembly declares the map -> its emitted metadata carries [assembly: DwarfProvidesMap].
            var provider = CompileToReference("Shared.Provider",
                """
                namespace Shared;
                public class Doc { public int V { get; set; } }
                public class Model { public int V { get; set; } }
                [global::DwarfMapper.DwarfMapper]
                [global::DwarfMapper.GenerateMap<Doc, Model>]
                public partial class SharedMapper { }
                """);

            var diags = RunRoot(RootSource, provider);

            Assert.DoesNotContain(diags, d => d.Id == "DWARF061");
        }

        [Fact]
        public void Root_reports_DWARF061_when_the_referenced_assembly_only_defines_types_no_map()
        {
            // Same types, but NO mapper -> no DwarfProvidesMap in metadata -> the consumed map is unprovided.
            var typesOnly = CompileToReference("Shared.TypesOnly",
                """
                namespace Shared;
                public class Doc { public int V { get; set; } }
                public class Model { public int V { get; set; } }
                """);

            var diags = RunRoot(RootSource, typesOnly);

            Assert.Contains(diags, d => d.Id == "DWARF061" && d.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        public void Two_referenced_assemblies_providing_the_same_pair_report_DWARF063()
        {
            // Both providers declare Shared.Doc -> Shared.Model, so the graph has two providers for one pair.
            var p1 = CompileToReference("Prov.One", Provider("Shared"));
            var p2 = CompileToReference("Prov.Two", Provider("Shared"));

            var diags = RunRoot("[assembly: global::DwarfMapper.DwarfMapperValidationRoot]", p1, p2);

            Assert.Contains(diags, d => d.Id == "DWARF063" && d.Severity == DiagnosticSeverity.Warning);
        }

        // Emits source WITHOUT running the generator, for a library whose manifest attributes were written by hand.
        private static PortableExecutableReference CompileWithoutGenerator(string assemblyName, string source)
        {
            using var ms = new MemoryStream();
            var result = GeneratorTestHarness.BuildCompilation(assemblyName, source).Emit(ms);
            Assert.True(result.Success,
                "library compilation failed:\n" +
                string.Join("\n",
                    result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            return MetadataReference.CreateFromImage(ms.ToArray());
        }

        [Fact]
        public void Root_reports_DWARF061_for_a_pair_only_a_referenced_consumer_requires()
        {
            // A mid-tier consumer compiled with the generator carries [assembly: DwarfRequiresMap] in its metadata. The
            // root consumes nothing itself, so the requirement it reports is the one it read from that metadata.
            var consumer = CompileToReference("Shared.Consumer",
                """
                namespace Shared;
                public class Doc { public int V { get; set; } }
                public class Model { public int V { get; set; } }
                public class Use
                {
                    public Model Convert(global::DwarfMapper.IDwarfMapper m, Doc d) => m.Map<Model>(d);
                }
                """);

            var diags = RunRoot("[assembly: global::DwarfMapper.DwarfMapperValidationRoot]", consumer);

            Assert.Contains(diags,
                d => d.Id == "DWARF061" &&
                     d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Shared.Doc", StringComparison.Ordinal));
        }

        [Fact]
        public void A_referenced_manifest_with_a_null_type_provides_nothing()
        {
            // Written by hand in a library built without the generator: a manifest with a null type argument names no
            // pair, so the root's own requirement stays unprovided.
            var library = CompileWithoutGenerator("Shared.NullManifest",
                """
                [assembly: global::DwarfMapper.DwarfProvidesMap(typeof(Shared.Doc), null)]
                [assembly: global::DwarfMapper.DwarfProvidesMap(null, typeof(Shared.Model))]
                namespace Shared;
                public class Doc { public int V { get; set; } }
                public class Model { public int V { get; set; } }
                """);

            var diags = RunRoot(RootSource, library);

            Assert.Contains(diags, d => d.Id == "DWARF061");
            Assert.DoesNotContain(diags, d => d.Id == "DWARF063");
        }

        [Fact]
        public void A_referenced_look_alike_manifest_attribute_of_another_shape_provides_nothing()
        {
            // A library that declares its own DwarfMapper.DwarfProvidesMapAttribute with ONE parameter: its name is the
            // manifest's, its shape is not, so it names no pair and the root's requirement stays unprovided.
            var library = CompileWithoutGenerator("Shared.LookAlike",
                """
                [assembly: DwarfMapper.DwarfProvidesMap(typeof(Shared.Doc))]
                namespace DwarfMapper
                {
                    [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
                    public sealed class DwarfProvidesMapAttribute : System.Attribute { public DwarfProvidesMapAttribute(System.Type source) { } }
                }
                namespace Shared
                {
                    public class Doc { public int V { get; set; } }
                    public class Model { public int V { get; set; } }
                }
                """);

            var diags = RunRoot(RootSource, library);

            Assert.Contains(diags, d => d.Id == "DWARF061");
        }

        [Fact]
        public void A_referenced_requires_manifest_with_a_null_type_requires_nothing()
        {
            // The requires-side twin: a hand-written requirement with a null type argument names no pair, so a root that
            // consumes nothing itself has nothing to report.
            var library = CompileWithoutGenerator("Shared.NullRequirement",
                """
                [assembly: global::DwarfMapper.DwarfRequiresMap(typeof(Shared.Doc), null)]
                namespace Shared;
                public class Doc { public int V { get; set; } }
                """);

            var diags = RunRoot("[assembly: global::DwarfMapper.DwarfMapperValidationRoot]", library);

            Assert.DoesNotContain(diags, d => d.Id == "DWARF061");
        }
    }
}
