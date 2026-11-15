// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.InteropServices;

namespace DwarfMapper.IntegrationTests
{
    [DwarfMapper]
    public partial class SpanWidenMapper
    {
        public partial void Bytes(ReadOnlySpan<byte> s, Span<ushort> d);

        public partial void UShorts(ReadOnlySpan<ushort> s, Span<uint> d);

        public partial void UInts(ReadOnlySpan<uint> s, Span<ulong> d);

        public partial void SBytes(ReadOnlySpan<sbyte> s, Span<short> d);

        public partial void Shorts(ReadOnlySpan<short> s, Span<int> d);

        public partial void Ints(ReadOnlySpan<int> s, Span<long> d);

        public partial void Floats(ReadOnlySpan<float> s, Span<double> d);

        public partial void MutableInts(Span<int> s, Span<long> d);
    }

    /// <summary>A span map, as the tests below call one.</summary>
    public delegate void SpanMap<TSource, TTarget>(ReadOnlySpan<TSource> source, Span<TTarget> destination);

    /// <summary>
    ///     <b>A widening span map writes what the scalar loop wrote, element for element, and nothing else</b> (round
    ///     35, T6).
    ///     <para>
    ///         The vector loop runs while a whole vector fits and leaves the rest to the scalar tail, so the lengths
    ///         that matter are the ones around each vector width: 0 to 70 covers 16-, 32- and 64-element boundaries for
    ///         every pair under 128- and 256-bit vectors, and a few longer lengths cover many iterations.
    ///         scripts/isa-matrix.ps1 runs this class under native, 256-bit, 128-bit, no-AVX2 and no-intrinsics
    ///         configurations; in the last the whole span goes through the tail.
    ///     </para>
    /// </summary>
    public class SpanMapWidenRuntimeTests
    {
        private static readonly int[] Lengths = [.. Enumerable.Range(0, 71), 127, 128, 129, 1000, 1023, 1024, 1025];

        private static readonly SpanWidenMapper Mapper = new();

        /// <summary>
        ///     Every length above, random bits for the source (every value of the type is possible: negatives, extremes,
        ///     and for float NaNs and denormals), three sentinel slots past the source's end. Returns what went wrong, so
        ///     each test states its own assertion and a failure lists every element that differed.
        /// </summary>
        private static List<string> Mismatches<TSource, TTarget>(SpanMap<TSource, TTarget> map, Func<TSource, TTarget> widen, TTarget sentinel)
            where TSource : unmanaged
            where TTarget : unmanaged, IEquatable<TTarget>
        {
            var mismatches = new List<string>();
            var random = new Random(20261114);
            foreach (var length in Lengths)
            {
                var bits = new byte[length * 8];
                random.NextBytes(bits);
                var source = MemoryMarshal.Cast<byte, TSource>(bits)[..length].ToArray();
                var destination = new TTarget[length + 3];
                Array.Fill(destination, sentinel);

                map(source, destination);

                for (var i = 0; i < length; i++)
                {
                    var expected = widen(source[i]);
                    if (!expected.Equals(destination[i]))
                    {
                        mismatches.Add($"length {length}, element {i}: {source[i]} widened to {destination[i]}, expected {expected}");
                    }
                }

                for (var i = length; i < destination.Length; i++)
                {
                    if (!sentinel.Equals(destination[i]))
                    {
                        mismatches.Add($"length {length}: slot {i}, past the source, was written");
                    }
                }
            }

            return mismatches;
        }

        [Fact]
        public void Byte_to_ushort() => Assert.Empty(Mismatches<byte, ushort>(Mapper.Bytes, v => v, ushort.MaxValue));

        [Fact]
        public void UShort_to_uint() => Assert.Empty(Mismatches<ushort, uint>(Mapper.UShorts, v => v, uint.MaxValue));

        [Fact]
        public void UInt_to_ulong() => Assert.Empty(Mismatches<uint, ulong>(Mapper.UInts, v => v, ulong.MaxValue));

        [Fact]
        public void SByte_to_short() => Assert.Empty(Mismatches<sbyte, short>(Mapper.SBytes, v => v, short.MaxValue));

        [Fact]
        public void Short_to_int() => Assert.Empty(Mismatches<short, int>(Mapper.Shorts, v => v, int.MaxValue));

        [Fact]
        public void Int_to_long() => Assert.Empty(Mismatches<int, long>(Mapper.Ints, v => v, long.MaxValue));

        [Fact]
        public void Float_to_double() => Assert.Empty(Mismatches<float, double>(Mapper.Floats, v => v, double.MaxValue));

        [Fact]
        public void A_mutable_source_span() => Assert.Empty(Mismatches<int, long>((s, d) => Mapper.MutableInts(s.ToArray(), d), v => v, long.MaxValue));

        // The length guard still comes first: a short destination is refused before anything is written.
        [Fact]
        public void A_destination_shorter_than_the_source_is_refused_and_left_untouched()
        {
            var destination = new long[63];
            Array.Fill(destination, -1L);

            Assert.Throws<ArgumentException>(() => Mapper.Ints(new int[64], destination));
            Assert.All(destination, slot => Assert.Equal(-1L, slot));
        }

        // Still a span map: nothing is allocated, on the vector path or the tail.
        [Fact]
        public void Nothing_is_allocated()
        {
            var source = new int[1000];
            var destination = new long[1000];
            for (var i = 0; i < 200; i++) Mapper.Ints(source, destination); // warm up

            Assert.Equal(0, CostContract.ExactBytesOnFreshThread(() => Mapper.Ints(source, destination), 1000));
        }
    }
}
