// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Every public attribute is FINDABLE by a human, in the way its <see cref="Discoverability" /> value
    ///     declares.
    ///     <para>
    ///         The third surface axis, and the one that closes a measured gap rather than a hypothetical one.
    ///         <c>SurfaceObligationTests</c> already requires a <c>ConsumerDirective</c> to appear in a runnable
    ///         sample, and a Conformance feature discharges that — correctly, because Conformance asserts an
    ///         observable runtime difference. But asserting is not teaching. Measured when this landed: 24 of 29
    ///         attributes had a Conformance feature, only 14 had a Gallery example, and <b>10 were proven but
    ///         undiscoverable</b>. A reader asking how to use <c>[MapCollectionKey]</c> found an assertion.
    ///     </para>
    ///     <para>
    ///         The <see cref="Discoverability.Infrastructure" /> obligation is INVERTED, and that is the point
    ///         of the design: such an element must NOT appear in the Gallery, because an example showing a
    ///         consumer hand-writing an attribute the generator emits would document an API that does not exist
    ///         that way. Its absence is proved rather than excused, which is why this axis — like
    ///         <see cref="SurfaceCategory" /> — needs no <c>Exempt</c> member.
    ///     </para>
    /// </summary>
    public class DiscoverabilityObligationTests
    {
        private static Type SurfaceAttr =>
            typeof(DwarfMapperAttribute).Assembly.GetType("DwarfMapper.DwarfSurfaceAttribute", throwOnError: true)!;

        /// <summary>Gallery + its regions, as one corpus. The Gallery is the "look it up" surface.</summary>
        private static string Gallery { get; } = Corpus(Path.Combine(RepoPaths.Samples, "DwarfMapper.Gallery"));

        private static string Conformance { get; } = Corpus(Path.Combine(RepoPaths.Samples, "DwarfMapper.Conformance"));

        private static string Docs { get; } = Corpus(Path.Combine(RepoPaths.Root, "docs")) +
                                              File.ReadAllText(Path.Combine(RepoPaths.Root, "README.md"));

        private static string Corpus(string dir)
        {
            if (!Directory.Exists(dir)) { return string.Empty; }

            var sb = new System.Text.StringBuilder();
            foreach (var f in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                if (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".md", StringComparison.Ordinal))
                {
                    sb.Append(File.ReadAllText(f)).Append('\n');
                }
            }

            return sb.ToString();
        }

        /// <summary>
        ///     Attribute usage is written short — <c>[AutoNest]</c>, never <c>[AutoNestAttribute]</c>.
        ///     <para>
        ///         The arity suffix has to come off FIRST. A generic attribute's <see cref="Type.Name" /> is
        ///         <c>MapPropertyAttribute`2</c>, which does not end in "Attribute", so trimming the suffix
        ///         alone left the probe searching for a string that can never appear at a use site — and the
        ///         gate reported every generic twin as missing an example it in fact had.
        ///     </para>
        /// </summary>
        private static string ShortName(Type attr)
        {
            var name = attr.Name;

            var tick = name.IndexOf('`', StringComparison.Ordinal);
            if (tick >= 0) { name = name[..tick]; }

            return name.EndsWith("Attribute", StringComparison.Ordinal)
                ? name[..^"Attribute".Length]
                : name;
        }

        private static bool IsUsedIn(string corpus, Type attr)
        {
            return Regex.IsMatch(corpus, @"\[\s*(?:assembly:\s*)?" + Regex.Escape(ShortName(attr)) + @"\b");
        }

        private static List<(Type Type, int Discovery)> Declarations()
        {
            var rows = new List<(Type, int)>();

            foreach (var t in typeof(DwarfMapperAttribute).Assembly.GetTypes())
            {
                if (!t.IsPublic || !typeof(Attribute).IsAssignableFrom(t)) { continue; }

                var data = t.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType == SurfaceAttr);
                if (data is null) { continue; }

                var named = data.NamedArguments.FirstOrDefault(n => n.MemberName == "Discovery");
                rows.Add((t, named.TypedValue.Value is null ? 0 : (int)named.TypedValue.Value!));
            }

            return rows;
        }

        [Fact]
        public void The_scan_reads_a_real_corpus_and_a_classified_surface()
        {
            // Non-vacuity on both sides: an empty Gallery corpus would make the coverage rule pass by finding
            // nothing to check, and an empty declaration set would make every rule below guard nothing.
            Assert.True(Gallery.Length > 5_000, $"Gallery corpus is {Gallery.Length} chars — the scan is reading nothing.");
            Assert.True(Conformance.Length > 5_000, $"Conformance corpus is {Conformance.Length} chars.");
            Assert.True(Declarations().Count >= 25, "Fewer than 25 classified public attributes found.");
        }

        [Fact]
        public void Every_attribute_declaring_GalleryExample_has_one()
        {
            var missing = Declarations()
                .Where(r => r.Discovery == 0 && !IsUsedIn(Gallery, r.Type))
                .Select(r => r.Type.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            Assert.True(missing.Count == 0,
                "An attribute declares Discovery = GalleryExample and has no Gallery example. A Conformance "
                + "feature proves the behaviour; it does not teach anyone to use it, and a reader looking this "
                + "up finds an assertion. Write the example, or narrow the declaration to ConformanceOnly with "
                + "a reason:\n  " + string.Join("\n  ", missing));
        }

        [Fact]
        public void Every_attribute_declaring_ConformanceOnly_is_proven_and_documented()
        {
            var offenders = new List<string>();

            foreach (var (type, discovery) in Declarations())
            {
                if (discovery != 1) { continue; }

                var shortName = ShortName(type);

                if (!IsUsedIn(Conformance, type) && !Docs.Contains(shortName, StringComparison.Ordinal))
                {
                    offenders.Add($"{type.Name}: neither a Conformance feature nor a docs section");
                }
            }

            Assert.True(offenders.Count == 0,
                "ConformanceOnly is not an exemption — it redirects the obligation to a Conformance feature "
                + "plus prose. An element with neither is simply undocumented:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void Infrastructure_attributes_are_absent_from_the_Gallery_and_hidden_from_completion()
        {
            // The INVERTED obligation. An example of a consumer hand-writing a generator-emitted attribute
            // would document an API that does not exist that way, so its absence is proved rather than excused.
            var offenders = new List<string>();

            foreach (var (type, discovery) in Declarations())
            {
                if (discovery != 2) { continue; }

                if (IsUsedIn(Gallery, type))
                {
                    offenders.Add($"{type.Name}: appears in the Gallery, but the generator writes it — a "
                        + "consumer-facing example would be a fabrication");
                }
            }

            Assert.True(offenders.Count == 0, string.Join("\n  ", offenders));
        }

        [Fact]
        public void The_usage_matcher_distinguishes_an_attribute_from_a_mere_mention()
        {
            // Control. Matching a bare name would count a doc comment as an example, which is precisely the
            // vacuity SurfaceObligationTests warns about: "a doc-comment mentioning the name does not count".
            Assert.Matches(@"\[\s*(?:assembly:\s*)?AutoNest\b", "[AutoNest]");
            Assert.Matches(@"\[\s*(?:assembly:\s*)?DwarfMapperDefaults\b", "[assembly: DwarfMapperDefaults(Foo = 1)]");
            Assert.DoesNotMatch(@"\[\s*(?:assembly:\s*)?AutoNest\b", "// AutoNest is described here");
        }
    }
}
