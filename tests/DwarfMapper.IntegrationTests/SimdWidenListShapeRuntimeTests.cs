// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;

namespace DwarfMapper.IntegrationTests
{
    // Round 33 T02: the Vector.Widen loop for the List- and ImmutableArray-involved shapes. SimdWidenRuntimeTests is
    // the array → array oracle; this is the same safety net for every other storage pairing - the vectorised result
    // MUST equal the scalar implicit widening, bit for bit, at every length around the vector boundary.

    public class SwlPairsSrc
    {
        public List<int> Ints { get; set; } = [];

        public List<short> Shorts { get; set; } = [];

        public List<byte> Bytes { get; set; } = [];

        public List<ushort> UShorts { get; set; } = [];

        public List<uint> UInts { get; set; } = [];

        public List<sbyte> SBytes { get; set; } = [];

        public List<float> Floats { get; set; } = [];
    }

    public class SwlPairsDst
    {
        public List<long> Ints { get; set; } = [];

        public List<int> Shorts { get; set; } = [];

        public List<ushort> Bytes { get; set; } = [];

        public List<uint> UShorts { get; set; } = [];

        public List<ulong> UInts { get; set; } = [];

        public List<short> SBytes { get; set; } = [];

        public List<double> Floats { get; set; } = [];
    }

    [DwarfMapper]
    public partial class SwlPairsMapper
    {
        public partial SwlPairsDst Map(SwlPairsSrc s);
    }

    // One int → long member per storage pairing the list-shape helper serves.
    public class SwlShapesSrc
    {
        public int[] ArrayToList { get; set; } = [];

        public List<int> ListToList { get; set; } = [];

        public List<int> ListToArray { get; set; } = [];

        public ImmutableArray<int> ImmutableToList { get; set; } = [];

        public ImmutableArray<int> ImmutableToArray { get; set; } = [];

        public int[] ArrayToImmutable { get; set; } = [];

        public List<int> ListToImmutable { get; set; } = [];

        public ImmutableArray<int> ImmutableToImmutable { get; set; } = [];

        public int[] ArrayToReadOnlyList { get; set; } = [];
    }

    public class SwlShapesDst
    {
        public List<long> ArrayToList { get; set; } = [];

        public List<long> ListToList { get; set; } = [];

        public long[] ListToArray { get; set; } = [];

        public List<long> ImmutableToList { get; set; } = [];

        public long[] ImmutableToArray { get; set; } = [];

        public ImmutableArray<long> ArrayToImmutable { get; set; } = [];

        public ImmutableArray<long> ListToImmutable { get; set; } = [];

        public ImmutableArray<long> ImmutableToImmutable { get; set; } = [];

        public IReadOnlyList<long> ArrayToReadOnlyList { get; set; } = [];
    }

    [DwarfMapper]
    public partial class SwlShapesMapper
    {
        public partial SwlShapesDst Map(SwlShapesSrc s);
    }

    public class SwlSharedSrc
    {
        public int[] A { get; set; } = [];

        public int[] B { get; set; } = [];
    }

    public class SwlSharedDst
    {
        public List<long> A { get; set; } = [];

        public List<long> B { get; set; } = [];
    }

    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class SwlSharedPreserveMapper
    {
        public partial SwlSharedDst Map(SwlSharedSrc s);
    }

    public class SimdWidenListShapeRuntimeTests
    {
        private static SwlShapesSrc Every(int[] values)
        {
            return new SwlShapesSrc
            {
                ArrayToList = values,
                ListToList = [.. values],
                ListToArray = [.. values],
                ImmutableToList = [.. values],
                ImmutableToArray = [.. values],
                ArrayToImmutable = values,
                ListToImmutable = [.. values],
                ImmutableToImmutable = [.. values],
                ArrayToReadOnlyList = values
            };
        }

        private static void AssertEveryShapeIs(long[] expected, SwlShapesDst d)
        {
            Assert.Equal(expected, d.ArrayToList);
            Assert.Equal(expected, d.ListToList);
            Assert.Equal(expected, d.ListToArray);
            Assert.Equal(expected, d.ImmutableToList);
            Assert.Equal(expected, d.ImmutableToArray);
            Assert.Equal(expected, d.ArrayToImmutable);
            Assert.Equal(expected, d.ListToImmutable);
            Assert.Equal(expected, d.ImmutableToImmutable);
            Assert.Equal(expected, d.ArrayToReadOnlyList);
        }

        private static void AssertEveryPairWidens(SwlPairsSrc src, SwlPairsDst d)
        {
            Assert.Equal(src.Ints.Select(x => (long)x), d.Ints);
            Assert.Equal(src.Shorts.Select(x => (int)x), d.Shorts);
            Assert.Equal(src.Bytes.Select(x => (ushort)x), d.Bytes);
            Assert.Equal(src.UShorts.Select(x => (uint)x), d.UShorts);
            Assert.Equal(src.UInts.Select(x => (ulong)x), d.UInts);
            Assert.Equal(src.SBytes.Select(x => (short)x), d.SBytes);
            // float → double: exact widening, NaN and infinity included - compared bitwise, since NaN != NaN.
            Assert.Equal(src.Floats.Select(x => (double)x).Select(BitConverter.DoubleToInt64Bits),
                d.Floats.Select(BitConverter.DoubleToInt64Bits));
        }

        // CA5394 flags System.Random as insecure randomness. These helpers only draw fuzz data, and a SEEDED generator
        // is the point: a failing case must reproduce, which a cryptographic generator cannot do. Scoped to them.
#pragma warning disable CA5394
        private static int[] RandomInts(int seed, int len)
        {
            var rng = new Random(seed);
            var ints = new int[len];
            for (var i = 0; i < len; i++) ints[i] = rng.Next(int.MinValue, int.MaxValue);
            return ints;
        }

        private static int[] RandomLengths(int seed, int count, int max)
        {
            var rng = new Random(seed);
            return [.. Enumerable.Range(0, count).Select(_ => rng.Next(0, max))];
        }

        private static SwlPairsSrc RandomPairs(int seed, int len)
        {
            var rng = new Random(seed);
            return new SwlPairsSrc
            {
                Ints = [.. Enumerable.Range(0, len).Select(_ => rng.Next(int.MinValue, int.MaxValue))],
                Shorts = [.. Enumerable.Range(0, len).Select(_ => (short)rng.Next(short.MinValue, short.MaxValue + 1))],
                Bytes = [.. Enumerable.Range(0, len).Select(_ => (byte)rng.Next(0, 256))],
                UShorts = [.. Enumerable.Range(0, len).Select(_ => (ushort)rng.Next(0, 65536))],
                UInts = [.. Enumerable.Range(0, len).Select(_ => (uint)rng.NextInt64(0, (long)uint.MaxValue + 1))],
                SBytes = [.. Enumerable.Range(0, len).Select(_ => (sbyte)rng.Next(sbyte.MinValue, sbyte.MaxValue + 1))],
                Floats = [.. Enumerable.Range(0, len).Select(_ => (float)((rng.NextDouble() * 2) - 1) * float.MaxValue)]
            };
        }
#pragma warning restore CA5394

        [Fact]
        public void All_seven_widen_pairs_map_correctly_through_lists()
        {
            var src = new SwlPairsSrc
            {
                Ints = [int.MinValue, -1, 0, 1, int.MaxValue],
                Shorts = [short.MinValue, -1, 0, 1, short.MaxValue],
                Bytes = [0, 1, 127, 128, 255],
                UShorts = [0, 1, 32767, 65535],
                UInts = [0, 1, uint.MaxValue],
                SBytes = [sbyte.MinValue, -1, 0, 1, sbyte.MaxValue],
                Floats = [float.MinValue, -1.5f, 0f, 1.5f, float.MaxValue, float.NaN, float.PositiveInfinity]
            };

            AssertEveryPairWidens(src, new SwlPairsMapper().Map(src));
        }

        [Fact]
        public void All_seven_widen_pairs_hold_through_full_vectors_and_a_tail()
        {
            // Five elements, as above, never fill a vector, so that case is all tail. 131 runs the vector body at any
            // Vector<T> width this suite meets, then a scalar tail; the extremes are put at the front, so they go
            // through the vector loop.
            var src = RandomPairs(33, 131);
            src.Ints[0] = int.MinValue;
            src.Ints[1] = int.MaxValue;
            src.UInts[0] = uint.MaxValue;
            src.Bytes[0] = byte.MaxValue;
            src.SBytes[0] = sbyte.MinValue;
            src.Floats[0] = float.NaN;
            src.Floats[1] = float.NegativeInfinity;

            AssertEveryPairWidens(src, new SwlPairsMapper().Map(src));
        }

        // ── Adversary/fuzz: every length around the vector boundary, in every shape, equals the scalar oracle ──
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(31)]
        [InlineData(32)]
        [InlineData(33)]
        [InlineData(63)]
        [InlineData(64)]
        [InlineData(65)]
        [InlineData(1000)]
        public void Every_shape_equals_the_scalar_oracle_for_every_length(int len)
        {
            var ints = RandomInts(len + 54321, len);

            AssertEveryShapeIs([.. ints.Select(x => (long)x)], new SwlShapesMapper().Map(Every(ints)));
        }

        [Theory]
        [InlineData(13)]
        [InlineData(777)]
        [InlineData(99999)]
        public void Fuzz_random_collections_match_the_scalar_oracle_in_every_shape(int seed)
        {
            var lengths = RandomLengths(seed, 40, 300);
            for (var iter = 0; iter < lengths.Length; iter++)
            {
                var ints = RandomInts((seed * 100) + iter, lengths[iter]);

                AssertEveryShapeIs([.. ints.Select(x => (long)x)], new SwlShapesMapper().Map(Every(ints)));
            }
        }

        [Fact]
        public void Under_Preserve_two_members_sharing_one_source_share_one_target()
        {
            // The B3 guarantee: Preserve rebuilds the source's topology, so one source collection reached twice is one
            // target collection. The vector path has no context to register in, so Preserve keeps the registering one.
            int[] shared = [1, 2, 3];

            var d = new SwlSharedPreserveMapper().Map(new SwlSharedSrc { A = shared, B = shared });

            Assert.Same(d.A, d.B);
            Assert.Equal([1L, 2L, 3L], d.A);
        }

        [Fact]
        public void Null_default_and_empty_sources_map_to_empty_and_never_throw()
        {
            // AsEmpty, the default: a null List or array source becomes an empty target, and a `default`
            // ImmutableArray - which wraps a null array and faults on AsSpan() - is answered before it is read.
            var nulls = new SwlShapesSrc
            {
                ArrayToList = null!,
                ListToList = null!,
                ListToArray = null!,
                ImmutableToList = default,
                ImmutableToArray = default,
                ArrayToImmutable = null!,
                ListToImmutable = null!,
                ImmutableToImmutable = default,
                ArrayToReadOnlyList = null!
            };

            AssertEveryShapeIs([], new SwlShapesMapper().Map(nulls));
            AssertEveryShapeIs([], new SwlShapesMapper().Map(Every([])));
        }
    }
}
