// SPDX-License-Identifier: GPL-2.0-only

// ReadMaxDepth clamps [DwarfMapper(MaxDepth = n)] into [DwarfLimits.MinMaxDepth, DwarfLimits.AbsoluteMaxDepth]. The
// upper clamp was pinned; the LOWER one never executed: no fixture asked for a depth below the minimum. A depth of 0
// would make the very first recursive call throw DwarfMappingDepthException, so it is raised to the minimum (1) — the
// same value DwarfRefContext itself enforces, because both read the one linked DwarfLimits file.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class MaxDepthClampCoverageTests
    {
        [Fact]
        public void A_max_depth_below_the_minimum_is_raised_to_the_minimum()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Node { public int V { get; set; } public Node? Next { get; set; } }
                               public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                               [DwarfMapper(MaxDepth = 0)]
                               public partial class M { public partial NodeDto Map(Node n); }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            Assert.Contains("new global::DwarfMapper.DwarfRefContext(1)", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("new global::DwarfMapper.DwarfRefContext(0)", generated, StringComparison.Ordinal);
        }
    }
}
