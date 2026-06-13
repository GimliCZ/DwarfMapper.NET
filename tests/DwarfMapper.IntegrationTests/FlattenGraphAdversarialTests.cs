// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// ── Top-level collection target-type matrix ───────────────────────────────────

public class TlcItem { public int Id { get; set; } public string Name { get; set; } = ""; }
public class TlcItemDto { public int Id { get; set; } public string Name { get; set; } = ""; }
public abstract class TlcAnimal { public string Name { get; set; } = ""; }
public class TlcDog : TlcAnimal { public string Breed { get; set; } = ""; }
public class TlcCat : TlcAnimal { public int Lives { get; set; } }
public class TlcAnimalDto { public string Name { get; set; } = ""; }
public class TlcDogDto : TlcAnimalDto { public string Breed { get; set; } = ""; }
public class TlcCatDto : TlcAnimalDto { public int Lives { get; set; } }

[DwarfMapper]
public partial class TlcMapper
{
    // List<T> → List<T>
    public partial List<TlcItemDto> Map(List<TlcItem> src);
    // List<T> → IReadOnlyList<T>
    public partial IReadOnlyList<TlcItemDto> MapRol(List<TlcItem> src);

    public partial TlcItemDto Map(TlcItem i);
}

// Polymorphic animal mapper (separate to avoid top-level collection + dispatch conflict)
[DwarfMapper]
public partial class TlcAnimalMapper
{
    [MapDerivedType<TlcDog, TlcDogDto>]
    [MapDerivedType<TlcCat, TlcCatDto>]
    public partial TlcAnimalDto Map(TlcAnimal a);

    public partial TlcDogDto Map(TlcDog d);
    public partial TlcCatDto Map(TlcCat c);
}

// HashSet<T> → List<T>  (separate mapper to avoid conflicts)
[DwarfMapper]
public partial class TlcHashSetMapper
{
    public partial List<TlcItemDto> Map(HashSet<TlcItem> src);
    public partial TlcItemDto Map(TlcItem i);
}

// Dictionary → IReadOnlyDictionary  (separate mapper)
public class TlcKey { public string K { get; set; } = ""; }
public class TlcValue { public int V { get; set; } }
public class TlcKeyDto { public string K { get; set; } = ""; }
public class TlcValueDto { public int V { get; set; } }

[DwarfMapper]
public partial class TlcDictMapper
{
    public partial IReadOnlyDictionary<TlcKeyDto, TlcValueDto> Map(Dictionary<TlcKey, TlcValue> src);
    public partial TlcKeyDto Map(TlcKey k);
    public partial TlcValueDto Map(TlcValue v);
}

public class TopLevelCollectionMatrixTests
{
    private readonly TlcMapper _m = new();
    private readonly TlcHashSetMapper _hsm = new();
    private readonly TlcDictMapper _dm = new();
    private readonly TlcAnimalMapper _am = new();

    // ── 1. List<T> → List<T> ─────────────────────────────────────────────────
    [Fact]
    public void ListToList_maps_all_elements()
    {
        var src = new List<TlcItem>
        {
            new() { Id = 1, Name = "Alpha" },
            new() { Id = 2, Name = "Beta" },
        };
        var result = _m.Map(src);
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal("Alpha", result[0].Name);
        Assert.Equal(2, result[1].Id);
    }

    // ── 2. List<T> → IReadOnlyList<T> ────────────────────────────────────────
    [Fact]
    public void ListToIReadOnlyList_maps_all_elements()
    {
        var src = new List<TlcItem> { new() { Id = 3, Name = "Gamma" } };
        IReadOnlyList<TlcItemDto> result = _m.MapRol(src);
        Assert.Single(result);
        Assert.Equal(3, result[0].Id);
        Assert.Equal("Gamma", result[0].Name);
    }

    // ── 3. HashSet<T> → List<T> ──────────────────────────────────────────────
    [Fact]
    public void HashSetToList_maps_all_elements()
    {
        var src = new HashSet<TlcItem>
        {
            new() { Id = 7, Name = "G" },
            new() { Id = 8, Name = "H" },
        };
        var result = _hsm.Map(src);
        Assert.Equal(2, result.Count);
        var ids = result.Select(x => x.Id).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 7, 8 }, ids);
    }

    // ── 4. Dictionary → IReadOnlyDictionary ──────────────────────────────────
    [Fact]
    public void DictionaryToIReadOnlyDictionary_maps_all_pairs()
    {
        var k1 = new TlcKey { K = "a" };
        var k2 = new TlcKey { K = "b" };
        var src = new Dictionary<TlcKey, TlcValue>
        {
            [k1] = new() { V = 10 },
            [k2] = new() { V = 20 },
        };
        var result = _dm.Map(src);
        Assert.Equal(2, result.Count);
        var vals = result.Values.Select(v => v.V).OrderBy(v => v).ToList();
        Assert.Equal(new[] { 10, 20 }, vals);
    }

    // ── 5. Polymorphic Animal → derived DTO — runtime dispatch ───────────────
    [Fact]
    public void PolyAnimal_each_element_dispatched_to_derived_dto()
    {
        var animals = new List<TlcAnimal>
        {
            new TlcDog { Name = "Rex", Breed = "Husky" },
            new TlcCat { Name = "Luna", Lives = 9 },
            new TlcDog { Name = "Buddy", Breed = "Lab" },
        };
        var results = animals.ConvertAll(a => _am.Map(a));
        Assert.Equal(3, results.Count);
        var dog1 = Assert.IsType<TlcDogDto>(results[0]);
        Assert.Equal("Husky", dog1.Breed);   // derived-only member
        var cat = Assert.IsType<TlcCatDto>(results[1]);
        Assert.Equal(9, cat.Lives);           // derived-only member
        var dog2 = Assert.IsType<TlcDogDto>(results[2]);
        Assert.Equal("Lab", dog2.Breed);
    }

    // ── 6. Null source list → ArgumentNullException ───────────────────────────
    [Fact]
    public void Null_source_list_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _m.Map((List<TlcItem>)null!));
    }

    // ── 7. Empty source list → empty result ──────────────────────────────────
    [Fact]
    public void Empty_source_list_yields_empty_result()
    {
        var result = _m.Map(new List<TlcItem>());
        Assert.Empty(result);
    }
}

// ── FlattenGraph edge-kind matrix ─────────────────────────────────────────────

// Source node with all five edge kinds
public class EkNode
{
    public string Name { get; set; } = "";
    public EkNode? SingleRef { get; set; }                // single reference
    public List<EkNode> ListEdge { get; set; } = new();   // List<T>
    public EkNode[] ArrayEdge { get; set; } = Array.Empty<EkNode>();  // T[]
    public HashSet<EkNode> SetEdge { get; set; } = new(); // HashSet<T>
    public Dictionary<string, EkNode> DictEdge { get; set; } = new(); // Dictionary<K,Node>
}

public class EkNodeDto
{
    public string Name { get; set; } = "";
    public EkNodeDto? SingleRef { get; set; }
    public List<EkNodeDto>? ListEdge { get; set; }
    public EkNodeDto[]? ArrayEdge { get; set; }
    public HashSet<EkNodeDto>? SetEdge { get; set; }
    public Dictionary<string, EkNodeDto>? DictEdge { get; set; }
}

public class EkRoot { public EkNode? Entry { get; set; } }
public class EkRootDto { public List<EkNodeDto> Nodes { get; set; } = new(); }

[DwarfMapper]
public partial class EkMapper
{
    [FlattenGraph(nameof(EkRoot.Entry), nameof(EkRootDto.Nodes))]
    public partial EkRootDto Map(EkRoot r);
}

// Interface/base-typed edge traversal (homo)
public class IEkBase { public string Name { get; set; } = ""; public IEkBase? Parent { get; set; } }
public class IEkConcrete : IEkBase { public int Extra { get; set; } }
public class IEkBaseDto { public string Name { get; set; } = ""; public IEkBaseDto? Parent { get; set; } }
public class IEkConcreteDto : IEkBaseDto { public int Extra { get; set; } }
public class IEkRoot { public IEkBase? Entry { get; set; } }
public class IEkRootDto { public List<IEkBaseDto> Nodes { get; set; } = new(); }

[DwarfMapper]
public partial class IEkMapper
{
    [FlattenGraph(nameof(IEkRoot.Entry), nameof(IEkRootDto.Nodes))]
    public partial IEkRootDto Map(IEkRoot r);
}

public class FlattenGraphEdgeKindMatrixTests
{
    private readonly EkMapper _m = new();
    private readonly IEkMapper _im = new();

    // ── 1. Single-reference edge traversed ───────────────────────────────────
    [Fact]
    public void EdgeKind_SingleRef_traversed()
    {
        var b = new EkNode { Name = "B" };
        var a = new EkNode { Name = "A", SingleRef = b };
        var result = _m.Map(new EkRoot { Entry = a });
        Assert.Equal(2, result.Nodes.Count);
        var names = result.Nodes.Select(n => n.Name).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "A", "B" }, names);
        Assert.All(result.Nodes, n => Assert.Null(n.SingleRef));  // edges degraded
    }

    // ── 2. List<Node> edge traversed ─────────────────────────────────────────
    [Fact]
    public void EdgeKind_ListEdge_traversed()
    {
        var c1 = new EkNode { Name = "C1" };
        var c2 = new EkNode { Name = "C2" };
        var a = new EkNode { Name = "A" };
        a.ListEdge.Add(c1);
        a.ListEdge.Add(c2);
        var result = _m.Map(new EkRoot { Entry = a });
        Assert.Equal(3, result.Nodes.Count);
        Assert.All(result.Nodes, n => Assert.Null(n.ListEdge));
    }

    // ── 3. Node[] edge traversed ──────────────────────────────────────────────
    [Fact]
    public void EdgeKind_ArrayEdge_traversed()
    {
        var d1 = new EkNode { Name = "D1" };
        var d2 = new EkNode { Name = "D2" };
        var a = new EkNode { Name = "A", ArrayEdge = new[] { d1, d2 } };
        var result = _m.Map(new EkRoot { Entry = a });
        Assert.Equal(3, result.Nodes.Count);
        Assert.All(result.Nodes, n => Assert.Null(n.ArrayEdge));
    }

    // ── 4. HashSet<Node> edge traversed ──────────────────────────────────────
    [Fact]
    public void EdgeKind_HashSetEdge_traversed()
    {
        var e1 = new EkNode { Name = "E1" };
        var e2 = new EkNode { Name = "E2" };
        var a = new EkNode { Name = "A", SetEdge = new HashSet<EkNode> { e1, e2 } };
        var result = _m.Map(new EkRoot { Entry = a });
        Assert.Equal(3, result.Nodes.Count);
        Assert.All(result.Nodes, n => Assert.Null(n.SetEdge));
    }

    // ── 5. Dictionary<K,Node> values traversed ───────────────────────────────
    // NOTE: If this test FAILS with count < 3 (dictionary values not traversed),
    // that is a REAL generator bug S2 — STOP and report.
    [Fact]
    public void EdgeKind_DictEdge_values_traversed()
    {
        var f1 = new EkNode { Name = "F1" };
        var f2 = new EkNode { Name = "F2" };
        var a = new EkNode
        {
            Name = "A",
            DictEdge = new Dictionary<string, EkNode>
            {
                ["k1"] = f1,
                ["k2"] = f2,
            }
        };
        var result = _m.Map(new EkRoot { Entry = a });
        Assert.Equal(3, result.Nodes.Count);
        var names = result.Nodes.Select(n => n.Name).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "A", "F1", "F2" }, names);
    }

    // ── 6. Interface/base-typed edge traversed (homo) ─────────────────────────
    [Fact]
    public void EdgeKind_InterfaceBaseEdge_traversed_homo()
    {
        var child = new IEkConcrete { Name = "Child", Extra = 7 };
        var parent = new IEkConcrete { Name = "Parent", Extra = 99 };
        // child.Parent is base-typed IEkBase but holds an IEkConcrete
        child.Parent = parent;
        var result = _im.Map(new IEkRoot { Entry = child });
        Assert.Equal(2, result.Nodes.Count);
        var names = result.Nodes.Select(n => n.Name).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "Child", "Parent" }, names);
        Assert.All(result.Nodes, n => Assert.Null(n.Parent));  // edges degraded
    }
}

// ── Golden fixtures ────────────────────────────────────────────────────────────

public class FlattenGraphGoldenFixtureTests
{
    private readonly FGMapper _m = new();

    // ── 1. Deep chain 50 nodes + back-edge cycle → exactly 50, edges null ────
    [Fact]
    public void DeepChain_50nodes_back_cycle_collected_exactly_50()
    {
        const int N = 50;
        var nodes = Enumerable.Range(0, N)
            .Select(i => new FGNode { Name = $"N{i:D2}", Value = i })
            .ToArray();
        // Linear chain
        for (var i = 0; i < N - 1; i++)
            nodes[i].X = nodes[i + 1];
        // Back-edge: last→first (cycle)
        nodes[N - 1].X = nodes[0];

        var root = new FGRoot { Entry = nodes[0], Tag = "deep" };
        var result = _m.Map(root);

        Assert.Equal(N, result.Nodes.Count);
        Assert.Equal("deep", result.Tag);
        Assert.All(result.Nodes, n => Assert.Null(n.X));
        Assert.All(result.Nodes, n => Assert.Null(n.Y));
    }

    // ── 2. Wide graph: root → 20 children → 5 grandchildren each = 121 nodes ─
    [Fact]
    public void WideGraph_root_20children_5grandchildren_each_121_total()
    {
        const int ChildCount = 20;
        const int GrandChildCount = 5;
        var expected = 1 + ChildCount + ChildCount * GrandChildCount; // 121

        var grandChildren = Enumerable.Range(0, ChildCount * GrandChildCount)
            .Select(i => new FGCollNode { Name = $"G{i:D3}" })
            .ToArray();
        var children = Enumerable.Range(0, ChildCount)
            .Select(i =>
            {
                var c = new FGCollNode { Name = $"C{i:D2}" };
                for (var g = 0; g < GrandChildCount; g++)
                    c.Children.Add(grandChildren[i * GrandChildCount + g]);
                return c;
            })
            .ToArray();
        var rootNode = new FGCollNode { Name = "Root" };
        foreach (var c in children)
            rootNode.Children.Add(c);

        var result = _m.MapColl(new FGCollRoot { Entry = rootNode });

        Assert.Equal(expected, result.Nodes.Count);
        Assert.All(result.Nodes, n => Assert.Null(n.Children));
    }
}
