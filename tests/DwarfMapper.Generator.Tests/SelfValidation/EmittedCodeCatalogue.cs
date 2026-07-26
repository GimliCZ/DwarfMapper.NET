// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Renders snippet regions containing the generator's OWN emitted output, so a document can show
///     "you write this → it emits that" with both halves derived rather than described.
///     <para>
///         This is what backs the claim in the README's Emit section — that the generated code reads like
///         something you would have hand-written. Prose asserting that is unfalsifiable; the emitted text
///         beside the source it came from is not, and it changes in the same commit the emitter does.
///     </para>
///     <para>
///         Lives in the test project rather than DwarfMapper.DocTooling because producing it means RUNNING
///         the generator, which needs Roslyn and the generator assembly. DocTooling stays a text-only library
///         with no compiler dependency; the emitted text arrives as ordinary regions it already knows how to
///         inject.
///     </para>
/// </summary>
internal static class EmittedCodeCatalogue
{
    /// <summary>
    ///     Which Gallery examples get their generated output published, by ordinal. Deliberately a short list:
    ///     the point is to show the SHAPE of what is emitted once or twice, not to mirror the whole corpus
    ///     into the docs.
    /// </summary>
    private static readonly (int Ordinal, string Id)[] Published =
    [
        (1, "emitted-flat-map"),
        (6, "emitted-deep-paths")
    ];

    /// <summary>The generator's output for each published example, keyed by region id.</summary>
    public static IReadOnlyDictionary<string, SnippetRegion> Render()
    {
        var byOrdinal = ExampleCatalogue.Scan().ToDictionary(e => e.Ordinal);
        var result = new Dictionary<string, SnippetRegion>(StringComparer.Ordinal);

        foreach (var (ordinal, id) in Published)
        {
            if (!byOrdinal.TryGetValue(ordinal, out var example))
                throw new DocToolingException(
                    $"Emitted-code catalogue publishes example {ordinal}, which no longer exists. Update "
                    + $"EmittedCodeCatalogue.Published or restore the example.");

            var emitted = Emit(example.RelativeFile);
            result[id] = new SnippetRegion(id, emitted, example.RelativeFile + " (generated)", 0);
        }

        return result;
    }

    /// <summary>
    ///     Compiles the example file exactly as it sits on disk and returns what the generator wrote. The
    ///     [DocExample] attribute is stripped first — it is Gallery-local metadata, and referencing the
    ///     Gallery assembly here would make its types ambiguous with the ones this compilation declares.
    /// </summary>
    private static string Emit(string relativeFile)
    {
        var source = File.ReadAllText(Path.Combine(RepoLayout.Root, relativeFile));
        source = Regex.Replace(source, @"\[DocExample\([^\]]*\)\]", "", RegexOptions.Singleline);

        var (diagnostics, generated) = GeneratorTestHarness.Run(source);

        var errors = diagnostics
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.Id + ": " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        if (errors.Count > 0)
            throw new DocToolingException(
                $"{relativeFile}: the generator reported errors while producing published output, so the "
                + "document would show code from a failed compilation:\n  " + string.Join("\n  ", errors));

        if (string.IsNullOrWhiteSpace(generated))
            throw new DocToolingException(
                $"{relativeFile}: the generator produced no output. Publishing an empty block would read as "
                + "\"this mapping needs no code\".");

        return generated.Trim('\n');
    }
}
