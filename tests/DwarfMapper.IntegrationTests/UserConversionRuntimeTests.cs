// SPDX-License-Identifier: GPL-2.0-only
#pragma warning disable CA1815, CA1062, CA2225, CA1305, IDE0079
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// A strong-type with a user-defined IMPLICIT operator (mirrors FusedChat's UserId -> int DTO field).
public sealed class UcId
{
    private readonly int _v;
    public UcId(int v) => _v = v;
    public static implicit operator int(UcId id) => id._v;
}

// A value-type with an EXPLICIT operator (potentially lossy → DWARF038 Info, still compiles + runs).
public readonly struct UcMoney
{
    private readonly long _cents;
    public UcMoney(long cents) => _cents = cents;
    public static explicit operator int(UcMoney m) => (int)m._cents;
}

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
        var dst = new UcMapper().Map(new UcSrc { Id = new UcId(42), Price = new UcMoney(1995) });
        Assert.Equal(42, dst.Id);   // implicit operator int(UcId)
        Assert.Equal(1995, dst.Price); // explicit operator int(UcMoney)
    }
}
