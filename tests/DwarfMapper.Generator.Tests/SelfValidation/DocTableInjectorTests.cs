// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Unit tests for the table injector. It had no dedicated test file until round 22 (T3 family B): the
///     guard whose comment reads "Refusing to treat the rest of the file as table body" is the thing standing
///     between a malformed marker and a truncated document, and the DocTooling mutation leg demonstrated that
///     truncation for real (NOTE 3 of <c>stryker-config.doctooling.json</c>). Message fragments and reported
///     paths are asserted deliberately — they are the user-facing contract of a refusal.
/// </summary>
public class DocTableInjectorTests
{
    [Fact]
    public void Replaces_the_table_body_and_preserves_the_surroundings()
    {
        const string doc = "Intro.\n\n<!-- table: options -->\n| old |\n<!-- endtable -->\n\nOutro.\n";

        var result = DocTableInjector.Inject(doc, "options", ["| new1 |", "| new2 |"], "d.md");

        // Exact equality, trailing newline included: the injector writes tracked files, so "almost the
        // same document" is not a passing grade, and the single-trailing-newline contract is part of it.
        Assert.Equal("Intro.\n\n<!-- table: options -->\n| new1 |\n| new2 |\n<!-- endtable -->\n\nOutro.\n",
            result);
    }

    [Fact]
    public void Handles_crlf_line_endings()
    {
        const string doc = "Intro.\r\n<!-- table: options -->\r\n| old |\r\n<!-- endtable -->\r\nOutro.\r\n";

        var result = DocTableInjector.Inject(doc, "options", ["| new |"], "d.md");

        Assert.Equal("Intro.\n<!-- table: options -->\n| new |\n<!-- endtable -->\nOutro.\n", result);
    }

    [Fact]
    public void A_marker_on_the_first_line_is_found()
    {
        // Boundary pin: FindIndex returning 0 is a hit, not a miss. A `< 0` check widened to `<= 0`
        // would refuse a document whose table opens the file.
        const string doc = "<!-- table: options -->\n<!-- endtable -->\n";

        var result = DocTableInjector.Inject(doc, "options", ["| row |"], "d.md");

        Assert.Equal("<!-- table: options -->\n| row |\n<!-- endtable -->\n", result);
    }

    [Fact]
    public void A_second_table_is_injected_without_disturbing_the_first()
    {
        // The close-marker search must start AFTER this table's open marker. Searching from anywhere
        // earlier binds to the previous table's close marker and garbles both blocks.
        const string doc = "<!-- table: a -->\n| akeep |\n<!-- endtable -->\n"
                           + "<!-- table: b -->\n| bold |\n<!-- endtable -->\n";

        var result = DocTableInjector.Inject(doc, "b", ["| bnew |"], "d.md");

        Assert.Equal("<!-- table: a -->\n| akeep |\n<!-- endtable -->\n"
                     + "<!-- table: b -->\n| bnew |\n<!-- endtable -->\n", result);
    }

    [Fact]
    public void A_document_without_the_marker_is_refused()
    {
        var ex = Assert.Throws<DocToolingException>(
            () => DocTableInjector.Inject("Just prose.\n", "options", ["| row |"], "d.md"));

        Assert.Contains("d.md: no '<!-- table: options -->' marker", ex.Message, StringComparison.Ordinal);
        Assert.Contains("rendered from code and has nowhere to go", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unclosed_table_is_refused_not_truncated()
    {
        // The dangerous failure mode this type exists to prevent: treating the rest of the file as
        // table body and deleting it on the next write.
        const string doc = "Intro.\n<!-- table: options -->\n| old |\nEpilogue that must survive.\n";

        var ex = Assert.Throws<DocToolingException>(
            () => DocTableInjector.Inject(doc, "options", ["| row |"], "d.md"));

        Assert.Contains("'<!-- table: options -->' is never closed with '<!-- endtable -->'",
            ex.Message, StringComparison.Ordinal);
        Assert.Contains("Refusing to treat the rest of the file as table body",
            ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_arguments_throw_with_the_offending_parameter_named()
    {
        // The document carries a complete marker pair so a deleted guard is reached by the null it was
        // guarding and lands on the wrong exception type deterministically.
        const string doc = "<!-- table: options -->\n<!-- endtable -->\n";

        var exMarkdown = Assert.Throws<ArgumentNullException>(
            () => DocTableInjector.Inject(null!, "options", ["| row |"], "d.md"));
        var exRows = Assert.Throws<ArgumentNullException>(
            () => DocTableInjector.Inject(doc, "options", null!, "d.md"));

        Assert.Equal("markdown", exMarkdown.ParamName);
        Assert.Equal("renderedRows", exRows.ParamName);
    }
}
