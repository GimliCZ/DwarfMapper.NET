// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    public struct EcbSrc
    {
        public int X;

        public int Y;
    }

    public struct EcbDst
    {
        public int X;

        public int Y;
    }

    public class EcbSource
    {
        public EcbSrc[] Items { get; set; } = [];

        public List<EcbSrc> Bag { get; set; } = [];
    }

    public class EcbTarget
    {
        public EcbDst[] Items { get; set; } = [];

        public List<EcbDst> Bag { get; set; } = [];
    }

    /// <summary>
    ///     <c>EcbSrc</c> and <c>EcbDst</c> are layout-identical and match by name, so the blittable proof
    ///     accepts the pair and both members WOULD take a block copy. <c>Scale</c> is the mapper's own
    ///     declared conversion for that element pair, which the resolver adopts — so it is what must run.
    /// </summary>
    [DwarfMapper]
    public partial class ElementConverterBeatsBlitMapper
    {
        public static EcbDst Scale(EcbSrc s)
        {
            return new EcbDst { X = s.X * 2, Y = s.Y * 2 };
        }

        public partial EcbTarget Map(EcbSource s);
    }

    /// <summary>
    ///     Round 29 T0.2c runtime oracle: a user-declared element converter beats the array/list blit.
    ///     <para>
    ///         The generator-side tests assert the emitted shape (no <c>MemoryMarshal.Cast</c>, a call to
    ///         <c>Scale</c>); this asserts the only thing a consumer can observe — the VALUES. Before the gate,
    ///         both members copied bytes and every element came back unscaled, with nothing in the build saying
    ///         the converter had been skipped. Both storages are covered because the array and the list-family
    ///         shapes are decided by two separate branches of the same arm.
    ///     </para>
    /// </summary>
    public class ElementConverterBeatsBlitRuntimeTests
    {
        [Fact]
        public void The_declared_element_converter_runs_for_an_array_member()
        {
            var mapped = Map();

            Assert.Equal(3, mapped.Items.Length);
            Assert.Equal(new[] { 2, 20, -14 }, mapped.Items.Select(i => i.X));
            Assert.Equal(new[] { 4, 40, 84 }, mapped.Items.Select(i => i.Y));
        }

        [Fact]
        public void The_declared_element_converter_runs_for_a_list_member()
        {
            var mapped = Map();

            Assert.Equal(3, mapped.Bag.Count);
            Assert.Equal(new[] { 2, 20, -14 }, mapped.Bag.Select(i => i.X));
            Assert.Equal(new[] { 4, 40, 84 }, mapped.Bag.Select(i => i.Y));
        }

        private static EcbTarget Map()
        {
            EcbSrc[] items =
            [
                new() { X = 1, Y = 2 },
                new() { X = 10, Y = 20 },
                new() { X = -7, Y = 42 },
            ];

            return new ElementConverterBeatsBlitMapper().Map(new EcbSource
            {
                Items = items,
                Bag = [.. items],
            });
        }
    }
}
