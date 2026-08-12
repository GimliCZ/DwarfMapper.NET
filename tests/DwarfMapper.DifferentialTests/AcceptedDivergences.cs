// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.DifferentialTests;

/// <summary>
///     Where DwarfMapper and an oracle legitimately disagree, and why.
/// </summary>
/// <remarks>
///     <para>
///         This list — not the passing tests — is the deliverable. Three mappers designed by different people
///         will differ on documented axes, and each difference is either a defect or a decision. Writing the
///         decision down turns <c>docs/COMPARISON.md</c> from prose into something executable: a claim about
///         how DwarfMapper differs from Mapperly is now a row that fails if it stops being true.
///     </para>
///     <para>
///         Every entry must name the axis and the reason. An entry with no reason would make the harness
///         green while proving nothing, which is exactly what a differential oracle is for avoiding.
///     </para>
/// </remarks>
internal sealed record AcceptedDivergence(string Shape, string Oracle, string PathPrefix, string Why);

internal static class AcceptedDivergences
{
    public static readonly AcceptedDivergence[] All =
    [
        new("EnumToString", Oracles.Mapperly, "<root>.Status",
            "DwarfMapper reads [Description]/[EnumMember] as the string form (EnumStringSource.Attribute, the "
            + "default), Mapperly uses the member identifier. Both are deliberate: DwarfMapper's default lets "
            + "InProgress serialize as \"in_progress\" with no converter, and DWARF083 reports the case where "
            + "that is not intended. EnumStringSource.Identifier is the one-line switch to Mapperly's "
            + "behaviour — asserted by the AGREE half of this same shape."),

        new("EnumToString", Oracles.AutoMapper, "<root>.Status",
            "Same axis. AutoMapper 14 uses .ToString(), i.e. the identifier — which is precisely why a "
            + "migration off it walks into this: see docs/howto/migrate-from-automapper.md row 3.")
    ];

    public static bool IsAccepted(string shape, string oracle, string path) =>
        All.Any(a => string.Equals(a.Shape, shape, StringComparison.Ordinal)
                     && string.Equals(a.Oracle, oracle, StringComparison.Ordinal)
                     && path.StartsWith(a.PathPrefix, StringComparison.Ordinal));
}

internal static class Oracles
{
    public const string Mapperly = "Mapperly";
    public const string AutoMapper = "AutoMapper";
}
