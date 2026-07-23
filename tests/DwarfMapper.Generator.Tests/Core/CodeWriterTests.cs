// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Core;

namespace DwarfMapper.Generator.Tests.Core;

/// <summary>
///     Unit tests for the emission primitive. The golden manifest proves the COMPOSITION is right; these prove
///     the primitive itself is, which the manifest cannot isolate.
/// </summary>
public class CodeWriterTests
{
    [Fact]
    public void Writes_lines_with_LF_and_no_indent_at_level_zero()
    {
        var w = new CodeWriter();
        w.Line("a").Line("b");

        Assert.Equal("a\nb\n", w.ToString());
    }

    [Fact]
    public void Indent_adds_four_spaces_per_level_and_restores_on_dispose()
    {
        var w = new CodeWriter();
        w.Line("outer");
        using (w.Indent())
        {
            w.Line("inner");
            using (w.Indent()) w.Line("deeper");
        }

        w.Line("back");

        Assert.Equal("outer\n    inner\n        deeper\nback\n", w.ToString());
    }

    [Fact]
    public void Blank_line_carries_no_trailing_whitespace()
    {
        // Current generated output has no trailing spaces. Emitting indentation on an empty line would
        // change bytes on nearly every generated file and move the golden manifest.
        var w = new CodeWriter();
        using (w.Indent())
        {
            w.Line("x");
            w.Line();
            w.Line("y");
        }

        Assert.Equal("    x\n\n    y\n", w.ToString());
    }

    [Fact]
    public void Block_emits_braces_at_the_header_level_and_indents_the_body()
    {
        var w = new CodeWriter();
        using (w.Block("if (x)")) w.Line("y();");

        Assert.Equal("if (x)\n{\n    y();\n}\n", w.ToString());
    }

    [Fact]
    public void Raw_bypasses_indentation_and_adds_no_newline()
    {
        // Helper bodies are spliced pre-formatted (MapEmitter.cs:44 appends synth.Code verbatim), so Raw must
        // not re-indent or terminate them.
        var w = new CodeWriter();
        using (w.Indent()) w.Raw("already   formatted\n");

        Assert.Equal("already   formatted\n", w.ToString());
    }

    [Fact]
    public void Initial_indent_offsets_every_line()
    {
        var w = new CodeWriter(2);
        w.Line("x");

        Assert.Equal("        x\n", w.ToString());
    }
}
