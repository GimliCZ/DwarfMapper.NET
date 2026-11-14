// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // An array of layout-identical structs is block-copied - unless the element pair's own partial says otherwise
    // (round 35 T2; the last untested cell of round 33's fast-path audit). The element method here ignores one member,
    // so a block copy would carry it across anyway: the array map has to go through the element method instead.
    //
    // In every mode. Under ReferenceHandling.Preserve and OnCycle.SetNull the pair's declared partial yields to a
    // generated helper, so the gate that asks "does the element resolve to a user conversion?" answered no, and
    // under SetNull the array was block-copied past the ignore (under Preserve the helper mapped the member itself).
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

    [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
    public partial class AbeSetNullMapper
    {
        [MapIgnore(nameof(AbeCellDto.Dropped))]
        public partial AbeCellDto MapCell(AbeCell cell);

        public partial AbeGridDto Map(AbeGrid grid);

        public partial AbeCellDto[] MapArray(AbeCell[] cells);
    }

    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class AbePreserveMapper
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

        [Fact]
        public void An_ignored_element_member_stays_ignored_in_an_array_map_under_SetNull()
        {
            var mapper = new AbeSetNullMapper();
            AssertElementConfigHonoured(mapper.MapArray(Cells));
            AssertElementConfigHonoured(mapper.Map(new AbeGrid
            {
                Cells = Cells
            }).Cells);
        }

        [Fact]
        public void An_ignored_element_member_stays_ignored_in_an_array_map_under_Preserve()
        {
            var mapper = new AbePreserveMapper();
            AssertElementConfigHonoured(mapper.MapArray(Cells));
            AssertElementConfigHonoured(mapper.Map(new AbeGrid
            {
                Cells = Cells
            }).Cells);
        }
    }
}
