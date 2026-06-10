// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Marks a constructor as the preferred target for DwarfMapper constructor-based mapping.
/// When present on exactly one constructor of the destination type, that constructor is
/// selected unconditionally (overriding the default selection policy). Placing the attribute
/// on more than one constructor is a build error (<c>DWARF025</c>).
/// </summary>
/// <remarks>
/// <para>
/// The default selection policy (when no constructor is annotated) is:
/// <list type="number">
///   <item>Parameterless constructor present → object-initializer mapping (no ctor args).</item>
///   <item>Exactly one non-parameterless constructor → use it.</item>
///   <item>Multiple non-parameterless constructors, unique maximum arity → use the longest.</item>
///   <item>Tie for maximum arity → <c>DWARF025 AmbiguousConstructor</c>; add <c>[DwarfMapperConstructor]</c> to resolve.</item>
/// </list>
/// </para>
/// <para>
/// Every constructor parameter is <strong>mandatory</strong>: if DwarfMapper cannot find a
/// source member to satisfy a parameter, it emits <c>DWARF024 ConstructorParameterUnmapped</c>
/// and refuses to generate the mapping method.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
public sealed class DwarfMapperConstructorAttribute : Attribute { }
