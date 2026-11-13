// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // An array of layout-identical structs is block-copied - unless the element pair's own partial says otherwise
    // (round 35 T2; the last untested cell of round 33's fast-path audit). The element method here ignores one member,
    // so a block copy would carry it across anyway: the array map has to go through the element method instead.
    //
    // Held here for a mapper WITHOUT reference handling, where it is true. Under ReferenceHandling.Preserve and
    // OnCycle.SetNull it is NOT true today, and not only for arrays: a nested member or element of a pair that has a
    // declared partial is routed through a generated helper, and that helper applies the class's pair-scoped
    // directives but not the directives written on the method. Issues/round35/ROUND35-TASKS.md, T2, has the measured
    // output and the decision that is open; the two modes get their facts here with the fix.
    public struct AbeCell
    {
        public int Kept;

        public int Dropped;
    }

    public struct AbeCellDto
    {
        public int Kept;

        public int Dropped;
    }

    public class AbeGrid
    {
        public AbeCell[] Cells { get; set; } = [];
    }

    public class AbeGridDto
    {
        public AbeCellDto[] Cells { get; set; } = [];
    }

    [DwarfMapper]
    public partial class AbeNoneMapper
    {
        [MapIgnore(nameof(AbeCellDto.Dropped))]
        public partial AbeCellDto MapCell(AbeCell cell);

        public partial AbeGridDto Map(AbeGrid grid);

        public partial AbeCellDto[] MapArray(AbeCell[] cells);
    }

    public class ArrayBlitElementConfigTests
    {
        private static readonly AbeCell[] Cells =
        [
            new()
            {
                Kept = 1,
                Dropped = 10
            },
            new()
            {
                Kept = 2,
                Dropped = 20
            }
        ];

        private static void AssertElementConfigHonoured(AbeCellDto[] mapped)
        {
            Assert.Equal([1, 2], mapped.Select(c => c.Kept));
            // The element method ignores this member: a block copy would have carried 10 and 20 across.
            Assert.Equal([0, 0], mapped.Select(c => c.Dropped));
        }

        [Fact]
        public void An_ignored_element_member_stays_ignored_in_an_array_map_without_reference_handling()
        {
            var mapper = new AbeNoneMapper();
            AssertElementConfigHonoured(mapper.MapArray(Cells));
            AssertElementConfigHonoured(mapper.Map(new AbeGrid
            {
                Cells = Cells
            }).Cells);
        }
    }
}
