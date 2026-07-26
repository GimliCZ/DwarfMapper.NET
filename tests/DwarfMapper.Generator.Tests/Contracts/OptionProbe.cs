// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Testing;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>
///     Observes what a class-level <c>[DwarfMapper]</c> option actually DOES at a given endpoint, by compiling
///     the same source with and without it.
///     <para>
///         Shared by the parity property and the generated option-support table on purpose. If the table were
///         built from hand-written declarations while the property measured real behaviour, the document would
///         describe the intent and the test would describe the code, and the two would drift apart silently —
///         which is the decay the generated docs exist to prevent.
///     </para>
/// </summary>
public static class OptionProbe
{
    public static (OptionEffect Effect, string Detail) Classify(
        Endpoint endpoint, string nonDefault, string? types)
    {
        var withOption = EndpointSources.Build(endpoint, options: nonDefault, types: types);
        var without = EndpointSources.Build(endpoint, types: types);

        var (diagnostics, generated) = GeneratorTestHarness.RunAll(withOption);
        var (baseDiagnostics, baseline) = GeneratorTestHarness.RunAll(without);

        // Only diagnostics the OPTION introduced count. A fixture that already errors without the option
        // would otherwise read as "refused" at every endpoint and mask a real silence — this is exactly how
        // an earlier NullStrategy cell passed for the wrong reason.
        // Keyed by (id, severity), not id alone. ImplicitConversions = false does not introduce a NEW
        // diagnostic — it escalates DWARF038 from Warning to Error. Comparing ids only, that reads as "no
        // diagnostic added", and an option that turns a warning into a build failure was being classified as
        // having no observable effect.
        var baseKeys = baseDiagnostics
            .Select(d => d.Id + ":" + d.Severity).ToHashSet(StringComparer.Ordinal);
        var added = diagnostics
            .Where(d => !baseKeys.Contains(d.Id + ":" + d.Severity))
            .Select(d => d.Severity == DiagnosticSeverity.Error ? d.Id : $"{d.Id} ({d.Severity})")
            .Distinct()
            .OrderBy(id => id, StringComparer.Ordinal).ToList();

        if (added.Count > 0) return (OptionEffect.Refused, string.Join(",", added));
        if (!string.Equals(generated, baseline, StringComparison.Ordinal))
            return (OptionEffect.Honoured, "output differs");

        // Nothing changed — but did the developer at least get stopped? NameConvention at the span endpoint
        // is the case that forced this distinction: the convention is not applied, yet the unmatched member
        // raises DWARF001 either way, so no wrong data can ship. Calling that the same failure as a silently
        // discarded trust boundary would overstate it.
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return (OptionEffect.UnhonouredButLoud, "unhonoured, but the build fails regardless");

        return (OptionEffect.Silent, "no diagnostic, identical output, compiles");
    }
}
