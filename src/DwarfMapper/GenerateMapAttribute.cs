// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Declares a mapping from <typeparamref name="TSource"/> to <typeparamref name="TTarget"/> on a
/// <c>[DwarfMapper]</c> partial class — WITHOUT writing a <c>partial</c> method per pair. The
/// generator emits a <c>public TTarget Map(TSource src)</c> method (one overload per declared pair)
/// with the same completeness gate, conversions, nested/collection handling, and hooks as a declared
/// partial mapper.
/// <para>
/// This keeps migration low-ceremony: the source/target types stay plain POCOs (no attributes on
/// them), and each mapping is a single attribute line — a near-mechanical replacement for, e.g.,
/// AutoMapper's <c>CreateMap&lt;A, B&gt;()</c>:
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
