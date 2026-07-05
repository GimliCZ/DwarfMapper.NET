// SPDX-License-Identifier: GPL-2.0-only
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Incremental-CACHING property. The pipeline models are value-equatable and symbol-free (records +
/// EquatableArray, no ISymbol/Compilation/SyntaxNode held), so an edit that does NOT touch a mapper class
/// must leave the per-mapper extraction step (and therefore the source-output step) <c>Cached</c>/
/// <c>Unchanged</c> — the generator must not recompute or re-emit. This is what actually makes the
/// generator cheap in the IDE; it is invisible to every other test (they each run the generator once). A
/// future regression that slips a non-equatable value (e.g. a raw <c>ImmutableArray</c> or a leaked symbol)
/// into a model would silently disable caching and ONLY this test would catch it.
/// </summary>
public class IncrementalCachingTests
{
    private const string MapperSource = """
        using DwarfMapper;
        using System.Collections.Generic;
        namespace Demo;
        public class Addr { public string City { get; set; } = ""; public string Zip { get; set; } = ""; }
        public class Src { public int Id { get; set; } public string Name { get; set; } = ""; public long N { get; set; } public Addr Address { get; set; } = new(); public List<int> Nums { get; set; } = new(); }
        public class Dst { public int Id { get; set; } public string Name { get; set; } = ""; public int N { get; set; } public string City { get; set; } = ""; public List<long> Nums { get; set; } = new(); }
        [DwarfMapper]
        public partial class M
        {
            [Flatten(nameof(Src.Address))]
            public partial Dst Map(Src s);
        }
        """;

    private static CSharpGeneratorDriver NewDriver() => CSharpGeneratorDriver.Create(
        new[] { new DwarfGenerator().AsSourceGenerator() },
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    [Fact]
    public void Unrelated_edit_leaves_the_mapper_pipeline_cached()
    {
        var compilation = GeneratorTestHarness.BuildCompilation("IncCacheAsm", MapperSource);
        GeneratorDriver driver = NewDriver();

        // Run 1 — primes the cache.
        driver = driver.RunGenerators(compilation);

        // An edit that does NOT touch the mapper class: add an unrelated syntax tree.
        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { public class Unrelated { public int Z; } }"));

        // Run 2 on the SAME driver so step reasons reflect the delta from run 1.
        driver = driver.RunGenerators(modified);

        var result = driver.GetRunResult().Results[0];

        // The extraction step must not recompute on an unrelated edit.
        var extractSteps = result.TrackedSteps[DwarfGenerator.ExtractStepName];
        Assert.NotEmpty(extractSteps);
        Assert.All(extractSteps, step => Assert.All(step.Outputs, output =>
            Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Extract step expected Cached/Unchanged after an unrelated edit, but was {output.Reason}. " +
                "A non-value-equatable field (raw ImmutableArray, leaked ISymbol/Compilation, etc.) likely " +
                "broke the model's value equality and disabled incremental caching.")));

        // And the source-output (emit) must not re-run either.
        var outputSteps = result.TrackedSteps["SourceOutput"];
        Assert.All(outputSteps, step => Assert.All(step.Outputs, output =>
            Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"SourceOutput expected Cached/Unchanged after an unrelated edit, but was {output.Reason}.")));
    }

    // Emits a provider assembly (with its generated [assembly: DwarfProvidesMap] manifest) as a metadata
    // reference, so a downstream validation-root compilation's ReadReferenced() returns a NON-empty pair set.
    private static PortableExecutableReference CompileProviderRef(string ns)
    {
        var src = $$"""
            namespace {{ns}};
            public class Doc { public int V { get; set; } }
            public class Model { public int V { get; set; } }
            [global::DwarfMapper.DwarfMapper]
            [global::DwarfMapper.GenerateMap<Doc, Model>]
            public partial class Mapper { }
            """;
        var compilation = GeneratorTestHarness.BuildCompilation("Prov_" + ns, src);
        var driver = CSharpGeneratorDriver.Create(new DwarfGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        using var ms = new MemoryStream();
        Assert.True(output.Emit(ms).Success);
        return MetadataReference.CreateFromImage(ms.ToArray());
    }

    [Fact]
    public void Unrelated_edit_leaves_the_root_validation_output_cached()
    {
        // A [DwarfMapperValidationRoot] that consumes a cross-assembly map. The root-validation SourceOutput
        // (DwarfMapper.Validate.g.cs / ValidateDi.g.cs) reads referenced provider manifests via the
        // CompilationProvider, which re-runs on every keystroke — so its output must be VALUE-equatable or the
        // node re-emits on every unrelated edit. Regression guard for that (raw ImmutableArray -> ref equality).
        var provRef = CompileProviderRef("Prov1");
        const string rootSrc = """
            [assembly: global::DwarfMapper.DwarfMapperValidationRoot]
            namespace App;
            public class Consumer
            {
                public global::Prov1.Model C(global::DwarfMapper.IDwarfMapper m, global::Prov1.Doc d)
                    => m.Map<global::Prov1.Model>(d);
            }
            """;
        var compilation = GeneratorTestHarness.BuildCompilation("RootIncCache", rootSrc).AddReferences(provRef);
        GeneratorDriver driver = NewDriver();

        driver = driver.RunGenerators(compilation);
        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { public class Unrelated { public int Z; } }"));
        driver = driver.RunGenerators(modified);

        var result = driver.GetRunResult().Results[0];
        var outputSteps = result.TrackedSteps["SourceOutput"];
        Assert.All(outputSteps, step => Assert.All(step.Outputs, output =>
            Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Root-validation SourceOutput expected Cached/Unchanged after an unrelated edit, but was " +
                $"{output.Reason}. The rootInfo tuple's Provided/Required must be value-equatable (EquatableArray), " +
                "else the [DwarfMapperValidationRoot] node re-emits Validate.g.cs on every keystroke.")));
    }

    private const string CoLocatedSource = """
        using DwarfMapper;
        namespace Demo;
        public class Src { public int Id { get; set; } public string Name { get; set; } = ""; }
        [GenerateMap<Src, Dst>]
        [MapProperty<Src, Dst>("Name", "FullName")]
        public sealed class Dst { public int Id { get; set; } public string FullName { get; set; } = ""; }
        """;

    [Fact]
    public void Unrelated_edit_leaves_the_co_located_pipeline_cached()
    {
        var compilation = GeneratorTestHarness.BuildCompilation("IncCacheCoLoc", CoLocatedSource);
        GeneratorDriver driver = NewDriver();
        driver = driver.RunGenerators(compilation);

        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { public class Unrelated { public int Z; } }"));
        driver = driver.RunGenerators(modified);

        var result = driver.GetRunResult().Results[0];
        var steps = result.TrackedSteps[DwarfGenerator.CoLocatedExtractStepName];
        Assert.NotEmpty(steps);
        Assert.All(steps, step => Assert.All(step.Outputs, output =>
            Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Co-located extract step expected Cached/Unchanged after an unrelated edit, but was {output.Reason}.")));
    }

    [Fact]
    public void Identical_compilation_run_twice_is_fully_cached()
    {
        var compilation = GeneratorTestHarness.BuildCompilation("IncCacheAsm2", MapperSource);
        GeneratorDriver driver = NewDriver();

        driver = driver.RunGenerators(compilation);
        // Re-run with the SAME compilation: everything must be Cached/Unchanged.
        driver = driver.RunGenerators(compilation);

        var result = driver.GetRunResult().Results[0];
        var extractSteps = result.TrackedSteps[DwarfGenerator.ExtractStepName];
        Assert.All(extractSteps, step => Assert.All(step.Outputs, output =>
            Assert.True(output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"expected Cached/Unchanged on identical re-run, got {output.Reason}")));
    }

    [Fact]
    public void Editing_the_mapper_does_recompute_it()
    {
        // Negative control: the test above only means something if a RELEVANT edit is NOT reported as cached.
        var compilation = GeneratorTestHarness.BuildCompilation("IncCacheAsm3", MapperSource);
        GeneratorDriver driver = NewDriver();
        driver = driver.RunGenerators(compilation);

        // Replace the mapper's syntax tree with a changed version (add a member to Dst + its source).
        var changed = MapperSource
            .Replace("public int Id { get; set; } public string Name", "public int Id { get; set; } public int Extra { get; set; } public string Name", System.StringComparison.Ordinal);
        var oldTree = compilation.SyntaxTrees.First();
        var modified = compilation.ReplaceSyntaxTree(oldTree, CSharpSyntaxTree.ParseText(changed));

        driver = driver.RunGenerators(modified);
        var result = driver.GetRunResult().Results[0];

        var extractSteps = result.TrackedSteps[DwarfGenerator.ExtractStepName];
        // At least one output must NOT be cached — the mapper changed, so it must be recomputed.
        Assert.Contains(extractSteps.SelectMany(s => s.Outputs),
            output => output.Reason is IncrementalStepRunReason.Modified or IncrementalStepRunReason.New);
    }
}
