// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>
/// A resolved [FlattenGraph] directive stored in <see cref="MapMethodModel"/>.
/// Carries the synthesized helper names and collection-conversion suffix needed for emission.
/// </summary>
public sealed record FlattenGraphDirective(
    /// <summary>Member name on the root source type entering the graph.</summary>
    string SourceNavigation,
    /// <summary>Collection member name on the root return type to populate.</summary>
    string TargetCollection,
    /// <summary>The <c>__DwarfMap_FlattenGraph_HASH</c> helper name.</summary>
    string TraversalHelperName,
    /// <summary>
    /// The outer converter helper name. For List/interface targets this equals
    /// <see cref="TraversalHelperName"/>; for array targets this is an
    /// <c>__DwarfMap_FlattenGraphArr_HASH</c> thin wrapper that calls <c>.ToArray()</c>.
    /// </summary>
    string ConverterHelperName
) : System.IEquatable<FlattenGraphDirective>;
