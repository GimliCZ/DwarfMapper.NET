// SPDX-License-Identifier: GPL-2.0-only

// 18 — Zero-allocation mapping into a caller-provided buffer. Declare `void Map(ReadOnlySpan<S>, Span<D>)`
// and each element runs through the full conversion pipeline (dst[i] = convert(src[i])) with nothing on the
// heap — the buffer can be stackalloc'd or pooled.
//
// A destination shorter than the source throws ArgumentException rather than truncating silently.

namespace DwarfMapper.Gallery.Ex18;

// <snippet: span-map>
[DwarfMapper]
public partial class Mapper
{
    public partial void Map(ReadOnlySpan<int> src, Span<long> dst);   // int -> long, widened per element
}
// </snippet>

[DocExample(18, Tier.Advanced, "Zero-alloc span mapping",
    Shows = "`void Map(ReadOnlySpan<S>, Span<D>)` into a caller-provided buffer")]
public static class Example
{
    public static void Run()
    {
        ReadOnlySpan<int> depths = stackalloc int[] { 700, 1400, 2100 };
        Span<long> fathoms = stackalloc long[3];

        new Mapper().Map(depths, fathoms);

        Console.WriteLine($"18 Span map           -> [{fathoms[0]}, {fathoms[1]}, {fathoms[2]}] (no heap)");
    }
}
