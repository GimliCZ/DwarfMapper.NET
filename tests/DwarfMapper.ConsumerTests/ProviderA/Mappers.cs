// SPDX-License-Identifier: GPL-2.0-only

using ConsumerTests.Contracts;
using DwarfMapper;

namespace ConsumerTests.ProviderA;

/// <summary>
///     The main provider. Public, so its maps self-register into the process-wide registry at module load —
///     which is how <c>Host</c> resolves them without referencing this assembly.
/// </summary>
[DwarfMapper]
[GenerateMap<Customer, CustomerDto>]
[GenerateMap<Address, AddressDto>]
public partial class CustomerMappers
{
}

/// <summary>
///     Polymorphic elements. A collection of <c>Command</c> containing an <c>AliasCommand</c> must not lose
///     <c>Alias</c> — the divergence Round 18 found in a live API response, where the collection loop bound
///     the base pair at COMPILE time while the previous mapper dispatched on the runtime type.
/// </summary>
[DwarfMapper]
public partial class CommandMappers
{
    [MapIgnore(nameof(CommandDto.Alias))]   // a plain Command has no alias; the hook fills it when there is one
    public partial CommandDto ToDto(Command source);

    /// <summary>
    ///     The runtime type test, done explicitly. <c>[AfterMap]</c> receives the whole source — which is what
    ///     is needed here and what <c>Use=</c> forbids (<c>DWARF014</c>).
    /// </summary>
    [AfterMap]
    private static void FillAlias(Command source, CommandDto target)
    {
        if (source is AliasCommand alias) target.Alias = alias.Alias;
    }
}

/// <summary>
///     Both construction paths over the same types, so the difference is observable rather than argued.
/// </summary>
[DwarfMapper]
public partial class TicketMappers
{
    /// <summary>
    ///     Constructor-PARAMETER binding: the generator constructs directly and fills an object initializer,
    ///     where <c>init</c> members are assignable — so <c>Reference</c> survives.
    /// </summary>
    [MapProperty(nameof(TicketRow.Subject), "subject")]
    [MapProperty(nameof(TicketRow.Priority), "priority")]
    public partial Ticket ToTicket(TicketRow source);
}

// The factory path over the SAME pair deliberately does NOT live here.
//
// Declaring both would put two providers on one (TicketRow, Ticket) key, and the registry is first-wins: the
// harness would then silently exercise whichever module initializer ran first. That is a real consumer hazard
// — and it is asserted as ambiguity on the Address pair, where it is the POINT of the test — but here it
// would just make the init-only assertion non-deterministic.
//
// The factory-drops-init-only behaviour itself is DWARF080, covered in-assembly where the contrast can be
// stated without a registry in the way.

/// <summary>
///     Non-public construction across an assembly boundary: <c>internal</c> ctor + <c>[InternalsVisibleTo]</c>
///     + <c>AllowNonPublic</c>. All three are required, and all three are compiler-checked.
/// </summary>
[DwarfMapper(AllowNonPublic = true)]
[GenerateMap<AuditRow, AuditEntry>]
public partial class AuditMappers
{
}

/// <summary>Patch-merge and full replace on ONE mapper — the shape that used to force a class split.</summary>
[DwarfMapper]
public partial class SettingsMappers
{
    public partial void Replace(SettingsPatch source, Settings destination);

    [MapNullSkip]
    public partial void Patch(SettingsPatch source, Settings destination);
}

/// <summary>
///     A hand-written map registered by declaration rather than by reflection.
/// </summary>
/// <remarks>
///     An object that HOLDS a collection, mapped to the collection, is not a DwarfMapper mapping shape — so it
///     is written by hand. Without <c>[ProvidesMap]</c> it is then an ordinary method that nothing registers:
///     the code is correct and every facade call site for the pair still throws, which a parity harness
///     reports as "not registered at all". Round 18 hit that on five pairs and recorded it as
///     <i>"the code is fine; the harness cannot see it."</i>
/// </remarks>
[DwarfMapper]
[GenerateMap<Quote, QuoteDto>]
public partial class QuoteMappers
{
    [ProvidesMap]
    public ICollection<QuoteDto> ToQuotes(QuoteBook book)
    {
        // The registry CALLS this method; it does not wrap it. A hand-written map carries its own guards.
        ArgumentNullException.ThrowIfNull(book);

        return book.Quotes.Select(Map).ToList();
    }
}

/// <summary>
///     A collection over a pair that constructs through a <c>[MapConstructor]</c> factory.
/// </summary>
/// <remarks>
///     The element route has to run the factory AND the member assignments. Adopting the factory as the
///     element converter emits <c>result.Add(Create(item))</c> — the bare factory, no members — which for a
///     factory that ignores its argument returns a list of blank objects. Found in a live consumer, whose own
///     source carried a fourteen-line comment telling readers not to rely on the map.
/// </remarks>
[DwarfMapper]
[GenerateMap<Part, PartDto>]
[MapConstructor<Part, PartDto>(nameof(CreatePart))]
public partial class PartMappers
{
    public partial ICollection<PartDto> ToParts(List<Part> source);

    private static PartDto CreatePart(Part source) => PartDto.FromCode(source.Code);
}

/// <summary>enum → string under the DEFAULT: <c>[Description]</c> decides the persisted text.</summary>
[DwarfMapper]
[GenerateMap<Shipment, ShipmentDoc>]
public partial class ShipmentDocMappers
{
}

/// <summary>Members named for C# keywords, across the assembly boundary.</summary>
[DwarfMapper]
[GenerateMap<KeywordRow, KeywordDto>]
public partial class KeywordMappers
{
}
