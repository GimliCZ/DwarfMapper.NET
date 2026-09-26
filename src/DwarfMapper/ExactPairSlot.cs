// SPDX-License-Identifier: GPL-2.0-only

using System.Threading;

namespace DwarfMapper
{
    /// <summary>
    ///     The one registry answer for a pair whose BOTH types are static at the call site, cached on the closed
    ///     generic type. Backs <c>DwarfMapperFacade.Map&lt;TSource, TDestination&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why a static generic field and not a cache.</b> The two-type overload knows its pair at JIT time,
    ///         so the lookup it did on every call — hash two <see cref="Type" /> references, probe a
    ///         <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}" /> — recomputed a
    ///         constant. The runtime already gives every closed generic instantiation its own statics, so the answer
    ///         can live there: the hot path becomes one reference read, one <c>int</c> compare and one field read,
    ///         with no dictionary and no allocation. No reflection, nothing a trimmer cannot see.
    ///     </para>
    ///     <para>
    ///         <b>What may be cached, and nothing more.</b> ONLY the EXACT registered pair, or the knowledge that
    ///         there is none. The facade's fallback resolves through <see cref="DwarfMapperRegistry.Map" />, which
    ///         dispatches on the source INSTANCE's runtime type: caching that here would key an answer derived from
    ///         one instance's runtime type against <c>TSource</c>, and hand the next call with a differently derived
    ///         instance a mapper meant for its sibling. So a miss caches <c>null</c> and re-enters the fallback every
    ///         time, which is the unchanged path at its unchanged cost.
    ///     </para>
    ///     <para>
    ///         <b>Why one <see cref="Entry" /> object rather than two fields.</b> The delegate and the version it was
    ///         read at must be published TOGETHER. Held as two statics they cannot be: two threads resolving the same
    ///         pair while a registration lands between them can interleave their four writes so that one thread's
    ///         delegate ends up stamped with the other's newer version — a stale map that now looks current, and
    ///         stays current forever. One immutable object behind a single reference write cannot tear, so the pair
    ///         is either wholly visible or not visible at all.
    ///     </para>
    ///     <para>
    ///         <b>The ordering, which is the whole correctness argument.</b> A reader takes the registry version
    ///         BEFORE its lookup; a writer bumps it AFTER its table write. Then the only possible error is
    ///         pessimistic: a reader can see a registration that lands mid-resolution and stamp it with the older
    ///         version, so the next call resolves once more. Reverse either half and the error becomes optimistic —
    ///         an answer taken before a registration, stamped with the version from after it.
    ///     </para>
    ///     <para>
    ///         <b>What this does NOT promise.</b> A reader is not monotonic: because the version is bumped after the
    ///         table write, there is a window where the table already holds a pair and the version does not yet say
    ///         so, and in it a reader that looked before the write can store its "absent" answer over a reader that
    ///         already found the map — so a caller can see the exact pair and then see the fallback again. Every
    ///         answer in that window is one the registry legitimately gives, because a registration is not published
    ///         until the bump. Buying monotonicity would cost a compare-and-swap loop for a property no caller needs.
    ///     </para>
    /// </remarks>
    internal static class ExactPairSlot<TSource, TDestination>
    {
        private static Entry? _entry;

        /// <summary>
        ///     The delegate registered for exactly <c>(TSource, TDestination)</c>, or <c>null</c> when no such pair is
        ///     registered and the caller must fall back to runtime-type resolution.
        /// </summary>
        internal static Func<object, object>? Get()
        {
            // BEFORE the lookup. See the remarks: this ordering is what makes a race pessimistic instead of wrong.
            var version = DwarfMapperRegistry.Version;

            var entry = Volatile.Read(ref _entry);
            if (entry is not null && entry.Version == version)
            {
                return entry.Map;
            }

            var map = DwarfMapperRegistry.TryGet(typeof(TSource), typeof(TDestination), out var found) ? found : null;
            Volatile.Write(ref _entry, new Entry(version, map));
            return map;
        }

        private sealed class Entry(int version, Func<object, object>? map)
        {
            internal int Version { get; } = version;

            internal Func<object, object>? Map { get; } = map;
        }
    }

    /// <summary>
    ///     <see cref="ExactPairSlot{TSource,TDestination}" /> for the update-into direction, backing
    ///     <c>DwarfMapperFacade.Map&lt;TSource, TDestination&gt;(TSource, TDestination)</c>.
    /// </summary>
    /// <remarks>
    ///     A separate closed generic type rather than a second field on the create slot, so a pair that has only one
    ///     of the two maps does not carry a permanently-null slot for the other. Everything the create slot's remarks
    ///     argue applies unchanged; the update side is in fact the simpler case, because
    ///     <see cref="DwarfMapperRegistry.Update" /> resolves on the DECLARED types and has no runtime-type walk to
    ///     be wrong about. A miss still routes through <c>Update</c> so the absent-map throw keeps its single source.
    /// </remarks>
    internal static class ExactUpdateSlot<TSource, TDestination>
    {
        private static Entry? _entry;

        /// <summary>The update-into delegate for exactly <c>(TSource, TDestination)</c>, or <c>null</c>.</summary>
        internal static Action<object, object>? Get()
        {
            var version = DwarfMapperRegistry.Version;

            var entry = Volatile.Read(ref _entry);
            if (entry is not null && entry.Version == version)
            {
                return entry.Map;
            }

            var map = DwarfMapperRegistry.TryGetUpdate(typeof(TSource), typeof(TDestination), out var found)
                ? found
                : null;
            Volatile.Write(ref _entry, new Entry(version, map));
            return map;
        }

        private sealed class Entry(int version, Action<object, object>? map)
        {
            internal int Version { get; } = version;

            internal Action<object, object>? Map { get; } = map;
        }
    }
}
