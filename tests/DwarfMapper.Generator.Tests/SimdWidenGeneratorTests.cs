// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     SIMD-widening fast-path: a lossless primitive widen pair in an array→array member emits a
    ///     Vector.Widen loop with a hardware-accelerated guard and a scalar tail (= scalar implicit widen).
    /// </summary>
    public class SimdWidenGeneratorTests
    {
        [Fact]
        public void Int_to_long_array_emits_Vector_Widen_with_scalar_tail()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int[] V { get; set; } = System.Array.Empty<int>(); }
                               public class D { public long[] V { get; set; } = System.Array.Empty<long>(); }
                               [DwarfMapper] public partial class M { public partial D Map(S s); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("Vector.Widen", generated, StringComparison.Ordinal);
            Assert.Contains("Vector.IsHardwareAccelerated", generated, StringComparison.Ordinal);
            // Scalar tail / fallback (the implicit widen).
            Assert.Contains("for (; __i < src.Length; __i++)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Float_to_double_array_uses_widen_path()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public float[] V { get; set; } = System.Array.Empty<float>(); }
                               public class D { public double[] V { get; set; } = System.Array.Empty<double>(); }
                               [DwarfMapper] public partial class M { public partial D Map(S s); }
                               """;
            var (diags, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("__DwarfWiden_", generated, StringComparison.Ordinal);
            Assert.Contains("Vector.Widen", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Non_widen_pair_does_not_use_SIMD()
        {
            // long→int is NARROWING (not a Widen pair) → must go through CreateChecked, not Vector.Widen.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public long[] V { get; set; } = System.Array.Empty<long>(); }
                               public class D { public int[] V { get; set; } = System.Array.Empty<int>(); }
                               [DwarfMapper] public partial class M { public partial D Map(S s); }
                               """;
            var (_, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain("Vector.Widen", generated, StringComparison.Ordinal);
            Assert.Contains("CreateChecked", generated, StringComparison.Ordinal); // loud narrowing instead
        }

        [Theory]
        // Cross-category and non-widen pairs must NEVER use Vector.Widen — only the 7 same-category
        // same-sign lossless integer/float widenings do.
        [InlineData("int", "double")] // cross-category (integer → floating) — not a Widen pair
        [InlineData("int", "float")] // cross-category lossy — not a Widen pair
        [InlineData("float", "int")] // floating → integer (narrowing/category) — not a Widen pair
        [InlineData("decimal", "double")] // decimal is not a Vector element type
        [InlineData("int", "ulong")] // sign-change widen — not in the Widen set
        [InlineData("byte", "ulong")] // multi-step widen — not a single Vector.Widen pair
        public void Non_widen_array_pairs_never_use_Vector_Widen(string srcT, string dstT)
        {
            var src = $$"""
                        using DwarfMapper;
                        namespace Demo;
                        public class S { public {{srcT}}[] V { get; set; } = System.Array.Empty<{{srcT}}>(); }
                        public class D { public {{dstT}}[] V { get; set; } = System.Array.Empty<{{dstT}}>(); }
                        [DwarfMapper] public partial class M { public partial D Map(S s); }
                        """;
            var (_, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain("Vector.Widen", generated, StringComparison.Ordinal);
        }

        // ── The fast path agrees with what the element loop does (round 29 T0.2c's rule) ─────────────────────────
        // BlitSoundnessTests pins that a conversion the author declared for the element pair keeps the loop, and the
        // widen gate does not ask that question - deliberately. A widen pair has an IMPLICIT conversion, and the
        // resolver assigns implicit pairs directly, ahead of any user method: user methods beat only SYNTHESIZED
        // conversions (18bc255, the long → int Shrink case). So a declared `long Conv(int)` is not what the element
        // loop calls either, and Vector.Widen - the implicit widen, vectorised - is still the same answer. The scalar
        // member is asserted beside it: if that precedence ever changes, this fails on `X = s.X` first, instead of the
        // fast path quietly disagreeing with the loop it replaces.
        [Fact]
        public void A_declared_method_with_a_widen_pairs_signature_does_not_displace_the_implicit_widen()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int X { get; set; } public int[] V { get; set; } = System.Array.Empty<int>(); public System.Collections.Generic.List<int> L { get; set; } = new(); }
                               public class D { public long X { get; set; } public long[] V { get; set; } = System.Array.Empty<long>(); public System.Collections.Generic.List<long> L { get; set; } = new(); }
                               [DwarfMapper] public partial class M
                               {
                                   public static long Conv(int x) => x * 10L;
                                   public partial D Map(S s);
                               }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("X = s.X,", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("Conv(", generated, StringComparison.Ordinal);
            Assert.Contains("__DwarfWiden_", generated, StringComparison.Ordinal);
            Assert.Contains("__DwarfWidenL_", generated, StringComparison.Ordinal);
        }

        // ── Round 33 T02: the List- and ImmutableArray-involved shapes ───────────────────────────────────────────
        // array → array is SynthesizeSimdWiden's; every other pairing of the storages SynthesizeBlitListShape handles
        // used to take the general value-element fill, which lost to Select(x => (long)x).ToList() by 1.48x at 1,024
        // int → long elements. The list-family interfaces materialise to List<T>, so they come along.
        [Theory]
        [InlineData("int[]", "System.Collections.Generic.List<long>")]
        [InlineData("System.Collections.Generic.List<int>", "long[]")]
        [InlineData("System.Collections.Generic.List<int>", "System.Collections.Generic.List<long>")]
        [InlineData("System.Collections.Immutable.ImmutableArray<int>", "System.Collections.Generic.List<long>")]
        [InlineData("System.Collections.Immutable.ImmutableArray<int>", "long[]")]
        [InlineData("int[]", "System.Collections.Immutable.ImmutableArray<long>")]
        [InlineData("System.Collections.Generic.List<int>", "System.Collections.Immutable.ImmutableArray<long>")]
        [InlineData("System.Collections.Immutable.ImmutableArray<int>", "System.Collections.Immutable.ImmutableArray<long>")]
        [InlineData("int[]", "System.Collections.Generic.IList<long>")]
        [InlineData("int[]", "System.Collections.Generic.IReadOnlyList<long>")]
        [InlineData("int[]", "System.Collections.Generic.ICollection<long>")]
        [InlineData("int[]", "System.Collections.Generic.IReadOnlyCollection<long>")]
        public void A_widen_pair_in_a_list_or_immutable_array_shape_takes_the_vector_loop(string srcT, string dstT)
        {
            var src = $$"""
                        using DwarfMapper;
                        namespace Demo;
                        public class S { public {{srcT}} V { get; set; } = default!; }
                        public class D { public {{dstT}} V { get; set; } = default!; }
                        [DwarfMapper] public partial class M { public partial D Map(S s); }
                        """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("__DwarfWidenL_", generated, StringComparison.Ordinal);
            Assert.Contains("Vector.Widen", generated, StringComparison.Ordinal);
            // The tail is an indexed loop over two spans - never `__d[__i++] = __item` behind a foreach, the index
            // the JIT could not prove in range, which is what lost to LINQ.
            Assert.Contains("for (; __i < __s.Length; __i++) __d[__i] = __s[__i];", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("__d[__i++]", generated, StringComparison.Ordinal);
        }

        [Theory]
        // An interface SOURCE: CollectionsMarshal.AsSpan is declared on the concrete List<T> (round 25's reason).
        [InlineData("System.Collections.Generic.IReadOnlyList<int>", "System.Collections.Generic.List<long>")]
        // A target that is no contiguous storage.
        [InlineData("int[]", "System.Collections.Generic.HashSet<long>")]
        public void A_widen_pair_the_span_shapes_cannot_hold_keeps_the_element_loop(string srcT, string dstT)
        {
            var src = $$"""
                        using DwarfMapper;
                        namespace Demo;
                        public class S { public {{srcT}} V { get; set; } = default!; }
                        public class D { public {{dstT}} V { get; set; } = default!; }
                        [DwarfMapper] public partial class M { public partial D Map(S s); }
                        """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("__DwarfMapColl_", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("Vector.Widen", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Under_Preserve_a_widen_into_a_list_keeps_the_registering_helper()
        {
            // Preserve registers a collection before filling it, so two members sharing one source collection share one
            // target (the B3 guarantee in EmitArray/EmitList); the vector helper is static and has no context to
            // register in. These shapes used that registering helper before the list path existed, and still do.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int[] V { get; set; } = []; }
                               public class D { public System.Collections.Generic.List<long> V { get; set; } = []; }
                               [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)] public partial class M { public partial D Map(S s); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.DoesNotContain("__DwarfWidenL_", generated, StringComparison.Ordinal);
            Assert.Contains("ctx.TryGetReference(src, out var __cc)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_null_collection_kept_null_keeps_the_element_loop()
        {
            // NullCollectionStrategy.AsNull hands a null source back as null, which the span shapes do not model - the
            // blit's gate (!NullAsNull) too. Under the default, AsEmpty, a null source becomes an empty list on either
            // path, so a nullable member there takes the vector loop like any other.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int[]? V { get; set; } }
                               public class D { public System.Collections.Generic.List<long>? V { get; set; } }
                               [DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)] public partial class M { public partial D Map(S s); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);
            Assert.Contains("__DwarfMapColl_", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("Vector.Widen", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_widen_pair_list_three_levels_down_and_inside_a_collection_element_takes_the_vector_loop()
        {
            // The decision is made per synthesized element-pair helper, so it should not care about depth - and "should
            // not" is how this repository's corpus holes got in (ListFillNestedTests' reason). Both decompositions: a
            // member three objects down, and a member of each element of an outer list of reference elements.
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Leaf    { public int[] Amounts { get; set; } = []; }
                               public class Middle  { public Leaf Inner { get; set; } = new(); }
                               public class Line    { public List<short> Qty { get; set; } = []; }
                               public class Root    { public Middle M { get; set; } = new(); public List<Line> Lines { get; set; } = []; }
                               public class LeafD   { public List<long> Amounts { get; set; } = []; }
                               public class MiddleD { public LeafD Inner { get; set; } = new(); }
                               public class LineD   { public int[] Qty { get; set; } = []; }
                               public class RootD   { public MiddleD M { get; set; } = new(); public List<LineD> Lines { get; set; } = []; }
                               [DwarfMapper] public partial class Mp { public partial RootD Map(Root r); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("private static global::System.Collections.Generic.List<long> __DwarfWidenL_", generated, StringComparison.Ordinal);
            Assert.Contains("private static int[] __DwarfWidenL_", generated, StringComparison.Ordinal);
            // ...and the outer list of reference elements still takes Add, in the same generated file.
            Assert.Contains(".Add(", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Two_members_widening_the_same_list_pair_reuse_one_synthesized_helper()
        {
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class C { public List<int> A { get; set; } = new(); public List<int> B { get; set; } = new(); }
                               public class D { public List<long> A { get; set; } = new(); public List<long> B { get; set; } = new(); }
                               [DwarfMapper] public partial class M { public partial D Map(C c); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            var declCount = System.Text.RegularExpressions.Regex.Matches(generated,
                "private static global::System.Collections.Generic.List<long> __DwarfWidenL_").Count;
            Assert.Equal(1, declCount);
        }

        [Fact]
        public void Same_size_pair_blits_not_widens()
        {
            // int→uint is same-size → the blit fast-path, NOT Vector.Widen.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int[] V { get; set; } = System.Array.Empty<int>(); }
                               public class D { public uint[] V { get; set; } = System.Array.Empty<uint>(); }
                               [DwarfMapper] public partial class M { public partial D Map(S s); }
                               """;
            var (_, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain("Vector.Widen", generated, StringComparison.Ordinal);
        }
    }
}
