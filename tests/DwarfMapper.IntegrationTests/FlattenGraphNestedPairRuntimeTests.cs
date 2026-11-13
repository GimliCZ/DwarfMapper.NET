// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    public class FgnNode
    {
        public int Id { get; set; }

        public FgnNode? Next { get; set; }
    }

    public class FgnNodeDto
    {
        public int Id { get; set; }

        public int? NextId { get; set; }
    }

    public class FgnGraph
    {
        public FgnNode? Entry { get; set; }
    }

    public class FgnGraphDto
    {
        public List<FgnNodeDto> Nodes { get; set; } = new();
    }

    public class FgnOuter
    {
        public FgnGraph Inner { get; set; } = new();

        public List<FgnGraph> Many { get; set; } = new();
    }

    public class FgnOuterDto
    {
        public FgnGraphDto Inner { get; set; } = new();

        public List<FgnGraphDto> Many { get; set; } = new();
    }

    [DwarfMapper]
    public partial class FgnNoneMapper
    {
        [FlattenGraph(nameof(FgnGraph.Entry), nameof(FgnGraphDto.Nodes))]
        public partial FgnGraphDto MapGraph(FgnGraph g);

        public partial FgnOuterDto Map(FgnOuter o);
    }

    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class FgnPreserveMapper
    {
        [FlattenGraph(nameof(FgnGraph.Entry), nameof(FgnGraphDto.Nodes))]
        public partial FgnGraphDto MapGraph(FgnGraph g);

        public partial FgnOuterDto Map(FgnOuter o);
    }

    [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
    public partial class FgnSetNullMapper
    {
        [FlattenGraph(nameof(FgnGraph.Entry), nameof(FgnGraphDto.Nodes))]
        public partial FgnGraphDto MapGraph(FgnGraph g);

        public partial FgnOuterDto Map(FgnOuter o);
    }

    /// <summary>
    ///     <b>A <c>[FlattenGraph]</c> method's pair nested in another map is flattened there too, in every mode</b>
    ///     (round 35, T15). Under Preserve and SetNull these two mappers did not build: DWARF001 on <c>Nodes</c>,
    ///     advising an ignore that would have emptied the list.
    /// </summary>
    public class FlattenGraphNestedPairRuntimeTests
    {
        /// <summary>Two nodes; <paramref name="cyclic" /> closes them into a ring, which the walk must still end.</summary>
        private static FgnOuter Outer(bool cyclic)
        {
            var first = new FgnNode { Id = 1 };
            var second = new FgnNode { Id = 2 };
            first.Next = second;
            if (cyclic) { second.Next = first; }

            var graph = new FgnGraph { Entry = first };
            return new FgnOuter { Inner = graph, Many = { graph, graph } };
        }

        private static void AssertFlattened(FgnOuterDto dto)
        {
            Assert.Equal([1, 2], dto.Inner.Nodes.Select(n => n.Id));
            Assert.Equal(2, dto.Many.Count);
            Assert.All(dto.Many, g => Assert.Equal([1, 2], g.Nodes.Select(n => n.Id)));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Without_reference_handling_the_nested_graph_is_flattened(bool cyclic)
        {
            AssertFlattened(new FgnNoneMapper().Map(Outer(cyclic)));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Under_SetNull_the_nested_graph_is_flattened(bool cyclic)
        {
            AssertFlattened(new FgnSetNullMapper().Map(Outer(cyclic)));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Under_Preserve_the_nested_graph_is_flattened_once_and_shared(bool cyclic)
        {
            var dto = new FgnPreserveMapper().Map(Outer(cyclic));

            AssertFlattened(dto);
            // One source graph, one target, whichever edge reached it.
            Assert.Same(dto.Many[0], dto.Many[1]);
            Assert.Same(dto.Inner, dto.Many[0]);
        }
    }
}
