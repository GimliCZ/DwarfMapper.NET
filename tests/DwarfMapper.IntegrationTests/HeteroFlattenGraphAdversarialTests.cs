// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

// ── 4-level hetero hierarchy ──────────────────────────────────────────────────

public abstract class H4Node
{
    public string Name { get; set; } = "";
    public H4Node? Parent { get; set; }
}

public class H4Root : H4Node
{
    public List<H4Node> Children { get; set; } = new();
}

public class H4Branch : H4Node
{
    public List<H4Node> Children { get; set; } = new();
}

public class H4Leaf : H4Node
{
    public int Data { get; set; }
}

public abstract class H4NodeDto
{
    public string Name { get; set; } = "";
}

public class H4RootDto : H4NodeDto
{
    public List<H4NodeDto>? Children { get; set; }
    public H4NodeDto? Parent { get; set; }
}

public class H4BranchDto : H4NodeDto
{
    public List<H4NodeDto>? Children { get; set; }
    public H4NodeDto? Parent { get; set; }
}

public class H4LeafDto : H4NodeDto
{
    public int Data { get; set; }
    public H4NodeDto? Parent { get; set; }
}

public class H4Tree
{
    public H4Node? Entry { get; set; }
}

public class H4TreeDto
{
    public List<H4NodeDto> Nodes { get; set; } = new();
}

[DwarfMapper]
public partial class H4Mapper
{
    [FlattenGraph(nameof(H4Tree.Entry), nameof(H4TreeDto.Nodes))]
    [MapDerivedType<H4Root, H4RootDto>]
    [MapDerivedType<H4Branch, H4BranchDto>]
    [MapDerivedType<H4Leaf, H4LeafDto>]
    public partial H4TreeDto Map(H4Tree t);
}

public class HeteroFlattenGraphGoldenFixtureTests
{
    private readonly H4Mapper _m = new();

    // ── 1. 4-level hetero tree: root → 2 branches → 3 leaves each = 9 nodes ─
    [Fact]
    public void FourLevel_hetero_tree_all_9_nodes_collected_correct_types()
    {
        var leaves = Enumerable.Range(0, 6).Select(i => new H4Leaf { Name = $"L{i}", Data = i * 10 }).ToArray();
        var b1 = new H4Branch { Name = "B1" };
        var b2 = new H4Branch { Name = "B2" };
        b1.Children.AddRange(leaves.Take(3));
        b2.Children.AddRange(leaves.Skip(3));
        var root = new H4Root { Name = "Root" };
        root.Children.Add(b1);
        root.Children.Add(b2);

        var result = _m.Map(new H4Tree { Entry = root });

        Assert.Equal(9, result.Nodes.Count);
        Assert.Single(result.Nodes.OfType<H4RootDto>());
        Assert.Equal(2, result.Nodes.OfType<H4BranchDto>().Count());
        Assert.Equal(6, result.Nodes.OfType<H4LeafDto>().Count());

        // Leaf values preserved
        var leafDtos = result.Nodes.OfType<H4LeafDto>().OrderBy(l => l.Data).ToList();
        Assert.Equal(new[] { 0, 10, 20, 30, 40, 50 }, leafDtos.Select(l => l.Data).ToArray());

        // All edges degraded
        Assert.All(result.Nodes.OfType<H4RootDto>(), n => Assert.Null(n.Children));
        Assert.All(result.Nodes.OfType<H4BranchDto>(), n => Assert.Null(n.Children));
        Assert.All(result.Nodes.OfType<H4LeafDto>(), n => Assert.Null(n.Parent));
    }

    // ── 2. MapDerivedType 4-level class hierarchy ─────────────────────────────
    // Uses DrvFourArmMapper from MapDerivedTypeRuntimeTests.cs
    [Fact]
    public void FourArm_hierarchy_all_levels_dispatch_correctly()
    {
        var m = new DrvFourArmMapper();

        // Test all four levels
        Assert.IsType<DrvLevel3Dto>(m.Map(new DrvLevel3 { Id = "a", L1 = 1, L2 = 2, L3 = 3 }));
        Assert.IsType<DrvLevel2Dto>(m.Map(new DrvLevel2 { Id = "b", L1 = 4, L2 = 5 }));
        Assert.IsType<DrvLevel1Dto>(m.Map(new DrvLevel1 { Id = "c", L1 = 6 }));
    }
}
