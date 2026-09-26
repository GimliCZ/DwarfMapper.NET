// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace DwarfMapper
{
    /// <summary>
    ///     The element-by-element walk the ambient registry's auto-registered collection shapes use. Called from
    ///     generated <c>[ModuleInitializer]</c> registrations; not meant to be called by hand.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         WHY THIS IS IN THE RUNTIME AND NOT IN THE EMITTED CODE. Every mapped pair auto-registers six
    ///         collection shapes, all keyed on <c>IEnumerable&lt;TSource&gt;</c>, so a source reaching one arrives as
    ///         an interface: the walk used to go through a BOXED interface enumerator into an un-sized
    ///         <c>List&lt;T&gt;</c> and then copy it, while the direct collection path has pre-sized since round 30.
    ///         Research P1 measured the gap at 1.07-1.95x for pre-sizing alone and 2.8-3.8x with a concrete fast
    ///         path, at 17-29 % more allocation.
    ///     </para>
    ///     <para>
    ///         The round-31 task specified inlining those fast paths into each emitted registration. Measured
    ///         before adopting it: the golden snapshot corpus grew <b>70 %</b> (113 KB to 193 KB), because the shape
    ///         is ~20 lines and there are six registrations per pair — 114 extra lines of generated source for a
    ///         single mapped pair, and a 500-pair application would carry tens of thousands. One generic helper here
    ///         gives the identical machine code (these are ordinary generics: the JIT specialises per type argument,
    ///         value-type instantiations get their own body) while the emitted registration stays one line. It is
    ///         also testable once, rather than six times per pair by inspecting strings.
    ///     </para>
    ///     <para>
    ///         Allocation-free of reflection and AOT-safe: no <c>GetType()</c>, no <c>MakeGenericType</c>, nothing a
    ///         trimmer cannot see. The two type tests are ordinary pattern matches the JIT resolves to a cast.
    ///     </para>
    /// </remarks>
    public static class DwarfCollectionMap
    {
        /// <summary>
        ///     Maps <paramref name="source" /> element-wise into a <see cref="List{T}" />, sized exactly when the
        ///     source's count is known without enumerating it.
        /// </summary>
        /// <param name="source">
        ///     The registered source, arriving as <see cref="object" /> because the registry's delegate is
        ///     <c>Func&lt;object, object&gt;</c>. Must be an <c>IEnumerable&lt;TSource&gt;</c>.
        /// </param>
        /// <param name="map">The element map — a direct call into the generated mapper.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design",
            "CA1002:Do not expose generic lists",
            Justification = "The return type IS the registry key's destination type. This overload exists because a "
                            + "pair registers `IEnumerable<S> -> List<D>`; handing back a Collection<D> or an "
                            + "IReadOnlyList<D> would not satisfy that registration, and the caller is generated code "
                            + "that casts to the registered destination. ToArray has the array twin for the same "
                            + "reason.")]
        public static List<TDestination> ToList<TSource, TDestination>(object source, Func<TSource, TDestination> map)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(map);

            // An array and a List are the shapes real callers pass. Both are indexable, so neither needs an
            // enumerator at all, and the destination is sized once.
            if (source is TSource[] array)
            {
                var fromArray = new List<TDestination>(array.Length);
                for (var i = 0; i < array.Length; i++)
                {
                    fromArray.Add(map(array[i]));
                }

                return fromArray;
            }

            if (source is List<TSource> list)
            {
                var span = CollectionsMarshal.AsSpan(list);
                var fromList = new List<TDestination>(span.Length);
                for (var i = 0; i < span.Length; i++)
                {
                    fromList.Add(map(span[i]));
                }

                return fromList;
            }

            var sequence = (IEnumerable<TSource>)source;
            var result = sequence.TryGetNonEnumeratedCount(out var count)
                ? new List<TDestination>(count)
                : new List<TDestination>();
            foreach (var element in sequence)
            {
                result.Add(map(element));
            }

            return result;
        }

        /// <summary>
        ///     <see cref="ToList{TSource,TDestination}" /> for an array destination. A counted source fills the array
        ///     directly; only a source of unknown length buffers into a list first.
        /// </summary>
        public static TDestination[] ToArray<TSource, TDestination>(object source, Func<TSource, TDestination> map)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(map);

            if (source is TSource[] array)
            {
                var fromArray = new TDestination[array.Length];
                for (var i = 0; i < array.Length; i++)
                {
                    fromArray[i] = map(array[i]);
                }

                return fromArray;
            }

            if (source is List<TSource> list)
            {
                var span = CollectionsMarshal.AsSpan(list);
                var fromList = new TDestination[span.Length];
                for (var i = 0; i < span.Length; i++)
                {
                    fromList[i] = map(span[i]);
                }

                return fromList;
            }

            var sequence = (IEnumerable<TSource>)source;
            if (sequence.TryGetNonEnumeratedCount(out var count))
            {
                var sized = new TDestination[count];
                var written = 0;
                foreach (var element in sequence)
                {
                    sized[written++] = map(element);
                }

                return sized;
            }

            var buffer = new List<TDestination>();
            foreach (var element in sequence)
            {
                buffer.Add(map(element));
            }

            return buffer.ToArray();
        }
    }
}
