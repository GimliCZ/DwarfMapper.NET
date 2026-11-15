// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
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
                    typeof(DwarfMapperAttribute),
                    committed,
                    "class-options"))
                .Concat(OptionTableRenderer.UndocumentedOptions(OptionTableRenderer.RenderRows(
                    typeof(DwarfMapperOptionsAttribute),
                    committed,
                    "assembly-options")))
                .ToList();

            Assert.True(undocumented.Count == 0,
                "Option(s) present in code with no description in docs/options.md. The row is rendered with an " + "empty cell so the omission is visible; write the prose:\n  " + string.Join("\n  ", undocumented));
        }

        [Fact]
        public void Every_snippet_marker_in_every_doc_matches_its_sample()
        {
            var regions = DocRegions.All();
            var stale = new List<string>();

            foreach (var relative in DocSet.All)
            {
                var committed = DocSet.Read(relative);
                var injected = DocSnippetInjector.Inject(committed, regions, relative).Markdown;

                if (string.Equals(relative, GalleryReadme, StringComparison.Ordinal))
                {
                    injected = DocTableInjector.Inject(
                        injected,
                        "gallery-index",
                        GalleryIndexRenderer.RenderRows(),
                        relative);
                }

                if (string.Equals(relative, OptionsDoc, StringComparison.Ordinal))
                {
                    injected = DocTableInjector.Inject(injected,
                        "class-options",
                        OptionTableRenderer.RenderRows(typeof(DwarfMapperAttribute), committed, "class-options"),
                        relative);
                    injected = DocTableInjector.Inject(injected,
                        "assembly-options",
                        OptionTableRenderer.RenderRows(typeof(DwarfMapperOptionsAttribute),
                            committed,
                            "assembly-options"),
                        relative);
                }

                if (string.Equals(Normalise(committed), Normalise(injected), StringComparison.Ordinal))
                {
                    continue;
                }

                // ARCH-06: repo writes go through RepoWriteGuard, which refuses them under a Stryker mutation
                // run. This is the write that emptied README.md, CONTRIBUTING.md and docs/diagnostics.md when
                // the DocTooling leg mutated the very renderers producing `injected` (T3-H1). The staleness
                // detection and the failure below are unaffected, so the leg's kills are unaffected.
                RepoWriteGuard.WriteBack(Path.Combine(RepoLayout.Root, relative), injected);
                stale.Add(relative);
            }

            Assert.True(stale.Count == 0,
                "Snippet(s) in these documents no longer match the sample code they came from:\n  " +
                string.Join("\n  ", stale) +
                (RepoWriteGuard.IsMutationRun
                    ? "\n\n" + RepoWriteGuard.RefusalNotice
                    : "\n\nEach file has been regenerated in your working tree — review the diff and commit it." + "\nThis fails rather than healing quietly on purpose: a healing doc test goes green in " + "CI while the file people read stays stale."));
        }

        /// <summary>
        ///     The Gallery README's section heads are hand-written around their snippet markers, so nothing rendered
        ///     them: example 51 sat under "Advanced" while the index put it in "Configuration", and example 8's
        ///     description was cut off at an escaped quote. They now have to equal the catalogue's tiers, titles,
        ///     files and descriptions, in reading order.
        /// </summary>
        [Fact]
        public void Gallery_section_heads_follow_the_example_catalogue()
        {
            var committed = DocSet.Read(GalleryReadme).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var sections = committed.SkipWhile(l => !l.StartsWith("## Index", StringComparison.Ordinal))
                .SkipWhile(l => !l.StartsWith("<!-- endtable -->", StringComparison.Ordinal));

            var heads = new List<string>();
            var inFence = false;
            foreach (var line in sections)
            {
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }

                if (!inFence && (line.StartsWith("## ", StringComparison.Ordinal) ||
                                 line.StartsWith("### ", StringComparison.Ordinal) ||
                                 (line.StartsWith('*') && line.Contains("* — ", StringComparison.Ordinal))))
                {
                    heads.Add(line.TrimEnd());
                }
            }

            Assert.Equal(GalleryIndexRenderer.RenderSectionHeads(), heads);
        }

        private static string Normalise(string text)
        {
            return text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        }

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
                "Only one option was documented in the fixture, so nearly every other option should be reported " + $"as undocumented; got {undocumented.Count}.");
        }
    }
}
