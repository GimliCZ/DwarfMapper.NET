// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Marks a partial class as a DwarfMapper. The generator implements the
/// partial mapping methods declared on it at compile time.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DwarfMapperAttribute : Attribute
{
    /// <summary>
    /// When <c>true</c>, source and destination member names are matched
    /// case-insensitively (ordinal-ignore-case). Defaults to <c>false</c>
    /// (exact, case-sensitive matching).
    /// </summary>
    public bool CaseInsensitive { get; set; }

    /// <summary>
    /// Strategy for enum-to-enum mapping. Defaults to <see cref="EnumStrategy.ByName"/>.
    /// </summary>
    public EnumStrategy EnumStrategy { get; set; } = EnumStrategy.ByName;

    /// <summary>
    /// How a nullable value-type source mapped to a non-nullable destination is
    /// handled when null. Defaults to <see cref="NullStrategy.Throw"/>.
    /// </summary>
    public NullStrategy NullStrategy { get; set; } = NullStrategy.Throw;

    /// <summary>
    /// When <c>true</c> (the default), a member whose type is a mappable object pair
    /// <c>(S, T)</c> with no declared mapper is automatically resolved by synthesizing
    /// a private nested mapper. Set to <c>false</c> to require explicit declarations
    /// for every nested type (today's behavior).
    /// </summary>
    public bool AutoNest { get; set; } = true;

    /// <summary>
    /// Controls how a null source collection or dictionary is mapped.
    /// <para>
    /// <see cref="NullCollectionStrategy.AsEmpty"/> (default): a null source
    /// produces an empty target; the mapper never throws <see cref="System.NullReferenceException"/>
    /// for a null collection.
    /// </para>
    /// <para>
    /// <see cref="NullCollectionStrategy.AsNull"/>: a null source propagates as
    /// <c>null</c> on the target. The target member must be a nullable reference type.
    /// </para>
    /// </summary>
    public NullCollectionStrategy NullCollections { get; set; } = NullCollectionStrategy.AsEmpty;
}
