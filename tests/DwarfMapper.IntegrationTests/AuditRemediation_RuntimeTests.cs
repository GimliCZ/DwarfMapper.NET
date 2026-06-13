// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

// ═══════════════════════════════════════════════════════════════════════════════
// SF-F3 — Dictionary<K,Node> edge values not traversed
//
// Node type intentionally does NOT carry a dict member on itself (that would
// trigger the separate MF-D CS7036 bug before MF-D is fixed).  The dict is
// only on the ROOT entry, whose values are plain nodes.  This isolates the
// SF-F3 "values not enqueued in BFS" bug from the MF-D "wrong arg-count" bug.
// ═══════════════════════════════════════════════════════════════════════════════

// A node type with a scalar edge (Next) but no dict member — avoids MF-D.
public class DNode
{
    public string Name { get; set; } = "";
    public DNode? Next { get; set; }
}

public class DNodeDto
{
    public string Name { get; set; } = "";
    public DNodeDto? Next { get; set; }
}

// Root has a Dictionary<string, DNode> so the source nav is dict-valued.
public class DRoot
{
    public Dictionary<string, DNode> Entries { get; set; } = new();
    public string Tag { get; set; } = "";
}

public class DRootDto
{
    public List<DNodeDto> Nodes { get; set; } = new();
    public string Tag { get; set; } = "";
}

[DwarfMapper]
public partial class DRootMapper
{
    [FlattenGraph(nameof(DRoot.Entries), nameof(DRootDto.Nodes))]
    public partial DRootDto Map(DRoot r);
}

// ═══════════════════════════════════════════════════════════════════════════════
// SF-F4 — Homo FlattenGraph: interface-typed edge not traversed
//
// Node.Link is typed IFGNode (interface).  The current generator treats this
// as a leaf (exact-type check misses it) so nodes reachable only via .Link
// are silently dropped.  The fixture compiles fine — the bug is runtime-only.
// ═══════════════════════════════════════════════════════════════════════════════

public interface IFGNode
{
    string Name { get; set; }
    IFGNode? Link { get; set; }
}

public class FGNodeImpl : IFGNode
{
    public string Name { get; set; } = "";
    public IFGNode? Link { get; set; }
}

public class FGNodeImplDto
{
    public string Name { get; set; } = "";
    public FGNodeImplDto? Link { get; set; }
}

public class FGIfaceRoot
{
    public FGNodeImpl? Entry { get; set; }
}

public class FGIfaceRootDto
{
    public List<FGNodeImplDto> Nodes { get; set; } = new();
}

[DwarfMapper]
public partial class FGIfaceMapper
{
    [FlattenGraph(nameof(FGIfaceRoot.Entry), nameof(FGIfaceRootDto.Nodes))]
    public partial FGIfaceRootDto Map(FGIfaceRoot r);
}

// ═══════════════════════════════════════════════════════════════════════════════
// Test class
// ═══════════════════════════════════════════════════════════════════════════════

public class AuditRemediation_RuntimeTests
{
    // ── SF-F3: Dict-valued source navigation — values must be traversed ───────
    //
    // NOTE: this tests "dict as source navigation" (the FlattenGraph sourceNav
    // points at a dict member).  The companion generator test SfF3_FlattenGraph_
    // DictValues_traversal_emitted covers the case where NODES have dict edges.

    [Fact]
    public void SfF3_DictSourceNav_values_reachable_nodes_all_collected()
    {
        // root.Entries has two nodes: a → b (via Next).
        // All nodes reachable from the dict values must be collected.
        // If the dict values themselves are not even seeded into BFS → 0 nodes.
        var b = new DNode { Name = "b" };
        var a = new DNode { Name = "a", Next = b };

        var mapper = new DRootMapper();
        var result = mapper.Map(new DRoot { Entries = new Dictionary<string, DNode> { ["a"] = a } });

        // Expect both a and b collected
        var names = result.Nodes.Select(n => n.Name).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "a", "b" }, names);
    }

    [Fact]
    public void SfF3_DictSourceNav_empty_dict_yields_empty_list()
    {
        var mapper = new DRootMapper();
        var result = mapper.Map(new DRoot { Entries = new Dictionary<string, DNode>() });
        Assert.Empty(result.Nodes);
    }

    [Fact]
    public void SfF3_DictSourceNav_multiple_values_all_seeded()
    {
        // Three independent nodes in the dict — all must appear in the output.
        var mapper = new DRootMapper();
        var root = new DRoot
        {
            Entries = new Dictionary<string, DNode>
            {
                ["x"] = new DNode { Name = "x" },
                ["y"] = new DNode { Name = "y" },
                ["z"] = new DNode { Name = "z" },
            }
        };
        var result = mapper.Map(root);
        Assert.Equal(3, result.Nodes.Count);
    }

    [Fact]
    public void SfF3_DictSourceNav_root_other_members_mapped_normally()
    {
        var mapper = new DRootMapper();
        var result = mapper.Map(new DRoot
        {
            Tag = "hello",
            Entries = new Dictionary<string, DNode> { ["n"] = new DNode { Name = "n" } }
        });
        Assert.Equal("hello", result.Tag);
    }

    // ── SF-F4: Interface-typed edge must be traversed ────────────────────────

    [Fact]
    public void SfF4_InterfaceTypedEdge_two_chained_nodes_both_collected()
    {
        // a.Link = b (typed IFGNode), b.Link = null
        // Before fix: only a is collected because .Link is IFGNode-typed
        // and the exact-equality edge check misses it → b never enqueued.
        var b = new FGNodeImpl { Name = "b", Link = null };
        var a = new FGNodeImpl { Name = "a", Link = b };

        var mapper = new FGIfaceMapper();
        var result = mapper.Map(new FGIfaceRoot { Entry = a });

        Assert.Equal(2, result.Nodes.Count);
        var names = result.Nodes.Select(n => n.Name).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "a", "b" }, names);
    }

    [Fact]
    public void SfF4_InterfaceTypedEdge_edge_degraded_in_output()
    {
        var b = new FGNodeImpl { Name = "b", Link = null };
        var a = new FGNodeImpl { Name = "a", Link = b };

        var mapper = new FGIfaceMapper();
        var result = mapper.Map(new FGIfaceRoot { Entry = a });

        // Topology degraded: Link must be null on all output DTOs.
        Assert.All(result.Nodes, dto => Assert.Null(dto.Link));
    }

    [Fact]
    public void SfF4_InterfaceTypedEdge_single_node_link_null_still_collected()
    {
        var a = new FGNodeImpl { Name = "a", Link = null };

        var mapper = new FGIfaceMapper();
        var result = mapper.Map(new FGIfaceRoot { Entry = a });

        Assert.Single(result.Nodes);
        Assert.Equal("a", result.Nodes[0].Name);
    }

    [Fact]
    public void SfF4_InterfaceTypedEdge_cycle_via_interface_terminates()
    {
        // a.Link = b, b.Link = a (cycle via IFGNode-typed property)
        var a = new FGNodeImpl { Name = "a" };
        var b = new FGNodeImpl { Name = "b" };
        a.Link = b;
        b.Link = a;

        var mapper = new FGIfaceMapper();
        var result = mapper.Map(new FGIfaceRoot { Entry = a }); // must not hang or throw
        Assert.Equal(2, result.Nodes.Count);
    }

    [Fact]
    public void SfF4_InterfaceTypedEdge_null_entry_yields_empty()
    {
        var mapper = new FGIfaceMapper();
        var result = mapper.Map(new FGIfaceRoot { Entry = null });
        Assert.Empty(result.Nodes);
    }
}
