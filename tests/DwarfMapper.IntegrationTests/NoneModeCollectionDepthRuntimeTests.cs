// SPDX-License-Identifier: GPL-2.0-only



// CA5394: System.Random is used ONLY for deterministic, seeded test-data generation. Not security.
#pragma warning disable CA5394

namespace DwarfMapper.IntegrationTests;

// None mode (default ReferenceHandling, default OnCycle=Throw): a self-referential type reached
// through a COLLECTION/DICTIONARY edge must depth-cap with a catchable DwarfMappingDepthException —
// NOT a silent StackOverflow. The project's "never a silent SO" guarantee, now honoured across
// collection edges: the shared depth-guard ctx is threaded into the (re-synthesized) collection
// element mapper for self-referential element types.

// Self-map through a list (the common case: the public Map IS the element mapper).
public class NmcTree
{
    public int V { get; set; }
    public List<NmcTree>? Kids { get; set; }
}

public class NmcTreeDto
{
    public int V { get; set; }
    public List<NmcTreeDto>? Kids { get; set; }
}

[DwarfMapper(MaxDepth = 16)]
public partial class NmcTreeMapper
{
    public partial NmcTreeDto Map(NmcTree t);
}

// Self-map through a dictionary value.
public class NmcDict
{
    public int V { get; set; }
    public Dictionary<string, NmcDict>? E { get; set; }
}

public class NmcDictDto
{
    public int V { get; set; }
    public Dictionary<string, NmcDictDto>? E { get; set; }
}

[DwarfMapper(MaxDepth = 16)]
public partial class NmcDictMapper
{
    public partial NmcDictDto Map(NmcDict n);
}

public class NoneModeCollectionDepthRuntimeTests
{
    [Fact]
    public void DeepCollectionChain_throws_DepthException_not_StackOverflow()
    {
        var head = new NmcTree { V = 0, Kids = new List<NmcTree>() };
        var cur = head;
        for (var i = 1; i < 50; i++) // 50 > MaxDepth(16)
        {
            var next = new NmcTree { V = i, Kids = new List<NmcTree>() };
            cur.Kids!.Add(next);
            cur = next;
        }

        Assert.Throws<DwarfMappingDepthException>(() => new NmcTreeMapper().Map(head));
    }

    [Fact]
    public void CyclicCollection_throws_DepthException_not_StackOverflow()
    {
        var a = new NmcTree { V = 1, Kids = new List<NmcTree>() };
        var b = new NmcTree { V = 2, Kids = new List<NmcTree>() };
        a.Kids!.Add(b);
        b.Kids!.Add(a); // cycle through the list
        Assert.Throws<DwarfMappingDepthException>(() => new NmcTreeMapper().Map(a));
    }

    [Fact]
    public void ShallowCollectionTree_maps_correctly()
    {
        var root = new NmcTree
        {
            V = 1,
            Kids = new List<NmcTree>
                { new() { V = 2, Kids = new List<NmcTree>() }, new() { V = 3, Kids = new List<NmcTree>() } }
        };
        var t = new NmcTreeMapper().Map(root);
        Assert.Equal(1, t.V);
        Assert.Equal(2, t.Kids!.Count);
        Assert.Equal(2, t.Kids[0].V);
        Assert.Equal(3, t.Kids[1].V);
    }

    [Fact]
    public void CyclicDictionary_throws_DepthException_not_StackOverflow()
    {
        var a = new NmcDict { V = 1, E = new Dictionary<string, NmcDict>() };
        var b = new NmcDict { V = 2, E = new Dictionary<string, NmcDict>() };
        a.E!["b"] = b;
        b.E!["a"] = a; // cycle through dict values
        Assert.Throws<DwarfMappingDepthException>(() => new NmcDictMapper().Map(a));
    }

    [Fact]
    public void ShallowDictionary_maps_correctly()
    {
        var root = new NmcDict
        {
            V = 1,
            E = new Dictionary<string, NmcDict> { ["x"] = new() { V = 2, E = new Dictionary<string, NmcDict>() } }
        };
        var t = new NmcDictMapper().Map(root);
        Assert.Equal(1, t.V);
        Assert.Equal(2, t.E!["x"].V);
    }

    // ── Fuzz: random List-wired graphs in None mode never silently StackOverflow ──
    // Each random graph either maps (shallow, within MaxDepth) or throws DwarfMappingDepthException.
    // A StackOverflow would crash the test host — so passing proves the depth guard reaches the
    // collection edge. Deterministic per seed.
    [Theory]
    [InlineData(11)]
    [InlineData(222)]
    [InlineData(3333)]
    [InlineData(44444)]
    public void Fuzz_random_collection_graphs_never_silently_overflow(int seed)
    {
        var rng = new Random(seed);
        for (var iter = 0; iter < 40; iter++)
        {
            var n = rng.Next(1, 14);
            var nodes = new List<NmcTree>();
            for (var i = 0; i < n; i++) nodes.Add(new NmcTree { V = i, Kids = new List<NmcTree>() });
            foreach (var node in nodes)
            {
                var edges = rng.Next(0, 4);
                for (var e = 0; e < edges; e++) node.Kids!.Add(nodes[rng.Next(n)]); // may form cycles/depth
            }

            var mapper = new NmcTreeMapper();
            // Either succeeds (finite within MaxDepth) or throws the catchable depth exception — never SO.
            var ex = Record.Exception(() => mapper.Map(nodes[0]));
            if (ex is not null) Assert.IsType<DwarfMappingDepthException>(ex);
        }
    }
}
