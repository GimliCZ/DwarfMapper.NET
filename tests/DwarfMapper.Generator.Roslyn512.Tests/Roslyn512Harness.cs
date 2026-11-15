// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using DwarfMapper.Generator.Registry;
using DwarfMapper.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace DwarfMapper.Generator.Roslyn512.Tests
{
    /// <summary>One run of both shipped generators over one source, compiled by Roslyn 5.12.</summary>
    /// <param name="Generated">Every generated <c>.g.cs</c> file, ordered by path and concatenated.</param>
    /// <param name="GeneratorDiagnostics">What the generators reported.</param>
    /// <param name="CompileDiagnostics">The warnings and errors of the compilation the generators produced.</param>
    /// <param name="Output">That compilation, for <see cref="Roslyn512Harness.Invoke" />.</param>
    internal sealed record GeneratorResult(
        string Generated,
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        ImmutableArray<Diagnostic> CompileDiagnostics,
        CSharpCompilation Output);

    /// <summary>A library compiled for a test to reference: the metadata reference, and the image to load at run time.</summary>
    internal sealed record CompiledLibrary(MetadataReference Reference, ImmutableArray<byte> Image);

    /// <summary>
    ///     Compiles test sources with Roslyn 5.12 and runs the generator's Roslyn 5.12 build over them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The reference set is built the way <c>GeneratorTestHarness</c> builds it in Generator.Tests: the loaded
    ///         assemblies first, then the ones fixtures reach for by type, then a short required list from the
    ///         trusted-platform set. The golden fingerprints depend on it (the generator emits <c>AddDwarfMappers</c>
    ///         only when <c>IServiceCollection</c> is in reach), and one <see cref="MetadataReference" /> is created
    ///         per file for the life of the host (round 34: per-call copies cost gigabytes of native memory).
    ///     </para>
    ///     <para>
    ///         <see cref="UnionPolyfillSource" /> declares <c>UnionAttribute</c> and <c>IUnion</c> when the running
    ///         runtime lacks them. .NET 10 does; on .NET 11 they live in <c>System.Private.CoreLib</c>, a second
    ///         declaration would be CS0436, and the net11 job runs this suite there.
    ///     </para>
    /// </remarks>
    internal static class Roslyn512Harness
    {
        private static readonly ConcurrentDictionary<string, PortableExecutableReference> ReferencesByPath =
            new(StringComparer.Ordinal);

        private static readonly Lazy<MetadataReference[]> LazyReferences = new(BuildReferences);

        private static readonly ConcurrentDictionary<(string AssemblyName, NullableContextOptions Nullable), CSharpCompilation> Baselines = new();

        /// <summary>The framework assemblies fixtures name without the host necessarily loading them.</summary>
        private static readonly string[] RequiredFrameworkAssemblies =
        [
            "System.Runtime.Serialization.Primitives",
            "System.Linq.Queryable",
            "System.Collections.Specialized",
            "System.ComponentModel.Primitives",
            "System.Text.Json"
        ];

        /// <summary>
        ///     The two types a union declaration needs, as .NET 11 declares them, for a runtime that has neither.
        /// </summary>
        internal const string UnionPolyfillSource = """
            #nullable enable
            namespace System.Runtime.CompilerServices
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, AllowMultiple = false)]
                public sealed class UnionAttribute : System.Attribute { }

                public interface IUnion
                {
                    object? Value { get; }
                }
            }
            """;

        /// <summary>True when the running runtime declares <c>UnionAttribute</c> itself (.NET 11 and later).</summary>
        internal static bool RuntimeHasUnionTypes { get; } =
            typeof(object).Assembly.GetType("System.Runtime.CompilerServices.UnionAttribute") is not null;

        internal static IReadOnlyList<MetadataReference> References => LazyReferences.Value;

        internal static PortableExecutableReference ReferenceTo(string path)
        {
            return ReferencesByPath.GetOrAdd(path, static p => MetadataReference.CreateFromFile(p));
        }

        /// <summary>
        ///     A compilation of <paramref name="trees" /> against <see cref="References" /> (plus <paramref name="extra" />).
        ///     Without extra references, it derives from one cached empty compilation per name and nullable context, so
        ///     the reference binding is paid once, as Generator.Tests' harness does.
        /// </summary>
        internal static CSharpCompilation BuildCompilation(
            string assemblyName,
            IEnumerable<SyntaxTree> trees,
            NullableContextOptions nullable,
            IReadOnlyCollection<MetadataReference>? extra = null)
        {
            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: nullable);
            if (extra is { Count: > 0 })
            {
                return CSharpCompilation.Create(assemblyName, trees, References.Concat(extra), options);
            }

            var baseline = Baselines.GetOrAdd((assemblyName, nullable),
                key => CSharpCompilation.Create(key.AssemblyName, Array.Empty<SyntaxTree>(), References, options));
            return baseline.AddSyntaxTrees(trees);
        }

        /// <summary>
        ///     Runs both shipped generators (the mapper generator and the <c>[MapTo]</c> registry generator) over
        ///     <paramref name="source" />, parsed as <paramref name="lang" />.
        /// </summary>
        internal static GeneratorResult Run(
            string source,
            LanguageVersion lang = LanguageVersion.CSharp15,
            NullableContextOptions nullable = NullableContextOptions.Enable,
            bool polyfill = true,
            params CompiledLibrary[] libraries)
        {
            var parse = CSharpParseOptions.Default.WithLanguageVersion(lang);
            var trees = new List<SyntaxTree>
            {
                CSharpSyntaxTree.ParseText(source, parse, "Source.cs")
            };
            if (polyfill && !RuntimeHasUnionTypes)
            {
                trees.Add(CSharpSyntaxTree.ParseText(UnionPolyfillSource, parse, "UnionPolyfill.cs"));
            }

            var compilation = BuildCompilation("DwarfMapperUnionAsm", trees, nullable, libraries.Select(l => l.Reference).ToList());
            var driver = CSharpGeneratorDriver.Create(
                new[]
                {
                    new DwarfGenerator().AsSourceGenerator(), new MapToGenerator().AsSourceGenerator()
                },
                parseOptions: parse);
            var ran = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
            AssertNoGeneratorCrash(ran.GetRunResult());

            var generated = string.Join("\n",
                output.SyntaxTrees
                    .Where(t => t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
                    .OrderBy(t => t.FilePath, StringComparer.Ordinal)
                    .Select(t => t.ToString()));
            var compileDiagnostics = output.GetDiagnostics()
                .Where(d => d.Severity >= DiagnosticSeverity.Warning)
                .ToImmutableArray();
            return new GeneratorResult(generated, generatorDiagnostics, compileDiagnostics, (CSharpCompilation)output);
        }

        /// <summary>
        ///     <see cref="Run" />, failing on any generator diagnostic of Warning or above, any compile error, and any
        ///     compile warning inside a generated file (a consumer cannot suppress those). Returns the run.
        /// </summary>
        internal static GeneratorResult CompilesClean(
            string source,
            LanguageVersion lang = LanguageVersion.CSharp15,
            NullableContextOptions nullable = NullableContextOptions.Enable,
            bool polyfill = true,
            params CompiledLibrary[] libraries)
        {
            var run = Run(source, lang, nullable, polyfill, libraries);
            var problems = run.GeneratorDiagnostics
                .Where(d => d.Severity >= DiagnosticSeverity.Warning)
                .Concat(run.CompileDiagnostics.Where(d =>
                    d.Severity == DiagnosticSeverity.Error ||
                    (d.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) ?? false)))
                .Select(d => d.Id + " " + d.GetMessage(CultureInfo.InvariantCulture) + " @ " + d.Location.GetLineSpan())
                .ToList();
            Assert.True(problems.Count == 0,
                "Expected a clean generation and compilation, got:\n  " + string.Join("\n  ", problems) +
                "\n\nGenerated:\n" + run.Generated);
            return run;
        }

        /// <summary>Compiles <paramref name="librarySource" /> into a library a test source can reference.</summary>
        internal static CompiledLibrary Compile(string librarySource, LanguageVersion lang = LanguageVersion.CSharp15)
        {
            var parse = CSharpParseOptions.Default.WithLanguageVersion(lang);
            var trees = new List<SyntaxTree>
            {
                CSharpSyntaxTree.ParseText(librarySource, parse, "Library.cs")
            };
            if (!RuntimeHasUnionTypes)
            {
                trees.Add(CSharpSyntaxTree.ParseText(UnionPolyfillSource, parse, "UnionPolyfill.cs"));
            }

            var library = CSharpCompilation.Create("DwarfUnionLibrary" + Guid.NewGuid().ToString("N"),
                trees,
                References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            using var pe = new MemoryStream();
            var emitted = library.Emit(pe);
            Assert.True(emitted.Success,
                "The test library did not compile:\n  " + string.Join("\n  ", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            var image = pe.ToArray();
            return new CompiledLibrary(MetadataReference.CreateFromImage(image), ImmutableArray.Create(image));
        }

        /// <summary>
        ///     Emits <paramref name="run" />'s compilation, loads it (and <paramref name="libraries" />) into a collectible
        ///     context, and calls the public static method <paramref name="method" /> of <paramref name="typeName" />.
        /// </summary>
        internal static object? Invoke(GeneratorResult run, string typeName, string method, params CompiledLibrary[] libraries)
        {
            using var pe = new MemoryStream();
            var emitted = run.Output.Emit(pe);
            Assert.True(emitted.Success,
                "Emit failed:\n  " + string.Join("\n  ", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

            var context = new AssemblyLoadContext("dwarf-roslyn512-" + Guid.NewGuid().ToString("N"), true);
            try
            {
                foreach (var library in libraries)
                {
                    using var image = new MemoryStream(library.Image.ToArray());
                    context.LoadFromStream(image);
                }

                pe.Position = 0;
                var assembly = context.LoadFromStream(pe);
                var type = assembly.GetType(typeName, true)!;
                var entry = type.GetMethod(method, BindingFlags.Public | BindingFlags.Static) ??
                            throw new MissingMethodException(typeName, method);
                return entry.Invoke(null, null);
            }
            finally
            {
                context.Unload();
            }
        }

        /// <summary>Fails the test if a generator threw (Roslyn turns that into a CS8785 warning and no sources).</summary>
        internal static void AssertNoGeneratorCrash(GeneratorDriverRunResult result)
        {
            foreach (var r in result.Results)
                if (r.Exception is { } ex)
                {
                    throw new InvalidOperationException(
                        $"Generator '{r.Generator.GetGeneratorType().Name}' THREW {ex.GetType().Name}: {ex.Message}. Stack: {ex.StackTrace}",
                        ex);
                }
        }

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

            Offer(typeof(DwarfMapperAttribute).Assembly.Location);
            Offer(typeof(RoundTrip).Assembly.Location);
            Offer(typeof(Queryable).Assembly.Location);
            Offer(typeof(IServiceCollection).Assembly.Location);
            Offer(typeof(NameValueCollection).Assembly.Location);

            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
            {
                foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                    if (RequiredFrameworkAssemblies.Contains(Path.GetFileNameWithoutExtension(path)))
                    {
                        Offer(path);
                    }
            }

            return byName.Values.Select(p => (MetadataReference)ReferenceTo(p)).ToArray();
        }
    }
}
