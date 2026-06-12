// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── Preserve: self-referential node → TryGetReference/SetReference ────────
    [Fact]
    public Task Snap_Graph_Preserve_SelfRef_Node()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Node    { public int V { get; set; } public Node? Next { get; set; } }
            public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
            [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
            public partial class M { public partial NodeDto Map(Node n); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Depth-guarded recursive type (None mode) ──────────────────────────────
    [Fact]
    public Task Snap_Graph_DepthGuarded_Recursive_None()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Tree    { public int Val { get; set; } public Tree? Left { get; set; } public Tree? Right { get; set; } }
            public class TreeDto { public int Val { get; set; } public TreeDto? Left { get; set; } public TreeDto? Right { get; set; } }
            [DwarfMapper]
            public partial class M { public partial TreeDto Map(Tree t); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
