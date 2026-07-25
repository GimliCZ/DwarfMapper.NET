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

    // ── Item 17: topology preserved through dict / array / struct-wrapper edge carriers ──

    private const string ArrayCarrierSource = """
        using DwarfMapper;
        namespace Topo;
        public class Node { public int V { get; set; } public Node?[] Edges { get; set; } = System.Array.Empty<Node?>(); }
        public class NodeDto { public int V { get; set; } public NodeDto?[] Edges { get; set; } = System.Array.Empty<NodeDto?>(); }
        [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
        public partial class ArrMapper { public partial NodeDto Map(Node n); }
        """;

    private const string DictCarrierSource = """
        using DwarfMapper;
        using System.Collections.Generic;
        namespace Topo;
        public class Node { public int V { get; set; } public Dictionary<int, Node> Edges { get; set; } = new(); }
        public class NodeDto { public int V { get; set; } public Dictionary<int, NodeDto> Edges { get; set; } = new(); }
        [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
        public partial class DictMapper { public partial NodeDto Map(Node n); }
        """;

    private const string StructWrapperCarrierSource = """
        using DwarfMapper;
        namespace Topo;
        public struct Wrap { public Node? Node { get; set; } }
        public struct WrapDto { public NodeDto? Node { get; set; } }
        public class Node { public int V { get; set; } public Wrap Edge { get; set; } }
        public class NodeDto { public int V { get; set; } public WrapDto Edge { get; set; } }
        [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
        public partial class WrapMapper { public partial NodeDto Map(Node n); }
        """;

    [Fact]
    public void Topology_shared_node_through_array_edge_preserved()
    {
        var (asm, errors) = GeneratorTestHarness.EmitAssembly(ArrayCarrierSource);
        Assert.True(asm is not null, $"array carrier failed to emit: {string.Join(", ", errors.Select(e => e.Id))}");
        var nodeType = asm!.GetType("Topo.Node")!;
        var mapper = Activator.CreateInstance(asm.GetType("Topo.ArrMapper")!)!;
        var map = asm.GetType("Topo.ArrMapper")!.GetMethod("Map")!;

        // Diamond + back-edge routed through the array: root.Edges = [shared, another]; another.Edges =
        // [shared]; shared.Edges = [root].
        var root = CreateNode(nodeType, 1);
        var another = CreateNode(nodeType, 2);
        var shared = CreateNode(nodeType, 99);
        SetArrayEdges(nodeType, root, new[] { shared, another });
        SetArrayEdges(nodeType, another, new[] { shared });
        SetArrayEdges(nodeType, shared, new[] { root });

        var dst = map.Invoke(mapper, new[] { root })!;
        var topo = GraphOracleComparer.TopologyDiff(root, dst);
        Assert.True(topo.Count == 0, "Array-edge topology violated:\n" + GraphOracleComparer.RenderTopologyDiff(topo));
    }

    [Fact]
    public void Topology_shared_node_through_dictionary_edge_preserved()
    {
        var (asm, errors) = GeneratorTestHarness.EmitAssembly(DictCarrierSource);
        Assert.True(asm is not null, $"dict carrier failed to emit: {string.Join(", ", errors.Select(e => e.Id))}");
        var nodeType = asm!.GetType("Topo.Node")!;
        var mapper = Activator.CreateInstance(asm.GetType("Topo.DictMapper")!)!;
        var map = asm.GetType("Topo.DictMapper")!.GetMethod("Map")!;

        var root = CreateNode(nodeType, 1);
        var another = CreateNode(nodeType, 2);
        var shared = CreateNode(nodeType, 99);
        AddDictEdge(nodeType, root, 0, shared);
        AddDictEdge(nodeType, root, 1, another);
        AddDictEdge(nodeType, another, 0, shared);
        AddDictEdge(nodeType, shared, 0, root); // back-edge through the dictionary

        var dst = map.Invoke(mapper, new[] { root })!;
        var topo = GraphOracleComparer.TopologyDiff(root, dst);
        Assert.True(topo.Count == 0, "Dictionary-edge topology violated:\n" + GraphOracleComparer.RenderTopologyDiff(topo));
    }

    [Fact]
    public void Topology_shared_node_through_struct_wrapper_edge_preserved()
    {
        var (asm, errors) = GeneratorTestHarness.EmitAssembly(StructWrapperCarrierSource);
        Assert.True(asm is not null, $"struct wrapper carrier failed to emit: {string.Join(", ", errors.Select(e => e.Id))}");
        var nodeType = asm!.GetType("Topo.Node")!;
        var wrapType = asm.GetType("Topo.Wrap")!;
        var mapper = Activator.CreateInstance(asm.GetType("Topo.WrapMapper")!)!;
        var map = asm.GetType("Topo.WrapMapper")!.GetMethod("Map")!;

        // root.Edge wraps shared; another.Edge wraps shared → shared is reached through two struct-wrapped
        // edges and must reconstruct to ONE instance.
        var root = CreateNode(nodeType, 1);
        var another = CreateNode(nodeType, 2);
        var shared = CreateNode(nodeType, 99);
        SetWrapEdge(nodeType, wrapType, root, shared);
        SetWrapEdge(nodeType, wrapType, another, shared);
        SetWrapEdge(nodeType, wrapType, shared, another);

        var dst = map.Invoke(mapper, new[] { root })!;
        var topo = GraphOracleComparer.TopologyDiff(root, dst);
        Assert.True(topo.Count == 0, "Struct-wrapper-edge topology violated:\n" + GraphOracleComparer.RenderTopologyDiff(topo));
    }

    private static void SetArrayEdges(Type nodeType, object node, object[] edges)
    {
        var arr = Array.CreateInstance(nodeType, edges.Length);
        for (var i = 0; i < edges.Length; i++) arr.SetValue(edges[i], i);
        nodeType.GetProperty("Edges")!.SetValue(node, arr);
    }

    private static void AddDictEdge(Type nodeType, object node, int key, object target)
    {
        var dict = nodeType.GetProperty("Edges")!.GetValue(node)!;
        dict.GetType().GetMethod("Add")!.Invoke(dict, new object[] { key, target });
    }

    private static void SetWrapEdge(Type nodeType, Type wrapType, object node, object target)
    {
        var wrap = Activator.CreateInstance(wrapType)!; // boxed struct
        wrapType.GetProperty("Node")!.SetValue(wrap, target);
        nodeType.GetProperty("Edge")!.SetValue(node, wrap);
    }

    // ── Item 6: SetNull yields a DAG + Preserve preserves distinct-node count ──────

    private const string NodeGraphSetNullSource = """
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
        [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
        public partial class TopoMapper { public partial NodeDto Map(Node n); }
        """;

    private static readonly string[] EdgeProps = { "Left", "Right", "Back" };

    [Theory]
    [MemberData(nameof(GraphSeeds))]
    public void SetNull_random_cyclic_graph_yields_acyclic_result(int seed)
    {
        var (asm, errors) = GeneratorTestHarness.EmitAssembly(NodeGraphSetNullSource);
        Assert.True(asm is not null,
            $"seed={seed} SetNull source failed to emit: {string.Join(", ", errors.Select(e => e.Id))}");

        var nodeType = asm!.GetType("Topo.Node")!;
        var mapperType = asm.GetType("Topo.TopoMapper")!;
        var mapMethod = mapperType.GetMethod("Map")!;
        var mapper = Activator.CreateInstance(mapperType)!;

        // Build a random graph that very likely CONTAINS cycles (every node points somewhere).
        var rng = new Random(seed);
        var nodes = Enumerable.Range(0, rng.Next(2, 6)).Select(i => CreateNode(nodeType, i)).ToArray();
        foreach (var n in nodes)
            foreach (var prop in EdgeProps)
                if (rng.Next(2) == 1)
                    SetProp(n, prop, nodes[rng.Next(nodes.Length)]);
        // Guarantee at least one back-edge cycle exists so we actually exercise SetNull.
        SetProp(nodes[^1], "Back", nodes[0]);
        SetProp(nodes[0], "Left", nodes[^1]);

        var dst = mapMethod.Invoke(mapper, new[] { nodes[0] })!;

        // The destination graph must be acyclic: an emitter that breaks only SOME back-edges would leave a
        // residual cycle that StackOverflows on consumption. DFS with an on-path set catches it.
        AssertAcyclic(dst);
    }

    [Theory]
    [MemberData(nameof(GraphSeeds))]
    public void Preserve_preserves_distinct_node_count(int seed)
    {
        var (asm, errors) = GeneratorTestHarness.EmitAssembly(NodeGraphSource);
        Assert.True(asm is not null,
            $"seed={seed} NodeGraphSource failed to emit: {string.Join(", ", errors.Select(e => e.Id))}");

        var nodeType = asm!.GetType("Topo.Node")!;
        var mapperType = asm.GetType("Topo.TopoMapper")!;
        var mapMethod = mapperType.GetMethod("Map")!;
        var mapper = Activator.CreateInstance(mapperType)!;

        var rng = new Random(seed);
        var nodes = Enumerable.Range(0, rng.Next(2, 6)).Select(i => CreateNode(nodeType, i)).ToArray();
        foreach (var n in nodes)
            foreach (var prop in EdgeProps)
                if (rng.Next(2) == 1)
                    SetProp(n, prop, nodes[rng.Next(nodes.Length)]);

        var root = nodes[0];
        var dst = mapMethod.Invoke(mapper, new[] { root })!;

        // Preserve must reconstruct EXACTLY as many distinct objects as are reachable in the source. A silent
        // de-dup failure (mapping a shared node to two copies) inflates the destination count past value/
        // edge-sampled topology checks; an over-merge deflates it.
        var srcCount = CountDistinctNodes(root);
        var dstCount = CountDistinctNodes(dst);
        Assert.True(srcCount == dstCount,
            $"seed={seed} Preserve distinct-node count mismatch: source has {srcCount}, destination has {dstCount}.");
    }

    /// <summary>DFS asserting the reference graph reachable from <paramref name="root"/> via Left/Right/Back
    /// contains no cycle (back-edge to a node already on the active path).</summary>
    private static void AssertAcyclic(object root)
    {
        var onPath = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var done = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Visit(root);

        void Visit(object? node)
        {
            if (node is null) return;
            Assert.True(onPath.Add(node), "SetNull result contains a cycle (a back-edge was not nulled).");
            if (done.Add(node))
                foreach (var prop in EdgeProps)
                    Visit(node.GetType().GetProperty(prop)!.GetValue(node));
            onPath.Remove(node);
        }
    }

    /// <summary>Counts distinct reference objects reachable via Left/Right/Back.</summary>
    private static int CountDistinctNodes(object root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<object>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!seen.Add(node)) continue;
            foreach (var prop in EdgeProps)
                if (node.GetType().GetProperty(prop)!.GetValue(node) is { } next)
                    stack.Push(next);
        }
        return seen.Count;
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
