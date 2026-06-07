// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Explicitly maps a source member to a differently-named destination member,
/// overriding name-based matching for that destination. Apply to a mapping method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class MapPropertyAttribute : Attribute
{
    /// <summary>Creates an explicit member mapping.</summary>
    /// <param name="source">Name of the source member to read from.</param>
    /// <param name="target">Name of the destination member to assign.</param>
    public MapPropertyAttribute(string source, string target)
    {
        Source = source;
        Target = target;
    }

    /// <summary>Name of the source member to read from.</summary>
    public string Source { get; }

    /// <summary>Name of the destination member to assign.</summary>
    public string Target { get; }

    /// <summary>
    /// Optional name of a conversion method declared in the mapper class used to
    /// transform the source value into the destination type. The method must take
    /// the source member type and return the destination member type.
    /// </summary>
    public string? Use { get; set; }
}
