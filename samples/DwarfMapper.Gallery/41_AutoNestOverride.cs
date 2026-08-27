// SPDX-License-Identifier: GPL-2.0-only

// 41 — [AutoNest(false)]: turn OFF nested-mapper synthesis for one method.
// A [DwarfMapper] class synthesises maps for nested object members automatically. Occasionally one method
// should not: the nested type is mapped elsewhere, or you want the member left at its default rather than
// deep-copied. This is the per-METHOD override of the class-level setting.

namespace DwarfMapper.Gallery.Ex41
{
    public sealed class Forge
    {
        public string Name { get; set; } = "";

        public Anvil? Anvil { get; set; }
    }

    public sealed class Anvil
    {
        public int Mass { get; set; }
    }

    public sealed class AnvilDto
    {
        public int Mass { get; set; }
    }

    public sealed class ForgeDto
    {
        public string Name { get; set; } = "";

        public AnvilDto? Anvil { get; set; }
    }

    public sealed class ForgeSummaryDto
    {
        public string Name { get; set; } = "";
    }

// <snippet: auto-nest-override>
    [DwarfMapper]
    public partial class Mapper
    {
        // Nested Anvil -> AnvilDto is synthesised for you.
        public partial ForgeDto ToDto(Forge forge);

        // The summary carries no nested member, so nothing needs synthesising for it. Saying so explicitly
        // keeps the intent on the method rather than in the reader's head.
        [AutoNest(false)]
        public partial ForgeSummaryDto ToSummary(Forge forge);
    }
// </snippet>

    [DocExample(41,
        Tier.Configuration,
        "Per-method auto-nest override",
        Shows = "disabling nested-mapper synthesis for a single method")]
    public static class Example
    {
        public static void Run()
        {
            var mapper = new Mapper();
            var forge = new Forge
            {
                Name = "Deep Forge",
                Anvil = new Anvil { Mass = 400 }
            };

            var full = mapper.ToDto(forge);
            var summary = mapper.ToSummary(forge);
            Console.WriteLine($"41 Auto-nest override -> full anvil {full.Anvil?.Mass}, summary '{summary.Name}'");
        }
    }
}
