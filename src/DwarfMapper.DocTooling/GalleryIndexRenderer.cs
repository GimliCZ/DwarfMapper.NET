// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using DwarfMapper.Gallery;

namespace DwarfMapper.DocTooling;

/// <summary>
///     Renders the Gallery index from the reflected catalogue, grouped by tier. Replaces a hand-maintained
///     table that no test touched — deleting an example used to leave its row behind indefinitely.
/// </summary>
public static class GalleryIndexRenderer
{
    public static IReadOnlyList<string> RenderRows()
    {
        var rows = new List<string> { "| # | Example | Shows |", "|---|---|---|" };
        string? tier = null;

        foreach (var e in ExampleCatalogue.Scan())
        {
            if (!string.Equals(tier, e.Tier, StringComparison.Ordinal))
            {
                tier = e.Tier;
                rows.Add($"| | **{TierName.Of(Enum.Parse<Tier>(e.Tier))}** | |");
            }

            // Paths are rendered relative to the Gallery directory, since that is where this README sits.
            var file = Path.GetRelativePath(
                    Path.Combine(RepoLayout.Root, "samples", "DwarfMapper.Gallery"),
                    Path.Combine(RepoLayout.Root, e.RelativeFile))
                .Replace('\\', '/');

            rows.Add(string.Create(CultureInfo.InvariantCulture,
                $"| {e.Ordinal:D2} | [`{file}`]({file}) — {e.Title} | {e.Shows} |"));
        }

        return rows;
    }
}
