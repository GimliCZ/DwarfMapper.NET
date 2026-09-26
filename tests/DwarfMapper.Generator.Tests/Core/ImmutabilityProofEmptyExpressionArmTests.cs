// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.Core
{
    /// <summary>
    ///     <see cref="ImmutabilityProof.TryEmptyExpression" />'s member-kind test. The <c>Empty</c> convention is a static,
    ///     public member that is either a readable PROPERTY or a FIELD of the declaring type. A public static
    ///     <c>Empty</c> of any other kind (a METHOD, or a property with no getter) has no value to read, and must be
    ///     skipped rather than emitted as <c>Bag.Empty</c>, which would not compile.
    ///     <see cref="ImmutabilityProofArmTests" /> pinned the non-static, non-public and wrong-type skips, but never an
    ///     <c>Empty</c> of the wrong member kind.
    /// </summary>
    public class ImmutabilityProofEmptyExpressionArmTests
    {
        [Theory]
        [InlineData("public static Bag Empty() => new();")] //  a method, not a readable member
        [InlineData("public static Bag Empty { set { } }")] //  a property with no getter
        public void A_public_static_Empty_that_cannot_be_read_as_a_value_is_skipped(string declaration)
        {
            var source = $$"""
                           namespace T;
                           public sealed class Bag { {{declaration}} }
                           """;
            var compilation = GeneratorTestHarness.BuildCompilation("ImmutabilityEmptyExpressionArms", [CSharpSyntaxTree.ParseText(source)]);

            Assert.Null(ImmutabilityProof.TryEmptyExpression(compilation.GetTypeByMetadataName("T.Bag")!));
        }
    }
}
