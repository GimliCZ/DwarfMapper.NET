// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // A shared collection under ReferenceHandling = Preserve, as a plain map keeps it (round 35).
    //
    // This is the behaviour [FlattenGraph] cannot give a collection leaf: its node helper copies without tracking, so
    // two nodes sharing ONE source list would each get their own. That used to happen in silence for a widening
    // collection (List<int> to List<long>); it is refused and reported now, like every other collection leaf under
    // Preserve (DWARF075; FlattenGraphGeneratorTests holds the refusal, which is why no flattened mapper of this shape
    // can be declared in a project that builds with warnings as errors). What a plain map does is held here, as the
    // contract the refusal protects.
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

    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class FplMapper
    {
        public partial FplNodeDto Map(FplNode node);
    }

    public class FlattenGraphPreserveLeafTests
    {
        [Fact]
        public void A_plain_Preserve_map_keeps_a_shared_widening_list_one_list()
        {
            var shared = new List<int>
            {
                1,
                2
            };
            var dto = new FplMapper().Map(new FplNode
            {
                Name = "a",
                Wide = shared,
                Next = new FplNode
                {
                    Name = "b",
                    Wide = shared
                }
            });

            Assert.Equal([1L, 2L], dto.Wide);
            Assert.Same(dto.Wide, dto.Next!.Wide);
        }
    }
}
