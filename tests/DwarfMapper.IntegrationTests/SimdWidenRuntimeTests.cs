// SPDX-License-Identifier: GPL-2.0-only



// CA5394: seeded System.Random for deterministic adversary/fuzz data only. Not security.
#pragma warning disable CA5394

namespace DwarfMapper.IntegrationTests;

// SIMD-widening fast-path for primitive array conversions. The vectorized result MUST be bit-for-bit
// identical to the scalar implicit widening — these tests are the safety net (unit + adversary/fuzz).

public class SwSrc
{
    public int[] Ints { get; set; } = Array.Empty<int>();
    public short[] Shorts { get; set; } = Array.Empty<short>();
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public ushort[] UShorts { get; set; } = Array.Empty<ushort>();
    public uint[] UInts { get; set; } = Array.Empty<uint>();
    public sbyte[] SBytes { get; set; } = Array.Empty<sbyte>();
    public float[] Floats { get; set; } = Array.Empty<float>();
}

public class SwDst
{
    public long[] Ints { get; set; } = Array.Empty<long>(); // int   → long
    public int[] Shorts { get; set; } = Array.Empty<int>(); // short → int
    public ushort[] Bytes { get; set; } = Array.Empty<ushort>(); // byte  → ushort
    public uint[] UShorts { get; set; } = Array.Empty<uint>(); // ushort→ uint
    public ulong[] UInts { get; set; } = Array.Empty<ulong>(); // uint  → ulong
    public short[] SBytes { get; set; } = Array.Empty<short>(); // sbyte → short
    public double[] Floats { get; set; } = Array.Empty<double>(); // float → double
}

[DwarfMapper]
public partial class SwMapper
{
    public partial SwDst Map(SwSrc s);
}

// Dedicated int[]→long[] mapper for focused length fuzzing.
public class SwIntSrc
{
    public int[] V { get; set; } = Array.Empty<int>();
}

public class SwLongDst
{
    public long[] V { get; set; } = Array.Empty<long>();
}

[DwarfMapper]
public partial class SwIntMapper
{
    public partial SwLongDst Map(SwIntSrc s);
}

public class SimdWidenRuntimeTests
{
    [Fact]
    public void All_seven_widen_pairs_map_correctly()
    {
        var src = new SwSrc
        {
            Ints = new[] { int.MinValue, -1, 0, 1, int.MaxValue },
            Shorts = new short[] { short.MinValue, -1, 0, 1, short.MaxValue },
            Bytes = new byte[] { 0, 1, 127, 128, 255 },
            UShorts = new ushort[] { 0, 1, 32767, 65535 },
            UInts = new uint[] { 0, 1, uint.MaxValue },
            SBytes = new sbyte[] { sbyte.MinValue, -1, 0, 1, sbyte.MaxValue },
            Floats = new[] { float.MinValue, -1.5f, 0f, 1.5f, float.MaxValue, float.NaN, float.PositiveInfinity }
        };
        var d = new SwMapper().Map(src);

        Assert.Equal(src.Ints.Select(x => (long)x), d.Ints);
        Assert.Equal(src.Shorts.Select(x => (int)x), d.Shorts);
        Assert.Equal(src.Bytes.Select(x => (ushort)x), d.Bytes);
        Assert.Equal(src.UShorts.Select(x => (uint)x), d.UShorts);
        Assert.Equal(src.UInts.Select(x => (ulong)x), d.UInts);
        Assert.Equal(src.SBytes.Select(x => (short)x), d.SBytes);
        // float→double: exact widening (incl. NaN/Inf). Compare bitwise to handle NaN.
        Assert.Equal(src.Floats.Select(x => (double)x).Select(BitConverter.DoubleToInt64Bits),
            d.Floats.Select(BitConverter.DoubleToInt64Bits));
    }

    // ── Adversary/fuzz: every length around the SIMD vector boundary must equal the scalar oracle ──
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
    public void Vectorized_equals_scalar_oracle_for_every_length(int len)
    {
        var rng = new Random(len + 12345);
        var ints = new int[len];
        for (var i = 0; i < len; i++) ints[i] = rng.Next(int.MinValue, int.MaxValue);

        var dst = new SwIntMapper().Map(new SwIntSrc { V = ints });

        Assert.Equal(len, dst.V.Length);
        for (var i = 0; i < len; i++)
            Assert.Equal(ints[i], dst.V[i]); // SIMD result must equal the scalar implicit widen
    }

    [Theory]
    [InlineData(13)]
    [InlineData(777)]
    [InlineData(99999)]
    public void Fuzz_random_arrays_match_scalar_oracle(int seed)
    {
        var rng = new Random(seed);
        for (var iter = 0; iter < 40; iter++)
        {
            var len = rng.Next(0, 300);
            var ints = new int[len];
            for (var i = 0; i < len; i++) ints[i] = rng.Next(int.MinValue, int.MaxValue);

            var dst = new SwIntMapper().Map(new SwIntSrc { V = ints });
            var oracle = ints.Select(x => (long)x).ToArray();
            Assert.Equal(oracle, dst.V);
        }
    }

    [Fact]
    public void Empty_and_null_arrays_are_safe()
    {
        var d0 = new SwIntMapper().Map(new SwIntSrc { V = Array.Empty<int>() });
        Assert.Empty(d0.V);
        // null source array → empty target (None-collection default), never NRE.
        var dn = new SwIntMapper().Map(new SwIntSrc { V = null! });
        Assert.NotNull(dn.V);
        Assert.Empty(dn.V);
    }
}
