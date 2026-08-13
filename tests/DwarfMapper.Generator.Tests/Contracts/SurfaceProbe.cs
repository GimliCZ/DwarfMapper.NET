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
    NoSuchSite
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
    private static readonly ConcurrentDictionary<string, (string[] DwarfKeys, string[] CompilerErrorIds,
        string Generated)> Baselines = new();

    public static (SurfaceEffect Effect, string Detail) Classify(SurfaceCase c, Endpoint endpoint)
    {
        var types = SurfaceFixtures.Get(c.Element.ProbeKey);
        var source = EndpointSources.BuildAt(endpoint, c.Site, c.Rendered, types);
        if (source is null) return (SurfaceEffect.NoSuchSite, "endpoint has no such declaration site");

        var (baseDwarfKeys, baseCompilerErrorIds, baseline) = Baseline(endpoint, c.Element.ProbeKey, types);

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
        var compilationErrors = GeneratorTestHarness.RunAndGetCompilationErrors(source);
        var newCompilerError = compilationErrors.FirstOrDefault(d =>
            d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS", StringComparison.Ordinal)
            && !baseCompilerErrorIds.Contains(d.Id, StringComparer.Ordinal));
        if (newCompilerError is not null)
            return (SurfaceEffect.NotCompilable, newCompilerError.Id);

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
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return (SurfaceEffect.UnhonouredButLoud, "unhonoured, but the build fails regardless");

        return (SurfaceEffect.Silent, "no diagnostic, identical output, compiles");
    }

    private static (string[] DwarfKeys, string[] CompilerErrorIds, string Generated) Baseline(Endpoint endpoint,
        string? probeKey, string? types)
    {
        var cacheKey = $"{endpoint}|{probeKey ?? "<flat>"}";
        return Baselines.GetOrAdd(cacheKey, _ =>
        {
            var baselineSource = EndpointSources.Build(endpoint, types: types);
            var (d, g) = GeneratorTestHarness.RunAll(baselineSource);
            var compilerErrorIds = GeneratorTestHarness.RunAndGetCompilationErrors(baselineSource)
                .Where(x => x.Id.StartsWith("CS", StringComparison.Ordinal))
                .Select(x => x.Id)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return (d.Select(x => x.Id + ":" + x.Severity).Distinct(StringComparer.Ordinal).ToArray(),
                compilerErrorIds, g);
        });
    }
}
