// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using DwarfMapper.Generator;
using DwarfMapper.Generator.Registry;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.CompilerTests
{
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
                // System.Linq.Queryable, named EXPLICITLY rather than left to the AppDomain sweep above.
                // The sweep only sees assemblies this test process has already LOADED, and nothing in this
                // project touched Queryable until the projection leg (round 23, I18) started rendering
                // `Project` methods — whose generated body calls `global::System.Linq.Queryable.Select`. The
                // symptom was a CS1069 ("forwarded to assembly System.Linq.Queryable … consider adding a
                // reference") on EVERY projection sample, which the smoke leg correctly reported as a silent
                // miscompilation because that is exactly what it looks like from the outside. It was the
                // harness, not the product. A `typeof` on the type is what pins it: it also forces the load,
                // so the reference cannot go missing again by accident of which tests ran first.
                .Append(MetadataReference.CreateFromFile(typeof(Queryable).Assembly.Location))
                .ToArray());

        /// <summary>
        ///     The same shared reference set the harness compiles against, for callers that build their own
        ///     compilation. Exposed rather than duplicated: rebuilding ~50 MetadataReferences is the expensive
        ///     part of a compile, and S3's cost measurement would be measuring reference construction as much
        ///     as the generator if it made its own set.
        /// </summary>
        internal static MetadataReference[] MetadataReferences => References.Value;

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
        public static (RunResult Result, Assembly? Assembly) RunAndEmit(IReadOnlyList<string> units)
        {
            var (result, output) = RunCore(
                units,
                FormattableString.Invariant($"TypeGraphAsm_{Guid.NewGuid():N}"));
            if (result.RefusedLoudly || result.CompilationErrors.Length > 0)
            {
                return (result, null);
            }

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

            return (result, Assembly.Load(pe.ToArray()));
        }

        /// <summary>
        ///     Instantiates the rendered mapper class <c>T.M</c> from an emitted graph assembly and invokes its
        ///     one-parameter <c>Map</c> with <paramref name="source" />. Reflection is test-side only (the
        ///     audit's stated boundary: the house no-reflection stance governs the shipped product, not test
        ///     oracles). A <see cref="System.Reflection.TargetInvocationException" /> is unwrapped so the map's
        ///     own exception (a runtime-behaviour fact about the product) reaches the assertion undisguised.
        /// </summary>
        public static object? InvokeMap(Assembly assembly, object source)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            var mapper = assembly.GetType("T.M") ?? throw new InvalidOperationException("emitted assembly has no mapper type 'T.M'");
            var map = mapper.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                          .SingleOrDefault(m => m.Name == "Map" && m.GetParameters().Length == 1) ??
                      throw new InvalidOperationException("mapper type 'T.M' has no one-parameter Map method");
            var instance = Activator.CreateInstance(mapper) ?? throw new InvalidOperationException("mapper type 'T.M' could not be instantiated");
            try
            {
                return map.Invoke(instance, [source]);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                throw tie.InnerException;
            }
        }

        /// <summary>
        ///     The projection twin of <see cref="InvokeMap" /> (round 23, I18): wraps <paramref name="source" />
        ///     in a one-element <c>IQueryable&lt;TSource&gt;</c>, invokes the mapper's <c>Project</c>, and
        ///     returns the single projected element.
        ///     <para>
        ///         ENUMERATING the returned <c>IQueryable</c> is what executes it. Under
        ///         <c>Enumerable.AsQueryable</c> the provider is LINQ-to-Objects, so the emitted expression tree
        ///         is COMPILED AND EVALUATED here — which is the honest limit of every projection claim in this
        ///         project (B19's rule): no ORM runs, so this proves the tree is well-formed and evaluates,
        ///         never that a database provider translates it.
        ///     </para>
        ///     <para>
        ///         Reflection is test-side only, the same declared boundary as <see cref="InvokeMap" />, and a
        ///         <see cref="System.Reflection.TargetInvocationException" /> is unwrapped for the same reason:
        ///         the projection's own exception is a runtime-behaviour fact about the product and must reach
        ///         the assertion undisguised.
        ///     </para>
        /// </summary>
        public static object? InvokeProject(Assembly assembly, Type sourceType, object source)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            ArgumentNullException.ThrowIfNull(sourceType);

            var mapper = assembly.GetType("T.M") ?? throw new InvalidOperationException("emitted assembly has no mapper type 'T.M'");
            var project = mapper.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                              .SingleOrDefault(m => m.Name == "Project" && m.GetParameters().Length == 1) ??
                          throw new InvalidOperationException("mapper type 'T.M' has no one-parameter Project method");

            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(sourceType))!;
            list.Add(source);

            // Enumerable.AsQueryable<T>(IEnumerable<T>) — the generic overload, not the non-generic one, so
            // the queryable's element type is TSource exactly and Project's parameter binds without a cast.
            var asQueryable = typeof(Queryable)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == nameof(Queryable.AsQueryable) && m.IsGenericMethodDefinition)
                .MakeGenericMethod(sourceType);
            var queryable = asQueryable.Invoke(null, [list]);

            var instance = Activator.CreateInstance(mapper) ?? throw new InvalidOperationException("mapper type 'T.M' could not be instantiated");
            object? projected;
            try
            {
                projected = project.Invoke(instance, [queryable]);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                throw tie.InnerException;
            }

            // The enumeration itself can throw from inside the tree, and that throw belongs to the product
            // exactly as much as one from Project's own body — so it is deliberately NOT wrapped either.
            var rows = ((IEnumerable)(projected ?? throw new InvalidOperationException("Project returned null")))
                .Cast<object?>()
                .ToList();

            return rows.Count == 1
                ? rows[0]
                : throw new InvalidOperationException(
                    FormattableString.Invariant($"Project over a ONE-element queryable yielded {rows.Count} rows"));
        }

        private static (RunResult Result, CSharpCompilation Output) RunCore(
            IReadOnlyList<string> units,
            string assemblyName)
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
    }
}
