// SPDX-License-Identifier: GPL-2.0-only

using System.Text;
using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.Core
{
    /// <summary>
    ///     <see cref="ImmutabilityProof.Classify" />'s refusals for the shapes the generator never hands it:
    ///     <list type="bullet">
    ///         <item>
    ///             a POINTER. It is unmanaged, so it gets past the unmanaged-type fast path, and it is refused as a
    ///             shape the proof cannot see through;
    ///         </item>
    ///         <item><c>dynamic</c>, refused in the same words;</item>
    ///         <item>
    ///             a graph wider than the visit budget, which answers Unprovable rather than Proven. Running out of
    ///             budget is not evidence of anything.
    ///         </item>
    ///     </list>
    /// </summary>
    public class ImmutabilityProofClassifyArmTests
    {
        private static CSharpCompilation Compile(string source) =>
            GeneratorTestHarness.BuildCompilation("ImmutabilityClassifyArms", [CSharpSyntaxTree.ParseText(source)]);

        [Fact]
        public void A_pointer_is_unprovable_even_though_it_is_unmanaged()
        {
            var compilation = Compile("namespace T;");
            var pointer = compilation.CreatePointerTypeSymbol(compilation.GetSpecialType(SpecialType.System_Int32));
            Assert.True(pointer.IsUnmanagedType);

            var verdict = ImmutabilityProof.Classify(pointer, out var reason);

            Assert.Equal(ImmutabilityVerdict.Unprovable, verdict);
            Assert.Equal("'int*' is a shape the immutability proof cannot see through", reason);
        }

        [Fact]
        public void Dynamic_is_unprovable()
        {
            var compilation = Compile("namespace T;");

            var verdict = ImmutabilityProof.Classify(compilation.DynamicType, out var reason);

            Assert.Equal(ImmutabilityVerdict.Unprovable, verdict);
            Assert.Equal("'dynamic' is a shape the immutability proof cannot see through", reason);
        }

        [Fact]
        public void A_graph_wider_than_the_visit_budget_is_unprovable_not_proven()
        {
            // 600 distinct sealed, empty classes, each held by a readonly field of one sealed type: every one of them
            // is Proven on its own, so the only thing that can stop the proof short of Proven is the budget.
            var sb = new StringBuilder("namespace T;\n");
            for (var i = 0; i < 600; i++)
                sb.Append("public sealed class E").Append(i).Append(" { }\n");
            sb.Append("public sealed class Wide\n{\n");
            for (var i = 0; i < 600; i++)
                sb.Append("    public readonly E").Append(i).Append(" F").Append(i).Append(" = null;\n");
            sb.Append("}\n");
            var compilation = Compile(sb.ToString());

            var verdict = ImmutabilityProof.Classify(compilation.GetTypeByMetadataName("T.Wide")!, out var reason);

            Assert.Equal(ImmutabilityVerdict.Unprovable, verdict);
            Assert.Equal("the immutability proof ran out of budget before it could finish", reason);
        }
    }
}
