// SPDX-License-Identifier: GPL-2.0-only

// 05 — Collections and dictionaries.
// Lists, arrays, sets and dictionaries map element-by-element, applying the same rules per element (here the
// nested Line -> LineDto). When the element type is unchanged (the string[] Tags), the whole block is bulk-copied.

namespace DwarfMapper.Gallery.Ex05;

public sealed class Line
{
    public string Sku { get; set; } = "";
    public int Qty { get; set; }
}

public sealed class LineDto
{
    public string Sku { get; set; } = "";
    public int Qty { get; set; }
}

public sealed class Basket
{
    public List<Line> Lines { get; set; } = new();
    public string[] Tags { get; set; } = Array.Empty<string>();
}

public sealed class BasketDto
{
    public List<LineDto> Lines { get; set; } = new();
    public string[] Tags { get; set; } = Array.Empty<string>();
}

[DwarfMapper]
public partial class Mapper
{
    public partial BasketDto ToDto(Basket b);
}

public static class Example
{
    public static void Run()
    {
        var dto = new Mapper().ToDto(new Basket
        {
            Lines = new List<Line> { new() { Sku = "AXE-1", Qty = 2 }, new() { Sku = "ALE-9", Qty = 6 } },
            Tags = new[] { "forged", "stout" }
        });
        Console.WriteLine($"05 Collections        -> {dto.Lines.Count} lines, tags: {string.Join(", ", dto.Tags)}");
    }
}
