// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using DwarfMapper.Generator;
using DwarfMapper.Generator.Registry;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DwarfMapper.NegativeCases
{
    /// <summary>
    ///     Compiles one case file, runs the generators over it, and reports what came out.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Deliberately NOT the <c>GeneratorTestHarness</c> from <c>DwarfMapper.Generator.Tests</c>. Two
    ///         reasons. The first is that this project has to stay able to say "the shipped generator refuses this
    ///         shape" without inheriting whatever accommodations the main harness has grown; an independent
    ///         implementation cannot be bent by the same mistake twice. The second is that a Round-18 audit found
    ///         two defects self-review had passed, both of the form "a check that read a subset of the inputs the
    ///         real path reads" — a second, differently-built instrument is the cheapest defence against that.
    ///     </para>
    ///     <para>
    ///         References come from the runtime's own trusted-platform-assemblies list rather than from whatever
    ///         happens to be loaded into the test AppDomain. The loaded-assembly trick works, but it makes the
    ///         reference set depend on which tests ran first, which is exactly the kind of order dependence a
    ///         negative-case suite must not have: a case could pass because an earlier test happened to load
    ///         <c>System.ComponentModel</c>.
    ///     </para>
    /// </remarks>
    internal static class CaseDriver
    {
        internal static readonly Lazy<ImmutableArray<MetadataReference>> Refs = new(() =>
        {
            var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;

            var paths = tpa
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // The runtime attribute assembly is a ProjectReference, so it is already on the TPA list — but assert
            // rather than assume, because a case that silently loses `[DwarfMapper]` would fail as a mess of
            // CS0246 rather than as the missing diagnostic it actually is.
            var runtime = typeof(DwarfMapperAttribute).Assembly.Location;
            if (!paths.Contains(runtime, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(runtime);
            }

            return paths
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
                .ToImmutableArray();
        });

        public static Outcome Run(string source, string assemblyName)
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
            var syntax = CSharpSyntaxTree.ParseText(source, parseOptions);

            var (buildProperties, standInAssemblies) = ProjectHeaders(source);
            var references = Refs.Value.AddRange(standInAssemblies.Select(StandIn));

            var compilation = CSharpCompilation.Create(
                assemblyName,
                [syntax],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));

            // Both generators, because the DWARFR registry family is only reachable through the [MapTo] one and a
            // case file should not have to know which driver owns its id.
            // The driver must parse what it generates with the SAME language version as the case, or Roslyn
            // refuses the updated compilation outright ("inconsistent language versions") and every case fails as
            // an exception rather than as a diagnostic mismatch.
            var driver = CSharpGeneratorDriver.Create(
                [new DwarfGenerator().AsSourceGenerator(), new MapToGenerator().AsSourceGenerator()],
                parseOptions: parseOptions,
                optionsProvider: new BuildPropertiesProvider(buildProperties));

            driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

            var generated = string.Join("\n\n",
                output.SyntaxTrees
                    .Where(t => t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
                    .OrderBy(t => t.FilePath, StringComparer.Ordinal)
                    .Select(t => t.ToString()));

            return new Outcome(generatorDiagnostics, output.GetDiagnostics(), generated);
        }

        /// <summary>
        ///     The <c>BUILD-PROPERTY</c> and <c>REFERENCES-ASSEMBLY</c> header lines (see <see cref="NegativeCase" />'s
        ///     grammar), read with the same stop rule as the rest of the header: the first line of real code ends it.
        /// </summary>
        private static (Dictionary<string, string> Properties, List<string> Assemblies) ProjectHeaders(string source)
        {
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            var assemblies = new List<string>();
            foreach (var raw in source.Split('\n'))
            {
                var line = raw.TrimEnd('\r').Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (!line.StartsWith("//", StringComparison.Ordinal))
                {
                    break;
                }

                var body = line[2..].Trim();
                if (body.StartsWith("BUILD-PROPERTY:", StringComparison.Ordinal))
                {
                    var pair = body["BUILD-PROPERTY:".Length..].Split('=', 2);
                    properties["build_property." + pair[0].Trim()] = pair.Length > 1 ? pair[1].Trim() : string.Empty;
                }
                else if (body.StartsWith("REFERENCES-ASSEMBLY:", StringComparison.Ordinal))
                {
                    assemblies.Add(body["REFERENCES-ASSEMBLY:".Length..].Trim());
                }
            }

            return (properties, assemblies);
        }

        /// <summary>An empty assembly with the given name — for a diagnostic that keys on a reference, not a type.</summary>
        private static MetadataReference StandIn(string name)
        {
            return CSharpCompilation.Create(name,
                    [],
                    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .ToMetadataReference();
        }

        private sealed class BuildPropertiesProvider(Dictionary<string, string> global) : AnalyzerConfigOptionsProvider
        {
            public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(global);

            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
            {
                return new Options([]);
            }

            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
            {
                return new Options([]);
            }
        }

        private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
            {
                return values.TryGetValue(key, out value);
            }
        }

        /// <summary>Every DWARF/DWARFR id the generators reported, deduplicated and ordered.</summary>
        public static ImmutableArray<string> DwarfIds(ImmutableArray<Diagnostic> diagnostics)
        {
            return diagnostics
                .Select(d => d.Id)
                .Where(id => id.StartsWith("DWARF", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToImmutableArray();
        }

        /// <summary>What the generators said, and what the compiler said about what they emitted.</summary>
        internal readonly record struct Outcome(
            ImmutableArray<Diagnostic> Generator,
            ImmutableArray<Diagnostic> Compilation,
            string GeneratedSource);
    }
}
