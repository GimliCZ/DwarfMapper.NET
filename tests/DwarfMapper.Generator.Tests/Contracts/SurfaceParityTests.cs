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
///     <para>
///         <b>THE LIMITATION OF EVERY CEILING IN THIS FILE, stated where they are read (B19).
///         <c>Honoured</c> proves an element had an EFFECT, not that the effect is RIGHT. A green matrix is
///         not a correct generator.</b> <c>SurfaceProbe.Classify</c> returns <c>Honoured</c> when the emitted
///         text merely DIFFERS between the with-and-without compilations. Nothing inspects what it differs
///         INTO — so a cell can be green while the code it graded is broken, and none of the counts below
///         will move.
///     </para>
///     <para>
///         Not hypothetical. Round 20's A7: <c>[AfterMap]</c> on
///         <c>public partial void Update(Src s, Dst d)</c> made the generator emit <c>Update(s, d);</c> as
///         the last statement OF <c>Update</c> — unconditional infinite recursion, shipped, compiling. This
///         matrix scored that cell <c>Honoured</c>, and the claim-parity theory passed it at the claimed
///         reading AND at the honest unclaimed one. It was found only because the NEIGHBOURING cell at
///         <c>SpanMap</c> read <c>Silent</c> and someone dumped the generated body while chasing that.
///         Every other finding in this repository was the matrix failing to MEASURE something, and was
///         therefore visible as a red or unaccounted cell; this one graded broken behaviour as working and
///         left no trace in any count.
///     </para>
///     <para>
///         <c>Refused</c> carries the same blind spot in weaker form: it proves a diagnostic was reported,
///         not that it was the RIGHT diagnostic. D2 is the worked example — a <c>DWARF038</c> about an
///         <c>int → string</c> conversion was filed for four rounds as a refusal of <c>[MapProperty]</c>'s
///         PLACEMENT, which it never was.
///     </para>
///     <para>
///         <b>No remedy is scoped here, deliberately.</b> "How does a cross-product of this size assert
///         correctness rather than difference" is a design question, and the cheap answers — a golden output
///         per cell, a runtime execution leg, a self-call check over generated bodies — differ enormously in
///         cost and in what they actually catch. The systemic pressure on this class of failure is the
///         round-22 compiler-testing arc: <b>R22-01</b>, a differential oracle that RUNS generated maps over
///         generated type graphs and compares against a reference interpretation, and <b>R22-02</b>,
///         metamorphic relations that assert properties of the result rather than of the diff. Until one of
///         them lands, read these ceilings as "no element silently stopped acting" — never as "the generator
///         is right".
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
        var claimed = (SurfaceCatalog.ClaimFor(element, c.Site) & ToFlag(endpoint)) != SurfaceEndpoints.None;
        var (effect, detail) = SurfaceProbe.Classify(c, endpoint);

        // No cell to judge: the endpoint has no such site, or AttributeUsage forbids it and the compiler
        // agrees. Both are the declaration telling the truth.
        //
        // EmittedInvalidCode is the third, and it is here for the opposite reason: the declaration is fine
        // and the GENERATOR broke the build, so the element's own behaviour is unobservable. Excused on both
        // branches like the other two, and counted separately by
        // The_cells_whose_generated_code_does_not_compile_are_counted — a cell judged by nothing must be
        // judged by nothing under a label that says which of the three it is.
        if (effect is SurfaceEffect.NoSuchSite or SurfaceEffect.NotCompilable
            or SurfaceEffect.EmittedInvalidCode) return;

        // No question asked, so no answer to judge. Excused HERE rather than failed, because a cell the
        // instrument could not pose is not a divergence and recording it as one would ratify a bug the
        // generator never committed. It is not excused quietly: every such cell is counted by
        // The_cells_that_pose_no_question_are_declared_and_counted, against a ceiling that can only shrink.
        if (SurfaceProbe.PosesNoQuestion(c, effect)) return;

        if (claimed)
        {
            if (effect is SurfaceEffect.Honoured or SurfaceEffect.Refused
                or SurfaceEffect.UnhonouredButLoud) return;

            // There is nothing at this endpoint for this ONE OPTION of an option bag to configure. Not a
            // narrowing of AppliesTo, because AppliesTo is per ELEMENT and [DwarfMapper] carries nineteen
            // independent options — no value of the flags can say "GenerateExtensions has no surface at
            // UpdateInto" without saying it about EnumStrategy too. The claim already exists, reviewed and
            // consumed by the option matrix; reading it here rather than restating it is the same reason
            // DeclaredDivergences is one file. Every such cell is counted by
            // The_cells_excused_as_structural_are_counted, because unlike a divergence a structural entry
            // does NOT fail when the shape changes.
            if (StructurallyInapplicableOption(c.Axis, endpoint) is not null) return;

            if (DeclaredDivergences.For(element.UsageName, arity, c.Axis, c.Site, ToFlag(endpoint))
                is { } declared)
            {
                Assert.False(string.IsNullOrWhiteSpace(declared.Value.Why),
                    $"{declared.Key} states no reason.");
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
                + "than dropping the endpoint for every site at once.\n\n"
                + "The fourth way out is a MAINTAINER DECISION, not a way past a red build: record it in "
                + "DeclaredDivergences.Reasons, with the cell named exactly and a reason stating what a "
                + "caller who wrote this reasonably expects. Adding a row there raises the declared-cell "
                + "count, which is itself ratcheted, so a new divergence is a deliberate act with a number "
                + "attached to it.");
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

    /// <summary>
    ///     The ceiling on cells the C# compiler rejects outright. Shrink-only, like the others.
    ///     <para>
    ///         99 → 98 when <c>D8</c> closed and 98 → 96 when <c>D14</c> did, both for the same reason
    ///         and neither because of a generator change: a fixture that could not pose its question had been
    ///         counting cells here. <c>keyed-collection-elements</c> declared a DIFFERENT element type on each
    ///         side, which the v1 key-based upsert refuses as <c>DWARF074</c> — an Error, hence <c>CS8795</c>
    ///         — so the one endpoint <c>[MapCollectionKey]</c> exists for was in this population rather than
    ///         reading <c>Honoured</c>.
    ///     </para>
    ///     <para>
    ///         96 → <b>10</b> when R4 was fixed — the largest single movement any of these ratchets has seen,
    ///         and not a generator change either: <see cref="SurfaceProbe.Classify" /> stopped calling a
    ///         refusal a placement rejection. See the remarks on
    ///         <see cref="The_cells_the_compiler_rejects_are_counted" /> for what the 86 cells that left
    ///         were, and what the 10 that remain are.
    ///     </para>
    ///     <para>
    ///         <b>The constant was held at 10 while the population measured 24, and that hold is what closed
    ///         a product defect.</b> A11 gave the <c>Struct</c> site a slot and measured <c>[MapTo]</c> there
    ///         for the first time: all fourteen of its cells were <c>CS0037</c>, because
    ///         <c>MapToGenerator</c> emitted <c>if (source is null) throw …</c> into every generated
    ///         extension method without asking whether the source could BE null, so <c>[MapTo]</c> on a
    ///         <c>struct</c> — legal per its own <c>AttributeUsage</c>, claiming all seven endpoints —
    ///         produced source that did not compile. Finding <b>A11-F1</b>, fixed in <b>A13</b>:
    ///         <c>TypeFacts.CanBeNull</c> now gates the guard, and the same predicate answers for the
    ///         synthesized-helper path that already had the discrimination the extension methods lacked.
    ///     </para>
    ///     <para>
    ///         Raising the constant to 24 would have closed a live product defect by moving it into the one
    ///         population this matrix explicitly does not judge — the exact move the shrink-only rule exists
    ///         to forbid, and the first time it fired against a defect rather than against slack. So
    ///         <see cref="The_cells_the_compiler_rejects_are_counted" /> failed on purpose instead, and the
    ///         generator changed. Measured after the fix: 24 → <b>10</b>, all fourteen cells reading
    ///         <see cref="SurfaceEffect.Honoured" /> at every one of the seven endpoints, and this constant
    ///         needed no edit — which is what a ratchet pointed at a defect is supposed to look like.
    ///     </para>
    ///     <para>
    ///         The label was wrong for those fourteen while they were here, and knowingly so.
    ///         <c>NotCompilable</c> means "the declaration told the truth and there is nothing here to
    ///         judge"; there the compiler was rejecting the GENERATOR'S OUTPUT, not the case's placement. R4
    ///         separated "the compiler rejected the placement" from "the generator refused loudly"; that was
    ///         a third thing — "the generator emitted broken code" — and it wanted a verdict of its own with
    ///         its own counted population, because the next such defect would land here mislabelled too.
    ///     </para>
    ///     <para>
    ///         <b>It has one now</b> — <see cref="SurfaceEffect.EmittedInvalidCode" />, keyed on WHERE the
    ///         compiler error was reported rather than on which id it carries. Landing it EMPTIED this
    ///         population: <b>10 → 0</b>. All ten residuals turned out to be errors reported against
    ///         <c>.g.cs</c> files, so the two shapes described above are not "the declaration telling the
    ///         truth" at all. See
    ///         <see cref="The_cells_whose_generated_code_does_not_compile_are_counted" /> for what they are
    ///         and what each needs.
    ///     </para>
    ///     <para>
    ///         Zero is not slack. The population this constant names — the compiler rejecting the CALLER's
    ///         own source — is empty by construction today: the case space is derived from
    ///         <c>AttributeUsage.ValidOn</c>, and <c>EndpointSources.BuildAt</c> returns null where a site
    ///         does not exist, so an illegal placement is never built in the first place. A cell arriving
    ///         here is therefore a genuinely new shape and should have to be looked at.
    ///     </para>
    /// </summary>
    private const int NotCompilableCellCeiling = 0;

    /// <summary>
    ///     The ceiling on cells where the GENERATOR emitted C# that does not compile. Shrink-only, and the
    ///     one population in this file that should end at zero and stay there.
    ///     <para>
    ///         Measured at <b>10</b> when the verdict was created, which was not the expected reading. Both
    ///         historical instances (<c>N4</c>'s <c>CS1912</c>, <c>A11-F1</c>'s <c>CS0037</c>) were already
    ///         fixed, so the population was expected to be EMPTY and the verdict's whole value was to be that
    ///         the next such defect fails on arrival. Instead the ten cells that had been sitting in
    ///         <see cref="NotCompilableCellCeiling" />'s population under the label "the declaration is
    ///         telling the truth" moved here, because every one of their compiler errors is reported against
    ///         a <c>.g.cs</c> file.
    ///     </para>
    ///     <para>
    ///         <b>Eight were the shape filed as B27</b>: <c>[GenerateMap&lt;Src, Dst&gt;]</c> on a class that
    ///         also declares a <c>partial Dst Map(Src)</c> over the same pair — and the ×2 case, which is
    ///         that collision twice. The generator emitted its own <c>Map</c> beside the one it was
    ///         implementing and the consumer got <c>CS0111</c> plus a <c>CS0121</c> cascade, with no
    ///         DwarfMapper diagnostic about a collision the generator created. <b>Closed as B27</b> (8 → 0):
    ///         the gap B27 named between <c>DWARF060</c> and <c>DWARF057</c> is now <c>DWARF094</c>, an
    ///         Error raised in the same signature pass <c>DWARF060</c> runs in, where identical-with-identical
    ///         used to fall through a <c>continue</c> labelled "a duplicate-pair concern" that nothing
    ///         downstream owned. All eight cells read <see cref="SurfaceEffect.Refused" /> now.
    ///     </para>
    ///     <para>
    ///         <b>Two more were <c>[DwarfMapper(ReferenceHandling = Preserve)]</c> at <c>SpanMap</c> and
    ///         <c>AsyncStream</c></b> — <c>CS7036</c> in the emitted mapper, because <c>Preserve</c> adds a
    ///         reference-tracker parameter that the element-wise emission did not pass. The note that verdict
    ///         replaced called them "the hand-written partial declaration fits no generated overload", which
    ///         would have been <c>CS8795</c> in the caller's file; the error was in the GENERATED file, so it
    ///         was the emission that was wrong, not the template. <b>Closed as B33</b> (10 → 8): the span and
    ///         async-stream emitters now thread ONE shared <c>DwarfRefContext</c> per call into a ctx-tailed
    ///         element converter — the identity-map scope the top-level collection path already gave a
    ///         <c>List&lt;T&gt;</c> map — and the same missing tail turned out to be reachable with no
    ///         <c>Preserve</c> in sight (a recursive element pair under default None, and under
    ///         <c>OnCycle = SetNull</c>). All three modes are pinned by EXECUTING tests in
    ///         <c>ElementWiseReferenceHandlingRuntimeTests</c>, not by the matrix's difference reading —
    ///         B19's point, honoured: two span slots or stream elements holding the same source object land
    ///         the SAME target instance under <c>Preserve</c>.
    ///     </para>
    ///     <para>
    ///         Neither shape was A12's to fix — one is a new diagnostic id with the five-file sync, the other
    ///         an emission defect at two endpoints — so the ceiling recorded what was measurably there rather
    ///         than pretending otherwise. What it bought immediately is that an eleventh cannot appear quietly.
    ///     </para>
    /// </summary>
    private const int EmittedInvalidCodeCellCeiling = 0;

    /// <summary>
    ///     The cells where the generator emitted code the C# compiler rejects, counted, with the ids that
    ///     were reported against its own output.
    ///     <para>
    ///         This is the population that must not exist. A refusal tells the caller what to change; invalid
    ///         C# in a file they never wrote tells them nothing and cannot be worked around. It is judged by
    ///         nothing for the same reason <see cref="SurfaceEffect.NotCompilable" /> is — the element's own
    ///         behaviour is unobservable once the build is broken — which is exactly why it has to be counted
    ///         rather than merely returned from.
    ///     </para>
    ///     <para>
    ///         The verdict is keyed on WHERE the error was reported, not on which id it carries, and that is
    ///         load-bearing: an id allowlist would have to be extended by whoever hit the new id, who is by
    ///         definition the person who has not noticed yet. <c>N4</c> was <c>CS1912</c> and <c>A11-F1</c>
    ///         was <c>CS0037</c>; neither was foreseeable from the other.
    ///     </para>
    /// </summary>
    [Fact]
    public void The_cells_whose_generated_code_does_not_compile_are_counted()
    {
        var broken = AllCells()
            .Where(x => x.Effect is SurfaceEffect.EmittedInvalidCode)
            .Select(x => $"  {x.Rendered} on a {x.Case.Site} @ {x.Endpoint} — {x.Detail}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        AssertExactPin(broken, EmittedInvalidCodeCellCeiling,
            "cells carry a compiler error against code the GENERATOR emitted",
            "There is one way to close one of these and it is not a bookkeeping move: make the generator "
            + "emit valid C#, or refuse the shape with a diagnostic before it emits anything. A cell here is "
            + "strictly worse than a refusal — the consumer gets a broken build in a file they never wrote, "
            + "and no statement of what to change.");
    }

    /// <summary>The ceiling on cells that pass BOTH claim branches. Shrink-only, like the others.</summary>
    private const int UnhonouredButLoudCellCeiling = 14;

    /// <summary>
    ///     The cells the C# compiler rejects, counted rather than merely returned from.
    ///     <para>
    ///         <see cref="SurfaceEffect.NotCompilable" /> passes without deciding anything, so every cell
    ///         here is a cell the matrix does not judge. What remains is the honest version of that:
    ///         <c>AttributeUsage</c> forbids the site, or the case duplicates a declaration, or its sampled
    ///         arguments fit no constructor — the compiler rejects the caller's own source and there is
    ///         genuinely nothing for the generator to be judged on.
    ///     </para>
    ///     <para>
    ///         <b>R4 is fixed, and this population is what it cost.</b> Something else used to wear this
    ///         label: a blocking generator error suppresses emission, which leaves the endpoint's partial
    ///         mapping method unimplemented — <c>CS8795</c> — and <see cref="SurfaceProbe.Classify" />
    ///         returned on the first new CS error before it ever read the generator's own diagnostics. So a
    ///         cell where the generator refused loudly and correctly was recorded as "the compiler rejected
    ///         the placement" and then SKIPPED by the bidirectional claim check. <b>86 cells</b> carried
    ///         that verdict; the ceiling fell 96 → <b>10</b> when they stopped. They read
    ///         <see cref="SurfaceEffect.Refused" /> now and are judged against their claim like every
    ///         other cell.
    ///     </para>
    ///     <para>
    ///         Two earlier notes in this history are settled by that, and both are worth keeping because
    ///         they were written as predictions and can now be checked. Two <c>[FlattenGraph]</c> directives
    ///         filling one destination collection made the generator emit a duplicate member initialization
    ///         — <c>CS1912</c> in <c>Demo.M.g.cs</c>, invalid C# handed to a consumer in a file they never
    ///         wrote, which is categorically worse than any refusal. Refused as <c>DWARF087</c> it joined
    ///         the <c>CS8795</c> crowd instead of leaving this population, and the note said "it becomes
    ///         <c>Refused</c> when R4 does". It did. Likewise <c>DWARF091</c> retired eight
    ///         <c>[BeforeMap]</c>/<c>[AfterMap]</c> cells by demoting <c>DWARF018</c> to a Warning, and that
    ///         note said this was "what R4 will eventually do for the rest of this population". It was: the
    ///         other 86 got there without any severity being changed, because the defect was never in the
    ///         severities — it was in the ORDER the probe asked its two questions.
    ///     </para>
    ///     <para>
    ///         The <b>10</b> the ceiling is set at are two shapes, and the ids are printed with the count so
    ///         they stay distinguishable. <c>[DwarfMapper(ReferenceHandling = Preserve)]</c> at
    ///         <c>SpanMap</c> and <c>AsyncStream</c> is <c>CS7036</c>: <c>Preserve</c> adds a
    ///         reference-tracker parameter to the generated signature, so the hand-written partial
    ///         declaration in the endpoint template fits no generated overload. The other eight are
    ///         duplicate <c>[GenerateMap&lt;Src, Dst&gt;]</c> declarations asking for the same method twice
    ///         — <c>CS0111</c>, plus the <c>CS0121</c> ambiguity that follows it. Both are the declaration
    ///         telling the truth.
    ///     </para>
    ///     <para>
    ///         A THIRD shape was in the printed list for one round and was not one of those: fourteen
    ///         <c>CS0037</c> cells, <c>[MapTo]</c> at the <c>Struct</c> site at all seven endpoints, measured
    ///         for the first time by A11 and rejected because the GENERATOR'S output did not compile — not
    ///         because the declaration was wrong. Finding <b>A11-F1</b>. The population measured <b>24</b>
    ///         against a constant deliberately left at 10, so this assertion stayed red rather than absorbing
    ///         a live defect; A13 fixed the generator and all fourteen left, reading <c>Honoured</c> at every
    ///         endpoint. The reasoning is on <see cref="NotCompilableCellCeiling" />; read it before changing
    ///         this number.
    ///     </para>
    ///     <para>
    ///         (The entries above record the same count arriving from the other direction, and none was a
    ///         generator change either: 107 → 99 via <c>DWARF091</c>, then 99 → 98 with <c>D8</c> —
    ///         <c>[MapDerivedType]</c> against a flat pair that declares no hierarchy, so the sampled
    ///         arguments named a type not assignable to the method's source parameter, <c>DWARF035</c>
    ///         behind <c>CS8795</c> — and 98 → 96 with <c>D14</c>. A fixture that could not pose its
    ///         question had been counting cells here for four rounds.)
    ///     </para>
    ///     <para>
    ///         An uncounted pass is a silent absence of coverage whatever its cause, which is the thing this
    ///         architecture exists to delete. That is why this count exists and why it may only shrink.
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

        AssertExactPin(rejected, NotCompilableCellCeiling,
            "cells are rejected by the C# compiler and therefore judged by nothing",
            "Close one by making the placement legal — give the case an argument list that fits, or a "
            + "fixture whose shape the endpoint template can actually declare. A CS8795 here is NOT one of "
            + "those: since R4 was fixed, a CS8795 accompanied by a new blocking DWARF diagnostic already "
            + "reads Refused, so a CS8795 reaching this list means the generator declined to emit while "
            + "saying NOTHING — a generator defect to report, not a placement rule to accept.");
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

    /// <summary>
    ///     The cells with no declaration site, pinned EXACTLY and PER CAUSE. Shrink-only, per cause.
    ///     <para>
    ///         This replaces a single total ceiling of 116 with a ten-wide shrink band (B6). The total was
    ///         blind twice over: offsetting drift between the causes summed to the same number — a slot
    ///         going missing at one endpoint funded by a structural cell leaving at another read as "no
    ///         change" — and the band under it meant even the total could wander by ten with nothing
    ///         registering. Both causes here are STRUCTURAL and cannot move (the registry front door
    ///         genuinely has no mapper class; neither it nor the co-located host declares a mapping method),
    ///         which is exactly why exact pins are honest: any movement at all is a template or catalogue
    ///         change someone must look at, and a NEW cause is a slot that went missing wearing a verdict
    ///         that says nothing can be done about it.
    ///     </para>
    ///     <para>Measured 2026-08-22, in the commit that introduced the pins. Total 116, unchanged.</para>
    /// </summary>
    private static readonly Dictionary<string, int> NoSuchSiteCausePins = new(StringComparer.Ordinal)
    {
        ["registry-has-no-mapper-class"] = 48,
        ["no-mapping-method"] = 68
    };

    /// <summary>
    ///     The cells with no declaration site, counted AND broken down by cause.
    ///     <para>
    ///         This is the largest population that passes without deciding anything, and it was the last one
    ///         outside every ratchet — about a sixth of the matrix, described in an earlier report as already
    ///         pinned when only its member-slot half was, and only at FIXTURE granularity. "137 cells have no
    ///         declaration site" is not a reviewable statement; the breakdown is the point, because the
    ///         causes were not the same kind of thing.
    ///     </para>
    ///     <para>
    ///         137 → 116, and the breakdown is now the whole story: every remaining cell is one of the two
    ///         STRUCTURAL causes. <c>registry-has-no-mapper-class</c> (48) and <c>no-mapping-method</c> (68)
    ///         cannot move — the registry front door genuinely has no mapper class, and neither it nor the
    ///         co-located host declares a mapping method, so there is nothing an improved template could
    ///         annotate.
    ///     </para>
    ///     <para>
    ///         The 21 that left were <c>no-fixture-declares-one</c>: <c>[MapTo]</c>'s fourteen Struct-site
    ///         cells and <c>[DwarfMapperConstructor]</c>'s seven Constructor-site cells, a TEMPLATE
    ///         limitation wearing the same verdict as a structural absence. Both sites now splice at a
    ///         marker through the same one mechanism the Property and Field sites use, and all 21 are
    ///         measured — which is what surfaced the <c>[MapTo]</c>-on-a-struct emission defect and the
    ///         three silent <c>[DwarfMapperConstructor]</c> cells. That is the point of counting a
    ///         population nobody judges: it is where defects go to not be noticed.
    ///     </para>
    /// </summary>
    [Fact]
    public void The_cells_with_no_declaration_site_are_counted_by_cause()
    {
        // The cause key is everything before the first ':' — the machine-readable half of the reason
        // Endpoints.SiteAbsenceReason states, which is the single source every NoSuchSite verdict flows
        // from, so this grouping cannot disagree with the classifier about what a cause IS.
        var siteless = AllCells().Where(x => x.Effect is SurfaceEffect.NoSuchSite).ToList();
        var byCause = siteless
            .GroupBy(x => CauseOf(x.Detail), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var breakdown = string.Join("\n", byCause
            .OrderByDescending(kvp => kvp.Value)
            .Select(kvp => $"  {kvp.Value,4}  {kvp.Key}"));

        var unpinned = byCause.Keys
            .Where(cause => !NoSuchSiteCausePins.ContainsKey(cause))
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
        Assert.True(unpinned.Count == 0,
            "Cells with no declaration site under cause(s) this test does not pin:\n  "
            + string.Join("\n  ", unpinned) + $"\n\nFull breakdown:\n{breakdown}\n\nBoth pinned causes are "
            + "STRUCTURAL — there is genuinely nothing at those endpoints for a template to annotate. A "
            + "cell arriving under any OTHER cause is a slot that went missing wearing a verdict that says "
            + "nothing can be done: give the endpoint template or the fixture in play the marker that site "
            + "splices at, rather than pinning the absence.");

        // C5: this loop used to open-code AssertExactPin's two asserts. Every other pinned population in
        // this file goes through the shared helper, and a hand-rolled copy is how one pin ends up with a
        // different meaning from its neighbours after somebody improves the helper — the very drift the
        // helper was extracted to stop. The per-cause reasoning that made the copy look necessary rides in
        // as howToClose, and the full breakdown with it, so nothing the messages used to say is lost.
        foreach (var (cause, pinned) in NoSuchSiteCausePins)
        {
            var cells = siteless
                .Where(x => string.Equals(CauseOf(x.Detail), cause, StringComparison.Ordinal))
                .Select(x => $"  {x.Rendered} on a {x.Case.Site} @ {x.Endpoint}")
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            AssertExactPin(cells, pinned, $"cells have no declaration site under '{cause}'",
                $"Full breakdown:\n{breakdown}\n\nPer-cause and exact on purpose (B6): the old total-only "
                + "ceiling let a slot go missing at one endpoint as long as a structural cell left at "
                + "another — offsetting drift summing to green. Both pinned causes are STRUCTURAL and "
                + "cannot grow, so a growth here is a catalogue or template change someone must look at; a "
                + "shrink means the endpoint gained the surface (lower the pin in the same commit) or the "
                + "catalogue lost cases that should still exist.");
        }
    }

    private static string CauseOf(string detail)
    {
        var colon = detail.IndexOf(':', StringComparison.Ordinal);
        return colon <= 0 ? detail : detail[..colon];
    }

    /// <summary>
    ///     The structural claim covering one option-bag case at one endpoint, or <c>null</c>.
    ///     <para>
    ///         An option-bag case renders as <c>Name = value</c> and its axis label is <c>Name=value</c>, so
    ///         the option name is everything before the first <c>=</c>. Nothing restricts the lookup to the
    ///         bags themselves and nothing needs to: <c>Every_exemption_names_a_real_option_and_endpoint</c>
    ///         already holds every key in <see cref="DeclaredDivergences.StructurallyInapplicable" /> to a
    ///         real <c>[DwarfMapper]</c> option name, so a directive's own named argument —
    ///         <c>[MapProperty(Use = …)]</c>, axis <c>Use="probe"</c> — cannot collide with one.
    ///     </para>
    /// </summary>
    private static string? StructurallyInapplicableOption(string axis, Endpoint endpoint)
    {
        var eq = axis.IndexOf('=', StringComparison.Ordinal);
        if (eq <= 0) return null;
        return DeclaredDivergences.StructurallyInapplicable
            .TryGetValue((axis[..eq], endpoint), out var why) ? why : null;
    }

    /// <summary>
    ///     The ceiling on cells excused as structural for one option of a bag. Shrink-only.
    ///     <para>
    ///         12 → <b>13</b>, and it is the only ratchet raised anywhere on this branch. It bought the
    ///         reclassification of <c>D17</c>: <c>[assembly: DwarfMapperDefaults(RegisterCollectionShapes =
    ///         false)]</c> at the <c>[MapTo]</c> registry front door. The reason is a MEASUREMENT, not a
    ///         reading of the word "registry" — A5 dumped that front door's whole output with and without each
    ///         assembly option and found an extension class and nothing else: no
    ///         <c>[assembly: DwarfProvidesMap]</c> and no <c>DwarfMapperRegistry.Register</c> call. The option
    ///         governs <c>AggregateEmitter.EmitAmbientRegistration</c>, which runs over
    ///         <c>MapperClassModel</c>s only, so <c>[MapTo]</c> maps are not in the ambient registry at all
    ///         and there are no rows here to withhold. Exactly ONE cell moved: the class-level twin at that
    ///         endpoint is <see cref="SurfaceEffect.NoSuchSite" /> (the registry has no mapper class to
    ///         annotate), so only the assembly-level cell was ever excusable.
    ///     </para>
    ///     <para>
    ///         The raise is the price of saying "there is nothing here to configure" out loud, which is what
    ///         this count exists to charge for. A5 declined to pay it and left the finding standing; the
    ///         judgement was made here instead, in a commit that carries the measurement.
    ///     </para>
    /// </summary>
    private const int StructurallyExcusedCellCeiling = 13;

    /// <summary>
    ///     Every cell excused because ONE OPTION of an option bag has no surface at that endpoint, counted.
    ///     <para>
    ///         This excuse is the only one in the matrix that is not self-retiring, and that asymmetry is why
    ///         it is counted. A <see cref="DeclaredDivergences.Reasons" /> row fails the moment its cell
    ///         starts working; a structural row cannot, because "there is nothing here to configure" and "it
    ///         is configured correctly" are both non-failures on the claimed branch. If <c>UpdateInto</c> ever
    ///         grew a convenience extension, the <c>GenerateExtensions</c> cell would flip Silent → Honoured
    ///         and this entry would go on sitting there. The count is the containment: the population may not
    ///         grow, so a new structural excuse is as deliberate an act as a new divergence.
    ///     </para>
    /// </summary>
    [Fact]
    public void The_cells_excused_as_structural_are_counted()
    {
        var excused = new List<string>();
        foreach (var element in SurfaceCatalog.CrossProductElements)
        foreach (var c in SurfaceCatalog.CasesFor(element))
        foreach (var endpoint in EndpointSources.All)
        {
            if ((SurfaceCatalog.ClaimFor(element, c.Site) & ToFlag(endpoint)) == SurfaceEndpoints.None) continue;
            if (SurfaceProbe.Classify(c, endpoint).Effect is not SurfaceEffect.Silent) continue;
            if (StructurallyInapplicableOption(c.Axis, endpoint) is not { } why) continue;
            excused.Add($"  {element.UsageName}({c.Axis}) on a {c.Site} @ {endpoint} — {why}");
        }

        excused.Sort(StringComparer.Ordinal);
        AssertRatchet(excused, StructurallyExcusedCellCeiling,
            "cells are excused because one option of a bag has no surface at that endpoint",
            "Close one by giving the endpoint the surface the option configures, and delete the "
            + "StructurallyInapplicable row — nothing else will notice that it became stale.");
    }

    /// <summary>
    ///     The number of FINDINGS recorded as unfixed divergences. Shrink-only.
    ///     <para>
    ///         23 → 19 when <c>DWARF088</c> and the <c>DWARFR04</c> wiring fix landed: <c>D3</c>, <c>D4</c>,
    ///         <c>D5</c> and <c>D21</c> were one shape — the wrong OVERLOAD of a directive, accepted in
    ///         silence — and one arity check on each side of the library retired all four.
    ///     </para>
    ///     <para>
    ///         19 → 18 when <c>D20</c> was closed by the co-located host learning to read the member forms
    ///         its own <c>[DwarfSurfaceSite]</c> had always claimed it did.
    ///     </para>
    ///     <para>
    ///         15 → 13 when <c>D18</c> and <c>D19</c> were closed together: <c>MapToGenerator</c> read no
    ///         assembly-level configuration at all, and one hoisted reader
    ///         (<c>Pipeline/AssemblyConfiguration</c>) gave that front door the same resolution the other two
    ///         already used. <c>D17</c> shared the root cause and did NOT close — the option withholds ambient
    ///         registry rows and that front door emits none — so it stays here with its reason corrected.
    ///     </para>
    ///     <para>
    ///         13 → <b>12</b> when <c>D10</c> closed. That one is worth reading as a correction and not as a
    ///         fix: the finding's own note claimed <c>[Flatten]</c> was "honoured at CreateMap and UpdateInto"
    ///         against a fixture with a real nested member, and it was not — against <c>nested-pair</c> the
    ///         directive emitted BYTE-IDENTICAL output at all five endpoints, and the two cells that read
    ///         <c>Refused</c> read so because of an incidental <c>DWARF044</c> nullable-hop warning. A finding
    ///         can be wrong about the endpoint it holds up as working, and this one was.
    ///     </para>
    ///     <para>
    ///         12 → <b>11</b> when <c>D11</c> closed. <c>[FlattenGraph]</c> was read on the create map and
    ///         discarded on the same mapper's four other overloads; the new <c>DWARF092</c> gate
    ///         (<c>MapperExtractor.ReportCreateMapOnlyDirectives</c>, one function, four call sites) refuses
    ///         it at all four. Re-measured in the same commit, not predicted.
    ///     </para>
    ///     <para>
    ///         11 → <b>10</b> when <c>D8</c> closed, on the same gate. Like <c>D10</c>, worth reading as a
    ///         correction as well as a fix: the entry claimed <c>[MapDerivedType]</c> "acts at CreateMap … in
    ///         BOTH the open and the generic form", and the OPEN form did not act anywhere — the flat DTO pair
    ///         declares no hierarchy, so the sampled arguments named a type not assignable to the method's
    ///         source parameter and the cell read <c>NotCompilable</c>. A finding can be wrong about the
    ///         endpoint it holds up as working, and that is now twice.
    ///     </para>
    ///     <para>
    ///         10 → <b>9</b> when <c>D13</c> closed, the third and last directive on the <c>DWARF092</c>
    ///         gate. Its evidence held on the substance and was imprecise on the mechanism: <c>[ReverseMap]</c>
    ///         does not GENERATE an inverse, it makes a separately-declared one inherit the forward renames
    ///         inverted, and a missing inverse is <c>DWARF052</c> rather than a silent absence. Corrected
    ///         where the entry stood, and the message deliberately does not repeat the wrong model.
    ///     </para>
    ///     <para>
    ///         9 → <b>8</b> when <c>D14</c> closed. <c>[MapCollectionKey]</c> is the MIRROR image of the three
    ///         above — read at the <c>UpdateInto</c> endpoint and discarded at the other four, the create map
    ///         included — so the <c>DWARF092</c> gate is generalized into
    ///         <c>MapperExtractor.ReportDirectivesNotReadHere</c>, called from all FIVE branches with each arm
    ///         naming its own home endpoint. And, for the third time, the finding's evidence was false about
    ///         the endpoint it held up as working: <c>keyed-collection-elements</c> declared <c>List&lt;Item&gt;</c>
    ///         against <c>List&lt;ItemDto&gt;</c>, the v1 upsert requires the same element type, and the
    ///         <c>UpdateInto</c> cell was <c>DWARF074</c> behind <c>CS8795</c>. Re-measured in the same commit.
    ///     </para>
    ///     <para>
    ///         8 → <b>7</b> when <c>D12</c> closed. <c>[Reinterpret]</c> is a member directive an element-wise
    ///         map cannot apply, which is <c>DWARF090</c>'s shape exactly, and it is the first arm of that gate
    ///         with NO pair-scoped twin — so its remedy is a DECLARED create map, measured before it was
    ///         prescribed. The entry was also wrong about one endpoint, which is the fourth time on this
    ///         branch: it claimed the directive acts at <c>Projection</c>, and only the create-map and
    ///         update-into branches read it. That cell is <c>UnhonouredButLoud</c> and stays there —
    ///         <see cref="UnhonouredButLoudCellCeiling" /> re-measured at 14, unchanged.
    ///     </para>
    ///     <para>
    ///         7 → <b>6</b> when <c>D15</c> closed, as the new <c>DWARF093</c>. Its stated mechanism was wrong
    ///         and the fork rested on it: the <c>DWARF067</c> at <c>CoLocatedHost</c> is an opinion about the
    ///         WRAPPER TYPE, not about placement, and it fired there only because that template declares a
    ///         <c>[GenerateMap]</c> pair while the sampled <c>typeof(Dst)</c> is not a single-parameter
    ///         generic. The mapper-endpoint silence was <c>ExpandWrapperMaps</c> returning early on an empty
    ///         pair list. Refused rather than emitted, argued from the attribute's own contract — it is an
    ///         expansion of the <c>[GenerateMap]</c> list, and four of the five endpoints have no
    ///         <c>W&lt;A&gt; -&gt; W&lt;B&gt;</c> create-map shape to synthesize at all.
    ///     </para>
    ///     <para>
    ///         6 → <b>4</b> when <c>D6</c> and <c>D7</c> closed together, which is how they were found: two
    ///         entries over one option written at two scopes, with three partial readers of it between them.
    ///         The last reader was the projection call site, which had been handed the bare class value.
    ///         Threading it was one line, and it was built, measured and REVERTED at A6 — not because the
    ///         generator behaviour was wrong but because <c>DWARF028</c> is an Error and, before R4, the
    ///         <c>CS8795</c> that follows a suppressed emission read as
    ///         <see cref="SurfaceEffect.NotCompilable" />: the fix measured as three cells moving INTO the
    ///         population this matrix judges by nothing. R4 is fixed, so the same three cells now read
    ///         <c>Refused (DWARF028 (behind CS8795))</c> and
    ///         <see cref="NotCompilableCellCeiling" /> did not move at all.
    ///     </para>
    ///     <para>
    ///         4 → <b>3</b> when <c>D9</c> closed, the third and last entry parked on the same instrument
    ///         defect. <c>[MapValue]</c> was threaded into the projection resolver in the position
    ///         <c>ResolveMembers</c> reads it, through the create map's OWN validation sequence rather than a
    ///         copy of it (<c>TryValidateMapValueTarget</c>). <c>Use=</c> is refused there as
    ///         <c>DWARF028</c> — a query provider cannot call back into managed code from inside an
    ///         expression tree, the treatment <c>[MapProperty(Use=)]</c> already gets — and a constant simply
    ///         becomes a literal in the <c>SELECT</c>.
    ///     </para>
    ///     <para>
    ///         3 → <b>2</b> when <c>D17</c> was reclassified rather than fixed — the one entry on this branch
    ///         that left by being judged structural rather than by starting to work, and the only ratchet
    ///         raise anywhere on it. See <see cref="StructurallyExcusedCellCeiling" /> for the measurement
    ///         that earned it.
    ///     </para>
    /// </summary>
    private const int DivergenceFindingCeiling = 2;

    /// <summary>
    ///     The number of CELLS those findings cover. Shrink-only, and the wider of the two guards.
    ///     <para>
    ///         162 → 113, the forty-nine cells those four findings covered, every one re-measured as
    ///         <c>Refused</c> rather than reasoned about. They became <c>Refused</c> and not
    ///         <c>NotCompilable</c> because <c>DWARF088</c> was a WARNING at the time: an Error suppressed the
    ///         class's emission and the refusal arrived as <c>CS8795</c> instead, which is the G4/R4 ordering
    ///         defect and would have moved these cells into
    ///         <see cref="NotCompilableCellCeiling" />'s population rather than out of this one. <b>That is
    ///         history now, in both halves.</b> R4 is fixed, so a <c>CS8795</c> behind a blocking DWARF error
    ///         reads <c>Refused</c>; and with the ratchet argument dead, <c>DWARF088</c> was escalated to an
    ///         <b>Error</b> on the product grounds it should have been decided on in the first place. All 47
    ///         of its cells were re-measured across the escalation and every one still reads <c>Refused</c> —
    ///         45 as <c>DWARF088 (behind CS8795)</c> at the five mapper endpoints, 2 plainly at
    ///         <c>CoLocatedHost</c>, which declares no partial mapping method to strand.
    ///     </para>
    ///     <para>
    ///         113 → 93 when <c>D20</c> was closed: the co-located host now reads the member-placement
    ///         <c>[MapProperty]</c> / <c>[MapIgnore]</c> forms off its own members. Unlike the four above,
    ///         this one did not resolve to a single verdict — measured, <b>2 cells are Honoured and 18
    ///         Refused</b>, and the twenty are three different things: <b>6</b> where the directive ACTS
    ///         (the 2 <c>Honoured</c>, plus <b>4</b> labelled <c>Refused</c> only because the rename they
    ///         perform raises the pre-existing <c>DWARF038</c> and the probe checks diagnostics before it
    ///         compares output), <b>6</b> refusals of a named argument the binding now reaches at all, and
    ///         <b>8</b> <c>DWARF089</c> — the method form written on a member. All three leave this
    ///         population; none enters <see cref="NotCompilableCellCeiling" />'s, because <c>DWARF089</c> is
    ///         a Warning and the host declares no partial method there would be a <c>CS8795</c> for.
    ///     </para>
    ///     <para>
    ///         84 → 82 with <c>D18</c> and <c>D19</c>, one cell each, and one verdict each: <c>D19</c> is now
    ///         <c>Refused (DWARFR10)</c> and <c>D18</c> <c>Honoured</c>. Re-measured across the whole matrix
    ///         before and after — <b>exactly those two rows differ</b>, both at <c>Registry</c> on the
    ///         <c>Assembly</c> site, which is the boundary the flip had to respect: the registry now emits an
    ///         <c>internal</c> extension class by default, and every other cell is byte-identical.
    ///     </para>
    ///     <para>
    ///         82 → <b>76</b> when <c>[MapNullSkip]</c>'s three readers became one
    ///         (<c>MapperExtractor.ResolveNullSkip</c>). <c>D6</c> and <c>D7</c> were narrowed rather than
    ///         deleted, which is the case this file's own failure message describes: six of their nine cells
    ///         closed with two verdicts — <b>4 Honoured</b> (the pair-scoped form at <c>CreateMap</c> and
    ///         <c>UpdateInto</c>, which it had never reached) and <b>2 Refused (DWARF090)</b> (the method form
    ///         element-wise, where a shared synthesized mapper structurally cannot see it) — and the three
    ///         <c>Projection</c> cells did not. They did not because the honest refusal there is the blocking
    ///         <c>DWARF028</c> the class-level option already gets, whose <c>CS8795</c> cascade would have moved
    ///         them into <see cref="NotCompilableCellCeiling" />'s population (measured: 99 → 102) instead of
    ///         out of this one. <see cref="DivergenceFindingCeiling" /> therefore did NOT move.
    ///     </para>
    ///     <para>
    ///         76 → <b>62</b> when <c>D9</c> and <c>D10</c> were worked. Fourteen cells, and — like the entry
    ///         above — not one verdict. <c>D10</c> closes ENTIRELY, six cells: <b>3 Honoured</b> (a
    ///         <c>[Flatten]</c> is now resolved by the projection translator too, through the single
    ///         <c>ResolveFlattenInfos</c> walk both resolvers call) and <b>3 Refused</b> (<c>DWARF017</c> for
    ///         the doubled directive, <c>DWARF090</c> element-wise). <c>D9</c> is NARROWED, eight cells:
    ///         <c>[MapValue]</c> at <c>SpanMap</c> and <c>AsyncStream</c> is now <c>DWARF090</c>, whose
    ///         pair-scoped remedy was measured <c>Honoured</c> at both before the message named it. Its four
    ///         <c>Projection</c> cells did not close, for the reason <c>D6</c>/<c>D7</c> did not: the
    ///         threading was built and measured, one cell closes and three land on <c>DWARF042</c>/
    ///         <c>DWARF041</c>, which are Errors, so <see cref="NotCompilableCellCeiling" />'s population
    ///         measured 99 → 102 — the same R4 ordering defect, and forbidden.
    ///     </para>
    ///     <para>
    ///         62 → <b>54</b> when <c>D11</c> closed: eight cells, one verdict — <b>8 Refused
    ///         (DWARF092)</b>, <c>[FlattenGraph]</c> at <c>UpdateInto</c>, <c>Projection</c>, <c>SpanMap</c>
    ///         and <c>AsyncStream</c> for both of its axes. A <b>Warning</b>, deliberately, so the cells land
    ///         in this population's complement rather than in
    ///         <see cref="NotCompilableCellCeiling" />'s — which is the whole reason <c>D6</c>/<c>D7</c> and
    ///         <c>D9</c> could not be closed the same way. That ceiling was re-measured at <b>99</b>,
    ///         unchanged.
    ///     </para>
    ///     <para>
    ///         54 → <b>38</b> when <c>D8</c> closed: sixteen cells, one verdict — <b>16 Refused
    ///         (DWARF092)</b>, both <c>[MapDerivedType]</c> forms at all four non-create-map endpoints.
    ///         <see cref="NotCompilableCellCeiling" /> moved too, and DOWN: the new
    ///         <c>polymorphic-hierarchy</c> fixture gives the open form a hierarchy to dispatch over, so its
    ///         <c>ctor(2)</c> cell at <c>CreateMap</c> is <c>Honoured</c> rather than <c>DWARF035</c> behind
    ///         <c>CS8795</c>, measured <b>99 → 98</b>.
    ///     </para>
    ///     <para>
    ///         38 → <b>34</b> when <c>D13</c> closed: four cells, <b>4 Refused (DWARF092)</b>,
    ///         <c>[ReverseMap]</c> at the four non-create-map endpoints. Its <c>CreateMap</c> cell is NOT part
    ///         of that and stays in <see cref="NotCompilableCellCeiling" />'s population: the endpoint
    ///         templates declare exactly ONE mapping method, so no inverse can exist there and
    ///         <c>DWARF052</c> — an Error — always fires. No fixture can lift that; a fixture supplies TYPES,
    ///         not a second method. It is a template limitation adjacent to G5's, not a divergence, and it is
    ///         why that ceiling stayed at 98 rather than moving again.
    ///     </para>
    ///     <para>
    ///         34 → <b>26</b> when <c>D14</c> closed: eight cells, one verdict — <b>8 Refused (DWARF092)</b>,
    ///         <c>[MapCollectionKey]</c> at <c>CreateMap</c>, <c>Projection</c>, <c>SpanMap</c> and
    ///         <c>AsyncStream</c> for both of its axes. <see cref="NotCompilableCellCeiling" /> moved too, and
    ///         DOWN, for the same reason it did under <c>D8</c>: the fixture could not pose its question. With
    ///         ONE element type on both sides the upsert is emitted, so both <c>UpdateInto</c> cells read
    ///         <c>Honoured</c> rather than <c>CS8795</c> behind a <c>DWARF074</c> — measured <b>98 → 96</b>.
    ///     </para>
    ///     <para>
    ///         26 → <b>22</b> when <c>D12</c> closed: four cells, one verdict — <b>4 Refused (DWARF090)</b>,
    ///         <c>[Reinterpret]</c> at <c>SpanMap</c> and <c>AsyncStream</c> for both of its axes. No other
    ///         population moved: <see cref="NotCompilableCellCeiling" /> stayed at 96 and
    ///         <see cref="UnhonouredButLoudCellCeiling" /> at 14, the latter because the <c>Projection</c>
    ///         cell the entry wrongly held up as working is left exactly where it was.
    ///     </para>
    ///     <para>
    ///         22 → <b>12</b> when <c>D15</c> closed: ten cells, one verdict — <b>10 Refused (DWARF093)</b>,
    ///         <c>[GenerateWrapperMap]</c> at all five mapper endpoints for both of its axes. A Warning, and
    ///         reported BEFORE the wrapper's shape is validated, precisely so these ten land here rather than
    ///         in <see cref="NotCompilableCellCeiling" />'s population: <c>DWARF067</c> is an Error.
    ///         Re-measured, and it stayed at <b>96</b>.
    ///     </para>
    ///     <para>
    ///         12 → <b>9</b> when <c>D6</c> and <c>D7</c> closed: three cells, one verdict — <b>3 Refused
    ///         (DWARF028 behind CS8795)</b>, <c>[MapNullSkip]</c>'s method form and its pair-scoped form's two
    ///         axes, all at <c>Projection</c>. The refusal is not new and was not written for this: the
    ///         projection resolver has always reported <c>DWARF028</c> for an untranslatable null-skip, which
    ///         is what the class-level <c>SkipNullSourceMembers</c> and its assembly twin already got there.
    ///         The two scoped forms simply never reached it. <see cref="NotCompilableCellCeiling" /> was
    ///         re-measured across the change and stayed at <b>10</b>; the three cells left
    ///         <see cref="SurfaceEffect.Silent" /> (148 → 145) for <see cref="SurfaceEffect.Refused" />
    ///         (362 → 365) and nothing else moved.
    ///     </para>
    ///     <para>
    ///         9 → <b>5</b> when <c>D9</c> closed: four cells, all <c>Refused</c>, and not all alike —
    ///         <c>ctor(2)</c> reads <c>DWARF064 (Info)</c> with the constant genuinely assigned, while
    ///         <c>ctor(1)</c>, <c>×2</c> and <c>Use="probe"</c> read <c>DWARF042</c>, <c>DWARF042</c> and
    ///         <c>DWARF028</c> respectively, each behind <c>CS8795</c>. That mix is the reading A8 measured
    ///         and reverted, and it is why this closed only after R4: three of the four were, before it,
    ///         cells LEAVING this population for the one nothing judges.
    ///         <see cref="NotCompilableCellCeiling" /> was re-measured and stayed at <b>10</b>;
    ///         <see cref="SurfaceEffect.Silent" /> fell 145 → 141 and <see cref="SurfaceEffect.Refused" />
    ///         rose 365 → 369, which is the four cells and nothing else.
    ///     </para>
    ///     <para>
    ///         5 → <b>4</b> with <c>D17</c>. The cell did not change VERDICT — it is
    ///         <see cref="SurfaceEffect.Silent" /> before and after — it changed which store excuses it, from
    ///         a recorded defect to a structural claim, and <see cref="StructurallyExcusedCellCeiling" /> rose
    ///         by exactly the one cell this fell by. That is the trade stated as a pair of numbers rather than
    ///         as a paragraph.
    ///     </para>
    /// </summary>
    private const int DivergentCellCeiling = 4;

    /// <summary>
    ///     Neither the number of recorded divergences nor the number of cells they cover may grow.
    ///     <para>
    ///         Two counts rather than one, because they fail on different mistakes. The FINDING count catches
    ///         a new defect being written down instead of fixed. The CELL count catches the subtler and more
    ///         likely one: widening an existing entry's cell list so a fresh regression is absorbed by a row
    ///         that already exists. Without it, a new silent cell added to <c>D3</c>'s list would pass both
    ///         the parity theory (it is declared) and the still-a-divergence gate (it is silent) with nothing
    ///         anywhere registering that the surface got worse.
    ///     </para>
    /// </summary>
    [Fact]
    public void The_recorded_divergences_are_counted()
    {
        var findings = DeclaredDivergences.Reasons
            .Select(e => $"  {e.Key}: {e.Value.Cells.Count} case(s) — {e.Value.Section}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        const string maintainerDecision =
            "A new divergence is a MAINTAINER DECISION, not a way past a red build: it says the generator "
            + "has a defect that will ship. Fix the generator, or refuse the directive with a diagnostic. "
            + "Only if neither is possible now does a row belong here — and then someone raises this number "
            + "deliberately, in a commit that says why.";

        AssertExactPin(findings, DivergenceFindingCeiling, "divergences are recorded as unfixed",
            maintainerDecision);

        var cells = DeclaredDivergences.AllDeclaredCells()
            .Select(x => $"  {x.Id}: {x.Cell.UsageName}`{x.Cell.Arity}({x.Cell.Axis}) on a {x.Cell.Site} "
                         + $"@ {x.Endpoint}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        AssertExactPin(cells, DivergentCellCeiling, "cells are covered by a recorded divergence",
            maintainerDecision);
    }

    /// <summary>
    ///     Every declared divergence is re-measured, and must STILL be one.
    ///     <para>
    ///         This is what separates this store from an allowlist, and it is the whole reason the store may
    ///         exist at all. A row that merely permits a cell to fail is an exemption: it goes on passing
    ///         forever, including after someone fixes the generator, and the next reader has no way to tell a
    ///         live defect from a fossil. A row that FAILS when its cell starts working is a ratchet — the
    ///         fix turns the build red, and the only way back to green is deleting the row.
    ///     </para>
    ///     <para>
    ///         Three ways a row can go stale and all three fail here: the cell is no longer silent (someone
    ///         honoured or refused it — delete the row); the cell no longer exists (an element, axis or site
    ///         was renamed, so the row governs nothing while reading as a reviewed decision); and the cell's
    ///         endpoint is no longer CLAIMED (a later <c>AppliesTo</c> narrowing took it, so the divergence
    ///         was resolved by declaring it away and the row is now a second, contradictory record of the
    ///         same cell).
    ///     </para>
    /// </summary>
    [Fact]
    public void Every_declared_divergence_is_still_a_divergence()
    {
        var stale = new List<string>();

        foreach (var (id, _, cell, endpointFlag) in DeclaredDivergences.AllDeclaredCells())
        {
            var where = $"{id}: {cell.UsageName}`{cell.Arity}({cell.Axis}) on a {cell.Site} @ {endpointFlag}";

            var element = SurfaceCatalog.CrossProductElements.FirstOrDefault(
                e => string.Equals(e.UsageName, cell.UsageName, StringComparison.Ordinal)
                     && Arity(e) == cell.Arity);
            var probed = element is null
                ? null
                : SurfaceCatalog.CasesFor(element).FirstOrDefault(
                    x => string.Equals(x.Axis, cell.Axis, StringComparison.Ordinal) && x.Site == cell.Site);

            if (probed is null)
            {
                stale.Add($"{where} — names no cell the matrix measures. The element, its axis or its site "
                          + "changed; the row now governs nothing while reading as a reviewed decision.");
                continue;
            }

            var endpoint = Enum.Parse<Endpoint>(endpointFlag.ToString());
            if ((SurfaceCatalog.ClaimFor(element!, probed.Site) & endpointFlag) == SurfaceEndpoints.None)
            {
                stale.Add($"{where} — the element no longer CLAIMS this endpoint, so the cell is not judged "
                          + "here any more. The divergence was declared away rather than fixed; delete the "
                          + "row so one cell is not recorded twice in two contradictory ways.");
                continue;
            }

            var (effect, detail) = SurfaceProbe.Classify(probed, endpoint);
            if (effect is not SurfaceEffect.Silent)
                stale.Add($"{where} — is now {effect} ({detail}), not Silent.");
        }

        Assert.True(stale.Count == 0,
            $"{stale.Count} recorded divergence cell(s) are no longer divergent:\n  "
            + string.Join("\n  ", stale)
            + "\n\nDELETE the row. This gate is the difference between a ratchet and an allowlist: a fixed "
            + "gap left on the list quietly re-permits the divergence if it ever comes back, and the reader "
            + "of the list cannot tell which entries are live. If a FINDING has lost only some of its cells, "
            + "narrow its cell list and lower DivergentCellCeiling to lock the improvement in.");
    }

    /// <summary>
    ///     No cell is covered twice, and no finding covers nothing.
    ///     <para>
    ///         Two cells' worth of bookkeeping that decide whether the counts above mean anything. A finding
    ///         covering no cell inflates the finding count while pinning nothing; two findings covering one
    ///         cell make the cell count larger than the population it describes, so the ratchet has slack
    ///         nobody put there on purpose — and deleting one of the two would leave the cell still excused,
    ///         which is how an entry stops being load-bearing without anyone noticing.
    ///     </para>
    /// </summary>
    [Fact]
    public void Every_finding_covers_cells_and_no_cell_is_covered_twice()
    {
        var empty = DeclaredDivergences.Reasons
            .Where(e => e.Value.Cells.Count == 0)
            .Select(e => e.Key).ToList();
        Assert.True(empty.Count == 0,
            "Finding(s) covering no cell at all: " + string.Join(", ", empty)
            + ". A finding that pins nothing is a note, not a record — delete it or name its cells.");

        var duplicates = DeclaredDivergences.AllDeclaredCells()
            .GroupBy(x => $"{x.Cell.UsageName}`{x.Cell.Arity}({x.Cell.Axis}) on a {x.Cell.Site} @ {x.Endpoint}",
                StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} — claimed by {string.Join(" and ", g.Select(x => x.Id))}")
            .ToList();
        Assert.True(duplicates.Count == 0,
            "Cell(s) covered by more than one finding:\n  " + string.Join("\n  ", duplicates)
            + "\n\nOne cell is evidence for one finding. Two rows over one cell means one of them can be "
            + "deleted without the cell becoming visible again.");

        foreach (var (id, divergence) in DeclaredDivergences.Reasons)
        {
            // Checked over EVERY entry rather than at the point each is consulted, because the option
            // matrix's lookup is by option name and would never reach an entry that only the surface matrix
            // covers. A row with no reason is a permission slip.
            Assert.False(string.IsNullOrWhiteSpace(divergence.Why),
                $"{id} states no reason. The reason is the whole difference between a recorded defect and an "
                + "exemption: it must say what a caller who wrote this reasonably expects.");

            AssertEvidenceLinkResolves(id, divergence.Section);
        }
    }

    /// <summary>
    ///     The evidence link must RESOLVE — file and anchor — not merely look like a link (B13).
    ///     <para>
    ///         The predecessor asserted <c>StartsWith("Issues/")</c> and <c>Contains('#')</c>, which is
    ///         satisfied by <c>Issues/#</c>. Every one of these entries is a defect record whose whole
    ///         warrant is "the evidence is written up over there"; a link that no longer lands is that
    ///         warrant silently withdrawn, and renaming a findings document or re-titling one of its
    ///         sections is exactly the ordinary edit that does it. All 26 anchors resolved when this went
    ///         in, so it is a latent hole being closed rather than a break being found.
    ///     </para>
    ///     <para>
    ///         Anchors are matched as an explicit <c>&lt;a id="…"&gt;</c>, which is the form these documents
    ///         use throughout, OR as a GitHub heading slug — lower-cased, non-alphanumerics dropped, spaces
    ///         hyphenated — so a link written the ordinary markdown way is accepted too rather than forcing
    ///         the explicit-anchor convention on a future document.
    ///     </para>
    /// </summary>
    private static void AssertEvidenceLinkResolves(string id, string section)
    {
        var hash = section.IndexOf('#', StringComparison.Ordinal);
        Assert.True(section.StartsWith("Issues/", StringComparison.Ordinal) && hash > "Issues/".Length,
            $"{id} does not link to a section of the findings write-up ('{section}'). The reason field "
            + "states what a caller expects; the write-up carries the evidence, and a record with nowhere "
            + "to read the evidence is an assertion.");

        var relative = section[..hash];
        var anchor = section[(hash + 1)..];
        Assert.False(string.IsNullOrWhiteSpace(anchor), $"{id}'s evidence link '{section}' names no anchor.");

        var path = Path.Combine(RepoPaths.Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path),
            $"{id}'s evidence link points at '{relative}', which does not exist. The write-up was moved or "
            + "renamed and the record now cites nothing.");

        var text = File.ReadAllText(path);
        var resolved = text.Contains($"<a id=\"{anchor}\"", StringComparison.Ordinal)
                       || HeadingSlugs(text).Contains(anchor);

        Assert.True(resolved,
            $"{id}'s evidence link '{section}' names an anchor that '{relative}' does not define. Neither "
            + $"an <a id=\"{anchor}\"> nor a heading slugging to '{anchor}' is in the file, so the link "
            + "lands at the top of the document and the reader has to hunt for the evidence the record "
            + "claims is written up.");
    }

    /// <summary>
    ///     GitHub's heading-to-anchor rule: drop all but word characters, spaces and hyphens, then hyphenate
    ///     the spaces. The real rule also lower-cases; the set is held under
    ///     <see cref="StringComparer.OrdinalIgnoreCase" /> instead, which answers the same question without
    ///     a culture-lowering call (CA1308) and costs only the ability to distinguish two headings that
    ///     differ in case alone — a distinction GitHub itself does not make either.
    /// </summary>
    private static HashSet<string> HeadingSlugs(string markdown)
    {
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith('#')) continue;

            var title = trimmed.TrimStart('#').Trim();
            var slug = new string(title
                .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or ' ' or '_')
                .Select(ch => ch == ' ' ? '-' : ch)
                .ToArray());
            if (slug.Length > 0) slugs.Add(slug);
        }

        return slugs;
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
    ///     Asserts a LARGE counted population against a stated ceiling in both directions: it may not grow,
    ///     and it may not sink more than ten below the ceiling either, because an unratcheted ceiling lets
    ///     the hole reopen silently after someone else's improvement paid for the slack.
    ///     <para>
    ///         <b>The shrink side is a ten-wide tolerance band, not an exact pin</b>, and it is only honest
    ///         for a population large enough that ten is a small fraction of it. At or below ten the band
    ///         swallows the population whole: every count from zero to the ceiling passes, so a legitimate
    ///         deletion silently funds an illegitimate addition and no number moves. Populations that small
    ///         use <see cref="AssertExactPin" /> instead — which is why this method REFUSES a ceiling of ten
    ///         or less rather than trusting each caller to remember.
    ///     </para>
    /// </summary>
    private static void AssertRatchet(List<string> cells, int ceiling, string what, string howToClose)
    {
        Assert.True(ceiling > 10,
            $"AssertRatchet was called with a ceiling of {ceiling}. Its shrink side is a ten-wide tolerance "
            + "band and cannot see churn in a population that small — every count from zero upward would "
            + $"pass, while its doc comment promises otherwise. Use {nameof(AssertExactPin)}.");

        Assert.True(cells.Count <= ceiling,
            $"{cells.Count} {what}, above the stated ceiling of {ceiling}:\n"
            + string.Join("\n", cells) + "\n\nThis number may only shrink. " + howToClose);

        Assert.True(cells.Count >= ceiling - 10,
            $"Only {cells.Count} {what}, well under the ceiling of {ceiling}. Lower the ceiling to lock the "
            + "improvement in.");
    }

    /// <summary>
    ///     Asserts a SMALL counted population at exactly its stated size, in both directions.
    ///     <para>
    ///         The in-house pattern for a population small enough that every member is individually
    ///         accounted for — <c>== 19</c> on the member-slot fixtures, <c>== 15</c> on the bool-flag
    ///         baseline. Exactness is the whole point: under a tolerance band, closing one member buys
    ///         silent room for a brand-new one of the same shape, and the count that was meant to be the
    ///         guard reports nothing. Every move in either direction is a deliberate act, restated here.
    ///     </para>
    /// </summary>
    private static void AssertExactPin(List<string> cells, int pinned, string what, string howToClose)
    {
        Assert.True(cells.Count <= pinned,
            $"{cells.Count} {what}, above the pinned count of {pinned}:\n"
            + string.Join("\n", cells) + "\n\nThis number may only shrink, and it is pinned EXACTLY: the "
            + "room a closed cell frees is not available to a new one. " + howToClose);

        Assert.True(cells.Count >= pinned,
            $"Only {cells.Count} {what}, under the pinned count of {pinned}. If a cell was genuinely closed, "
            + "lower the pin in the same commit to lock the improvement in — this population is pinned "
            + "exactly, so an improvement does not silently fund a replacement.");
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
