// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Forces a reinterpret blit (vectorized memmove) for an unmanaged array→array member, even when the
/// automatic layout proof is too conservative to confirm it. You assert the field correspondence;
/// the generator still requires unmanaged elements and emits a runtime element-size guard, so a size
/// mismatch throws rather than corrupting memory. Apply to a mapping method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ReinterpretAttribute : Attribute
{
    /// <summary>Creates a forced-blit directive for the named destination member.</summary>
    /// <param name="member">The array member to blit.</param>
    public ReinterpretAttribute(string member) => Member = member;

    /// <summary>Name of the destination array member to blit.</summary>
    public string Member { get; }
}
