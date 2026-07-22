// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Framework;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Tests the framework itself. It concentrates a large number of assertions into a few helpers, so a helper
///     that silently passed everything would neuter the whole safety net while the suite stayed green — the
///     exact vacuity failure this repo keeps finding. Every assertion is proven to FIRE.
/// </summary>
public class GeneratorFrameworkSelfTests
{
    private const string Valid = """
                                 using DwarfMapper;
                                 namespace Demo;
                                 public class A { public int X { get; set; } }
                                 public class B { public int X { get; set; } }
                                 [DwarfMapper] public partial class M { public partial B Map(A a); }
                                 """;

    /// <summary>
    ///     Same as <see cref="Valid" /> but also exercises DwarfGenerator's co-located ([GenerateMap]-on-plain-
    ///     class) pipeline. DwarfGenerator.AllStepNames includes DwarfMapperCoLocatedExtract, and
    ///     ForAttributeWithMetadataName never enters TrackedSteps at all when no syntax node carries that
    ///     attribute — so <see cref="Valid" /> alone makes GeneratorCacheAssert fail for the WRONG reason ("never
    ///     appeared in TrackedSteps") rather than proving the real generator's caching. Every declared step must
    ///     actually run for the fixture under test.
    /// </summary>
    private const string ValidAllPipelines = Valid + """

                                              public class Src3 { public int Id { get; set; } }
                                              [GenerateMap<Src3, Dst3>] public sealed class Dst3 { public int Id { get; set; } }
                                              """;

    /// <summary>A generator whose model is a plain class with reference equality — caching cannot work.</summary>
    private sealed class NonEquatableGenerator : IIncrementalGenerator
    {
        public const string StepName = "NonEquatableStep";

        private sealed class Model
        {
            public string Name { get; init; } = "";
        }

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var models = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
                    static (ctx, _) => new Model { Name = ctx.Node.ToString() })
                .WithTrackingName(StepName);

            context.RegisterSourceOutput(models, static (spc, m) => spc.AddSource(
                "NonEquatable_" + m.Name.GetHashCode(System.StringComparison.Ordinal) + ".g.cs", "// x"));
        }
    }

    [Fact]
    public void FullyCachedOnRerun_FIRES_for_a_non_equatable_model()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
            GeneratorCacheAssert.FullyCachedOnRerun(
                new NonEquatableGenerator(), Valid, new[] { NonEquatableGenerator.StepName }));

        Assert.Contains("Cached/Unchanged", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A generator whose tracked step's own output value IS a raw <see cref="ISymbol" /> — the degenerate
    ///     leak shape where the direct type match on the step's output alone is already enough to catch it.
    /// </summary>
    private sealed class LeakedSymbolGenerator : IIncrementalGenerator
    {
        public const string StepName = "LeakedSymbolStep";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var symbols = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
                    // Deliberate leak: rooting the declared symbol instead of projecting a value-equatable
                    // snapshot off of it.
                    static (ctx, ct) => ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct))
                .WithTrackingName(StepName);

            context.RegisterSourceOutput(symbols, static (spc, symbol) => spc.AddSource(
                (symbol?.Name ?? "Unknown") + "_Leaked.g.cs", "// x"));
        }
    }

    [Fact]
    public void NoSymbolsInPipeline_FIRES_for_a_symbol_returned_directly()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
            GeneratorCacheAssert.NoSymbolsInPipeline(
                new LeakedSymbolGenerator(), Valid, new[] { LeakedSymbolGenerator.StepName }));

        Assert.Contains("must not hold", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A generator whose tracked step's OUTPUT is a model record — not a raw symbol — but that model has a
    ///     property typed <see cref="ISymbol" />. This is the realistic regression: a hand-written pipeline
    ///     model (e.g. <c>record Model(string Name, EquatableArray&lt;Member&gt; Members)</c>) gaining a symbol-
    ///     typed member. A direct type match on the step's own output value (<c>Model</c>) passes this trivially;
    ///     only a reflective walk of the model's members catches it.
    /// </summary>
    private sealed class LeakedSymbolPropertyGenerator : IIncrementalGenerator
    {
        public const string StepName = "LeakedSymbolPropertyStep";

        private sealed record Model(string Name, ISymbol? Symbol);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var models = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
                    // Deliberate leak: the step's OWN output value is `Model`, a plain record — but one of its
                    // properties holds the declared symbol instead of a value-equatable projection of it.
                    static (ctx, ct) => new Model(ctx.Node.ToString(), ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct)))
                .WithTrackingName(StepName);

            context.RegisterSourceOutput(models, static (spc, m) => spc.AddSource(
                "LeakedProperty_" + m.Name.GetHashCode(StringComparison.Ordinal) + ".g.cs", "// x"));
        }
    }

    [Fact]
    public void NoSymbolsInPipeline_FIRES_for_a_symbol_held_in_a_model_property()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
            GeneratorCacheAssert.NoSymbolsInPipeline(
                new LeakedSymbolPropertyGenerator(), Valid, new[] { LeakedSymbolPropertyGenerator.StepName }));

        Assert.Contains("must not hold", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Model.Symbol", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A generator whose tracked step's output is a chain of plain records nested one level deeper than
    ///     GeneratorCacheAssert's walk cap, with NO leak anywhere in the chain. A depth-capped walk and a
    ///     genuinely clean walk both return "no leak found" unless truncation is tracked separately — this proves
    ///     that a real truncation is reported as a hard failure rather than being mistaken for a clean pass.
    /// </summary>
    private sealed class DeepNoLeakGenerator : IIncrementalGenerator
    {
        public const string StepName = "DeepNoLeakStep";

        // Depth 10 of chained nodes comfortably clears the depth-8 cap: no ISymbol/SyntaxNode/Location/
        // Compilation anywhere in the chain, so the only thing that can make this walk fail is truncation.
        private sealed record Node(string Name, Node? Next);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var models = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
                    static (ctx, _) => BuildChain(ctx.Node.ToString(), 10))
                .WithTrackingName(StepName);

            context.RegisterSourceOutput(models, static (spc, m) => spc.AddSource(
                "DeepNoLeak_" + m.Name.GetHashCode(StringComparison.Ordinal) + ".g.cs", "// x"));
        }

        private static Node BuildChain(string name, int depth) =>
            depth == 0 ? new Node(name, null) : new Node(name, BuildChain(name, depth - 1));
    }

    [Fact]
    public void Symbol_leak_walk_reports_truncation_rather_than_passing_silently()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
            GeneratorCacheAssert.NoSymbolsInPipeline(
                new DeepNoLeakGenerator(), Valid, new[] { DeepNoLeakGenerator.StepName }));

        Assert.Contains("depth cap", ex.Message, StringComparison.Ordinal);
        Assert.Contains("NOT evidence of a clean model", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A generator whose tracked step's output holds a reference cycle: a node whose own <c>Next</c> property
    ///     points back to itself. The walk's reference-equality cycle guard must stop this from hanging or
    ///     stack-overflowing — and, since nothing in the cycle is a leak, the check must still pass cleanly.
    /// </summary>
    private sealed class CyclicModelGenerator : IIncrementalGenerator
    {
        public const string StepName = "CyclicModelStep";

        private sealed class Node
        {
            public string Name { get; set; } = "";
            public Node? Next { get; set; }
        }

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var models = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
                    static (ctx, _) =>
                    {
                        var node = new Node { Name = ctx.Node.ToString() };
                        node.Next = node;
                        return node;
                    })
                .WithTrackingName(StepName);

            context.RegisterSourceOutput(models, static (spc, m) => spc.AddSource(
                "Cyclic_" + m.Name.GetHashCode(StringComparison.Ordinal) + ".g.cs", "// x"));
        }
    }

    [Fact]
    public void Symbol_leak_walk_terminates_on_a_cyclic_model()
    {
        // No wrapping ThrowsAny: the point being proven is that this returns at all (rather than hanging or
        // stack-overflowing on the self-reference) AND that it does not misreport the cycle as a leak.
        GeneratorCacheAssert.NoSymbolsInPipeline(
            new CyclicModelGenerator(), Valid, new[] { CyclicModelGenerator.StepName });
    }

    [Fact]
    public void CachedAfterUnrelatedEdit_FIRES_for_a_non_equatable_model()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
            GeneratorCacheAssert.CachedAfterUnrelatedEdit(
                new NonEquatableGenerator(), Valid, new[] { NonEquatableGenerator.StepName }));

        Assert.Contains("Cached/Unchanged", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declared_tracking_name_that_never_runs_is_a_failure()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
            GeneratorCacheAssert.FullyCachedOnRerun(
                new DwarfGenerator(), Valid, new[] { "NoSuchStepName" }));

        Assert.Contains("never appeared", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FullyCachedOnRerun_passes_for_the_real_generator()
    {
        GeneratorCacheAssert.FullyCachedOnRerun(new DwarfGenerator(), ValidAllPipelines, DwarfGenerator.AllStepNames);
    }

    [Fact]
    public void Fingerprint_is_sensitive_to_a_single_character_of_output()
    {
        var a = new GoldenCase("self:a", Valid, "DwarfGenerator");
        var b = a with { Source = Valid.Replace("public int X", "public int Xx", StringComparison.Ordinal) };

        Assert.NotEqual(GoldenFingerprint.Compute(a), GoldenFingerprint.Compute(b));
    }

    [Fact]
    public void Runner_returns_every_emitted_file_not_just_the_first()
    {
        var run = GeneratorRunner.Run(new DwarfGenerator(), Valid);

        Assert.True(run.OutputsByHintName.Count > 1,
            "Expected the per-mapper file AND the assembly-wide aggregates. If only one file comes back the "
            + "runner has inherited GeneratorTestHarness.Run's aggregate filter and the golden corpus would "
            + "never cover the facade/DI/manifest emitters.");
    }
}
