// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Linq;
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// Tree node whose Children can contain an ancestor (cycle through a collection).
public class TNode { public string Name { get; set; } = ""; public List<TNode> Children { get; set; } = new(); }
public class TNodeDto { public string Name { get; set; } = ""; public List<TNodeDto> Children { get; set; } = new(); }

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve, MaxDepth = 64)]
public partial class TreeGraphMapper { public partial TNodeDto Map(TNode n); }

// Two parents sharing the SAME list instance.
public class SharingRoot { public Holder P1 { get; set; } = new(); public Holder P2 { get; set; } = new(); }
public class Holder { public List<int> Items { get; set; } = new(); }
public class SharingRootDto { public HolderDto P1 { get; set; } = new(); public HolderDto P2 { get; set; } = new(); }
public class HolderDto { public List<int> Items { get; set; } = new(); }

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class SharingMapper { public partial SharingRootDto Map(SharingRoot r); }

// Diamond of plain objects (NO back-edges — purely non-cyclic shared instance).
// root.Left and root.Right reference the SAME Leaf instance; Leaf has no pointer back.
// This is the exact shape of the C2/C2b bug: Leaf is not recursion-capable, but
// under Preserve its mapper must still receive ctx so the shared instance dedupes.
public class PlainLeaf { public int V { get; set; } }
public class PlainDiamondRoot { public PlainLeaf? Left { get; set; } public PlainLeaf? Right { get; set; } }
public class PlainLeafDto { public int V { get; set; } }
public class PlainDiamondRootDto { public PlainLeafDto? Left { get; set; } public PlainLeafDto? Right { get; set; } }

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class PlainDiamondMapper { public partial PlainDiamondRootDto Map(PlainDiamondRoot r); }

public class SharedObjectGraphRuntimeTests
{
    [Fact]
    public void Cycle_through_collection_reconstructs_not_throws()
    {
        var root = new TNode { Name = "root" };
        var child = new TNode { Name = "child" };
        root.Children.Add(child);
        child.Children.Add(root); // cycle: child -> root via collection

        var dto = new TreeGraphMapper().Map(root);
        Assert.Equal("root", dto.Name);
        var childDto = dto.Children.Single();
        Assert.Equal("child", childDto.Name);
        // The cycle is closed: child's child IS the root target instance.
        Assert.Same(dto, childDto.Children.Single());
    }

    [Fact]
    public void Shared_list_maps_to_one_target_instance()
    {
        var shared = new List<int> { 1, 2, 3 };
        var holder = new Holder { Items = shared };
        var root = new SharingRoot { P1 = holder, P2 = holder }; // P1 and P2 are the SAME holder

        var dto = new SharingMapper().Map(root);
        // Same holder → same target holder, and the same target list.
        Assert.Same(dto.P1, dto.P2);
        Assert.Same(dto.P1.Items, dto.P2.Items);
        Assert.Equal(new[] { 1, 2, 3 }, dto.P1.Items);
    }

    /// <summary>
    /// Diamond of plain objects under Preserve — no back-edges, no cycles, purely shared.
    /// The shared Leaf instance (root.Left == root.Right) must map to ONE target Leaf instance
    /// (Assert.Same). This is the exact class of the C2/C2b bug: the Leaf mapper was NOT
    /// recursion-capable (no type-graph cycle) yet needed ctx to thread the identity map.
    /// </summary>
    [Fact]
    public void Diamond_plain_objects_non_cyclic_dedupes_to_one_target_instance()
    {
        var leaf = new PlainLeaf { V = 42 };
        var root = new PlainDiamondRoot { Left = leaf, Right = leaf }; // SAME instance — no back-edge

        var dto = new PlainDiamondMapper().Map(root);

        Assert.NotNull(dto.Left);
        Assert.NotNull(dto.Right);
        // One source leaf → one target leaf (reference-identity preserved).
        Assert.Same(dto.Left, dto.Right);
        Assert.Equal(42, dto.Left!.V);
    }
}
