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

    [Fact]
    public void No_snippet_region_outside_a_declared_example_is_orphaned()
    {
        // A region no document references is maintained forever and read by nobody — but only if the file it
        // sits in exists SOLELY to be quoted. A region inside a declared [DocExample] is already reader-facing:
        // the file is linked from the generated index and the example runs on every `dotnet run`, so an
        // unquoted one is a doc opportunity, not dead weight. Scoping the rule that way keeps it aimed at the
        // decay it was written for: a region in, say, AotSample, which nothing links and nobody runs by hand.
        var regions = SnippetScanner.ScanAll();
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var relative in DocSet.All)
            referenced.UnionWith(
                DocSnippetInjector.Inject(DocSet.Read(relative), regions, relative).ReferencedIds);

        var exampleFiles = ExampleCatalogue.Scan()
            .Select(e => e.RelativeFile)
            .ToHashSet(StringComparer.Ordinal);

        var orphans = regions.Values
            .Where(r => !referenced.Contains(r.Id) && !exampleFiles.Contains(r.RelativeFile))
            .Select(r => $"{r.Id} ({r.RelativeFile}:{r.StartLine})")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(orphans.Count == 0,
            "Snippet region(s) in a non-example file that no document references. Reference them, or delete "
            + "the markers:\n  " + string.Join("\n  ", orphans));
    }

    [Fact]
    public void Every_gallery_example_owns_at_least_one_snippet_region()
    {
        // An example the docs cannot quote is invisible to every reader who does not browse samples/.
        // Enforced only for the Gallery: regions in AotSample exist to be quoted, not to be examples.
        var regions = SnippetScanner.ScanAll().Values
            .GroupBy(r => r.RelativeFile, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var silent = ExampleCatalogue.Scan()
            .Where(e => !regions.ContainsKey(e.RelativeFile))
            .Select(e => $"{e.Ordinal:D2} {e.Title} ({e.RelativeFile})")
            .ToList();

        Assert.True(silent.Count == 0,
            "Gallery example(s) with no '// <snippet: …>' region, so no document can quote them:\n  "
            + string.Join("\n  ", silent));
    }
}
