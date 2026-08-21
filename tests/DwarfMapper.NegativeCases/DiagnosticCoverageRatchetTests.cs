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
///         <see cref="PredatesThisProject" /> used to be described here as the visible opt-out — add an id,
///         a reviewer sees the diff and asks. Visibility was the only enforcement, which made "holds by
///         construction" a claim about reviewer diligence (B11). It is now a counted, bounded population:
///         <see cref="The_exemption_list_is_an_exactly_pinned_bounded_population" /> pins its size exactly
///         and refuses any id newer than the project's creation date, so the hatch is closed rather than
///         merely lit.
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
    ///     needing a case file. Remove entries as cases are written; never add.
    ///     <para>
    ///         "Never add" is enforced, not asked for (B11). The remarks above used to call this list a
    ///         visible escape hatch, and visibility was the whole enforcement: nothing blocked an addition, so
    ///         the ratchet's "holds by construction" claim rested on a reviewer noticing a diff. Two guards
    ///         close that. The count is pinned exactly at <see cref="PredatesThisProjectPin" />, so any growth
    ///         is a red build; and because an exact pin alone is swap-blind — retire one entry and smuggle a
    ///         new id in the same commit, count unchanged — every entry must also fall at or before
    ///         <see cref="ExemptionIdHorizon" />, the highest id that existed on the project's creation date.
    ///         A diagnostic minted after that date does not predate the project by definition, so there is no
    ///         honest edit this bound forbids.
    ///     </para>
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
        // DWARF038 removed 2026-08-16: [DwarfMapper(ImplicitConversions = false)] has no observable effect
        // OTHER than escalating this suggestion to a refusal, so the option's own proof obligation is this
        // case. The ratchet tightening, again as intended.
        "DWARF036", "DWARF037", "DWARF039",
        "DWARF040", "DWARF041", "DWARF042", "DWARF043",
        "DWARF044", "DWARF045", "DWARF046", "DWARF047",
        "DWARF048", "DWARF049", "DWARF050", "DWARF051",
        "DWARF052", "DWARF053", "DWARF054", "DWARF055",
        // DWARF058 removed 2026-08-12: DWARF081's case declares it, because two mappers from one source type
        // provoke it inherently. The ratchet tightening, exactly as intended.
        "DWARF056", "DWARF057", "DWARF059",
        // DWARF061 removed 2026-08-16: the validation root's whole observable effect is this refusal, so it
        // is where [assembly: DwarfMapperValidationRoot] is proved to do anything at all.
        "DWARF060", "DWARF062", "DWARF063",
        "DWARF064", "DWARF065", "DWARF066", "DWARF067",
        "DWARF068", "DWARF069", "DWARF070", "DWARF071",
        "DWARF073", "DWARF074", "DWARF075", "DWARF076",
        "DWARF077"
    ];

    /// <summary>
    ///     The exact size of <see cref="PredatesThisProject" />. Measured 2026-08-22 (66 entries: the 78 ids
    ///     of 2026-08-12 minus the twelve retired by gained cases and deletions since). Shrink-only: lower it
    ///     in the same commit as the entry it loses, and never raise it — a raise would be the hatch this pin
    ///     exists to close.
    /// </summary>
    private const int PredatesThisProjectPin = 66;

    /// <summary>
    ///     The highest DWARF id that existed on 2026-08-12, compared ordinally (the ids are fixed-width, and
    ///     the <c>DWARFR</c> family sorts after every <c>DWARFnnn</c>, so it is excluded too — correctly, as
    ///     the whole registry family postdates the project). This constant never moves: it is a statement of
    ///     history, not a ceiling someone re-measures.
    /// </summary>
    private const string ExemptionIdHorizon = "DWARF077";

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
            + "exist.\n\nPredatesThisProject is NOT an option: it is exactly pinned and bounded at "
            + "DWARF077, because a diagnostic minted now does not predate the project. If the shape "
            + "genuinely cannot be reached from a single compilation unit (DWARF061 and the DWARFR family "
            + "need several assemblies), that is a new exemption CATEGORY — design it with its own counted "
            + "store and its own stated obligation, the way every other excuse class carries one.");
    }

    /// <summary>
    ///     REG-05: no id ships with an id-only assertion.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         All 81 live ids are accounted for today — every one has a case file or an entry in
    ///         <see cref="PredatesThisProject" /> — but that accounting says only that the diagnostic FIRES.
    ///         The remedy prose, the half that tells a reader with a red build what to write instead, is
    ///         pinned by <c>EXPECT-MESSAGE</c>, and nothing required one: <c>A_case_gets_the_message_it_declares</c>
    ///         returns early when a case declares none, so an id-only case is green and silent.
    ///     </para>
    ///     <para>
    ///         Composed with the ratchet above, this closes the class rather than the instance. A new
    ///         diagnostic must have a case file (or a visible exemption); a case file must pin wording. The
    ///         property therefore holds by construction for every id that ever arrives, instead of holding by
    ///         the diligence of whoever wrote the last one. It was green when written — <c>DWARF086</c> is the
    ///         first id it had the chance to fail on.
    ///     </para>
    /// </remarks>
    [Fact]
    public void Every_covered_diagnostic_pins_its_remedy_wording()
    {
        // Without this, a Load() that returned cases whose headers failed to parse would leave both sets
        // empty and the Except() below vacuously satisfied.
        Assert.NotEmpty(NegativeCase.CoveredIds);

        var unpinned = NegativeCase.CoveredIds
            .Where(id => !NegativeCase.WordingPinnedIds.Contains(id, StringComparer.Ordinal))
            .ToList();

        Assert.True(unpinned.Count == 0,
            "Diagnostic(s) with a case file that pins the id but not the wording:\n  "
            + string.Join("\n  ", unpinned)
            + "\n\nAdd an `// EXPECT-MESSAGE <id>: <substring>` line naming the part of the message a reader "
            + "must be able to act on — the attribute to write instead, the option to set, the shape to "
            + "change. Pinning the id alone lets the remedy prose rot into something that no longer tells "
            + "anyone what to do, with every test still green: exactly the defect three Round-18 tasks "
            + "existed to repair.");
    }

    /// <summary>
    ///     B11: the hatch the ratchet's remarks claimed was "closed by construction" is now closed by a
    ///     measurement. Two guards, because each alone leaks: the exact pin makes any growth red but is
    ///     swap-blind (retire one entry, smuggle one in, count unchanged), and the horizon bound makes any
    ///     post-creation id red but says nothing about the count. Together, an entry can only ever LEAVE.
    /// </summary>
    [Fact]
    public void The_exemption_list_is_an_exactly_pinned_bounded_population()
    {
        Assert.True(PredatesThisProject.Length <= PredatesThisProjectPin,
            $"PredatesThisProject holds {PredatesThisProject.Length} entries, above the pinned "
            + $"{PredatesThisProjectPin}. This population may only shrink: a new diagnostic arrives with a "
            + "case file or it does not arrive, and an id created after 2026-08-12 cannot 'predate this "
            + "project' whatever the commit message says.");

        Assert.True(PredatesThisProject.Length >= PredatesThisProjectPin,
            $"PredatesThisProject holds {PredatesThisProject.Length} entries, under the pinned "
            + $"{PredatesThisProjectPin}. An entry was retired — good — but the pin must move in the same "
            + "commit, or the room it freed silently funds a swap-in the exact pin exists to prevent.");

        var beyondHorizon = PredatesThisProject
            .Where(id => string.CompareOrdinal(id, ExemptionIdHorizon) > 0)
            .ToList();
        Assert.True(beyondHorizon.Count == 0,
            "PredatesThisProject names id(s) newer than the project's creation date:\n  "
            + string.Join("\n  ", beyondHorizon)
            + $"\n\nEvery id after {ExemptionIdHorizon} was minted after 2026-08-12 and therefore does not "
            + "predate this project. It needs a case file — there is no list to add it to.");

        var duplicated = PredatesThisProject
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(duplicated.Count == 0,
            "PredatesThisProject lists id(s) twice: " + string.Join(", ", duplicated)
            + ". A duplicate makes the pinned count claim coverage of an id the list does not actually "
            + "hold room for.");
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
