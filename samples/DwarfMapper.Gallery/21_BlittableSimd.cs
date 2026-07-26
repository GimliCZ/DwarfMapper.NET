// SPDX-License-Identifier: GPL-2.0-only

// 21 — The one place DwarfMapper is faster than the other compile-time mappers rather than merely equal.
// When a whole array member's element type is unmanaged, identically sized and layout-compatible, the
// generator proves it and bulk-copies the entire span with MemoryMarshal.Cast + CopyTo behind a runtime size
// guard, instead of writing fields element by element.
//
// The proof is automatic and total: no cast is emitted without a full layout+name proof, and anything that
// fails the proof silently falls back to ordinary per-member assignment. You write nothing to opt in — this
// mapper is declared exactly like example 01. The speed is a property of the DATA, not of the declaration.

namespace DwarfMapper.Gallery.Ex21;

public struct Vein
{
    public int Depth;
    public int Yield;
}

public struct VeinDto
{
    public int Depth;
    public int Yield;
}

public sealed class Seam
{
    public Vein[] Veins { get; set; } = [];
}

public sealed class SeamDto
{
    public VeinDto[] Veins { get; set; } = [];
}

// <snippet: blittable-simd>
[DwarfMapper]
public partial class Mapper
{
    // Vein[] -> VeinDto[]: unmanaged, same size, same field names and order, so the whole array is
    // bulk-copied rather than looped. Nothing here asks for that; the generator proves it.
    public partial SeamDto ToDto(Seam s);
}
// </snippet>

[DocExample(21, Tier.Advanced, "Blittable bulk copy",
    Shows = "a layout-identical array is bulk-copied, not looped — proven, never assumed")]
public static class Example
{
    public static void Run()
    {
        var dto = new Mapper().ToDto(new Seam
        {
            Veins = [new Vein { Depth = 700, Yield = 12 }, new Vein { Depth = 1400, Yield = 3 }]
        });

        Console.WriteLine(
            $"21 Blittable copy     -> {dto.Veins.Length} veins, first {dto.Veins[0].Depth}m/{dto.Veins[0].Yield}");
    }
}
