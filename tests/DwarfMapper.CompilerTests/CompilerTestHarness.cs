// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using DwarfMapper.Generator;
using DwarfMapper.Generator.Registry;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.CompilerTests;

/// <summary>
///     The one compile seam of this project: run BOTH shipped generators over rendered units and report
///     what a consumer's build would see.
///     <para>
///         Both generators, for the reason <c>GeneratorTestHarness.RunAndGetCompilationErrors</c> states —
///         a consumer's build runs <see cref="DwarfGenerator" /> AND <see cref="MapToGenerator" />, so a
///         harness that runs one is not measuring the product. Unlike that harness this one runs the driver
///         ONCE per call and reads generator diagnostics and output-compilation errors from the same run:
///         at fuzz sample counts a second generator pass per sample is pure wall-clock.
///     </para>
/// </summary>
internal static class CompilerTestHarness
{
    /// <summary>The outcome of one generator-plus-compile run.</summary>
    /// <param name="GeneratorDiagnostics">Everything the generators reported (DWARF/DWARFR ids).</param>
    /// <param name="CompilationErrors">CS errors of the final compilation (user source + generated).</param>
    /// <param name="GeneratedSource">All generated files, ordered by hint path and concatenated.</param>
    public sealed record RunResult(
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        ImmutableArray<Diagnostic> CompilationErrors,
        string GeneratedSource)
    {
        /// <summary>A loud refusal — an error-severity diagnostic from a generator — is a VALID outcome.</summary>
        public bool RefusedLoudly => GeneratorDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    ///     The metadata reference set, built once and shared — MetadataReference instances are immutable and
    ///     thread-safe, and rebuilding ~50 of them per sample serialises parallel compilations on metadata
    ///     I/O (measured in the Generator.Tests harness; same rationale, same shape).
    /// </summary>
    private static readonly Lazy<MetadataReference[]> References = new(() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .Append(MetadataReference.CreateFromFile(typeof(DwarfMapperAttribute).Assembly.Location))
            .ToArray());

    /// <summary>Runs both generators over the given compilation units (nullable disabled, house default).</summary>
    public static RunResult Run(IReadOnlyList<string> units)
    {
        var trees = units.Select(u => CSharpSyntaxTree.ParseText(u)).ToArray();
        var compilation = CSharpCompilation.Create(
            "TypeGraphAsm",
            trees,
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new DwarfGenerator(), new MapToGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        var errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        var generated = string.Join("\n",
            output.SyntaxTrees
                .Where(t => t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
                .OrderBy(t => t.FilePath, StringComparer.Ordinal)
                .Select(t => t.ToString()));

        return new RunResult(generatorDiagnostics, errors, generated);
    }
}
