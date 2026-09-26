// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.DocTooling
{
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
                if (_root is not null)
                {
                    return _root;
                }

                return _root = ResolveRoot(AppContext.BaseDirectory);
            }
        }

        /// <summary>
        ///     The repository root at or above <paramref name="startDirectory" />, or the failure that says the
        ///     pipeline cannot run where it was started.
        /// </summary>
        /// <remarks>
        ///     Takes the starting directory rather than reading <see cref="AppContext.BaseDirectory" /> itself so
        ///     that both outcomes can be stated: a test cannot move the base directory of its own process, so
        ///     until this was split out, the not-found failure was unreachable and every test ran from inside the
        ///     repository, where the walk always succeeds.
        /// </remarks>
        internal static string ResolveRoot(string startDirectory)
        {
            return FindRoot(startDirectory) ??
                   throw new DocToolingException(
                       "Could not find DwarfMapper.NET.sln above " + startDirectory + ". The doc pipeline reads and rewrites files in the working tree, so it " + "cannot run detached from the repository.");
        }

        /// <summary>
        ///     Walks up from <paramref name="startDirectory" /> looking for the solution file, and returns
        ///     <see langword="null" /> when it reaches the top without finding one.
        /// </summary>
        internal static string? FindRoot(string startDirectory)
        {
            var dir = startDirectory;
            while (dir is not null && !File.Exists(Path.Combine(dir, "DwarfMapper.NET.sln")))
                dir = Path.GetDirectoryName(dir);

            return dir;
        }

        /// <summary>The <c>docs/</c> directory.</summary>
        public static string Docs => Path.Combine(Root, "docs");

        /// <summary>The <c>samples/</c> directory — the corpus every snippet is extracted from.</summary>
        public static string Samples => Path.Combine(Root, "samples");

        /// <summary>The Gallery project directory, whose <c>NN_*.cs</c> files carry the examples.</summary>
        public static string GalleryRoot => Path.Combine(Samples, "DwarfMapper.Gallery");
    }
}
