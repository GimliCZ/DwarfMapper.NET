// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.DocTooling;

/// <summary>
///     Resolves repository-relative paths from the test-run working directory. Every consumer needs these
///     and none of them should re-implement the walk-up.
/// </summary>
public static class RepoLayout
{
    private static string? _root;

    /// <summary>
    ///     The repository root, found by walking up from the running assembly to the directory holding
    ///     <c>DwarfMapper.NET.sln</c>.
    /// </summary>
    public static string Root
    {
        get
        {
            if (_root is not null) return _root;
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Combine(dir, "DwarfMapper.NET.sln")))
                dir = Path.GetDirectoryName(dir);

            return _root = dir
                           ?? throw new DocToolingException(
                               "Could not find DwarfMapper.NET.sln above " + AppContext.BaseDirectory
                               + ". The doc pipeline reads and rewrites files in the working tree, so it "
                               + "cannot run detached from the repository.");
        }
    }

    /// <summary>The <c>docs/</c> directory.</summary>
    public static string Docs => Path.Combine(Root, "docs");

    /// <summary>The <c>samples/</c> directory — the corpus every snippet is extracted from.</summary>
    public static string Samples => Path.Combine(Root, "samples");

    /// <summary>The Gallery project directory, whose <c>NN_*.cs</c> files carry the examples.</summary>
    public static string GalleryRoot => Path.Combine(Samples, "DwarfMapper.Gallery");
}
