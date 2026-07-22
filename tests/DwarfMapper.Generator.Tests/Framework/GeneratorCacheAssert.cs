// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>
///     Cacheability assertions runnable against any generator. Incrementality is invisible to every other test
///     (they each run the generator once), so a model that stops being value-equatable — or that leaks an
///     ISymbol — silently disables caching and roots old compilations while the suite stays green.
/// </summary>
internal static class GeneratorCacheAssert
{
    private const string UnrelatedEdit = "namespace Other { public class Unrelated { public int Z; } }";

    public static void Battery(GeneratorUnderTest generator, string source)
    {
        ArgumentNullException.ThrowIfNull(generator);

        FullyCachedOnRerun(generator.Create(), source, generator.TrackingNames);
        CachedAfterUnrelatedEdit(generator.Create(), source, generator.TrackingNames);
        NoSymbolsInPipeline(generator.Create(), source, generator.TrackingNames);
    }

    public static void FullyCachedOnRerun(IIncrementalGenerator generator, string source,
        IReadOnlyList<string> trackingNames)
    {
        var compilation = GeneratorTestHarness.BuildCompilation("CacheAsm", source);
        var driver = GeneratorRunner.RunTracked(generator).RunGenerators(compilation);
        driver = driver.RunGenerators(compilation);

        AssertStepsCached(driver, trackingNames, "an identical re-run");
    }

    public static void CachedAfterUnrelatedEdit(IIncrementalGenerator generator, string source,
        IReadOnlyList<string> trackingNames)
    {
        var compilation = GeneratorTestHarness.BuildCompilation("CacheAsm", source);
        var driver = GeneratorRunner.RunTracked(generator).RunGenerators(compilation);

        var modified = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(UnrelatedEdit));
        driver = driver.RunGenerators(modified);

        AssertStepsCached(driver, trackingNames, "an unrelated edit");
    }

    public static void NoSymbolsInPipeline(IIncrementalGenerator generator, string source,
        IReadOnlyList<string> trackingNames)
    {
        var compilation = GeneratorTestHarness.BuildCompilation("CacheAsm", source);
        var driver = GeneratorRunner.RunTracked(generator).RunGenerators(compilation);
        var tracked = driver.GetRunResult().Results[0].TrackedSteps;

        var missing = new List<string>();

        foreach (var name in trackingNames)
        {
            if (!tracked.TryGetValue(name, out var steps))
            {
                missing.Add(name);
                continue;
            }

            foreach (var value in steps.SelectMany(s => s.Outputs).Select(o => o.Value))
                Assert.True(IsSymbolFree(value),
                    $"Step '{name}' produced a {value?.GetType().Name} — pipeline models must not hold "
                    + "ISymbol, SyntaxNode, Location or Compilation. They are never equatable (so caching dies) "
                    + "and they root old compilations, forcing Roslyn to retain memory it could free.");
        }

        Assert.True(missing.Count == 0,
            "Declared tracking name(s) never appeared in TrackedSteps: " + string.Join(", ", missing)
            + ". Either the fixture does not trigger that pipeline, or the name in AllStepNames is stale. A step "
            + "that never runs is not evidence of freedom from leaked symbols — it is an unasserted step.");
    }

    private static bool IsSymbolFree(object? value)
    {
        return value is not (ISymbol or SyntaxNode or Location or Compilation);
    }

    private static void AssertStepsCached(GeneratorDriver driver, IReadOnlyList<string> trackingNames,
        string what)
    {
        var tracked = driver.GetRunResult().Results[0].TrackedSteps;

        var missing = new List<string>();

        foreach (var name in trackingNames)
        {
            if (!tracked.TryGetValue(name, out var steps))
            {
                missing.Add(name);
                continue;
            }

            Assert.All(steps, step => Assert.All(step.Outputs, output =>
                Assert.True(
                    output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                    $"Step '{name}' was {output.Reason} after {what}; expected Cached/Unchanged. A "
                    + "non-value-equatable field (raw ImmutableArray, leaked symbol, unstable collection) "
                    + "likely broke the model's equality and disabled incremental caching.")));
        }

        Assert.True(missing.Count == 0,
            "Declared tracking name(s) never appeared in TrackedSteps: " + string.Join(", ", missing)
            + ". Either the fixture does not trigger that pipeline, or the name in AllStepNames is stale. A step "
            + "that never runs is not evidence of cacheability — it is an unasserted step.");
    }
}
