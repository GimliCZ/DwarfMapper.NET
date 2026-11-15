// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using DwarfMapper.Gallery;

namespace DwarfMapper.DocTooling
{
    /// <summary>
    ///     Renders the Gallery index from the reflected catalogue, grouped by tier. Replaces a hand-maintained
    ///     table that no test touched — deleting an example used to leave its row behind indefinitely.
    /// </summary>
    public static class GalleryIndexRenderer
    {
        public static IReadOnlyList<string> RenderRows()
        {
            var rows = new List<string>
            {
                "| # | Example | Shows |",
                "|---|---|---|"
            };
            string? tier = null;

            foreach (var e in ExampleCatalogue.Scan())
            {
                if (!string.Equals(tier, e.Tier, StringComparison.Ordinal))
                {
                    tier = e.Tier;
                    rows.Add($"| | **{TierName.Of(Enum.Parse<Tier>(e.Tier))}** | |");
                }

                var file = RelativeToGallery(e.RelativeFile);

                rows.Add(string.Create(CultureInfo.InvariantCulture,
                    $"| {e.Ordinal:D2} | [`{file}`]({file}) — {e.Title} | {e.Shows} |"));
            }

            return rows;
        }

        /// <summary>
        ///     The lines that head each example's section of the Gallery README, in reading order: <c>## Tier</c> where
        ///     the tier changes, then <c>### N. Title</c> and <c>*file* — shows</c>. The sections are written by hand
        ///     around their snippet markers; comparing their heads with this list is what stops a section from
        ///     sitting under the wrong tier or keeping a title the example no longer has.
        /// </summary>
        public static IReadOnlyList<string> RenderSectionHeads()
        {
            var heads = new List<string>();
            string? tier = null;

            foreach (var e in ExampleCatalogue.Scan())
            {
                if (!string.Equals(tier, e.Tier, StringComparison.Ordinal))
                {
                    tier = e.Tier;
                    heads.Add($"## {TierName.Of(Enum.Parse<Tier>(e.Tier))}");
                }

                heads.Add(string.Create(CultureInfo.InvariantCulture, $"### {e.Ordinal}. {e.Title}"));
                heads.Add($"*{RelativeToGallery(e.RelativeFile)}* — {e.Shows}");
            }

            return heads;
        }

        // Paths are rendered relative to the Gallery directory, since that is where its README sits.
        private static string RelativeToGallery(string relativeFile)
        {
            return Path.GetRelativePath(
                    Path.Combine(RepoLayout.Root, "samples", "DwarfMapper.Gallery"),
                    Path.Combine(RepoLayout.Root, relativeFile))
                .Replace('\\', '/');
        }
    }
}
