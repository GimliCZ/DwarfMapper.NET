// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Reflection;
using DwarfMapper.Gallery;

namespace DwarfMapper.DocTooling;

/// <summary>One Gallery example, as reflection sees it, bound to the file that defines it.</summary>
public sealed record DocExampleEntry(
    int Ordinal,
    string Tier,
    string Title,
    string Shows,
    string RelativeFile,
    MethodInfo Run);

/// <summary>
///     The example catalogue, read by reflecting over the Gallery assembly. This is the assembly-scanning
///     half of the pipeline: the runner order and the generated index both come from here, so neither is a
///     list anyone maintains by hand.
/// </summary>
public static class ExampleCatalogue
{
    /// <summary>Every declared example, ordered by tier and then ordinal — the reading order.</summary>
    public static IReadOnlyList<DocExampleEntry> Scan()
    {
        var files = Directory
            .GetFiles(RepoLayout.GalleryRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsNotBuildOutput)
            .ToList();

        // GetTypes() rather than GetExportedTypes(): a non-public example would otherwise vanish from the
        // catalogue silently, shrinking the index rather than failing.
        return typeof(DocExampleAttribute).Assembly
            .GetTypes()
            .Select(t => (Type: t, Attr: t.GetCustomAttribute<DocExampleAttribute>()))
            .Where(x => x.Attr is not null)
            .Select(x => Build(x.Type, x.Attr!, files))
            .OrderBy(e => (int)Enum.Parse<Tier>(e.Tier))
            .ThenBy(e => e.Ordinal)
            .ToList();
    }

    private static bool IsNotBuildOutput(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal)
        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal);

    private static DocExampleEntry Build(Type type, DocExampleAttribute attr, List<string> galleryFiles)
    {
        var run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
                  ?? throw new DocToolingException(
                      $"[DocExample] type {type.FullName} has no 'public static void Run()'. The Gallery "
                      + "runner invokes it by reflection, so an example without one would be indexed but "
                      + "never run.");

        var prefix = attr.Ordinal.ToString("D2", CultureInfo.InvariantCulture) + "_";
        var matches = galleryFiles
            .Where(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        if (matches.Count != 1)
            throw new DocToolingException(
                $"[DocExample({attr.Ordinal}, …)] on {type.Name} resolves to {matches.Count} files matching "
                + $"'{prefix}*.cs' under {RepoLayout.GalleryRoot} (expected exactly 1)"
                + (matches.Count > 1 ? ": " + string.Join(", ", matches.Select(Path.GetFileName)) : ".")
                + " Ordinal binds an example to its file; zero matches means the file was renamed, and two "
                + "would bind the index entry to whichever was found first.");

        return new DocExampleEntry(
            attr.Ordinal,
            attr.Tier.ToString(),
            attr.Title,
            attr.Shows,
            Path.GetRelativePath(RepoLayout.Root, matches[0]).Replace('\\', '/'),
            run);
    }
}
