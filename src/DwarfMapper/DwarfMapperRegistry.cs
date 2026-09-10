// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace DwarfMapper
{
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
        private static readonly RegistryTable<Func<object, object>> Maps = new();

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
        ///     <para>
        ///         <b>Why a copy-on-write array and not a concurrent collection.</b> This was a
        ///         <see cref="ConcurrentBag{T}" />, which looks like the obvious choice and is the wrong one for
        ///         a read-mostly set: a bag's enumerator does not walk in place, it COPIES every element into a
        ///         fresh list on each enumeration. Since <see cref="Map" /> enumerates this on every ambient
        ///         interface-path call, the per-call allocation grew with the size of the consumer's entire map
        ///         graph — 56 KB per call against 2,740 registered pairs, versus 24 B on the exact-type path.
        ///         Retention was flat, so nothing leaked; but under Server GC that churn produces exactly the
        ///         rising heap a consumer reported as a leak.
        ///     </para>
        ///     <para>
        ///         The access pattern is the argument: registration happens once per assembly load from a module
        ///         initializer, lookup happens per call forever after. Copy-on-write pays the whole cost on the
        ///         rare side and leaves the hot side allocating nothing — a <c>for</c> over an array reference
        ///         read once. Pinned by <c>AmbientDispatchAllocationRuntimeTests</c>.
        ///     </para>
        /// </remarks>
        private static volatile (Type Source, Type Destination, Func<object, object> Map)[] _interfaceMaps = [];

        /// <summary>Serialises the copy-on-write swap; never taken on the lookup path.</summary>
        private static readonly Lock InterfaceMapsGate = new();

        /// <summary>
        ///     Update-into (merge) maps, keyed separately from the create-maps.
        /// </summary>
        /// <remarks>
        ///     A pair can legitimately have BOTH — <c>TDest Map(TSource)</c> and <c>void Update(TSource, TDest)</c>
        ///     are different operations over the same types — so they cannot share a key space without one
        ///     shadowing the other.
        /// </remarks>
        private static readonly RegistryTable<Action<object, object>> UpdateMaps = new();

        /// <summary>All registered (source, destination) pairs. For diagnostics / validation only.</summary>
        public static IReadOnlyCollection<(Type Source, Type Destination)> Provided
        {
            get
            {
                // Deliberately NOT `Maps.Keys`: ConcurrentDictionary.Keys materialises its own List snapshot,
                // so reading it here allocated the key set twice — 87 KB per read at 2,740 pairs. The
                // dictionary's own enumerator is lazy and allocates nothing per element.
                var list = new List<(Type, Type)>(Maps.Count);
                foreach (var key in Maps.EnumerateKeys())
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
            if (!Maps.TryRegister(key, map))
            {
                return;
            }

            // Mirror interface-keyed registrations into the assignability list. Only on a successful TryAdd, so a
            // duplicate registration does not double-count and read as ambiguous at lookup time.
            if (source.IsInterface)
            {
                lock (InterfaceMapsGate)
                {
                    var current = _interfaceMaps;
                    var grown = new (Type, Type, Func<object, object>)[current.Length + 1];
                    Array.Copy(current, grown, current.Length);
                    grown[current.Length] = (source, destination, map);

                    // Publish the fully built array in one volatile write: a reader either sees the old array
                    // or the complete new one, never a half-filled one.
                    _interfaceMaps = grown;
                }
            }
        }

        /// <summary>True if a map for the exact pair is registered.</summary>
        public static bool IsProvided(Type source, Type destination)
        {
            return Maps.IsProvided(new Key(source, destination));
        }

        /// <summary>True if more than one assembly registered a map for the exact pair.</summary>
        public static bool IsAmbiguous(Type source, Type destination)
        {
            return Maps.IsAmbiguous(new Key(source, destination));
        }

        /// <summary>Tries to get the map delegate for the exact pair (no base-type walk).</summary>
        public static bool TryGet(Type source, Type destination, out Func<object, object>? map)
        {
            return Maps.TryGet(new Key(source, destination), out map);
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

            // RS0030: object.GetType() is banned project-wide (zero-reflection claim). This call is the ONE
            // sanctioned exception the ban message itself names: ambient-registry dispatch resolves the map by
            // the value's runtime type (then base types, then interfaces) - the documented core mechanism of
            // this API. GetType() is AOT- and trim-safe; no member metadata is reflected over.
#pragma warning disable RS0030
            var runtimeType = source.GetType();
#pragma warning restore RS0030
            if (Maps.TryGet(new Key(runtimeType, destination), out var direct))
            {
                return direct(source);
            }

            for (var baseType = runtimeType.BaseType; baseType is not null; baseType = baseType.BaseType)
                if (Maps.TryGet(new Key(baseType, destination), out var viaBase))
                {
                    return viaBase(source);
                }

            // One volatile read; the array is never mutated in place, so this snapshot stays coherent for the
            // whole walk even if another assembly registers mid-loop.
            var interfaceMaps = _interfaceMaps;

            Func<object, object>? viaInterface = null;
            Type? firstMatch = null;
            List<Type>? candidates = null;

            for (var i = 0; i < interfaceMaps.Length; i++)
            {
                var (ifaceSource, ifaceDestination, map) = interfaceMaps[i];
                if (ifaceDestination != destination || !ifaceSource.IsInstanceOfType(source))
                {
                    continue;
                }

                if (viaInterface is null)
                {
                    viaInterface = map;
                    firstMatch = ifaceSource;
                }
                else
                {
                    // Only now is a list worth allocating. The old code built one on the FIRST match, so the
                    // overwhelmingly common single-match case — the one that returns — paid for a collection
                    // that existed solely to have its Count compared to 1.
                    candidates ??= [firstMatch!];
                    candidates.Add(ifaceSource);
                }
            }

            if (viaInterface is not null && candidates is null)
            {
                return viaInterface(source);
            }

            // candidates is null here only when nothing matched; when it is non-null the match was ambiguous
            // and the throw names every interface that accepted the source, as before.
            throw new DwarfMapMissingException(runtimeType, destination, candidates);
        }

        /// <summary>
        ///     Registers an update-into map for the exact <paramref name="source" />/<paramref name="destination" />
        ///     pair. Idempotent per key; a second registration is recorded as ambiguous (the first wins) and
        ///     surfaced by <see cref="IsUpdateAmbiguous" /> rather than throwing at load — the same contract
        ///     <see cref="Register" /> gives the create table, on the update table's own set.
        /// </summary>
        public static void RegisterUpdate(Type source, Type destination, Action<object, object> map)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(map);

            var key = new Key(source, destination);
            UpdateMaps.TryRegister(key, map);
        }

        /// <summary>True if an update-into map for the exact pair is registered.</summary>
        public static bool IsUpdateProvided(Type source, Type destination)
        {
            return UpdateMaps.IsProvided(new Key(source, destination));
        }

        /// <summary>
        ///     True if more than one update-into map was registered for the exact pair. Mirrors
        ///     <see cref="IsAmbiguous" /> on the create table: a duplicate is first-wins and MARKED, and the two
        ///     tables are marked independently, so neither accessor reports the other's duplicates.
        /// </summary>
        public static bool IsUpdateAmbiguous(Type source, Type destination)
        {
            return UpdateMaps.IsAmbiguous(new Key(source, destination));
        }

        // ── Deliberate asymmetries with the create table ────────────────────────────────────────────────
        // Three create-side members have NO update-side twin, and a reader looking for one should find this note
        // rather than assume parity that does not exist:
        //
        //   * No `UpdateProvided` enumeration. `Provided` exists to feed validation, and validation asks only
        //     whether a create map is reachable — the emitted `DwarfMap.Validate()` calls `IsProvided`, never
        //     `Provided` — so an update-table enumerator would be surface added for no caller.
        //   * No `TryGetUpdate`. `TryGet` hands out the create delegate for callers that want to invoke it
        //     themselves; the update delegate is only ever meaningful applied to a destination the caller
        //     already holds, which is exactly what `Update` does.
        //   * No base/interface walk in `Update` — see its remarks below. That one is a SAFETY property, not an
        //     omission, so mirroring the create table here would be a regression.
        //
        // Torture coverage mirrors the create table only where a twin exists; RegistryConcurrencyTortureTests
        // states the same asymmetries at the point where the missing tests would otherwise be.

        /// <summary>
        ///     Applies the registered update-into map, mutating <paramref name="destination" /> in place.
        /// </summary>
        /// <remarks>
        ///     Resolution is by the DECLARED types, not by <c>GetType()</c>. Update-into writes into an instance
        ///     the caller already holds, so silently dispatching on a more-derived runtime type could write
        ///     members the caller's declared contract never mentioned — a create-map has no equivalent hazard,
        ///     because it hands back an object the caller had no prior expectations about.
        /// </remarks>
        public static void Update(object source, object destination, Type sourceType, Type destinationType)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(sourceType);
            ArgumentNullException.ThrowIfNull(destinationType);

            if (!UpdateMaps.TryGet(new Key(sourceType, destinationType), out var map))
            {
                throw new DwarfMapMissingException(sourceType, destinationType, null, true);
            }

            map(source, destination);
        }

        /// <summary>
        ///     One registration table: the delegate registered per (source, destination) pair, and the pairs a
        ///     second, distinct registration arrived for.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The two halves are ONE type because keeping them as separate statics is what allowed the bug
        ///         this registry used to have. <c>RegisterUpdate</c> marked its duplicates in the CREATE table's
        ///         ambiguity set — the only one that existed at the time — so two update registrations for a pair
        ///         with no create map left <see cref="IsAmbiguous" /> reporting <c>true</c> while
        ///         <see cref="IsProvided" /> reported <c>false</c>: an ambiguous map that was never registered.
        ///         The duplicate was real; it was recorded against the wrong table.
        ///     </para>
        ///     <para>
        ///         Nothing about that was subtle. It was one identifier out of four, and with four loose
        ///         dictionaries the type system had no opinion about which ambiguity set belonged to which map
        ///         table. Bound together, marking the wrong one is not a mistake that can be written: the
        ///         duplicate is recorded by the same call that failed to add, on the same instance.
        ///     </para>
        ///     <para>
        ///         A pair can legitimately have BOTH a create and an update map — <c>TDest Map(TSource)</c> and
        ///         <c>void Update(TSource, TDest)</c> are different operations over the same types — so the two
        ///         instances must stay separate key spaces. That is a real distinction; the four-static version
        ///         merely failed to enforce it.
        ///     </para>
        /// </remarks>
        private sealed class RegistryTable<TDelegate>
            where TDelegate : Delegate
        {
            private readonly ConcurrentDictionary<Key, byte> _ambiguous = new();
            private readonly ConcurrentDictionary<Key, TDelegate> _maps = new();

            public int Count => _maps.Count;

            /// <summary>
            ///     Lazily walks the registered keys. Replaces a <c>Keys</c> property: that returns
            ///     <see cref="ConcurrentDictionary{TKey,TValue}.Keys" />, which builds a fresh
            ///     <see cref="List{T}" /> of every key on each read.
            /// </summary>
            public IEnumerable<Key> EnumerateKeys()
            {
                foreach (var entry in _maps)
                    yield return entry.Key;
            }

            /// <summary>
            ///     Registers <paramref name="map" /> for <paramref name="key" />, first-wins.
            /// </summary>
            /// <returns>
            ///     <c>true</c> when this was the first registration for the pair; <c>false</c> when it was a
            ///     duplicate, which this call has recorded as ambiguous. Duplicates do not throw at load —
            ///     validation surfaces them instead, so one bad assembly cannot take the process down at startup.
            /// </returns>
            public bool TryRegister(Key key, TDelegate map)
            {
                if (_maps.TryAdd(key, map))
                {
                    return true;
                }

                _ambiguous.TryAdd(key, 1);
                return false;
            }

            public bool IsProvided(Key key)
            {
                return _maps.ContainsKey(key);
            }

            public bool IsAmbiguous(Key key)
            {
                return _ambiguous.ContainsKey(key);
            }

            /// <remarks>
            ///     <c>[MaybeNullWhen(false)]</c> so a caller that checks the result gets a non-null delegate
            ///     without a <c>!</c> — the same contract <c>ConcurrentDictionary.TryGetValue</c> carries, which
            ///     is where this flow analysis came from before the table existed. Dropping it would have pushed
            ///     three null-forgiving operators into the lookup path.
            /// </remarks>
            public bool TryGet(Key key, [MaybeNullWhen(false)] out TDelegate map)
            {
                return _maps.TryGetValue(key, out map);
            }
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
}
