// SPDX-License-Identifier: GPL-2.0-only

using System.IO;
using DwarfMapper.Generator.Tests.Framework;

namespace DwarfMapper.Generator.Tests.Golden;

/// <summary>
///     Exercises <see cref="GoldenManifest" />'s line parser directly, against in-memory lines rather than the
///     real, committed manifest file — that file is shared with <c>GoldenCorpusTests</c>, which reads/writes it
///     and may run concurrently in a different test class.
/// </summary>
public class GoldenManifestTests
{
    [Fact]
    public void ParseLines_throws_on_a_line_with_no_space_separator()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => GoldenManifest.ParseLines(["# header", "feat:Basic abc123", "malformed-no-space"]));

        Assert.Contains("Malformed manifest line 3", ex.Message, StringComparison.Ordinal);
        Assert.Contains("malformed-no-space", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseLines_skips_blank_lines_and_comments()
    {
        var result = GoldenManifest.ParseLines([
            "# a comment",
            "",
            "feat:Basic abc123",
        ]);

        var entry = Assert.Single(result);
        Assert.Equal("feat:Basic", entry.Key);
        Assert.Equal("abc123", entry.Value);
    }

    [Fact]
    public void ParseLines_keeps_the_last_space_as_the_separator()
    {
        // Case ids may legitimately contain spaces (e.g. combinatorial cell descriptions); the hash never does,
        // so splitting on the LAST space is the only safe choice.
        var result = GoldenManifest.ParseLines(["id with spaces abc123"]);

        var entry = Assert.Single(result);
        Assert.Equal("id with spaces", entry.Key);
        Assert.Equal("abc123", entry.Value);
    }
}
