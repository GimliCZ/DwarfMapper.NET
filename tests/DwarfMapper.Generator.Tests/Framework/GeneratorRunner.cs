// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>
///     Runs ANY <see cref="IIncrementalGenerator" />. Six sites previously hardcoded
///     <c>CSharpGeneratorDriver.Create(new DwarfGenerator())</c>, so adding the registry generator required a
///     bespoke harness method rather than passing the generator in. Reuses
///     <see cref="GeneratorTestHarness.BuildCompilation" /> because the metadata-reference set is cached there
///     and must stay single-sourced.
/// </summary>
internal static class GeneratorRunner
{
    public static GeneratorRun Run(IIncrementalGenerator generator, string source,
        NullableContextOptions nullable = NullableContextOptions.Disable)
    {
        ArgumentNullException.ThrowIfNull(generator);

        var compilation = GeneratorTestHarness.BuildCompilation("DwarfMapperRunnerAsm", source, nullable);
        var driver = CSharpGeneratorDriver.Create(generator);
        var ranDriver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        // Take outputs from the run result (HintName + SourceText) rather than filtering
        // syntax-tree paths: Roslyn derives a tree's path from the hint name plus ".cs", not
        // ".g.cs", so any emitter whose hint name doesn't already end in ".g" was silently
        // dropped from every golden fingerprint under the old path-filter approach. Note that
        // RunGeneratorsAndUpdateCompilation returns the UPDATED driver (GeneratorDriver is immutable) —
        // GetRunResult must be called on that return value, not on the pre-run "driver".
        var result = ranDriver.GetRunResult();
        var outputs = result.Results
            .SelectMany(r => r.GeneratedSources)
            .ToImmutableDictionary(
                g => g.HintName,
                g => g.SourceText.ToString(),
                StringComparer.Ordinal);

        return new GeneratorRun(diagnostics, outputs);
    }

    /// <summary>
    ///     A driver with step tracking enabled, for the cacheability battery. Takes no Compilation on purpose —
    ///     the caller drives it with RunGenerators(compilation), and an unused parameter would fail the build
    ///     here (IDE0060 under AnalysisMode=All).
    /// </summary>
    public static GeneratorDriver RunTracked(IIncrementalGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(generator);

        return CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));
    }
}
