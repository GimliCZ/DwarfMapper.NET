// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <b>A span map of a lossless primitive widen pair takes the <c>Vector.Widen</c> loop</b> (round 35, T6),
    ///     <b>and no other span map does.</b>
    ///     <para>
    ///         The array shape has widened with <c>Vector.Widen</c> since round 22 and the List shapes since round 33;
    ///         the span endpoint kept the plain element loop. Measured span to span before the change (Windows, AVX2,
    ///         1,024 elements): int to long 1.4x, float to double 4x, byte to ushort 5.8x. What the emitted loop
    ///         computes is held by <c>SpanMapWidenRuntimeTests</c>, under every vector width scripts/isa-matrix.ps1
    ///         sets.
    ///     </para>
    /// </summary>
    public class SpanMapWidenTests
    {
        private const string Widen = "global::System.Numerics.Vector.Widen(__v, out var __lo, out var __hi);";

        private static string Mapper(string source, string target, string sourceSpan = "ReadOnlySpan")
        {
            return $$"""
                     using System;
                     using DwarfMapper;
                     namespace Demo;
                     [DwarfMapper]
                     public partial class M
                     {
                         public partial void Map({{sourceSpan}}<{{source}}> s, Span<{{target}}> d);
                     }
                     """;
        }

        [Theory]
        [InlineData("byte", "ushort")]
        [InlineData("ushort", "uint")]
        [InlineData("uint", "ulong")]
        [InlineData("sbyte", "short")]
        [InlineData("short", "int")]
        [InlineData("int", "long")]
        [InlineData("float", "double")]
        public void Each_of_the_seven_widen_pairs_takes_the_vector_loop(string source, string target)
        {
            var generated = GeneratorAssert.CompilesClean(Mapper(source, target));

            Assert.Contains(Widen, generated, StringComparison.Ordinal);
            Assert.Contains($"int __w = global::System.Numerics.Vector<{source}>.Count;", generated, StringComparison.Ordinal);
            Assert.Contains($"int __half = global::System.Numerics.Vector<{target}>.Count;", generated, StringComparison.Ordinal);
            Assert.Contains($"var __v = new global::System.Numerics.Vector<{source}>(s.Slice(__i));", generated, StringComparison.Ordinal);
            // The scalar tail is the implicit widening, and the length guard still comes first.
            Assert.Contains("for (; __i < s.Length; __i++) d[__i] = s[__i];", generated, StringComparison.Ordinal);
            Assert.True(generated.IndexOf("if (d.Length < s.Length)", StringComparison.Ordinal) <
                        generated.IndexOf(Widen, StringComparison.Ordinal),
                "the destination length is checked before anything is written");
        }

        [Fact]
        public void A_mutable_source_span_takes_it_too()
        {
            var generated = GeneratorAssert.CompilesClean(Mapper("int", "long", "Span"));

            Assert.Contains(Widen, generated, StringComparison.Ordinal);
        }

        // An implicit conversion that is not one Vector.Widen step (two steps, or a change of kind), a nullable element,
        // and a pair of one size keep what they had: the element loop, or the block copy.
        [Theory]
        [InlineData("byte", "int")]
        [InlineData("int", "double")]
        [InlineData("int?", "long?")]
        public void Any_other_pair_keeps_the_element_loop(string source, string target)
        {
            var generated = GeneratorAssert.CompilesClean(Mapper(source, target));

            Assert.DoesNotContain("Vector.Widen", generated, StringComparison.Ordinal);
            Assert.Contains("for (int __i = 0; __i < s.Length; __i++)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_pair_of_one_size_keeps_the_block_copy()
        {
            var generated = GeneratorAssert.CompilesClean(Mapper("int", "int"));

            Assert.DoesNotContain("Vector.Widen", generated, StringComparison.Ordinal);
        }
    }
}
