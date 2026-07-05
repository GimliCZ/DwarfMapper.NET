// SPDX-License-Identifier: GPL-2.0-only

// 03 — Built-in type conversions (no config needed).
// Differently-typed members are bridged automatically and AOT-safely: lossless widening (int -> long) and
// enum-by-name are silent. (Parse/narrowing like string -> int also work, but surface a DWARF038 *suggestion*
// so they're visible, never silent — see docs/diagnostics.md#dwarf038.)

namespace DwarfMapper.Gallery.Ex03;

public enum Rank
{
    Miner,
    Smith,
    Lord
}

public enum RankDto
{
    Miner,
    Smith,
    Lord
}

public sealed class Hero
{
    public int Level { get; set; }
    public Rank Rank { get; set; }
}

public sealed class HeroDto
{
    public long Level { get; set; }
    public RankDto Rank { get; set; }
}

[DwarfMapper]
public partial class Mapper
{
    public partial HeroDto ToDto(Hero h); // int -> long (widen), Rank -> RankDto (by name)
}

public static class Example
{
    public static void Run()
    {
        var dto = new Mapper().ToDto(new Hero { Level = 7, Rank = Rank.Smith });
        Console.WriteLine($"03 Built-in convert   -> level {dto.Level} (long), rank {dto.Rank}");
    }
}
