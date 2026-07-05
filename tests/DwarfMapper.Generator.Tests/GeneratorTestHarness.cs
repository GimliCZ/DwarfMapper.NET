// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Reflection;
using DwarfMapper.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace DwarfMapper.Generator.Tests;

internal static class GeneratorTestHarness
{
    /// <summary>
    ///     The metadata reference set, built ONCE and reused across every compilation. Each
    ///     <see cref="MetadataReference.CreateFromFile(string)" /> reads assembly metadata from disk under a
    ///     lock — rebuilding it per call (~50 references) serialised parallel compilations on metadata I/O and
    ///     dominated wall-clock (the full power-set fuzz was contention-bound, not CPU-bound). MetadataReference
    ///     instances are immutable and thread-safe to share, so a single cached array is both correct and far
    ///     faster for the whole generator-test suite.
    /// </summary>
    private static readonly Lazy<MetadataReference[]> References = new(() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .Append(MetadataReference.CreateFromFile(typeof(DwarfMapperAttribute).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(RoundTrip).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Queryable).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location))
            .ToArray());

    public static (ImmutableArray<Diagnostic> Diagnostics, string GeneratedSource) Run(string source,
        NullableContextOptions nullable = NullableContextOptions.Disable)
    {
        var compilation = BuildCompilation("DwarfMapperTestAsm", source, nullable);

        var driver = CSharpGeneratorDriver.Create(new DwarfGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics);

        var generated = output.SyntaxTrees
            .Where(t => t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
            // Exclude the assembly-wide aggregate outputs (convenience facade + DI registration) so single-
            // mapper snapshot tests keep selecting the per-mapper file regardless of emit order.
            .Where(t => !t.FilePath.EndsWith("DwarfMapper.Extensions.g.cs", StringComparison.Ordinal)
                        && !t.FilePath.EndsWith("DwarfMapper.ServiceCollectionExtensions.g.cs",
                            StringComparison.Ordinal)
                        && !t.FilePath.EndsWith("DwarfMapper.AmbientRegistration.g.cs", StringComparison.Ordinal)
                        && !t.FilePath.EndsWith("DwarfMapper.AmbientRequires.g.cs", StringComparison.Ordinal)
                        && !t.FilePath.EndsWith("DwarfMapper.Validate.g.cs", StringComparison.Ordinal))
            .Select(t => t.ToString())
            .FirstOrDefault() ?? string.Empty;

        return (genDiagnostics, generated);
    }

    /// <summary>
    ///     Runs the generator and returns the text of the single generated file whose hint name ends with
    ///     <paramref name="hintNameSuffix" /> (e.g. <c>"DwarfMapper.Extensions.g.cs"</c>), or an empty string if
    ///     no such file was produced. Used to assert on the assembly-wide aggregate outputs.
    /// </summary>
    public static string RunAndGetSource(string source, string hintNameSuffix,
        NullableContextOptions nullable = NullableContextOptions.Disable)
    {
        var compilation = BuildCompilation("DwarfMapperTestAsm", source, nullable);

        var driver = CSharpGeneratorDriver.Create(new DwarfGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        return output.SyntaxTrees
            .Where(t => t.FilePath.EndsWith(hintNameSuffix, StringComparison.Ordinal))
            .Select(t => t.ToString())
            .FirstOrDefault() ?? string.Empty;
    }

    /// <summary>
    ///     Runs the generator and returns the C# ERROR diagnostics of the final
    ///     compilation (original source + generated sources). Empty => generated code compiles.
    /// </summary>
    public static ImmutableArray<Diagnostic> RunAndGetCompilationErrors(string source,
        NullableContextOptions nullable = NullableContextOptions.Disable)
    {
        var compilation = BuildCompilation("DwarfMapperCompileTestAsm", source, nullable);

        var driver = CSharpGeneratorDriver.Create(new DwarfGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        return outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
    }

    /// <summary>
    ///     Runs the generator and emits the resulting compilation to an in-memory assembly.
    ///     Returns the loaded assembly (or null on emit failure) and any emit-time error diagnostics.
    ///     Each call uses a unique assembly name to avoid load collisions across seeds.
    /// </summary>
    public static (Assembly? Assembly, ImmutableArray<Diagnostic> Errors) EmitAssembly(string source)
    {
        var asmName = "FuzzAsm_" + Guid.NewGuid().ToString("N");
        var compilation = BuildCompilation(asmName, source);

        var driver = CSharpGeneratorDriver.Create(new DwarfGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        using var ms = new MemoryStream();
        var result = outputCompilation.Emit(ms);

        if (result.Success)
        {
            var asm = Assembly.Load(ms.ToArray());
            return (asm, ImmutableArray<Diagnostic>.Empty);
        }

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        return (null, errors);
    }

    // ── Shared compilation builder ────────────────────────────────────────────

    public static CSharpCompilation BuildCompilation(string assemblyName, string source,
        NullableContextOptions nullable = NullableContextOptions.Disable)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: nullable));
    }
}
