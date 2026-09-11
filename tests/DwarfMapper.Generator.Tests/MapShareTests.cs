// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>[MapShare]</c> and the automatic share: the emitted text, the refusals, and the two properties the
    ///     feature cannot be allowed to lose — that the proof terminates, and that it never accepts an interface
    ///     as evidence of immutability.
    /// </summary>
    public class MapShareTests
    {
        /// <summary>The shapes every test here maps, so the assertions differ only in what they claim.</summary>
        private const string Shapes = """
                                      using DwarfMapper;
                                      using System.Collections.Generic;
                                      using System.Collections.Immutable;
                                      namespace Demo;
                                      public sealed class Badge { public Badge(string n) { Name = n; } public string Name { get; } }
                                      public sealed record Tag(string Text);
                                      public sealed class Loose { public string Name { get; set; } = ""; }
                                      """;

        [Fact]
        public void An_immutable_list_of_a_proven_element_is_shared_with_no_helper()
        {
            var gen = GeneratorAssert.CompilesClean(Shapes + """
                                                            public sealed class A { public ImmutableList<Badge> Items { get; init; } = null!; }
                                                            public sealed class B { public ImmutableList<Badge> Items { get; init; } = null!; }
                                                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                                                            """);

            // The reference is assigned. The guard reproduces the copying helper's `if (src is null) return
            // Empty;` arm and names a cached singleton, so nothing is allocated on either branch.
            Assert.Contains("Items = a.Items ?? global::System.Collections.Immutable.ImmutableList<global::Demo.Badge>.Empty",
                gen,
                StringComparison.Ordinal);
            Assert.DoesNotContain("__DwarfMapColl", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void An_immutable_array_is_shared_behind_an_IsDefault_guard()
        {
            var gen = GeneratorAssert.CompilesClean(Shapes + """
                                                            public sealed class A { public ImmutableArray<Badge> Items { get; init; } }
                                                            public sealed class B { public ImmutableArray<Badge> Items { get; init; } }
                                                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                                                            """);

            // `is null` against ImmutableArray<T> is CS0037 — it is a struct that is never null and can still
            // wrap a null array — so the two guard forms are not interchangeable.
            Assert.Contains(
                "Items = a.Items.IsDefault ? global::System.Collections.Immutable.ImmutableArray<global::Demo.Badge>.Empty : a.Items",
                gen,
                StringComparison.Ordinal);
            Assert.DoesNotContain("__DwarfMapColl", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_sealed_record_element_is_proven()
        {
            // Every record declares `protected virtual Type EqualityContract { get; }`. Nothing in the consumer's
            // source mentions it, so a proof that refused System.Type would refuse every record in existence
            // through a member the consumer never wrote — which is why ImmutabilityProof names it.
            var gen = GeneratorAssert.CompilesClean(Shapes + """
                                                            public sealed class A { public ImmutableList<Tag> Tags { get; init; } = null!; }
                                                            public sealed class B { public ImmutableList<Tag> Tags { get; init; } = null!; }
                                                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                                                            """);

            Assert.DoesNotContain("__DwarfMapColl", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void An_interface_is_not_accepted_as_evidence_and_is_copied()
        {
            // THE ruling this feature is built on. IReadOnlyList<T> is an interface, not a guarantee: a List<T>
            // behind it is still a List<T> at run time and the source can mutate it after the map. A refusal
            // costs a copy; a wrong acceptance costs correctness.
            var gen = GeneratorAssert.CompilesClean(Shapes + """
                                                            public sealed class A { public IReadOnlyList<Badge> Items { get; init; } = null!; }
                                                            public sealed class B { public IReadOnlyList<Badge> Items { get; init; } = null!; }
                                                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                                                            """);

            Assert.Contains("__DwarfMapColl", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void MapShare_forces_the_share_on_an_interface_the_proof_cannot_see_through()
        {
            var gen = GeneratorAssert.CompilesClean(Shapes + """
                                                            public sealed class A { public IReadOnlyList<Badge> Items { get; init; } = null!; }
                                                            public sealed class B { public IReadOnlyList<Badge> Items { get; init; } = null!; }
                                                            [DwarfMapper] public partial class M { [MapShare("Items")] public partial B Map(A a); }
                                                            """);

            Assert.Contains("Items = a.Items ?? global::System.Array.Empty<global::Demo.Badge>()",
                gen,
                StringComparison.Ordinal);
            Assert.DoesNotContain("__DwarfMapColl", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_settable_member_is_refused_even_when_MapShare_asks_for_it()
        {
            // The caller cannot assert away a fact the generator can see. `Loose.Name` has a setter, so two
            // graphs sharing one Loose could diverge after the map — and nothing would report it.
            const string src = """
                               public sealed class A { public ImmutableList<Loose> Items { get; init; } = null!; }
                               public sealed class B { public ImmutableList<Loose> Items { get; init; } = null!; }
                               [DwarfMapper] public partial class M { [MapShare("Items")] public partial B Map(A a); }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(Shapes + src);

            var d = Assert.Single(diagnostics, x => x.Id == "DWARF104");
            var message = d.GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("not immutable: sharing would alias mutable state", message, StringComparison.Ordinal);
            Assert.Contains("Loose.Name", message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_settable_member_is_copied_by_the_automatic_path_without_a_diagnostic()
        {
            // The automatic path refuses in silence, because a refusal is what every member got yesterday. Only
            // the caller who ASKED gets told.
            var (diagnostics, gen) = GeneratorAssert.CompilesCleanWithDiagnostics(Shapes + """
                                                                                          public sealed class A { public ImmutableList<Loose> Items { get; init; } = null!; }
                                                                                          public sealed class B { public ImmutableList<Loose> Items { get; init; } = null!; }
                                                                                          [DwarfMapper] public partial class M { public partial B Map(A a); }
                                                                                          """);

            Assert.Contains("__DwarfMapColl", gen, StringComparison.Ordinal);
            Assert.DoesNotContain(diagnostics, x => x.Id == "DWARF104");
        }

        [Fact]
        public void A_mutable_element_behind_an_interface_is_still_refused_by_MapShare()
        {
            // The interface's own verdict is a GAP the caller may assert past; a mutable type argument is a
            // FACT, and it survives the gap. Without the type-argument walk in the interface arm, [MapShare]
            // would have shared a sequence of provably mutable elements on the caller's word.
            const string src = """
                               public sealed class A { public IReadOnlyList<Loose> Items { get; init; } = null!; }
                               public sealed class B { public IReadOnlyList<Loose> Items { get; init; } = null!; }
                               [DwarfMapper] public partial class M { [MapShare("Items")] public partial B Map(A a); }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(Shapes + src);

            Assert.Contains(diagnostics,
                x => x.Id == "DWARF104" &&
                     x.GetMessage(CultureInfo.InvariantCulture)
                         .Contains("not immutable: sharing would alias mutable state", StringComparison.Ordinal));
        }

        [Fact]
        public void A_reference_cycle_refuses_rather_than_looping()
        {
            // The property this feature cannot be allowed to lose. `Node` reaches itself, so the proof re-enters
            // a type it is already judging; it answers Unprovable and unwinds. If it looped instead, this test
            // would hang rather than fail — which is why it asserts on the COPY being emitted: reaching the
            // assertion at all is half the proof.
            var gen = GeneratorAssert.CompilesClean(Shapes + """
                                                            public sealed class Node { public Node(ImmutableList<Node> kids) { Kids = kids; } public ImmutableList<Node> Kids { get; } }
                                                            public sealed class A { public ImmutableList<Node> Roots { get; init; } = null!; }
                                                            public sealed class B { public ImmutableList<Node> Roots { get; init; } = null!; }
                                                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                                                            """);

            Assert.Contains("__DwarfMapColl", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void MapShare_naming_no_destination_member_reports_DWARF104()
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(Shapes + """
                                                                     public sealed class A { public ImmutableList<Badge> Items { get; init; } = null!; }
                                                                     public sealed class B { public ImmutableList<Badge> Items { get; init; } = null!; }
                                                                     [DwarfMapper] public partial class M { [MapShare("Nope")] public partial B Map(A a); }
                                                                     """);

            Assert.Contains(diagnostics,
                d => d.Id == "DWARF104" &&
                     d.GetMessage(CultureInfo.InvariantCulture).Contains("Nope", StringComparison.Ordinal));
        }

        [Fact]
        public void MapShare_across_two_different_types_reports_DWARF104()
        {
            // Sharing performs no conversion at all, so "same type both sides" is not a convenience — it is what
            // makes assigning the reference a MAPPING.
            var (diagnostics, _) = GeneratorTestHarness.Run(Shapes + """
                                                                     public sealed class A { public ImmutableList<Badge> Items { get; init; } = null!; }
                                                                     public sealed class B { public ImmutableArray<Badge> Items { get; init; } }
                                                                     [DwarfMapper] public partial class M { [MapShare("Items")] public partial B Map(A a); }
                                                                     """);

            Assert.Contains(diagnostics,
                d => d.Id == "DWARF104" &&
                     d.GetMessage(CultureInfo.InvariantCulture)
                         .Contains("same type", StringComparison.Ordinal));
        }

        [Fact]
        public void An_array_member_is_never_shared()
        {
            // T[] is the canonical shared-mutable-state shape: its elements are settable through any reference
            // to it. This is a DISPROOF, not a gap, so even [MapShare] cannot take it.
            var (diagnostics, _) = GeneratorTestHarness.Run(Shapes + """
                                                                     public sealed class A { public Badge[] Items { get; init; } = null!; }
                                                                     public sealed class B { public Badge[] Items { get; init; } = null!; }
                                                                     [DwarfMapper] public partial class M { [MapShare("Items")] public partial B Map(A a); }
                                                                     """);

            Assert.Contains(diagnostics,
                d => d.Id == "DWARF104" &&
                     d.GetMessage(CultureInfo.InvariantCulture)
                         .Contains("not immutable: sharing would alias mutable state", StringComparison.Ordinal));
        }

        [Fact]
        public void The_share_reaches_update_into()
        {
            var gen = GeneratorAssert.CompilesClean(Shapes + """
                                                            public sealed class A { public ImmutableList<Badge> Items { get; set; } = null!; }
                                                            public sealed class B { public ImmutableList<Badge> Items { get; set; } = null!; }
                                                            [DwarfMapper] public partial class M { public partial void Update(A a, B b); }
                                                            """);

            Assert.Contains("b.Items = a.Items ?? global::System.Collections.Immutable.ImmutableList<global::Demo.Badge>.Empty",
                gen,
                StringComparison.Ordinal);
            Assert.DoesNotContain("__DwarfMapColl", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_MapProperty_rename_shares_too()
        {
            var gen = GeneratorAssert.CompilesClean(Shapes + """
                                                            public sealed class A { public ImmutableList<Badge> Source { get; init; } = null!; }
                                                            public sealed class B { public ImmutableList<Badge> Target { get; init; } = null!; }
                                                            [DwarfMapper] public partial class M { [MapProperty("Source", "Target")] public partial B Map(A a); }
                                                            """);

            Assert.Contains("Target = a.Source ?? global::System.Collections.Immutable.ImmutableList<global::Demo.Badge>.Empty",
                gen,
                StringComparison.Ordinal);
            Assert.DoesNotContain("__DwarfMapColl", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void Nothing_is_shared_under_NullCollectionStrategy_AsNull()
        {
            // Review fix. The refusal used to be asked of the COLLECTION resolution's own shape flag, which
            // left a same-type ImmutableDictionary sharing under AsNull while the dictionary helper it
            // replaced returned null for a null source — a divergence visible only to a caller who had opted
            // into the option, which is the worst place for one. The question is now asked once, of the
            // option, ahead of any shape analysis.
            var gen = GeneratorAssert.CompilesClean(Shapes + """
                                                            public sealed class A { public ImmutableDictionary<string, Badge>? Items { get; init; } }
                                                            public sealed class B { public ImmutableDictionary<string, Badge>? Items { get; init; } }
                                                            [DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
                                                            public partial class M { public partial B Map(A a); }
                                                            """);

            Assert.Contains("__DwarfMapDict", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void MapShare_under_AsNull_is_refused_rather_than_ignored()
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(Shapes + """
                                                                     public sealed class A { public ImmutableList<Badge>? Items { get; init; } }
                                                                     public sealed class B { public ImmutableList<Badge>? Items { get; init; } }
                                                                     [DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
                                                                     public partial class M { [MapShare("Items")] public partial B Map(A a); }
                                                                     """);

            Assert.Contains(diagnostics,
                d => d.Id == "DWARF104" &&
                     d.GetMessage(CultureInfo.InvariantCulture)
                         .Contains("NullCollectionStrategy.AsNull", StringComparison.Ordinal));
        }

        [Fact]
        public void MapShare_beside_a_value_transforming_MapProperty_is_refused_rather_than_ignored()
        {
            // Review fix. Standing aside for the conversion is right; standing aside SILENTLY is the "accepted
            // it, changed nothing, said nothing" shape — the caller wrote two directives and one of them did
            // nothing, with no way to tell which.
            var (diagnostics, _) = GeneratorTestHarness.Run(Shapes + """
                                                                     public sealed class A { public ImmutableList<Badge> Items { get; init; } = null!; }
                                                                     public sealed class B { public ImmutableList<Badge> Items { get; init; } = null!; }
                                                                     [DwarfMapper] public partial class M
                                                                     {
                                                                         [MapShare("Items")]
                                                                         [MapProperty("Items", "Items", Use = nameof(Reverse))]
                                                                         public partial B Map(A a);
                                                                         public static ImmutableList<Badge> Reverse(ImmutableList<Badge> s) => s.Reverse();
                                                                     }
                                                                     """);

            Assert.Contains(diagnostics,
                d => d.Id == "DWARF104" &&
                     d.GetMessage(CultureInfo.InvariantCulture)
                         .Contains("cannot both apply", StringComparison.Ordinal));
        }

        [Fact]
        public void MapShare_on_a_MapValue_target_is_refused_rather_than_ignored()
        {
            // [MapValue] claims the member before auto-matching sees it, and the member IS writable, so the
            // unknown-name check would have passed this in silence.
            var (diagnostics, _) = GeneratorTestHarness.Run(Shapes + """
                                                                     public sealed class A { public ImmutableList<Badge> Items { get; init; } = null!; }
                                                                     public sealed class B { public string Tag { get; init; } = ""; public ImmutableList<Badge> Items { get; init; } = null!; }
                                                                     [DwarfMapper] public partial class M
                                                                     {
                                                                         [MapShare("Tag")]
                                                                         [MapValue("Tag", "api-v2")]
                                                                         public partial B Map(A a);
                                                                     }
                                                                     """);

            Assert.Contains(diagnostics,
                d => d.Id == "DWARF104" &&
                     d.GetMessage(CultureInfo.InvariantCulture)
                         .Contains("[MapValue]", StringComparison.Ordinal));
        }

        [Fact]
        public void A_When_predicate_survives_the_share()
        {
            // Review fix, and the one that was RED. The share's branch in ResolveExplicitMaps `continue`s
            // above Phase 8, where NullSubstitute= and When= are read — so a provable member carrying
            // [MapProperty(When = ...)] was emitted as an UNCONDITIONAL assignment and the predicate simply
            // vanished. Verbatim the defect the EndpointContractMatrix preamble was written about, and one
            // that matrix cannot see here because its fixture has no collection member.
            var gen = GeneratorAssert.CompilesClean(Shapes + """
                                                            public sealed class A { public ImmutableList<Badge> Items { get; set; } = null!; }
                                                            public sealed class B { public ImmutableList<Badge> Items { get; set; } = null!; }
                                                            [DwarfMapper] public partial class M
                                                            {
                                                                [MapProperty("Items", "Items", When = nameof(Wanted))]
                                                                public partial B Map(A a);
                                                                public static bool Wanted(A a) => a.Items.Count > 0;
                                                            }
                                                            """);

            Assert.Contains("if (Wanted(a))", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void MapShare_beside_a_When_predicate_is_refused_rather_than_ignored()
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(Shapes + """
                                                                     public sealed class A { public ImmutableList<Badge> Items { get; set; } = null!; }
                                                                     public sealed class B { public ImmutableList<Badge> Items { get; set; } = null!; }
                                                                     [DwarfMapper] public partial class M
                                                                     {
                                                                         [MapShare("Items")]
                                                                         [MapProperty("Items", "Items", When = nameof(Wanted))]
                                                                         public partial B Map(A a);
                                                                         public static bool Wanted(A a) => a.Items.Count > 0;
                                                                     }
                                                                     """);

            Assert.Contains(diagnostics,
                d => d.Id == "DWARF104" &&
                     d.GetMessage(CultureInfo.InvariantCulture).Contains("When=", StringComparison.Ordinal));
        }

        [Fact]
        public void MapShare_on_a_member_that_was_never_copied_says_nothing()
        {
            // Review fix. TryPlanShare used to run the proof BEFORE asking whether the member takes a helper
            // at all, so [MapShare] on a same-type MUTABLE class reported "sharing would alias mutable
            // state" — about a member the generator raw-assigns by default and goes on raw-assigning the
            // moment the attribute is deleted. The refusal was true of nothing: there was no copy to refuse.
            var (diagnostics, gen) = GeneratorAssert.CompilesCleanWithDiagnostics(Shapes + """
                                                                                          public sealed class A { public Loose Child { get; init; } = null!; }
                                                                                          public sealed class B { public Loose Child { get; init; } = null!; }
                                                                                          [DwarfMapper] public partial class M { [MapShare("Child")] public partial B Map(A a); }
                                                                                          """);

            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF104");
            Assert.Contains("Child = a.Child", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_nested_immutable_collection_is_proven_and_not_mistaken_for_a_cycle()
        {
            // Review fix. The visiting set was keyed on the ORIGINAL DEFINITION, so the inner
            // ImmutableList<int> re-entered the outer ImmutableList<...>'s entry and the proof reported a
            // "reference cycle" about a perfectly ordinary nested collection — the safe direction (a copy),
            // and a false statement. Keyed on the CONSTRUCTED type it is proven, and a true cycle still
            // terminates, because a true cycle recurs at the IDENTICAL constructed symbol.
            var gen = GeneratorAssert.CompilesClean(Shapes + """
                                                            public sealed class A { public ImmutableList<ImmutableList<int>> Rows { get; init; } = null!; }
                                                            public sealed class B { public ImmutableList<ImmutableList<int>> Rows { get; init; } = null!; }
                                                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                                                            """);

            Assert.DoesNotContain("__DwarfMapColl", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_declared_converter_for_the_member_still_wins_over_the_share()
        {
            // The share must never take a member away from a conversion the caller wrote. Use= names the
            // converter explicitly, so the share stands aside rather than silently replacing it — the failure
            // class round 29 T0.2c found in the blit, checked here before it can happen again.
            var gen = GeneratorAssert.CompilesClean(Shapes + """
                                                            public sealed class A { public ImmutableList<Badge> Items { get; init; } = null!; }
                                                            public sealed class B { public ImmutableList<Badge> Items { get; init; } = null!; }
                                                            [DwarfMapper] public partial class M
                                                            {
                                                                [MapProperty("Items", "Items", Use = nameof(Reverse))]
                                                                public partial B Map(A a);
                                                                public static ImmutableList<Badge> Reverse(ImmutableList<Badge> s) => s.Reverse();
                                                            }
                                                            """);

            Assert.Contains("Items = Reverse(a.Items)", gen, StringComparison.Ordinal);
        }

        // The four arms of DWARF104's "which modifier" message. The mutation leg found every one of them
        // UNCOVERED: 293 mutants tested, and the four-way ternary that names the conflicting modifier had no
        // test reaching it at all. The test above proves the AUTOMATIC path stands aside for a converter; none
        // proved what happens when the caller ASKED for a share and also wrote a modifier, which is the case
        // the diagnostic exists for. A guard nothing exercises is a guard nobody has checked fires.

        [Theory]
        [InlineData("Use = nameof(Reverse)", "Use=")]
        [InlineData("When = nameof(Always)", "When=")]
        [InlineData("NullSubstitute = \"none\"", "NullSubstitute=")]
        [InlineData("StringFormat = \"G\"", "StringFormat=")]
        public void MapShare_beside_a_modifier_names_that_modifier_in_the_refusal(string modifier, string named)
        {
            var src = $$"""
                        public sealed class A { public ImmutableList<Badge> Items { get; init; } = null!; }
                        public sealed class B { public ImmutableList<Badge> Items { get; init; } = null!; }
                        [DwarfMapper] public partial class M
                        {
                            [MapShare("Items")]
                            [MapProperty("Items", "Items", {{modifier}})]
                            public partial B Map(A a);
                            public static ImmutableList<Badge> Reverse(ImmutableList<Badge> s) => s.Reverse();
                            public static bool Always(A a) => true;
                        }
                        """;
            var (diagnostics, _) = GeneratorTestHarness.Run(Shapes + src);

            var d = Assert.Single(diagnostics, x => x.Id == "DWARF104");
            var message = d.GetMessage(CultureInfo.InvariantCulture);

            // The point of the arm is that it names the RIGHT modifier. A message that said "Use=" for every
            // conflict would pass a test that only asserted the id, and would send the caller to the wrong
            // line of their own source.
            Assert.Contains(named, message, StringComparison.Ordinal);
            // And the opening literal — the one that says WHICH member and WHICH directive — is a separate
            // string the modifier assertion does not reach; the mutation leg blanked it without a failure.
            Assert.Contains("[MapShare] member 'Items' also carries a [MapProperty]", message, StringComparison.Ordinal);
        }
    }
}
