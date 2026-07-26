// SPDX-License-Identifier: GPL-2.0-only

using System.Text;

namespace DwarfMapper.DocTooling;

/// <summary>The rewritten document, plus which snippet ids it referenced (for orphan detection).</summary>
public sealed record InjectionResult(string Markdown, IReadOnlySet<string> ReferencedIds);

/// <summary>
///     Rewrites <c>&lt;!-- snippet: id --&gt;</c> … <c>&lt;!-- endsnippet --&gt;</c> blocks in markdown,
///     replacing each body with a fenced extract of the named sample region. Prose outside the markers is
///     copied through untouched — this injects into hand-written documents, it does not generate them.
/// </summary>
public static class DocSnippetInjector
{
    private const string OpenPrefix = "<!-- snippet:";
    private const string CloseMarker = "<!-- endsnippet -->";

    public static InjectionResult Inject(
        string markdown, IReadOnlyDictionary<string, SnippetRegion> regions, string docPath)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(regions);

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var sb = new StringBuilder();
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (!trimmed.StartsWith(OpenPrefix, StringComparison.Ordinal))
            {
                sb.Append(line).Append('\n');
                i++;
                continue;
            }

            var id = ParseId(trimmed, docPath, i + 1);
            if (!regions.TryGetValue(id, out var region))
                throw new DocToolingException(
                    $"{docPath}:{i + 1}: snippet '{id}' is referenced here but no sample defines it. Add a "
                    + $"'// <snippet: {id}>' region to a file under samples/, or fix the id.");

            referenced.Add(id);
            var closeIndex = FindClose(lines, i + 1, docPath, id, i + 1);

            // The marker's own indentation is reapplied to everything emitted. A fence indented under a bullet
            // is legitimate markdown (README.md has two); emitting at column zero would break it out of the
            // list item and silently reflow the document around it.
            var indent = line[..(line.Length - line.TrimStart().Length)];

            // The fence must be LONGER than any backtick run inside the body, or a snippet containing ``` in
            // a string or comment closes the block early: the rest of the code leaks out as prose, and the
            // next run re-reads it as document text. Markdown allows any fence of three or more backticks.
            var fence = new string('`', LongestBacktickRun(region.Body) + 1);

            sb.Append(line).Append('\n');
            sb.Append(indent).Append(fence).Append("csharp\n");
            foreach (var bodyLine in region.Body.Split('\n'))
                sb.Append(bodyLine.Length == 0 ? "" : indent).Append(bodyLine).Append('\n');
            sb.Append(indent).Append(fence).Append('\n');
            sb.Append(lines[closeIndex]).Append('\n');
            i = closeIndex + 1;
        }

        return new InjectionResult(sb.ToString().TrimEnd() + "\n", referenced);
    }

    /// <summary>The longest run of backticks on any line of the body, floored at 2 so the fence is ≥ 3.</summary>
    private static int LongestBacktickRun(string body)
    {
        var longest = 2;
        var run = 0;
        foreach (var c in body)
            if (c == '`')
            {
                run++;
                if (run > longest) longest = run;
            }
            else
            {
                run = 0;
            }

        return longest;
    }

    private static string ParseId(string trimmedLine, string docPath, int line)
    {
        var end = trimmedLine.IndexOf("-->", StringComparison.Ordinal);
        if (end < 0)
            throw new DocToolingException(
                $"{docPath}:{line}: malformed marker '{trimmedLine}' — expected '<!-- snippet: id -->'.");

        var id = trimmedLine[OpenPrefix.Length..end].Trim();
        if (id.Length == 0)
            throw new DocToolingException($"{docPath}:{line}: snippet marker has an empty id.");

        return id;
    }

    /// <summary>
    ///     Finds the closing marker. Running off the end throws rather than treating the rest of the file as a
    ///     snippet body — that would delete every following paragraph on the next write, and it would look
    ///     like a successful regeneration.
    /// </summary>
    private static int FindClose(string[] lines, int from, string docPath, string id, int openLine)
    {
        for (var i = from; i < lines.Length; i++)
            if (string.Equals(lines[i].TrimStart(), CloseMarker, StringComparison.Ordinal))
                return i;

        throw new DocToolingException(
            $"{docPath}:{openLine}: snippet '{id}' is never closed with '{CloseMarker}'. Refusing to treat "
            + "the rest of the file as its body.");
    }
}
