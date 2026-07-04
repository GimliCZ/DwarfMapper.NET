// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Declares a mapping from <typeparamref name="TSource"/> to <typeparamref name="TTarget"/> WITHOUT
/// writing a <c>partial</c> method per pair. Both placements get the same completeness gate,
/// conversions, nested/collection handling, and hooks as a declared partial mapper — they differ only
/// in <i>where</i> the generated method lands.
/// <para>
/// <b>Mode 1 — on a <c>[DwarfMapper]</c> partial class.</b> The generator emits
/// <c>public TTarget Map(TSource src)</c> overloads (one per declared pair) into that class. This keeps
/// migration low-ceremony: the source/target types stay plain POCOs (no attributes on them), and each
/// mapping is a single attribute line — a near-mechanical replacement for, e.g., AutoMapper's
/// <c>CreateMap&lt;A, B&gt;()</c>:
/// </para>
/// <code>
/// [DwarfMapper]
/// [GenerateMap&lt;Order, OrderDto&gt;]
/// [GenerateMap&lt;Customer, CustomerDto&gt;]
/// public partial class Mappers { }
///
/// // usage (overload resolved by the source type):
/// OrderDto dto = new Mappers().Map(order);
/// </code>
/// <para>
/// <b>Mode 2 — co-located on a plain class (no <c>[DwarfMapper]</c>, no <c>partial</c>).</b> Placed on a
/// host that is not a <c>[DwarfMapper]</c> mapper, the mapping is emitted into a SEPARATE generated
/// <c>&lt;Host&gt;Mapper</c> type (the host stays a plain class) and is consumed via the generated
/// convenience extension (<c>src.ToTarget()</c>) or <c>AddDwarfMappers()</c> DI. This lets a mapping
/// live next to the type it targets and disappear when that type is deleted. A name clash with an
/// existing <c>&lt;Host&gt;Mapper</c> type is reported as <c>DWARF057</c>.
/// </para>
/// <code>
/// // PersonDto.cs — the mapping lives ON the DTO, no [DwarfMapper], no partial:
/// [GenerateMap&lt;Person, PersonDto&gt;]
/// public sealed class PersonDto { /* ... */ }
///
/// // usage via the generated extension:
/// PersonDto dto = person.ToPersonDto();
/// </code>
/// <para>
/// Generated <c>Map</c> overloads are distinguished by source type; declaring two pairs with the same
/// source type but different targets is a compile error (ambiguous overload) — declare those as named
/// <c>partial</c> methods instead.
/// </para>
/// </summary>
/// <typeparam name="TSource">The source type to map from.</typeparam>
/// <typeparam name="TTarget">The destination type to map to.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class GenerateMapAttribute<TSource, TTarget> : Attribute
{
}
