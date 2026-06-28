// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// The crux of the ambient registry: a consumer in one assembly uses a map declared in ANOTHER assembly it
/// references, and the validation root verifies the linkage at compile time across the real metadata boundary
/// (the provider's generated <c>[assembly: DwarfProvidesMap]</c> read from its emitted metadata).
/// </summary>
public sealed class AmbientCrossAssemblyTests
{
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
            "provider compilation failed:\n" + string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return MetadataReference.CreateFromImage(ms.ToArray());
    }

    private static System.Collections.Immutable.ImmutableArray<Diagnostic> RunRoot(string source, params MetadataReference[] extraRefs)
    {
        var compilation = GeneratorTestHarness.BuildCompilation("AmbientRootAsm", source).AddReferences(extraRefs);
        var driver = CSharpGeneratorDriver.Create(new DwarfGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        return diagnostics;
    }

    private static string Provider(string ns) => $$"""
        namespace {{ns}};
        public class Doc { public int V { get; set; } }
        public class Model { public int V { get; set; } }
        [global::DwarfMapper.DwarfMapper]
        [global::DwarfMapper.GenerateMap<Doc, Model>]
        public partial class Mapper { }
        """;

    private const string RootSource = """
        [assembly: global::DwarfMapper.DwarfMapperValidationRoot]
        namespace App;
        public class Consumer
        {
            public global::Shared.Model Convert(global::DwarfMapper.IDwarfMapper m, global::Shared.Doc d)
                => m.Map<global::Shared.Model>(d);
        }
        """;

    [Fact]
    public void Root_resolves_a_map_provided_by_a_referenced_assembly()
    {
        // Provider assembly declares the map -> its emitted metadata carries [assembly: DwarfProvidesMap].
        var provider = CompileToReference("Shared.Provider", """
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
        var typesOnly = CompileToReference("Shared.TypesOnly", """
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
}
