// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     The complete region set a document can quote: regions scanned from sample SOURCE, plus regions
///     rendered from the generator's own EMITTED output.
///     <para>
///         One place, used by both the currency test and the reconciliation rules, so the two cannot disagree
///         about what exists — a disagreement would show up as a document that heals forever or an orphan that
///         is not really orphaned.
///     </para>
/// </summary>
internal static class DocRegions
{
    public static IReadOnlyDictionary<string, SnippetRegion> All() =>
        SnippetScanner.Merge(
            SnippetScanner.ScanAll().Values.Concat(EmittedCodeCatalogue.Render().Values));
}
