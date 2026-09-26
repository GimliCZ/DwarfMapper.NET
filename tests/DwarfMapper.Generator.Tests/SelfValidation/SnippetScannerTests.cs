// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Unit tests for the region parser. It feeds a writer that rewrites tracked files, so the malformed-input
    ///     cases matter as much as the happy path: a marker bug that truncated a document would be silent data
    ///     loss in the documentation pipeline.
    /// </summary>
    public class SnippetScannerTests
    {
        [Fact]
        public void Extracts_a_region_and_strips_the_markers()
        {
            const string source = """
                                  class C
                                  {
                                      // <snippet: demo>
                                      var x = 1;
                                      // </snippet>
                                  }
                                  """;

            var region = Assert.Single(SnippetScanner.ScanFile("F.cs", source));

            Assert.Equal("demo", region.Id);
            Assert.Equal("var x = 1;", region.Body);
            Assert.Equal(3, region.StartLine);
        }

        [Fact]
        public void Dedents_to_the_shallowest_line_preserving_relative_indentation()
        {
            const string source = """
                                  // <snippet: demo>
                                          if (a)
                                          {
                                              b();
                                          }
                                  // </snippet>
                                  """;

            Assert.Equal("if (a)\n{\n    b();\n}", SnippetScanner.ScanFile("F.cs", source)[0].Body);
        }

        [Fact]
        public void Dedents_by_common_prefix_not_by_character_count()
        {
            // A tab is one character but not one space. Counting instead of matching the actual prefix string
            // would cut a tab-indented line at the wrong place and silently corrupt the rendered snippet.
            var source = "// <snippet: demo>\n\tone\n\t\ttwo\n// </snippet>";

            Assert.Equal("one\n\ttwo", SnippetScanner.ScanFile("F.cs", source)[0].Body);
        }

        [Fact]
        public void Blank_lines_inside_a_region_survive_as_empty_lines()
        {
            const string source = """
                                  // <snippet: demo>
                                      a();

                                      b();
                                  // </snippet>
                                  """;

            Assert.Equal("a();\n\nb();", SnippetScanner.ScanFile("F.cs", source)[0].Body);
        }

        [Fact]
        public void Leading_and_trailing_blank_lines_are_trimmed_from_the_region()
        {
            // The trim is what keeps a region author free to pad the markers for readability without the
            // padding showing up inside the rendered fence. Whitespace-only lines count as blank.
            var source = "// <snippet: demo>\n\n   \nvar x = 1;\n\t\n\n// </snippet>";

            Assert.Equal("var x = 1;", SnippetScanner.ScanFile("F.cs", source)[0].Body);
        }

        [Fact]
        public void An_unclosed_region_is_a_loud_failure()
        {
            // The marker sits on line 2 so the reported line number discriminates: the number the message
            // quotes is where a maintainer goes, and an off-by-one there is worse than no number at all.
            var ex = Assert.Throws<DocToolingException>(() => SnippetScanner.ScanFile("F.cs", "class C\n// <snippet: demo>\nvar x = 1;\n"));

            Assert.Contains("F.cs:2: snippet 'demo' is never closed", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_close_without_an_open_is_a_loud_failure()
        {
            var ex = Assert.Throws<DocToolingException>(() => SnippetScanner.ScanFile("F.cs", "var x = 1;\n// </snippet>\n"));

            Assert.Contains("F.cs:2:", ex.Message, StringComparison.Ordinal);
            Assert.Contains("no matching open", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_nested_region_is_a_loud_failure()
        {
            // Both quoted positions are pinned: the offending inner open (line 3) and the still-open outer
            // region (line 1) — the message names two places and both must be right.
            var ex = Assert.Throws<DocToolingException>(() => SnippetScanner.ScanFile(
                "F.cs",
                "// <snippet: a>\nx\n// <snippet: b>\ny\n// </snippet>\n// </snippet>\n"));

            Assert.Contains("F.cs:3: snippet 'b' opens while 'a' (line 1) is", ex.Message, StringComparison.Ordinal);
            Assert.Contains("still open", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void An_empty_region_is_a_loud_failure()
        {
            // An empty body would render as an empty code fence, which reads as "this feature needs no code".
            var ex = Assert.Throws<DocToolingException>(() => SnippetScanner.ScanFile("F.cs", "class C\n// <snippet: demo>\n// </snippet>\n"));

            Assert.Contains("F.cs:2: snippet 'demo' is empty", ex.Message, StringComparison.Ordinal);
            Assert.Contains("needs no code", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_marker_with_no_id_is_a_loud_failure()
        {
            var ex = Assert.Throws<DocToolingException>(() => SnippetScanner.ScanFile("F.cs", "class C\n// <snippet: >\nx\n// </snippet>\n"));

            Assert.Contains("F.cs:2: snippet marker has an empty id", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_marker_missing_its_close_delimiter_is_a_loud_failure()
        {
            // A line that starts like a marker but never closes it must be refused, not guessed at.
            var ex = Assert.Throws<DocToolingException>(() => SnippetScanner.ScanFile("F.cs", "class C\n// <snippet: demo\nx\n// </snippet>\n"));

            Assert.Contains("F.cs:2: malformed snippet marker", ex.Message, StringComparison.Ordinal);
            Assert.Contains("expected '// <snippet: id>'", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Null_text_throws_with_the_parameter_named()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => SnippetScanner.ScanFile("F.cs", null!));

            Assert.Equal("text", ex.ParamName);
        }

        [Fact]
        public void Merge_of_a_null_sequence_throws_with_the_parameter_named()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => SnippetScanner.Merge(null!));

            Assert.Equal("regions", ex.ParamName);
        }

        [Fact]
        public void Build_output_folders_are_excluded_from_scanning()
        {
            // The predicate is a path-shape contract: bin/ and obj/ are excluded INDEPENDENTLY — a predicate
            // that only excluded paths under both at once would scan generated sources for markers.
            var sep = Path.DirectorySeparatorChar;

            Assert.False(SnippetScanner.IsNotBuildOutput($"s{sep}obj{sep}Debug{sep}A.cs"));
            Assert.False(SnippetScanner.IsNotBuildOutput($"s{sep}bin{sep}Debug{sep}A.cs"));
            Assert.True(SnippetScanner.IsNotBuildOutput($"s{sep}A.cs"));
        }

        [Fact]
        public void Handles_crlf_line_endings()
        {
            var source = "// <snippet: demo>\r\n    var x = 1;\r\n// </snippet>\r\n";

            Assert.Equal("var x = 1;", SnippetScanner.ScanFile("F.cs", source)[0].Body);
        }

        [Fact]
        public void Finds_several_regions_in_one_file()
        {
            const string source = """
                                  // <snippet: a>
                                  one
                                  // </snippet>
                                  filler
                                  // <snippet: b>
                                  two
                                  // </snippet>
                                  """;

            var regions = SnippetScanner.ScanFile("F.cs", source);

            Assert.Equal(["a", "b"], regions.Select(r => r.Id));
            Assert.Equal(["one", "two"], regions.Select(r => r.Body));
        }

        [Fact]
        public void A_duplicate_id_across_files_is_refused()
        {
            // Added because the mutation battery killed nothing when the duplicate check was disabled: the real
            // corpus has no duplicates, so no test could reach the branch. "Whichever file was scanned first" is
            // not a documentation contract.
            var ex = Assert.Throws<DocToolingException>(() => SnippetScanner.Merge(
            [
                new SnippetRegion("demo", "a", "A.cs", 1),
                new SnippetRegion("demo", "b", "B.cs", 9)
            ]));

            Assert.Contains("Duplicate snippet id 'demo'", ex.Message, StringComparison.Ordinal);
            Assert.Contains("A.cs:1", ex.Message, StringComparison.Ordinal);
            Assert.Contains("B.cs:9", ex.Message, StringComparison.Ordinal);
            Assert.Contains("rename one of them", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Distinct_ids_merge_without_complaint()
        {
            var merged = SnippetScanner.Merge(
            [
                new SnippetRegion("one", "a", "A.cs", 1),
                new SnippetRegion("two", "b", "B.cs", 2)
            ]);

            Assert.Equal(["one", "two"], merged.Keys.OrderBy(k => k, StringComparer.Ordinal));
        }
    
        [Fact]
        public void SampleFiles_reads_only_cs_files_and_finds_some()
        {
            // The SEARCH PATTERN is a contract, and it is unreachable through SnippetScanner's public entry point: with
            // today's corpus a wrong pattern gives the same answer, which is why a mutation leg carried the
            // blanked pattern as a survivor. The trap is that blanking it does NOT match nothing -
            // Directory.GetFiles reads an empty searchPattern as "every file" - so the failure is reading TOO
            // MUCH. Under it, a `.md` or `.globalconfig` carrying a snippet marker would be scanned, and its ids would collide with the real ones or add regions no sample declares.
            //
            // Both halves matter: the emptiness check refuses a pattern matching nothing, the extension check
            // refuses one matching everything. The live samples tree carries non-.cs files outside bin/obj, so
            // this can tell those apart.
            var files = SnippetScanner.SampleFiles();

            Assert.NotEmpty(files);
            Assert.All(files,
                f => Assert.True(f.EndsWith(".cs", StringComparison.Ordinal),
                    $"SampleFiles() returned a non-source file: {f}. A blank search pattern reads every file, and " +
                    "a snippet marker in a non-.cs file would then be read as though it were a sample."));
        }
}
}
