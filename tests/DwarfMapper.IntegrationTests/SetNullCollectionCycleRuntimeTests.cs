// SPDX-License-Identifier: GPL-2.0-only



// CA5394: System.Random is used ONLY for deterministic, seeded test-data generation. Not security.
#pragma warning disable CA5394

namespace DwarfMapper.IntegrationTests;

// ── Cycles routed through a COLLECTION edge under OnCycle = SetNull ───────────
// The shared DwarfRefContext (on-stack guard) is threaded through the collection element
// mapper, so a node re-entered while still on the stack maps to null — even across a List<T>
// or Dictionary<K,V> boundary.

public class SncTree
{
    public int V { get; set; }
    public List<SncTree>? Children { get; set; }
}

public class SncTreeDto
{
    public int V { get; set; }
    public List<SncTreeDto>? Children { get; set; }
}

[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
public partial class SncTreeMapper
{
    public partial SncTreeDto Map(SncTree t);
}

// Cross-collection cycle via dictionary value.
public class SncDictNode
{
    public int V { get; set; }
    public Dictionary<string, SncDictNode>? Edges { get; set; }
}

public class SncDictNodeDto
{
    public int V { get; set; }
    public Dictionary<string, SncDictNodeDto>? Edges { get; set; }
}

[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
public partial class SncDictMapper
{
    public partial SncDictNodeDto Map(SncDictNode n);
}

public class SetNullCollectionCycleRuntimeTests
{
    // ── List-edge cycle: root.Children=[child], child.Children=[root] ────────────
    [Fact]
    public void ListEdge_cycle_back_edge_element_is_null()
    {
        var root = new SncTree { V = 1, Children = new List<SncTree>() };
        var child = new SncTree { V = 2, Children = new List<SncTree>() };
        root.Children!.Add(child);
        child.Children!.Add(root); // back-edge through the list

        var t = new SncTreeMapper().Map(root);
        Assert.Equal(1, t.V);
        Assert.NotNull(t.Children);
        Assert.Single(t.Children!);
        Assert.Equal(2, t.Children![0].V);
        // child.Children[0] == root, which is still on the stack → element nulled.
        Assert.NotNull(t.Children[0].Children);
        Assert.Single(t.Children[0].Children!);
        Assert.Null(t.Children[0].Children![0]);
    }

    // ── Self-loop through a list: node.Children contains itself ─────────────────
    [Fact]
    public void ListEdge_self_loop_element_is_null()
    {
        var n = new SncTree { V = 7, Children = new List<SncTree>() };
        n.Children!.Add(n);

        var t = new SncTreeMapper().Map(n);
        Assert.Equal(7, t.V);
        Assert.Single(t.Children!);
        Assert.Null(t.Children![0]); // the self-reference element breaks to null
    }

    // ── Dictionary-value cycle ──────────────────────────────────────────────────
    [Fact]
    public void DictValueEdge_cycle_back_edge_value_is_null()
    {
        var a = new SncDictNode { V = 1, Edges = new Dictionary<string, SncDictNode>() };
        var b = new SncDictNode { V = 2, Edges = new Dictionary<string, SncDictNode>() };
        a.Edges!["to-b"] = b;
        b.Edges!["to-a"] = a; // cycle through dictionary values

        var t = new SncDictMapper().Map(a);
        Assert.Equal(1, t.V);
        Assert.NotNull(t.Edges);
        Assert.True(t.Edges!.ContainsKey("to-b"));
        Assert.Equal(2, t.Edges["to-b"].V);
        // b.Edges["to-a"] == a, on the stack → value nulled.
        Assert.NotNull(t.Edges["to-b"].Edges);
        Assert.Null(t.Edges["to-b"].Edges!["to-a"]);
    }

    // ── Fuzz: random graphs wired through List edges (incl. back-edges) terminate ─
    [Theory]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(256)]
    [InlineData(65537)]
    public void Fuzz_random_list_graphs_terminate_and_are_acyclic(int seed)
    {
        var rng = new Random(seed);
        for (var iter = 0; iter < 40; iter++)
        {
            var n = rng.Next(1, 10);
            var nodes = new List<SncTree>();
            for (var i = 0; i < n; i++) nodes.Add(new SncTree { V = i, Children = new List<SncTree>() });
            // Wire each node's Children to a random subset of existing nodes (incl. self/ancestors → cycles).
            foreach (var node in nodes)
            {
                var edges = rng.Next(0, 4);
                for (var e = 0; e < edges; e++) node.Children!.Add(nodes[rng.Next(n)]);
            }

            var mapper = new SncTreeMapper();
            var t1 = mapper.Map(nodes[0]); // must not StackOverflow
            Assert.NotNull(t1);
            AssertAcyclicFinite(t1);

            var t2 = mapper.Map(nodes[0]);
            Assert.Equal(Shape(t1), Shape(t2)); // deterministic
        }
    }

    private static void AssertAcyclicFinite(SncTreeDto root)
    {
        var onPath = new HashSet<SncTreeDto>(ReferenceEqualityComparer.Instance);
        var total = 0;

        void Walk(SncTreeDto? node)
        {
            if (node is null) return;
            Assert.True(onPath.Add(node), "result must be acyclic");
            Assert.True(++total < 1_000_000, "result must be finite");
            if (node.Children is not null)
                foreach (var c in node.Children)
                    Walk(c);
            onPath.Remove(node);
        }

        Walk(root);
    }

    private static string Shape(SncTreeDto? node)
    {
        if (node is null) return ".";
        var parts = node.Children is null
            ? ""
            : string.Join(",", node.Children.Select(Shape));
        return $"({node.V} [{parts}])";
    }
}
