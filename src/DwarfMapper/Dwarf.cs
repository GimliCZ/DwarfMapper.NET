// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper
{
    /// <summary>
    ///     The statically-bound twin of <see cref="IDwarfMapper" />: <c>Dwarf.Map&lt;Order, OrderDto&gt;(order)</c>.
    ///     When the calling assembly itself registers the pair, the generator binds the call at compile time to the
    ///     generated mapper - a direct call, no registry lookup. Every other call resolves through
    ///     <see cref="DwarfMapperRegistry" /> exactly as the facade does.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why a static class and not the interface.</b> Binding a call at compile time replaces its target.
    ///         Done to <see cref="IDwarfMapper" />, that would bypass whatever implementation the caller injected - a
    ///         logging decorator, a test double - and the substitution would be silent. A static call has no receiver,
    ///         so there is nothing to bypass: this class exists so that the compile-time binding can never change
    ///         WHICH code runs, only how directly it is reached. <see cref="IDwarfMapper" /> keeps its semantics.
    ///     </para>
    ///     <para>
    ///         <b>What is bound, and what is not.</b> A call is bound only when the pair is registered by the SAME
    ///         assembly the call is in - and, for a create map, by no assembly it references - which is the case where
    ///         the registry hands back that assembly's own delegate. The bound call runs the expression the registration
    ///         runs, on the same mapper instance. A pair declared in another assembly, or reached through a base type or
    ///         an interface, is resolved at run time by the bodies below.
    ///     </para>
    ///     <para>
    ///         One case can differ: an update-into pair registered by this assembly AND by another one. Update maps
    ///         carry no manifest, so the generator cannot see the other registration; the bound call uses this
    ///         assembly's map, while the registry answers with whichever registered first.
    ///         <see cref="DwarfMapperRegistry.IsUpdateAmbiguous" /> reports that pair either way.
    ///     </para>
    ///     <para>
    ///         The compile-time binding uses C# interceptors. The package's <c>build/DwarfMapper.props</c> enables
    ///         them for the <c>DwarfMapper.Generated</c> namespace only; nothing needs to be set in the consuming
    ///         project.
    ///     </para>
    /// </remarks>
    public static class Dwarf
    {
        /// <summary>
        ///     Maps <paramref name="source" /> to <typeparamref name="TDestination" /> using the static source type:
        ///     the exact <c>(TSource, TDestination)</c> pair when one is registered, otherwise the runtime type of
        ///     <paramref name="source" /> (see <see cref="DwarfMapperRegistry.Map" />).
        /// </summary>
        public static TDestination Map<TSource, TDestination>(TSource source)
        {
            // The exact pair first: `Map<Base, Dto>(derived)` must use the (Base, Dto) map the caller asked for, not
            // dispatch on the derived runtime type. Only a miss falls through to the runtime-type walk.
            if (DwarfMapperRegistry.TryGet(typeof(TSource), typeof(TDestination), out var map))
            {
                return (TDestination)map(source!);
            }

            return (TDestination)DwarfMapperRegistry.Map(source!, typeof(TDestination));
        }

        /// <summary>
        ///     Maps <paramref name="source" /> ONTO the existing <paramref name="destination" />, preserving its
        ///     identity - the update-into shape, resolved from the exact <c>(TSource, TDestination)</c> pair (see
        ///     <see cref="DwarfMapperRegistry.Update" />).
        /// </summary>
        public static void Map<TSource, TDestination>(TSource source, TDestination destination)
        {
            DwarfMapperRegistry.Update(source!, destination!, typeof(TSource), typeof(TDestination));
        }
    }
}
