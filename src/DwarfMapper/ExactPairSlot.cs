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
    ///         <b>Why a static generic field and not a cache object.</b> The two-type overload knows its pair at JIT
    ///         time, so the lookup it did on every call — hash two <see cref="Type" /> references, probe a
    ///         <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}" /> — recomputed a
    ///         constant. The runtime already gives every closed generic instantiation its own statics, so the answer
    ///         lives there: one reference read and one null test, no allocation. No reflection, nothing a trimmer
    ///         cannot see.
    ///     </para>
    ///     <para>
    ///         <b>Only a FOUND delegate is stored, and that is what makes this whole type small.</b> Registration is
    ///         add-only and first-wins — <c>TryAdd</c>, with no unregister — so a delegate that has once been
    ///         resolved for a pair is immutable for the life of the process, and caching it needs no invalidation at
    ///         all. A MISS is deliberately not cached: it re-asks on the next call, which is how a pair registered by
    ///         a module initializer that has not run yet is still picked up, and it costs exactly the dictionary
    ///         probe the caller would have paid anyway.
    ///     </para>
    ///     <para>
    ///         The first version of this type carried a registration version counter, an immutable
    ///         <c>(delegate, version)</c> entry object, paired volatile writes, and an ordering argument about
    ///         reading the version before the lookup and bumping it after the table write. All of it existed to
    ///         invalidate a cached MISS. Not caching the miss deleted the counter, the entry, the ordering argument,
    ///         and a documented non-monotonicity — a reader could previously see the exact pair and then see the
    ///         fallback again. It cannot now: <c>_map</c> only ever goes from <c>null</c> to one value.
    ///     </para>
    ///     <para>
    ///         <b>What may be cached, and nothing more.</b> ONLY the EXACT registered pair. The facade's fallback
    ///         resolves through <see cref="DwarfMapperRegistry.Map" />, which dispatches on the source INSTANCE's
    ///         runtime type: caching that here would key an answer derived from one instance's runtime type against
    ///         <c>TSource</c>, and hand the next call with a differently derived instance a mapper meant for its
    ///         sibling.
    ///     </para>
    /// </remarks>
    internal static class ExactPairSlot<TSource, TDestination>
    {
        private static Func<object, object>? _map;

        /// <summary>
        ///     The delegate registered for exactly <c>(TSource, TDestination)</c>, or <c>null</c> when no such pair is
        ///     registered and the caller must fall back to runtime-type resolution.
        /// </summary>
        internal static Func<object, object>? Get()
        {
            var cached = Volatile.Read(ref _map);
            if (cached is not null)
            {
                return cached;
            }

            if (!DwarfMapperRegistry.TryGet(typeof(TSource), typeof(TDestination), out var found) || found is null)
            {
                return null;
            }

            Volatile.Write(ref _map, found);
            return found;
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
    ///     be wrong about. A miss routes through <c>Update</c> so the absent-map throw keeps its single source.
    /// </remarks>
    internal static class ExactUpdateSlot<TSource, TDestination>
    {
        private static Action<object, object>? _map;

        /// <summary>The update-into delegate for exactly <c>(TSource, TDestination)</c>, or <c>null</c>.</summary>
        internal static Action<object, object>? Get()
        {
            var cached = Volatile.Read(ref _map);
            if (cached is not null)
            {
                return cached;
            }

            if (!DwarfMapperRegistry.TryGetUpdate(typeof(TSource), typeof(TDestination), out var found)
                || found is null)
            {
                return null;
            }

            Volatile.Write(ref _map, found);
            return found;
        }
    }
}
