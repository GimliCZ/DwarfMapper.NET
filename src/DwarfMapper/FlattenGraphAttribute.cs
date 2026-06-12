// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Collapses a transitively-reachable object graph starting from <see cref="SourceNavigation"/>
/// into a flat collection of distinct node DTOs at <see cref="TargetCollection"/> on the return type.
/// <para>
/// <b>Intentional topology degradation:</b> inter-node edges (members of the node type whose type is
/// the node type or a collection of the node type) are set to <c>null</c> in each mapped node DTO.
/// Only scalar and non-node members (leaf data) are preserved. The attribute name and this documentation
/// make the edge-drop explicit — opting into <c>[FlattenGraph]</c> IS the acknowledgment of topology loss.
/// </para>
/// <para>
/// The traversal is deterministic BFS, cycle-safe via a reference-identity visited set
/// (<see cref="System.Collections.Generic.ReferenceEqualityComparer"/>), and AOT-safe (no reflection).
/// Null entry → empty collection (no <see cref="System.NullReferenceException"/>).
/// </para>
/// <para>
/// <b>sourceNavigation</b>: Name of a member on the mapping parameter (root) type that enters the graph.
/// Its type must be the node type <c>TNode</c>, or a supported collection of <c>TNode</c>.
/// </para>
/// <para>
/// <b>targetCollection</b>: Name of a collection member on the return (root DTO) type whose element type
/// is the node DTO type. Supported target kinds: <c>List&lt;TNodeDto&gt;</c>, <c>TNodeDto[]</c>,
/// <c>IReadOnlyList&lt;TNodeDto&gt;</c>, <c>ICollection&lt;TNodeDto&gt;</c>,
/// <c>IReadOnlyCollection&lt;TNodeDto&gt;</c>, <c>IList&lt;TNodeDto&gt;</c>,
/// <c>IEnumerable&lt;TNodeDto&gt;</c>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class FlattenGraphAttribute : Attribute
{
    /// <summary>
    /// Creates a graph-flatten directive for the named navigation and target collection.
    /// </summary>
    /// <param name="sourceNavigation">Member name on the root source type entering the graph.</param>
    /// <param name="targetCollection">Collection member name on the root return type to populate.</param>
    public FlattenGraphAttribute(string sourceNavigation, string targetCollection)
    {
        SourceNavigation = sourceNavigation;
        TargetCollection = targetCollection;
    }

    /// <summary>Name of the member on the root source type that enters the graph.</summary>
    public string SourceNavigation { get; }

    /// <summary>Name of the flat-collection member on the root return type to populate.</summary>
    public string TargetCollection { get; }
}
