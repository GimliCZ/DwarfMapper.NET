// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <b>DWARF118: a <c>[FlattenGraph]</c> method on a recursion cycle through one of its own node leaves is
    ///     refused</b> (round 35, T17).
    ///     <para>
    ///         The walk tracks the nodes it has visited and nothing else. A node member that is not an edge is a leaf,
    ///         converted by an ordinary call; when that call leads back to the flatten method, the walk is entered
    ///         again from inside itself. Measured, one process per case: a node holding a reference to the object
    ///         that holds the graph ended in a stack overflow without reference handling, under Preserve and under
    ///         SetNull. Without reference handling that is older than this round; under the other two it became
    ///         reachable when T15 let the nested flatten pair build.
    ///     </para>
    /// </summary>
    public class FlattenGraphLeafLeadsBackTests
    {
        /// <summary><paramref name="leaf" /> is a member of the node and of its DTO, as <c>(source, destination)</c>.</summary>
        private static string Source(string mode, (string Source, string Destination) leaf, string extraMethods = "")
        {
            return $$"""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class Tag { public string Text { get; set; } = ""; }
                     public class TagDto { public string Text { get; set; } = ""; }
                     public class Node { public int Id { get; set; } public Node? Next { get; set; } {{leaf.Source}} }
                     public class NodeDto { public int Id { get; set; } public int? NextId { get; set; } {{leaf.Destination}} }
                     public class G    { public Node? Entry { get; set; } }
                     public class GDto { public List<NodeDto> Nodes { get; set; } = new(); }
                     public class Outer    { public G Inner { get; set; } = new(); }
                     public class OuterDto { public GDto Inner { get; set; } = new(); }
                     {{mode}}
                     public partial class M
                     {
                         [FlattenGraph(nameof(G.Entry), nameof(GDto.Nodes))]
                         public partial GDto MapG(G g);
                         public partial OuterDto Map(Outer o);
                         {{extraMethods}}
                     }
                     """;
        }

        private static readonly (string, string) OwnerLeaf = ("public Outer? Owner { get; set; }", "public OuterDto? Owner { get; set; }");

        [Theory]
        [InlineData(DispatchPairUnderReferenceHandlingTests.None)]
        [InlineData(DispatchPairUnderReferenceHandlingTests.Preserve)]
        [InlineData(DispatchPairUnderReferenceHandlingTests.SetNull)]
        public void A_node_member_that_leads_back_to_the_flatten_method_is_refused_in_every_mode(string mode)
        {
            var diagnostics = GeneratorTestHarness.Run(Source(mode, OwnerLeaf)).Diagnostics;

            var refusal = Assert.Single(diagnostics, d => d.Id == "DWARF118");
            Assert.Equal(DiagnosticSeverity.Error, refusal.Severity);
            var message = refusal.GetMessage(CultureInfo.InvariantCulture);
            Assert.StartsWith("[FlattenGraph] on 'MapG': the node member 'Owner' is converted by 'Map', which leads back to 'MapG'", message, StringComparison.Ordinal);
            // On the flatten method's own line.
            Assert.Equal(15, refusal.Location.GetLineSpan().StartLinePosition.Line);
        }

        // A leaf that leads somewhere else is flattened as it always was - through a declared method too.
        [Theory]
        [InlineData(DispatchPairUnderReferenceHandlingTests.None)]
        [InlineData(DispatchPairUnderReferenceHandlingTests.SetNull)]
        public void A_node_member_that_does_not_lead_back_is_flattened(string mode)
        {
            var source = Source(mode, ("public Tag? Label { get; set; }", "public TagDto? Label { get; set; }"), "public partial TagDto MapTag(Tag t);");
            var (diagnostics, generated) = GeneratorTestHarness.Run(source);

            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            GeneratorAssert.EmitsCompilableCode(source);
            Assert.Contains("MapTag(n.Label)", generated, StringComparison.Ordinal);
        }

        // The direct case: the leaf is the flattened type itself, converted by the flatten method.
        [Fact]
        public void A_node_member_converted_by_the_flatten_method_itself_is_refused()
        {
            var diagnostics = GeneratorTestHarness.Run(Source(DispatchPairUnderReferenceHandlingTests.None,
                ("public G? Sub { get; set; }", "public GDto? Sub { get; set; }"))).Diagnostics;

            var refusal = Assert.Single(diagnostics, d => d.Id == "DWARF118");
            Assert.Contains("the node member 'Sub' is converted by 'MapG'", refusal.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }
}
