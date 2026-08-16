// SPDX-License-Identifier: GPL-2.0-only

using ConsumerTests.Contracts;
using DwarfMapper;

// The policy stated ONCE, for the assembly, which is how a real consumer expresses "this assembly persists
// identifiers, whatever the [Description] annotations are for". Deliberately not repeated per mapper: the
// class-level form of this option is exercised by ProviderA's neighbours, and what needed proving here is
// that the ASSEMBLY-level form reaches a mapper at all — the enum-format assertion in
// ConsumerSurfaceTests fails if it does not, so this row is observed rather than merely present.
[assembly: DwarfMapperDefaults(EnumStringSource = EnumStringSource.Identifier)]

namespace ConsumerTests.ProviderB;

/// <summary>
///     A SECOND provider that reaches the same nested <c>Address</c> pair as <c>ProviderA</c>, under a
///     different policy.
/// </summary>
/// <remarks>
///     <para>
///         This is the shape behind the open gap Round 18 recorded: a nested pair reached from two classes was
///         synthesized twice, once null-guarded and once not, silently. The migration hit it because
///         <c>SkipNullSourceMembers</c> was class-scoped and forced a class split; the split then forked the
///         nested pair's behaviour.
///     </para>
///     <para>
///         Here the split is deliberate and the divergence is <b>asserted</b>, so the two copies cannot quietly
///         swap behaviour. Note the pair-scoped <c>[MapNullSkip&lt;S,T&gt;]</c> makes the intent local and
///         explicit rather than an accident of which class happened to pull the pair in.
///     </para>
/// </remarks>
[DwarfMapper]
[GenerateMap<Address, AddressDto>]
[MapNullSkip<Address, AddressDto>]
public partial class AddressPatchMappers
{
}

/// <summary>
///     A create-map declared in a different assembly from <c>ProviderA</c>'s. The registry is process-wide, so
///     <c>Host</c> resolves both without referencing either.
/// </summary>
[DwarfMapper]
[GenerateMap<AliasCommand, CommandDto>]
[MapProperty<AliasCommand, CommandDto>(nameof(AliasCommand.Alias), nameof(CommandDto.Alias))]
public partial class AliasCommandMappers
{
}

/// <summary>
///     The same enum, the other reading of the same annotation — declared in a DIFFERENT assembly.
/// </summary>
/// <remarks>
///     <para>
///         <c>EnumStringSource.Identifier</c> says the annotations on this enum are for display and the
///         persisted form is the member name. Both readings are live in this process at once, over one enum,
///         from two assemblies — which is the case the synthesized helper's name has to survive. Keyed by TYPE
///         alone the two would have shared one helper, and whichever loaded first would have decided the
///         persisted format for the other.
///     </para>
///     <para>
///         The reading comes from the assembly-level <c>[DwarfMapperDefaults]</c> at the top of this file, not
///         from an option on this class. That is what makes the assertion over this pair a test of the
///         assembly-scoped form: if the generator read the ambient default only for its own mappers and not
///         for this one, the persisted string would revert to "Next-Day" and the host would say so.
///     </para>
/// </remarks>
[DwarfMapper]
[GenerateMap<Shipment, ShipmentLog>]
public partial class ShipmentLogMappers
{
}
