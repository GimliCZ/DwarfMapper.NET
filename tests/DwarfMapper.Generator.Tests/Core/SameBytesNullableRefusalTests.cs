// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using DwarfMapper.Generator.Tests.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.Core
{
    /// <summary>
    ///     <see cref="BlittableProof.SameBytesIgnoringNames" /> refuses a <c>Nullable&lt;T&gt;</c> on either
    ///     side, whatever the bytes say.
    ///     <para>
    ///         This is the check <c>[Reinterpret]</c> rests on: the attribute asserts the layouts match and the
    ///         generator emits a block copy on the consumer's word. The <c>Nullable&lt;T&gt;</c> refusal is not
    ///         about bytes at all — a nullable pair cannot be the type argument its caller casts over, CS0453,
    ///         so accepting it would emit code the consumer's build rejects.
    ///     </para>
    ///     <para>
    ///         Flagged by the round-29 Codecov report as one uncovered line and two partial branches: the
    ///         corpus reaches this method only through pairs that already passed the nullable filter upstream,
    ///         so neither side of the <c>||</c> had been taken here.
    ///     </para>
    /// </summary>
    public class SameBytesNullableRefusalTests
    {
        private const string Source = """
                                      namespace T;
                                      public struct Pair { public int A; public int B; }
                                      public struct Holder
                                      {
                                          public Pair Plain;
                                          public Pair? Optional;
                                          public int Scalar;
                                      }
                                      """;

        private static ITypeSymbol Field(string name)
        {
            var c = GeneratorTestHarness.BuildCompilation(
                "SameBytes_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(Source)]);
            return c.GetTypeByMetadataName("T.Holder")!.GetMembers(name).OfType<IFieldSymbol>().Single().Type;
        }

        /// <summary>
        ///     The control, and it has to come first: the same struct against itself IS byte-identical, so the
        ///     three refusals below are attributable to nullability rather than to a method that says no to
        ///     everything.
        /// </summary>
        [Fact]
        public void A_struct_against_itself_is_byte_identical()
        {
            var plain = Field("Plain");

            Assert.True(BlittableProof.SameBytesIgnoringNames(plain, plain));
        }

        /// <summary>Nullable on the SOURCE side: the first operand of the refusal.</summary>
        [Fact]
        public void A_nullable_source_is_refused()
        {
            Assert.False(BlittableProof.SameBytesIgnoringNames(Field("Optional"), Field("Plain")));
        }

        /// <summary>Nullable on the DESTINATION side: the second operand, which short-circuiting hides.</summary>
        [Fact]
        public void A_nullable_destination_is_refused()
        {
            Assert.False(BlittableProof.SameBytesIgnoringNames(Field("Plain"), Field("Optional")));
        }

        /// <summary>
        ///     Both sides nullable — the same type on each side, so the BYTES are trivially identical and only
        ///     the nullable rule can be what refuses it. That is the case that would slip through a check
        ///     written as "the layouts differ".
        /// </summary>
        [Fact]
        public void Two_nullables_of_the_same_type_are_still_refused()
        {
            var optional = Field("Optional");

            Assert.False(BlittableProof.SameBytesIgnoringNames(optional, optional));
        }

        /// <summary>
        ///     And a genuine layout mismatch is still refused for the ordinary reason, so the nullable rule has
        ///     not become the only thing this method checks.
        /// </summary>
        [Fact]
        public void A_real_layout_mismatch_is_still_refused()
        {
            Assert.False(BlittableProof.SameBytesIgnoringNames(Field("Plain"), Field("Scalar")));
        }
    }
}
