// SPDX-License-Identifier: GPL-2.0-only
// CASE: [FlattenGraph] written on a mapping method that is not a create map
// WHY:  A graph flatten replaces the source of a DESTINATION COLLECTION with a breadth-first walk of a
//       source navigation. Only the create map resolves one, because only the create map constructs the
//       destination it fills. Written on an update-into, a projection, a span map or an async-stream map the
//       directive was read by nobody and reported by nobody: the collection was filled by ordinary direct
//       mapping instead, so the identical declaration on the identical mapper produced a walked graph on one
//       overload and a shallow copy on the next four (surface-matrix finding D11).
//
//       Refused rather than honoured. The three directives this gate covers are all about the destination the
//       create map BUILDS, and an update-into writes into an instance the caller already constructed. The
//       remedy is the same declaration on a create map over the same pair — and at the two ELEMENT-WISE
//       endpoints that is not merely advice: a span or stream map adopts a declared mapping method for its
//       element pair, so the create map carrying the directive is what its loop calls.
// EXPECT: DWARF092
// EXPECT-MESSAGE DWARF092: [FlattenGraph("Root", "Flat")] on 'UpdateTree' is not read at the update-into endpoint
// EXPECT-MESSAGE DWARF092: [FlattenGraph("Root", "Flat")] on 'MapTrees' is not read at the span-map endpoint
// EXPECT-MESSAGE DWARF092: filled by ordinary direct mapping instead
// EXPECT-MESSAGE DWARF092: Declare it on a create map over the same pair
// EXPECT-MESSAGE DWARF092: so that create map is what this method's loop calls

using System;
using System.Collections.Generic;
using DwarfMapper;

namespace Demo;

public sealed class GraphNode
{
    public int Id { get; set; }

    public List<GraphNode> Children { get; set; } = new();
}

public sealed class GraphNodeDto
{
    public int Id { get; set; }
}

public sealed class TreeRoot
{
    public int Id { get; set; }

    public GraphNode? Root { get; set; }

    public List<GraphNode> Flat { get; set; } = new();
}

public sealed class TreeRootDto
{
    public int Id { get; set; }

    public List<GraphNodeDto> Flat { get; set; } = new();
}

[DwarfMapper]
public partial class CreateMapOnlyDirectiveMapper
{
    [FlattenGraph("Root", "Flat")]
    public partial void UpdateTree(TreeRoot src, TreeRootDto dst);

    [FlattenGraph("Root", "Flat")]
    public partial void MapTrees(ReadOnlySpan<TreeRoot> src, Span<TreeRootDto> dst);
}
