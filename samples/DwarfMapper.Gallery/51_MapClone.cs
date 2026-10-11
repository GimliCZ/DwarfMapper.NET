// SPDX-License-Identifier: GPL-2.0-only

// 51 — [MapClone]: a same-type map, on purpose.
// A map from a type to itself is usually a mistyped type argument, so DwarfMapper reports DWARF076 — and under
// warnings-as-errors the build stops. When a copy IS what you want (a snapshot taken before an edit, say), [MapClone]
// says so on the one method: DWARF076 goes away there, the generated copy is exactly the same, and every other
// same-type map in the project is still caught. The copy is member by member: a nested object is shared, a
// collection is rebuilt with the same elements.

namespace DwarfMapper.Gallery.Ex51
{
    public sealed class Tunnel
    {
        public string Name { get; set; } = "";

        public int Depth { get; set; }
    }

// <snippet: map-clone>
    [DwarfMapper]
    public partial class Mapper
    {
        [MapClone]
        public partial Tunnel Snapshot(Tunnel tunnel);
    }
// </snippet>

    [DocExample(51,
        Tier.Configuration,
        "A deliberate same-type copy",
        Shows = "accepting DWARF076 for one method with [MapClone]")]
    public static class Example
    {
        public static void Run()
        {
            var live = new Tunnel
            {
                Name = "Khazad",
                Depth = 1200
            };
            var snapshot = new Mapper().Snapshot(live);
            live.Depth = 1300;
            Console.WriteLine($"51 Clone              -> snapshot of {snapshot.Name} at {snapshot.Depth}, live now at {live.Depth}");
        }
    }
}
