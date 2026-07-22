// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Text;

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>
///     The manifest file: one sorted line per case, "<id> <sha256>". <see cref="Load" /> never auto-creates it —
///     a missing file simply comes back as an empty dictionary; it is the caller's job (see
///     <c>GoldenCorpusTests.Generated_output_matches_the_golden_manifest</c>) to treat that as a failure with
///     instructions, because a self-healing golden file lets CI silently bless whatever it produced. A malformed
///     line is never silently dropped either — it throws, so the manifest can never shrink unnoticed.
/// </summary>
internal static class GoldenManifest
{
    public const string UpdateEnvVar = "DWARF_GOLDEN_UPDATE";

    public static string Path =>
        System.IO.Path.Combine(RepoRoot(), "tests", "DwarfMapper.Generator.Tests", "Golden",
            "output-manifest.txt");

    public static IReadOnlyDictionary<string, string> Load()
    {
        if (!File.Exists(Path)) return new Dictionary<string, string>(StringComparer.Ordinal);

        return ParseLines(File.ReadAllLines(Path));
    }

    /// <summary>
    ///     The line parser, split out from <see cref="Load" /> so it can be exercised directly against in-memory
    ///     lines instead of the real, shared manifest file on disk (tests run in parallel across classes).
    ///     Blank lines and '#' comments are skipped; anything else without a space separator is malformed and
    ///     throws rather than being silently dropped — a manifest that quietly shrinks would be misread as a
    ///     legitimate corpus change by the added/removed detection in <c>GoldenCorpusTests</c>.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ParseLines(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || line[0] == '#') continue;

            var space = line.LastIndexOf(' ');
            if (space <= 0)
                throw new InvalidDataException(
                    $"Malformed manifest line {i + 1}: '{line}'. Never edit the manifest by hand; regenerate it "
                    + $"deliberately with {UpdateEnvVar}=1.");

            result[line[..space]] = line[(space + 1)..];
        }

        return result;
    }

    public static void Write(IReadOnlyDictionary<string, string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"# DwarfMapper golden output manifest — {entries.Count} cases.\n");
        sb.Append("# One line per pinned case: <case-id> <sha256 of generated source + full diagnostics>.\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"# Regenerate deliberately with {UpdateEnvVar}=1; never edit by hand.\n");

        foreach (var kv in entries.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            sb.Append(CultureInfo.InvariantCulture, $"{kv.Key} {kv.Value}\n");

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, sb.ToString());
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("DwarfMapper.NET.sln").Length == 0)
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not locate the repository root (DwarfMapper.NET.sln).");
        return dir!.FullName;
    }
}
