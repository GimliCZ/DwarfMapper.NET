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
//       [MapDerivedType] is here for the same reason and in BOTH of its forms: a dispatch arm decides which
//       destination TYPE to construct from the source's runtime type, so away from the create map a derived
//       instance was mapped as its base and every member the derived DTO declares beyond the base one was
//       dropped, silently (finding D8). The two applications below are written in the two different syntaxes
//       on purpose — the message quotes back the form the caller typed, not a normalized one.
// EXPECT: DWARF092
// EXPECT-MESSAGE DWARF092: [FlattenGraph("Root", "Flat")] on 'UpdateTree' is not read at the update-into endpoint
// EXPECT-MESSAGE DWARF092: [FlattenGraph("Root", "Flat")] on 'MapTrees' is not read at the span-map endpoint
// EXPECT-MESSAGE DWARF092: filled by ordinary direct mapping instead
// EXPECT-MESSAGE DWARF092: Declare it on a create map over the same pair
// EXPECT-MESSAGE DWARF092: so that create map is what this method's loop calls
// EXPECT-MESSAGE DWARF092: [MapDerivedType<PolyDerived, PolyDerivedDto>] on 'UpdatePoly'
// EXPECT-MESSAGE DWARF092: [MapDerivedType(typeof(PolyDerived), typeof(PolyDerivedDto))] on 'ProjectPoly'
// EXPECT-MESSAGE DWARF092: from the source's RUNTIME type, and only the create map constructs one
//
//       [ReverseMap] is the third, and the one whose message carries NO transfer claim even element-wise: it
//       does not change what the create map emits, it makes a SEPARATELY-DECLARED inverse inherit the forward
//       renames with their ends swapped. Written on an update-into, nothing looks for an inverse, nothing
//       inherits a rename, and no DWARF052 is raised either — that check lives on the same create-map path
//       (finding D13). The inverse update below is declared, and still does not inherit the rename.
// EXPECT-MESSAGE DWARF092: [ReverseMap] on 'UpdateRenamed' is not read at the update-into endpoint
// EXPECT-MESSAGE DWARF092: separately-declared inverse method
//
//       [MapCollectionKey] is the MIRROR IMAGE, and the reason this id is no longer named after the create
//       map. A key-based upsert merges the source elements into the List<T> the destination already holds,
//       so it needs a destination to merge INTO — and the create map, the projection, the span map and the
//       async stream all build a fresh one. Written on any of those four it was discarded and the collection
//       was rebuilt wholesale, which is what it would have been without the directive (finding D14). Its
//       message names the UPDATE-INTO as the home endpoint and carries no transfer claim anywhere: what an
//       element-wise loop adopts is a declared CREATE map for its element pair, and an update-into is not
//       one. DWARF074 already validated this directive at the update-into and its own documentation named
//       "not an update-into method" as a case it covered — no call site ever asked that question.
// EXPECT-MESSAGE DWARF092: [MapCollectionKey("Items", "Id")] on 'MapOrder' is not read at the create-map endpoint
// EXPECT-MESSAGE DWARF092: Declare it on an update-into over the same pair
// EXPECT-MESSAGE DWARF092: A key-based upsert MERGES the source elements

using System;
using System.Collections.Generic;
using System.Linq;
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

public class PolyBase
{
    public int Id { get; set; }
}

public sealed class PolyDerived : PolyBase
{
    public string? Extra { get; set; }
}

public class PolyBaseDto
{
    public int Id { get; set; }
}

public sealed class PolyDerivedDto : PolyBaseDto
{
    public string? Extra { get; set; }
}

public sealed class OrderLine
{
    public int Id { get; set; }

    public string? Label { get; set; }
}

public sealed class Order
{
    public int Id { get; set; }

    public List<OrderLine> Items { get; set; } = new();
}

public sealed class OrderDto
{
    public int Id { get; set; }

    public List<OrderLine> Items { get; set; } = new();
}

public sealed class RenamedSrc
{
    public int Id { get; set; }

    public string? A { get; set; }
}

public sealed class RenamedDst
{
    public int Id { get; set; }

    public string? B { get; set; }
}

[DwarfMapper]
public partial class DirectiveNotReadAtThisEndpointMapper
{
    [FlattenGraph("Root", "Flat")]
    public partial void UpdateTree(TreeRoot src, TreeRootDto dst);

    [FlattenGraph("Root", "Flat")]
    public partial void MapTrees(ReadOnlySpan<TreeRoot> src, Span<TreeRootDto> dst);

    [MapDerivedType<PolyDerived, PolyDerivedDto>]
    public partial void UpdatePoly(PolyBase src, PolyBaseDto dst);

    [MapDerivedType(typeof(PolyDerived), typeof(PolyDerivedDto))]
    public partial IQueryable<PolyBaseDto> ProjectPoly(IQueryable<PolyBase> q);

    [ReverseMap]
    [MapProperty("A", "B")]
    public partial void UpdateRenamed(RenamedSrc src, RenamedDst dst);

    [MapProperty("B", "A")]
    public partial void BackRenamed(RenamedDst dst, RenamedSrc src);

    [MapCollectionKey("Items", "Id")]
    public partial OrderDto MapOrder(Order src);
}
