// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Golden snapshot for the heterogeneous [FlattenGraph] emitted code.
///     Run once to accept the baseline; re-run to detect regressions.
/// </summary>
public class HeteroFlattenGraphSnapshotTests
{
    [Fact]
    public Task HeteroFlattenGraph_output_is_stable()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public abstract class FsNode { public string Name { get; set; } = ""; }
                           public class Folder : FsNode { public List<FsNode> Children { get; set; } = new(); }
                           public class File   : FsNode { public long Size { get; set; } }
                           public abstract class FsNodeDto { public string Name { get; set; } = ""; }
                           public class FolderDto : FsNodeDto { public List<FsNodeDto>? Children { get; set; } }
                           public class FileDto   : FsNodeDto { public long Size { get; set; } }
                           public class Tree    { public FsNode? Root { get; set; } public string Label { get; set; } = ""; }
                           public class TreeDto { public List<FsNodeDto> Nodes { get; set; } = new(); public string Label { get; set; } = ""; }
                           [DwarfMapper]
                           public partial class M
                           {
                               [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
                               [MapDerivedType<Folder, FolderDto>]
                               [MapDerivedType<File, FileDto>]
                               public partial TreeDto Map(Tree t);
                           }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verify(generated);
    }
}
