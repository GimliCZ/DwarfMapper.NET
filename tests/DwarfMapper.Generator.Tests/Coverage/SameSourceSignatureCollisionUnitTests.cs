// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis.Text;

// Unit tests for MapperExtractor.ReportSameSourceSignatureCollisions, widened from private to internal (owner ruling
// 2026-09-13: extract, expose, test — no deletion).
//
// Why no generator fixture reaches three of its arms:
//   - the location FALLBACKS (the colliding model's own location missing, then the owner's): every public model is
//     added to `methods` together with its publicMethodLocs entry — declared methods, [GenerateMap] pairs and the
//     top-level collection route alike — so the first lookup always succeeds;
//   - the DWARF094 "declared partial came LATER than its [GenerateMap] twin" arm, which drops the OWNER and re-points
//     the signature at the declared method: declared methods are extracted before [GenerateMap] pairs, so the later
//     model is always the synthesized one. The comment says the arm is "asserted rather than assumed, in case
//     extraction order ever changes" — this pins what it does when it does.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class SameSourceSignatureCollisionUnitTests
    {
        private static readonly LocationInfo OwnerLoc = Loc(3);
        private static readonly LocationInfo LaterLoc = Loc(9);

        private static LocationInfo Loc(int line) =>
            new("Mapper.cs", new TextSpan(line * 10, 3), new LinePositionSpan(new LinePosition(line, 4), new LinePosition(line, 7)));

        private static MapMethodModel Model(string returnType, bool generated) =>
            new("Map", "public", returnType, "global::Demo.Src", "s", true,
                EquatableArray.From(Array.Empty<MemberMap>()),
                EquatableArray.From(Array.Empty<string>()),
                EquatableArray.From(Array.Empty<HookCall>()),
                false,
                "",
                IsPartial: !generated,
                EmitAsNonPartial: generated);

        private static (List<MapMethodModel> Methods, List<DiagnosticInfo> Diagnostics) Run(Dictionary<int, LocationInfo?> locs, params MapMethodModel[] models)
        {
            var methods = models.ToList();
            var diagnostics = new List<DiagnosticInfo>();
            MapperExtractor.ReportSameSourceSignatureCollisions(methods, locs, diagnostics);
            return (methods, diagnostics);
        }

        [Fact]
        public void A_duplicate_GenerateMap_without_its_own_location_is_reported_at_the_owners_and_the_later_one_dropped()
        {
            var owner = Model("global::Demo.Dst", generated: true);
            var later = Model("global::Demo.Dst", generated: true);

            var (methods, diagnostics) = Run(new Dictionary<int, LocationInfo?> { [0] = OwnerLoc }, owner, later);

            var d = Assert.Single(diagnostics);
            Assert.Equal("DWARF094", d.Descriptor.Id);
            Assert.Equal(OwnerLoc, d.Location);
            Assert.StartsWith("Duplicate [GenerateMap]", d.MessageArg, StringComparison.Ordinal);
            Assert.Same(owner, Assert.Single(methods));
        }

        [Fact]
        public void A_duplicate_with_no_location_on_either_side_is_reported_without_one()
        {
            var (_, diagnostics) = Run(new Dictionary<int, LocationInfo?>(), Model("global::Demo.Dst", generated: true), Model("global::Demo.Dst", generated: true));

            Assert.Null(Assert.Single(diagnostics).Location);
        }

        [Fact]
        public void A_declared_partial_after_its_GenerateMap_twin_keeps_its_slot_and_a_third_duplicate_collides_with_it()
        {
            var generatedFirst = Model("global::Demo.Dst", generated: true);
            var declared = Model("global::Demo.Dst", generated: false);
            var generatedAgain = Model("global::Demo.Dst", generated: true);

            var (methods, diagnostics) = Run(new Dictionary<int, LocationInfo?> { [0] = OwnerLoc, [1] = LaterLoc, [2] = Loc(12) },
                generatedFirst, declared, generatedAgain);

            Assert.Equal(2, diagnostics.Count);
            Assert.All(diagnostics, d => Assert.Equal("DWARF094", d.Descriptor.Id));
            Assert.Equal(LaterLoc, diagnostics[0].Location);
            Assert.StartsWith("[GenerateMap] would emit", diagnostics[0].MessageArg, StringComparison.Ordinal);
            Assert.Contains("already declares a partial", diagnostics[0].MessageArg, StringComparison.Ordinal);
            Assert.Same(declared, Assert.Single(methods));
        }

        [Fact]
        public void A_conflicting_return_type_takes_the_same_location_fallbacks_and_drops_the_later_model()
        {
            var owner = Model("global::Demo.Dst", generated: false);
            var later = Model("global::Demo.Other", generated: true);

            var (methods, diagnostics) = Run(new Dictionary<int, LocationInfo?> { [0] = OwnerLoc }, owner, later);
            var (_, unlocated) = Run(new Dictionary<int, LocationInfo?>(), Model("global::Demo.Dst", generated: false), Model("global::Demo.Other", generated: true));

            var d = Assert.Single(diagnostics);
            Assert.Equal("DWARF060", d.Descriptor.Id);
            Assert.Equal(OwnerLoc, d.Location);
            Assert.Contains("('global::Demo.Dst' and 'global::Demo.Other')", d.MessageArg, StringComparison.Ordinal);
            Assert.Same(owner, Assert.Single(methods));
            Assert.Null(Assert.Single(unlocated).Location);
        }

        [Fact]
        public void Synthesized_update_span_stream_and_two_declared_partials_are_not_collisions_and_a_located_conflict_keeps_its_own_location()
        {
            var declared = Model("global::Demo.Dst", generated: false);
            var synthesized = declared with { IsPartial = false };
            var update = declared with { IsUpdateInto = true };
            var span = declared with { IsSpanMap = true };
            var stream = declared with { IsAsyncStreamMap = true };
            var declaredTwice = Model("global::Demo.Dst", generated: false);
            var conflicting = Model("global::Demo.Other", generated: true);

            var (methods, diagnostics) = Run(new Dictionary<int, LocationInfo?> { [0] = OwnerLoc, [6] = LaterLoc },
                declared, synthesized, update, span, stream, declaredTwice, conflicting);

            // Two identical declared partial DEFINITIONS are the compiler's error in the caller's own file, not a
            // generator collision: nothing reported for them, nothing dropped. The later [GenerateMap] with a different
            // target is DWARF060 at its OWN location.
            var d = Assert.Single(diagnostics);
            Assert.Equal("DWARF060", d.Descriptor.Id);
            Assert.Equal(LaterLoc, d.Location);
            Assert.Equal(new[] { declared, synthesized, update, span, stream, declaredTwice }, methods);
        }
    }
}
