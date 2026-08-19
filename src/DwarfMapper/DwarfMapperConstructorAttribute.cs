// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     Marks a constructor as the preferred target for DwarfMapper constructor-based mapping.
///     When present on exactly one constructor of the destination type, that constructor is
///     selected unconditionally (overriding the default selection policy). Placing the attribute
///     on more than one constructor is a build error (<c>DWARF025</c>).
/// </summary>
/// <remarks>
///     <para>
///         The default selection policy (when no constructor is annotated) is:
///         <list type="number">
///             <item>Parameterless constructor present → object-initializer mapping (no ctor args).</item>
///             <item>Exactly one non-parameterless constructor → use it.</item>
///             <item>Multiple non-parameterless constructors, unique maximum arity → use the longest.</item>
///             <item>
///                 Tie for maximum arity → <c>DWARF025 AmbiguousConstructor</c>; add <c>[DwarfMapperConstructor]</c> to
///                 resolve.
///             </item>
///         </list>
///     </para>
///     <para>
///         Every <em>mandatory</em> constructor parameter must be satisfied: if DwarfMapper cannot find a
///         source member for a non-optional, non-<c>params</c> parameter, it emits
///         <c>DWARF024 ConstructorParameterUnmapped</c>
///         (optional parameters and <c>params</c> arrays with no matching source are omitted and take their default)
///         and refuses to generate the mapping method.
///     </para>
/// </remarks>
// The flat pair is the shape that poses this element's question, and it now carries a
// ConstructorSlotMarker ahead of a second constructor to do it. The key used to be "internal-member",
// whose destination declares no constructor at all and whose BASELINE is DWARF001 — against it every
// Constructor-site cell could only ever have read UnhonouredButLoud, i.e. the fixture, not the generator,
// would have decided all seven. Measurement metadata, read only by the test projects; see DwarfSurfaceAttribute.
[DwarfSurface(SurfaceCategory.ConsumerDirective)]
[DwarfSurfaceSite(AttributeTargets.Constructor,
    SurfaceEndpoints.All & ~SurfaceEndpoints.UpdateInto,
    "An update-into writes into a destination THE CALLER ALREADY BUILT — `void Update(Src s, Dst d)` receives "
    + "the `Dst`, null-guards it and assigns its members — so for the pair that endpoint declares there is no "
    + "construction step, and 'which constructor should build this' is not a question it asks. That is a fact "
    + "about the shape of the endpoint's signature, not about what the generator currently reads: every other "
    + "endpoint builds its destination and every other endpoint honours this directive, measured. It is "
    + "narrowed rather than refused with a diagnostic BECAUSE the attribute sits on the destination TYPE, "
    + "which one codebase legitimately shares between a create map that honours it and an update-into that "
    + "has nothing to construct — a refusal there would fire on a correct declaration. (Contrast the "
    + "[MapTo] registry, which DOES construct and got DWARFR11.) Read this as scoped to the endpoint's own "
    + "destination and not as 'inert at an update-into': a NESTED destination member IS constructed there — "
    + "the update-into replaces it wholesale, DWARF065 — and the annotated constructor of that nested type "
    + "is called, measured and pinned by "
    + "ConstructorMappingTests.Annotated_ctor_is_honoured_for_a_nested_update_into_target. That construction "
    + "is the auto-synthesized CREATE map for the inner pair, which is the CreateMap cell of this matrix, "
    + "and the update-into merely calls it.")]
[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
public sealed class DwarfMapperConstructorAttribute : Attribute
{
}
