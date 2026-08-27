// SPDX-License-Identifier: GPL-2.0-only

// 47 — [RestatesBase<TSource, TTarget>]: declare that a pair restates its base pair's configuration.
// DwarfMapper has no inheritance primitive on purpose — every pair's configuration is written where it is
// used, so reading one mapping never means chasing a base class. The cost is duplication between a base
// pair and a derived one, and duplication drifts. This attribute makes the two checkable against each
// other, so drift is REPORTED rather than discovered later.

namespace DwarfMapper.Gallery.Ex47
{
    public class Tool
    {
        public string Maker { get; set; } = "";
    }

    public sealed class Hammer : Tool
    {
        public int Heft { get; set; }
    }

    public class ToolDto
    {
        public string Forger { get; set; } = "";
    }

    public sealed class HammerDto : ToolDto
    {
        public int Heft { get; set; }
    }

// <snippet: restate-base-config>
    [DwarfMapper]
    [MapProperty<Tool, ToolDto>(nameof(Tool.Maker), nameof(ToolDto.Forger))]
    [MapProperty<Hammer, HammerDto>(nameof(Tool.Maker), nameof(ToolDto.Forger))]
    [RestatesBase<Hammer, HammerDto>]
    public partial class Mapper
    {
        public partial ToolDto ToDto(Tool tool);

        public partial HammerDto ToDto(Hammer hammer);
    }
// </snippet>

    [DocExample(47,
        Tier.Advanced,
        "Restated base configuration",
        Shows = "checking a derived pair against the base pair it restates")]
    public static class Example
    {
        public static void Run()
        {
            var dto = new Mapper().ToDto(new Hammer
            {
                Maker = "Gimli",
                Heft = 3
            });
            Console.WriteLine($"47 Restates base      -> forger {dto.Forger}, heft {dto.Heft}");
        }
    }
}
