// SPDX-License-Identifier: GPL-2.0-only

// 43 — [DwarfMapperConstructor]: choose which constructor the mapper calls.
// When a destination has several constructors, DwarfMapper picks one by policy. Marking one constructor
// settles it unconditionally — useful when the policy's choice is legal but not what you meant.

namespace DwarfMapper.Gallery.Ex43
{
    public sealed class Gem
    {
        public string Kind { get; set; } = "";

        public int Carats { get; set; }
    }

// <snippet: constructor-selection>
    public sealed class GemDto
    {
        // The policy would consider both constructors. The attribute says which one is the real entry point.
        [DwarfMapperConstructor]
        public GemDto(string kind, int carats)
        {
            Kind = kind;
            Carats = carats;
        }

        public GemDto(string kind)
            : this(kind, 0)
        {
        }

        public string Kind { get; }

        public int Carats { get; }
    }

    [DwarfMapper]
    public partial class Mapper
    {
        public partial GemDto ToDto(Gem gem);
    }
// </snippet>

    [DocExample(43,
        Tier.Configuration,
        "Explicit constructor selection",
        Shows = "marking the constructor a mapped type should be built through")]
    public static class Example
    {
        public static void Run()
        {
            var dto = new Mapper().ToDto(new Gem
            {
                Kind = "Ruby",
                Carats = 5
            });
            Console.WriteLine($"43 Ctor selection     -> {dto.Kind} ({dto.Carats} carats, via the 2-arg ctor)");
        }
    }
}
