// SPDX-License-Identifier: GPL-2.0-only

// 40 — [BeforeMap] hooks: run your own code before a map, without wrapping the mapper.
// The hook is matched by SOURCE TYPE: `void Hook(TSource)` runs for every mapping method whose source is
// assignable to that parameter, so one hook can cover a family of maps. Useful for validation or for
// normalising a source in place before it is read.

namespace DwarfMapper.Gallery.Ex40
{
    public sealed class Ore
    {
        public string Name { get; set; } = "";

        public int Weight { get; set; }
    }

    public sealed class OreDto
    {
        public string Name { get; set; } = "";

        public int Weight { get; set; }
    }

// <snippet: before-map-hook>
    [DwarfMapper]
    public partial class Mapper
    {
        public partial OreDto ToDto(Ore ore);

        // Runs before every map whose source is an Ore. Trim the name at the source, once, rather than at
        // each destination member.
        [BeforeMap]
        public void Normalise(Ore ore)
        {
            ArgumentNullException.ThrowIfNull(ore);
            ore.Name = ore.Name.Trim();
        }
    }
// </snippet>

    [DocExample(40,
        Tier.Configuration,
        "Before-map hook",
        Shows = "running your own code before a map, matched by source type")]
    public static class Example
    {
        public static void Run()
        {
            var dto = new Mapper().ToDto(new Ore
            {
                Name = "   mithril   ",
                Weight = 12
            });
            Console.WriteLine($"40 Before-map hook    -> name '{dto.Name}' (trimmed), weight {dto.Weight}");
        }
    }
}
