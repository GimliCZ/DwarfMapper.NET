// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // CA2225/CA1062: the strong types' conversion operators are written the way a consumer's usually are, with
    // no named alternate (ToInt32) and no null check, because that is the shape the user-conversion path maps.
#pragma warning disable CA2225, CA1062
    // A strong-type with a user-defined IMPLICIT operator (e.g. a UserId strong-type -> int DTO field).
    public sealed class UcId
    {
        private readonly int _v;

        public UcId(int v)
        {
            _v = v;
        }

        public static implicit operator int(UcId id)
        {
            return id._v;
        }
    }

// A value-type with an EXPLICIT operator (potentially lossy → DWARF038 Info, still compiles + runs).
    public readonly struct UcMoney
    {
        private readonly long _cents;

        public UcMoney(long cents)
        {
            _cents = cents;
        }

        public static explicit operator int(UcMoney m)
        {
            return (int)m._cents;
        }
    }

#pragma warning restore CA2225, CA1062

    public sealed class UcSrc
    {
        public required UcId Id { get; init; }

        public UcMoney Price { get; init; }
    }

    public sealed class UcDst
    {
        public required int Id { get; init; }

        public int Price { get; init; }
    }

    [DwarfMapper]
    [GenerateMap<UcSrc, UcDst>]
    public partial class UcMapper
    {
    }

    public sealed class UserConversionRuntimeTests
    {
        [Fact]
        public void Maps_strong_type_to_primitive_via_user_defined_operators()
        {
            var dst = new UcMapper().Map(new UcSrc
            {
                Id = new UcId(42),
                Price = new UcMoney(1995)
            });
            Assert.Equal(42, dst.Id); // implicit operator int(UcId)
            Assert.Equal(1995, dst.Price); // explicit operator int(UcMoney)
        }
    }
}
