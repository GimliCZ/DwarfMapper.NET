// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>
/// A resolved [FlattenGraph] directive stored in <see cref="MapMethodModel"/>.
/// Carries the synthesized helper names and collection-conversion suffix needed for emission.
/// </summary>
/// <param name="SourceNavigation">Member name on the root source type entering the graph.</param>
/// <param name="TargetCollection">Collection member name on the root return type to populate.</param>
/// <param name="TraversalHelperName">The <c>__DwarfMap_FlattenGraph_HASH</c> helper name.</param>
/// <param name="ConverterHelperName">
/// The outer converter helper name. For List/interface targets this equals
/// <see cref="TraversalHelperName"/>; for array targets this is an
/// <c>__DwarfMap_FlattenGraphArr_HASH</c> thin wrapper that calls <c>.ToArray()</c>.
/// </param>
public sealed record FlattenGraphDirective(
    string SourceNavigation,
    string TargetCollection,
    string TraversalHelperName,
    string ConverterHelperName
) : System.IEquatable<FlattenGraphDirective>;
