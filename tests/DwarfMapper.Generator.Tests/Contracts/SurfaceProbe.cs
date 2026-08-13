// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>What a surface element DOES at one endpoint, observed rather than declared.</summary>
internal enum SurfaceEffect
{
    /// <summary>The element changed the emitted output.</summary>
    Honoured,

    /// <summary>The element produced a diagnostic — a refusal the caller can see.</summary>
    Refused,

    /// <summary>Accepted, changed nothing observable, compiled. The dangerous one.</summary>
    Silent,

    /// <summary>
    ///     Changed nothing, but the build fails at this endpoint anyway. Distinguished from
    ///     <see cref="Silent" /> deliberately: silence only ships wrong data when the code COMPILES.
    /// </summary>
    UnhonouredButLoud,

    /// <summary>
    ///     The site is illegal per <c>AttributeUsage</c> and the compiler rejects it — the declaration is
    ///     telling the truth.
    /// </summary>
    NotCompilable,

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
        // CS8795 tops out at one occurrence regardless of the case), and FirstNewOccurrence is verified
        // directly instead.
        var withCompilerErrorCounts = GeneratorTestHarness.RunAndGetCompilationErrors(source)
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS", StringComparison.Ordinal))
            .GroupBy(d => d.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var newCompilerErrorId = FirstNewOccurrence(withCompilerErrorCounts, baseCompilerErrorCounts);
        if (newCompilerErrorId is not null)
            return (SurfaceEffect.NotCompilable, newCompilerErrorId);

        var (diagnostics, generated) = GeneratorTestHarness.RunAll(source);

        var added = diagnostics
            .Where(d => !baseDwarfKeys.Contains(d.Id + ":" + d.Severity, StringComparer.Ordinal))
            // DWARF078 is the cascade signpost that accompanies ANY blocking error; including it appends a
            // meaningless suffix to every refused cell and tells the reader nothing about the element.
            .Where(d => !string.Equals(d.Id, "DWARF078", StringComparison.Ordinal))
            .Select(d => d.Severity == DiagnosticSeverity.Error ? d.Id : $"{d.Id} ({d.Severity})")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

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
        if (types is not null && !source.Contains(types, StringComparison.Ordinal))
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

    private static (string[] DwarfKeys, IReadOnlyDictionary<string, int> CompilerErrorCounts, string Generated)
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
    ///     The first CS-prefixed diagnostic id whose occurrence count in <paramref name="withCounts" />
    ///     exceeds its count in <paramref name="baselineCounts" /> (an id absent from the baseline counts as
    ///     zero) — or <c>null</c> if none. A pure counting function, deliberately separated from
    ///     <see cref="GeneratorTestHarness" /> so the over-subtraction direction (a case that legitimately
    ///     re-triggers a CS id the baseline already carries once) is testable without needing an end-to-end
    ///     compile that can actually produce two occurrences of the same id — see <c>SurfaceProbeTests</c> for
    ///     why that scenario cannot currently be constructed through the real harness.
    /// </summary>
    internal static string? FirstNewOccurrence(IReadOnlyDictionary<string, int> withCounts,
        IReadOnlyDictionary<string, int> baselineCounts)
    {
        foreach (var id in withCounts.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var baseCount = baselineCounts.TryGetValue(id, out var n) ? n : 0;
            if (withCounts[id] > baseCount) return id;
        }

        return null;
    }
}
