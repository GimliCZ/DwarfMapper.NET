// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>How the emitter unwraps a nullable value-type source for a non-nullable destination.</summary>
public enum NullHandling
{
    /// <summary>Assign directly.</summary>
    None = 0,

    /// <summary>Emit <c>x ?? throw</c>.</summary>
    ThrowIfNull = 1,

    /// <summary>Emit <c>x.GetValueOrDefault()</c>.</summary>
    ValueOrDefault = 2,

    /// <summary>
    /// Source and target are both <c>Nullable&lt;T&gt;</c>; emit
    /// <c>x.HasValue ? Conv(x.Value) : null</c> (null-preserving).
    /// </summary>
    NullableProject = 3,
}
