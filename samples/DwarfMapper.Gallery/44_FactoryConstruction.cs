// SPDX-License-Identifier: GPL-2.0-only

// 44 — [MapConstructor<TSource, TTarget>]: build the destination with your own factory.
// Pair-scoped, declared on the MAPPER rather than on the type — so a type you do not own can still be
// constructed your way. The factory runs first; every settable member is then assigned as usual, while
// init-only, required and get-only members stay the factory's responsibility.

namespace DwarfMapper.Gallery.Ex44
{
    public sealed class Rune
    {
        public string Glyph { get; set; } = "";

        public int Power { get; set; }
    }

    public sealed class RuneDto
    {
        public RuneDto(string glyph)
        {
            Glyph = glyph;
        }

        /// <summary>Get-only: the factory must supply it, and the mapper will not reassign it.</summary>
        public string Glyph { get; }

        public int Power { get; set; }
    }

// <snippet: factory-construction>
    [DwarfMapper]
    [GenerateMap<Rune, RuneDto>]
    [MapConstructor<Rune, RuneDto>(nameof(CreateRune))]
    public partial class Mapper
    {
        // Any method taking the source and returning the target. Power is settable, so the generator still
        // assigns it afterwards — the factory only has to cover what the mapper cannot set.
        public static RuneDto CreateRune(Rune rune)
        {
            ArgumentNullException.ThrowIfNull(rune);
            return new RuneDto(rune.Glyph.ToUpperInvariant());
        }
    }
// </snippet>

    [DocExample(44,
        Tier.Advanced,
        "Factory-based construction",
        Shows = "constructing the destination through your own factory, pair-scoped")]
    public static class Example
    {
        public static void Run()
        {
            var dto = new Mapper().Map(new Rune
            {
                Glyph = "ansuz",
                Power = 7
            });
            Console.WriteLine($"44 Factory ctor       -> glyph {dto.Glyph} (from factory), power {dto.Power} (assigned)");
        }
    }
}
