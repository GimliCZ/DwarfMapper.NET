// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>What a surface element DOES at one endpoint, observed rather than declared.</summary>
internal enum SurfaceEffect
{
    /// <summary>
    ///     The element changed the emitted output.
    ///     <para>
    ///         <b>Difference, not correctness (B19).</b> This verdict is reached by comparing the emitted
    ///         text with and without the element and finding it DIFFERENT. Nothing here inspects what it
    ///         differs into, so <c>Honoured</c> is compatible with the generator having emitted something
    ///         broken — round 20's A7 scored an unconditionally self-recursive <c>Update</c> as
    ///         <c>Honoured</c>. The full statement of the limitation, and the round-22 work aimed at it,
    ///         is on <c>SurfaceParityTests</c>, where the ceilings this verdict feeds are read.
    ///     </para>
    /// </summary>
    Honoured,

    /// <summary>
    ///     The element produced a diagnostic — a refusal the caller can see.
    ///     <para>
    ///         Includes a BLOCKING refusal, whose emitted consequence is that no mapper is generated and the
    ///         endpoint's partial method is left unimplemented (<c>CS8795</c>). That compiler error is the
    ///         refusal's shadow, not a separate verdict; reading it as one is the defect
    ///         <see cref="SurfaceProbe.Classify" />'s discrimination rule exists to prevent.
    ///     </para>
    /// </summary>
    Refused,

    /// <summary>Accepted, changed nothing observable, compiled. The dangerous one.</summary>
    Silent,

    /// <summary>
    ///     Changed nothing, but the build fails at this endpoint anyway. Distinguished from
    ///     <see cref="Silent" /> deliberately: silence only ships wrong data when the code COMPILES.
    /// </summary>
    UnhonouredButLoud,

    /// <summary>
    ///     The C# compiler rejects the source the case produced — the site is illegal per
    ///     <c>AttributeUsage</c>, the case duplicates a declaration, its sampled arguments fit no constructor.
    ///     The declaration is telling the truth and there is nothing here for the generator to be judged on.
    ///     <para>
    ///         NOT a generator refusal. A blocking DWARF diagnostic also ends in a compiler error — the
    ///         unimplemented partial method — and wore this label for 86 cells until the rule in
    ///         <see cref="SurfaceProbe.Classify" /> separated the two. Those cells read
    ///         <see cref="Refused" /> now and are judged against their claim like every other cell.
    ///     </para>
    /// </summary>
    NotCompilable,

    /// <summary>
    ///     The placement was legal and the GENERATOR emitted C# that does not compile — a compiler error
    ///     reported against a <c>.g.cs</c> file the consumer never wrote and cannot fix.
    ///     <para>
    ///         The exact inverse of <see cref="NotCompilable" />, which is why it needed a verdict of its own
    ///         rather than a note. There the declaration is at fault and the matrix has nothing to judge;
    ///         here the declaration is fine and the generator is at fault, which is worse than any refusal —
    ///         a refusal at least tells the caller what to change. Two real defects landed under the wrong
    ///         label before this existed: <b>N4</b> (two <c>[FlattenGraph]</c> directives filling one
    ///         destination collection emitted a duplicate member initialization, <c>CS1912</c>, now refused
    ///         as <c>DWARF087</c>) and <b>A11-F1</b> (<c>[MapTo]</c> on a struct emitted
    ///         <c>if (source is null) throw …</c> against a type that cannot be null, <c>CS0037</c>, fixed in
    ///         A13). Both were found by reading a <c>NotCompilable</c> list that said there was nothing to
    ///         see.
    ///     </para>
    ///     <para>
    ///         Judged by nothing, like <see cref="NotCompilable" /> — the element's own behaviour is
    ///         unobservable once the build is broken — and counted for exactly that reason, against its own
    ///         shrink-only ceiling.
    ///     </para>
    /// </summary>
    EmittedInvalidCode,

    /// <summary>The endpoint has no such declaration site, so there is no cell here to judge.</summary>
    NoSuchSite,

    /// <summary>
    ///     The case demands a fixture shape, and this endpoint's template did not deliver it — the
    ///     <c>Registry</c> and <c>CoLocatedHost</c> templates declare their own DTO pair and ignore the one a
    ///     case asks for. The attribute was compiled against a shape that cannot trigger it, so the generator
    ///     was never asked the question.
    ///     <para>
    ///         Kept distinct from <see cref="Silent" /> because they look identical in the output and mean
    ///         opposite things: silence means the directive was discarded, this means nothing was said. Only a
    ///         would-be-<see cref="Silent" /> cell is downgraded — a case that is honoured or refused against
    ///         the flat pair anyway has plainly been asked something, and reclassifying it would erase a real
    ///         reading.
    ///     </para>
    /// </summary>
    Unasked
}

/// <summary>
///     Observes what one surface case does at one endpoint, by compiling the same source with and without it.
///     <para>
///         Generalizes <see cref="OptionProbe" /> from the class-level option surface to every declared
///         element. The baseline compile is MEMOIZED per (endpoint, fixture): every case sharing a fixture
///         shares one baseline, which roughly halves a matrix of this size.
///     </para>
/// </summary>
internal static class SurfaceProbe
{
    private static readonly ConcurrentDictionary<string, (string[] DwarfKeys,
        IReadOnlyDictionary<string, int> CompilerErrorCounts, string Generated)> Baselines = new();

    public static (SurfaceEffect Effect, string Detail) Classify(SurfaceCase c, Endpoint endpoint)
    {
        // The CASE's key, not the element's: an option bag needs one shape per option, and reading the
        // element-wide key here would leave every per-property refinement inert while appearing to apply.
        var types = SurfaceFixtures.Get(c.ProbeKey);
        var source = EndpointSources.BuildAt(endpoint, c.Site, c.Rendered, types, c.MapperOptions);

        // The CAUSE, not just the fact. Four different things produce this verdict and two of them are
        // limitations of the endpoint templates rather than absences in the library, which "endpoint has no
        // such declaration site" flattened into one unreviewable sentence for 137 cells.
        if (source is null)
            return (SurfaceEffect.NoSuchSite,
                EndpointSources.SiteAbsenceReason(endpoint, c.Site, types) ?? "no declaration site");

        // The baseline carries the SAME ambient options. Otherwise the options themselves are the difference
        // between the two compilations and every such cell reads Honoured for the mapper's configuration
        // rather than for the element under test.
        var (baseDwarfKeys, baseCompilerErrorCounts, baseline) =
            Baseline(endpoint, c.ProbeKey, types, c.MapperOptions);

        // The C# COMPILER's verdict on the placement, not the generator's. GeneratorTestHarness.RunAll only
        // returns the diagnostics the GENERATOR itself reported (the out parameter of
        // RunGeneratorsAndUpdateCompilation), which never includes a CS-prefixed error for an illegal
        // AttributeUsage site — the generator is never invoked with a placement the compiler already rejected.
        // RunAndGetCompilationErrors reads outputCompilation.GetDiagnostics(), the FINAL compilation's verdict,
        // which does carry it. Confirmed empirically: see the task report.
        //
        // Only a CS error the CASE introduced counts. A fixture whose BASELINE already fails to compile — a
        // blocking DWARF error leaves the mapping method's partial declaration unimplemented, which is
        // CS8795 in the final compilation regardless of what the case under test did — would otherwise read
        // every case sharing that fixture as NotCompilable, no matter what the element actually did. Same
        // "only what changed" principle OptionProbe applies to DWARF diagnostics, applied here to CS.
        // Confirmed empirically against the "snake-case-member" fixture: see the task report.
        //
        // Subtracted by OCCURRENCE COUNT, not id membership: an id present in the baseline once but the
        // WITH-run twice is still a new failure the case introduced — keying on "is this id in the baseline
        // at all" would mask it. See SurfaceProbeTests for why the harness cannot currently construct that
        // scenario end-to-end (every class-model endpoint declares exactly one partial mapping method, so
        // CS8795 tops out at one occurrence regardless of the case), and NewOccurrences is verified
        // directly instead.
        //
        // Collected here but NOT decided on here: the verdict needs the generator's own diagnostics, which are
        // read below. See "THE DISCRIMINATION".
        // The diagnostic OBJECTS are kept, not just the counts, because the id alone cannot answer the
        // question below: the same CS id means opposite things depending on which file it is reported in.
        var withCompilerErrors = GeneratorTestHarness.RunAndGetCompilationErrors(source)
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS", StringComparison.Ordinal))
            .ToList();
        var withCompilerErrorCounts = withCompilerErrors
            .GroupBy(d => d.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var newCompilerErrorIds = NewOccurrences(withCompilerErrorCounts, baseCompilerErrorCounts);

        var (diagnostics, generated) = GeneratorTestHarness.RunAll(source);

        var addedDiagnostics = diagnostics
            .Where(d => !baseDwarfKeys.Contains(d.Id + ":" + d.Severity, StringComparer.Ordinal))
            // DWARF078 is the cascade signpost that accompanies ANY blocking error; including it appends a
            // meaningless suffix to every refused cell and tells the reader nothing about the element. Filtered
            // FIRST, before the blocking-error test below reads this list, so the signpost never pollutes a
            // rendered detail string. IsGeneratorRefusal ALSO excludes it internally (belt and braces), so this
            // filter's own removal or reordering cannot turn the signpost into the sole evidence of a refusal.
            .Where(d => !string.Equals(d.Id, "DWARF078", StringComparison.Ordinal))
            .ToList();

        var added = addedDiagnostics
            .Select(d => d.Severity == DiagnosticSeverity.Error ? d.Id : $"{d.Id} ({d.Severity})")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        // THE DISCRIMINATION. A new CS error used to end the classification right here, before the generator's
        // own diagnostics were ever read, and that conflated two opposite events under one verdict:
        //
        //   * the C# compiler rejected the PLACEMENT (an illegal AttributeUsage site, a duplicate member
        //     declaration, a constructor the sampled arguments do not fit) — the declaration told the truth
        //     and there is genuinely nothing here to judge; and
        //   * the GENERATOR refused, loudly and correctly. A blocking DWARF error suppresses emission, so the
        //     partial mapping method the endpoint template declares is left unimplemented, and the FINAL
        //     compilation reports CS8795 as a consequence. The element did the most visible thing it can do —
        //     it produced a diagnostic the caller can act on — and the cell was recorded as "the compiler
        //     rejected the placement" and then SKIPPED by the bidirectional claim check.
        //
        // The second is Refused; only the first is NotCompilable. The rule discriminates on both halves of the
        // causal story, because either half alone is wrong:
        //
        //   * every new CS id must be an ABSENT-EMISSION id (CS8795 and nothing else). A case that also
        //     introduces CS0111 or CS7036 has a real placement problem on top of whatever the generator said,
        //     and calling that Refused would hide the placement defect behind the refusal; and
        //   * a new BLOCKING DWARF diagnostic must exist. Without one, an unimplemented partial method is the
        //     generator declining to emit while saying nothing — which is not a refusal the caller can see,
        //     and must keep failing the count rather than being absorbed into the judged population.
        //
        // Both directions are pinned in SurfaceProbeTests: a CS8795-with-blocking-DWARF cell that must read
        // Refused, and a real placement rejection that must stay NotCompilable. A mistake here does not
        // produce a red cell — it produces a matrix that is confidently wrong.
        if (newCompilerErrorIds.Count > 0)
        {
            // THE THIRD THING, asked FIRST, because it is the only one of the three that can be decided
            // without looking at anything else: WHERE was the error reported? A compiler error inside a
            // .g.cs file is not the compiler rejecting the caller's placement and it is not the generator
            // refusing — it is the generator handing the consumer code that does not compile, in a file
            // they never wrote. Both of the discriminations below are about a CS error in the USER's source,
            // so neither is disturbed by asking this first; measured, every CS8795 the refusal rule reads is
            // reported against the user's own partial declaration (137 of 137 across the whole matrix).
            var emitted = EmittedCodeErrorIds(withCompilerErrors, newCompilerErrorIds);
            if (emitted.Count > 0)
                return (SurfaceEffect.EmittedInvalidCode,
                    string.Join(",", emitted) + " in generated code");

            if (!IsGeneratorRefusal(newCompilerErrorIds, addedDiagnostics))
                return (SurfaceEffect.NotCompilable, string.Join(",", newCompilerErrorIds));

            // The compiler error is REPORTED, not swallowed. A refusal that reached this verdict through
            // the discrimination is materially different from one the generator raised against source that
            // compiles either way — the caller gets a broken build as well as a diagnostic — and a reader
            // of the Refused population can now tell the two apart. It also makes the end-to-end pin test
            // in SurfaceProbeTests non-vacuous without a second compile: asserting the detail names CS8795
            // proves the cell actually travelled this branch, rather than arriving at Refused down the
            // ordinary added-diagnostics path below.
            return (SurfaceEffect.Refused,
                string.Join(",", added) + " (behind " + string.Join(",", newCompilerErrorIds) + ")");
        }

        if (added.Count > 0) return (SurfaceEffect.Refused, string.Join(",", added));
        if (!string.Equals(generated, baseline, StringComparison.Ordinal))
            return (SurfaceEffect.Honoured, "output differs");

        // A diagnostic the baseline raised and this run does not. Some directives exist ONLY to withdraw a
        // diagnostic — [MapIgnoreSource]'s whole effect is to silence the source-coverage suggestion — and
        // measuring only ADDED diagnostics and changed output reports those as doing nothing at all. The
        // element plainly acted: the build says something different because of it.
        var withoutKeys = diagnostics.Select(d => d.Id + ":" + d.Severity).ToHashSet(StringComparer.Ordinal);
        var withdrawn = baseDwarfKeys.Where(k => !withoutKeys.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (withdrawn.Count > 0)
            return (SurfaceEffect.Honoured, "withdrew " + string.Join(",", withdrawn));
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return (SurfaceEffect.UnhonouredButLoud, "unhonoured, but the build fails regardless");

        // Silence only means something if the question was asked. Checked LAST, on the silent path alone:
        // MapNullSkip<Src,Dst> is Honoured at CoLocatedHost against that endpoint's own flat pair despite its
        // fixture never being delivered, and a rule that fired before classification would erase that reading.
        //
        // "Did the fixture arrive" is a STRUCTURAL fact about the endpoint's template, not a string search.
        // This used to test whether the built source still contained the fixture text verbatim, which held
        // only while nothing was ever spliced INTO that text; the moment a slot site splices at a marker
        // inside a fixture, a perfectly-delivered fixture reads as never delivered and honest silence is
        // absorbed into Unasked. Equivalent today (measured: every ceiling unmoved by this change alone),
        // and no longer a trap for the next slot site.
        if (types is not null && !EndpointSources.DeliversFixture(endpoint))
            return (SurfaceEffect.Unasked,
                $"the '{c.ProbeKey}' fixture never reached this endpoint's source; the {endpoint} template "
                + "declares its own DTO pair");

        if (c.MapperOptions is not null && !source.Contains(c.MapperOptions, StringComparison.Ordinal))
            return (SurfaceEffect.Unasked,
                $"the ambient options this case needs ({c.MapperOptions}) never reached this endpoint's "
                + $"source; the {endpoint} template declares no mapper class to carry them");

        return (SurfaceEffect.Silent, "no diagnostic, identical output, compiles");
    }

    /// <summary>
    ///     Whether this cell poses the generator no question — either the fixture the case demands never
    ///     reached the endpoint, or the case is declared unmeasurable at the element and did indeed measure
    ///     nothing.
    ///     <para>
    ///         One predicate, consulted by the parity theory (which excuses such a cell) and by the count that
    ///         holds the total to a shrink-only ceiling. Two copies would drift, and a drifting copy is how an
    ///         excused cell stops being counted.
    ///     </para>
    /// </summary>
    public static bool PosesNoQuestion(SurfaceCase c, SurfaceEffect effect)
    {
        ArgumentNullException.ThrowIfNull(c);
        return effect is SurfaceEffect.Unasked
               || (c.Unmeasured is not null && effect is SurfaceEffect.Silent);
    }

    /// <summary>
    ///     The element-free compile of one fixture at one endpoint — what every case sharing that fixture is
    ///     measured AGAINST.
    ///     <para>
    ///         Internal rather than private so the fixture-baseline rule can be gated on it (B5) instead of
    ///         restated as a comment. Reading it through this method rather than rebuilding the compile is
    ///         what keeps the gate nearly free: the memo is process-wide, so the gate and the matrix share
    ///         one compile per (endpoint, fixture, options) however the runner orders them.
    ///     </para>
    /// </summary>
    internal static (string[] DwarfKeys, IReadOnlyDictionary<string, int> CompilerErrorCounts, string Generated)
        Baseline(Endpoint endpoint, string? probeKey, string? types, string? options)
    {
        var cacheKey = $"{endpoint}|{probeKey ?? "<flat>"}|{options ?? "<none>"}";
        return Baselines.GetOrAdd(cacheKey, _ =>
        {
            var baselineSource = EndpointSources.Build(endpoint, types: types, options: options ?? "");
            var (d, g) = GeneratorTestHarness.RunAll(baselineSource);
            var compilerErrorCounts = GeneratorTestHarness.RunAndGetCompilationErrors(baselineSource)
                .Where(x => x.Severity == DiagnosticSeverity.Error
                            && x.Id.StartsWith("CS", StringComparison.Ordinal))
                .GroupBy(x => x.Id, StringComparer.Ordinal)
                .ToDictionary(g2 => g2.Key, g2 => g2.Count(), StringComparer.Ordinal);
            return (d.Select(x => x.Id + ":" + x.Severity).Distinct(StringComparer.Ordinal).ToArray(),
                (IReadOnlyDictionary<string, int>)compilerErrorCounts, g);
        });
    }

    /// <summary>
    ///     Whether a case whose compilation gained CS errors is the GENERATOR refusing rather than the C#
    ///     compiler rejecting the caller's source. Both halves of the causal story must hold:
    ///     <paramref name="newCompilerErrorIds" /> must be entirely absent-emission ids, and
    ///     <paramref name="addedDiagnostics" /> must contain at least one Error.
    ///     <para>
    ///         A pure predicate, separated from <see cref="Classify" /> for the same reason
    ///         <see cref="NewOccurrences" /> is: two of its four input combinations cannot be constructed
    ///         by any cell the matrix currently contains, so they are only testable directly. No real cell
    ///         produces CS8795 without a blocking DWARF diagnostic (measured: all 86 that moved carried
    ///         one), and none mixes CS8795 with a genuine placement error. Both remain reachable in
    ///         principle — a second partial mapping method in an endpoint template would do it — and an
    ///         untested branch guarding 86 cells is precisely the kind of confidently-wrong instrument this
    ///         file exists to prevent.
    ///     </para>
    ///     <para>
    ///         <c>DWARF078</c> is excluded from <paramref name="addedDiagnostics" /> INSIDE this method, not
    ///         just by the caller. <see cref="Classify" /> also filters it before calling here, but that is
    ///         belt and braces rather than the only guard: the cascade signpost that accompanies every
    ///         blocking error must never be the sole evidence that one occurred, and a caller-side-only
    ///         filter is an invariant a later refactor could move past this call without anything going red.
    ///         Filtering here too means the predicate holds that invariant itself.
    ///     </para>
    /// </summary>
    internal static bool IsGeneratorRefusal(IReadOnlyList<string> newCompilerErrorIds,
        IReadOnlyList<Diagnostic> addedDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(newCompilerErrorIds);
        ArgumentNullException.ThrowIfNull(addedDiagnostics);

        return addedDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error
                                          && !string.Equals(d.Id, "DWARF078", StringComparison.Ordinal))
               && newCompilerErrorIds.Count > 0
               && newCompilerErrorIds.All(id => AbsentEmissionErrorIds.Contains(id, StringComparer.Ordinal));
    }

    /// <summary>
    ///     Of the CS ids this case introduced, those with at least one occurrence reported against code the
    ///     GENERATOR wrote — in ordinal id order, empty if none. Non-empty means
    ///     <see cref="SurfaceEffect.EmittedInvalidCode" />.
    ///     <para>
    ///         A pure function over the diagnostics, separated from <see cref="Classify" /> for the reason
    ///         <see cref="IsGeneratorRefusal" /> and <see cref="NewOccurrences" /> are: the population it
    ///         guards is one nobody wants to have, so it must stay testable when that population is empty.
    ///         Both historical instances (<c>CS1912</c> from a duplicate <c>[FlattenGraph]</c> destination,
    ///         <c>CS0037</c> from <c>[MapTo]</c> on a struct) are fixed, and the next one will arrive in some
    ///         id nobody has thought of — so the rule is deliberately about the LOCATION and not about a list
    ///         of ids. An allowlist of "ids that mean the generator broke" would have had to be extended by
    ///         whoever hit the new one, which is precisely the person who has not noticed yet.
    ///     </para>
    ///     <para>
    ///         "At least one occurrence", not "every occurrence": one broken emission usually also lights up
    ///         the caller's own source (the ambiguity cascade a duplicate generated method produces is
    ///         reported at both), and requiring purity there would let the generator's defect hide behind its
    ///         own consequences.
    ///     </para>
    ///     <para>
    ///         The match is by ID, not by diagnostic identity, and that is weaker than it looks in exactly one
    ///         contrived shape: a baseline whose GENERATED code already carries some id, plus a case that
    ///         introduces the same id in the CALLER's source, would be attributed to the generator. It needs a
    ///         baseline that is already broken in generated code — which no fixture in this matrix has, since
    ///         a baseline that does not compile is caught by the <see cref="SurfaceEffect.UnhonouredButLoud" />
    ///         count long before it gets here — so it is stated rather than guarded. Guarding it means
    ///         subtracting per-occurrence with locations rather than per-id, which is a bigger change than the
    ///         hazard warrants today.
    ///     </para>
    ///     <para>
    ///         The location test itself is <see cref="GeneratorTestHarness.IsInGeneratedCode" />, which
    ///         existed for <c>GeneratedCodeWarnings</c> before this verdict did. It is CALLED, not copied:
    ///         two statements of "what counts as generated" is how the warning gate and this verdict would
    ///         come to disagree.
    ///     </para>
    /// </summary>
    internal static IReadOnlyList<string> EmittedCodeErrorIds(
        IReadOnlyList<Diagnostic> compilerErrors, IReadOnlyList<string> newCompilerErrorIds)
    {
        ArgumentNullException.ThrowIfNull(compilerErrors);
        ArgumentNullException.ThrowIfNull(newCompilerErrorIds);

        return compilerErrors
            .Where(GeneratorTestHarness.IsInGeneratedCode)
            .Select(d => d.Id)
            .Where(id => newCompilerErrorIds.Contains(id, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     The CS error ids that mean "the mapping method has no implementation", which is what a generator
    ///     that declined to emit leaves behind — as opposed to an id that means the caller's own source is
    ///     wrong. Only these are eligible to be re-read as a refusal by <see cref="Classify" />.
    ///     <para>
    ///         Deliberately a list of exactly one and deliberately not a prefix test. Every class-model
    ///         endpoint in <see cref="EndpointSources" /> declares its mapping method <c>partial</c>, so
    ///         suppressed emission surfaces as <c>CS8795</c> and nothing else; a second id appearing here later
    ///         would be a new endpoint template shape, which is a change someone should have to make on
    ///         purpose. Everything outside this set — <c>CS0111</c> (the case duplicates a declaration),
    ///         <c>CS7036</c> (the sampled arguments do not fit a constructor), <c>CS1912</c> (invalid C# in
    ///         GENERATED code, which is worse than any refusal and must never be absorbed into one) — is the
    ///         compiler rejecting something real, and stays <see cref="SurfaceEffect.NotCompilable" />.
    ///     </para>
    /// </summary>
    private static readonly string[] AbsentEmissionErrorIds = ["CS8795"];

    /// <summary>
    ///     Every CS-prefixed diagnostic id whose occurrence count in <paramref name="withCounts" /> exceeds its
    ///     count in <paramref name="baselineCounts" /> (an id absent from the baseline counts as zero), in
    ///     ordinal id order — empty if none. A pure counting function, deliberately separated from
    ///     <see cref="GeneratorTestHarness" /> so the over-subtraction direction (a case that legitimately
    ///     re-triggers a CS id the baseline already carries once) is testable without needing an end-to-end
    ///     compile that can actually produce two occurrences of the same id — see <c>SurfaceProbeTests</c> for
    ///     why that scenario cannot currently be constructed through the real harness.
    ///     <para>
    ///         ALL of them, not just the first: <see cref="Classify" />'s refusal rule is a universal
    ///         quantifier over the ids the case introduced, and a function that returns only the first would
    ///         let a case whose CS8795 is accompanied by a genuine placement error read as a clean refusal.
    ///     </para>
    /// </summary>
    internal static IReadOnlyList<string> NewOccurrences(IReadOnlyDictionary<string, int> withCounts,
        IReadOnlyDictionary<string, int> baselineCounts)
    {
        var newIds = new List<string>();
        foreach (var id in withCounts.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var baseCount = baselineCounts.TryGetValue(id, out var n) ? n : 0;
            if (withCounts[id] > baseCount) newIds.Add(id);
        }

        return newIds;
    }
}
