// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper
{
    /// <summary>
    ///     Excludes a member from mapping. Two placements:
    ///     <list type="bullet">
    ///         <item>
    ///             On a <b>mapping method or class</b> (class model): <c>[MapIgnore(destination)]</c> excludes
    ///             that destination from completeness checking (silences <c>DWARF001</c>).
    ///         </item>
    ///         <item>
    ///             On a <b>source member</b> (the <c>[MapTo]</c> registry): <c>[MapIgnore]</c> never reads the
    ///             annotated member. Stack with <see cref="MapPropertyAttribute" /> for per-target map/ignore (positional).
    ///         </item>
    ///     </list>
    /// </summary>
    [DwarfSurface(SurfaceCategory.ConsumerDirective)]
    [DwarfSurfaceSite(AttributeTargets.Property | AttributeTargets.Field,
        SurfaceEndpoints.Registry | SurfaceEndpoints.CoLocatedHost,
        "The member-placement form IS the registry form: the no-target constructor says 'never read THE ANNOTATED " +
        "MEMBER', which only means anything where the annotated type is itself the declaration of the mapping. " +
        "That holds at Registry ([MapTo] on the source type) and at CoLocatedHost ([GenerateMap<S,T>] on the " +
        "target type), so both stay claimed. At the other five endpoints the mapping is declared by a partial " +
        "method on a SEPARATE [DwarfMapper] class, which is also where the method/class form's completeness " +
        "obligation lives; a member of the DTO pair — two ordinary types the consumer may not even own — is not " +
        "part of that declaration and so has no destination set to exclude anything from. The two placements are " +
        "two features sharing a name, not one feature the generator happens to read in one place.")]
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field,
        AllowMultiple = true,
        Inherited = false)]
    public sealed class MapIgnoreAttribute : Attribute
    {
        /// <summary>Member-placement form (the <c>[MapTo]</c> registry): never read the annotated member.</summary>
        public MapIgnoreAttribute()
        {
            Target = null;
        }

        /// <summary>Method/class-placement form (class model): ignore the destination member named <paramref name="target" />.</summary>
        /// <param name="target">Name of the destination member to ignore.</param>
        public MapIgnoreAttribute(string target)
        {
            Target = target;
        }

        /// <summary>
        ///     Name of the destination member to ignore (method/class form); <c>null</c> for the member form.
        ///     (Was <c>TargetMember</c> before 1.0; renamed for consistency with <see cref="MapPropertyAttribute" />.)
        /// </summary>
        public string? Target { get; }
    }
}
