// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     How a nullable <b>value-type</b> source mapped to a non-nullable destination is handled when null. (A nullable
///     reference source is unaffected — it raw-assigns; guard with <c>SkipNullSourceMembers</c> or <c>NullSubstitute</c>.)
/// </summary>
public enum NullStrategy
{
    /// <summary>Throw at runtime if the source value is null (default).</summary>
    Throw = 0,

    /// <summary>
    ///     Use the destination type's default value when a nullable value-type source is null (does not apply to nullable
    ///     reference sources).
    /// </summary>
    SetDefault = 1
}
