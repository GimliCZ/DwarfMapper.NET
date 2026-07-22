// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using System.Reflection;
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
            {
                var leak = FindSymbolLeak(value, "", 0, new HashSet<object>(ReferenceEqualityComparer.Instance));

                if (leak is null) continue;

                // The root value IS the leak (no member path) versus the leak is reachable through one or
                // more members of the root value — the two get worded differently so the failure names
                // exactly what the reviewer needs to look at.
                var where = leak.Value.Path.Length == 0
                    ? $"Step '{name}' produced a {leak.Value.TypeName}"
                    : $"Step '{name}' output {value?.GetType().Name}{leak.Value.Path} holds a {leak.Value.TypeName}";

                Assert.Fail(where + " — pipeline models must not hold ISymbol, SyntaxNode, Location or "
                    + "Compilation. They are never equatable (so caching dies) and they root old compilations, "
                    + "forcing Roslyn to retain memory it could free.");
            }
        }

        Assert.True(missing.Count == 0,
            "Declared tracking name(s) never appeared in TrackedSteps: " + string.Join(", ", missing)
            + ". Either the fixture does not trigger that pipeline, or the name in AllStepNames is stale. A step "
            + "that never runs is not evidence of freedom from leaked symbols — it is an unasserted step.");
    }

    /// <summary>Where a leaked <see cref="ISymbol" />/<see cref="SyntaxNode" />/etc. was found, relative to
    /// the tracked step's own output value, and the runtime type name of the offending value.</summary>
    private readonly record struct SymbolLeak(string Path, string TypeName);

    // Bounds the reflective walk below: pipeline models are small hand-written records/EquatableArray wrappers,
    // not deep object graphs, so 4 levels comfortably covers a realistic leak (e.g. Model -> EquatableArray
    // element -> a property on that element) without risking a runaway walk over an unrelated deep type (BCL
    // collections, Roslyn-adjacent types reachable off some unrelated property, etc).
    private const int MaxWalkDepth = 4;

    /// <summary>
    ///     Recursively inspects <paramref name="value" /> — and, up to <see cref="MaxWalkDepth" /> levels, its
    ///     public instance properties, fields, and (for anything enumerable, including EquatableArray-wrapped
    ///     collections) elements — for an <see cref="ISymbol" />, <see cref="SyntaxNode" />, <see cref="Location" />,
    ///     or <see cref="Compilation" />. A direct type match on the step's OWN output value only catches the
    ///     degenerate case where the output IS one of those types; the realistic regression is a hand-written
    ///     model record gaining a property of one of those types, which this walk is what actually catches.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "A property getter here is arbitrary third-party/generator model code; any exception "
        + "it throws must be swallowed so this test helper fails for the leak it's checking, not for an "
        + "unrelated getter side effect.")]
    private static SymbolLeak? FindSymbolLeak(object? value, string path, int depth, HashSet<object> visited)
    {
        if (value is null) return null;

        if (value is ISymbol or SyntaxNode or Location or Compilation)
            return new SymbolLeak(path, value.GetType().Name);

        // Short-circuit primitives, strings and enums: never the leak shape, and walking their members would
        // just burn depth budget for nothing.
        if (value is string) return null;

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is decimal) return null;

        if (depth >= MaxWalkDepth) return null;

        // Reference-equality cycle guard: a model that (accidentally or by design) references an ancestor
        // must not send this walk into an infinite loop or a stack overflow.
        if (!visited.Add(value)) return null;

        if (value is IEnumerable enumerable)
        {
            var index = 0;
            foreach (var item in enumerable)
            {
                var elementLeak = FindSymbolLeak(item, $"{path}[{index}]", depth + 1, visited);
                if (elementLeak is not null) return elementLeak;
                index++;
            }

            return null;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Indexers (this[...]) have no meaningful single "value" to walk and no stable path segment.
            if (property.GetIndexParameters().Length > 0) continue;

            object? propertyValue;
            try
            {
                propertyValue = property.GetValue(value);
            }
            catch
            {
                // A getter throwing (e.g. a property that is only valid in some state) is not this helper's
                // problem to diagnose — skip it rather than fail the cacheability check for an unrelated reason.
                continue;
            }

            var propertyLeak = FindSymbolLeak(propertyValue, $"{path}.{property.Name}", depth + 1, visited);
            if (propertyLeak is not null) return propertyLeak;
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object? fieldValue;
            try
            {
                fieldValue = field.GetValue(value);
            }
            catch
            {
                continue;
            }

            var fieldLeak = FindSymbolLeak(fieldValue, $"{path}.{field.Name}", depth + 1, visited);
            if (fieldLeak is not null) return fieldLeak;
        }

        return null;
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
