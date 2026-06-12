// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── FlattenGraph: linear chain A→B→C, edges degraded ─────────────────────
    [Fact]
    public Task Snap_FlattenGraph_LinearChain()
    {
        const string src = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class Node    { public string Name { get; set; } = ""; public Node? Next { get; set; } }
            public class NodeDto { public string Name { get; set; } = ""; public NodeDto? Next { get; set; } }
            public class Root    { public Node? Entry { get; set; } public string Tag { get; set; } = ""; }
            public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); public string Tag { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [FlattenGraph("Entry", "Nodes")]
                public partial RootDto Map(Root r);
            }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
