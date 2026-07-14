// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

// Zero-alloc span mapping: `void Map(ReadOnlySpan<S> src, Span<D> dst)` maps element-wise into a
// caller-provided destination buffer (no heap allocation). Element conversions reuse the full
// pipeline. Defensive: a destination too small to hold the source throws (never silent truncation).

public struct SpPointSrc
{
    public int X;
    public int Y;
}

public struct SpPointDst
{
    public long X;
    public long Y;
}

[DwarfMapper]
public partial class SpanMapper
{
    // scalar element widening (int -> long, implicit)
    public partial void Map(ReadOnlySpan<int> src, Span<long> dst);

    // struct element mapping (nested, auto-synthesized)
    public partial void MapPoints(ReadOnlySpan<SpPointSrc> src, Span<SpPointDst> dst);
}

public class SpanMapRuntimeTests
{
    [Fact]
    public void Scalar_span_maps_elementwise_into_buffer()
    {
        ReadOnlySpan<int> src = stackalloc int[] { 1, 2, 3 };
        Span<long> dst = stackalloc long[3];

        new SpanMapper().Map(src, dst);

        Assert.Equal(1L, dst[0]);
        Assert.Equal(2L, dst[1]);
        Assert.Equal(3L, dst[2]);
    }

    [Fact]
    public void Struct_span_maps_elementwise()
    {
        Span<SpPointSrc> src = stackalloc SpPointSrc[2];
        src[0] = new SpPointSrc { X = 10, Y = 20 };
        src[1] = new SpPointSrc { X = 30, Y = 40 };
        Span<SpPointDst> dst = stackalloc SpPointDst[2];

        new SpanMapper().MapPoints(src, dst);

        Assert.Equal(10L, dst[0].X);
        Assert.Equal(20L, dst[0].Y);
        Assert.Equal(30L, dst[1].X);
        Assert.Equal(40L, dst[1].Y);
    }

    [Fact]
    public void Destination_too_small_throws_not_silent_truncation()
    {
        var caught = false;
        try
        {
            // Span can't cross a lambda/local-function boundary, so test inline.
            ReadOnlySpan<int> src = stackalloc int[] { 1, 2, 3 };
            Span<long> dst = stackalloc long[2]; // too small
            new SpanMapper().Map(src, dst);
        }
        catch (ArgumentException)
        {
            caught = true;
        }

        Assert.True(caught, "destination smaller than source must throw ArgumentException");
    }

    [Fact]
    public void Empty_source_leaves_destination_untouched()
    {
        var src = ReadOnlySpan<int>.Empty;
        Span<long> dst = stackalloc long[] { 111L, 222L };
        new SpanMapper().Map(src, dst); // no elements to map; must not throw or alter dst
        Assert.Equal(111L, dst[0]);
        Assert.Equal(222L, dst[1]);
    }
}
