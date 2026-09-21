// SPDX-License-Identifier: GPL-2.0-only

// 49 — Sharing an immutable collection instead of copying it.
//
// Two members, and only one of them needs you.
//
// `Alloys` is an ImmutableList<Alloy> on both sides and `Alloy` is sealed with nothing but get-only members,
// so the generator can PROVE that nothing reachable through the reference can ever be written. It assigns
// the reference. No attribute, no option, no allocation — a statically decidable optimization should not
// have to be asked for.
//
// `Runes` is an IReadOnlyList<Rune>, and the proof refuses it on principle. An interface is not a guarantee:
// a List<Rune> assigned to that member is still a List<Rune> at run time, and a source that mutates it after
// the map would silently change the destination too. So the automatic path copies, and [MapShare] is where
// you say "I know this instance is never mutated" — the same bargain [Reinterpret] offers for a memory
// layout the blit proof declines to confirm. If you are wrong, two object graphs you believe are
// independent are not, and no diagnostic will tell you.
//
// What you CANNOT assert away: a settable property, a writable field, an event or an array anywhere in the
// graph is DWARF104, in both modes. The generator refuses what it can disprove and takes your word only for
// what it merely cannot prove.

using System.Collections.Immutable;

namespace DwarfMapper.Gallery.Ex49
{
    public sealed class Alloy
    {
        public Alloy(string name, int hardness)
        {
            Name = name;
            Hardness = hardness;
        }

        public string Name { get; }

        public int Hardness { get; }
    }

    public sealed class Rune
    {
        public Rune(string glyph)
        {
            Glyph = glyph;
        }

        public string Glyph { get; }
    }

    public sealed class Forge
    {
        public ImmutableList<Alloy> Alloys { get; init; } = ImmutableList<Alloy>.Empty;

        public IReadOnlyList<Rune> Runes { get; init; } = [];
    }

    public sealed class ForgeDto
    {
        public ImmutableList<Alloy> Alloys { get; init; } = ImmutableList<Alloy>.Empty;

        public IReadOnlyList<Rune> Runes { get; init; } = [];
    }

// <snippet: map-share>
    [DwarfMapper]
    public partial class Mapper
    {
        // Alloys is shared automatically: same type both sides, and Alloy is provably immutable.
        // Runes is an INTERFACE, which the proof refuses — [MapShare] is the caller's own assertion.
        [MapShare(nameof(ForgeDto.Runes))]
        public partial ForgeDto ToDto(Forge f);
    }
// </snippet>

    [DocExample(49,
        Tier.Advanced,
        "`[MapShare]` — share an immutable reference",
        Shows = "assigning a collection's reference instead of copying it, automatically where it is provable")]
    public static class Example
    {
        public static void Run()
        {
            var runes = new List<Rune>
            {
                new("ᚦ"),
                new("ᚨ")
            };
            var forge = new Forge
            {
                Alloys = ImmutableList.Create(new Alloy("mithril", 12)),
                Runes = runes
            };

            var dto = new Mapper().ToDto(forge);

            Console.WriteLine(
                $"49 [MapShare]         -> alloys shared: {ReferenceEquals(forge.Alloys, dto.Alloys)}, " +
                $"runes shared: {ReferenceEquals(runes, dto.Runes)}");
        }
    }
}
