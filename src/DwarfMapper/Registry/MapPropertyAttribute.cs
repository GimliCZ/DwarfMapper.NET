// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Registry;

/// <summary>
/// EXPERIMENTAL (v23 prototype). Binds the annotated source member to a destination member. Stack these
/// (and <see cref="MapIgnoreAttribute"/>) on one member and they are read <b>in source order, aligned
/// positionally to the targets in <c>[MapTo(...)]</c></b>: the first attribute is the directive for the
/// first target, the second for the second target, and so on. A single attribute applies to every target.
/// Example: <c>[MapProperty("Name"), MapProperty("FullName")]</c> → <c>Name</c> in target 0, <c>FullName</c>
/// in target 1; <c>[MapProperty("Name"), MapIgnore]</c> → mapped in target 0, ignored in target 1.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
public sealed class MapPropertyAttribute : Attribute
{
    /// <summary>Binds this member to <paramref name="destinationMember"/> in the positionally-matched target.</summary>
    /// <param name="destinationMember">Name of the destination member this source member supplies.</param>
    public MapPropertyAttribute(string destinationMember) => DestinationMember = destinationMember;

    /// <summary>Name of the destination member this source member supplies.</summary>
    public string DestinationMember { get; }
}
