// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation;

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
    public void An_unclosed_region_is_a_loud_failure()
    {
        var ex = Assert.Throws<DocToolingException>(
            () => SnippetScanner.ScanFile("F.cs", "// <snippet: demo>\nvar x = 1;\n"));

        Assert.Contains("never closed", ex.Message, StringComparison.Ordinal);
        Assert.Contains("F.cs", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_close_without_an_open_is_a_loud_failure()
    {
        var ex = Assert.Throws<DocToolingException>(
            () => SnippetScanner.ScanFile("F.cs", "var x = 1;\n// </snippet>\n"));

        Assert.Contains("no matching open", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nested_region_is_a_loud_failure()
    {
        var ex = Assert.Throws<DocToolingException>(() => SnippetScanner.ScanFile(
            "F.cs", "// <snippet: a>\n// <snippet: b>\nx\n// </snippet>\n// </snippet>\n"));

        Assert.Contains("still open", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_region_is_a_loud_failure()
    {
        // An empty body would render as an empty code fence, which reads as "this feature needs no code".
        var ex = Assert.Throws<DocToolingException>(
            () => SnippetScanner.ScanFile("F.cs", "// <snippet: demo>\n// </snippet>\n"));

        Assert.Contains("empty", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_marker_with_no_id_is_a_loud_failure()
    {
        var ex = Assert.Throws<DocToolingException>(
            () => SnippetScanner.ScanFile("F.cs", "// <snippet: >\nx\n// </snippet>\n"));

        Assert.Contains("empty id", ex.Message, StringComparison.Ordinal);
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
}
