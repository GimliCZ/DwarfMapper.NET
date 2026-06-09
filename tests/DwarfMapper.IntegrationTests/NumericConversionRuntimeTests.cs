// SPDX-License-Identifier: GPL-2.0-only
using System;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

// ── Domain types for numeric narrowing tests ──────────────────────────────────

public class LongSrc  { public long  V { get; set; } }
public class IntDst   { public int   V { get; set; } }

[DwarfMapper]
public partial class LongToIntMapper { public partial IntDst Map(LongSrc s); }

public class IntSrc   { public int  V { get; set; } }
public class UIntDst  { public uint V { get; set; } }

[DwarfMapper]
public partial class IntToUIntMapper { public partial UIntDst Map(IntSrc s); }

public class IntSrc2   { public int   V { get; set; } }
public class ShortDst  { public short V { get; set; } }

[DwarfMapper]
public partial class IntToShortMapper { public partial ShortDst Map(IntSrc2 s); }

public class ULongSrc  { public ulong V { get; set; } }
public class LongDst   { public long  V { get; set; } }

[DwarfMapper]
public partial class ULongToLongMapper { public partial LongDst Map(ULongSrc s); }

// Nullable composition: int? → short
public class NullableIntSrc { public int?  V { get; set; } }
public class ShortDst2      { public short V { get; set; } }

[DwarfMapper]
public partial class NullableIntToShortMapper { public partial ShortDst2 Map(NullableIntSrc s); }

// ── Tests ─────────────────────────────────────────────────────────────────────

public class NumericConversionRuntimeTests
{
    // ── long → int ────────────────────────────────────────────────────────────

    [Fact]
    public void Long_to_int_in_range_preserves_value()
    {
        var result = new LongToIntMapper().Map(new LongSrc { V = 42L });
        Assert.Equal(42, result.V);
    }

    [Fact]
    public void Long_to_int_max_in_range_preserves_value()
    {
        var result = new LongToIntMapper().Map(new LongSrc { V = int.MaxValue });
        Assert.Equal(int.MaxValue, result.V);
    }

    [Fact]
    public void Long_to_int_out_of_range_throws_OverflowException()
    {
        var mapper = new LongToIntMapper();
        Assert.Throws<OverflowException>(() => mapper.Map(new LongSrc { V = (long)int.MaxValue + 1L }));
    }

    [Fact]
    public void Long_to_int_negative_out_of_range_throws_OverflowException()
    {
        var mapper = new LongToIntMapper();
        Assert.Throws<OverflowException>(() => mapper.Map(new LongSrc { V = (long)int.MinValue - 1L }));
    }

    // ── int → uint ────────────────────────────────────────────────────────────

    [Fact]
    public void Int_to_uint_in_range_preserves_value()
    {
        var result = new IntToUIntMapper().Map(new IntSrc { V = 100 });
        Assert.Equal(100u, result.V);
    }

    [Fact]
    public void Int_to_uint_negative_throws_OverflowException()
    {
        var mapper = new IntToUIntMapper();
        Assert.Throws<OverflowException>(() => mapper.Map(new IntSrc { V = -1 }));
    }

    // ── int → short ───────────────────────────────────────────────────────────

    [Fact]
    public void Int_to_short_in_range_preserves_value()
    {
        var result = new IntToShortMapper().Map(new IntSrc2 { V = 1000 });
        Assert.Equal((short)1000, result.V);
    }

    [Fact]
    public void Int_to_short_out_of_range_throws_OverflowException()
    {
        var mapper = new IntToShortMapper();
        Assert.Throws<OverflowException>(() => mapper.Map(new IntSrc2 { V = short.MaxValue + 1 }));
    }

    // ── ulong → long ──────────────────────────────────────────────────────────

    [Fact]
    public void Ulong_to_long_in_range_preserves_value()
    {
        var result = new ULongToLongMapper().Map(new ULongSrc { V = 9876543210UL });
        Assert.Equal(9876543210L, result.V);
    }

    [Fact]
    public void Ulong_to_long_out_of_range_throws_OverflowException()
    {
        var mapper = new ULongToLongMapper();
        Assert.Throws<OverflowException>(() => mapper.Map(new ULongSrc { V = (ulong)long.MaxValue + 1UL }));
    }

    // ── Nullable composition: int? → short ───────────────────────────────────

    [Fact]
    public void Nullable_int_to_short_in_range_preserves_value()
    {
        var result = new NullableIntToShortMapper().Map(new NullableIntSrc { V = 42 });
        Assert.Equal((short)42, result.V);
    }

    [Fact]
    public void Nullable_int_to_short_null_throws_by_default()
    {
        var mapper = new NullableIntToShortMapper();
        Assert.Throws<InvalidOperationException>(() => mapper.Map(new NullableIntSrc { V = null }));
    }

    [Fact]
    public void Nullable_int_to_short_out_of_range_throws_OverflowException()
    {
        var mapper = new NullableIntToShortMapper();
        Assert.Throws<OverflowException>(() => mapper.Map(new NullableIntSrc { V = short.MaxValue + 1 }));
    }
}
