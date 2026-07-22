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
    ///     A generator whose tracked step's own output value IS a raw <see cref="ISymbol" /> — the exact leak
    ///     <see cref="GeneratorCacheAssert.NoSymbolsInPipeline" /> exists to catch. <c>IsSymbolFree</c> is a
    ///     direct type-pattern match on the tracked step's output, not a reflective walk of its fields, so a
    ///     model class that merely HOLDS a <see cref="Compilation" /> or <see cref="ISymbol" /> in a property
    ///     would slip past undetected; the leak has to be the step's own value.
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
    public void NoSymbolsInPipeline_FIRES_for_a_leaked_symbol()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
            GeneratorCacheAssert.NoSymbolsInPipeline(
                new LeakedSymbolGenerator(), Valid, new[] { LeakedSymbolGenerator.StepName }));

        Assert.Contains("must not hold", ex.Message, StringComparison.Ordinal);
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
