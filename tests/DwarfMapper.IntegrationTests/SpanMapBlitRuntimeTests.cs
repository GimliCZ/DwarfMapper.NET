// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // Zero-alloc span map, blit fast path (round 29, T0.2): when the element pair is proven layout-identical
    // (BlittableProof.CanReinterpret), the generated body is one MemoryMarshal.Cast<S, D>(src).CopyTo(dst)
    // block copy rather than the per-element loop. Runtime behaviour must match the loop exactly — same
    // values, same length guard, same remainder handling for a non-power-of-two/non-multiple length.

    public struct BlitVec3Src
    {
        public float X;
        public float Y;
        public float Z;
    }

    public struct BlitVec3Dst
    {
        public float X;
        public float Y;
        public float Z;
    }

    [DwarfMapper]
    public partial class SpanMapBlitMapper
    {
        // layout-identical element pair (same fields, same names, same order) -> block copy
        public partial void Map(ReadOnlySpan<BlitVec3Src> src, Span<BlitVec3Dst> dst);
    }

    // Round 29 T0.2b: a Nullable<T> element pair is not layout-identical to MemoryMarshal.Cast's struct
    // constraint (CS0453 — see SpanMapBlitTests.Nullable_element_pair_is_not_a_reinterpret), so this pair keeps
    // the element loop, which now lifts the nullable element through the synthesized helper instead of handing
    // it bare (that was CS1503 in the consumer's build before this fix).
    public struct NullableElemSrc
    {
        public int V;
    }

    public struct NullableElemDst
    {
        public int V;
    }

    [DwarfMapper]
    public partial class SpanMapNullableElementMapper
    {
        public partial void Map(ReadOnlySpan<NullableElemSrc?> src, Span<NullableElemDst?> dst);
    }

    // Round 29 T0.2b, shape (c): the destination element CANNOT hold null, so the pair resolves through
    // TryResolveConversion's nullable-value-source arm exactly like the array/list arm resolves P?[] -> Q[] —
    // same BEHAVIOUR (NullHandling.ThrowIfNull, a runtime throw under the default NullStrategy.Throw, never a
    // compile-time refusal; empirically confirmed the array arm reaches the identical arm). The MESSAGE TEXT
    // is not identical, and is not meant to be: the array arm's own message stays the pre-existing generic
    // "Collection element was null" (several of its target shapes have no loop counter to name), while the
    // span map's inline loop always has __i and elemFq in scope, so review fix rounds 1-2 opted it into
    // CollectionConverter.ElementExpr's indexExpr parameter for a self-diagnosing message naming both the
    // index AND the destination type — see the assertion below for the exact text this mapper produces.
    [DwarfMapper]
    public partial class SpanMapNullableToNonNullableElementMapper
    {
        public partial void Map(ReadOnlySpan<NullableElemSrc?> src, Span<NullableElemDst> dst);
    }

    public class SpanMapBlitRuntimeTests
    {
        private static BlitVec3Src[] MakeSource(int count)
        {
            var src = new BlitVec3Src[count];
            for (var i = 0; i < count; i++)
            {
                src[i] = new BlitVec3Src
                {
                    X = i,
                    Y = i + 0.5f,
                    Z = -i
                };
            }

            return src;
        }

        [Fact]
        public void Layout_identical_span_map_copies_every_element()
        {
            const int count = 1000;
            var src = MakeSource(count);
            var dst = new BlitVec3Dst[count];

            new SpanMapBlitMapper().Map(src, dst);

            for (var i = 0; i < count; i++)
            {
                Assert.Equal(src[i].X, dst[i].X);
                Assert.Equal(src[i].Y, dst[i].Y);
                Assert.Equal(src[i].Z, dst[i].Z);
            }
        }

        [Fact]
        public void Destination_too_small_throws_not_silent_truncation()
        {
            var src = MakeSource(10);
            var dst = new BlitVec3Dst[9]; // too small

            Assert.Throws<ArgumentException>(() => new SpanMapBlitMapper().Map(src, dst));
        }

        [Fact]
        public void Non_multiple_length_round_trips_without_a_remainder_defect()
        {
            // 997: not a multiple of any vector width and not a power of two — the case a widening/vectorized
            // kernel drops on the floor if its remainder handling is wrong. MemoryMarshal.Cast + CopyTo has no
            // remainder to handle (it's a runtime memmove sized in bytes), but this pins that guarantee at
            // this specific, deliberately awkward length rather than only at round numbers.
            const int count = 997;
            var src = MakeSource(count);
            var dst = new BlitVec3Dst[count];

            new SpanMapBlitMapper().Map(src, dst);

            for (var i = 0; i < count; i++)
            {
                Assert.Equal(src[i].X, dst[i].X);
                Assert.Equal(src[i].Y, dst[i].Y);
                Assert.Equal(src[i].Z, dst[i].Z);
            }
        }

        /// <summary>
        ///     Round 29 T0.2b: five elements, the middle two null. Null must stay null (never an unchecked
        ///     <c>.Value</c> that throws, never a silently unmapped null) and every non-null value must still map
        ///     through the synthesized element helper.
        /// </summary>
        [Fact]
        public void Nullable_struct_elements_preserve_null_and_map_values()
        {
            NullableElemSrc? Some(int v)
            {
                return new NullableElemSrc { V = v };
            }

            var src = new NullableElemSrc?[] { Some(1), Some(2), null, null, Some(5) };
            var dst = new NullableElemDst?[5];

            new SpanMapNullableElementMapper().Map(src, dst);

            Assert.Equal(1, dst[0]!.Value.V);
            Assert.Equal(2, dst[1]!.Value.V);
            Assert.Null(dst[2]);
            Assert.Null(dst[3]);
            Assert.Equal(5, dst[4]!.Value.V);
        }

        /// <summary>
        ///     Round 29 T0.2b, shape (c): the RUNTIME half of the throw-on-null decision — the compile-time half
        ///     (that the pair resolves this way at all, and the exact throw text) is pinned in
        ///     <c>SpanMapNullableElementTests.Nullable_struct_source_into_non_nullable_target_throws_on_null_like_the_array_arm</c>.
        ///     Happy path first (no element is null: every value must still map), then the null-in-the-middle
        ///     case actually throws <see cref="InvalidOperationException" /> at the point of the null element —
        ///     never an unchecked <c>.Value</c> (which would also throw, but with the wrong, unexplained message)
        ///     and never a silently substituted default.
        /// </summary>
        [Fact]
        public void Nullable_struct_source_into_non_nullable_target_maps_when_no_element_is_null()
        {
            NullableElemSrc? Some(int v)
            {
                return new NullableElemSrc { V = v };
            }

            var src = new NullableElemSrc?[] { Some(1), Some(2), Some(3) };
            var dst = new NullableElemDst[3];

            new SpanMapNullableToNonNullableElementMapper().Map(src, dst);

            Assert.Equal(1, dst[0].V);
            Assert.Equal(2, dst[1].V);
            Assert.Equal(3, dst[2].V);
        }

        /// <summary>
        ///     Review fix rounds 1-2: the thrown message names the index of the null element (1 here) AND the
        ///     destination element type, not the generic array/list-arm text — see the doc comment on
        ///     <see cref="SpanMapNullableToNonNullableElementMapper" /> above for why the two arms' message TEXT
        ///     legitimately differs even though the underlying resolution decision (throw, not refuse) mirrors.
        /// </summary>
        [Fact]
        public void Nullable_struct_source_into_non_nullable_target_throws_on_a_null_element()
        {
            var src = new NullableElemSrc?[] { new NullableElemSrc { V = 1 }, null, new NullableElemSrc { V = 3 } };
            var dst = new NullableElemDst[3];

            var ex = Assert.Throws<InvalidOperationException>(
                () => new SpanMapNullableToNonNullableElementMapper().Map(src, dst));
            Assert.Equal(
                "Element at index 1 was null, and the destination element type 'global::DwarfMapper.IntegrationTests.NullableElemDst' does not admit null.",
                ex.Message);
        }
    }
}
