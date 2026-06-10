// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Per-method override for the <see cref="DwarfMapperAttribute.AutoNest"/> class-level setting.
/// Applying <c>[AutoNest(false)]</c> to a single mapping method disables auto-synthesis of nested
/// object mappers for that method, even when the enclosing class has <c>AutoNest = true</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AutoNestAttribute : Attribute
{
    /// <summary>
    /// Initialises the attribute with the given auto-nest value.
    /// </summary>
    /// <param name="enabled">
    /// <c>true</c> to enable auto-nesting for this method; <c>false</c> to disable it.
    /// </param>
    public AutoNestAttribute(bool enabled = true)
    {
        Enabled = enabled;
    }

    /// <summary>Gets whether auto-nesting is enabled for this method.</summary>
    public bool Enabled { get; }
}
