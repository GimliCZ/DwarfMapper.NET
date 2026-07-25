// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Core;

namespace DwarfMapper.Generator.Tests.Core;

/// <summary>
///     ISSUE-023 replaced a per-node "can X reach X?" DFS with one Tarjan SCC pass. The golden manifest proves
///     the two agree on the graphs the corpus happens to produce; it cannot prove they agree on graph shapes the
///     corpus never builds. These tests pin the shapes directly and then check the two algorithms against each
///     other over a spread of generated graphs.
/// </summary>
public class StronglyConnectedTests
{
    private static Dictionary<string, HashSet<string>> Graph(params (string From, string[] To)[] edges)
    {
        var g = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (from, to) in edges)
            g[from] = new HashSet<string>(to, StringComparer.Ordinal);
        return g;
    }

    [Fact]
    public void A_self_edge_is_a_cycle()
    {
        var result = StronglyConnected.NodesOnACycle(Graph(("a", new[] { "a" })));
        Assert.Equal(new[] { "a" }, result.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void A_plain_chain_has_no_cycle()
    {
        var result = StronglyConnected.NodesOnACycle(
            Graph(("a", new[] { "b" }), ("b", new[] { "c" }), ("c", new[] { "d" })));
        Assert.Empty(result);
    }

    [Fact]
    public void A_diamond_is_not_a_cycle()
    {
        // a→b, a→c, b→d, c→d: every node reachable two ways, none on a cycle.
        var result = StronglyConnected.NodesOnACycle(
            Graph(("a", new[] { "b", "c" }), ("b", new[] { "d" }), ("c", new[] { "d" })));
        Assert.Empty(result);
    }

    [Fact]
    public void Every_member_of_a_multi_node_cycle_is_reported()
    {
        var result = StronglyConnected.NodesOnACycle(
            Graph(("a", new[] { "b" }), ("b", new[] { "c" }), ("c", new[] { "a" })));
        Assert.Equal(new[] { "a", "b", "c" }, result.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void A_tail_hanging_off_a_cycle_is_not_itself_on_the_cycle()
    {
        // a↔b is a cycle; c is reachable FROM it but cannot return, so c is not recursion-capable.
        var result = StronglyConnected.NodesOnACycle(
            Graph(("a", new[] { "b" }), ("b", new[] { "a", "c" }), ("c", new[] { "d" })));
        Assert.Equal(new[] { "a", "b" }, result.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Two_disjoint_cycles_are_both_found()
    {
        var result = StronglyConnected.NodesOnACycle(
            Graph(("a", new[] { "b" }), ("b", new[] { "a" }), ("x", new[] { "y" }), ("y", new[] { "x" })));
        Assert.Equal(new[] { "a", "b", "x", "y" }, result.OrderBy(v => v, StringComparer.Ordinal));
    }

    [Fact]
    public void An_edge_to_an_unknown_node_does_not_throw()
    {
        // Synthesized call graphs routinely name a callee that has no outgoing edges of its own.
        var result = StronglyConnected.NodesOnACycle(Graph(("a", new[] { "missing" })));
        Assert.Empty(result);
    }

    /// <summary>
    ///     A deep chain proves the traversal is iterative. A recursive Tarjan would put the graph's depth on the
    ///     generator's own stack, and a StackOverflow inside a source generator kills the whole compilation with
    ///     no usable diagnostic.
    /// </summary>
    [Fact]
    public void A_very_deep_chain_does_not_overflow_the_stack()
    {
        var g = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        const int depth = 50_000;
        for (var i = 0; i < depth; i++)
            g["n" + i] = new HashSet<string>(new[] { "n" + (i + 1) }, StringComparer.Ordinal);
        // Close the loop so there IS a cycle to find at the very bottom of the descent.
        g["n" + depth] = new HashSet<string>(new[] { "n0" }, StringComparer.Ordinal);

        var result = StronglyConnected.NodesOnACycle(g);

        Assert.Equal(depth + 1, result.Count);
    }

    /// <summary>
    ///     The equivalence check the audit asked for: across a spread of generated graphs, SCC membership must
    ///     agree node-for-node with the per-node DFS it replaced.
    /// </summary>
    [Fact]
    public void Matches_the_per_node_DFS_it_replaced_across_generated_graphs()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var g = RandomGraph(seed, out var nodes);

            var viaScc = StronglyConnected.NodesOnACycle(g);
            var viaDfs = new HashSet<string>(nodes.Where(n => CanReachSelf(g, n)), StringComparer.Ordinal);

            Assert.True(viaScc.SetEquals(viaDfs),
                $"seed {seed}: SCC gave [{string.Join(",", viaScc.OrderBy(x => x, StringComparer.Ordinal))}] "
                + $"but the reference DFS gave [{string.Join(",", viaDfs.OrderBy(x => x, StringComparer.Ordinal))}]");
        }
    }

    private static Dictionary<string, HashSet<string>> RandomGraph(int seed, out List<string> nodes)
    {
        var rng = new Random(seed);
        var count = 2 + rng.Next(9);
        nodes = Enumerable.Range(0, count).Select(i => "n" + i).ToList();

        var g = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var n in nodes)
        {
            var deps = new HashSet<string>(StringComparer.Ordinal);
            // Sparse enough that many graphs are acyclic and many are not — both branches get exercised.
            foreach (var candidate in nodes)
                if (rng.Next(4) == 0)
                    deps.Add(candidate);
            g[n] = deps;
        }

        return g;
    }

    /// <summary>The exact algorithm ISSUE-023 removed, kept here purely as the reference oracle.</summary>
    private static bool CanReachSelf(Dictionary<string, HashSet<string>> graph, string start)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();

        if (!graph.TryGetValue(start, out var startDeps)) return false;
        foreach (var dep in startDeps) stack.Push(dep);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (StringComparer.Ordinal.Equals(current, start)) return true;
            if (!visited.Add(current)) continue;
            if (graph.TryGetValue(current, out var deps))
                foreach (var dep in deps)
                    stack.Push(dep);
        }

        return false;
    }
}
