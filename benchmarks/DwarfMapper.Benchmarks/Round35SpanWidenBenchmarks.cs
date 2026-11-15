// SPDX-License-Identifier: GPL-2.0-only

using System;
using BenchmarkDotNet.Attributes;

namespace DwarfMapper.Benchmarks
{
    [DwarfMapper]
    public partial class Round35SpanWidenM
    {
        public partial void Ints(ReadOnlySpan<int> s, Span<long> d);

        public partial void Floats(ReadOnlySpan<float> s, Span<double> d);

        public partial void Bytes(ReadOnlySpan<byte> s, Span<ushort> d);
    }

    /// <summary>
    ///     <b>Round 35 T6: a span map of a lossless primitive widen pair takes the <c>Vector.Widen</c> loop.</b>
    ///     <para>
    ///         The array shape has widened with <c>Vector.Widen</c> since round 22 and the List shapes since round 33
    ///         (<see cref="Round33WidenListBenchmarks" />); the span endpoint kept the plain element loop. Each pair has
    ///         two arms in ONE process, on one payload: Before is the span map as emitted before the change, verbatim;
    ///         After calls the generated mapper. Nothing is allocated by either - the caller owns both spans - so the
    ///         ratio is the loop alone. The round's threshold is the one round 33 used: at least 1.3x, Before against
    ///         After, at n = 1,024.
    ///     </para>
    /// </summary>
    [MemoryDiagnoser]
    public class Round35SpanWidenBenchmarks
    {
        private readonly Round35SpanWidenM _mapper = new();
        private int[] _ints = null!;
        private long[] _longs = null!;
        private float[] _floats = null!;
        private double[] _doubles = null!;
        private byte[] _bytes = null!;
        private ushort[] _ushorts = null!;

        [Params(16, 1024)]
        public int N { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            // A widen has no branch on the value, so any spread serves; this one is deterministic, signed, and
            // covers the whole range (a multiplicative hash of the index), with no generator to seed.
            _ints = new int[N];
            _floats = new float[N];
            _bytes = new byte[N];
            for (var i = 0; i < N; i++)
            {
                var h = unchecked((uint)(i + RealisticPayloads.Seed) * 2654435761u);
                _ints[i] = unchecked((int)h);
                _floats[i] = (float)((h % 2_000_001) - 1_000_000.0);
                _bytes[i] = (byte)h;
            }

            _longs = new long[N];
            _doubles = new double[N];
            _ushorts = new ushort[N];
        }

        // ── int → long ──────────────────────────────────────────────────────────────────────────────────────
        [Benchmark(Baseline = true)]
        [BenchmarkCategory("IntToLong")]
        public void IntToLong_Before()
        {
            BeforeInts(_ints, _longs);
        }

        [Benchmark]
        [BenchmarkCategory("IntToLong")]
        public void IntToLong_After()
        {
            _mapper.Ints(_ints, _longs);
        }

        // ── float → double ──────────────────────────────────────────────────────────────────────────────────
        [Benchmark]
        [BenchmarkCategory("FloatToDouble")]
        public void FloatToDouble_Before()
        {
            BeforeFloats(_floats, _doubles);
        }

        [Benchmark]
        [BenchmarkCategory("FloatToDouble")]
        public void FloatToDouble_After()
        {
            _mapper.Floats(_floats, _doubles);
        }

        // ── byte → ushort ───────────────────────────────────────────────────────────────────────────────────
        [Benchmark]
        [BenchmarkCategory("ByteToUShort")]
        public void ByteToUShort_Before()
        {
            BeforeBytes(_bytes, _ushorts);
        }

        [Benchmark]
        [BenchmarkCategory("ByteToUShort")]
        public void ByteToUShort_After()
        {
            _mapper.Bytes(_bytes, _ushorts);
        }

        // ── The Before arms: the span maps as emitted before round 35 T6, verbatim ──────────────────────────────
        private static void BeforeInts(ReadOnlySpan<int> s, Span<long> d)
        {
            if (d.Length < s.Length)
                throw new ArgumentException("DwarfMapper: destination span (length " + d.Length + ") is smaller than the source span (length " + s.Length + ").", nameof(d));
            for (int __i = 0; __i < s.Length; __i++)
                d[__i] = s[__i];
        }

        private static void BeforeFloats(ReadOnlySpan<float> s, Span<double> d)
        {
            if (d.Length < s.Length)
                throw new ArgumentException("DwarfMapper: destination span (length " + d.Length + ") is smaller than the source span (length " + s.Length + ").", nameof(d));
            for (int __i = 0; __i < s.Length; __i++)
                d[__i] = s[__i];
        }

        private static void BeforeBytes(ReadOnlySpan<byte> s, Span<ushort> d)
        {
            if (d.Length < s.Length)
                throw new ArgumentException("DwarfMapper: destination span (length " + d.Length + ") is smaller than the source span (length " + s.Length + ").", nameof(d));
            for (int __i = 0; __i < s.Length; __i++)
                d[__i] = s[__i];
        }
    }
}
