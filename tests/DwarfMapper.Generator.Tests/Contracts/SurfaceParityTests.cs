// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper;

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>
///     The executed cross-product: every surface element, at every legal declaration site, in every case its
///     declaration admits, at every endpoint.
///     <para>
///         Verified in BOTH directions against the element's own claim, resolved per (element, SITE) by
///         <see cref="SurfaceCatalog.ClaimFor" />. A claimed endpoint where the element does nothing
///         observable fails (the claim over-reaches); an unclaimed endpoint where it changes the output fails
///         too (the claim under-reaches and the matrix would otherwise skip a live cell). There is therefore
///         no claim value that passes vacuously.
///     </para>
///     <para>
///         Nothing in this file decides a claim. Every cell's expectation is read off the type it describes —
///         the element's <c>[DwarfSurface(AppliesTo)]</c>, refined for one site by <c>[DwarfSurfaceSite]</c>.
///         A predicate used to live here deciding ~100 member-site cells, which is exactly the hand-kept,
///         test-side knowledge this architecture exists to delete: nothing forced a newly added element to
///         acquire an entry, and the cells it governed were reviewed by no one.
///     </para>
///     <para>
///         Traited so it can run as its own CI leg: this is roughly seven times the work of the option matrix.
///     </para>
/// </summary>
[Trait("Category", "SurfaceMatrix")]
public sealed class SurfaceParityTests
{
    /// <summary>
    ///     One row per cell, keyed by (usage name, generic ARITY, axis, site, endpoint).
    ///     <para>
    ///         Arity is part of the key because five elements share a usage name with a generic twin —
    ///         <c>MapProperty</c>/<c>MapProperty&lt;S,T&gt;</c>, <c>MapIgnore</c>/<c>MapIgnore&lt;T&gt;</c>,
    ///         <c>MapValue</c>/<c>MapValue&lt;T&gt;</c>, <c>MapNullSkip</c>/<c>MapNullSkip&lt;S,T&gt;</c> and
    ///         the two <c>MapDerivedType</c> forms — and <see cref="SurfaceElement.UsageName" /> strips the
    ///         arity by design. Keyed on the name alone, the two <c>MapDerivedType</c> forms (same sites, same
    ///         axes) produce identical rows, and resolving them back would hand both to whichever came first:
    ///         the generic form would never be measured while appearing in the matrix as though it had been.
    ///     </para>
    /// </summary>
    public static TheoryData<string, int, string, string, Endpoint> Cells()
    {
        var data = new TheoryData<string, int, string, string, Endpoint>();
        foreach (var element in SurfaceCatalog.CrossProductElements)
        foreach (var c in SurfaceCatalog.CasesFor(element))
        foreach (var endpoint in EndpointSources.All)
            data.Add(element.UsageName, Arity(element), c.Axis, c.Site.ToString(), endpoint);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void Every_cell_matches_the_elements_own_AppliesTo_claim(
        string usageName, int arity, string axis, string site, Endpoint endpoint)
    {
        var (element, c) = Resolve(usageName, arity, axis, site);
        var claimed = (SurfaceCatalog.ClaimFor(element, c.Site) & ToFlag(endpoint)) != 0;
        var (effect, detail) = SurfaceProbe.Classify(c, endpoint);

        // No cell to judge: the endpoint has no such site, or AttributeUsage forbids it and the compiler
        // agrees. Both are the declaration telling the truth.
        if (effect is SurfaceEffect.NoSuchSite or SurfaceEffect.NotCompilable) return;

        // No question asked, so no answer to judge. Excused HERE rather than failed, because a cell the
        // instrument could not pose is not a divergence and recording it as one would ratify a bug the
        // generator never committed. It is not excused quietly: every such cell is counted by
        // The_cells_that_pose_no_question_are_declared_and_counted, against a ceiling that can only shrink.
        if (SurfaceProbe.PosesNoQuestion(c, effect)) return;

        if (claimed)
        {
            if (effect is SurfaceEffect.Honoured or SurfaceEffect.Refused
                or SurfaceEffect.UnhonouredButLoud) return;

            // Task 7 renames this to DeclaredDivergences.Reasons. Use the current name here so this task
            // compiles on its own; update the reference as part of that rename, not before it.
            if (OptionGaps.KnownSilent.TryGetValue($"{usageName}@{endpoint}", out var why))
            {
                Assert.False(string.IsNullOrWhiteSpace(why));
                return;
            }

            Assert.Fail(
                $"{c.Rendered} on a {site} CLAIMS {endpoint} ({ClaimSource(element, c.Site)}) but is SILENT "
                + $"there: no diagnostic, and output byte-identical to the same source without it. ({detail})"
                + "\n\nThe caller wrote something, the generator accepted it, changed nothing, and said "
                + "nothing. Three ways out, in order of preference:\n"
                + $"  1. honour it at {endpoint};\n"
                + $"  2. refuse it there with a diagnostic;\n"
                + $"  3. drop {endpoint} from the claim — but only if it STRUCTURALLY cannot apply, not "
                + "because it currently does not. If the element reaches this endpoint from ANOTHER site, "
                + $"narrow this one alone with [DwarfSurfaceSite(AttributeTargets.{site}, …, \"why\")] rather "
                + "than dropping the endpoint for every site at once.");
        }
        else
        {
            if (effect is SurfaceEffect.Silent or SurfaceEffect.UnhonouredButLoud) return;

            Assert.Fail(
                $"{c.Rendered} on a {site} does NOT claim {endpoint} ({ClaimSource(element, c.Site)}) but is "
                + $"{effect} there ({detail}). The claim under-reaches: this cell is live and the matrix was "
                + $"told to skip it. Add {endpoint} to the claim that governs the {site} site.");
        }
    }

    /// <summary>
    ///     Which declaration decided this cell's claim — the element's default, or a site override. Named in
    ///     both failure messages because the fix is a different edit in each case, and "AppliesTo" pointed at
    ///     the wrong one as soon as site overrides existed.
    /// </summary>
    private static string ClaimSource(SurfaceElement element, AttributeTargets site) =>
        element.SiteClaims.Any(sc => (sc.Site & site) == site)
            ? $"[DwarfSurfaceSite({site})]"
            : "[DwarfSurface(AppliesTo)]";

    /// <summary>
    ///     Counts an element as acting at <see cref="Endpoint.CreateMap" /> if ANY of its cases does, not just
    ///     the first. The brief specified the first case; measured, that reads 7 elements rather than the 12
    ///     that genuinely act, because <c>CasesFor</c> orders the zero-argument constructor first and a bare
    ///     <c>[DwarfMapper]</c> or <c>[MapIgnore]</c> configures nothing — the weakest case in the element's
    ///     whole domain stood in for the element. Widening it makes the guard stronger, not looser: it now
    ///     fails if an element loses its LAST acting case rather than only its first-listed one.
    /// </summary>
    [Fact]
    public void The_matrix_is_not_vacuous()
    {
        // If Classify() broke so every cell read NoSuchSite, the theory would return at the first guard and
        // pass without comparing anything.
        var acting = SurfaceCatalog.CrossProductElements
            .Count(e => SurfaceCatalog.CasesFor(e)
                .Any(c => SurfaceProbe.Classify(c, Endpoint.CreateMap).Effect
                    is SurfaceEffect.Honoured or SurfaceEffect.Refused));

        // Measured at 12 (see Issues/round20/SURFACE-MATRIX-FINDINGS.md); the gate sits below that so an
        // ordinary change does not trip it, and a collapse of the instrument does.
        Assert.True(acting >= 10,
            $"Only {acting} elements visibly act at CreateMap. Either the fixtures stopped triggering their "
            + "elements or Classify() is not observing correctly — in both cases the matrix is passing "
            + "without testing anything.");
    }

    /// <summary>
    ///     The ceiling on cells the instrument cannot pose a question about. Measured, stated, and SHRINK-ONLY:
    ///     raising it is how a coverage hole grows back one cell at a time with nobody the wiser.
    /// </summary>
    private const int UnaskableCellCeiling = 44;

    /// <summary>
    ///     Every cell the matrix excuses for posing no question is named and counted here.
    ///     <para>
    ///         These are the two shapes the instrument still cannot ask: a case declared <c>Unmeasured</c> at
    ///         the element (a bare option bag, which selects every default and therefore configures nothing),
    ///         and a case whose fixture the endpoint's template refused to carry (<c>Registry</c> and
    ///         <c>CoLocatedHost</c> declare their own DTO pair). Neither is a divergence: the generator was
    ///         never asked. But an unasked question that nothing counts is exactly the silent absence of
    ///         coverage this whole arrangement exists to delete, so the total is pinned and the list printed.
    ///     </para>
    /// </summary>
    [Fact]
    public void The_cells_that_pose_no_question_are_declared_and_counted()
    {
        var unaskable = AllCells()
            .Where(x => SurfaceProbe.PosesNoQuestion(x.Case, x.Effect))
            .Select(x => $"  {x.Rendered} on a {x.Case.Site} @ {x.Endpoint} — {x.Case.Unmeasured ?? x.Detail}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        AssertRatchet(unaskable, UnaskableCellCeiling,
            "cells pose the generator no question",
            "Close a hole instead: give the case a fixture that triggers it with "
            + "[DwarfSurfaceProbe(ProbeKey = ...)], or an argument list that bites.");
    }

    /// <summary>The ceiling on cells the C# compiler rejects outright. Shrink-only, like the others.</summary>
    private const int NotCompilableCellCeiling = 107;

    /// <summary>The ceiling on cells that pass BOTH claim branches. Shrink-only, like the others.</summary>
    private const int UnhonouredButLoudCellCeiling = 14;

    /// <summary>
    ///     The cells the C# compiler rejects, counted rather than merely returned from.
    ///     <para>
    ///         <see cref="SurfaceEffect.NotCompilable" /> passes without deciding anything, and most of the
    ///         time that is honest — <c>AttributeUsage</c> forbids the site and the compiler agrees, which is
    ///         the declaration telling the truth. But two other things wear the same label. A blocking
    ///         generator error leaves the partial mapping method unimplemented (<c>CS8795</c>), which is the
    ///         known ordering defect G4/R4; and generated code that does not compile — two identical
    ///         <c>[FlattenGraph]</c> directives emit a duplicate member initialization, <c>CS1912</c> — is a
    ///         real defect hiding behind a verdict that reads like a placement rule.
    ///     </para>
    ///     <para>
    ///         An uncounted pass is a silent absence of coverage whatever its cause, which is the thing this
    ///         architecture exists to delete. The ids are printed with the count so the three populations stay
    ///         distinguishable while R4 is outstanding.
    ///     </para>
    /// </summary>
    [Fact]
    public void The_cells_the_compiler_rejects_are_counted()
    {
        var rejected = AllCells()
            .Where(x => x.Effect is SurfaceEffect.NotCompilable)
            .Select(x => $"  {x.Rendered} on a {x.Case.Site} @ {x.Endpoint} — {x.Detail}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        AssertRatchet(rejected, NotCompilableCellCeiling,
            "cells are rejected by the C# compiler and therefore judged by nothing",
            "Close one by making the placement legal, or — where the id is CS8795 or a CS error in GENERATED "
            + "code — by fixing the ordering defect R4 names, so a refusal reads as Refused rather than as a "
            + "placement rule.");
    }

    /// <summary>
    ///     The cells that pass BOTH claim branches, counted.
    ///     <para>
    ///         <see cref="SurfaceEffect.UnhonouredButLoud" /> means "changed nothing, but the build fails
    ///         anyway", and it is accepted on the claimed branch AND on the unclaimed one — so it decides
    ///         nothing in either direction. That is not hypothetical: a fixture whose BASELINE carries a
    ///         blocking error emits nothing at all, so an element that does nothing produces byte-identical
    ///         (empty) output beside that error and lands here. The <c>graph-navigation-to-flat-collection</c>
    ///         fixture did exactly that for eight cells until its <c>Src</c> gained the member that lets the
    ///         baseline compile — a fixture that cannot compile without the element under test can never show
    ///         that element doing nothing.
    ///     </para>
    ///     <para>
    ///         Some of these are honest and permanent (a case that legitimately changes nothing at an endpoint
    ///         whose baseline is broken for an unrelated, deliberate reason). Counting them is not a claim
    ///         that they are wrong; it is a claim that their number must not grow unnoticed.
    ///     </para>
    /// </summary>
    [Fact]
    public void The_cells_that_pass_both_claim_branches_are_counted()
    {
        var undecided = AllCells()
            .Where(x => x.Effect is SurfaceEffect.UnhonouredButLoud)
            .Select(x => $"  {x.Rendered} on a {x.Case.Site} @ {x.Endpoint} (probe: {x.Case.ProbeKey ?? "flat"})")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        AssertRatchet(undecided, UnhonouredButLoudCellCeiling,
            "cells pass both the claimed and the unclaimed branch, so they decide nothing",
            "Close one by making the fixture's BASELINE compile, so the element under test is the only "
            + "difference between the two compilations and its verdict is legible again.");
    }

    /// <summary>The ceiling on cells with no declaration site. Shrink-only, like the others.</summary>
    private const int NoSuchSiteCellCeiling = 137;

    /// <summary>
    ///     The cells with no declaration site, counted AND broken down by cause.
    ///     <para>
    ///         This is the largest population that passes without deciding anything, and it was the last one
    ///         outside every ratchet — about a sixth of the matrix, described in an earlier report as already
    ///         pinned when only its member-slot half was, and only at FIXTURE granularity. "137 cells have no
    ///         declaration site" is not a reviewable statement; the breakdown is the point, because the four
    ///         causes are not the same kind of thing:
    ///     </para>
    ///     <para>
    ///         <c>registry-has-no-mapper-class</c> and <c>no-mapping-method</c> are STRUCTURAL — the registry
    ///         front door genuinely has no mapper class and neither it nor the co-located host declares a
    ///         mapping method, so there is nothing an improved template could annotate.
    ///         <c>no-fixture-declares-one</c> (struct and constructor sites) and <c>no-member-slot</c> are
    ///         TEMPLATE LIMITATIONS wearing the same verdict: those sites could be measured, and are not.
    ///         Both are reported as findings rather than treated as shapes; the count keeps them from
    ///         drifting upward meanwhile.
    ///     </para>
    /// </summary>
    [Fact]
    public void The_cells_with_no_declaration_site_are_counted_by_cause()
    {
        var siteless = AllCells().Where(x => x.Effect is SurfaceEffect.NoSuchSite).ToList();
        var byCause = string.Join("\n", siteless
            .GroupBy(x => x.Detail, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .Select(g => $"  {g.Count(),4}  {g.Key}"));

        Assert.True(siteless.Count <= NoSuchSiteCellCeiling,
            $"{siteless.Count} cells have no declaration site and are therefore judged by nothing, above the "
            + $"stated ceiling of {NoSuchSiteCellCeiling}:\n{byCause}\n\nThis number may only shrink. Two of "
            + "the causes are TEMPLATE limitations rather than structural absences — a struct fixture and a "
            + "constructor-bearing fixture could exist, and a fixture could carry a MemberSlotMarker. Close "
            + "one of those; the two structural causes (registry-has-no-mapper-class, no-mapping-method) "
            + "cannot move, because there is nothing at those endpoints for a template to annotate.");

        Assert.True(siteless.Count >= NoSuchSiteCellCeiling - 10,
            $"Only {siteless.Count} cells have no declaration site, well under the ceiling of "
            + $"{NoSuchSiteCellCeiling}:\n{byCause}\n\nLower the ceiling to lock the improvement in.");
    }

    /// <summary>Every cell, classified once, for the ratchets that count a whole population.</summary>
    private static IEnumerable<(SurfaceCase Case, Endpoint Endpoint, SurfaceEffect Effect, string Detail,
        string Rendered)> AllCells()
    {
        foreach (var element in SurfaceCatalog.CrossProductElements)
        foreach (var c in SurfaceCatalog.CasesFor(element))
        foreach (var endpoint in EndpointSources.All)
        {
            var (effect, detail) = SurfaceProbe.Classify(c, endpoint);
            yield return (c, endpoint, effect, detail,
                c.Rendered.Replace("\n", " + ", StringComparison.Ordinal));
        }
    }

    /// <summary>
    ///     Asserts a counted population against a stated ceiling in BOTH directions: it may not grow, and it
    ///     may not sit well under the ceiling either, because an unratcheted ceiling lets the hole reopen
    ///     silently after someone else's improvement paid for the slack.
    /// </summary>
    private static void AssertRatchet(List<string> cells, int ceiling, string what, string howToClose)
    {
        Assert.True(cells.Count <= ceiling,
            $"{cells.Count} {what}, above the stated ceiling of {ceiling}:\n"
            + string.Join("\n", cells) + "\n\nThis number may only shrink. " + howToClose);

        Assert.True(cells.Count >= ceiling - 10,
            $"Only {cells.Count} {what}, well under the ceiling of {ceiling}. Lower the ceiling to lock the "
            + "improvement in.");
    }

    /// <summary>
    ///     Every <c>Unmeasured</c> declaration must actually excuse a cell. One that excuses none reads as a
    ///     reviewed decision about a hole that no longer exists — the same failure mode as a site override
    ///     restating the element's own default, and the same reason that one is an error too.
    /// </summary>
    [Fact]
    public void Every_Unmeasured_declaration_excuses_at_least_one_cell()
    {
        var idle = SurfaceCatalog.CrossProductElements
            .SelectMany(SurfaceCatalog.CasesFor)
            .Where(c => c.Unmeasured is not null)
            .Where(c => !EndpointSources.All.Any(ep =>
                SurfaceProbe.PosesNoQuestion(c, SurfaceProbe.Classify(c, ep).Effect)))
            .Select(c => $"{c.Element.UsageName}: {c.Rendered} on a {c.Site}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(idle.Count == 0,
            "Case(s) declared Unmeasured that excuse no cell anywhere:\n  " + string.Join("\n  ", idle)
            + "\n\nThe case is measured after all — delete the declaration and let the matrix judge it.");
    }

    /// <summary>
    ///     Every theory row must name exactly one case. A row that names two is worse than a missing row: the
    ///     duplicate resolves to whichever element the catalogue happened to order first, so the other one is
    ///     never measured while the matrix reports a cell for it. <see cref="Resolve" /> uses
    ///     <c>Single</c> rather than <c>First</c> for the same reason; this states the invariant up front
    ///     instead of leaving it to whichever cell happens to trip over it after several minutes of compiling.
    /// </summary>
    [Fact]
    public void The_theory_key_names_exactly_one_case()
    {
        var duplicates = SurfaceCatalog.CrossProductElements
            .SelectMany(e => SurfaceCatalog.CasesFor(e)
                .Select(c => $"{e.UsageName}`{Arity(e)}/{c.Axis}/{c.Site}"))
            .GroupBy(k => k, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} ×{g.Count()}")
            .ToList();

        Assert.True(duplicates.Count == 0,
            "The (name, arity, axis, site) key is not unique: " + string.Join(", ", duplicates)
            + ". Add a discriminator to Cells() rather than letting one row stand in for two cases.");
    }

    private static (SurfaceElement, SurfaceCase) Resolve(string usageName, int arity, string axis, string site)
    {
        var element = SurfaceCatalog.CrossProductElements
            .Single(e => string.Equals(e.UsageName, usageName, StringComparison.Ordinal)
                         && Arity(e) == arity);
        var c = SurfaceCatalog.CasesFor(element)
            .Single(x => string.Equals(x.Axis, axis, StringComparison.Ordinal)
                         && string.Equals(x.Site.ToString(), site, StringComparison.Ordinal));
        return (element, c);
    }

    private static int Arity(SurfaceElement element) => element.Type.GetGenericArguments().Length;

    private static SurfaceEndpoints ToFlag(Endpoint e) =>
        Enum.Parse<SurfaceEndpoints>(e.ToString());
}
