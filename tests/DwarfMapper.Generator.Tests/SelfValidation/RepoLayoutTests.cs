// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     How the doc pipeline finds the repository it is allowed to rewrite. Both outcomes matter: finding the
    ///     root is what every doc test depends on, and NOT finding it has to be a loud, named failure rather than
    ///     a pipeline that quietly rewrites whatever directory it happens to start in.
    /// </summary>
    public sealed class RepoLayoutTests
    {
        [Fact]
        public void The_root_is_the_directory_holding_the_solution_file()
        {
            InTempTree((root, nested) => Assert.Equal(root, RepoLayout.FindRoot(nested)));
        }

        /// <summary>The walk starts where it is told, so a directory that already holds the marker is its own root.</summary>
        [Fact]
        public void A_directory_holding_the_solution_file_is_its_own_root()
        {
            InTempTree((root, _) => Assert.Equal(root, RepoLayout.FindRoot(root)));
        }

        /// <summary>
        ///     Reaching the top of the drive without finding the marker. Until the walk was split out of the
        ///     Root property this could not be reached at all: a test cannot move its own process's base
        ///     directory, and every test runs from inside the repository.
        /// </summary>
        [Fact]
        public void A_directory_with_no_solution_above_it_has_no_root()
        {
            var detached = Path.GetTempPath();

            Assert.Null(RepoLayout.FindRoot(detached));
        }

        /// <summary>
        ///     The failure the pipeline must not run past. The message names the directory it started from,
        ///     because a doc-test run shows nothing else.
        /// </summary>
        [Fact]
        public void Resolving_a_root_that_does_not_exist_fails_naming_the_directory()
        {
            var detached = Path.GetTempPath();

            var ex = Assert.Throws<DocToolingException>(() => RepoLayout.ResolveRoot(detached));

            Assert.Contains(detached, ex.Message, StringComparison.Ordinal);
            Assert.Contains("DwarfMapper.NET.sln", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Resolving_a_root_that_exists_returns_it()
        {
            InTempTree((root, nested) => Assert.Equal(root, RepoLayout.ResolveRoot(nested)));
        }

        /// <summary>
        ///     The three directories the pipeline reads and writes, each stated once. Docs was reached by
        ///     nothing at all: the doc tests build their paths from Root themselves, so the property that names
        ///     the documentation directory had no test and could have pointed anywhere.
        /// </summary>
        [Fact]
        public void The_named_directories_sit_under_the_repository_root()
        {
            Assert.Equal(Path.Combine(RepoLayout.Root, "docs"), RepoLayout.Docs);
            Assert.Equal(Path.Combine(RepoLayout.Root, "samples"), RepoLayout.Samples);
            Assert.Equal(Path.Combine(RepoLayout.Samples, "DwarfMapper.Gallery"), RepoLayout.GalleryRoot);

            // ...and they are real, which is what makes the equality above worth asserting.
            Assert.True(Directory.Exists(RepoLayout.Docs));
            Assert.True(Directory.Exists(RepoLayout.GalleryRoot));
        }

        /// <summary>
        ///     ARCH-06: temp directory only, never the repository. Registered in
        ///     <see cref="RepoWriteGuardTests.Every_raw_write_api_use_in_the_test_tree_is_a_registered_pattern" />.
        /// </summary>
        private static void InTempTree(Action<string, string> body)
        {
            var root = Path.Combine(Path.GetTempPath(), "dwarfmapper-layout-" + Guid.NewGuid().ToString("N"));
            var nested = Path.Combine(root, "src", "Deep");
            Directory.CreateDirectory(nested);
            try
            {
                File.WriteAllText(Path.Combine(root, "DwarfMapper.NET.sln"), "solution marker");
                body(root, nested);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
