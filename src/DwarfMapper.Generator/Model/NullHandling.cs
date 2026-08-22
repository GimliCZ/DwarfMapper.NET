// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Model;

/// <summary>How the emitter handles a possibly-null source value on its way to the destination.</summary>
public enum NullHandling
{
    /// <summary>Assign directly.</summary>
    None = 0,

    /// <summary>Emit <c>x ?? throw</c>.</summary>
    ThrowIfNull = 1,

    /// <summary>Emit <c>x.GetValueOrDefault()</c>.</summary>
    ValueOrDefault = 2,

    /// <summary>
    ///     Source is <c>Nullable&lt;T&gt;</c> and the target can hold null (it is <c>Nullable&lt;U&gt;</c> or a
    ///     nullable-annotated reference); emit <c>x.HasValue ? Conv(x.Value) : null</c> (null-preserving).
    /// </summary>
    NullableProject = 3
}
