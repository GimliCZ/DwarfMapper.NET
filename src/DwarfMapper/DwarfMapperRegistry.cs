// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Concurrent;

namespace DwarfMapper;

/// <summary>
///     Process-wide registry of generated maps, populated at assembly load by generated
///     <c>[ModuleInitializer]</c> code (zero reflection, AOT-safe). It is the runtime backing for the
///     ambient <see cref="IDwarfMapper" /> facade, which lets one assembly use a map declared in another
///     without a compile-time reference to it — the cross-assembly analogue of AutoMapper's single
///     <c>IMapper</c>, but filled by codegen rather than a reflection scan.
/// </summary>
/// <remarks>
///     In-assembly mapping should keep using the concrete generated mapper classes (fully compile-checked,
///     no dictionary hop). The registry is only for cross-assembly / ambient resolution. Linkage
///     completeness is verified separately (compile-time DWARF061 at the validation root, or
///     <c>DwarfMap.Validate()</c> at startup) so a missing map is a loud failure, not a silent surprise.
/// </remarks>
public static class DwarfMapperRegistry
{
    private static readonly ConcurrentDictionary<Key, Func<object, object>> Maps = new();
    private static readonly ConcurrentDictionary<Key, byte> Ambiguous = new();

    /// <summary>All registered (source, destination) pairs. For diagnostics / validation only.</summary>
    public static IReadOnlyCollection<(Type Source, Type Destination)> Provided
    {
        get
        {
            var list = new List<(Type, Type)>(Maps.Count);
            foreach (var key in Maps.Keys)
                list.Add((key.Source, key.Destination));
            return list;
        }
    }

    /// <summary>
    ///     Registers a map <paramref name="source" /> -> <paramref name="destination" />. Idempotent per key;
    ///     a second DISTINCT provider for the same pair is recorded as ambiguous (the first registration
    ///     wins) and surfaced by <see cref="IsAmbiguous" /> / validation rather than throwing at load.
    /// </summary>
    public static void Register(Type source, Type destination, Func<object, object> map)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(map);

        var key = new Key(source, destination);
        if (!Maps.TryAdd(key, map))
            Ambiguous.TryAdd(key, 1);
    }

    /// <summary>True if a map for the exact pair is registered.</summary>
    public static bool IsProvided(Type source, Type destination)
    {
        return Maps.ContainsKey(new Key(source, destination));
    }

    /// <summary>True if more than one assembly registered a map for the exact pair.</summary>
    public static bool IsAmbiguous(Type source, Type destination)
    {
        return Ambiguous.ContainsKey(new Key(source, destination));
    }

    /// <summary>Tries to get the map delegate for the exact pair (no base-type walk).</summary>
    public static bool TryGet(Type source, Type destination, out Func<object, object>? map)
    {
        return Maps.TryGetValue(new Key(source, destination), out map);
    }

    /// <summary>
    ///     Maps <paramref name="source" /> to <paramref name="destination" />. Resolves by the runtime type of
    ///     <paramref name="source" /> first, then walks its base types (most-derived-first) so an instance of a
    ///     derived type can be served by a map registered for a base. Throws
    ///     <see cref="DwarfMapMissingException" /> when no map matches.
    /// </summary>
    public static object Map(object source, Type destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        var runtimeType = source.GetType();
        if (Maps.TryGetValue(new Key(runtimeType, destination), out var direct))
            return direct(source);

        for (var baseType = runtimeType.BaseType; baseType is not null; baseType = baseType.BaseType)
            if (Maps.TryGetValue(new Key(baseType, destination), out var viaBase))
                return viaBase(source);

        throw new DwarfMapMissingException(runtimeType, destination);
    }

    /// <summary>Test-only: clears the registry. Not for production use.</summary>
    internal static void ResetForTests()
    {
        Maps.Clear();
        Ambiguous.Clear();
    }

    private readonly struct Key : IEquatable<Key>
    {
        public readonly Type Source;
        public readonly Type Destination;

        public Key(Type source, Type destination)
        {
            Source = source;
            Destination = destination;
        }

        public bool Equals(Key other)
        {
            return Source == other.Source && Destination == other.Destination;
        }

        public override bool Equals(object? obj)
        {
            return obj is Key k && Equals(k);
        }

        public override int GetHashCode()
        {
            return unchecked((Source.GetHashCode() * 397) ^ Destination.GetHashCode());
        }
    }
}
