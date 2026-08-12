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

    /// <summary>
    ///     The subset of <see cref="Maps" /> whose SOURCE is an interface, kept as a flat list so lookup can
    ///     test assignability without asking the runtime type for its interfaces.
    /// </summary>
    /// <remarks>
    ///     This exists to keep the library trim-safe. The obvious implementation —
    ///     <c>source.GetType().GetInterfaces()</c> — trips IL2075, because the trimmer cannot prove the
    ///     interface metadata of a type obtained from <c>object.GetType()</c> survives. Suppressing that would
    ///     have been the first trimming suppression in this assembly, in the one library whose pitch is
    ///     AOT-safety.
    ///     <para>
    ///         Inverting the question removes the hazard entirely: instead of asking the source which
    ///         interfaces it has, ask each registered interface whether it accepts the source.
    ///         <see cref="Type.IsInstanceOfType" /> needs no annotation, and the interface types here are
    ///         already rooted — generated module initializers reference them directly to register the map.
    ///     </para>
    ///     <para>
    ///         The list stays short: one entry per interface-keyed registration, scanned only after both the
    ///         exact-type and base-type lookups have missed.
    ///     </para>
    /// </remarks>
    private static readonly ConcurrentBag<(Type Source, Type Destination, Func<object, object> Map)>
        InterfaceMaps = [];

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
        {
            Ambiguous.TryAdd(key, 1);
            return;
        }

        // Mirror interface-keyed registrations into the assignability list. Only on a successful TryAdd, so a
        // duplicate registration does not double-count and read as ambiguous at lookup time.
        if (source.IsInterface)
            InterfaceMaps.Add((source, destination, map));
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
    ///     <paramref name="source" /> first, then its base types (most-derived-first), then the interfaces it
    ///     implements. Throws <see cref="DwarfMapMissingException" /> when no map matches.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why interfaces are searched.</b> Without it, two independent gates disagreed about which type
    ///         to key on: the compile-time check (<c>DWARF061</c>) validates the call site's <i>static</i>
    ///         argument type, while this lookup used only the runtime type and its base chain. A method
    ///         declared <c>ICollection&lt;T&gt;</c> that returns a <c>List&lt;T&gt;</c> therefore needed
    ///         <b>both</b> pairs declared — declaring only the static one built clean and threw at runtime.
    ///         That cost a real migration a false start, and it is precisely the class of failure a
    ///         compile-time mapper exists to prevent.
    ///     </para>
    ///     <para>
    ///         It also makes a single registration reach far more sources: one entry keyed on
    ///         <c>IEnumerable&lt;S&gt;</c> now serves a <c>List&lt;S&gt;</c>, an <c>S[]</c>, a
    ///         <c>HashSet&lt;S&gt;</c>, and even a lazy LINQ iterator — whose private compiler-generated type
    ///         no attribute could ever name.
    ///     </para>
    ///     <para>
    ///         Interfaces are searched <b>last</b> and are the only ambiguous step, since a type can implement
    ///         many. Registration order is not meaningful, so a match is taken only when exactly ONE registered
    ///         interface accepts the source — otherwise the throw names the candidates rather than picking one
    ///         arbitrarily and mapping through a silently different map on the next run.
    ///     </para>
    /// </remarks>
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

        Func<object, object>? viaInterface = null;
        List<Type>? candidates = null;

        foreach (var (ifaceSource, ifaceDestination, map) in InterfaceMaps)
        {
            if (ifaceDestination != destination || !ifaceSource.IsInstanceOfType(source)) continue;

            if (viaInterface is null)
            {
                viaInterface = map;
                candidates = [ifaceSource];
            }
            else
            {
                candidates!.Add(ifaceSource);
            }
        }

        if (viaInterface is not null && candidates!.Count == 1)
            return viaInterface(source);

        throw new DwarfMapMissingException(runtimeType, destination, candidates);
    }

    /// <summary>Test-only: clears the registry. Not for production use.</summary>
    internal static void ResetForTests()
    {
        Maps.Clear();
        Ambiguous.Clear();
        InterfaceMaps.Clear();
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
