// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.DocTooling;

/// <summary>One extractable region of sample source, already dedented and ready to sit inside a fence.</summary>
public sealed record SnippetRegion(string Id, string Body, string RelativeFile, int StartLine);

/// <summary>
///     Finds <c>// &lt;snippet: id&gt;</c> … <c>// &lt;/snippet&gt;</c> regions in sample source. This is the
///     source-scanning half of the pipeline; the compiled sample is the truth and this is how the docs read
///     it, so a snippet cannot describe code that does not build.
///     <para>
///         Every malformed shape throws rather than degrading. The injector writes into tracked files, and a
///         marker bug that silently dropped or truncated a region would be data loss in the documentation.
///     </para>
/// </summary>
public static class SnippetScanner
{
    private const string OpenPrefix = "// <snippet:";
    private const string CloseMarker = "// </snippet>";

    /// <summary>
    ///     Every region in every sample file, keyed by id. Duplicate ids across files are refused here rather
    ///     than resolved, because "whichever was found first" is not a documentation contract.
    /// </summary>
    public static IReadOnlyDictionary<string, SnippetRegion> ScanAll()
    {
        var result = new Dictionary<string, SnippetRegion>(StringComparer.Ordinal);

        foreach (var path in Directory
                     .GetFiles(RepoLayout.Samples, "*.cs", SearchOption.AllDirectories)
                     .Where(IsNotBuildOutput)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(RepoLayout.Root, path).Replace('\\', '/');
            foreach (var region in ScanFile(relative, File.ReadAllText(path)))
            {
                if (result.TryGetValue(region.Id, out var first))
                    throw new DocToolingException(
                        $"Duplicate snippet id '{region.Id}': {first.RelativeFile}:{first.StartLine} and "
                        + $"{region.RelativeFile}:{region.StartLine}. A doc marker must resolve to exactly "
                        + "one region — rename one of them.");

                result[region.Id] = region;
            }
        }

        return result;
    }

    private static bool IsNotBuildOutput(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal)
        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal);

    /// <summary>Parses one file's regions. <paramref name="relativePath" /> is used only for messages.</summary>
    public static IReadOnlyList<SnippetRegion> ScanFile(string relativePath, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var found = new List<SnippetRegion>();
        var body = new List<string>();
        string? openId = null;
        var openLine = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith(OpenPrefix, StringComparison.Ordinal))
            {
                var id = ParseId(trimmed, relativePath, i + 1);
                if (openId is not null)
                    throw new DocToolingException(
                        $"{relativePath}:{i + 1}: snippet '{id}' opens while '{openId}' (line {openLine}) is "
                        + "still open. Nested snippet regions are not supported.");

                openId = id;
                openLine = i + 1;
                body.Clear();
                continue;
            }

            if (string.Equals(trimmed, CloseMarker, StringComparison.Ordinal))
            {
                if (openId is null)
                    throw new DocToolingException(
                        $"{relativePath}:{i + 1}: '{CloseMarker}' with no matching open marker.");

                found.Add(new SnippetRegion(
                    openId, Dedent(body, openId, relativePath, openLine), relativePath, openLine));
                openId = null;
                continue;
            }

            if (openId is not null) body.Add(lines[i]);
        }

        if (openId is not null)
            throw new DocToolingException(
                $"{relativePath}:{openLine}: snippet '{openId}' is never closed with '{CloseMarker}'.");

        return found;
    }

    private static string ParseId(string trimmedLine, string relativePath, int line)
    {
        var close = trimmedLine.IndexOf('>', StringComparison.Ordinal);
        if (close < 0)
            throw new DocToolingException(
                $"{relativePath}:{line}: malformed snippet marker '{trimmedLine}' — expected "
                + "'// <snippet: id>'.");

        var id = trimmedLine[OpenPrefix.Length..close].Trim();
        if (id.Length == 0)
            throw new DocToolingException($"{relativePath}:{line}: snippet marker has an empty id.");

        return id;
    }

    /// <summary>
    ///     Removes the longest whitespace prefix common to every non-blank line. Matched as a STRING, not
    ///     counted as characters: a tab is one character but not one space, so counting would cut a
    ///     tab-indented line at the wrong offset and corrupt the rendered snippet.
    /// </summary>
    private static string Dedent(List<string> body, string id, string relativePath, int openLine)
    {
        var kept = new List<string>(body);
        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[0])) kept.RemoveAt(0);
        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1])) kept.RemoveAt(kept.Count - 1);

        if (kept.Count == 0)
            throw new DocToolingException(
                $"{relativePath}:{openLine}: snippet '{id}' is empty. An empty region renders as an empty "
                + "code fence, which reads as \"this feature needs no code\".");

        // A body carrying the injector's own closing marker has no correct rendering: once written into a
        // document, the next run would find THAT line first and treat everything after it as prose, garbling
        // the file. Refusing is the only safe answer, and no real sample needs such a line.
        var marker = kept.FirstOrDefault(l =>
            string.Equals(l.Trim(), "<!-- endsnippet -->", StringComparison.Ordinal)
            || string.Equals(l.Trim(), "<!-- endtable -->", StringComparison.Ordinal));

        if (marker is not null)
            throw new DocToolingException(
                $"{relativePath}:{openLine}: snippet '{id}' contains the line '{marker.Trim()}', which is an "
                + "injector marker. Once written into a document the next run would end the block there and "
                + "treat the rest of the file as prose. Remove the line or narrow the region.");

        var nonBlank = kept.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var prefix = Whitespace(nonBlank[0]);
        foreach (var line in nonBlank)
        {
            var w = Whitespace(line);
            while (prefix.Length > 0 && !w.StartsWith(prefix, StringComparison.Ordinal))
                prefix = prefix[..^1];
        }

        return string.Join('\n', kept.Select(l =>
            string.IsNullOrWhiteSpace(l) ? "" : l[prefix.Length..]));
    }

    private static string Whitespace(string line) => line[..(line.Length - line.TrimStart().Length)];
}
