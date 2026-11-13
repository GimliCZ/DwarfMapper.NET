// SPDX-License-Identifier: GPL-2.0-only
// CASE: a [FlattenGraph] method whose node has a member that leads back to the method - the node points at the
//       object that holds the graph, and that object's map flattens the graph again.
// WHY:  Round 35 (T17). The walk a flatten method emits tracks the nodes it has visited and nothing else. A node
//       member that is not an edge of the graph is converted by an ordinary call from inside the flat-node helper;
//       when that call leads back to the flatten method, the walk is entered again from inside itself with a fresh
//       visited set and no depth. Measured, one process per case: data that closes the loop ended in a stack overflow
//       without reference handling, under Preserve and under SetNull alike - a process death, not an exception. The
//       flat-node helper is emitted as text with one parameter, so no context can be handed through it; the shape is
//       refused where it is declared.
// EXPECT: DWARF118, DWARF078
// EXPECT-MESSAGE DWARF118: [FlattenGraph] on 'Flatten': the node member 'Owner' is converted by 'Map', which leads back to 'Flatten'
// EXPECT-CS: CS8795

using System.Collections.Generic;
using DwarfMapper;

namespace Demo;

public class Node
{
    public int Id { get; set; }
    public Node? Next { get; set; }
    public Holder? Owner { get; set; }
}

public class NodeDto
{
    public int Id { get; set; }
    public int? NextId { get; set; }
    public HolderDto? Owner { get; set; }
}

public class Graph
{
    public Node? Entry { get; set; }
}

public class GraphDto
{
    public List<NodeDto> Nodes { get; set; } = new();
}

public class Holder
{
    public Graph Inner { get; set; } = new();
}

public class HolderDto
{
    public GraphDto Inner { get; set; } = new();
}

[DwarfMapper]
public partial class M
{
    [FlattenGraph(nameof(Graph.Entry), nameof(GraphDto.Nodes))]
    public partial GraphDto Flatten(Graph graph);

    public partial HolderDto Map(Holder holder);
}
