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
[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
public sealed class DwarfMapperConstructorAttribute : Attribute
{
}
