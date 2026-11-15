// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <b>A <c>[FlattenGraph]</c> method's pair used inside another map</b> - as a member and as a list element -
    ///     in each reference-handling mode (round 35, T15).
    ///     <para>
    ///         Without reference handling the nested use calls the method, and always did. Under Preserve and SetNull
    ///         the pair was handed to a synthesized object map, which knows nothing of the walk: the flattened
    ///         collection had no source member, and the build stopped on DWARF001 advising
    ///         <c>[MapIgnore("Nodes")]</c> - which, taken, would have emptied the list. The method now keeps its
    ///         route, as a <c>[MapDerivedType]</c> dispatch does (T13).
    ///     </para>
    ///     <para>The runtime half is <c>FlattenGraphNestedPairRuntimeTests</c> in IntegrationTests.</para>
    /// </summary>
    public class FlattenGraphNestedPairTests
    {
        private static string Source(string mode)
        {
            return $$"""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class Node { public int Id { get; set; } public Node? Next { get; set; } }
                     public class NodeDto { public int Id { get; set; } public int? NextId { get; set; } }
                     public class G    { public Node? Entry { get; set; } }
                     public class GDto { public List<NodeDto> Nodes { get; set; } = new(); }
                     public class Outer    { public G Inner { get; set; } = new(); public List<G> Many { get; set; } = new(); }
                     public class OuterDto { public GDto Inner { get; set; } = new(); public List<GDto> Many { get; set; } = new(); }
                     {{mode}}
                     public partial class M
                     {
                         [FlattenGraph(nameof(G.Entry), nameof(GDto.Nodes))]
                         public partial GDto MapG(G g);
                         public partial OuterDto Map(Outer o);
                     }
                     """;
        }

        [Theory]
        [InlineData(DispatchPairUnderReferenceHandlingTests.None)]
        [InlineData(DispatchPairUnderReferenceHandlingTests.Preserve)]
        [InlineData(DispatchPairUnderReferenceHandlingTests.SetNull)]
        public void The_nested_pair_is_mapped_by_the_flatten_method_in_every_mode(string mode)
        {
            var source = Source(mode);
            var (diagnostics, generated) = GeneratorTestHarness.Run(source);

            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            GeneratorAssert.EmitsCompilableCode(source);
            // No object map of G -> GDto stands in for the walk.
            Assert.DoesNotContain("__DwarfMap_Obj_global__Demo_G_global__Demo_GDto", generated, StringComparison.Ordinal);
        }

        // Under Preserve both nested uses reach the method through its context-taking companion, so one G is one GDto
        // wherever it is met; elsewhere the public method is the whole of it.
        [Theory]
        [InlineData(DispatchPairUnderReferenceHandlingTests.None, false)]
        [InlineData(DispatchPairUnderReferenceHandlingTests.Preserve, true)]
        [InlineData(DispatchPairUnderReferenceHandlingTests.SetNull, false)]
        public void Under_Preserve_the_nested_uses_share_the_context(string mode, bool companion)
        {
            var (_, generated) = GeneratorTestHarness.Run(Source(mode));

            Assert.Equal(companion, generated.Contains("__DwarfMap_Depth_MapG(__item, ctx, depth + 1)", StringComparison.Ordinal));
            Assert.Equal(!companion, generated.Contains("(MapG(__item))", StringComparison.Ordinal));
        }
    }
}
