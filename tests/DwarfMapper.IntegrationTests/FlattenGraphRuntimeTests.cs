// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

// ── Test models ───────────────────────────────────────────────────────────────

public class FGNode
{
    public string Name { get; set; } = "";
    public long Value { get; set; } // long→int via numeric narrowing
    public FGNode? X { get; set; }
    public FGNode? Y { get; set; }
}

public class FGNodeDto
{
    public string Name { get; set; } = "";
    public int Value { get; set; } // long→int conversion
    public FGNodeDto? X { get; set; }
    public FGNodeDto? Y { get; set; }
}

public class FGRoot
{
    public FGNode? Entry { get; set; }
    public string Tag { get; set; } = "";
}

public class FGRootDto
{
    public List<FGNodeDto> Nodes { get; set; } = new();
    public string Tag { get; set; } = "";
}

// Collection-edge model
public class FGCollNode
{
    public string Name { get; set; } = "";
    public List<FGCollNode> Children { get; set; } = new();
}

public class FGCollNodeDto
{
    public string Name { get; set; } = "";
    public List<FGCollNodeDto>? Children { get; set; }
}

public class FGCollRoot
{
    public FGCollNode? Entry { get; set; }
}

public class FGCollRootDto
{
    public List<FGCollNodeDto> Nodes { get; set; } = new();
}

// Array target model
public class FGRootArr
{
    public FGNode? Entry { get; set; }
}

public class FGRootArrDto
{
    public FGNodeDto[] Nodes { get; set; } = Array.Empty<FGNodeDto>();
}

// IReadOnlyList target model
public class FGRootRol
{
    public FGNode? Entry { get; set; }
}

public class FGRootRolDto
{
    public IReadOnlyList<FGNodeDto> Nodes { get; set; } = new List<FGNodeDto>();
}

[DwarfMapper]
public partial class FGMapper
{
    [FlattenGraph(nameof(FGRoot.Entry), nameof(FGRootDto.Nodes))]
    public partial FGRootDto Map(FGRoot root);

    [FlattenGraph(nameof(FGCollRoot.Entry), nameof(FGCollRootDto.Nodes))]
    public partial FGCollRootDto MapColl(FGCollRoot root);

    [FlattenGraph(nameof(FGRootArr.Entry), nameof(FGRootArrDto.Nodes))]
    public partial FGRootArrDto MapArr(FGRootArr root);

    [FlattenGraph(nameof(FGRootRol.Entry), nameof(FGRootRolDto.Nodes))]
    public partial FGRootRolDto MapRol(FGRootRol root);
}

public class FlattenGraphRuntimeTests
{
    private readonly FGMapper _mapper = new();

    // ── 1. Null entry → empty collection ─────────────────────────────────────

    [Fact]
    public void Null_entry_yields_empty_collection()
    {
        var root = new FGRoot { Entry = null, Tag = "test" };
        var result = _mapper.Map(root);
        Assert.Empty(result.Nodes);
        Assert.Equal("test", result.Tag);
    }

    // ── 2. Single node ────────────────────────────────────────────────────────

    [Fact]
    public void Single_node_yields_one_element()
    {
        var node = new FGNode { Name = "A", Value = 42L, X = null, Y = null };
        var root = new FGRoot { Entry = node, Tag = "t" };
        var result = _mapper.Map(root);
        Assert.Single(result.Nodes);
        Assert.Equal("A", result.Nodes[0].Name);
        Assert.Equal(42, result.Nodes[0].Value); // long→int
        Assert.Null(result.Nodes[0].X); // edge nulled
        Assert.Null(result.Nodes[0].Y); // edge nulled
    }

    // ── 3. Two-node linear chain ──────────────────────────────────────────────

    [Fact]
    public void Two_node_chain_yields_two_elements()
    {
        var b = new FGNode { Name = "B" };
        var a = new FGNode { Name = "A", X = b };
        var root = new FGRoot { Entry = a };
        var result = _mapper.Map(root);
        Assert.Equal(2, result.Nodes.Count);
        var names = result.Nodes.Select(n => n.Name).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "A", "B" }, names);
    }

    // ── 4. Owner graph cycle: A→B→C→A (3-node ring) — terminates ─────────────

    [Fact]
    public void Owner_graph_cycle_terminates_and_collects_all_nodes()
    {
        var a = new FGNode { Name = "A" };
        var b = new FGNode { Name = "B" };
        var c = new FGNode { Name = "C" };
        a.X = b;
        b.X = c;
        c.X = a; // ring cycle
        var root = new FGRoot { Entry = a };
        var result = _mapper.Map(root);
        Assert.Equal(3, result.Nodes.Count);
        var names = result.Nodes.Select(n => n.Name).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "A", "B", "C" }, names);
        // All edge members must be null (topology degraded)
        Assert.All(result.Nodes, n => Assert.Null(n.X));
        Assert.All(result.Nodes, n => Assert.Null(n.Y));
    }

    // ── 5. Diamond: shared node collected exactly once ───────────────────────

    [Fact]
    public void Diamond_shared_node_collected_once()
    {
        var shared = new FGNode { Name = "S" };
        var left = new FGNode { Name = "L", X = shared };
        var right = new FGNode { Name = "R", X = shared };
        var entry = new FGNode { Name = "E", X = left, Y = right };
        var root = new FGRoot { Entry = entry };
        var result = _mapper.Map(root);
        Assert.Equal(4, result.Nodes.Count); // E, L, R, S — S exactly once
        Assert.Single(result.Nodes, n => n.Name == "S");
    }

    // ── 6. Self-loop terminates, node collected once ──────────────────────────

    [Fact]
    public void Self_loop_terminates_node_collected_once()
    {
        var node = new FGNode { Name = "Self" };
        node.X = node; // self-loop
        var root = new FGRoot { Entry = node };
        var result = _mapper.Map(root);
        Assert.Single(result.Nodes);
        Assert.Equal("Self", result.Nodes[0].Name);
        Assert.Null(result.Nodes[0].X);
    }

    // ── 7. Collection edge traverses all children ─────────────────────────────

    [Fact]
    public void Collection_edge_traverses_all_children()
    {
        var c1 = new FGCollNode { Name = "C1" };
        var c2 = new FGCollNode { Name = "C2" };
        var rootNode = new FGCollNode { Name = "Root" };
        rootNode.Children.Add(c1);
        rootNode.Children.Add(c2);
        var root = new FGCollRoot { Entry = rootNode };
        var result = _mapper.MapColl(root);
        Assert.Equal(3, result.Nodes.Count);
        var names = result.Nodes.Select(n => n.Name).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "C1", "C2", "Root" }, names);
        Assert.All(result.Nodes, n => Assert.Null(n.Children));
    }

    // ── 8. Root's other members map normally ──────────────────────────────────

    [Fact]
    public void Root_other_members_map_normally()
    {
        var node = new FGNode { Name = "N" };
        var root = new FGRoot { Entry = node, Tag = "hello" };
        var result = _mapper.Map(root);
        Assert.Equal("hello", result.Tag);
    }

    // ── 9. Leaf scalar conversion preserved (long→int) ───────────────────────

    [Fact]
    public void Leaf_scalar_conversion_preserved()
    {
        var node = new FGNode { Name = "N", Value = 99L };
        var root = new FGRoot { Entry = node };
        var result = _mapper.Map(root);
        Assert.Equal(99, result.Nodes[0].Value);
    }

    // ── 10. Array target produces correct array ───────────────────────────────

    [Fact]
    public void Array_target_works()
    {
        var b = new FGNode { Name = "B" };
        var a = new FGNode { Name = "A", X = b };
        var root = new FGRootArr { Entry = a };
        var result = _mapper.MapArr(root);
        Assert.Equal(2, result.Nodes.Length);
        var names = result.Nodes.Select(n => n.Name).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "A", "B" }, names);
    }

    // ── 11. IReadOnlyList target works ────────────────────────────────────────

    [Fact]
    public void IReadOnlyList_target_works()
    {
        var b = new FGNode { Name = "B" };
        var a = new FGNode { Name = "A", X = b };
        var root = new FGRootRol { Entry = a };
        var result = _mapper.MapRol(root);
        Assert.Equal(2, result.Nodes.Count);
    }

    // ── 12. Cycle does NOT throw DwarfMappingDepthException ───────────────────

    [Fact]
    public void Cycle_does_not_throw_depth_exception()
    {
        var a = new FGNode { Name = "A" };
        var b = new FGNode { Name = "B" };
        a.X = b;
        b.X = a;
        var root = new FGRoot { Entry = a };
        var result = _mapper.Map(root); // must not throw
        Assert.Equal(2, result.Nodes.Count);
    }

    // ── 13. Null entry → empty array (array target) ───────────────────────────

    [Fact]
    public void Null_entry_yields_empty_array()
    {
        var root = new FGRootArr { Entry = null };
        var result = _mapper.MapArr(root);
        Assert.Empty(result.Nodes);
    }

    // ── 14. Multiple nodes with same value — both collected ───────────────────

    [Fact]
    public void Multiple_nodes_with_same_name_collected_separately()
    {
        // Two distinct objects with same Name — different references → both collected
        var a = new FGNode { Name = "A" };
        var b = new FGNode { Name = "A" }; // same name, different ref
        a.X = b;
        var root = new FGRoot { Entry = a };
        var result = _mapper.Map(root);
        Assert.Equal(2, result.Nodes.Count);
    }
}
