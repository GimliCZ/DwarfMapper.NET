// SPDX-License-Identifier: GPL-2.0-only

// 17 — Mapping onto an EXISTING instance instead of constructing one. Declare a two-parameter partial method
// and the target's identity is preserved — the object you passed in is the object that was mutated, which is
// what you need for a tracked entity, a pooled object, or a pre-allocated buffer.
//
// Pick exactly one shape per pair: `void Update(src, dest)` or `TDest Update(src, dest)` returning dest.
// You cannot declare both, because C# does not overload on return type.

namespace DwarfMapper.Gallery.Ex17;

public sealed class ForgeOrderDto
{
    public string Smith { get; set; } = "";
    public int Quantity { get; set; }
}

public sealed class ForgeOrder
{
    public string Smith { get; set; } = "";
    public int Quantity { get; set; }
}

// <snippet: update-into>
[DwarfMapper]
public partial class Mapper
{
    public partial void Update(ForgeOrderDto src, ForgeOrder dest);   // mutates dest in place
}
// </snippet>

[DocExample(17, Tier.Advanced, "Update an existing instance",
    Shows = "a two-parameter partial method preserves the target's identity")]
public static class Example
{
    public static void Run()
    {
        var tracked = new ForgeOrder { Smith = "Telchar", Quantity = 1 };
        var alias = tracked;   // a second reference, standing in for the one your DbContext is holding

        new Mapper().Update(new ForgeOrderDto { Smith = "Telchar", Quantity = 40 }, tracked);

        // alias sees the new quantity because the target was MUTATED, not replaced — which is the whole
        // point of update-into. A create-style map would have left alias.Quantity at 1.
        Console.WriteLine(
            $"17 Update-into        -> {tracked.Smith} x{tracked.Quantity}; alias sees {alias.Quantity}");
    }
}
