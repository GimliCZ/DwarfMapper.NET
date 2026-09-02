// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using DwarfMapper.Generator;
using DwarfMapper.Generator.Registry;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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

            var compilation = CSharpCompilation.Create(
                assemblyName,
                [syntax],
                Refs.Value,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));

            // Both generators, because the DWARFR registry family is only reachable through the [MapTo] one and a
            // case file should not have to know which driver owns its id.
            // The driver must parse what it generates with the SAME language version as the case, or Roslyn
            // refuses the updated compilation outright ("inconsistent language versions") and every case fails as
            // an exception rather than as a diagnostic mismatch.
            var driver = CSharpGeneratorDriver.Create(
                [new DwarfGenerator().AsSourceGenerator(), new MapToGenerator().AsSourceGenerator()],
                parseOptions: parseOptions);

            driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

            var generated = string.Join("\n\n",
                output.SyntaxTrees
                    .Where(t => t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
                    .OrderBy(t => t.FilePath, StringComparer.Ordinal)
                    .Select(t => t.ToString()));

            return new Outcome(generatorDiagnostics, output.GetDiagnostics(), generated);
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
