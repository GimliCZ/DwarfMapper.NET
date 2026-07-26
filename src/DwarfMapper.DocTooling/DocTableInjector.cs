// SPDX-License-Identifier: GPL-2.0-only

using System.Text;

namespace DwarfMapper.DocTooling;

/// <summary>
///     Rewrites the body of a <c>&lt;!-- table: name --&gt;</c> … <c>&lt;!-- endtable --&gt;</c> pair. Kept
///     separate from the snippet injector because a table's rows are rendered from reflection while a
///     snippet's body is extracted from source — same marker shape, different truth source.
/// </summary>
public static class DocTableInjector
{
    public static string Inject(
        string markdown, string tableName, IReadOnlyList<string> renderedRows, string docPath)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(renderedRows);

        var open = $"<!-- table: {tableName} -->";
        const string close = "<!-- endtable -->";

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = Array.FindIndex(lines, l => string.Equals(l.TrimStart(), open, StringComparison.Ordinal));
        if (start < 0)
            throw new DocToolingException(
                $"{docPath}: no '{open}' marker. The table is rendered from code and has nowhere to go.");

        var end = Array.FindIndex(lines, start + 1,
            l => string.Equals(l.TrimStart(), close, StringComparison.Ordinal));
        if (end < 0)
            throw new DocToolingException(
                $"{docPath}: '{open}' is never closed with '{close}'. Refusing to treat the rest of the file "
                + "as table body.");

        var sb = new StringBuilder();
        for (var i = 0; i <= start; i++) sb.Append(lines[i]).Append('\n');
        foreach (var row in renderedRows) sb.Append(row).Append('\n');
        for (var i = end; i < lines.Length; i++) sb.Append(lines[i]).Append('\n');

        return sb.ToString().TrimEnd() + "\n";
    }
}
