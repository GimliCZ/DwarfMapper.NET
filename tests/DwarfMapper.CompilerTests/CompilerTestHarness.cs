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
        return RunCore(units, "TypeGraphAsm").Result;
    }

    /// <summary>
    ///     K1's execution seam: <see cref="Run" /> plus an in-memory emit and load of the final compilation,
    ///     so the differential oracle can EXECUTE the generated map. The assembly is null exactly when the
    ///     run refused loudly or the output had compilation errors (nothing sound to execute either way).
    ///     Each call emits under a unique assembly name: every loaded graph declares the same
    ///     <c>T.S0</c>/<c>T.D0</c>… type names, and distinct assembly identities keep the runtime (and the
    ///     ambient registry's module-init self-registration, which keys on <see cref="Type" /> instances)
    ///     from ever conflating two samples. Default ALC on purpose — registry registration roots the
    ///     assembly anyway, so a collectible ALC would only pretend to unload.
    /// </summary>
    public static (RunResult Result, System.Reflection.Assembly? Assembly) RunAndEmit(IReadOnlyList<string> units)
    {
        var (result, output) = RunCore(
            units,
            FormattableString.Invariant($"TypeGraphAsm_{Guid.NewGuid():N}"));
        if (result.RefusedLoudly || result.CompilationErrors.Length > 0) return (result, null);

        using var pe = new MemoryStream();
        var emit = output.Emit(pe);
        if (!emit.Success)
        {
            // Errors-free per the check above, so a failed emit is emit-only diagnostics — surface them
            // as compilation errors rather than silently returning a null assembly (loud, per H7 spirit).
            return (result with
            {
                CompilationErrors = emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .ToImmutableArray()
            }, null);
        }

        return (result, System.Reflection.Assembly.Load(pe.ToArray()));
    }

    /// <summary>
    ///     Instantiates the rendered mapper class <c>T.M</c> from an emitted graph assembly and invokes its
    ///     one-parameter <c>Map</c> with <paramref name="source" />. Reflection is test-side only (the
    ///     audit's stated boundary: the house no-reflection stance governs the shipped product, not test
    ///     oracles). A <see cref="System.Reflection.TargetInvocationException" /> is unwrapped so the map's
    ///     own exception (a runtime-behaviour fact about the product) reaches the assertion undisguised.
    /// </summary>
    public static object? InvokeMap(System.Reflection.Assembly assembly, object source)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var mapper = assembly.GetType("T.M")
                     ?? throw new InvalidOperationException("emitted assembly has no mapper type 'T.M'");
        var map = mapper.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                      .SingleOrDefault(m => m.Name == "Map" && m.GetParameters().Length == 1)
                  ?? throw new InvalidOperationException("mapper type 'T.M' has no one-parameter Map method");
        var instance = Activator.CreateInstance(mapper)
                       ?? throw new InvalidOperationException("mapper type 'T.M' could not be instantiated");
        try
        {
            return map.Invoke(instance, [source]);
        }
        catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
        {
            throw tie.InnerException;
        }
    }

    private static (RunResult Result, CSharpCompilation Output) RunCore(
        IReadOnlyList<string> units, string assemblyName)
    {
        var trees = units.Select(u => CSharpSyntaxTree.ParseText(u)).ToArray();
        var compilation = CSharpCompilation.Create(
            assemblyName,
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

        return (new RunResult(generatorDiagnostics, errors, generated), (CSharpCompilation)output);
    }
}
