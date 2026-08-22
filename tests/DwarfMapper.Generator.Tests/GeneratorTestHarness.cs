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
    private static readonly Lazy<MetadataReference[]> References = new(BuildReferences);

    /// <summary>
    ///     Loaded assemblies FIRST, then every remaining assembly the host could load, from the runtime's
    ///     trusted-platform-assemblies list.
    ///     <para>
    ///     The sweep alone is <b>environment-dependent</b>: <see cref="AppDomain.CurrentDomain" />'s assembly
    ///     list contains only what something has already touched, so whether a fixture compiles depended on
    ///     which tests ran first and on which runner hosted them — the same source passed under
    ///     <c>dotnet test</c> and failed in an IDE, reported as a generator bug rather than a harness one.
    ///     It had already been patched three times, once per victim: <c>System.Linq.Queryable</c>,
    ///     <c>System.Collections.Specialized</c> (see the CS1069 note below), and finally
    ///     <c>EnumMemberAttribute</c> in <c>System.Runtime.Serialization.Primitives</c>, whose absence
    ///     surfaced as CS0246 inside <c>EnumSerializedNameTests</c>. A hand-kept list that grows by one entry
    ///     each time it bites someone is an allowlist; TPA is the whole set, so the class is closed rather
    ///     than its latest instance.
    ///     </para>
    ///     <para>
    ///     STRICTLY ADDITIVE, deliberately: the loaded set is taken first and TPA only contributes simple
    ///     names it does not already carry, so this can add a reference that was missing but can never
    ///     substitute a different build of one that already worked.
    ///     </para>
    ///     <para>
    ///     Built ONCE and reused. <see cref="MetadataReference.CreateFromFile(string)" /> reads metadata from
    ///     disk under a lock — rebuilding per call serialised parallel compilations on metadata I/O and
    ///     dominated wall-clock (the full power-set fuzz was contention-bound, not CPU-bound). The instances
    ///     are immutable and thread-safe to share.
    ///     </para>
    /// </summary>
    /// <summary>
    ///     Framework assemblies fixture sources name but that the host does not necessarily load. A
    ///     REQUIREMENT list, not an allowlist — an entry states what must be reachable, so adding one
    ///     tightens the contract. <c>HarnessReferenceSetTests</c> holds it to that.
    /// </summary>
    internal static readonly string[] RequiredFrameworkAssemblies =
    [
        "System.Runtime.Serialization.Primitives", // EnumMemberAttribute — EnumSerializedNameTests
        "System.Linq.Queryable",                   // IQueryable projections
        "System.Collections.Specialized",          // NameValueCollection / OrderedDictionary
        "System.ComponentModel.Primitives",        // DescriptionAttribute, used by enum-name fixtures
        "System.Text.Json"                         // JsonPropertyName, same family
    ];

    private static MetadataReference[] BuildReferences()
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Offer(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Length != 0 && !byName.ContainsKey(name)) byName[name] = path;
        }

        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic))
            Offer(loaded.Location);

        // The four the fixtures reach for by type rather than by name; they are ordinarily in TPA too, and are
        // kept explicit so a packaging change that drops one fails here instead of somewhere less legible.
        Offer(typeof(DwarfMapperAttribute).Assembly.Location);
        Offer(typeof(RoundTrip).Assembly.Location);
        Offer(typeof(Queryable).Assembly.Location);
        Offer(typeof(IServiceCollection).Assembly.Location);

        // System.Collections.Specialized is type-forwarded and not loaded by default, so a test source that
        // names NameValueCollection/OrderedDictionary would otherwise hit CS1069 (missing reference) rather
        // than exercising the generator.
        Offer(typeof(System.Collections.Specialized.NameValueCollection).Assembly.Location);

        // Resolved from the runtime's trusted-platform list, which enumerates every assembly the host COULD
        // load rather than only what it has touched. Restricted to the names fixtures actually need, and that
        // restriction is a measurement, not caution: offering the whole TPA set closes the class outright but
        // takes this project from 62s to 83s (~34%), because every one of the thousands of compilations then
        // binds against ~200 references instead of ~50. That is four times the ~10% fast-tier growth cap.
        // HarnessReferenceSetTests turns the residual risk into a loud, named failure instead of a CS0246 the
        // next reader blames on the generator; the cheap way to close the class properly is to retry a failed
        // compilation once against the full TPA set and report what it needed (recorded, not built).
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
            foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                if (RequiredFrameworkAssemblies.Contains(Path.GetFileNameWithoutExtension(path)))
                    Offer(path);

        return byName.Values.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();
    }

    /// <summary>
    ///     The reference set the compilations run against. Exposed so a self-validation test can assert that
    ///     it is complete independently of what the host happens to have loaded.
    /// </summary>
    internal static IReadOnlyList<MetadataReference> ReferenceSet => References.Value;

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
    ///     Every generated file, ordered by path and concatenated — including the assembly-wide aggregates
    ///     (<c>DwarfMapper.Extensions.g.cs</c>, the DI registration, the validation facade) that
    ///     <see cref="Run(string, NullableContextOptions)" /> deliberately drops so single-mapper snapshots
    ///     stay stable regardless of emit order.
    ///     <para>
    ///         The option-support matrix needs this. Measuring only the per-mapper file made every option
    ///         whose effect lands in an aggregate output invisible: <c>GenerateExtensions</c> read as having
    ///         no observable effect at ANY endpoint, which is a property of the instrument rather than of the
    ///         generator — and it would have been published as fact.
    ///     </para>
    ///     <para>
    ///         ALL means both shipped generators. The package contains two — <see cref="DwarfGenerator" /> and
    ///         the separate <see cref="DwarfMapper.Generator.Registry.MapToGenerator" /> — and a consumer's
    ///         build runs both, so a harness that runs one is not measuring the product. Driving only
    ///         <c>DwarfGenerator</c> made every <c>[MapTo]</c> source emit nothing at all, which the surface
    ///         matrix then read as "the element is SILENT at the registry endpoint" for all 122 of that
    ///         column's cells: the same instrument-not-generator confusion described above, one endpoint over.
    ///     </para>
    /// </summary>
    public static (ImmutableArray<Diagnostic> Diagnostics, string GeneratedSource) RunAll(string source,
        NullableContextOptions nullable = NullableContextOptions.Disable)
    {
        var compilation = BuildCompilation("DwarfMapperTestAsm", source, nullable);

        var driver = CSharpGeneratorDriver.Create(new DwarfGenerator(),
            new DwarfMapper.Generator.Registry.MapToGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics);

        var generated = string.Join("\n",
            output.SyntaxTrees
                .Where(t => t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
                .OrderBy(t => t.FilePath, StringComparer.Ordinal)
                .Select(t => t.ToString()));

        return (genDiagnostics, generated);
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

        // Both shipped generators, for the same reason as RunAll: a consumer's build runs both, and the
        // registry's emitted extension class is as much "generated code that must compile" as the mapper's.
        var driver = CSharpGeneratorDriver.Create(new DwarfGenerator(),
            new DwarfMapper.Generator.Registry.MapToGenerator());
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
            .Where(IsInGeneratedCode)
            .ToImmutableArray();
    }

    /// <summary>
    ///     Whether a compiler diagnostic is reported against code THIS GENERATOR WROTE, rather than against
    ///     the caller's own source.
    ///     <para>
    ///         The distinction is the difference between two opposite defects, so it is stated once and
    ///         shared rather than re-spelled per caller. A diagnostic in the user's source says the caller
    ///         wrote something the compiler rejects; a diagnostic in a <c>.g.cs</c> file says the generator
    ///         handed the consumer code that does not compile, in a file they never wrote and cannot fix.
    ///         <see cref="GeneratedCodeWarnings" /> has used this test since it was written;
    ///         <c>SurfaceProbe.Classify</c> is the second caller, and a second copy of the test is exactly
    ///         how the two would come to disagree about what "generated" means.
    ///     </para>
    ///     <para>
    ///         Keyed on the <c>.g.cs</c> suffix because that is what every hint name this package emits ends
    ///         in, and the hand-written trees the harness builds carry no path at all. A location with no
    ///         source tree (a compilation-level diagnostic) is NOT generated code: it belongs to no file, and
    ///         attributing it to the generator would be a guess.
    ///     </para>
    /// </summary>
    public static bool IsInGeneratedCode(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return diagnostic.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) == true;
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
