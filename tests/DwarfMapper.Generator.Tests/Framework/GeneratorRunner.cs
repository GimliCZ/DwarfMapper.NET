// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.Framework
{
    /// <summary>
    ///     Runs ANY <see cref="IIncrementalGenerator" />. Six sites previously hardcoded
    ///     <c>CSharpGeneratorDriver.Create(new DwarfGenerator())</c>, so adding the registry generator required a
    ///     bespoke harness method rather than passing the generator in. Reuses
    ///     <see cref="GeneratorTestHarness.BuildCompilation" /> because the metadata-reference set is cached there
    ///     and must stay single-sourced.
    /// </summary>
    internal static class GeneratorRunner
    {
        public static GeneratorRun Run(
            IIncrementalGenerator generator,
            string source,
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
            AssertNoGeneratorCrash(result);
            var outputs = result.Results
                .SelectMany(r => r.GeneratedSources)
                .ToImmutableDictionary(
                    g => g.HintName,
                    g => g.SourceText.ToString(),
                    StringComparer.Ordinal);

            return new GeneratorRun(diagnostics, outputs);
        }

        /// <summary>
        ///     Fails the test if any generator threw.
        /// </summary>
        /// <remarks>
        ///     Roslyn does not let a generator exception escape: it parks it on
        ///     <see cref="GeneratorRunResult.Exception" /> and downgrades it to a CS8785 WARNING. From the outside a
        ///     crash and a deliberate refusal are then indistinguishable — both produce no sources, and the build
        ///     still reports zero errors. Every assertion in this suite is written against sources or diagnostics,
        ///     so before this check existed the whole battery would go green while the generator was dying on every
        ///     input. That is exactly how an ArgumentOutOfRangeException in <c>LocationInfo.From</c> reached a
        ///     consumer and erased every generated map in their project.
        /// </remarks>
        public static void AssertNoGeneratorCrash(GeneratorDriverRunResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            foreach (var r in result.Results)
            {
                if (r.Exception is { } ex)
                {
                    throw new InvalidOperationException(
                        $"Generator '{r.Generator.GetGeneratorType().Name}' THREW {ex.GetType().Name}: {ex.Message}. " +
                        "A generator that throws contributes nothing to the compilation. " +
                        $"Stack: {ex.StackTrace}",
                        ex);
                }
            }
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
                new[]
                {
                    generator.AsSourceGenerator()
                },
                driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));
        }
    }
}
