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
        ///     Round 29 T0.2 fix-round-1 (Important #1): the blit must not override a user-declared element
        ///     converter. <c>Scale</c> qualifies as an auto-candidate (a non-void, one-parameter ordinary method
        ///     whose types match by implicit conversion — see <c>CollectMethods</c>), and
        ///     <c>TryResolveConversion</c>'s auto-candidate arm picks it up BEFORE the auto-nest arm that would
        ///     otherwise synthesize <c>__DwarfMap_Obj_*</c>. Since the resolved converter is not the default
        ///     synthesized helper, the pair must keep the element loop and call it, even though the pair is
        ///     otherwise layout-identical.
        /// </summary>
        [Fact]
        public void User_declared_element_converter_is_honoured_not_blitted()
        {
            const string src = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    public struct Vec3S { public float X, Y, Z; }
                    public struct Vec3D { public float X, Y, Z; }
                    [DwarfMapper]
                    public partial class M
                    {
                        public partial void Map(ReadOnlySpan<Vec3S> src, Span<Vec3D> dst);
                        public Vec3D Scale(Vec3S s) => new Vec3D { X = s.X * 2, Y = s.Y * 2, Z = s.Z * 2 };
                    }
                }
                """;
            var generated = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);
            Assert.Contains("Scale(src[__i])", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("MemoryMarshal.Cast", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Round 29 T0.2 fix-round-1 (Important #1): a pair-scoped <c>[MapIgnore&lt;T&gt;]</c> targeting the
        ///     element pair's destination type customizes the SAME synthesized <c>__DwarfMap_Obj_*</c> helper the
        ///     registry hands back for this pair (it is keyed purely by the type pair, not by which route reached
        ///     it) — a block copy would silently re-include the ignored member's bytes. Must keep the loop.
        /// </summary>
        [Fact]
        public void Pair_scoped_ignore_on_the_element_pair_keeps_the_loop()
        {
            const string src = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    public struct Vec3S { public float X, Y, Z; }
                    public struct Vec3D { public float X, Y, Z; }
                    [DwarfMapper]
                    [MapIgnore<Vec3D>("Z")]
                    public partial class M { public partial void Map(ReadOnlySpan<Vec3S> src, Span<Vec3D> dst); }
                }
                """;
            var generated = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);
            Assert.Contains("for (int __i", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("MemoryMarshal.Cast", generated, StringComparison.Ordinal);
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
        ///     Round 29 T0.2b fixed the gap these remarks used to document: the span map's PER-ELEMENT
        ///     resolution of a nullable STRUCT element now goes through the same lift
        ///     <c>CollectionConverter.ElementExpr</c> gives an array/list element (<c>src[__i]</c> is no longer
        ///     handed bare to the synthesized helper's non-nullable <c>P</c> parameter — see
        ///     <c>SpanMapNullableElementTests.Nullable_struct_elements_are_lifted_null_preserving</c> for the
        ///     full null-preserving assertion). This test's own claim is unchanged and narrower: the pair is
        ///     categorically not a reinterpret, so no cast and no DWARF100 near-miss — now checked through
        ///     <c>CompilesClean</c> since the loop this cast decision sits inside compiles.
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
            var generated = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);
            var (diagnostics, _) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF100");
            Assert.DoesNotContain("MemoryMarshal.Cast", generated, StringComparison.Ordinal);
            Assert.Contains("for (int __i", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Round 29 T0.2 fix-round-2 (Important #1, repro from review): <c>GeneratedNames.IsSynthesized</c>
        ///     alone also admits <c>__DwarfMap_UserConv_*</c> — the shim <c>UserConversionConverter</c> wraps a
        ///     user's OWN <c>implicit</c>/<c>explicit operator</c> in. Reached here via <c>[AutoNest(false)]</c>,
        ///     which stops the auto-nest arm from claiming the pair so the user-operator arm claims it instead.
        ///     Must keep the loop and call the operator's synthesized wrapper — not blit past a user's own
        ///     conversion logic (here, deliberately NOT a straight copy: it doubles every field).
        /// </summary>
        [Fact]
        public void User_defined_conversion_operator_is_honoured_not_blitted_when_autonest_is_off()
        {
            const string src = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    public struct Vec3S { public float X, Y, Z; }
                    public struct Vec3D
                    {
                        public float X, Y, Z;
                        public static implicit operator Vec3D(Vec3S s) => new Vec3D { X = s.X * 2, Y = s.Y * 2, Z = s.Z * 2 };
                    }
                    [DwarfMapper]
                    public partial class M
                    {
                        [AutoNest(false)]
                        public partial void Map(ReadOnlySpan<Vec3S> src, Span<Vec3D> dst);
                    }
                }
                """;
            var generated = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);
            Assert.Contains("__DwarfMap_UserConv_", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("MemoryMarshal.Cast", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Round 29 T0.2 fix-round-2: proves the gate's tightening did NOT over-correct into
        ///     <c>GeneratedNames.IsObjectMap</c>-only, which would have silently made the
        ///     <c>BlittableProof.CanReinterpretEnums</c> half of the blit condition permanently unreachable for
        ///     span maps. An enum-by-value pair over the SAME underlying type resolves through
        ///     <c>EnumConverter</c>'s own synthesized helper (<c>__DwarfMap_EnumVal_*</c>, verified empirically),
        ///     never <c>__DwarfMap_Obj_*</c> — and that helper is a byte-identical <c>CreateChecked</c> with no
        ///     customization surface (no <c>[MapIgnore]</c>/<c>[MapProperty]</c>/<c>[MapValue]</c>/hook reaches an
        ///     enum arm), so it must still blit.
        /// </summary>
        [Fact]
        public void Enum_byvalue_pair_still_blits()
        {
            const string src = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    public enum SrcEnum : int { A, B }
                    public enum DstEnum : int { A, B }
                    [DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]
                    public partial class M { public partial void Map(ReadOnlySpan<SrcEnum> src, Span<DstEnum> dst); }
                }
                """;
            var generated = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);
            Assert.Contains("MemoryMarshal.Cast<global::T.SrcEnum, global::T.DstEnum>(src).CopyTo(dst)", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("for (int __i", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Round 29 T0.2 fix-round-2 (Important #2, repro from review): a pair-scoped <c>[MapIgnore&lt;T&gt;]</c>
        ///     that customizes a span map's element pair — and so keeps the loop rather than blitting — must
        ///     still be reported by DWARF056 when nothing ever actually reads it. Here a user-declared converter
        ///     (<c>Scale</c>) owns construction entirely and never consults <c>decls.PairIgnores</c>, so the
        ///     ignore matches NOTHING; before the fix, <c>SpanElementPairHasCustomization</c>'s own lookup
        ///     mutated the same internal <c>Consumed</c> flag DWARF056's sweep reads, silencing it merely by
        ///     asking.
        /// </summary>
        [Fact]
        public void Unapplied_pair_scoped_ignore_on_a_span_map_still_reports_DWARF056()
        {
            const string src = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    public struct Vec3S { public float X, Y, Z; }
                    public struct Vec3D { public float X, Y, Z; }
                    [DwarfMapper]
                    [MapIgnore<Vec3D>("Z")]
                    public partial class M
                    {
                        public partial void Map(ReadOnlySpan<Vec3S> src, Span<Vec3D> dst);
                        public Vec3D Scale(Vec3S s) => new Vec3D { X = s.X * 2, Y = s.Y * 2, Z = s.Z * 2 };
                    }
                }
                """;
            GeneratorAssert.Reports(src, "DWARF056");
        }
    }
}
