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
            // System.Collections.Specialized is type-forwarded and not loaded by default, so a test source that
            // names NameValueCollection/OrderedDictionary would otherwise hit CS1069 (missing reference) rather
            // than exercising the generator. Reference it explicitly so those legacy System.* members compile.
            .Append(MetadataReference.CreateFromFile(
                typeof(System.Collections.Specialized.NameValueCollection).Assembly.Location))
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
    ///     Runs the <c>[MapTo]</c> registry generator (a SEPARATE <see cref="IIncrementalGenerator" /> from
    ///     <see cref="DwarfGenerator" />, so the default <see cref="Run(string, NullableContextOptions)" /> never
    ///     exercises it) and returns its diagnostics. The registry's whole error surface — the
    ///     <c>DWARFR01</c>–<c>DWARFR06</c> family — is only reachable through this driver.
    /// </summary>
    public static ImmutableArray<Diagnostic> RunMapTo(string source,
        NullableContextOptions nullable = NullableContextOptions.Disable)
    {
        return RunMapToWithSource(source, nullable).Diagnostics;
    }

    /// <summary>
    ///     As <see cref="RunMapTo" />, but also returns the generated registry extension source — needed to
    ///     assert on what the registry DID emit (e.g. that an unassignable member is not assigned at all).
    /// </summary>
    public static (ImmutableArray<Diagnostic> Diagnostics, string GeneratedSource) RunMapToWithSource(
        string source, NullableContextOptions nullable = NullableContextOptions.Disable)
    {
        var compilation = BuildCompilation("DwarfMapperMapToTestAsm", source, nullable);

        var driver = CSharpGeneratorDriver.Create(new DwarfMapper.Generator.Registry.MapToGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics);

        var generated = output.SyntaxTrees
            .Where(t => t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
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
        NullableContextOptions nullable = NullableContextOptions.Disable,
        string assemblyName = "DwarfMapperTestAsm")
    {
        var compilation = BuildCompilation(assemblyName, source, nullable);

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
    ///     Runs the generator and returns every C# compiler diagnostic of WARNING severity or worse whose
    ///     location lies inside a GENERATED file. Empty => the generated code is clean in the consumer's build.
    /// <para>
    ///     <see cref="RunAndGetCompilationErrors" /> filters to <see cref="DiagnosticSeverity.Error" />, which
    ///     made a whole class of defect invisible: generated code that merely WARNS. That is not a lesser
    ///     problem — this repo's own Directory.Build.props sets <c>TreatWarningsAsErrors</c>, as do many
    ///     consumers, so a warning emitted from a <c>.g.cs</c> file is a hard build break in code the user
    ///     cannot edit and cannot fix. (A nullable-reference raw assign emitted exactly such a CS8601; see
    ///     DWARF070.) Diagnostics originating in the USER's own source are excluded — the schemas and test
    ///     inputs are allowed to be sloppy; only what the generator itself emits is held to this bar.
    /// </para>
    /// </summary>
    public static ImmutableArray<Diagnostic> GeneratedCodeWarnings(string source,
        NullableContextOptions nullable = NullableContextOptions.Enable)
    {
        var compilation = BuildCompilation("DwarfMapperWarnTestAsm", source, nullable);

        var driver = CSharpGeneratorDriver.Create(new DwarfGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        return outputCompilation.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Where(d => d.Id.StartsWith("CS", StringComparison.Ordinal))
            .Where(d => d.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) == true)
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
