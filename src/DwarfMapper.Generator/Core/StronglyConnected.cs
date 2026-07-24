// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Core;

/// <summary>
///     Tarjan's strongly-connected-components pass over a directed call graph, used to answer
///     "is this method on a cycle?" — i.e. recursion-capable — for every node at once.
///     <para>
///     ISSUE-023: both engines answered that question with a fresh depth-first search per node
///     ("can X reach X?"), which is O(V·(V+E)). One SCC pass answers it for the whole graph in O(V+E).
///     The generator runs on every keystroke in the IDE, so the quadratic term is paid interactively.
///     </para>
///     <para>
///     The DFS is ITERATIVE, not recursive. A synthesized call graph is as deep as the user's type graph,
///     and a recursive Tarjan would put that depth on the generator's own stack — a StackOverflow inside a
///     generator takes the whole compilation down with no usable diagnostic.
///     </para>
/// </summary>
internal static class StronglyConnected
{
    /// <summary>
    ///     Returns every node that lies on a cycle: any node in a strongly-connected component of more than
    ///     one member, plus any node carrying a self-edge. This is exactly the set the per-node
    ///     "can X reach itself" search produced, computed in a single pass.
    ///     <para>
    ///     Membership is independent of traversal order, so the result does not depend on dictionary or
    ///     hash-set enumeration order — the generator's output stays deterministic.
    ///     </para>
    /// </summary>
    public static HashSet<string> NodesOnACycle(IReadOnlyDictionary<string, HashSet<string>> graph)
    {
        var onCycle = new HashSet<string>(StringComparer.Ordinal);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLink = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var component = new Stack<string>();
        var nextIndex = 0;

        foreach (var root in graph.Keys)
        {
            if (index.ContainsKey(root)) continue;

            var work = new Stack<(string Node, IEnumerator<string> Edges)>();
            index[root] = lowLink[root] = nextIndex++;
            component.Push(root);
            onStack.Add(root);
            work.Push((root, EdgesOf(graph, root)));

            while (work.Count > 0)
            {
                var (node, edges) = work.Peek();

                if (edges.MoveNext())
                {
                    var dep = edges.Current;
                    if (!index.ContainsKey(dep))
                    {
                        index[dep] = lowLink[dep] = nextIndex++;
                        component.Push(dep);
                        onStack.Add(dep);
                        work.Push((dep, EdgesOf(graph, dep)));
                    }
                    else if (onStack.Contains(dep))
                    {
                        // Back-edge into the current component.
                        if (index[dep] < lowLink[node]) lowLink[node] = index[dep];
                    }

                    continue;
                }

                // Node exhausted: fold its low-link into its parent, then close a root's component.
                work.Pop();
                if (work.Count > 0)
                {
                    var parent = work.Peek().Node;
                    if (lowLink[node] < lowLink[parent]) lowLink[parent] = lowLink[node];
                }

                if (lowLink[node] != index[node]) continue;

                var size = 0;
                string popped;
                do
                {
                    popped = component.Pop();
                    onStack.Remove(popped);
                    size++;
                    // Provisionally record; discarded below when the component turns out to be a singleton
                    // without a self-edge.
                    onCycle.Add(popped);
                } while (!StringComparer.Ordinal.Equals(popped, node));

                // A one-member component is only a cycle when the node points at itself.
                if (size == 1 && !HasSelfEdge(graph, node)) onCycle.Remove(node);
            }
        }

        return onCycle;
    }

    private static IEnumerator<string> EdgesOf(IReadOnlyDictionary<string, HashSet<string>> graph, string node)
    {
        return graph.TryGetValue(node, out var deps)
            ? deps.GetEnumerator()
            : System.Linq.Enumerable.Empty<string>().GetEnumerator();
    }

    private static bool HasSelfEdge(IReadOnlyDictionary<string, HashSet<string>> graph, string node)
    {
        return graph.TryGetValue(node, out var deps) && deps.Contains(node);
    }
}
