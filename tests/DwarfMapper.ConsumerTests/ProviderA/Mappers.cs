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
