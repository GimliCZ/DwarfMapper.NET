// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Asserts the MESSAGE, not only the id — for every diagnostic whose value is the message.
/// </summary>
/// <remarks>
///     <para>
///         Round 18 produced three tasks that existed purely because a message was unhelpful or wrong:
///         <c>DWARF076</c> documented a suppression that did not work, <c>CS9035</c> said nothing about the
///         mapper that caused it, and <c>DwarfMapMissingException</c> named an unpronounceable compiler-
///         generated iterator type. In each case the id was correct and the message was the defect.
///     </para>
///     <para>
///         Individual diagnostic suites assert their own text. This is the layer above: a single table saying
///         which diagnostics carry an obligation to name a REMEDY, checked against the descriptors so a
///         reworded message cannot quietly drop it. It is deliberately about the contract, not the wording —
///         each row names a substring that must survive any reasonable rewrite.
///     </para>
/// </remarks>
public class DiagnosticMessageContractTests
{
    /// <summary>
    ///     Diagnostics whose message must name a way OUT, and the token that proves it does.
    /// </summary>
    /// <remarks>
    ///     Every entry is a diagnostic a consumer meets while their build is red. "Here is what is wrong" is
    ///     half a message; the other half is what to write instead, and that half is what these pin.
    /// </remarks>
    private static readonly (string Id, string MustMention, string Why)[] RemedyContracts =
    [
        ("DWARF001", "[MapIgnore(", "the completeness gate must name the escape hatch, or it reads as a wall"),
        ("DWARF007", "[MapIgnore(", "a read-only member the source would have filled needs an explicit decision"),
        ("DWARF014", "signature", "a Use= that does not resolve is almost always a signature mismatch"),
        ("DWARF072", "[MapProperty]", "explicit-only refuses a member; the reader needs the way to allow it"),
        ("DWARF078", "CS8795", "its entire job is connecting the cascade to its cause"),
        ("DWARF079", "[MapValue", "the required+ignore remedy three separate migrations each reinvented"),
        ("DWARF080", "[MapProperty]", "prefer constructor-parameter binding to a factory — the generalisable fix"),
        ("DWARF082", "public", "the shape requirement is the fix"),
        ("DWARF083", "display", "the whole point is that the annotation was probably meant for display")
    ];

    private static DiagnosticDescriptor Descriptor(string id)
    {
        var field = typeof(Diagnostics.DiagnosticDescriptors)
            .GetFields()
            .Select(f => f.GetValue(null))
            .OfType<DiagnosticDescriptor>()
            .FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal));

        Assert.True(field is not null, $"No descriptor with id '{id}'. Update RemedyContracts if it was retired.");
        return field!;
    }

    [Theory]
    [MemberData(nameof(RemedyContractRows))]
    public void A_diagnostic_that_promises_a_remedy_still_names_it(string id, string mustMention, string why)
    {
        var message = Descriptor(id).MessageFormat.ToString(CultureInfo.InvariantCulture);

        Assert.True(message.Contains(mustMention, StringComparison.Ordinal),
            $"{id}'s message no longer mentions '{mustMention}'.\n\nWhy that matters: {why}.\n\n"
            + $"Current message:\n  {message}\n\n"
            + "Reword freely, but keep the remedy. A diagnostic that says only what is wrong leaves the "
            + "reader exactly where they started — which is how three Round-18 tasks came to exist.");
    }

    public static TheoryData<string, string, string> RemedyContractRows()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (id, mustMention, why) in RemedyContracts) data.Add(id, mustMention, why);
        return data;
    }

    [Fact]
    public void Every_contract_row_names_a_diagnostic_that_still_exists()
    {
        // Cheap, but it is what keeps the table from rotting into a list of retired ids that vacuously pass.
        foreach (var (id, _, _) in RemedyContracts) Descriptor(id);
    }
}
