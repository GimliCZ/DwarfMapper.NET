// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;
using DwarfMapper.Generator.Diagnostics;

namespace DwarfMapper.NegativeCases;

/// <summary>
///     A forward-looking ratchet: from here on, a new diagnostic arrives with a case file or it does not
///     arrive.
/// </summary>
/// <remarks>
///     <para>
///         Demanding a case for all 78 existing ids up front would have been a mechanical translation of
///         tests that already exist, and the churn would have bought nothing — those diagnostics are asserted,
///         just not here. What was actually missing is a rule for the NEXT one. So the ids that predate this
///         project are listed below, and the ratchet only says: a descriptor that is neither in that list nor
///         backed by a case file fails the build.
///     </para>
///     <para>
///         Adding an id to <see cref="PredatesThisProject" /> is therefore how you opt out — deliberately, in a
///         diff a reviewer will see and ask about. That is the same device the doc-fence scan uses, and the
///         reason its allowlist is empty today: an escape hatch that is visible gets closed.
///     </para>
///     <para>
///         The list is also allowed to SHRINK, and should. Every entry removed is a diagnostic that gained an
///         executable statement of the shape that triggers it and the message a consumer will read.
///     </para>
/// </remarks>
public class DiagnosticCoverageRatchetTests
{
    /// <summary>
    ///     Every DWARF id that existed when this project was created (2026-08-12), and so is exempt from
    ///     needing a case file. Remove entries as cases are written; never add without a reason in the commit.
    /// </summary>
    private static readonly string[] PredatesThisProject =
    [
        "DWARF002", "DWARF003", "DWARF005", "DWARF008",
        "DWARF009", "DWARF010", "DWARF011", "DWARF012",
        "DWARF013", "DWARF015", "DWARF016", "DWARF017",
        "DWARF018", "DWARF020", "DWARF021", "DWARF022",
        "DWARF023", "DWARF024", "DWARF025", "DWARF026",
        "DWARF027", "DWARF028", "DWARF030", "DWARF031",
        "DWARF032", "DWARF033", "DWARF034", "DWARF035",
        "DWARF036", "DWARF037", "DWARF038", "DWARF039",
        "DWARF040", "DWARF041", "DWARF042", "DWARF043",
        "DWARF044", "DWARF045", "DWARF046", "DWARF047",
        "DWARF048", "DWARF049", "DWARF050", "DWARF051",
        "DWARF052", "DWARF053", "DWARF054", "DWARF055",
        "DWARF056", "DWARF057", "DWARF058", "DWARF059",
        "DWARF060", "DWARF061", "DWARF062", "DWARF063",
        "DWARF064", "DWARF065", "DWARF066", "DWARF067",
        "DWARF068", "DWARF069", "DWARF070", "DWARF071",
        "DWARF073", "DWARF074", "DWARF075", "DWARF076",
        "DWARF077"
    ];

    private static IEnumerable<string> AllDescriptorIds() =>
        typeof(DiagnosticDescriptors)
            .GetFields()
            .Select(f => f.GetValue(null))
            .OfType<DiagnosticDescriptor>()
            .Select(d => d.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal);

    [Fact]
    public void Every_diagnostic_has_a_case_file_or_predates_this_project()
    {
        var unaccounted = AllDescriptorIds()
            .Where(id => !NegativeCase.CoveredIds.Contains(id, StringComparer.Ordinal)
                         && !PredatesThisProject.Contains(id, StringComparer.Ordinal))
            .ToList();

        Assert.True(unaccounted.Count == 0,
            "New diagnostic(s) with no negative case:\n  " + string.Join("\n  ", unaccounted)
            + "\n\nAdd Cases/<id>_<ShortName>.cs declaring the exact shape that triggers it and the part of "
            + "the message a consumer must be able to read. A diagnostic whose text is unasserted is a "
            + "diagnostic that can quietly stop being useful — which is how three Round-18 tasks came to "
            + "exist.\n\nIf it genuinely cannot be reached from a single compilation unit (DWARF061 and the "
            + "DWARFR family need several assemblies), add it to PredatesThisProject with that reason in the "
            + "commit message.");
    }

    [Fact]
    public void The_exemption_list_does_not_name_a_retired_diagnostic()
    {
        // A list that outlives its entries stops being a statement about coverage and becomes decoration.
        var all = AllDescriptorIds().ToList();

        var stale = PredatesThisProject.Where(id => !all.Contains(id, StringComparer.Ordinal)).ToList();

        Assert.True(stale.Count == 0,
            "PredatesThisProject names diagnostic(s) that no longer exist:\n  " + string.Join("\n  ", stale)
            + "\n\nDelete them. The list is a debt register, and a debt to nobody is not a debt.");
    }

    [Fact]
    public void The_exemption_list_does_not_name_a_diagnostic_that_now_has_a_case()
    {
        // The ratchet must actually tighten. Leaving an id in both places would let a case file be deleted
        // later with nothing to notice.
        var both = PredatesThisProject
            .Where(id => NegativeCase.CoveredIds.Contains(id, StringComparer.Ordinal))
            .ToList();

        Assert.True(both.Count == 0,
            "These diagnostics have a case file AND an exemption:\n  " + string.Join("\n  ", both)
            + "\n\nRemove them from PredatesThisProject — that is the ratchet tightening, and it is the only "
            + "way the list ever gets shorter.");
    }
}
