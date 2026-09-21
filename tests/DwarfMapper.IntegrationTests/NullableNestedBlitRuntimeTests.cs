// SPDX-License-Identifier: GPL-2.0-only

// fixture names carry the Nnb (nullable-nested-blit) scenario prefix
// ReSharper disable InconsistentNaming
namespace DwarfMapper.IntegrationTests
{
    public struct NnbAddr
    {
        public int Street, City, Zip, Country;
    }

    public struct NnbAddrDto
    {
        public int Street, City, Zip, Country;
    }

    public struct NnbOrder
    {
        public long Id;
        public NnbAddr? Ship;
        public long Amount;
    }

    public struct NnbOrderDto
    {
        public long Id;
        public NnbAddrDto? Ship;
        public long Amount;
    }

    public class NnbSrc
    {
        public NnbOrder[] Items { get; set; } = Array.Empty<NnbOrder>();
    }

    public class NnbDst
    {
        public NnbOrderDto[] Items { get; set; } = Array.Empty<NnbOrderDto>();
    }

    [DwarfMapper]
    public partial class NnbMapper
    {
        public partial NnbDst Map(NnbSrc s);
    }

    public class NullableNestedBlitRuntimeTests
    {
        // Round 29, T0.1: Nullable<T> pairs no longer stop the root array blit — the middle element's null
        // `Ship` must survive as null through the block copy, and a present `Ship` must read back its value.
        [Fact]
        public void Root_array_blits_with_a_null_middle_element_and_the_values_survive()
        {
            var src = new NnbSrc
            {
                Items = new[]
                {
                    new NnbOrder { Id = 1, Ship = new NnbAddr { Street = 10, City = 20, Zip = 30, Country = 40 }, Amount = 100 },
                    new NnbOrder { Id = 2, Ship = null, Amount = 200 },
                    new NnbOrder { Id = 3, Ship = new NnbAddr { Street = 11, City = 21, Zip = 31, Country = 41 }, Amount = 300 }
                }
            };

            var dst = new NnbMapper().Map(src);

            Assert.Equal(3, dst.Items.Length);
            Assert.True(dst.Items[0].Ship.HasValue);
            Assert.Equal(30, dst.Items[0].Ship!.Value.Zip);
            Assert.Null(dst.Items[1].Ship);
            Assert.True(dst.Items[2].Ship.HasValue);
            Assert.Equal(31, dst.Items[2].Ship!.Value.Zip);
            Assert.Equal(src.Items.Select(i => i.Id), dst.Items.Select(i => i.Id));
            Assert.Equal(src.Items.Select(i => i.Amount), dst.Items.Select(i => i.Amount));
        }
    }
}
