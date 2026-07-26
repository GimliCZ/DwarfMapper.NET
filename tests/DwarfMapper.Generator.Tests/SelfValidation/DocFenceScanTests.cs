// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     The ratchet. Every <c>csharp</c> fence must be inside a snippet marker pair or carry an explicit
///     exemption, so hand-written C# cannot creep back into the documentation after the conversion.
///     <para>
///         Scoped to csharp fences on purpose. diff/bash/xml/ini fences are out of scope by language and need
///         no marker: competitor "before" code cannot compile here, and a shell command is not an API.
///     </para>
/// </summary>
public class DocFenceScanTests
{
    private const string ExemptMarker = "<!-- fence-exempt:";

    /// <summary>
    ///     Documents whose csharp fences are not yet converted, with the task that converts them. This list
    ///     must only SHRINK. An entry that is no longer needed fails the companion test below, so the ratchet
    ///     tightens as the conversion lands rather than being quietly retained.
    /// </summary>
    private static readonly Dictionary<string, string> Unconverted = new(StringComparer.Ordinal)
    {
        ["README.md"] = "converted in Task 9",
        ["docs/howto/ambient-cross-assembly-maps.md"] = "converted in Task 10",
        ["docs/howto/common-changes.md"] = "converted in Task 10",
        ["docs/howto/migrate-from-automapper.md"] = "converted in Task 10",
        ["docs/howto/migrate-from-handwritten.md"] = "converted in Task 10",
        ["docs/howto/migrate-from-mapster.md"] = "converted in Task 10",
        ["docs/diagnostics.md"] = "converted in phase 4, when its tables are injected"
    };

    [Fact]
    public void No_hand_written_csharp_fence_outside_a_snippet_or_an_exemption()
    {
        var offenders = new List<string>();

        foreach (var relative in DocSet.All.Where(d => !Unconverted.ContainsKey(d)))
            offenders.AddRange(UnbackedFences(relative, DocSet.Read(relative)));

        Assert.True(offenders.Count == 0,
            "Hand-written C# fence(s) found. Back each with a '<!-- snippet: id -->' pair whose region lives "
            + "in a compiled sample, or mark it '<!-- fence-exempt: reason -->' immediately above:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Every_unconverted_entry_still_has_an_unbacked_fence()
    {
        // An entry left behind after conversion would silently re-permit hand-written fences in that file.
        var fixedUp = Unconverted.Keys
            .Where(d => UnbackedFences(d, DocSet.Read(d)).Count == 0)
            .ToList();

        Assert.True(fixedUp.Count == 0,
            "These documents have no unbacked csharp fences left — remove them from Unconverted so the "
            + "ratchet tightens: " + string.Join(", ", fixedUp));
    }

    /// <summary>
    ///     Returns "path:line" for every csharp fence that is neither inside a snippet marker pair nor
    ///     immediately preceded by an exemption comment.
    /// </summary>
    private static List<string> UnbackedFences(string relative, string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var offenders = new List<string>();
        var insideSnippet = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith("<!-- snippet:", StringComparison.Ordinal)) insideSnippet = true;
            else if (string.Equals(trimmed, "<!-- endsnippet -->", StringComparison.Ordinal))
                insideSnippet = false;

            if (!trimmed.StartsWith("```csharp", StringComparison.Ordinal) || insideSnippet) continue;

            var preceding = PrecedingNonBlankLine(lines, i);
            if (preceding?.StartsWith(ExemptMarker, StringComparison.Ordinal) != true)
                offenders.Add($"{relative}:{i + 1}");
        }

        return offenders;
    }

    /// <summary>The nearest non-blank line above <paramref name="index" />, trimmed.</summary>
    private static string? PrecedingNonBlankLine(string[] lines, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.Length != 0) return trimmed;
        }

        return null;
    }
}
