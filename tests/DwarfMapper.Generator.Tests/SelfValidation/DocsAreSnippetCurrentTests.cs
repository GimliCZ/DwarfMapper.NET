// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Heal-or-fail over every document the pipeline owns: the corrected text is written into the working tree
///     so the diff is right there, and then the test FAILS so it has to be committed.
///     <para>
///         Deliberately not healing quietly. A healing doc test goes green in CI while the committed file
///         people actually read stays stale — the state this whole pipeline exists to prevent.
///     </para>
/// </summary>
public class DocsAreSnippetCurrentTests
{
    private const string GalleryReadme = "samples/DwarfMapper.Gallery/README.md";
    private const string OptionsDoc = "docs/options.md";

    /// <summary>
    ///     An option that exists in code but has no prose in the cheat-sheet. The table renders it with an
    ///     empty cell rather than omitting it, so the page cannot lie by silence — and this fails until a
    ///     human writes the description.
    /// </summary>
    [Fact]
    public void Every_option_in_code_carries_prose_in_the_cheat_sheet()
    {
        var committed = DocSet.Read(OptionsDoc);

        var undocumented = OptionTableRenderer
            .UndocumentedOptions(OptionTableRenderer.RenderRows(
                typeof(DwarfMapperAttribute), committed, "class-options"))
            .Concat(OptionTableRenderer.UndocumentedOptions(OptionTableRenderer.RenderRows(
                typeof(DwarfMapperOptionsAttribute), committed, "assembly-options")))
            .ToList();

        Assert.True(undocumented.Count == 0,
            "Option(s) present in code with no description in docs/options.md. The row is rendered with an "
            + "empty cell so the omission is visible; write the prose:\n  "
            + string.Join("\n  ", undocumented));
    }

    [Fact]
    public void Every_snippet_marker_in_every_doc_matches_its_sample()
    {
        var regions = SnippetScanner.ScanAll();
        var stale = new List<string>();

        foreach (var relative in DocSet.All)
        {
            var committed = DocSet.Read(relative);
            var injected = DocSnippetInjector.Inject(committed, regions, relative).Markdown;

            if (string.Equals(relative, GalleryReadme, StringComparison.Ordinal))
                injected = DocTableInjector.Inject(
                    injected, "gallery-index", GalleryIndexRenderer.RenderRows(), relative);

            if (string.Equals(relative, OptionsDoc, StringComparison.Ordinal))
            {
                injected = DocTableInjector.Inject(injected, "class-options",
                    OptionTableRenderer.RenderRows(typeof(DwarfMapperAttribute), committed, "class-options"),
                    relative);
                injected = DocTableInjector.Inject(injected, "assembly-options",
                    OptionTableRenderer.RenderRows(typeof(DwarfMapperOptionsAttribute), committed,
                        "assembly-options"),
                    relative);
            }

            if (string.Equals(Normalise(committed), Normalise(injected), StringComparison.Ordinal)) continue;

            File.WriteAllText(Path.Combine(RepoLayout.Root, relative), injected);
            stale.Add(relative);
        }

        Assert.True(stale.Count == 0,
            "Snippet(s) in these documents no longer match the sample code they came from. Each file has "
            + "been regenerated in your working tree — review the diff and commit it:\n  "
            + string.Join("\n  ", stale)
            + "\n\nThis fails rather than healing quietly on purpose: a healing doc test goes green in CI "
            + "while the file people read stays stale.");
    }

    private static string Normalise(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

    [Fact]
    public void An_option_missing_from_the_committed_table_is_reported_as_undocumented()
    {
        // Added because the mutation battery survived a renderer that emits "TBD" for a missing option: the
        // currency test only notices an EMPTY cell, so a helpful-looking placeholder would ship an option
        // documented by a word that says nothing. This drives the renderer directly with a table that omits
        // one option, so the detection is exercised rather than inferred.
        const string committed = """
            <!-- table: class-options -->
            | Option | Type | Default | What it does |
            |---|---|---|---|
            | `MaxDepth` | `int` | `64` | Depth bound for recursion-capable pairs. |
            <!-- endtable -->
            """;

        var rows = OptionTableRenderer.RenderRows(typeof(DwarfMapperAttribute), committed, "class-options");
        var undocumented = OptionTableRenderer.UndocumentedOptions(rows);

        Assert.DoesNotContain("MaxDepth", undocumented);
        Assert.Contains("CaseInsensitive", undocumented);
        Assert.True(undocumented.Count > 5,
            "Only one option was documented in the fixture, so nearly every other option should be reported "
            + $"as undocumented; got {undocumented.Count}.");
    }
}
