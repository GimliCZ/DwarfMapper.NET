// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // The directives written on a declared partial apply wherever its pair is mapped (round 35 T2).
    //
    // Under ReferenceHandling.Preserve and OnCycle.SetNull a NESTED use of a declared pair - a member, the elements of
    // a list or an array - cannot call the public partial: it takes no tracking context, so a generated helper stands
    // in. That helper used to be built from the class's pair-scoped directives alone, and the ones written on the
    // method were dropped without a word: an [MapIgnore]d member was mapped, a rename and a [MapValue] were not
    // applied. [MapIgnore] is how a field is kept out of a transfer object, so the first of those is not cosmetic.
    // One fact per mode, each through all three nested routes, against the directly called method as the reference.
    public class NdpItem
    {
        public int Kept { get; set; }

        public int Dropped { get; set; }

        public int Old { get; set; }
    }

    public class NdpItemDto
    {
        public int Kept { get; set; }

        public int Dropped { get; set; }

        public int New { get; set; }

        public int Fixed { get; set; }
    }

    public class NdpRoot
    {
        public NdpItem Child { get; set; } = new();

        public List<NdpItem> Kids { get; set; } = new();

        public NdpItem[] Row { get; set; } = [];
    }

    public class NdpRootDto
    {
        public NdpItemDto Child { get; set; } = new();

        public List<NdpItemDto> Kids { get; set; } = new();

        public NdpItemDto[] Row { get; set; } = [];
    }

    [DwarfMapper]
    public partial class NdpNoneMapper
    {
        [MapIgnore(nameof(NdpItemDto.Dropped))]
        [MapProperty(nameof(NdpItem.Old), nameof(NdpItemDto.New))]
        [MapValue(nameof(NdpItemDto.Fixed), 7)]
        public partial NdpItemDto MapItem(NdpItem item);

        public partial NdpRootDto Map(NdpRoot root);
    }

    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class NdpPreserveMapper
    {
        [MapIgnore(nameof(NdpItemDto.Dropped))]
        [MapProperty(nameof(NdpItem.Old), nameof(NdpItemDto.New))]
        [MapValue(nameof(NdpItemDto.Fixed), 7)]
        public partial NdpItemDto MapItem(NdpItem item);

        public partial NdpRootDto Map(NdpRoot root);
    }

    [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
    public partial class NdpSetNullMapper
    {
        [MapIgnore(nameof(NdpItemDto.Dropped))]
        [MapProperty(nameof(NdpItem.Old), nameof(NdpItemDto.New))]
        [MapValue(nameof(NdpItemDto.Fixed), 7)]
        public partial NdpItemDto MapItem(NdpItem item);

        public partial NdpRootDto Map(NdpRoot root);
    }

    public class NestedDeclaredPairDirectivesTests
    {
        private static NdpItem Item(int seed)
        {
            return new NdpItem
            {
                Kept = seed,
                Dropped = seed + 100,
                Old = seed + 200
            };
        }

        private static NdpRoot Root()
        {
            return new NdpRoot
            {
                Child = Item(1),
                Kids =
                [
                    Item(2)
                ],
                Row =
                [
                    Item(3)
                ]
            };
        }

        private static void AssertAsTheMethodMapsIt(NdpItemDto direct, NdpRootDto nested)
        {
            // The reference: the declared method, called directly.
            Assert.Equal((1, 0, 201, 7), (direct.Kept, direct.Dropped, direct.New, direct.Fixed));

            Assert.Equal((1, 0, 201, 7), (nested.Child.Kept, nested.Child.Dropped, nested.Child.New, nested.Child.Fixed));
            var kid = Assert.Single(nested.Kids);
            Assert.Equal((2, 0, 202, 7), (kid.Kept, kid.Dropped, kid.New, kid.Fixed));
            var cell = Assert.Single(nested.Row);
            Assert.Equal((3, 0, 203, 7), (cell.Kept, cell.Dropped, cell.New, cell.Fixed));
        }

        [Fact]
        public void Without_reference_handling_a_nested_pair_maps_as_its_declared_method_does()
        {
            var mapper = new NdpNoneMapper();
            AssertAsTheMethodMapsIt(mapper.MapItem(Item(1)), mapper.Map(Root()));
        }

        [Fact]
        public void Under_Preserve_a_nested_pair_maps_as_its_declared_method_does()
        {
            var mapper = new NdpPreserveMapper();
            AssertAsTheMethodMapsIt(mapper.MapItem(Item(1)), mapper.Map(Root()));
        }

        [Fact]
        public void Under_SetNull_a_nested_pair_maps_as_its_declared_method_does()
        {
            var mapper = new NdpSetNullMapper();
            AssertAsTheMethodMapsIt(mapper.MapItem(Item(1)), mapper.Map(Root()));
        }
    }
}
