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
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var outputs = output.SyntaxTrees
            .Where(t => t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
            .ToImmutableDictionary(
                t => Path.GetFileName(t.FilePath),
                t => t.ToString(),
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
