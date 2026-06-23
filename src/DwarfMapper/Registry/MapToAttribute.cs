// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Registry;

/// <summary>
/// EXPERIMENTAL (v23 prototype). Declares that the annotated plain type maps to one or more
/// destination types, without writing a <c>partial</c> mapper class. The generator emits static
/// extension methods (<c>source.MapTo&lt;TTarget&gt;()</c> / <c>source.To{Target}()</c>) for each
/// declared target. Per-member configuration uses the member-level
/// <see cref="MapPropertyAttribute"/> / <see cref="MapIgnoreAttribute"/> in this namespace.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class MapToAttribute : Attribute
{
    /// <summary>Declares the destination types this source maps to.</summary>
    /// <param name="targets">One or more destination types — e.g. <c>[MapTo(typeof(A), typeof(B))]</c>.</param>
    public MapToAttribute(params Type[] targets) => Targets = targets ?? Array.Empty<Type>();

    /// <summary>The destination types this source maps to.</summary>
    public Type[] Targets { get; }
}
