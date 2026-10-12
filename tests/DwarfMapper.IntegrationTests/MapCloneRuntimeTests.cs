// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // [MapClone] (round 32) states that a same-type map is a deliberate copy, which drops DWARF076 for the method and
    // changes nothing that is generated - so what this file pins is what that copy IS, as MapCloneAttribute documents
    // it: a new instance, the same values, a nested object SHARED (not cloned) and a collection REBUILT into a new one
    // with the same elements. Measured, not assumed: the self-map emits `Leaf = source.Leaf` and routes collections and
    // arrays through their collection helpers.

    public sealed class CloneLeaf
    {
        public int V { get; set; }
    }

    public sealed class CloneSubject
    {
        public string Name { get; set; } = "";
        public CloneLeaf Leaf { get; set; } = new();
        public List<int> Numbers { get; set; } = new();
        public int[] Raw { get; set; } = Array.Empty<int>();
    }

    [DwarfMapper]
    public partial class CloneMapper
    {
        [MapClone]
        public partial CloneSubject Copy(CloneSubject source);
    }

    public class MapCloneRuntimeTests
    {
        private static CloneSubject Source()
        {
            return new CloneSubject
            {
                Name = "Durin",
                Leaf = new CloneLeaf { V = 7 },
                Numbers = [1, 2, 3],
                Raw = [4, 5]
            };
        }

        [Fact]
        public void A_declared_clone_is_a_new_instance_with_the_same_values()
        {
            var source = Source();

            var copy = new CloneMapper().Copy(source);

            Assert.NotSame(source, copy);
            Assert.Equal("Durin", copy.Name);
            Assert.Equal(7, copy.Leaf.V);
            Assert.Equal([1, 2, 3], copy.Numbers);
            Assert.Equal([4, 5], copy.Raw);
        }

        [Fact]
        public void A_nested_object_is_shared_while_a_collection_is_rebuilt()
        {
            var source = Source();

            var copy = new CloneMapper().Copy(source);

            Assert.Same(source.Leaf, copy.Leaf);
            Assert.NotSame(source.Numbers, copy.Numbers);
            Assert.NotSame(source.Raw, copy.Raw);

            source.Numbers.Add(9);
            Assert.Equal([1, 2, 3], copy.Numbers);
        }
    }
}
