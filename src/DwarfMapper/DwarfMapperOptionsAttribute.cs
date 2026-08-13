// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     Assembly-wide DwarfMapper options. Apply once with <c>[assembly: DwarfMapperOptions(...)]</c>.
/// </summary>
/// <remarks>
///     <c>AppliesTo</c> omits the update-into, projection, span and async-stream endpoints because the only
///     thing this attribute governs is the accessibility of the generated convenience extension, and that
///     extension is create-shaped: it is the <c>source.ToTarget()</c> form. An update mutates an instance it
///     is handed, a projection emits an expression tree, and the span and stream overloads are generated per
///     MAPPER rather than per overload — none of the four produces an extension whose accessibility there is
///     to decide. Same shape of reason, and the same four endpoints, as the <c>GenerateExtensions</c> rows in
///     <c>OptionGaps.StructurallyInapplicable</c>. <see cref="SurfaceEndpoints.Registry" /> is deliberately still
///     CLAIMED: the registry DOES emit an extension class (<c>__DwarfRegistry_Src</c>, with a public/internal
///     choice of its own), so the option has something to govern there and simply does not — that is a
///     divergence to fix, not a shape to declare away.
/// </remarks>
[DwarfSurface(SurfaceCategory.EmissionShape,
    AppliesTo = SurfaceEndpoints.CreateMap | SurfaceEndpoints.Registry | SurfaceEndpoints.CoLocatedHost)]
[DwarfSurfaceProbe(constructorArity: 0,
    Unmeasured = "a bare [assembly: DwarfMapperOptions] selects the default accessibility, which is what the "
                 + "generator emits with no attribute at all — silent by construction. The PublicExtensions "
                 + "case above is where this element's one question lives.")]
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DwarfMapperOptionsAttribute : Attribute
{
    /// <summary>
    ///     When <c>true</c>, the generated convenience extension methods (in the <c>DwarfMapper.Extensions</c>
    ///     namespace) are emitted <c>public</c> — usable from <b>other assemblies</b> — for every pair whose source
    ///     <b>and</b> destination types are both effectively public. A pair involving a non-public type stays
    ///     assembly-internal to remain accessibility-safe (a public extension over an internal type would not
    ///     compile). Defaults to <c>false</c> (all generated extensions are assembly-internal).
    ///     <para>
    ///         This is the opt-in for the common layered layout where mappers/DTOs live in a library and are consumed
    ///         from another project. Without it, call the mapper instance method or use <c>AddDwarfMappers()</c> DI
    ///         across assemblies.
    ///     </para>
    /// </summary>
    public bool PublicExtensions { get; set; }
}
