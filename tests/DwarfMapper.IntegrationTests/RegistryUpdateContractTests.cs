// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

/// <summary>
///     The update table's contract, which nothing pinned. <c>RegisterUpdate</c> was referenced exactly twice
///     in the repository — its own declaration and the generator emitting calls to it — and executed only
///     IMPLICITLY, through module initializers registering update maps and consumer tests reaching them via
///     <c>Update</c>. First-wins, ambiguity marking, and separation from the create table were all unasserted
///     on the same shared static the create table guards carefully.
///     <para>
///         What that hid, measured rather than read: a duplicate <c>RegisterUpdate</c> WAS marked, but into
///         the create table's ambiguity set — the only one that existed. Two update registrations for a pair
///         with no create map left <c>IsAmbiguous</c> true while <c>IsProvided</c> was false. Every test
///         below fails against that code, three of them at compile time (there was no
///         <c>IsUpdateAmbiguous</c>) and <see cref="Duplicate_update_registrations_do_not_mark_the_create_table" />
///         on its assertion.
///     </para>
///     <para>
///         Pinned to the <c>registry-torture</c> collection: <see cref="DwarfMapperRegistry" /> is a process-wide
///         static that lives for the whole run, so these must not race the torture tests through it. Isolation
///         within the collection comes from key types unique to each test, the same convention the torture
///         suite uses — the runtime assembly exposes no reset hook, deliberately.
///     </para>
/// </summary>
[Collection("registry-torture")]
public sealed class RegistryUpdateContractTests
{
    private sealed class USrc { public int Id { get; set; } }

    private sealed class UDst { public int Id { get; set; } }

    private sealed class UMarkSrc { public int Id { get; set; } }

    private sealed class UMarkDst { public int Id { get; set; } }

    private sealed class UAliasSrc { public int Id { get; set; } }

    private sealed class UAliasDst { public int Id { get; set; } }

    private sealed class USoloSrc { public int Id { get; set; } }

    private sealed class USoloDst { public int Id { get; set; } }

    /// <summary>
    ///     A duplicate keeps the FIRST delegate, exactly as <c>Register</c> does. Last-wins would let the
    ///     behaviour of a pair depend on assembly load order, which is not something a consumer controls.
    /// </summary>
    [Fact]
    public void A_duplicate_update_registration_is_first_wins_and_marked_ambiguous()
    {
        DwarfMapperRegistry.RegisterUpdate(typeof(USrc), typeof(UDst), (_, d) => ((UDst)d).Id = 1);
        DwarfMapperRegistry.RegisterUpdate(typeof(USrc), typeof(UDst), (_, d) => ((UDst)d).Id = 2);

        var dst = new UDst();
        DwarfMapperRegistry.Update(new USrc(), dst, typeof(USrc), typeof(UDst));

        Assert.Equal(1, dst.Id);
        Assert.True(DwarfMapperRegistry.IsUpdateAmbiguous(typeof(USrc), typeof(UDst)),
            "A duplicate update registration must be MARKED, exactly as the create table marks its "
            + "duplicates. Silently dropping the loser is the asymmetry the create table already solved.");
    }

    /// <summary>
    ///     The regression guard for the defect this file was written to find: a duplicate on the UPDATE table
    ///     must not be recorded against the CREATE table.
    /// </summary>
    /// <remarks>
    ///     Before the fix this asserted pair read <c>IsAmbiguous == true</c> and <c>IsProvided == false</c> —
    ///     an ambiguous create map for a pair that had no create map at all. `IsAmbiguous` is documented as
    ///     answering whether more than one assembly registered a MAP for the pair, so the update table
    ///     writing into it made the create-side accessor answer with the update table's data. Nothing else in
    ///     the repository reads that set, which is exactly why it went unnoticed.
    /// </remarks>
    [Fact]
    public void Duplicate_update_registrations_do_not_mark_the_create_table()
    {
        DwarfMapperRegistry.RegisterUpdate(typeof(UMarkSrc), typeof(UMarkDst), (_, d) => ((UMarkDst)d).Id = 1);
        DwarfMapperRegistry.RegisterUpdate(typeof(UMarkSrc), typeof(UMarkDst), (_, d) => ((UMarkDst)d).Id = 2);

        Assert.True(DwarfMapperRegistry.IsUpdateAmbiguous(typeof(UMarkSrc), typeof(UMarkDst)));

        Assert.False(DwarfMapperRegistry.IsProvided(typeof(UMarkSrc), typeof(UMarkDst)),
            "sanity: no create map was ever registered for this pair.");
        Assert.False(DwarfMapperRegistry.IsAmbiguous(typeof(UMarkSrc), typeof(UMarkDst)),
            "A duplicate UPDATE registration marked the CREATE table's ambiguity set, so this pair reported "
            + "'ambiguous but not provided' — a create map that is contested and does not exist. The two key "
            + "spaces are separate precisely because a pair can legitimately have both a create map and an "
            + "update map; their ambiguity must be separate for the same reason.");
    }

    /// <summary>A single registration is not ambiguous — without this the marking assertions could pass on a set that is always non-empty.</summary>
    [Fact]
    public void A_single_update_registration_is_not_ambiguous()
    {
        Assert.False(DwarfMapperRegistry.IsUpdateAmbiguous(typeof(USoloSrc), typeof(USoloDst)),
            "sanity: nothing registered yet.");

        DwarfMapperRegistry.RegisterUpdate(typeof(USoloSrc), typeof(USoloDst), (_, d) => ((USoloDst)d).Id = 1);

        Assert.True(DwarfMapperRegistry.IsUpdateProvided(typeof(USoloSrc), typeof(USoloDst)));
        Assert.False(DwarfMapperRegistry.IsUpdateAmbiguous(typeof(USoloSrc), typeof(USoloDst)));
    }

    /// <summary>
    ///     The two key spaces do not alias, in both directions. A create registration must not surface as an
    ///     update registration — a caller would get a create where they asked for an in-place update — and its
    ///     ambiguity must not surface as the update table's.
    /// </summary>
    [Fact]
    public void The_update_table_does_not_alias_the_map_table()
    {
        DwarfMapperRegistry.Register(typeof(UAliasSrc), typeof(UAliasDst), _ => new UAliasDst());
        DwarfMapperRegistry.Register(typeof(UAliasSrc), typeof(UAliasDst), _ => new UAliasDst());

        Assert.True(DwarfMapperRegistry.IsAmbiguous(typeof(UAliasSrc), typeof(UAliasDst)),
            "sanity: the create table marked its own duplicate.");

        Assert.False(DwarfMapperRegistry.IsUpdateProvided(typeof(UAliasSrc), typeof(UAliasDst)),
            "A Register on the create table must not surface as an update registration — a caller would get "
            + "a create where they asked for an in-place update.");
        Assert.False(DwarfMapperRegistry.IsUpdateAmbiguous(typeof(UAliasSrc), typeof(UAliasDst)),
            "The create table's duplicate must not surface as an update-table duplicate either. The "
            + "separation has to hold in BOTH directions, or one accessor is still reading the other's set.");
    }
}
