// SPDX-License-Identifier: GPL-2.0-only

// 50 — An enum-keyed dictionary written into an inline array.
//
// A `Dictionary<Ore, int>` per row is a hash table per row: an object, a bucket array, an entry array, and a
// hash on every read. When the key is a small enum, all of that is buying you nothing — the key already IS a
// number, and the number is dense. `[InlineArray(n)]` gives you n slots that live INSIDE the object that
// declares them, and [MapDenseEnumKeys] fills them by index: `slots[(int)key - Offset] = value`.
//
// `Offset = 1` because `Ore` is 1-based, which is the usual shape when 0 means "unset". Without it the array
// would need a slot for a value nothing ever uses.
//
// What the generator PROVES before emitting that line: every member `Ore` declares lands inside
// [Offset, Offset + n). Both ends — a negative member would index in FRONT of the array — and in a width that
// cannot wrap, so a member outside `int` cannot cast its way into range. Add a fifth ore tomorrow and this
// file stops compiling with DWARF105 naming it: the proof re-runs on every build, which is the whole safety
// argument. There is deliberately no bounds-checked fallback — a shape that cannot be proven is refused, not
// mapped more slowly.
//
// What no compile-time proof can reach is a key that is not a declared member: `(Ore)999` is legal C#. The
// emitted loop range-checks the index and throws ArgumentOutOfRangeException naming the key, so an
// unexpected key is a diagnosable failure rather than a write into a slot that belongs to something else.
//
// [Flags] enums are refused. Their key space is the power set of their members — `Iron | Gold` is a
// legitimate key that no member declares — so proving the declared members are in range would prove nothing.

using System.Runtime.CompilerServices;

namespace DwarfMapper.Gallery.Ex50
{
// CA1008 asks every enum for a zero member. This one deliberately has none: a 1-based enum with no "unset"
// value is the shape Offset exists for, and adding a None = 0 here to satisfy the analyzer would delete the
// very thing the example is about — the wasted slot at index 0 that Offset removes.
#pragma warning disable CA1008
    public enum Ore
    {
        Iron = 1,
        Copper = 2,
        Mithril = 3,
        Adamantine = 4
    }
#pragma warning restore CA1008

    /// <summary>Four <c>int</c> slots with no object behind them — they live inside whatever declares them.</summary>
    [InlineArray(4)]
    public struct OreTally
    {
        private int _e0;
    }

    public sealed class Seam
    {
        public string Name { get; init; } = "";

        public Dictionary<Ore, int> Yield { get; init; } = new();
    }

    public sealed class SeamReport
    {
        public string Name { get; set; } = "";

        // A FIELD rather than a property: an inline array read through a property is a struct COPY, so
        // `report.Yield[0] = x` would not compile for a caller. Both are writable destinations as far as the
        // mapper is concerned — it assigns the whole array either way.
        public OreTally Yield;
    }

// <snippet: map-dense-enum-keys>
    [DwarfMapper]
    public partial class Mapper
    {
        // Ore is 1-based, so Offset = 1 puts Ore.Iron in slot 0 and the array needs exactly four slots.
        [MapDenseEnumKeys(nameof(SeamReport.Yield), Offset = 1)]
        public partial SeamReport ToReport(Seam s);
    }
// </snippet>

    [DocExample(50,
        Tier.Advanced,
        "`[MapDenseEnumKeys]` — an enum-keyed dictionary as an inline array",
        Shows = "indexing a fixed-size inline array by enum value instead of hashing, with the range proven at compile time")]
    public static class Example
    {
        public static void Run()
        {
            var seam = new Seam
            {
                Name = "deep vein",
                Yield = new Dictionary<Ore, int>
                {
                    [Ore.Iron] = 40,
                    [Ore.Mithril] = 3
                }
            };

            var report = new Mapper().ToReport(seam);

            Console.WriteLine(
                $"50 [MapDenseEnumKeys] -> {report.Name}: iron {report.Yield[0]}, copper {report.Yield[1]}, " +
                $"mithril {report.Yield[2]}, adamantine {report.Yield[3]}");
        }
    }
}
