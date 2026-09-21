// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Generator.Tests.Contracts
{
    /// <summary>
    ///     The fixture-baseline rule, as a gate rather than as a comment (B5).
    ///     <para>
    ///         <b>The rule:</b> a fixture that cannot compile WITHOUT the element under test can never show that
    ///         element doing nothing. Its baseline emits nothing, so an inert directive produces byte-identical
    ///         (empty) output beside an error and the cell reads <c>UnhonouredButLoud</c> — a verdict that passes
    ///         a claimed endpoint and an unclaimed one alike. The fixture swallows the very reading it was built
    ///         to produce. That happened to <c>[FlattenGraph]</c>, and the repair (giving <c>Src</c> a member the
    ///         baseline needs) was written up in <c>SurfaceFixtures</c> as prose that nothing enforced.
    ///     </para>
    ///     <para>
    ///         <b>The trap, which is why this took a task to write and not a line:</b> a naive "every baseline
    ///         must compile" gate fires on five legitimate fixtures. <c>CaseInsensitive</c>,
    ///         <c>NameConvention</c> and <c>AllowNonPublicMembers</c> exist precisely to REMOVE a
    ///         <c>DWARF001</c>, <c>[Flatten]</c> exists to supply a destination member that has no direct
    ///         source, and <c>[MapDenseEnumKeys]</c> is the only thing that maps a dictionary into an inline
    ///         array — so for those five the broken baseline is the question, the element under test is
    ///         expected to clear it, and the cell reads <c>Honoured</c> for a real reason. They are excepted BY
    ///         NAME below, never by count, and each states the diagnostic id it must still fail with.
    ///     </para>
    ///     <para>
    ///         <b>The exception list is an obligation, not an allowlist.</b> It is asserted in both directions:
    ///         a named fixture whose baseline started compiling fails just as loudly as an unnamed one whose
    ///         baseline stopped, and a named fixture that rotted into a DIFFERENT error fails too. Otherwise the
    ///         names would be a place to put a fixture so that nothing is
    ///         asked of it — which is the shape this repository spent round 20 deleting.
    ///     </para>
    /// </summary>
    public class SurfaceFixtureBaselineTests
    {
        /// <summary>
        ///     The endpoint the rule is measured at, and the scope decision that goes with it.
        ///     <para>
        ///         Whether a fixture poses a question is a property of the TYPE PAIR, and the pair is what
        ///         <c>CreateMap</c> compiles with nothing else in the way. Broken baselines at the OTHER
        ///         endpoints are a property of the endpoint, not of the fixture, and they are deliberately out
        ///         of scope here. Measured 2026-08-22, running this same check across all seven: five fixtures
        ///         (<c>described-enum-to-string</c>, <c>divergent-order-enums</c>, <c>narrowing-conversion</c>,
        ///         <c>nullable-value-to-nonnull</c>, <c>reinterpretable-array-member</c>) carry
        ///         <c>DWARF028</c>+CS8795 at <c>Projection</c> — the mapping is not translatable to an
        ///         expression tree, which is the endpoint's answer and not the fixture's fault — and
        ///         <c>recursive-graph</c> additionally carries CS7036 at <c>SpanMap</c> and <c>AsyncStream</c>.
        ///     </para>
        ///     <para>
        ///         Those are already instrumented, and by the right instrument:
        ///         <c>SurfaceParityTests.The_cells_that_pass_both_claim_branches_are_counted</c> ratchets exactly
        ///         the cells a broken baseline produces, and its own doc comment blesses "a case that
        ///         legitimately changes nothing at an endpoint whose baseline is broken for an unrelated,
        ///         deliberate reason" as honest and permanent. Gating them a second time here would need a
        ///         per-endpoint exception store — a new allowlist — to say what that ratchet already says by
        ///         counting.
        ///     </para>
        /// </summary>
        private const Endpoint ReferenceEndpoint = Endpoint.CreateMap;

        /// <summary>
        ///     The fixtures whose baseline is BROKEN BY DESIGN, each with the diagnostic id it must still be
        ///     broken WITH and the element that is expected to clear it. Keyed by <c>[SurfaceProbe]</c> key, so
        ///     a renamed fixture fails <see cref="Every_named_exception_is_a_real_fixture" /> rather than
        ///     silently losing its exception.
        ///     <para>
        ///         The id is STORED rather than assumed (round 29 T3.2). Four of these five are
        ///         <c>DWARF001</c> — a destination member with no source at all — and the check hard-coded that.
        ///         <c>[MapDenseEnumKeys]</c>'s fixture is broken for a different reason: its member has a source
        ///         and no CONVERSION, which is <c>DWARF005</c>, and a dictionary can never convert to an inline
        ///         array without the directive. Widening the check to "any error" would have turned the list
        ///         into the allowlist its own docstring refuses; carrying the id keeps the rule exactly as
        ///         strong — still broken, and still broken for the stated reason — while letting it describe
        ///         more than one reason.
        ///     </para>
        /// </summary>
        private static readonly Dictionary<string, (string Id, string Why)> BaselineIsBrokenByDesign =
            new(StringComparer.Ordinal)
            {
                ["case-mismatched-member"] = ("DWARF001",
                    "Src.name vs Dst.Name — [DwarfMapper(CaseInsensitive = true)] is what matches them"),
                ["snake-case-member"] = ("DWARF001",
                    "Src.user_name vs Dst.UserName — [DwarfMapper(NameConvention = ...)] is what matches them"),
                ["internal-member"] = ("DWARF001",
                    "Src.Name is internal — [DwarfMapper(AllowNonPublicMembers = true)] is what reaches it"),
                ["flattenable-nested-member"] = ("DWARF001",
                    "Dst.X has no direct source — [Flatten] on Src.Child is what supplies it"),
                ["dense-enum-keyed-member"] = ("DWARF005",
                    "Src.Counts is a Dictionary<DenseKey,int> and Dst.Counts is an [InlineArray] struct — " +
                    "there is no conversion between them and there is not meant to be; [MapDenseEnumKeys] is " +
                    "the only thing that maps one into the other")
            };

        /// <summary>
        ///     Every fixture's baseline compiles at <see cref="ReferenceEndpoint" />, except the four named
        ///     above — which must still be failing <c>DWARF001</c> specifically, or their exception has gone
        ///     stale and is hiding a fixture that now measures something different from what it claims.
        /// </summary>
        [Fact]
        public void Every_fixture_baseline_compiles_unless_it_is_a_named_by_design_exception()
        {
            var shouldCompileButDoesNot = new List<string>();
            var namedButNotItsStatedId = new List<string>();

            foreach (var (key, _) in SurfaceFixtures.All.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var (dwarfKeys, compilerErrors, _) =
                    SurfaceProbe.Baseline(ReferenceEndpoint, key, SurfaceFixtures.Get(key), null);

                var errors = dwarfKeys
                    .Where(k => k.EndsWith(":Error", StringComparison.Ordinal))
                    .Concat(compilerErrors.Select(e => string.Create(CultureInfo.InvariantCulture,
                        $"{e.Key}×{e.Value}")))
                    .ToList();

                if (!BaselineIsBrokenByDesign.TryGetValue(key, out var declared))
                {
                    if (errors.Count > 0)
                    {
                        shouldCompileButDoesNot.Add($"{key}: {string.Join(", ", errors)}");
                    }
                }
                else if (!dwarfKeys.Contains(declared.Id + ":Error", StringComparer.Ordinal))
                {
                    // Not "is it still broken" but "is it still broken FOR THE STATED REASON". A fixture that
                    // healed and one that rotted into some other error are both declarations that stopped
                    // describing their fixture, and both would coast forever under a bare is-it-broken check.
                    namedButNotItsStatedId.Add(
                        $"{key} — declared {declared.Id}, \"{declared.Why}\", but the baseline reports " + (errors.Count == 0 ? "no error at all" : string.Join(", ", errors)));
                }
            }

            Assert.True(shouldCompileButDoesNot.Count == 0,
                $"Surface fixture(s) whose BASELINE does not compile at {ReferenceEndpoint}:\n    " +
                string.Join("\n    ", shouldCompileButDoesNot) +
                "\n\nA fixture that cannot compile without the element under test can never show that element " +
                "doing nothing: the baseline emits nothing, an inert element emits nothing, the outputs match " +
                "byte for byte, and the cell reads UnhonouredButLoud — which passes the claimed branch and the " +
                "unclaimed one alike. Give the fixture whatever member the baseline needs (the [FlattenGraph] " +
                "fixture's Src.Flat exists for exactly this and says so), or, if the broken baseline IS the " +
                "question the element answers, name the fixture in BaselineIsBrokenByDesign with the id it " +
                "fails with and the element that clears it.");

            Assert.True(namedButNotItsStatedId.Count == 0,
                "Fixture(s) named as broken-by-design whose baseline no longer reports the id they declare:\n    " +
                string.Join("\n    ", namedButNotItsStatedId) +
                "\n\nThe exception has gone stale. Its cells no longer read Honoured for the reason the " +
                "declaration gives — they read whatever the element does against a working baseline, which " +
                "may be nothing. Remove the entry so the ordinary rule covers the fixture again, and re-read " +
                "whatever finding cites those cells.");
        }

        [Fact]
        public void Every_named_exception_is_a_real_fixture()
        {
            // A renamed or deleted fixture would otherwise leave its exception behind as a permission slip with
            // nothing under it, and the next fixture to take that [SurfaceProbe] key would inherit it silently.
            var unknown = BaselineIsBrokenByDesign.Keys
                .Where(k => !SurfaceFixtures.All.ContainsKey(k))
                .ToList();

            Assert.True(unknown.Count == 0,
                "Baseline exception(s) naming no fixture: " + string.Join(", ", unknown) + ". Delete the entry or fix the [SurfaceProbe] key it was written against.");

            Assert.False(
                BaselineIsBrokenByDesign.Values.Any(v => string.IsNullOrWhiteSpace(v.Id) ||
                                                         string.IsNullOrWhiteSpace(v.Why)),
                "Every baseline exception must name the id it is broken with AND which element clears it.");
        }
    }
}
