// SPDX-License-Identifier: GPL-2.0-only

using CsCheck;
using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Property-based tests over the snippet pipeline, with integrated shrinking (CsCheck), closing open
///     item 4 of <c>docs/research/testing-conformance-REPORT.md</c>.
///     <para>
///         A text transformer is a far better PBT target than the generator ever was: the input space is
///         adversarial (fences, markers, tabs, CRLF), the invariants are exact, and a failure shrinks to a
///         one-line counterexample instead of a forty-line body someone has to bisect by hand.
///     </para>
///     <para>
///         These matter more than the example-based tests because the injector WRITES TRACKED FILES. A
///         marker bug that dropped or truncated content would not throw — it would commit.
///     </para>
/// </summary>
public class DocPipelinePropertyTests
{
    /// <summary>
    ///     Deliberately adversarial: the lines a naive implementation mishandles. Ordinary code lines would
    ///     only ever exercise the happy path.
    /// </summary>
    private static readonly Gen<string> CodeLine = Gen.OneOf(
        Gen.Const("var x = 1;"),
        Gen.Const("if (a)"),
        Gen.Const("{"),
        Gen.Const("}"),
        Gen.Const("// a trailing comment"),
        Gen.Const("    var indented = 2;"),
        Gen.Const("\tvar tabbed = 3;"),
        Gen.Const("\t\tvar deeper = 4;"),
        Gen.Const(""),
        Gen.Const("        "),
        Gen.Const("string s = \"| pipe | table |\";"),
        Gen.Const("var unicode = \"→ ≡ ó\";"),
        // The line that broke idempotence before the fence width was made dynamic. Kept in the generator so
        // it is exercised on every run rather than only by the regression test below.
        Gen.Const("var md = \"```\";"),
        Gen.Const("```"));

    private static readonly Gen<List<string>> Body =
        Gen.Int[1, 8].SelectMany(n => CodeLine.Array[n].Select(a => a.ToList()));

    private static Dictionary<string, SnippetRegion> One(string body) =>
        new(StringComparer.Ordinal) { ["demo"] = new SnippetRegion("demo", body, "F.cs", 1) };

    [Fact]
    public void Injection_is_idempotent()
    {
        // If a second run produced different text, the heal-or-fail test could never converge: every CI run
        // would rewrite the file and fail again, forever.
        Body.Sample(lines =>
        {
            var body = string.Join('\n', lines).Trim('\n');
            if (body.Trim().Length == 0) return;

            var regions = One(body);
            const string doc = "Prose.\n\n<!-- snippet: demo -->\n<!-- endsnippet -->\n\nMore prose.\n";

            var once = DocSnippetInjector.Inject(doc, regions, "d.md").Markdown;
            var twice = DocSnippetInjector.Inject(once, regions, "d.md").Markdown;

            Assert.Equal(once, twice);
        }, iter: 500);
    }

    [Fact]
    public void Prose_outside_the_markers_always_survives()
    {
        // The failure that would be silent data loss: content before or after a marker pair being consumed.
        Body.Sample(lines =>
        {
            var body = string.Join('\n', lines).Trim('\n');
            if (body.Trim().Length == 0) return;

            const string before = "UNIQUE_BEFORE_SENTINEL";
            const string after = "UNIQUE_AFTER_SENTINEL";
            var doc = $"{before}\n\n<!-- snippet: demo -->\n<!-- endsnippet -->\n\n{after}\n";

            var result = DocSnippetInjector.Inject(doc, One(body), "d.md").Markdown;

            Assert.Contains(before, result, StringComparison.Ordinal);
            Assert.Contains(after, result, StringComparison.Ordinal);
        }, iter: 500);
    }

    [Fact]
    public void A_region_survives_extraction_after_being_written_into_source()
    {
        // The end-to-end contract: what the scanner reads back out of a sample file is what was put in,
        // whatever the indentation. This is where a character-counting dedent dies on a tab.
        Gen.Select(Body, Gen.Int[0, 3]).Sample(t =>
        {
            var (lines, indentDepth) = t;
            var indent = new string(' ', indentDepth * 4);

            var written = new List<string> { indent + "// <snippet: demo>" };
            written.AddRange(lines.Select(l => l.Length == 0 ? "" : indent + l));
            written.Add(indent + "// </snippet>");

            var source = string.Join('\n', written);
            var expected = Dedent(lines);
            if (expected.Trim().Length == 0) return;   // an all-blank region is a loud error, tested elsewhere

            var region = Assert.Single(SnippetScanner.ScanFile("F.cs", source));
            Assert.Equal(expected, region.Body);
        }, iter: 500);
    }

    [Fact]
    public void A_malformed_marker_always_throws_and_never_returns_partial_text()
    {
        // Every malformed shape must FAIL rather than degrade. A silent partial result would be written to
        // the working tree and committed as if it were correct.
        Gen.OneOf(
                Gen.Const("// <snippet: a>\nx\n"),                              // never closed
                Gen.Const("x\n// </snippet>\n"),                                // close with no open
                Gen.Const("// <snippet: a>\n// <snippet: b>\nx\n// </snippet>\n// </snippet>\n"),
                Gen.Const("// <snippet: >\nx\n// </snippet>\n"),                 // empty id
                Gen.Const("// <snippet: a>\n// </snippet>\n"))                   // empty body
            .Sample(source => Assert.Throws<DocToolingException>(
                () => SnippetScanner.ScanFile("F.cs", source)), iter: 200);
    }


    [Fact]
    public void A_body_containing_a_code_fence_still_round_trips()
    {
        // Found by the property probe, not by review: with a fixed three-backtick fence, a body containing
        // ``` closed the block early, leaked the remaining code into the document as prose, and the next run
        // re-read that prose as content. The fence is now wider than any backtick run inside the body.
        var regions = One("var a = 1;\nvar md = \"```\";\nvar b = 2;");
        const string doc = "Before.\n\n<!-- snippet: demo -->\n<!-- endsnippet -->\n\nAfter.\n";

        var once = DocSnippetInjector.Inject(doc, regions, "d.md").Markdown;
        var twice = DocSnippetInjector.Inject(once, regions, "d.md").Markdown;

        Assert.Equal(once, twice);
        Assert.Contains("After.", twice, StringComparison.Ordinal);
        Assert.Contains("````csharp", twice, StringComparison.Ordinal);
    }

    [Fact]
    public void A_region_containing_an_injector_marker_is_refused()
    {
        // The other half of the same discovery. This one has no correct rendering, so the scanner refuses it
        // rather than writing a document that the next run would garble.
        var ex = Assert.Throws<DocToolingException>(() => SnippetScanner.ScanFile(
            "F.cs", "// <snippet: demo>\nvar a = 1;\n<!-- endsnippet -->\nvar b = 2;\n// </snippet>\n"));

        Assert.Contains("injector marker", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>The dedent oracle, written independently of the implementation.</summary>
    private static string Dedent(List<string> lines)
    {
        var kept = new List<string>(lines);
        while (kept.Count > 0 && kept[0].Trim().Length == 0) kept.RemoveAt(0);
        while (kept.Count > 0 && kept[^1].Trim().Length == 0) kept.RemoveAt(kept.Count - 1);
        if (kept.Count == 0) return "";

        var nonBlank = kept.Where(l => l.Trim().Length != 0).ToList();
        var prefix = nonBlank[0][..(nonBlank[0].Length - nonBlank[0].TrimStart().Length)];
        foreach (var line in nonBlank)
        {
            var w = line[..(line.Length - line.TrimStart().Length)];
            while (prefix.Length > 0 && !w.StartsWith(prefix, StringComparison.Ordinal))
                prefix = prefix[..^1];
        }

        return string.Join('\n', kept.Select(l => l.Trim().Length == 0 ? "" : l[prefix.Length..]));
    }
}
