// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Unit tests for the markdown writer. It rewrites tracked documents in place, so "does it truncate" is a
///     more important question here than "does it format nicely".
/// </summary>
public class DocSnippetInjectorTests
{
    private static Dictionary<string, SnippetRegion> Regions(params (string Id, string Body)[] rs) =>
        rs.ToDictionary(r => r.Id, r => new SnippetRegion(r.Id, r.Body, "F.cs", 1), StringComparer.Ordinal);

    [Fact]
    public void Fills_an_empty_marker_pair_with_a_fenced_block()
    {
        const string doc = """
            Text.

            <!-- snippet: demo -->
            <!-- endsnippet -->
            """;

        var result = DocSnippetInjector.Inject(doc, Regions(("demo", "var x = 1;")), "d.md");

        Assert.Equal("""
            Text.

            <!-- snippet: demo -->
            ```csharp
            var x = 1;
            ```
            <!-- endsnippet -->
            """, result.Markdown.TrimEnd());
        Assert.Equal(["demo"], result.ReferencedIds);
    }

    [Fact]
    public void Replaces_a_stale_body_rather_than_appending_to_it()
    {
        const string doc = """
            <!-- snippet: demo -->
            ```csharp
            var old = 0;
            ```
            <!-- endsnippet -->
            """;

        var result = DocSnippetInjector.Inject(doc, Regions(("demo", "var fresh = 1;")), "d.md");

        Assert.Contains("var fresh = 1;", result.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("var old = 0;", result.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Is_idempotent()
    {
        const string doc = """
            <!-- snippet: demo -->
            <!-- endsnippet -->
            """;
        var regions = Regions(("demo", "var x = 1;"));

        var once = DocSnippetInjector.Inject(doc, regions, "d.md").Markdown;
        var twice = DocSnippetInjector.Inject(once, regions, "d.md").Markdown;

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Preserves_prose_outside_the_markers_verbatim()
    {
        const string doc = """
            # Heading

            Before.

            <!-- snippet: demo -->
            <!-- endsnippet -->

            After the block.
            """;

        var result = DocSnippetInjector.Inject(doc, Regions(("demo", "x")), "d.md");

        Assert.Contains("# Heading", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("Before.", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("After the block.", result.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_id_is_a_loud_failure()
    {
        // The marker sits on line 2 so the reported line number discriminates: a maintainer chasing a
        // doc-pipeline failure goes to the number the message quotes, and an off-by-one (or negated)
        // line is worse than none. The remedy sentence is asserted too — it is pasteable output.
        var ex = Assert.Throws<DocToolingException>(() => DocSnippetInjector.Inject(
            "Intro.\n<!-- snippet: ghost -->\n<!-- endsnippet -->\n", Regions(("demo", "x")), "d.md"));

        Assert.Contains("d.md:2: snippet 'ghost'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("no sample defines it", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'// <snippet: ghost>' region to a file under samples/", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unclosed_marker_is_a_loud_failure_and_not_a_truncation()
    {
        // The dangerous failure mode: swallowing the rest of the file while looking for a close marker.
        var ex = Assert.Throws<DocToolingException>(() => DocSnippetInjector.Inject(
            "Intro.\n<!-- snippet: demo -->\nprose that must not be eaten\n", Regions(("demo", "x")), "d.md"));

        Assert.Contains("d.md:2: snippet 'demo' is never closed with '<!-- endsnippet -->'",
            ex.Message, StringComparison.Ordinal);
        Assert.Contains("the rest of the file as its body", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_marker_missing_its_close_delimiter_is_a_loud_failure()
    {
        // A line that starts like a marker but never closes its comment must be refused, not guessed at.
        var ex = Assert.Throws<DocToolingException>(() => DocSnippetInjector.Inject(
            "Intro.\n<!-- snippet: demo\n<!-- endsnippet -->\n", Regions(("demo", "x")), "d.md"));

        Assert.Contains("d.md:2: malformed marker", ex.Message, StringComparison.Ordinal);
        Assert.Contains("expected '<!-- snippet: id -->'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_marker_with_an_empty_id_is_a_loud_failure()
    {
        var ex = Assert.Throws<DocToolingException>(() => DocSnippetInjector.Inject(
            "Intro.\n<!-- snippet: -->\n<!-- endsnippet -->\n", Regions(("demo", "x")), "d.md"));

        Assert.Contains("d.md:2: snippet marker has an empty id", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Handles_crlf_line_endings()
    {
        // Exact equality, untrimmed: this also pins the single-trailing-newline contract of the writer.
        var result = DocSnippetInjector.Inject(
            "<!-- snippet: demo -->\r\n<!-- endsnippet -->\r\n", Regions(("demo", "var x = 1;")), "d.md");

        Assert.Equal("<!-- snippet: demo -->\n```csharp\nvar x = 1;\n```\n<!-- endsnippet -->\n",
            result.Markdown);
    }

    [Fact]
    public void Two_separate_backtick_runs_do_not_widen_the_fence()
    {
        // The fence must beat the longest single run, not the sum of all runs: a counter that never
        // resets between runs would emit an ever-wider fence and a different document each time.
        var result = DocSnippetInjector.Inject(
            "<!-- snippet: demo -->\n<!-- endsnippet -->\n",
            Regions(("demo", "var a = \"```\";\nvar b = \"```\";")), "d.md");

        Assert.Equal("<!-- snippet: demo -->\n````csharp\nvar a = \"```\";\nvar b = \"```\";\n````\n"
                     + "<!-- endsnippet -->\n", result.Markdown);
    }

    [Fact]
    public void Null_arguments_throw_with_the_offending_parameter_named()
    {
        // The document carries a real marker so a deleted regions-guard is reached by the null it was
        // guarding and lands on the wrong exception type deterministically.
        var exMarkdown = Assert.Throws<ArgumentNullException>(
            () => DocSnippetInjector.Inject(null!, Regions(("demo", "x")), "d.md"));
        var exRegions = Assert.Throws<ArgumentNullException>(
            () => DocSnippetInjector.Inject("<!-- snippet: demo -->\n<!-- endsnippet -->\n", null!, "d.md"));

        Assert.Equal("markdown", exMarkdown.ParamName);
        Assert.Equal("regions", exRegions.ParamName);
    }

    [Fact]
    public void Reports_every_referenced_id_for_orphan_detection()
    {
        const string doc = """
            <!-- snippet: a -->
            <!-- endsnippet -->
            <!-- snippet: b -->
            <!-- endsnippet -->
            """;

        var result = DocSnippetInjector.Inject(doc, Regions(("a", "1"), ("b", "2")), "d.md");

        Assert.Equal(["a", "b"], result.ReferencedIds.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Preserves_the_markers_indentation_so_a_fence_inside_a_list_item_stays_in_it()
    {
        // A fence indented under a bullet is legitimate markdown, and README.md has two. Emitting the fence at
        // column zero would break it out of the list item and silently reflow the document.
        const string doc = """
            - A bullet with an example:
              <!-- snippet: demo -->
              <!-- endsnippet -->
            """;

        var result = DocSnippetInjector.Inject(doc, Regions(("demo", "var x = 1;\n\nvar y = 2;")), "d.md");

        Assert.Equal("""
            - A bullet with an example:
              <!-- snippet: demo -->
              ```csharp
              var x = 1;

              var y = 2;
              ```
              <!-- endsnippet -->
            """, result.Markdown.TrimEnd());
    }

    [Fact]
    public void A_document_of_prose_and_markers_terminates_and_reproduces_every_line()
    {
        // Pins the loop's progress contract: every input line appears exactly once in the output. Under an
        // advance-loss defect (a branch that forgets to move the line index) the in-loop progress guard
        // turns this into an instant loud failure instead of a hang.
        const string doc = "one\n<!-- snippet: demo -->\n<!-- endsnippet -->\ntwo\nthree\n";

        var result = DocSnippetInjector.Inject(doc, Regions(("demo", "var x = 1;")), "d.md").Markdown;

        Assert.Single(result.Split('\n'), l => l == "one");
        Assert.Single(result.Split('\n'), l => l == "two");
        Assert.Single(result.Split('\n'), l => l == "three");
    }

    [Fact]
    public void A_document_with_no_markers_is_returned_unchanged()
    {
        // The harness must be inert before it has work to do, or a later diff is ambiguous.
        const string doc = "# Just prose\n\nNothing to inject here.\n";

        var result = DocSnippetInjector.Inject(doc, Regions(("demo", "x")), "d.md");

        Assert.Equal(doc.TrimEnd(), result.Markdown.TrimEnd());
        Assert.Empty(result.ReferencedIds);
    }
}
