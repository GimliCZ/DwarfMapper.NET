// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.CompilerServices;

namespace DwarfMapper.IntegrationTests;

public sealed class ShapeSrc
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class ShapeDst
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class OptedOutSrc
{
    public int Id { get; set; }
}

public sealed class OptedOutDst
{
    public int Id { get; set; }
}

/// <summary>An ordinary mapper — collection shapes are registered for it by default.</summary>
[DwarfMapper]
[GenerateMap<ShapeSrc, ShapeDst>]
public partial class ShapeMappers
{
}

/// <summary>The opt-out, for a mapper never reached through the facade over a collection.</summary>
[DwarfMapper(RegisterCollectionShapes = false)]
[GenerateMap<OptedOutSrc, OptedOutDst>]
public partial class OptedOutMappers
{
}

/// <summary>
///     The ambient registry now serves collection shapes derived from each declared element map.
/// </summary>
/// <remarks>
///     <para>
///         This closes the blocking defect Round 18 found: AutoMapper derived collection maps implicitly from
///         the element map, so <c>_mapper.Map&lt;ICollection&lt;Dto&gt;&gt;(entities)</c> just worked. The
///         registry resolves an EXACT pair, so every such call threw <c>DwarfMapMissingException</c> at first
///         use — 47 call sites in one codebase, all latent behind a green build.
///     </para>
///     <para>
///         It slipped past three independent safety nets at once: the consumer's 706 tests (which construct or
///         mock services and never resolve through DI), the parity suite (which replays only DECLARED pairs),
///         and <c>DWARF061</c> (which needs the source type, and at <c>Map&lt;ICollection&lt;T&gt;&gt;(data)</c>
///         only the destination is static). It surfaced only because a benchmark happened to build the real DI
///         graph and count unresolvable registrations. See <c>Issues/Rount18/</c>.
///     </para>
/// </remarks>
public sealed class CollectionShapeRegistrationTests
{
    private static void EnsureRegistered() =>
        RuntimeHelpers.RunModuleConstructor(typeof(ShapeMappers).Module.ModuleHandle);

    private static List<ShapeSrc> Two() =>
        [new() { Id = 1, Name = "a" }, new() { Id = 2, Name = "b" }];

    [Fact]
    public void A_List_source_maps_to_ICollection_through_the_facade()
    {
        // Verbatim the shape that threw: the call site names only the DESTINATION, and the source is whatever
        // the repository happened to return.
        EnsureRegistered();

        var mapped = (ICollection<ShapeDst>)DwarfMapperRegistry.Map(Two(), typeof(ICollection<ShapeDst>));

        Assert.Equal([1, 2], mapped.Select(d => d.Id));
        Assert.Equal(["a", "b"], mapped.Select(d => d.Name));
    }

    [Theory]
    [InlineData(typeof(List<ShapeDst>))]
    [InlineData(typeof(ICollection<ShapeDst>))]
    [InlineData(typeof(IEnumerable<ShapeDst>))]
    [InlineData(typeof(IReadOnlyList<ShapeDst>))]
    [InlineData(typeof(IReadOnlyCollection<ShapeDst>))]
    [InlineData(typeof(ShapeDst[]))]
    public void Every_registered_destination_shape_resolves(Type destination)
    {
        // The destination axis has to be keyed exactly, because it is what the call site names. Six shapes
        // cover what a consumer actually writes.
        EnsureRegistered();

        var mapped = (IEnumerable<ShapeDst>)DwarfMapperRegistry.Map(Two(), destination);

        Assert.Equal([1, 2], mapped.Select(d => d.Id));
        Assert.IsAssignableFrom(destination, mapped);
    }

    [Fact]
    public void An_array_source_is_served_by_the_same_registration()
    {
        // The SOURCE axis does not need six rows: everything is keyed on IEnumerable<TSource>, and the
        // registry's interface lookup does the rest. That is what keeps the table at six rows per pair
        // instead of thirty.
        EnsureRegistered();

        var mapped = (List<ShapeDst>)DwarfMapperRegistry.Map(
            Two().ToArray(), typeof(List<ShapeDst>));

        Assert.Equal(2, mapped.Count);
    }

    [Fact]
    public void A_HashSet_source_is_served_by_the_same_registration()
    {
        EnsureRegistered();

        var mapped = (List<ShapeDst>)DwarfMapperRegistry.Map(
            new HashSet<ShapeSrc>(Two()), typeof(List<ShapeDst>));

        Assert.Equal(2, mapped.Count);
    }

    [Fact]
    public void A_lazy_LINQ_source_is_served_without_a_materializing_ToList()
    {
        // Before this, a Where()/SelectMany() result could not be mapped at all — its private iterator type is
        // unnameable by any attribute — and the workaround was a load-bearing .ToList() at each call site,
        // found one runtime failure at a time.
        EnsureRegistered();

        var lazy = Two().Where(s => s.Id > 1);

        var mapped = (List<ShapeDst>)DwarfMapperRegistry.Map(lazy, typeof(List<ShapeDst>));

        Assert.Equal(2, Assert.Single(mapped).Id);
    }

    [Fact]
    public void An_empty_source_maps_to_an_empty_destination_rather_than_throwing()
    {
        EnsureRegistered();

        var mapped = (List<ShapeDst>)DwarfMapperRegistry.Map(new List<ShapeSrc>(), typeof(List<ShapeDst>));

        Assert.Empty(mapped);
    }

    [Fact]
    public void RegisterCollectionShapes_false_registers_the_element_map_only()
    {
        // The opt-out has to actually opt out, or the option is decoration. The ELEMENT map must still work —
        // only the derived shapes are withheld.
        RuntimeHelpers.RunModuleConstructor(typeof(OptedOutMappers).Module.ModuleHandle);

        Assert.True(DwarfMapperRegistry.IsProvided(typeof(OptedOutSrc), typeof(OptedOutDst)));
        Assert.False(DwarfMapperRegistry.IsProvided(
            typeof(IEnumerable<OptedOutSrc>), typeof(List<OptedOutDst>)));

        Assert.Throws<DwarfMapMissingException>(
            () => DwarfMapperRegistry.Map(new List<OptedOutSrc>(), typeof(List<OptedOutDst>)));
    }

    [Fact]
    public void The_element_map_itself_is_unaffected()
    {
        EnsureRegistered();

        var mapped = (ShapeDst)DwarfMapperRegistry.Map(new ShapeSrc { Id = 5, Name = "x" }, typeof(ShapeDst));

        Assert.Equal(5, mapped.Id);
        Assert.Equal("x", mapped.Name);
    }
}
