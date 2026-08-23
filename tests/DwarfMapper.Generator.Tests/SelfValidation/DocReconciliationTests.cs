// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using DwarfMapper.DocTooling;
using DwarfMapper.Gallery;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
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
                $"Only {examples.Count} [DocExample] types found. The Gallery has 15 example files, so a " + "smaller number means the reflection filter is dropping them, not that they were deleted.");
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
                "Duplicate [DocExample] ordinals. Ordinal binds an example to its NN_*.cs file, so a collision " + "would bind one example's index entry to another's code:\n" + string.Join("\n", duplicates));
        }

        [Fact]
        public void Every_example_binds_to_exactly_one_source_file()
        {
            // Scan() throws if an ordinal matches zero or two files; this asserts the resolved paths are real
            // and distinct, which a buggy glob could satisfy vacuously by resolving everything to one file.
            var files = ExampleCatalogue.Scan().Select(e => e.RelativeFile).ToList();

            Assert.All(files,
                f => Assert.True(
                    File.Exists(Path.Combine(RepoLayout.Root, f)),
                    $"resolved file does not exist: {f}"));
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
            var regions = DocRegions.All();
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
                "Snippet region(s) in a non-example file that no document references. Reference them, or delete " + "the markers:\n  " + string.Join("\n  ", orphans));
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
                "Gallery example(s) with no '// <snippet: …>' region, so no document can quote them:\n  " + string.Join("\n  ", silent));
        }

        [Fact]
        public void Every_declared_example_is_public()
        {
            // The mutation battery survived swapping GetTypes() for GetExportedTypes() in ExampleCatalogue,
            // because every example happens to be public — an EQUIVALENT mutant, which the battery's own header
            // says must not be catalogued. This test makes the equivalence a rule rather than an accident: if a
            // non-public example is ever added, the two reflection calls diverge and this fails, naming it.
            var assembly = typeof(DocExampleAttribute).Assembly;
            var exported = assembly.GetExportedTypes().ToHashSet();

            var nonPublic = assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<DocExampleAttribute>() is not null)
                .Where(t => !exported.Contains(t))
                .Select(t => t.FullName ?? t.Name)
                .ToList();

            Assert.True(nonPublic.Count == 0,
                "Non-public [DocExample] type(s). An example the Gallery runs but the catalogue could drop is a " + "runner/index divergence waiting to happen — make them public:\n  " + string.Join("\n  ", nonPublic));
        }

        [Fact]
        public void The_published_generator_output_is_real_emitted_code()
        {
            // Without this, a harness that silently returned "" or a header-only stub would render a
            // plausible-looking block, and the drift check would hold that lie stable forever — the exact failure
            // mode The_api_reference_actually_carries_summaries was written for on the reference side.
            var emitted = EmittedCodeCatalogue.Render();

            Assert.Equal(2, emitted.Count);

            var flat = emitted["emitted-flat-map"].Body;
            Assert.Contains("Age = src.Age", flat, StringComparison.Ordinal);
            Assert.Contains("Name = src.Name", flat, StringComparison.Ordinal);
            Assert.Contains("ThrowIfNull", flat, StringComparison.Ordinal);

            // The whole point of the deep-paths example: the dotted string became a real member access.
            var deep = emitted["emitted-deep-paths"].Body;
            Assert.Contains("City = o.Customer.Address.City", deep, StringComparison.Ordinal);

            Assert.All(emitted.Values,
                r => Assert.True(r.Body.Split('\n').Length > 8,
                    $"'{r.Id}' rendered only {r.Body.Split('\n').Length} lines — too short to be a generated mapper."));
        }

        [Fact]
        public void The_catalogue_orders_examples_by_tier_then_ordinal()
        {
            // The catalogue's order IS the reading order: the Gallery runner walks it and the generated index
            // table is rendered from it, in this sequence. Nothing had ever asserted the sequence, so flipping
            // Scan()'s OrderBy/ThenBy to their Descending twins produced a different index and a different
            // runner order while every test still passed — a generated document whose sections reorder between
            // machines is exactly the drift this file exists to stop.
            var ordered = ExampleCatalogue.Scan()
                .Select(e => (TierIndex: (int)Enum.Parse<Tier>(e.Tier), e.Ordinal, e.Title))
                .ToList();

            // Non-vacuity: "sorted" is trivially true of one element, and the ordinal tiebreak is asserted over
            // nothing unless some tier actually holds two examples.
            var tiers = ordered.Select(e => e.TierIndex).Distinct().Count();
            Assert.True(tiers >= 2,
                $"Only {tiers} tier(s) in the catalogue — the tier half of the ordering would be asserted over " + "nothing.");
            Assert.True(ordered.GroupBy(e => e.TierIndex).Any(g => g.Count() >= 2),
                "No tier holds two examples — the ordinal tiebreak would be asserted over nothing.");

            for (var i = 1; i < ordered.Count; i++)
            {
                var previous = ordered[i - 1];
                var current = ordered[i];

                Assert.True(
                    previous.TierIndex < current.TierIndex ||
                    (previous.TierIndex == current.TierIndex && previous.Ordinal < current.Ordinal),
                    $"Catalogue out of order at index {i}: tier {previous.TierIndex} ordinal {previous.Ordinal} " + $"('{previous.Title}') is listed before tier {current.TierIndex} ordinal {current.Ordinal} " + $"('{current.Title}'). Scan() orders by tier, then by ordinal — both ascending.");
            }
        }

        [Fact]
        public void Scanned_regions_enumerate_in_ascending_file_order()
        {
            // ScanAll sorts the sample paths ordinally before reading them, and that sort is the only thing
            // making the scan stable across machines — Directory.GetFiles promises no order at all. The sort's
            // externally visible consequence is the order the returned dictionary enumerates in: Merge only ever
            // ADDS, so the dictionary enumerates in insertion order, and every consumer that walks .Values (the
            // orphan check above, DocRegions, the duplicate-id refusal's "first" file) inherits it. Reversing
            // the sort was invisible to every other test.
            var files = SnippetScanner.ScanAll()
                .Values
                .Select(r => r.RelativeFile)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Assert.True(files.Count >= 2,
                $"Only {files.Count} sample file(s) carry snippet regions — a file-ordering assertion over one " + "file is vacuous.");
            Assert.Equal(files.OrderBy(f => f, StringComparer.Ordinal).ToList(), files);
        }
    }
}
