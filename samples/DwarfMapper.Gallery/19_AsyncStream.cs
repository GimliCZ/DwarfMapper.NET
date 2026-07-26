// SPDX-License-Identifier: GPL-2.0-only

// 19 — Streaming a mapped sequence without materializing it. Declare
// `IAsyncEnumerable<D> Map(IAsyncEnumerable<S>)` and the generator emits an async iterator that yields each
// converted element as it arrives, so back-pressure is preserved and the whole sequence never exists at once.
// Ideal over a DB cursor or a network stream.

namespace DwarfMapper.Gallery.Ex19;

public sealed class Ore
{
    public string Kind { get; set; } = "";
    public int Weight { get; set; }
}

public sealed class OreDto
{
    public string Kind { get; set; } = "";
    public long Weight { get; set; }
}

// <snippet: async-stream>
[DwarfMapper]
public partial class Mapper
{
    public partial IAsyncEnumerable<OreDto> Map(IAsyncEnumerable<Ore> src);
}
// </snippet>

[DocExample(19, Tier.Advanced, "Async streaming",
    Shows = "`IAsyncEnumerable<D> Map(IAsyncEnumerable<S>)` — element-by-element, no buffering")]
public static class Example
{
    public static void Run()
    {
        var kinds = CollectAsync().GetAwaiter().GetResult();
        Console.WriteLine($"19 Async stream       -> {string.Join(", ", kinds)}");
    }

    private static async Task<List<string>> CollectAsync()
    {
        var seen = new List<string>();
        await foreach (var dto in new Mapper().Map(Quarry()).ConfigureAwait(false))
            seen.Add($"{dto.Kind}:{dto.Weight}");
        return seen;
    }

    /// <summary>Stands in for a cursor or socket — yields one item at a time, never a whole list.</summary>
    private static async IAsyncEnumerable<Ore> Quarry()
    {
        foreach (var ore in new[]
                 {
                     new Ore { Kind = "mithril", Weight = 3 },
                     new Ore { Kind = "iron", Weight = 9 }
                 })
        {
            await Task.Yield();
            yield return ore;
        }
    }
}
