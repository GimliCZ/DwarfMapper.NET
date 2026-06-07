// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper;

/// <summary>How a nullable source value mapped to a non-nullable destination is handled when null.</summary>
public enum NullStrategy
{
    /// <summary>Throw at runtime if the source value is null (default).</summary>
    Throw = 0,

    /// <summary>Use the destination type's default value when the source is null.</summary>
    SetDefault = 1,
}
