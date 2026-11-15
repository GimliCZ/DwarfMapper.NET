// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using DwarfMapper.Generator.Roslyn512.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.Framework
{
    /// <summary>
    ///     The golden runner, for the Roslyn 5.12 build. Same name, signature and result as Generator.Tests' runner, so
    ///     the LINKED <c>GoldenFingerprint</c> compiles against it unchanged; the compilation it builds is that runner's
    ///     too (the same assembly name, options and reference set), with one deliberate difference: the source is parsed
    ///     as C# 14, which is what Roslyn 5.0's default parse options mean. Roslyn 5.12's default is C# 15, and comparing
    ///     two languages would not isolate the two builds.
    /// </summary>
    internal static class GeneratorRunner
    {
        public static GeneratorRun Run(
            IIncrementalGenerator generator,
            string source,
            NullableContextOptions nullable = NullableContextOptions.Disable)
        {
            ArgumentNullException.ThrowIfNull(generator);

            var parse = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14);
            var compilation = Roslyn512Harness.BuildCompilation(
                "DwarfMapperRunnerAsm",
                new[]
                {
                    CSharpSyntaxTree.ParseText(source, parse)
                },
                nullable);
            var driver = CSharpGeneratorDriver.Create(
                new[]
                {
                    generator.AsSourceGenerator()
                },
                parseOptions: parse);
            var ran = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

            var result = ran.GetRunResult();
            Roslyn512Harness.AssertNoGeneratorCrash(result);
            var outputs = result.Results
                .SelectMany(r => r.GeneratedSources)
                .ToImmutableDictionary(
                    g => g.HintName,
                    g => g.SourceText.ToString(),
                    StringComparer.Ordinal);

            return new GeneratorRun(diagnostics, outputs);
        }
    }
}
