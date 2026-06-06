// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Explicitly excludes a destination member from completeness checking and
/// mapping. Required to silence DWARF001 for an intentionally unmapped member.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class MapIgnoreAttribute : Attribute
{
    /// <summary>Initialises a new instance of <see cref="MapIgnoreAttribute"/>.</summary>
    /// <param name="targetMember">Name of the destination member to ignore.</param>
    public MapIgnoreAttribute(string targetMember) => TargetMember = targetMember;

    /// <summary>Name of the destination member to ignore.</summary>
    public string TargetMember { get; }
}
