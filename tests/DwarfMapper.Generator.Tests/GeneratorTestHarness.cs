// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Globalization;
using System.Reflection;
using DwarfMapper.Generator.Registry;
using DwarfMapper.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace DwarfMapper.Generator.Tests
{
    internal static class GeneratorTestHarness
    {
        /// <summary>
        ///     The metadata reference set, built ONCE and reused across every compilation. Each
        ///     <see cref="MetadataReference.CreateFromFile(string, MetadataReferenceProperties, DocumentationProvider)" /> reads assembly metadata from disk under a
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
        ///         The sweep alone is <b>environment-dependent</b>: <see cref="AppDomain.CurrentDomain" />'s assembly
        ///         list contains only what something has already touched, so whether a fixture compiles depended on
        ///         which tests ran first and on which runner hosted them — the same source passed under
        ///         <c>dotnet test</c> and failed in an IDE, reported as a generator bug rather than a harness one.
        ///         It had already been patched three times, once per victim: <c>System.Linq.Queryable</c>,
        ///         <c>System.Collections.Specialized</c> (see the CS1069 note below), and finally
        ///         <c>EnumMemberAttribute</c> in <c>System.Runtime.Serialization.Primitives</c>, whose absence
        ///         surfaced as CS0246 inside <c>EnumSerializedNameTests</c>. A hand-kept list that grows by one entry
        ///         each time it bites someone is an allowlist; TPA is the whole set, so the class is closed rather
        ///         than its latest instance.
        ///     </para>
        ///     <para>
        ///         STRICTLY ADDITIVE, deliberately: the loaded set is taken first and TPA only contributes simple
        ///         names it does not already carry, so this can add a reference that was missing but can never
        ///         substitute a different build of one that already worked.
        ///     </para>
        ///     <para>
        ///         Built ONCE and reused. <see cref="MetadataReference.CreateFromFile(string, MetadataReferenceProperties, DocumentationProvider)" /> reads metadata from
        ///         disk under a lock — rebuilding per call serialised parallel compilations on metadata I/O and
        ///         dominated wall-clock (the full power-set fuzz was contention-bound, not CPU-bound). The instances
        ///         are immutable and thread-safe to share.
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
            "System.Linq.Queryable", // IQueryable projections
            "System.Collections.Specialized", // NameValueCollection / OrderedDictionary
            "System.ComponentModel.Primitives", // DescriptionAttribute, used by enum-name fixtures
            "System.Text.Json" // JsonPropertyName, same family
        ];

        /// <summary>
        ///     The reference set the compilations run against. Exposed so a self-validation test can assert that
        ///     it is complete independently of what the host happens to have loaded.
        /// </summary>
        internal static IReadOnlyList<MetadataReference> ReferenceSet => References.Value;

        private static MetadataReference[] BuildReferences()
        {
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            void Offer(string? path)
            {
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                var name = Path.GetFileNameWithoutExtension(path);
                if (name.Length != 0 && !byName.ContainsKey(name))
                {
                    byName[name] = path;
                }
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
            Offer(typeof(NameValueCollection).Assembly.Location);

            // Resolved from the runtime's trusted-platform list, which enumerates every assembly the host COULD
            // load rather than only what it has touched. Restricted to the names fixtures actually need, and that
            // restriction is a measurement, not caution: offering the whole TPA set closes the class outright but
            // takes this project from 62s to 83s (~34%), because every one of the thousands of compilations then
            // binds against ~200 references instead of ~50. That is four times the ~10% fast-tier growth cap.
            // HarnessReferenceSetTests turns the residual risk into a loud, named failure instead of a CS0246 the
            // next reader blames on the generator; the cheap way to close the class properly is to retry a failed
            // compilation once against the full TPA set and report what it needed (recorded, not built).
            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
            {
                foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                    if (RequiredFrameworkAssemblies.Contains(Path.GetFileNameWithoutExtension(path)))
                    {
                        Offer(path);
                    }
            }

            return byName.Values.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();
        }

        public static (ImmutableArray<Diagnostic> Diagnostics, string GeneratedSource) Run(
            string source,
            NullableContextOptions nullable = NullableContextOptions.Disable,
            bool allowUnsafe = false)
        {
            var compilation = BuildCompilation("DwarfMapperTestAsm", source, nullable, allowUnsafe);

            var driver = CSharpGeneratorDriver.Create(new DwarfGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics);
            AssertGeneratedSourceParses(output);

            var generated = output.SyntaxTrees
                                .Where(t => t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
                                // Exclude the assembly-wide aggregate outputs (convenience facade + DI registration) so single-
                                // mapper snapshot tests keep selecting the per-mapper file regardless of emit order.
                                .Where(t => !t.FilePath.EndsWith("DwarfMapper.Extensions.g.cs", StringComparison.Ordinal) &&
                                            !t.FilePath.EndsWith("DwarfMapper.ServiceCollectionExtensions.g.cs",
                                                StringComparison.Ordinal) &&
                                            !t.FilePath.EndsWith("DwarfMapper.AmbientRegistration.g.cs", StringComparison.Ordinal) &&
                                            !t.FilePath.EndsWith("DwarfMapper.AmbientRequires.g.cs", StringComparison.Ordinal) &&
                                            !t.FilePath.EndsWith("DwarfMapper.Validate.g.cs", StringComparison.Ordinal))
                                .Select(t => t.ToString())
                                .FirstOrDefault() ??
                            string.Empty;

            return (genDiagnostics, generated);
        }

        /// <summary>
        ///     Runs the <c>[MapTo]</c> registry generator (a SEPARATE <see cref="IIncrementalGenerator" /> from
        ///     <see cref="DwarfGenerator" />, so the default <see cref="Run(string, NullableContextOptions, bool)" /> never
        ///     exercises it) and returns its diagnostics. The registry's whole error surface — the
        ///     <c>DWARFR01</c>–<c>DWARFR06</c> family — is only reachable through this driver.
        /// </summary>
        public static ImmutableArray<Diagnostic> RunMapTo(
            string source,
            NullableContextOptions nullable = NullableContextOptions.Disable)
        {
            return RunMapToWithSource(source, nullable).Diagnostics;
        }

        /// <summary>
        ///     Every generated file, ordered by path and concatenated — including the assembly-wide aggregates
        ///     (<c>DwarfMapper.Extensions.g.cs</c>, the DI registration, the validation facade) that
        ///     <see cref="Run(string, NullableContextOptions, bool)" /> deliberately drops so single-mapper snapshots
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
        public static (ImmutableArray<Diagnostic> Diagnostics, string GeneratedSource) RunAll(
            string source,
            NullableContextOptions nullable = NullableContextOptions.Disable)
        {
            var compilation = BuildCompilation("DwarfMapperTestAsm", source, nullable);

            var driver = CSharpGeneratorDriver.Create(new DwarfGenerator(),
                new MapToGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics);
            AssertGeneratedSourceParses(output);

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
            string source,
            NullableContextOptions nullable = NullableContextOptions.Disable)
        {
            var compilation = BuildCompilation("DwarfMapperMapToTestAsm", source, nullable);

            var driver = CSharpGeneratorDriver.Create(new MapToGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics);
            AssertGeneratedSourceParses(output);

            var generated = output.SyntaxTrees
                                .Where(t => t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
                                .Select(t => t.ToString())
                                .FirstOrDefault() ??
                            string.Empty;

            return (genDiagnostics, generated);
        }

        /// <summary>
        ///     Runs the generator and returns the text of the single generated file whose hint name ends with
        ///     <paramref name="hintNameSuffix" /> (e.g. <c>"DwarfMapper.Extensions.g.cs"</c>), or an empty string if
        ///     no such file was produced. Used to assert on the assembly-wide aggregate outputs.
        /// </summary>
        public static string RunAndGetSource(
            string source,
            string hintNameSuffix,
            NullableContextOptions nullable = NullableContextOptions.Disable,
            string assemblyName = "DwarfMapperTestAsm")
        {
            var compilation = BuildCompilation(assemblyName, source, nullable);

            var driver = CSharpGeneratorDriver.Create(new DwarfGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
            AssertGeneratedSourceParses(output);

            return output.SyntaxTrees
                       .Where(t => t.FilePath.EndsWith(hintNameSuffix, StringComparison.Ordinal))
                       .Select(t => t.ToString())
                       .FirstOrDefault() ??
                   string.Empty;
        }

        /// <summary>
        ///     Runs the generator and returns the C# ERROR diagnostics of the final
        ///     compilation (original source + generated sources). Empty => generated code compiles.
        /// </summary>
        public static ImmutableArray<Diagnostic> RunAndGetCompilationErrors(
            string source,
            NullableContextOptions nullable = NullableContextOptions.Disable)
        {
            var compilation = BuildCompilation("DwarfMapperCompileTestAsm", source, nullable);

            // Both shipped generators, for the same reason as RunAll: a consumer's build runs both, and the
            // registry's emitted extension class is as much "generated code that must compile" as the mapper's.
            var driver = CSharpGeneratorDriver.Create(new DwarfGenerator(),
                new MapToGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
            AssertGeneratedSourceParses(outputCompilation);

            return outputCompilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();
        }

        /// <summary>
        ///     Runs the generator and returns every C# compiler diagnostic of WARNING severity or worse whose
        ///     location lies inside a GENERATED file. Empty => the generated code is clean in the consumer's build.
        ///     <para>
        ///         <see cref="RunAndGetCompilationErrors" /> filters to <see cref="DiagnosticSeverity.Error" />, which
        ///         made a whole class of defect invisible: generated code that merely WARNS. That is not a lesser
        ///         problem — this repo's own Directory.Build.props sets <c>TreatWarningsAsErrors</c>, as do many
        ///         consumers, so a warning emitted from a <c>.g.cs</c> file is a hard build break in code the user
        ///         cannot edit and cannot fix. (A nullable-reference raw assign emitted exactly such a CS8601; see
        ///         DWARF070.) Diagnostics originating in the USER's own source are excluded — the schemas and test
        ///         inputs are allowed to be sloppy; only what the generator itself emits is held to this bar.
        ///     </para>
        /// </summary>
        public static ImmutableArray<Diagnostic> GeneratedCodeWarnings(
            string source,
            NullableContextOptions nullable = NullableContextOptions.Enable,
            bool includeRegistry = false)
        {
            var compilation = BuildCompilation("DwarfMapperWarnTestAsm", source, nullable);

            // The registry generator ([MapTo]) has its own element loop and object helpers, so a shape that is
            // clean through the class model can still warn through the registry; opt in per test rather than
            // always, so the combinatorial cells keep measuring exactly the generator they were written against.
            var driver = includeRegistry
                ? CSharpGeneratorDriver.Create(new DwarfGenerator(), new MapToGenerator())
                : CSharpGeneratorDriver.Create(new DwarfGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
            AssertGeneratedSourceParses(outputCompilation);

            return outputCompilation.GetDiagnostics()
                .Where(d => d.Severity >= DiagnosticSeverity.Warning)
                .Where(d => d.Id.StartsWith("CS", StringComparison.Ordinal))
                .Where(IsInGeneratedCode)
                .ToImmutableArray();
        }

        /// <summary>
        ///     THE SELF-PARSE INVARIANT: nothing this generator writes may fail to PARSE.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Every one of the 25 broken naming positions, the <c>int class</c> defect of <c>b888cc3</c>, and
        ///         both withdrawn-view defects was a SYNTAX failure. So the invariant does not care which escape
        ///         was forgotten, only whether the result is valid C# — which is close to the strongest guarantee
        ///         available to a tool that writes files the consumer cannot edit.
        ///     </para>
        ///     <para>
        ///         <b>Why it lives here and not in the generator, with the number.</b> Measured on this machine
        ///         (2026-09-07, Release, three fixtures × 20 rounds, 4 generated files / 9,377 chars per round):
        ///         a bare driver run is <b>2.08 ms</b>; adding <c>ParseText + GetDiagnostics</c> over its output
        ///         costs a further <b>2.33 ms</b> — <b>112 % of the generator's own run time</b>, and <b>9.4 %</b>
        ///         of a full compilation (24.7 ms) of the same fixture. Reading the diagnostics off the driver's
        ///         OWN trees instead of re-parsing measured the same (116 %): the driver defers the parse, so
        ///         there is no already-paid-for tree to read cheaply.
        ///     </para>
        ///     <para>
        ///         That cost buys NO prevention in the generator, and this is the part worth being explicit
        ///         about: the consumer's compiler parses the emitted file microseconds later and reports the very
        ///         same syntax errors. An always-on check would change WHO reports them — one located DWARF
        ///         instead of 27 CS errors in an unowned file — which is a real improvement in message quality
        ///         and no improvement at all in what ships. What stops a broken emission from EVER reaching a
        ///         consumer is catching it here, in the corpus, before release. So the check is paid for once in
        ///         CI rather than on every consumer keystroke, and the doubling of generator time is refused.
        ///     </para>
        ///     <para>
        ///         <b>Its limit, stated so the guarantee is not over-read.</b> It catches syntax only. An
        ///         unescaped CONTEXTUAL keyword in type position — <c>record</c>, <c>partial</c>, <c>scoped</c> —
        ///         parses into the same tree as a good name and fails later, in the binder or as the CS8860
        ///         WARNING that <c>P16</c> measured. Rule 1 (escape at the model boundary,
        ///         <c>EmittedIdentifiersAreEscapedTests</c>) is what covers that family; this is not a substitute
        ///         for it.
        ///     </para>
        ///     <para>
        ///         Called from every method here that drives a generator, so its reach is the whole generator-test
        ///         corpus rather than a fixture list someone has to remember to extend.
        ///     </para>
        /// </remarks>
        public static void AssertGeneratedSourceParses(Compilation output)
        {
            ArgumentNullException.ThrowIfNull(output);

            List<string>? broken = null;
            foreach (var tree in output.SyntaxTrees)
            {
                if (!tree.FilePath.EndsWith(GeneratedFileSuffix, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var d in tree.GetDiagnostics())
                {
                    if (d.Severity != DiagnosticSeverity.Error)
                    {
                        continue;
                    }

                    broken ??= [];
                    if (broken.Count < MaxReportedSyntaxErrors)
                    {
                        var line = d.Location.GetLineSpan().StartLinePosition.Line + 1;
                        broken.Add($"{Path.GetFileName(tree.FilePath)}({line}): {d.Id} {d.GetMessage(CultureInfo.InvariantCulture)}");
                    }
                }
            }

            if (broken is null)
            {
                return;
            }

            Assert.Fail(
                "THE GENERATOR EMITTED C# THAT DOES NOT PARSE. This is the b888cc3 failure mode: the consumer " +
                "gets a pile of CS errors in a .g.cs file they never wrote and cannot edit, with no DwarfMapper " +
                "diagnostic connecting them to anything.\n\n  " +
                string.Join("\n  ", broken) +
                "\n\n--- generated ---\n" +
                string.Join("\n", output.SyntaxTrees
                    .Where(t => t.FilePath.EndsWith(GeneratedFileSuffix, StringComparison.Ordinal))
                    .Select(t => t.ToString())));
        }

        /// <summary>The suffix every hint name this generator emits under ends with.</summary>
        private const string GeneratedFileSuffix = ".g.cs";

        /// <summary>
        ///     How many syntax errors a failure message lists. One broken identifier cascades into dozens of
        ///     follow-on errors (b888cc3 produced 27), and the first few name the site; the rest are noise that
        ///     would bury the generated source printed underneath them.
        /// </summary>
        private const int MaxReportedSyntaxErrors = 12;

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
            return EmitAssembly(BuildCompilation("FuzzAsm_" + Guid.NewGuid().ToString("N"), source));
        }

        /// <summary>
        ///     <see cref="EmitAssembly(string)" /> for a compilation the caller built — several syntax trees in a
        ///     chosen order, unsafe code, a specific assembly name. The generator runs against exactly that
        ///     compilation, so what it saw is what the test controls.
        /// </summary>
        public static (Assembly? Assembly, ImmutableArray<Diagnostic> Errors) EmitAssembly(CSharpCompilation compilation)
        {
            var driver = CSharpGeneratorDriver.Create(new DwarfGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
            AssertGeneratedSourceParses(outputCompilation);

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

        public static CSharpCompilation BuildCompilation(
            string assemblyName,
            string source,
            NullableContextOptions nullable = NullableContextOptions.Disable,
            bool allowUnsafe = false)
        {
            return BuildCompilation(assemblyName,
                new[]
                {
                    CSharpSyntaxTree.ParseText(source)
                },
                nullable,
                allowUnsafe);
        }

        /// <summary>
        ///     The multi-tree form. Tree order is the order the compiler sees the files in, which is the order it
        ///     lays out a partial struct's fields in — a test that needs a particular file order states it here.
        /// </summary>
        public static CSharpCompilation BuildCompilation(
            string assemblyName,
            IReadOnlyList<SyntaxTree> trees,
            NullableContextOptions nullable = NullableContextOptions.Disable,
            bool allowUnsafe = false)
        {
            // Derive from an EMPTY baseline that already carries this exact (name, options, references) triple,
            // rather than calling CSharpCompilation.Create per test. Caching the MetadataReference objects
            // (References, above) stopped the disk reads; it did not stop Roslyn rebuilding a ReferenceManager
            // and re-binding every referenced assembly's symbols for each new compilation. AddSyntaxTrees on a
            // compilation whose name, options and references are unchanged reuses that bound state — which is
            // the whole cost for the shapes this suite compiles, since the sources are a few dozen lines and the
            // reference set is the framework. The key covers every input Create() was given, so the result is
            // the same compilation Create() would have produced: same identity, same options, same references,
            // and the trees in the order the caller listed them (the baseline holds none, so order is preserved
            // — the multi-tree overload's field-layout contract depends on that).
            return Baselines
                .GetOrAdd((assemblyName, nullable, allowUnsafe),
                    static k => CSharpCompilation.Create(
                        k.AssemblyName,
                        Array.Empty<SyntaxTree>(),
                        References.Value,
                        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                            nullableContextOptions: k.Nullable,
                            allowUnsafe: k.AllowUnsafe)))
                .AddSyntaxTrees(trees);
        }

        /// <summary>
        ///     One empty compilation per distinct (assembly name, nullable context, allowUnsafe) triple, so the
        ///     reference binding behind it is paid once per triple instead of once per test. The suite uses a
        ///     handful of assembly names, so this stays small and lives for the test host's lifetime.
        /// </summary>
        private static readonly ConcurrentDictionary<(string AssemblyName, NullableContextOptions Nullable, bool AllowUnsafe), CSharpCompilation> Baselines = new();
    }
}
