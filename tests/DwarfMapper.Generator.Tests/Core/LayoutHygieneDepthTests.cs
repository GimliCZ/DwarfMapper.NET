// SPDX-License-Identifier: GPL-2.0-only

using System.Text;
using DwarfMapper.Generator.Pipeline;
using DwarfMapper.Generator.Tests.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.Core
{
    /// <summary>
    ///     <see cref="LayoutHygiene" />'s depth guard.
    ///     <para>
    ///         <c>MeasureMember</c> opens with <c>if (depth &gt; MaxDepth) return null</c>. Nothing in the corpus
    ///         nests structs seventeen deep, so the guard was unreached — the round-29 Codecov patch report flagged
    ///         it. <c>MeasureStruct</c> carried a copy until round 30 removed it: it is entered at depth 0 by
    ///         <c>Measure</c> and otherwise only through <c>MeasureMember</c>, which has already refused.
    ///     </para>
    ///     <para>
    ///         A depth guard is worth a test for the reason the repository applies to every recursion in the
    ///         generator: it is what makes the walk TERMINATE on a consumer type the author never imagined.
    ///         Unreached, nobody knows whether it returns null or throws, and the difference between those is a
    ///         refused optimization and a crashed build.
    ///     </para>
    /// </summary>
    public class LayoutHygieneDepthTests
    {
        private const int MaxDepth = 16;

        /// <summary>A chain <c>S0 { S1 { S2 { … } } }</c> of the requested depth, innermost holding an int.</summary>
        private static string NestedStructs(int depth)
        {
            var sb = new StringBuilder("namespace T;\n");
            for (var i = 0; i < depth; i++)
            {
                sb.Append("public struct S").Append(i).Append(" { public S").Append(i + 1).Append(" Inner; }\n");
            }

            sb.Append("public struct S").Append(depth).Append(" { public int V; }\n");
            return sb.ToString();
        }

        private static ITypeSymbol Type(string source, string metadataName)
        {
            var c = GeneratorTestHarness.BuildCompilation(
                "LayoutDepth_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(source)]);
            var t = c.GetTypeByMetadataName(metadataName);
            Assert.True(t is not null, $"fixture type {metadataName} not found");
            return t!;
        }

        /// <summary>
        ///     Shallow enough to measure: the control, and the thing that makes the refusal below a statement
        ///     about DEPTH rather than about nested structs in general.
        /// </summary>
        [Fact]
        public void A_shallow_nest_is_measured()
        {
            var measured = LayoutHygiene.Measure(Type(NestedStructs(3), "T.S0"));

            Assert.True(measured is not null, "a three-deep unmanaged nest should measure");
            Assert.True(measured!.Value.Size > 0, "a measured layout must have a positive size");
        }

        /// <summary>
        ///     Past <c>MaxDepth</c> the walk REFUSES rather than recursing or throwing. Null is the contract —
        ///     "anything whose width this generator may not claim" — and a refused measurement simply means no
        ///     layout advisory, which is the safe direction.
        /// </summary>
        [Fact]
        public void A_nest_deeper_than_the_limit_refuses_instead_of_recursing()
        {
            var deep = NestedStructs(MaxDepth + 4);

            var measured = LayoutHygiene.Measure(Type(deep, "T.S0"));

            Assert.True(measured is null,
                "a nest deeper than MaxDepth must refuse; measuring it would mean the guard never fired.");
        }

        /// <summary>
        ///     <c>MeasureMember</c> carries the same guard, and its depth parameter is private — only the
        ///     zero-depth wrapper is public. So the guard is reached the way production reaches it: through
        ///     <c>MeasureStruct</c>'s recursion on the deep nest above. This pins the wrapper's own contract
        ///     beside it, so a refusal there can be attributed to depth rather than to the member type.
        /// </summary>
        [Fact]
        public void The_public_MeasureMember_measures_a_primitive_at_depth_zero()
        {
            var intType = Type("namespace T; public struct S0 { public int V; }", "T.S0")
                          .GetMembers("V").OfType<IFieldSymbol>().Single().Type;

            var measured = LayoutHygiene.MeasureMember(intType);

            Assert.True(measured is not null, "an int must measure at depth 0");
            Assert.Equal(4, measured!.Value.Size);
            Assert.Equal(4, measured.Value.Align);
        }
    }
}
