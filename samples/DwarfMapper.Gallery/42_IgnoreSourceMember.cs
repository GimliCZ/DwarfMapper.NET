// SPDX-License-Identifier: GPL-2.0-only

// 42 — [MapIgnoreSource]: declare that a SOURCE member is intentionally unread.
// With RequiredMapping = Both, DwarfMapper checks coverage in both directions and reports DWARF039 for a
// source member no destination reads — which is how a forgotten field is caught. When the omission is
// deliberate, say so; the suggestion goes away and the intent is on the record.

namespace DwarfMapper.Gallery.Ex42
{
    public sealed class Miner
    {
        public string Name { get; set; } = "";

        public int Depth { get; set; }

        /// <summary>Deliberately not carried to the DTO.</summary>
        public string InternalNotes { get; set; } = "";
    }

    public sealed class MinerDto
    {
        public string Name { get; set; } = "";

        public int Depth { get; set; }
    }

// <snippet: ignore-source-member>
    [DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]
    public partial class Mapper
    {
        [MapIgnoreSource(nameof(Miner.InternalNotes))]
        public partial MinerDto ToDto(Miner miner);
    }
// </snippet>

    [DocExample(42,
        Tier.Configuration,
        "Intentionally unread source member",
        Shows = "silencing DWARF039 for a source member no destination reads")]
    public static class Example
    {
        public static void Run()
        {
            var dto = new Mapper().ToDto(new Miner
            {
                Name = "Durin",
                Depth = 900,
                InternalNotes = "not for export"
            });
            Console.WriteLine($"42 Ignore source      -> {dto.Name} at depth {dto.Depth} (notes dropped by design)");
        }
    }
}
