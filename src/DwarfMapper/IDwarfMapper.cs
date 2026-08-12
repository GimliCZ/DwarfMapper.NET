// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     Ambient mapper facade: resolves a map from the process-wide <see cref="DwarfMapperRegistry" /> by
///     destination type. This is the cross-assembly convenience (AutoMapper-<c>IMapper</c>-style ergonomics)
///     — inject it once and call <c>Map&lt;TDestination&gt;(source)</c> regardless of which assembly declared
///     the map. Prefer the concrete generated mapper classes for in-assembly mapping (compile-checked, no
///     dictionary hop); use this only for ambient / cross-assembly resolution.
/// </summary>
public interface IDwarfMapper
{
    /// <summary>
    ///     Maps <paramref name="source" /> to <typeparamref name="TDestination" /> (resolved by the
    ///     runtime type of <paramref name="source" />).
    /// </summary>
    TDestination Map<TDestination>(object source);

    /// <summary>
    ///     Maps <paramref name="source" /> to <typeparamref name="TDestination" /> using the static
    ///     source type — avoids the <c>GetType()</c> hop when the source type is known.
    /// </summary>
    TDestination Map<TSource, TDestination>(TSource source);

    /// <summary>
    ///     Maps <paramref name="source" /> ONTO the existing <paramref name="destination" />, preserving its
    ///     identity — the update-into (merge) shape.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Both other overloads CONSTRUCT a new destination, which is the wrong shape for a tracked
    ///         entity, a pooled object, or a PATCH endpoint. Before this existed, the only way to reach
    ///         update-into was to inject the concrete generated mapper alongside the facade — the single place
    ///         a ~300-map migration was not a near-verbatim swap, at 16 call sites.
    ///     </para>
    ///     <para>
    ///         Fully generic in both directions, so both types are known at the call site: this is no more
    ///         type-erased than <see cref="Map{TSource,TDestination}(TSource)" />, which already ships.
    ///     </para>
    ///     <para>
    ///         Resolved from an exact <c>(TSource, TDestination)</c> pair in a key space distinct from the
    ///         create-maps — a pair can have both, and they are different operations.
    ///     </para>
    /// </remarks>
    void Map<TSource, TDestination>(TSource source, TDestination destination);
}

/// <summary>
///     Default <see cref="IDwarfMapper" /> backed by <see cref="DwarfMapperRegistry" />. Stateless;
///     register a single instance (see the generated <c>AddDwarfMappers</c> DI extension).
/// </summary>
public sealed class DwarfMapperFacade : IDwarfMapper
{
    /// <summary>Shared stateless instance (the registry holds the only mutable state).</summary>
    public static readonly IDwarfMapper Instance = new DwarfMapperFacade();

    /// <inheritdoc />
    public TDestination Map<TDestination>(object source)
    {
        return (TDestination)DwarfMapperRegistry.Map(source, typeof(TDestination));
    }

    /// <inheritdoc />
    public TDestination Map<TSource, TDestination>(TSource source)
    {
        // Actually use TSource. This overload documents itself as "uses the static source type — avoids the
        // GetType() hop", but its body was byte-identical to the one-parameter overload: it called
        // DwarfMapperRegistry.Map, which resolves via source.GetType() and then walks base types. So the
        // advertised fast path did not exist, and `Map<Base, Dto>(derived)` silently dispatched on the DERIVED
        // runtime type rather than the requested static one. The exact-pair lookup gives both: no GetType()
        // hop, and the pair the caller actually asked for.
        if (DwarfMapperRegistry.TryGet(typeof(TSource), typeof(TDestination), out var map) && map is not null)
            return (TDestination)map(source!);

        return (TDestination)DwarfMapperRegistry.Map(source!, typeof(TDestination));
    }

    /// <inheritdoc />
    public void Map<TSource, TDestination>(TSource source, TDestination destination)
    {
        DwarfMapperRegistry.Update(source!, destination!, typeof(TSource), typeof(TDestination));
    }
}
