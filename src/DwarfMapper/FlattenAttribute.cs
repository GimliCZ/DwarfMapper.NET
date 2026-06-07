// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Flattens a complex source member: its readable sub-members are mapped to
/// destination members of the same name (e.g. <c>Address.City → City</c>). Apply to a mapping method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class FlattenAttribute : Attribute
{
    /// <summary>Creates a flatten directive for the named source member.</summary>
    /// <param name="sourceMember">Name of the complex source member to flatten.</param>
    public FlattenAttribute(string sourceMember) => SourceMember = sourceMember;

    /// <summary>Name of the complex source member whose sub-members are pulled up.</summary>
    public string SourceMember { get; }
}
