// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>
///     Endpoint divergences that are known, recorded, and not yet fixed.
///     <para>
///         A ratchet, not an allowance. Any silent cell NOT named here fails the build, and any entry here
///         that stops being silent must be removed — so the list can only shrink, and it cannot quietly
///         re-permit a divergence that comes back.
///     </para>
///     <para>
///         Kept in one place because two things consume it: the parity property, which would otherwise fail
///         the build, and the generated support matrix, which renders these cells as <c>SILENT</c> so a reader
///         sees the gap rather than a blank. Two copies would drift, and a stale copy is how a fixed gap
///         silently keeps its exemption.
///     </para>
/// </summary>
public static class OptionGaps
{
    public static readonly Dictionary<string, string> KnownSilent = new(StringComparer.Ordinal)
    {
        ["RequiredMapping"] =
            "source-side completeness (DWARF039) fires at CreateMap only. Setting RequiredMapping = Both to "
            + "catch a too-wide input DTO does nothing on Update/Project/MapSpan/MapStream. Lower severity "
            + "than the trust-boundary gaps — it fails to WARN rather than producing wrong data — but the "
            + "option is still accepted and discarded, which is the same shape of bug",

        ["ReferenceHandling"] =
            "the REVERSE asymmetry: silent at CreateMap and UpdateInto while acting at Projection (DWARF028) "
            + "and both span endpoints. Identity preservation needs genuinely shared references to observe, "
            + "and the probe fixture has none, so the CreateMap cells cannot yet be called a divergence "
            + "rather than an untriggered probe. Needs a shared-reference fixture before it can be judged"
    };
}
