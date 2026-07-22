// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Text;

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>
///     The manifest file: one sorted line per case, "<id> <sha256>". Never auto-created — a missing manifest
///     fails with instructions, because a self-healing golden file lets CI silently bless whatever it produced.
/// </summary>
internal static class GoldenManifest
{
    public const string UpdateEnvVar = "DWARF_GOLDEN_UPDATE";

    public static string Path =>
        System.IO.Path.Combine(RepoRoot(), "tests", "DwarfMapper.Generator.Tests", "Golden",
            "output-manifest.txt");

    public static IReadOnlyDictionary<string, string> Load()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(Path)) return result;

        foreach (var line in File.ReadAllLines(Path))
        {
            if (line.Length == 0 || line[0] == '#') continue;

            var space = line.LastIndexOf(' ');
            if (space <= 0) continue;

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
