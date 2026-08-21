// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Unit tests for the options-table renderer (T3 family D). Every formatting and fallback path here is
///     reachable only through option shapes <c>DwarfMapperAttribute</c> does not currently have — which is
///     exactly the case the renderer exists to survive: the table is generated so that a NEW option appears
///     automatically. Synthetic option types stand in for that future so the paths are pinned before they
///     are needed, not discovered broken by them.
/// </summary>
public class OptionTableRendererTests
{
    [Fact]
    public void Rows_render_type_default_and_carried_prose_in_committed_order()
    {
        // Curated order: Limit deliberately before Named. New options append AFTER the committed rows,
        // in ordinal name order — reversing that tiebreak is a different document.
        const string committed = "Intro.\n\n<!-- table: options -->\n"
                                 + "| Option | Type | Default | What it does |\n"
                                 + "|---|---|---|---|\n"
                                 + "| `Limit` | `int?` | `3` | Caps things. |\n"
                                 + "| `Named` | `string` | `\"abc\"` | Names things. |\n"
                                 + "<!-- endtable -->\n";

        var rows = OptionTableRenderer.RenderRows(typeof(SyntheticOptions), committed, "options");

        Assert.Equal(
        [
            "| Option | Type | Default | What it does |",
            "|---|---|---|---|",
            "| `Limit` | `int?` | `3` | Caps things. |",
            "| `Named` | `string` | `\"abc\"` | Names things. |",
            "| `Absent` | `string` | `null` |  |",
            "| `Count2` | `int` | `7` |  |",
            "| `Empty` | `string` | `\"\"` |  |",
            "| `Flag` | `bool` | `true` |  |",
            "| `Mode` | `Flavour` | `Sour` |  |"
        ], rows);
    }

    [Fact]
    public void A_type_without_a_parameterless_constructor_renders_em_dashes()
    {
        var rows = OptionTableRenderer.RenderRows(typeof(NoDefaults), "", "options");

        Assert.Equal(
        [
            "| Option | Type | Default | What it does |",
            "|---|---|---|---|",
            "| `Value` | `int` | — |  |"
        ], rows);
    }

    [Fact]
    public void A_throwing_constructor_falls_back_to_em_dashes()
    {
        // Activator wraps the ctor's throw in TargetInvocationException; the renderer must catch it and
        // degrade to "no defaults" rather than letting doc generation die on a constructible-looking type.
        var rows = OptionTableRenderer.RenderRows(typeof(ThrowingCtor), "", "options");

        Assert.Equal(
        [
            "| Option | Type | Default | What it does |",
            "|---|---|---|---|",
            "| `Value` | `int` | — |  |"
        ], rows);
    }

    [Fact]
    public void A_ragged_committed_row_is_skipped_and_a_five_cell_row_still_carries_prose()
    {
        // The ragged row (too few cells) must be skipped, not crash the reader; the five-cell row (no
        // trailing pipe) sits exactly on the `< 5` boundary and its prose must still be carried.
        const string committed = "<!-- table: options -->\n"
                                 + "| Option | Type | Default | What it does |\n"
                                 + "|---|---|---|---|\n"
                                 + "| `Empty` | ragged |\n"
                                 + "| `Named` | `string` | `\"abc\"` | Carried prose\n"
                                 + "<!-- endtable -->\n";

        var rows = OptionTableRenderer.RenderRows(typeof(SyntheticOptions), committed, "options");

        Assert.Contains("| `Named` | `string` | `\"abc\"` | Carried prose |", rows);
        Assert.Contains("| `Empty` | `string` | `\"\"` |  |", rows);
    }

    [Fact]
    public void Prose_reading_stops_at_the_endtable_marker()
    {
        // A pipe row AFTER the close marker must never be read as prose: the reader that ran past the
        // marker would let any later table (or example output) silently re-caption real options.
        const string committed = "<!-- table: options -->\n"
                                 + "| Option | Type | Default | What it does |\n"
                                 + "|---|---|---|---|\n"
                                 + "| `Named` | `string` | `\"abc\"` | Real prose. |\n"
                                 + "<!-- endtable -->\n"
                                 + "| `Named` | `string` | `\"abc\"` | HIJACKED |\n";

        var rows = OptionTableRenderer.RenderRows(typeof(SyntheticOptions), committed, "options");

        Assert.Contains("| `Named` | `string` | `\"abc\"` | Real prose. |", rows);
        Assert.DoesNotContain(rows, r => r.Contains("HIJACKED", StringComparison.Ordinal));
    }

    [Fact]
    public void Header_and_separator_rows_never_masquerade_as_prose()
    {
        // OptionNamedOption's property is literally called "Option". Only the literal header/separator
        // filter keeps the header's "What it does" cell from becoming that option's documentation.
        const string committed = "<!-- table: options -->\n"
                                 + "| Option | Type | Default | What it does |\n"
                                 + "|---|---|---|---|\n"
                                 + "<!-- endtable -->\n";

        var rows = OptionTableRenderer.RenderRows(typeof(OptionNamedOption), committed, "options");

        Assert.Equal(
        [
            "| Option | Type | Default | What it does |",
            "|---|---|---|---|",
            "| `Option` | `string` | `\"x\"` |  |"
        ], rows);
    }

    [Fact]
    public void Committed_crlf_markdown_still_carries_prose()
    {
        const string committed = "<!-- table: options -->\r\n"
                                 + "| Option | Type | Default | What it does |\r\n"
                                 + "|---|---|---|---|\r\n"
                                 + "| `Named` | `string` | `\"abc\"` | Survives CRLF. |\r\n"
                                 + "<!-- endtable -->\r\n";

        var rows = OptionTableRenderer.RenderRows(typeof(SyntheticOptions), committed, "options");

        Assert.Contains("| `Named` | `string` | `\"abc\"` | Survives CRLF. |", rows);
    }

    [Fact]
    public void An_unclosed_committed_table_reads_to_the_end_without_crashing()
    {
        // The committed file is hand-edited; a missing close marker must degrade to "read to the end",
        // not walk one index past it.
        const string committed = "<!-- table: options -->\n"
                                 + "| Option | Type | Default | What it does |\n"
                                 + "|---|---|---|---|\n"
                                 + "| `Named` | `string` | `\"abc\"` | To the end. |";

        var rows = OptionTableRenderer.RenderRows(typeof(SyntheticOptions), committed, "options");

        Assert.Contains("| `Named` | `string` | `\"abc\"` | To the end. |", rows);
    }

    [Fact]
    public void Undocumented_options_are_reported_for_genuine_option_rows_only()
    {
        var undocumented = OptionTableRenderer.UndocumentedOptions(
        [
            "| Option | Type | Default | What it does |",
            "|---|---|---|---|",
            "| `A` | `int` | `1` |  |",
            "| `B` | `int` | `1` | documented |",
            "| `C` | `int` | `1` | ",
            "| plain | not an option row | x |  |"
        ]);

        // A: blank prose, six cells. C: blank prose on the five-cell boundary — still an option row.
        // The separator and the backtick-less row must stay outside the population entirely.
        Assert.Equal(["A", "C"], undocumented);
    }

    [Fact]
    public void Null_arguments_throw_with_the_offending_parameter_named()
    {
        var exType = Assert.Throws<ArgumentNullException>(
            () => OptionTableRenderer.RenderRows(null!, "", "options"));
        var exRows = Assert.Throws<ArgumentNullException>(
            () => OptionTableRenderer.UndocumentedOptions(null!));

        Assert.Equal("attributeType", exType.ParamName);
        Assert.Equal("renderedRows", exRows.ParamName);
    }
}

// The synthetic option types live at namespace level (CA1034 forbids visible nested types). They are
// reflection fixtures for OptionTableRendererTests only.

/// <summary>Tiny enum so the <c>Enum</c> formatting arm has a case.</summary>
public enum Flavour
{
    Sweet,
    Sour
}

/// <summary>
///     One property per formatting path: non-empty string, empty string, nullable int, null default,
///     bool, enum, plain int — plus a read-only property the <c>CanWrite</c> filter must drop.
/// </summary>
public sealed class SyntheticOptions
{
    public string Named { get; set; } = "abc";

    public string Empty { get; set; } = "";

    public int? Limit { get; set; } = 3;

    public string? Absent { get; set; }

    public bool Flag { get; set; } = true;

    public Flavour Mode { get; set; } = Flavour.Sour;

    public int Count2 { get; set; } = 7;

    public string ReadOnlyThing { get; } = "never rendered";
}

/// <summary>No parameterless constructor, so defaults cannot be instantiated at all.</summary>
public sealed class NoDefaults(int value)
{
    public int Value { get; set; } = value;
}

/// <summary>A parameterless constructor that throws — the <c>TargetInvocationException</c> path.</summary>
public sealed class ThrowingCtor
{
    public ThrowingCtor() => throw new InvalidOperationException("defaults are not constructible");

    public int Value { get; set; }
}

/// <summary>
///     A property deliberately named like the header's first cell: the header/separator skip in the
///     prose reader must key on the literal words, or this option would inherit "What it does" as prose.
/// </summary>
public sealed class OptionNamedOption
{
    public string Option { get; set; } = "x";
}
