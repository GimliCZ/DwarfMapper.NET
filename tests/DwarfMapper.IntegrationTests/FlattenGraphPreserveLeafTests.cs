// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // A collection LEAF of a flattened node under ReferenceHandling = Preserve (round 35; the round-33 fast-path
    // audit's untested cell).
    //
    // [FlattenGraph] maps a node's data members through a helper that does no reference tracking. A leaf whose
    // conversion needs one of the tracked helpers is refused and reported (DWARF075: an int[] member, for one). A
    // widening collection leaf - List<int> to List<long> - needs none, so it IS flattened, each node getting its own
    // copy. What that gives up is pinned here on purpose, because nothing reports it: two nodes that share ONE source
    // list map to TWO target lists, where the same mapper's plain map keeps them one. Kept on purpose (owner decision
    // 9, 2026-11-13: flattening already gives up the graph's shape, and separate copies never link what was not
    // linked) and stated in docs/diagnostics.md under DWARF075; this test holds the documented behaviour.
    public class FplNode
    {
        public string Name { get; set; } = "";

        public List<int> Wide { get; set; } = new();

        public FplNode? Next { get; set; }
    }

    public class FplNodeDto
    {
        public string Name { get; set; } = "";

        public List<long> Wide { get; set; } = new();

        public FplNodeDto? Next { get; set; }
    }

    public class FplRoot
    {
        public FplNode? Entry { get; set; }
    }

    public class FplRootDto
    {
        public List<FplNodeDto> Nodes { get; set; } = new();
    }

    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class FplMapper
    {
        public partial FplNodeDto Map(FplNode node);

        [FlattenGraph(nameof(FplRoot.Entry), nameof(FplRootDto.Nodes))]
        public partial FplRootDto Flatten(FplRoot root);
    }

    public class FlattenGraphPreserveLeafTests
    {
        private static FplNode TwoNodesSharingOneList()
        {
            var shared = new List<int>
            {
                1,
                2
            };
            return new FplNode
            {
                Name = "a",
                Wide = shared,
                Next = new FplNode
                {
                    Name = "b",
                    Wide = shared
                }
            };
        }

        [Fact]
        public void A_plain_Preserve_map_keeps_a_shared_widening_list_one_list()
        {
            var dto = new FplMapper().Map(TwoNodesSharingOneList());

            Assert.Equal([1L, 2L], dto.Wide);
            Assert.Same(dto.Wide, dto.Next!.Wide);
        }

        [Fact]
        public void A_flattened_node_gets_its_own_copy_of_a_shared_widening_list()
        {
            var dto = new FplMapper().Flatten(new FplRoot
            {
                Entry = TwoNodesSharingOneList()
            });

            Assert.Equal(2, dto.Nodes.Count);
            Assert.Equal([1L, 2L], dto.Nodes[0].Wide);
            Assert.Equal([1L, 2L], dto.Nodes[1].Wide);
            // The values are right in both; the sharing is not carried over.
            Assert.NotSame(dto.Nodes[0].Wide, dto.Nodes[1].Wide);
        }
    }
}
