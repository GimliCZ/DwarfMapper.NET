// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     How a nullable <b>value-type</b> source mapped to a non-nullable destination is handled when null.
///     <para>
///     A nullable <b>reference</b> source is unaffected — it raw-assigns. That is deliberate, but it is not
///     silent: it reports <b>DWARF070</b>, so the null flowing into a non-nullable member is a build-time
///     warning you can act on. Guard it with <c>SkipNullSourceMembers</c> or <c>NullSubstitute</c>, or make the
///     destination member nullable.
///     </para>
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
