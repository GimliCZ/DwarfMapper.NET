// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// ── Domain models ─────────────────────────────────────────────────────────────

public abstract class HFsNode
{
    public string Name { get; set; } = "";
    // Base edge: present on all derived types — inherited by Folder and HFile
    public HFsNode? Parent { get; set; }
}

public class HFolder : HFsNode
{
    public List<HFsNode> Children { get; set; } = new();
}

public class HFile : HFsNode
{
    public long Size { get; set; }
}

// ── DTO models ───────────────────────────────────────────────────────────────

public abstract class HFsNodeDto
{
    public string Name { get; set; } = "";
}

public class HFolderDto : HFsNodeDto
{
    public List<HFsNodeDto>? Children { get; set; }
    public HFsNodeDto? Parent { get; set; }
}

public class HFileDto : HFsNodeDto
{
    public long Size { get; set; }
    public HFsNodeDto? Parent { get; set; }
}

// ── Root types ───────────────────────────────────────────────────────────────

public class HTree
{
    public HFsNode? Root { get; set; }
    public string Label { get; set; } = "";
}

public class HTreeDto
{
    public List<HFsNodeDto> Nodes { get; set; } = new();
    public string Label { get; set; } = "";
}

// Array target
public class HTreeArrDto
{
    public HFsNodeDto[] Nodes { get; set; } = Array.Empty<HFsNodeDto>();
}

// Unregistered node type (to test loud failure)
public class HSymlink : HFsNode { public string Target { get; set; } = ""; }

// ── Mapper ───────────────────────────────────────────────────────────────────

[DwarfMapper]
public partial class HeteroFlattenGraphMapper
{
    [FlattenGraph(nameof(HTree.Root), nameof(HTreeDto.Nodes))]
    [MapDerivedType<HFolder, HFolderDto>]
    [MapDerivedType<HFile, HFileDto>]
    public partial HTreeDto Map(HTree t);

    [FlattenGraph(nameof(HTree.Root), nameof(HTreeArrDto.Nodes))]
    [MapDerivedType<HFolder, HFolderDto>]
    [MapDerivedType<HFile, HFileDto>]
    public partial HTreeArrDto MapArr(HTree t);
}

public class HeteroFlattenGraphRuntimeTests
{
    private readonly HeteroFlattenGraphMapper _mapper = new();

    // ── 1. Null entry → empty collection ─────────────────────────────────────

    [Fact]
    public void HeteroFG_null_entry_yields_empty_collection()
    {
        var tree = new HTree { Root = null, Label = "empty" };
        var result = _mapper.Map(tree);
        Assert.Empty(result.Nodes);
        Assert.Equal("empty", result.Label);
    }

    // ── 2. Single File node → correct type, leaf preserved, edge nulled ───────

    [Fact]
    public void HeteroFG_single_file_node_correct_type_and_values()
    {
        var file = new HFile { Name = "readme.txt", Size = 1024L };
        var tree = new HTree { Root = file, Label = "root" };
        var result = _mapper.Map(tree);

        Assert.Single(result.Nodes);
        var dto = Assert.IsType<HFileDto>(result.Nodes[0]);
        Assert.Equal("readme.txt", dto.Name);
        Assert.Equal(1024L, dto.Size);
        Assert.Null(dto.Parent);  // edge degraded
        Assert.Equal("root", result.Label);
    }

    // ── 3. Single Folder node → correct type, edge nulled ────────────────────

    [Fact]
    public void HeteroFG_single_folder_node_correct_type_and_values()
    {
        var folder = new HFolder { Name = "src" };
        var tree = new HTree { Root = folder };
        var result = _mapper.Map(tree);

        Assert.Single(result.Nodes);
        var dto = Assert.IsType<HFolderDto>(result.Nodes[0]);
        Assert.Equal("src", dto.Name);
        Assert.Null(dto.Children);  // edge degraded
        Assert.Null(dto.Parent);    // base edge degraded
    }

    // ── 4. Folder with two File children: all 3 nodes collected, correct types ─

    [Fact]
    public void HeteroFG_folder_with_files_all_nodes_collected_correct_types()
    {
        var f1 = new HFile { Name = "a.txt", Size = 10L };
        var f2 = new HFile { Name = "b.txt", Size = 20L };
        var folder = new HFolder { Name = "docs" };
        folder.Children.Add(f1);
        folder.Children.Add(f2);

        var tree = new HTree { Root = folder };
        var result = _mapper.Map(tree);

        Assert.Equal(3, result.Nodes.Count);

        var folders = result.Nodes.OfType<HFolderDto>().ToList();
        var files = result.Nodes.OfType<HFileDto>().ToList();

        Assert.Single(folders);
        Assert.Equal(2, files.Count);

        Assert.Equal("docs", folders[0].Name);
        var sizes = files.Select(f => f.Size).OrderBy(s => s).ToList();
        Assert.Equal(new[] { 10L, 20L }, sizes);
    }

    // ── 5. Nested Folders + Files: entire hierarchy flattened ────────────────

    [Fact]
    public void HeteroFG_deep_tree_all_nodes_flattened()
    {
        var f1 = new HFile { Name = "inner.txt", Size = 5L };
        var inner = new HFolder { Name = "inner" };
        inner.Children.Add(f1);
        var outer = new HFolder { Name = "outer" };
        outer.Children.Add(inner);

        var tree = new HTree { Root = outer };
        var result = _mapper.Map(tree);

        Assert.Equal(3, result.Nodes.Count);
        var names = result.Nodes.Select(n => n.Name).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "inner", "inner.txt", "outer" }, names);
    }

    // ── 6. Diamond: File reachable from two Folders → collected once ──────────

    [Fact]
    public void HeteroFG_diamond_file_collected_once()
    {
        var shared = new HFile { Name = "shared.bin", Size = 999L };
        var f1 = new HFolder { Name = "left" };
        var f2 = new HFolder { Name = "right" };
        f1.Children.Add(shared);
        f2.Children.Add(shared); // same ref from both
        var root = new HFolder { Name = "root" };
        root.Children.Add(f1);
        root.Children.Add(f2);

        var tree = new HTree { Root = root };
        var result = _mapper.Map(tree);

        // root, left, right, shared (once)
        Assert.Equal(4, result.Nodes.Count);
        var sharedDtos = result.Nodes.OfType<HFileDto>().ToList();
        Assert.Single(sharedDtos);
        Assert.Equal("shared.bin", sharedDtos[0].Name);
    }

    // ── 7. Cross-type cycle: Folder→File→Folder via Parent → terminates ───────

    [Fact]
    public void HeteroFG_cross_type_cycle_terminates_each_node_once()
    {
        var folder = new HFolder { Name = "parent" };
        var file = new HFile { Name = "child.txt", Size = 1L };
        folder.Children.Add(file);
        file.Parent = folder; // cross-type back-edge

        var tree = new HTree { Root = folder };
        var result = _mapper.Map(tree);

        // Must terminate and collect each node exactly once
        Assert.Equal(2, result.Nodes.Count);
        Assert.Single(result.Nodes.OfType<HFolderDto>());
        Assert.Single(result.Nodes.OfType<HFileDto>());

        // Edges must be degraded
        var folderDto = result.Nodes.OfType<HFolderDto>().Single();
        var fileDto = result.Nodes.OfType<HFileDto>().Single();
        Assert.Null(folderDto.Children);
        Assert.Null(fileDto.Parent);
    }

    // ── 8. Base-edge (Parent) traversal: File.Parent points to Folder ─────────

    [Fact]
    public void HeteroFG_base_edge_traversed_reaches_parent_folder()
    {
        var folder = new HFolder { Name = "dir" };
        var file = new HFile { Name = "file.txt", Size = 42L };
        // Only set file.Parent — folder NOT in children (edge only from file)
        file.Parent = folder;

        var tree = new HTree { Root = file }; // start from file, parent reached via Parent edge
        var result = _mapper.Map(tree);

        // file + folder (via file.Parent) = 2 nodes
        Assert.Equal(2, result.Nodes.Count);
        Assert.Single(result.Nodes.OfType<HFileDto>());
        Assert.Single(result.Nodes.OfType<HFolderDto>());
    }

    // ── 9. Unregistered runtime type → ArgumentException ─────────────────────

    [Fact]
    public void HeteroFG_unregistered_runtime_type_throws_ArgumentException()
    {
        var symlink = new HSymlink { Name = "link.lnk", Target = "/etc" };
        var tree = new HTree { Root = symlink };

        Assert.Throws<ArgumentException>(() => _mapper.Map(tree));
    }

    // ── 10. Null edge skipped without NRE ─────────────────────────────────────

    [Fact]
    public void HeteroFG_null_collection_edge_skipped()
    {
        // Children is empty, Parent is null — no NPE
        var folder = new HFolder { Name = "empty-dir" };
        var tree = new HTree { Root = folder };
        var result = _mapper.Map(tree);
        Assert.Single(result.Nodes);
    }

    // ── 11. Mixed edge kinds: single-ref Parent + collection Children ─────────

    [Fact]
    public void HeteroFG_mixed_edge_kinds_both_traversed()
    {
        var child = new HFile { Name = "c.txt", Size = 1L };
        var parent = new HFolder { Name = "par" };
        var middle = new HFolder { Name = "mid" };
        middle.Children.Add(child);
        middle.Parent = parent; // single-ref edge to parent

        var tree = new HTree { Root = middle };
        var result = _mapper.Map(tree);

        // middle, child, parent = 3 nodes
        Assert.Equal(3, result.Nodes.Count);
        Assert.Equal(2, result.Nodes.OfType<HFolderDto>().Count());
        Assert.Single(result.Nodes.OfType<HFileDto>());
    }

    // ── 12. Array target works ────────────────────────────────────────────────

    [Fact]
    public void HeteroFG_array_target_works()
    {
        var file = new HFile { Name = "test.bin", Size = 8L };
        var folder = new HFolder { Name = "bin" };
        folder.Children.Add(file);

        var tree = new HTree { Root = folder };
        var result = _mapper.MapArr(tree);

        Assert.Equal(2, result.Nodes.Length);
        Assert.Single(result.Nodes.OfType<HFolderDto>());
        Assert.Single(result.Nodes.OfType<HFileDto>());
    }

    // ── 13. Root other members mapped normally ────────────────────────────────

    [Fact]
    public void HeteroFG_root_other_members_mapped_normally()
    {
        var file = new HFile { Name = "x.txt", Size = 1L };
        var tree = new HTree { Root = file, Label = "my-tree" };
        var result = _mapper.Map(tree);

        Assert.Equal("my-tree", result.Label);
    }

    // ── 14. Self-loop (Folder.Parent = itself) → terminates ──────────────────

    [Fact]
    public void HeteroFG_self_loop_folder_terminates()
    {
        var folder = new HFolder { Name = "self" };
        folder.Parent = folder; // self-loop via base edge
        var tree = new HTree { Root = folder };
        var result = _mapper.Map(tree);
        Assert.Single(result.Nodes);
    }
}
