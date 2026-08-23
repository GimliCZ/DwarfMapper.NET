// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;
using DwarfMapper.Gallery;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Unit tests for the catalogue's binding rules, through the internal file-list seam (T3 family C). The
    ///     live Gallery is well-formed by construction, so neither refusal in <c>Build</c> had ever run: the
    ///     first keeps an example from being indexed but never run, the second keeps an index entry from binding
    ///     to whichever file was found first. Both are exercised here with a synthetic type and controlled file
    ///     lists — path strings only, nothing touches the filesystem.
    /// </summary>
    public class ExampleCatalogueTests
    {
        private static string InGallery(string fileName)
        {
            return Path.Combine(RepoLayout.GalleryRoot, fileName);
        }

        [Fact]
        public void A_wellformed_example_binds_to_its_single_file()
        {
            var attr = new DocExampleAttribute(5, Tier.Basics, "Synthetic title")
            {
                Shows = "the file-list seam"
            };
            // The decoy pins the prefix as "05_" — ordinal, two digits, underscore. A prefix that lost its
            // underscore (or its D2 format) would match the decoy too and turn this into a refusal.
            List<string> files = [InGallery("05_Synthetic.cs"), InGallery("059_Decoy.cs"), InGallery("07_Other.cs")];

            var entry = ExampleCatalogue.Build(typeof(SyntheticExample), attr, files);

            Assert.Equal(5, entry.Ordinal);
            Assert.Equal("Basics", entry.Tier);
            Assert.Equal("Synthetic title", entry.Title);
            Assert.Equal("the file-list seam", entry.Shows);
            Assert.Equal("samples/DwarfMapper.Gallery/05_Synthetic.cs", entry.RelativeFile);
            Assert.Equal(typeof(SyntheticExample).GetMethod(nameof(SyntheticExample.Run)), entry.Run);
        }

        [Fact]
        public void A_type_without_a_public_static_run_is_refused()
        {
            var attr = new DocExampleAttribute(1, Tier.Basics, "No runner");

            var ex = Assert.Throws<DocToolingException>(() => ExampleCatalogue.Build(typeof(ExampleWithoutRun), attr, []));

            Assert.Contains(typeof(ExampleWithoutRun).FullName!, ex.Message, StringComparison.Ordinal);
            Assert.Contains("has no 'public static void Run()'", ex.Message, StringComparison.Ordinal);
            Assert.Contains("runner invokes it by reflection", ex.Message, StringComparison.Ordinal);
            Assert.Contains("indexed but never run", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void An_example_whose_file_was_renamed_is_refused()
        {
            var attr = new DocExampleAttribute(7, Tier.Advanced, "Orphaned");

            var ex = Assert.Throws<DocToolingException>(() => ExampleCatalogue.Build(typeof(SyntheticExample), attr, [InGallery("06_Other.cs")]));

            Assert.Contains("resolves to 0 files matching", ex.Message, StringComparison.Ordinal);
            Assert.Contains("'07_*.cs'", ex.Message, StringComparison.Ordinal);
            Assert.Contains("(expected exactly 1).", ex.Message, StringComparison.Ordinal);
            Assert.Contains("zero matches means the file was renamed", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void An_example_matching_two_files_is_refused_naming_both()
        {
            var attr = new DocExampleAttribute(7, Tier.Advanced, "Ambiguous");

            var ex = Assert.Throws<DocToolingException>(() => ExampleCatalogue.Build(
                typeof(SyntheticExample),
                attr,
                [InGallery("07_A.cs"), InGallery("07_B.cs")]));

            Assert.Contains("resolves to 2 files matching", ex.Message, StringComparison.Ordinal);
            Assert.Contains(": 07_A.cs, 07_B.cs", ex.Message, StringComparison.Ordinal);
            Assert.Contains("whichever was found first", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Build_output_folders_are_excluded_from_the_catalogue()
        {
            // The predicate is a path-shape contract: bin/ and obj/ are excluded INDEPENDENTLY — a predicate
            // that only excluded paths under both at once would let generated sources into the catalogue.
            var sep = Path.DirectorySeparatorChar;

            Assert.False(ExampleCatalogue.IsNotBuildOutput($"g{sep}obj{sep}Debug{sep}05_A.cs"));
            Assert.False(ExampleCatalogue.IsNotBuildOutput($"g{sep}bin{sep}Debug{sep}05_A.cs"));
            Assert.True(ExampleCatalogue.IsNotBuildOutput($"g{sep}05_A.cs"));
        }
    }

// The synthetic example types live at namespace level (CA1034 forbids visible nested types). They are
// reflection fixtures for ExampleCatalogueTests only.

    /// <summary>A well-formed example: <c>public static void Run()</c> exists, as the runner requires.</summary>
    public static class SyntheticExample
    {
        public static void Run()
        {
            // Nothing to do — the catalogue binds to the method, it never invokes it.
        }
    }

    /// <summary>Deliberately missing <c>public static void Run()</c> — the first refusal's shape.</summary>
    public sealed class ExampleWithoutRun;
}
