// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

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
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = true, Inherited = false)]
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
