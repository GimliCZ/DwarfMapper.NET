// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Testing;

namespace DwarfMapper.Generator.Tests.Fuzzing;

/// <summary>
///     VF3 — topology oracle fuzz: wire <see cref="GraphOracleComparer.TopologyDiff" /> into
///     actual Preserve-mode mapping tests so that sharing/cycle topology is verified, not just
///     value equality.  A duplicate-shared-node bug (registering references but not actually
///     de-duplicating) would pass value tests but fail topology tests.
///     Strategy: emit several fixed graph schemas under Preserve, build diamond / self-loop /
///     shared-list shapes reflectively, map, and assert TopologyDiff is empty for each seed.
/// </summary>
public class TopologyOracleFuzzTests
{
    // ── Shared "node graph" source (class with self-referential properties) ───────

    private const string NodeGraphSource = """
                                           using DwarfMapper;
                                           using System.Collections.Generic;
                                           namespace Topo;

                                           public class Node {
                                               public int V { get; set; }
                                               public Node? Left  { get; set; }
                                               public Node? Right { get; set; }
                                               public Node? Back  { get; set; }
                                           }
                                           public class NodeDto {
                                               public int V { get; set; }
                                               public NodeDto? Left  { get; set; }
                                               public NodeDto? Right { get; set; }
                                               public NodeDto? Back  { get; set; }
                                           }
                                           [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                                           public partial class TopoMapper { public partial NodeDto Map(Node n); }
                                           """;

    // ── Shared-list node graph (Preserve over a node that holds a List<Node>) ───

    private const string SharedListSource = """
                                            using DwarfMapper;
                                            using System.Collections.Generic;
                                            namespace Topo;

                                            public class ListNode {
                                                public int V { get; set; }
                                                public List<ListNode> Children { get; set; } = new();
                                            }
                                            public class ListNodeDto {
                                                public int V { get; set; }
                                                public List<ListNodeDto> Children { get; set; } = new();
                                            }
                                            [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                                            public partial class ListMapper { public partial ListNodeDto Map(ListNode n); }
                                            """;

    // ─────────────────────────────────────────────────────────────────────────────
    // Tests
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Topology_diamond_no_back_edge_preserved()
    {
        var (asm, errors) = GeneratorTestHarness.EmitAssembly(NodeGraphSource);
        Assert.True(asm is not null,
            $"NodeGraphSource failed to emit: {string.Join(", ", errors.Select(e => e.Id))}");

        var nodeType = asm!.GetType("Topo.Node")!;
        var mapperType = asm.GetType("Topo.TopoMapper")!;
        var mapMethod = mapperType.GetMethod("Map")!;
        var mapper = Activator.CreateInstance(mapperType)!;

        // Diamond: root.Left == root.Right == shared (no back-edge)
        var shared = CreateNode(nodeType, 99);
        var root = CreateNode(nodeType, 1);
        SetProp(root, "Left", shared);
        SetProp(root, "Right", shared);

        var dst = mapMethod.Invoke(mapper, new[] { root })!;

        var topo = GraphOracleComparer.TopologyDiff(root, dst);
        Assert.True(topo.Count == 0,
            "Diamond topology violated:\n" + GraphOracleComparer.RenderTopologyDiff(topo));
    }

    [Fact]
    public void Topology_self_loop_preserved()
    {
        var (asm, errors) = GeneratorTestHarness.EmitAssembly(NodeGraphSource);
        Assert.True(asm is not null,
            $"NodeGraphSource failed to emit: {string.Join(", ", errors.Select(e => e.Id))}");

        var nodeType = asm!.GetType("Topo.Node")!;
        var mapperType = asm.GetType("Topo.TopoMapper")!;
        var mapMethod = mapperType.GetMethod("Map")!;
        var mapper = Activator.CreateInstance(mapperType)!;

        // Self-loop: n.Back = n
        var n = CreateNode(nodeType, 42);
        SetProp(n, "Back", n);

        var dst = mapMethod.Invoke(mapper, new[] { n })!;

        var topo = GraphOracleComparer.TopologyDiff(n, dst);
        Assert.True(topo.Count == 0,
            "Self-loop topology violated:\n" + GraphOracleComparer.RenderTopologyDiff(topo));
    }

    [Fact]
    public void Topology_two_node_cycle_preserved()
    {
        var (asm, errors) = GeneratorTestHarness.EmitAssembly(NodeGraphSource);
        Assert.True(asm is not null,
            $"NodeGraphSource failed to emit: {string.Join(", ", errors.Select(e => e.Id))}");

        var nodeType = asm!.GetType("Topo.Node")!;
        var mapperType = asm.GetType("Topo.TopoMapper")!;
        var mapMethod = mapperType.GetMethod("Map")!;
        var mapper = Activator.CreateInstance(mapperType)!;

        // Two-node cycle: a.Left = b, b.Back = a
        var a = CreateNode(nodeType, 1);
        var b = CreateNode(nodeType, 2);
        SetProp(a, "Left", b);
        SetProp(b, "Back", a);

        var dst = mapMethod.Invoke(mapper, new[] { a })!;

        var topo = GraphOracleComparer.TopologyDiff(a, dst);
        Assert.True(topo.Count == 0,
            "Two-node cycle topology violated:\n" + GraphOracleComparer.RenderTopologyDiff(topo));
    }

    [Fact]
    public void Topology_diamond_with_back_edge_preserved()
    {
        var (asm, errors) = GeneratorTestHarness.EmitAssembly(NodeGraphSource);
        Assert.True(asm is not null,
            $"NodeGraphSource failed to emit: {string.Join(", ", errors.Select(e => e.Id))}");

        var nodeType = asm!.GetType("Topo.Node")!;
        var mapperType = asm.GetType("Topo.TopoMapper")!;
        var mapMethod = mapperType.GetMethod("Map")!;
        var mapper = Activator.CreateInstance(mapperType)!;

        // Diamond with back-edge: root.Left = root.Right = shared, shared.Back = root
        var root = CreateNode(nodeType, 1);
        var shared = CreateNode(nodeType, 99);
        SetProp(root, "Left", shared);
        SetProp(root, "Right", shared);
        SetProp(shared, "Back", root);

        var dst = mapMethod.Invoke(mapper, new[] { root })!;

        var topo = GraphOracleComparer.TopologyDiff(root, dst);
        Assert.True(topo.Count == 0,
            "Diamond+back-edge topology violated:\n" + GraphOracleComparer.RenderTopologyDiff(topo));
    }

    [Fact]
    public void Topology_shared_list_node_preserved()
    {
        var (asm, errors) = GeneratorTestHarness.EmitAssembly(SharedListSource);
        Assert.True(asm is not null,
            $"SharedListSource failed to emit: {string.Join(", ", errors.Select(e => e.Id))}");

        var nodeType = asm!.GetType("Topo.ListNode")!;
        var mapperType = asm.GetType("Topo.ListMapper")!;
        var mapMethod = mapperType.GetMethod("Map")!;
        var mapper = Activator.CreateInstance(mapperType)!;

        // Shared list node: root.Children = [shared]; another.Children = [shared]
        // root.Children[0] == another.Children[0] == shared
        var shared = CreateListNode(nodeType, 99);
        var root = CreateListNode(nodeType, 1);
        var another = CreateListNode(nodeType, 2);
        AddListChild(nodeType, root, shared);
        AddListChild(nodeType, another, shared);
        AddListChild(nodeType, root, another); // root has both as children

        var dst = mapMethod.Invoke(mapper, new[] { root })!;

        var topo = GraphOracleComparer.TopologyDiff(root, dst);
        Assert.True(topo.Count == 0,
            "Shared-list-node topology violated:\n" + GraphOracleComparer.RenderTopologyDiff(topo));
    }

    // ── Multi-seed property fuzz: generate random graph shapes and verify topology ─

    public static IEnumerable<object[]> GraphSeeds()
    {
        return Enumerable.Range(0, 12).Select(i => new object[] { i });
    }

    [Theory]
    [MemberData(nameof(GraphSeeds))]
    public void Topology_random_graph_shapes_preserved(int seed)
    {
        var (asm, errors) = GeneratorTestHarness.EmitAssembly(NodeGraphSource);
        Assert.True(asm is not null,
            $"seed={seed} NodeGraphSource failed to emit: {string.Join(", ", errors.Select(e => e.Id))}");

        var nodeType = asm!.GetType("Topo.Node")!;
        var mapperType = asm.GetType("Topo.TopoMapper")!;
        var mapMethod = mapperType.GetMethod("Map")!;
        var mapper = Activator.CreateInstance(mapperType)!;

        // Build a random graph: up to 5 nodes, random edges via seed
        var rng = new Random(seed);
        var nodes = Enumerable.Range(0, rng.Next(2, 6))
            .Select(i => CreateNode(nodeType, i))
            .ToArray();

        // Set some random edges (Left/Right/Back) among the nodes
        var edgeProps = new[] { "Left", "Right", "Back" };
        foreach (var n in nodes)
        foreach (var prop in edgeProps)
            if (rng.Next(2) == 1)
                SetProp(n, prop, nodes[rng.Next(nodes.Length)]);

        var root = nodes[0];
        var dst = mapMethod.Invoke(mapper, new[] { root })!;

        var topo = GraphOracleComparer.TopologyDiff(root, dst);
        Assert.True(topo.Count == 0,
            $"seed={seed} random-graph topology violated:\n" +
            GraphOracleComparer.RenderTopologyDiff(topo));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Reflection helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static object CreateNode(Type nodeType, int v)
    {
        var n = Activator.CreateInstance(nodeType)!;
        nodeType.GetProperty("V")!.SetValue(n, v);
        return n;
    }

    private static object CreateListNode(Type nodeType, int v)
    {
        var n = Activator.CreateInstance(nodeType)!;
        nodeType.GetProperty("V")!.SetValue(n, v);
        // Children list is initialized in the ctor; no need to create it
        return n;
    }

    private static void SetProp(object obj, string name, object? value)
    {
        obj.GetType().GetProperty(name)!.SetValue(obj, value);
    }

    private static void AddListChild(Type nodeType, object parent, object child)
    {
        var childrenProp = nodeType.GetProperty("Children")!;
        var list = childrenProp.GetValue(parent)!;
        var addMethod = list.GetType().GetMethod("Add")!;
        addMethod.Invoke(list, new[] { child });
    }
}
