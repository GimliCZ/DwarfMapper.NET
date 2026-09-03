// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     A zero-alloc span map (<c>void Map(ReadOnlySpan&lt;S&gt; src, Span&lt;D&gt; dst)</c>) whose element pair is
    ///     proven layout-identical (<see cref="DwarfMapper.Generator.Pipeline.BlittableProof.CanReinterpret" />) emits
    ///     one <c>MemoryMarshal.Cast&lt;S, D&gt;(src).CopyTo(dst)</c> block copy instead of the element loop — same
    ///     proof, same fast path as the array/list blit (round 29, T0.2). The length guard still runs first: the
    ///     block copy is only sound once the destination is known to be large enough.
    /// </summary>
    public class SpanMapBlitTests
    {
        private const string Pair = """
            using System;
            using DwarfMapper;
            namespace T
            {
                public struct Vec3S { public float X, Y, Z; }
                public struct Vec3D { public float X, Y, Z; }
                [DwarfMapper] public partial class M { public partial void Map(ReadOnlySpan<Vec3S> src, Span<Vec3D> dst); }
            }
            """;

        [Fact]
        public void Layout_identical_span_map_is_a_block_copy()
        {
            var generated = GeneratorAssert.CompilesClean(Pair, NullableContextOptions.Enable);
            Assert.Contains("MemoryMarshal.Cast<global::T.Vec3S, global::T.Vec3D>(src).CopyTo(dst)", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("for (int __i", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Length_check_stays_in_front_of_the_block_copy()
        {
            var generated = GeneratorAssert.CompilesClean(Pair, NullableContextOptions.Enable);
            var check = generated.IndexOf("dst.Length < src.Length", StringComparison.Ordinal);
            var copy = generated.IndexOf("MemoryMarshal.Cast", StringComparison.Ordinal);
            Assert.True(check >= 0 && check < copy, "the ArgumentException guard must precede the copy");
        }

        [Fact]
        public void Non_identical_layout_keeps_the_element_loop_and_explains_it()
        {
            const string src = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    public struct Vec3S { public float X, Y, Z; }
                    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
                    public struct Vec3A { public float X, Y, Z; }
                    [DwarfMapper] public partial class M { public partial void Map(ReadOnlySpan<Vec3S> src, Span<Vec3A> dst); }
                }
                """;
            var generated = GeneratorAssert.EmitsCompilableCode(src, NullableContextOptions.Enable);
            Assert.Contains("for (int __i", generated, StringComparison.Ordinal);
            GeneratorAssert.Reports(src, "DWARF100");
        }

        /// <summary>
        ///     A top-level <c>Nullable&lt;T&gt;</c> element pair is byte-identical per
        ///     <see cref="DwarfMapper.Generator.Pipeline.BlittableProof.LayoutIdentical" />'s nested-field
        ///     unwrap (round 29, T0.1), but the pair itself cannot be the type argument
        ///     <c>MemoryMarshal.Cast</c> is asked to reinterpret: its <c>struct</c> constraint refuses
        ///     <c>Nullable&lt;T&gt;</c> outright (CS0453), unlike the weaker <c>unmanaged</c> constraint
        ///     <c>Nullable&lt;T&gt;</c> DOES satisfy. Must not emit the cast, and must not report DWARF100 —
        ///     it is not a near miss, it is categorically excluded (<c>BlittableProof.CanReinterpret</c> /
        ///     <c>TryExplainNearMiss</c> refuse it before the layout proof runs at all).
        /// </summary>
        /// <remarks>
        ///     Asserted against the raw generator output rather than through <c>GeneratorAssert</c>'s
        ///     compile-clean helpers: a span map's PER-ELEMENT resolution of a nullable STRUCT element (as
        ///     opposed to the array/list converter's own nullable-element lifting) is a separate, pre-existing
        ///     gap this task does not own — <c>src[__i]</c> (<c>P?</c>) is passed bare to the synthesized
        ///     helper's non-nullable <c>P</c> parameter (CS1503), with or without the fix here. This test's only
        ///     claim is about the CAST, not about the loop's own compilability for this element shape.
        /// </remarks>
        [Fact]
        public void Nullable_element_pair_is_not_a_reinterpret()
        {
            const string src = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    public struct P { public int V; }
                    public struct Q { public int V; }
                    [DwarfMapper] public partial class M { public partial void Map(ReadOnlySpan<P?> src, Span<Q?> dst); }
                }
                """;
            var (diagnostics, generated) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF100");
            Assert.DoesNotContain("MemoryMarshal.Cast", generated, StringComparison.Ordinal);
        }
    }
}
