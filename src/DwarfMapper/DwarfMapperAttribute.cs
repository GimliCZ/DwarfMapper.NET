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

    /// <summary>
    /// Controls how shared object references and cycles in the source graph are handled.
    /// <para>
    /// <see cref="ReferenceHandlingStrategy.None"/> (default): no identity map. Recursion-capable
    /// pairs still get a depth counter — cyclic data throws <see cref="DwarfMappingDepthException"/>
    /// at <see cref="MaxDepth"/>. Zero allocation overhead.
    /// </para>
    /// <para>
    /// <see cref="ReferenceHandlingStrategy.Preserve"/>: full topology reconstruction — every distinct
    /// source node mapped once, all edges (shared/cycle) relinked. One small dictionary allocation
    /// per top-level <c>Map</c> call (keyed by reference identity via
    /// <c>ReferenceEqualityComparer.Instance</c>).
    /// </para>
    /// </summary>
    public ReferenceHandlingStrategy ReferenceHandling { get; set; } = ReferenceHandlingStrategy.None;

    /// <summary>
    /// Maximum recursion depth for recursion-capable auto-synthesized mappers (default 64).
    /// When a mapping reaches this depth, a <see cref="DwarfMappingDepthException"/> is thrown
    /// instead of a silent (uncatchable) <see cref="System.StackOverflowException"/>.
    /// <para>
    /// The depth counter applies only to pairs that can form a cycle in the type graph
    /// (self-referential or mutually-recursive types). Acyclic type graphs have zero overhead.
    /// </para>
    /// <para>
    /// Hard cap: <c>1000</c>. Values above 1000 are silently clamped to 1000.
    /// Default: <c>64</c> (matching System.Text.Json / AutoMapper defaults).
    /// </para>
    /// </summary>
    public int MaxDepth { get; set; } = 64;
}
