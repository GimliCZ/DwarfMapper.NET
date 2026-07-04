// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper;

/// <summary>
/// Ambient mapper facade: resolves a map from the process-wide <see cref="DwarfMapperRegistry"/> by
/// destination type. This is the cross-assembly convenience (AutoMapper-<c>IMapper</c>-style ergonomics)
/// — inject it once and call <c>Map&lt;TDestination&gt;(source)</c> regardless of which assembly declared
/// the map. Prefer the concrete generated mapper classes for in-assembly mapping (compile-checked, no
/// dictionary hop); use this only for ambient / cross-assembly resolution.
/// </summary>
public interface IDwarfMapper
{
    /// <summary>Maps <paramref name="source"/> to <typeparamref name="TDestination"/> (resolved by the
    /// runtime type of <paramref name="source"/>).</summary>
    TDestination Map<TDestination>(object source);

    /// <summary>Maps <paramref name="source"/> to <typeparamref name="TDestination"/> using the static
    /// source type — avoids the <c>GetType()</c> hop when the source type is known.</summary>
    TDestination Map<TSource, TDestination>(TSource source);
}

/// <summary>Default <see cref="IDwarfMapper"/> backed by <see cref="DwarfMapperRegistry"/>. Stateless;
/// register a single instance (see the generated <c>AddDwarfMappers</c> DI extension).</summary>
public sealed class DwarfMapperFacade : IDwarfMapper
{
    /// <summary>Shared stateless instance (the registry holds the only mutable state).</summary>
    public static readonly IDwarfMapper Instance = new DwarfMapperFacade();

    /// <inheritdoc />
    public TDestination Map<TDestination>(object source)
        => (TDestination)DwarfMapperRegistry.Map(source, typeof(TDestination));

    /// <inheritdoc />
    public TDestination Map<TSource, TDestination>(TSource source)
        => (TDestination)DwarfMapperRegistry.Map(source!, typeof(TDestination));
}
