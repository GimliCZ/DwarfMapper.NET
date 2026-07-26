// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     The reconciliation contract between the two independent reads of the sample corpus: reflection over
///     the Gallery assembly, and a source scan of samples/**. Either one alone can go stale silently; the
///     point of this file is that they cannot go stale in the same direction.
/// </summary>
public class DocReconciliationTests
{
    [Fact]
    public void Every_gallery_example_is_discovered_by_reflection()
    {
        var examples = ExampleCatalogue.Scan();

        // 15 examples exist as of this task. The assertion is >= so adding one is not a failure, but
        // silently LOSING the catalogue (a reflection filter that matches nothing) is.
        Assert.True(examples.Count >= 15,
            $"Only {examples.Count} [DocExample] types found. The Gallery has 15 example files, so a "
            + "smaller number means the reflection filter is dropping them, not that they were deleted.");
    }

    [Fact]
    public void Example_ordinals_are_unique()
    {
        var duplicates = ExampleCatalogue.Scan()
            .GroupBy(e => e.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(e => e.Title))}")
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Duplicate [DocExample] ordinals. Ordinal binds an example to its NN_*.cs file, so a collision "
            + "would bind one example's index entry to another's code:\n" + string.Join("\n", duplicates));
    }

    [Fact]
    public void Every_example_binds_to_exactly_one_source_file()
    {
        // Scan() throws if an ordinal matches zero or two files; this asserts the resolved paths are real
        // and distinct, which a buggy glob could satisfy vacuously by resolving everything to one file.
        var files = ExampleCatalogue.Scan().Select(e => e.RelativeFile).ToList();

        Assert.All(files, f => Assert.True(
            File.Exists(Path.Combine(RepoLayout.Root, f)), $"resolved file does not exist: {f}"));
        Assert.Equal(files.Count, files.Distinct(StringComparer.Ordinal).Count());
    }
}
