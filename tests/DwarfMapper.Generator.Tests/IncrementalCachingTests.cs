// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Incremental-CACHING property. The pipeline models are value-equatable and symbol-free (records +
///     EquatableArray, no ISymbol/Compilation/SyntaxNode held), so an edit that does NOT touch a mapper class
///     must leave the per-mapper extraction step (and therefore the source-output step) <c>Cached</c>/
///     <c>Unchanged</c> — the generator must not recompute or re-emit. This is what actually makes the
///     generator cheap in the IDE; it is invisible to every other test (they each run the generator once). A
///     future regression that slips a non-equatable value (e.g. a raw <c>ImmutableArray</c> or a leaked symbol)
///     into a model would silently disable caching and ONLY this test would catch it.
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

    private const string CoLocatedSource = """
                                           using DwarfMapper;
                                           namespace Demo;
                                           public class Src { public int Id { get; set; } public string Name { get; set; } = ""; }
                                           [GenerateMap<Src, Dst>]
                                           [MapProperty<Src, Dst>("Name", "FullName")]
                                           public sealed class Dst { public int Id { get; set; } public string FullName { get; set; } = ""; }
                                           """;

    private static CSharpGeneratorDriver NewDriver()
    {
        return CSharpGeneratorDriver.Create(
            new[] { new DwarfGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));
    }

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

    // A mapper that reports diagnostics WITHOUT being fatal: DWARF070 (nullable ref -> non-nullable target)
    // and DWARF038 (implicit cross-category numeric). Both must survive caching.
    private const string DiagnosticSource = """
                                            #nullable enable
                                            using DwarfMapper;
                                            namespace Demo;
                                            public class Src { public string? Name { get; set; } public int N { get; set; } }
                                            public class Dst { public string Name { get; set; } = ""; public double N { get; set; } }
                                            [DwarfMapper]
                                            public partial class M { public partial Dst Map(Src s); }
                                            """;

    [Fact]
    public void Diagnostics_are_still_reported_when_the_output_is_CACHED()
    {
        // The nastiest incremental-generator failure mode, and the one no other test here can see: a warning
        // that is reported on the first run and then VANISHES on subsequent runs because the output node was
        // served from cache. In an IDE that means a diagnostic which appears once and then disappears on the
        // next keystroke; in a build with incremental compilation it can mean a warning that simply never
        // surfaces. Diagnostics must be part of the cached output, not a side effect of computing it.
        //
        // Note the deliberate shape: the SAME driver is reused across both runs (a fresh driver would have an
        // empty cache and prove nothing — which is exactly why DeterminismFuzzTests, comparing two fresh
        // drivers, cannot catch this).
        var compilation = GeneratorTestHarness.BuildCompilation("IncCacheDiag", DiagnosticSource,
            NullableContextOptions.Enable);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new DwarfGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));

        driver = driver.RunGenerators(compilation);
        var firstRun = driver.GetRunResult().Results[0].Diagnostics;

        Assert.Contains(firstRun, d => d.Id == "DWARF070");

        // Second run on the SAME compilation and the SAME driver: everything is a cache hit.
        driver = driver.RunGenerators(compilation);
        var secondRun = driver.GetRunResult().Results[0].Diagnostics;

        Assert.Equal(
            firstRun.Select(d => d.Id + "@" + d.Location.GetLineSpan().StartLinePosition).OrderBy(s => s,
                StringComparer.Ordinal),
            secondRun.Select(d => d.Id + "@" + d.Location.GetLineSpan().StartLinePosition).OrderBy(s => s,
                StringComparer.Ordinal));
    }

    [Fact]
    public void Diagnostics_survive_an_unrelated_edit()
    {
        // Same failure mode, but the realistic IDE version: the user types in a DIFFERENT file. The mapper is
        // untouched, so its node is cached — and its warnings must not evaporate just because nothing about it
        // changed.
        var compilation = GeneratorTestHarness.BuildCompilation("IncCacheDiag2", DiagnosticSource,
            NullableContextOptions.Enable);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new DwarfGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));

        driver = driver.RunGenerators(compilation);

        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { public class Unrelated { public int Z; } }"));
        driver = driver.RunGenerators(modified);

        var afterEdit = driver.GetRunResult().Results[0].Diagnostics;

        Assert.Contains(afterEdit, d => d.Id == "DWARF070");
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
            .Replace("public int Id { get; set; } public string Name",
                "public int Id { get; set; } public int Extra { get; set; } public string Name",
                StringComparison.Ordinal);
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
