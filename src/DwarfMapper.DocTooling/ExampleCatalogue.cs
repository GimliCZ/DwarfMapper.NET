// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Reflection;
using DwarfMapper.Gallery;

namespace DwarfMapper.DocTooling
{
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
            var files = GalleryFiles();

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

        /// <summary>Every Gallery source file the catalogue reads, build output excluded.</summary>
        // Internal rather than private, for IsNotBuildOutput's reason: this is the other half of "which files
        // do we read", and no test through Scan() can tell "*.cs" from a pattern that matches everything.
        // THE MUTATION THAT BLANKS THE PATTERN DOES NOT NARROW IT - Directory.GetFiles treats an empty
        // searchPattern as "every file", so it returns MORE (57 against 17 in this assembly's own folder), and
        // it survived a mutation leg because today's corpus has no non-.cs file whose name starts with an example ordinal. The pattern is a contract, so it is
        // pinned where a test can reach it.
        internal static List<string> GalleryFiles()
        {
            return Directory
                .GetFiles(RepoLayout.GalleryRoot, "*.cs", SearchOption.AllDirectories)
                .Where(IsNotBuildOutput)
                .ToList();
        }

        // Internal rather than private: the exclusion is a path-shape contract the tests pin directly —
        // the live corpus keeps no marker-bearing sources under bin/obj, so no test through Scan() can
        // tell a correct predicate from a broken one.
        internal static bool IsNotBuildOutput(string path)
        {
            return !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal) &&
                   !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal);
        }

        // Internal rather than private: this is the file-list seam. Both refusals below exist to stop a
        // silently wrong index, and the real Gallery is well-formed by construction, so they are reachable
        // only with a synthetic type and a controlled file list.
        internal static DocExampleEntry Build(Type type, DocExampleAttribute attr, List<string> galleryFiles)
        {
            var run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static) ??
                      throw new DocToolingException(
                          $"[DocExample] type {type.FullName} has no 'public static void Run()'. The Gallery " + "runner invokes it by reflection, so an example without one would be indexed but " + "never run.");

            var prefix = attr.Ordinal.ToString("D2", CultureInfo.InvariantCulture) + "_";
            var matches = galleryFiles
                .Where(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.Ordinal))
                .ToList();

            if (matches.Count != 1)
            {
                throw new DocToolingException(
                    $"[DocExample({attr.Ordinal}, …)] on {type.Name} resolves to {matches.Count} files matching " +
                    $"'{prefix}*.cs' under {RepoLayout.GalleryRoot} (expected exactly 1)" +
                    (matches.Count > 1 ? ": " + string.Join(", ", matches.Select(Path.GetFileName)) : ".") +
                    " Ordinal binds an example to its file; zero matches means the file was renamed, and two " +
                    "would bind the index entry to whichever was found first.");
            }

            return new DocExampleEntry(
                attr.Ordinal,
                attr.Tier.ToString(),
                attr.Title,
                attr.Shows,
                Path.GetRelativePath(RepoLayout.Root, matches[0]).Replace('\\', '/'),
                run);
        }
    }
}
