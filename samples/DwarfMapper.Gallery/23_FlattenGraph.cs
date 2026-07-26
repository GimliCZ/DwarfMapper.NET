// SPDX-License-Identifier: GPL-2.0-only

// 23 — Collapsing a whole object GRAPH into a flat list. [FlattenGraph(root, target)] walks the graph
// breadth-first from the named root member and writes every node it reaches into the named list member.
// [MapDerivedType<S,D>] tells it how to map each concrete node type it meets, so a heterogeneous tree
// (folders and files) flattens without a visitor, a switch, or a cast.
//
// Note the topology is DEGRADED on purpose: what was a tree becomes a sequence. That is the point — it is
// for the wire format or the UI list, not a lossless copy of the graph. Use example 20's Preserve mode when
// the shape itself has to survive.

namespace DwarfMapper.Gallery.Ex23;

public abstract class Chamber
{
    public string Name { get; set; } = "";
}

public sealed class Hall : Chamber
{
    public List<Chamber> Children { get; set; } = [];
}

public sealed class Vault : Chamber
{
    public long Depth { get; set; }
}

public abstract class ChamberDto
{
    public string Name { get; set; } = "";
}

public sealed class HallDto : ChamberDto
{
    public List<ChamberDto>? Children { get; set; }
}

public sealed class VaultDto : ChamberDto
{
    public long Depth { get; set; }
}

public sealed class Mine
{
    public Chamber? Root { get; set; }
    public string Label { get; set; } = "";
}

public sealed class MineDto
{
    public List<ChamberDto> Nodes { get; set; } = [];
    public string Label { get; set; } = "";
}

// <snippet: flatten-graph>
[DwarfMapper]
public partial class Mapper
{
    [FlattenGraph(nameof(Mine.Root), nameof(MineDto.Nodes))]   // walk the graph, fill the list
    [MapDerivedType<Hall, HallDto>]                            // ...mapping each node type it meets
    [MapDerivedType<Vault, VaultDto>]
    public partial MineDto ToDto(Mine m);
}
// </snippet>

[DocExample(23, Tier.Advanced, "`[FlattenGraph]` — a graph becomes a list",
    Shows = "breadth-first graph collapse with per-node-type mapping")]
public static class Example
{
    public static void Run()
    {
        var dto = new Mapper().ToDto(new Mine
        {
            Label = "Moria",
            Root = new Hall
            {
                Name = "Twenty-first Hall",
                Children = [new Vault { Name = "Deep Vault", Depth = 2100 }]
            }
        });

        Console.WriteLine(
            $"23 [FlattenGraph]     -> {dto.Label}: {dto.Nodes.Count} nodes "
            + $"[{string.Join(", ", dto.Nodes.Select(n => n.Name))}]");
    }
}
