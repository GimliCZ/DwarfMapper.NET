// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

// ── Mapper types for additional narrowing pairs ───────────────────────────────

// long → short
public class LongSrcD  { public long  V { get; set; } }
public class ShortDstD { public short V { get; set; } }
[DwarfMapper] public partial class LongToShortMapper { public partial ShortDstD Map(LongSrcD s); }

// long → byte
public class ByteDstD  { public byte  V { get; set; } }
[DwarfMapper] public partial class LongToByteMapper  { public partial ByteDstD  Map(LongSrcD s); }

// int → byte
public class IntSrcD   { public int   V { get; set; } }
[DwarfMapper] public partial class IntToByteMapper   { public partial ByteDstD  Map(IntSrcD s); }

// int → sbyte
public class SByteDstD { public sbyte V { get; set; } }
[DwarfMapper] public partial class IntToSByteMapper  { public partial SByteDstD Map(IntSrcD s); }

// uint → int (sign/range)
public class UIntSrcD  { public uint  V { get; set; } }
[DwarfMapper] public partial class UIntToIntMapper   { public partial IntDst    Map(UIntSrcD s); }

// ulong → uint
public class ULongSrcD { public ulong V { get; set; } }
public class UIntDstD  { public uint  V { get; set; } }
[DwarfMapper] public partial class ULongToUIntMapper { public partial UIntDstD  Map(ULongSrcD s); }

// long → ulong (negative → throw)
public class LongSrcD2  { public long  V { get; set; } }
public class ULongDstD  { public ulong V { get; set; } }
[DwarfMapper] public partial class LongToULongMapper { public partial ULongDstD Map(LongSrcD2 s); }

// short → byte
public class ShortSrcD { public short V { get; set; } }
[DwarfMapper] public partial class ShortToByteMapper { public partial ByteDstD  Map(ShortSrcD s); }

// ── Nullable composition ──────────────────────────────────────────────────────

// int? → short (default NullStrategy = Throw)
public class NullableIntSrcD  { public int?  V { get; set; } }
public class ShortDstD2       { public short V { get; set; } }
[DwarfMapper] public partial class NullableIntToShortMapperD { public partial ShortDstD2 Map(NullableIntSrcD s); }

// int? → short with SetDefault
[DwarfMapper(NullStrategy = NullStrategy.SetDefault)]
public partial class NullableIntToShortSetDefaultMapper { public partial ShortDstD2 Map(NullableIntSrcD s); }

// long? → int
public class NullableLongSrcD { public long? V { get; set; } }
[DwarfMapper] public partial class NullableLongToIntMapper   { public partial IntDst  Map(NullableLongSrcD s); }

// ── Collections of narrowing pairs ────────────────────────────────────────────

// List<long> → List<int>
public class ListLongSrc  { public List<long> Items { get; set; } = new(); }
public class ListIntDst   { public List<int>  Items { get; set; } = new(); }
[DwarfMapper] public partial class ListLongToIntMapper { public partial ListIntDst Map(ListLongSrc s); }

// long[] → int[]
public class ArrayLongSrc { public long[] Items { get; set; } = System.Array.Empty<long>(); }
public class ArrayIntDst  { public int[]  Items { get; set; } = System.Array.Empty<int>();  }
[DwarfMapper] public partial class ArrayLongToIntMapper { public partial ArrayIntDst Map(ArrayLongSrc s); }

// Dictionary<string, long> → Dictionary<string, int>
public class DictStrLongSrc { public Dictionary<string, long> D { get; set; } = new(); }
public class DictStrIntDst  { public Dictionary<string, int>  D { get; set; } = new(); }
[DwarfMapper] public partial class DictStrLongToIntMapper { public partial DictStrIntDst Map(DictStrLongSrc s); }

// ── Additional string↔T types ─────────────────────────────────────────────────

// string → decimal
public class StringSrcE   { public string  V { get; set; } = ""; }
public class DecimalDstE  { public decimal V { get; set; } }
[DwarfMapper] public partial class StringToDecimalMapper  { public partial DecimalDstE   Map(StringSrcE s); }

// string → TimeSpan
public class TimeSpanDstE { public TimeSpan V { get; set; } }
[DwarfMapper] public partial class StringToTimeSpanMapper { public partial TimeSpanDstE  Map(StringSrcE s); }

// string → DateTime
public class DateTimeDstE { public DateTime V { get; set; } }
[DwarfMapper] public partial class StringToDateTimeMapper { public partial DateTimeDstE  Map(StringSrcE s); }

// decimal → string
public class DecimalSrcE  { public decimal V { get; set; } }
public class StringDstE   { public string  V { get; set; } = ""; }
[DwarfMapper] public partial class DecimalToStringMapper { public partial StringDstE Map(DecimalSrcE s); }

// ── Explicit Use= precedence over auto-Parse ──────────────────────────────────

public class StringSrcWithUse { public string Amount { get; set; } = ""; }
public class IntDstWithUse    { public int    Amount { get; set; } }

[DwarfMapper]
public partial class UseMethodOverrideMapper
{
    [MapProperty("Amount", "Amount", Use = nameof(DoubleParse))]
    public partial IntDstWithUse Map(StringSrcWithUse s);

    // Custom parser that multiplies by 2 — proves auto-Parse is NOT used.
    private static int DoubleParse(string v)
        => int.Parse(v, CultureInfo.InvariantCulture) * 2;
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class ConversionDefensiveRuntimeTests
{
    // ── long → short ──────────────────────────────────────────────────────────

    [Fact]
    public void Long_to_short_in_range_preserves_value()
        => Assert.Equal((short)32000, new LongToShortMapper().Map(new LongSrcD { V = 32000L }).V);

    [Fact]
    public void Long_to_short_at_max_boundary_preserves_value()
        => Assert.Equal(short.MaxValue, new LongToShortMapper().Map(new LongSrcD { V = short.MaxValue }).V);

    [Fact]
    public void Long_to_short_just_past_max_throws_OverflowException()
        => Assert.Throws<OverflowException>(() => new LongToShortMapper().Map(new LongSrcD { V = (long)short.MaxValue + 1L }));

    [Fact]
    public void Long_to_short_at_min_boundary_preserves_value()
        => Assert.Equal(short.MinValue, new LongToShortMapper().Map(new LongSrcD { V = short.MinValue }).V);

    [Fact]
    public void Long_to_short_just_past_min_throws_OverflowException()
        => Assert.Throws<OverflowException>(() => new LongToShortMapper().Map(new LongSrcD { V = (long)short.MinValue - 1L }));

    // ── long → byte ───────────────────────────────────────────────────────────

    [Fact]
    public void Long_to_byte_in_range_preserves_value()
        => Assert.Equal((byte)200, new LongToByteMapper().Map(new LongSrcD { V = 200L }).V);

    [Fact]
    public void Long_to_byte_at_max_boundary_preserves_value()
        => Assert.Equal(byte.MaxValue, new LongToByteMapper().Map(new LongSrcD { V = byte.MaxValue }).V);

    [Fact]
    public void Long_to_byte_just_past_max_throws_OverflowException()
        => Assert.Throws<OverflowException>(() => new LongToByteMapper().Map(new LongSrcD { V = (long)byte.MaxValue + 1L }));

    [Fact]
    public void Long_to_byte_negative_throws_OverflowException()
        => Assert.Throws<OverflowException>(() => new LongToByteMapper().Map(new LongSrcD { V = -1L }));

    // ── int → byte ────────────────────────────────────────────────────────────

    [Fact]
    public void Int_to_byte_in_range_preserves_value()
        => Assert.Equal((byte)42, new IntToByteMapper().Map(new IntSrcD { V = 42 }).V);

    [Fact]
    public void Int_to_byte_at_max_boundary_preserves_value()
        => Assert.Equal(byte.MaxValue, new IntToByteMapper().Map(new IntSrcD { V = byte.MaxValue }).V);

    [Fact]
    public void Int_to_byte_just_past_max_throws_OverflowException()
        => Assert.Throws<OverflowException>(() => new IntToByteMapper().Map(new IntSrcD { V = byte.MaxValue + 1 }));

    [Fact]
    public void Int_to_byte_negative_throws_OverflowException()
        => Assert.Throws<OverflowException>(() => new IntToByteMapper().Map(new IntSrcD { V = -1 }));

    // ── int → sbyte ───────────────────────────────────────────────────────────

    [Fact]
    public void Int_to_sbyte_in_range_preserves_value()
        => Assert.Equal((sbyte)100, new IntToSByteMapper().Map(new IntSrcD { V = 100 }).V);

    [Fact]
    public void Int_to_sbyte_at_max_boundary_preserves_value()
        => Assert.Equal(sbyte.MaxValue, new IntToSByteMapper().Map(new IntSrcD { V = sbyte.MaxValue }).V);

    [Fact]
    public void Int_to_sbyte_just_past_max_throws_OverflowException()
        => Assert.Throws<OverflowException>(() => new IntToSByteMapper().Map(new IntSrcD { V = sbyte.MaxValue + 1 }));

    [Fact]
    public void Int_to_sbyte_negative_at_min_boundary_preserves_value()
        => Assert.Equal(sbyte.MinValue, new IntToSByteMapper().Map(new IntSrcD { V = sbyte.MinValue }).V);

    [Fact]
    public void Int_to_sbyte_just_past_min_throws_OverflowException()
        => Assert.Throws<OverflowException>(() => new IntToSByteMapper().Map(new IntSrcD { V = sbyte.MinValue - 1 }));

    // ── uint → int (sign/range) ───────────────────────────────────────────────

    [Fact]
    public void UInt_to_int_in_range_preserves_value()
        => Assert.Equal(12345, new UIntToIntMapper().Map(new UIntSrcD { V = 12345u }).V);

    [Fact]
    public void UInt_to_int_at_max_boundary_preserves_value()
        => Assert.Equal(int.MaxValue, new UIntToIntMapper().Map(new UIntSrcD { V = (uint)int.MaxValue }).V);

    [Fact]
    public void UInt_to_int_just_past_max_throws_OverflowException()
        => Assert.Throws<OverflowException>(() => new UIntToIntMapper().Map(new UIntSrcD { V = (uint)int.MaxValue + 1u }));

    // ── long → ulong (negative → throw) ──────────────────────────────────────

    [Fact]
    public void Long_to_ulong_non_negative_in_range_preserves_value()
        => Assert.Equal(9876543210UL, new LongToULongMapper().Map(new LongSrcD2 { V = 9876543210L }).V);

    [Fact]
    public void Long_to_ulong_zero_preserved()
        => Assert.Equal(0UL, new LongToULongMapper().Map(new LongSrcD2 { V = 0L }).V);

    [Fact]
    public void Long_to_ulong_negative_throws_OverflowException()
    {
        // KEY signed-corruption guard: -1L must NOT wrap to UInt64.MaxValue.
        Assert.Throws<OverflowException>(() => new LongToULongMapper().Map(new LongSrcD2 { V = -1L }));
    }

    [Fact]
    public void Long_to_ulong_min_value_throws_OverflowException()
        => Assert.Throws<OverflowException>(() => new LongToULongMapper().Map(new LongSrcD2 { V = long.MinValue }));

    // ── int → uint (negative → throw) ────────────────────────────────────────

    [Fact]
    public void Int_to_uint_negative_one_does_NOT_wrap_to_UInt32Max()
    {
        // This is the KEY regression guard: -1 must throw, not silently produce UInt32.MaxValue.
        Assert.Throws<OverflowException>(() => new IntToUIntMapper().Map(new IntSrc { V = -1 }));
    }

    // ── ulong → uint ──────────────────────────────────────────────────────────

    [Fact]
    public void Ulong_to_uint_in_range_preserves_value()
        => Assert.Equal(100u, new ULongToUIntMapper().Map(new ULongSrcD { V = 100UL }).V);

    [Fact]
    public void Ulong_to_uint_at_max_boundary_preserves_value()
        => Assert.Equal(uint.MaxValue, new ULongToUIntMapper().Map(new ULongSrcD { V = uint.MaxValue }).V);

    [Fact]
    public void Ulong_to_uint_just_past_max_throws_OverflowException()
        => Assert.Throws<OverflowException>(() => new ULongToUIntMapper().Map(new ULongSrcD { V = (ulong)uint.MaxValue + 1UL }));

    // ── short → byte ──────────────────────────────────────────────────────────

    [Fact]
    public void Short_to_byte_in_range_preserves_value()
        => Assert.Equal((byte)100, new ShortToByteMapper().Map(new ShortSrcD { V = 100 }).V);

    [Fact]
    public void Short_to_byte_at_max_boundary_preserves_value()
        => Assert.Equal(byte.MaxValue, new ShortToByteMapper().Map(new ShortSrcD { V = byte.MaxValue }).V);

    [Fact]
    public void Short_to_byte_just_past_max_throws_OverflowException()
        => Assert.Throws<OverflowException>(() => new ShortToByteMapper().Map(new ShortSrcD { V = (short)(byte.MaxValue + 1) }));

    [Fact]
    public void Short_to_byte_negative_throws_OverflowException()
        => Assert.Throws<OverflowException>(() => new ShortToByteMapper().Map(new ShortSrcD { V = -1 }));

    // ── Nullable composition ──────────────────────────────────────────────────

    [Fact]
    public void Nullable_int_to_short_in_range_preserves_value()
        => Assert.Equal((short)100, new NullableIntToShortMapperD().Map(new NullableIntSrcD { V = 100 }).V);

    [Fact]
    public void Nullable_int_to_short_out_of_range_throws_OverflowException()
        => Assert.Throws<OverflowException>(() => new NullableIntToShortMapperD().Map(new NullableIntSrcD { V = short.MaxValue + 1 }));

    [Fact]
    public void Nullable_int_to_short_null_with_throw_strategy_throws_InvalidOperationException()
        => Assert.Throws<InvalidOperationException>(() => new NullableIntToShortMapperD().Map(new NullableIntSrcD { V = null }));

    [Fact]
    public void Nullable_int_to_short_null_with_set_default_gives_zero()
        => Assert.Equal((short)0, new NullableIntToShortSetDefaultMapper().Map(new NullableIntSrcD { V = null }).V);

    [Fact]
    public void Nullable_long_to_int_in_range_preserves_value()
        => Assert.Equal(42, new NullableLongToIntMapper().Map(new NullableLongSrcD { V = 42L }).V);

    [Fact]
    public void Nullable_long_to_int_out_of_range_throws_OverflowException()
        => Assert.Throws<OverflowException>(() => new NullableLongToIntMapper().Map(new NullableLongSrcD { V = (long)int.MaxValue + 1L }));

    // ── Collections of narrowing pairs ────────────────────────────────────────

    [Fact]
    public void List_long_to_list_int_all_in_range_maps_correctly()
    {
        var result = new ListLongToIntMapper().Map(new ListLongSrc { Items = new List<long> { 1L, 42L, -10L } });
        Assert.Equal(new[] { 1, 42, -10 }, result.Items);
    }

    [Fact]
    public void List_long_to_list_int_out_of_range_element_throws_OverflowException()
        => Assert.Throws<OverflowException>(() =>
            new ListLongToIntMapper().Map(new ListLongSrc { Items = new List<long> { 1L, (long)int.MaxValue + 1L } }));

    [Fact]
    public void Array_long_to_array_int_all_in_range_maps_correctly()
    {
        var result = new ArrayLongToIntMapper().Map(new ArrayLongSrc { Items = new long[] { 5L, -3L, 100L } });
        Assert.Equal(new[] { 5, -3, 100 }, result.Items);
    }

    [Fact]
    public void Array_long_to_array_int_out_of_range_element_throws_OverflowException()
        => Assert.Throws<OverflowException>(() =>
            new ArrayLongToIntMapper().Map(new ArrayLongSrc { Items = new long[] { (long)int.MaxValue + 1L } }));

    [Fact]
    public void Dictionary_str_long_to_str_int_all_in_range_maps_correctly()
    {
        var result = new DictStrLongToIntMapper().Map(new DictStrLongSrc
            { D = new Dictionary<string, long> { ["a"] = 1L, ["b"] = -5L } });
        Assert.Equal(1, result.D["a"]);
        Assert.Equal(-5, result.D["b"]);
    }

    [Fact]
    public void Dictionary_str_long_to_str_int_out_of_range_throws_OverflowException()
        => Assert.Throws<OverflowException>(() =>
            new DictStrLongToIntMapper().Map(new DictStrLongSrc
                { D = new Dictionary<string, long> { ["x"] = (long)int.MaxValue + 1L } }));

    // ── string→T: parse failures are LOUD ────────────────────────────────────

    [Fact]
    public void String_to_int_empty_throws()
        => Assert.ThrowsAny<Exception>(() => new StringToIntMapper2().Map(new StringSrc { V = "" }));

    [Fact]
    public void String_to_int_whitespace_throws()
        => Assert.ThrowsAny<Exception>(() => new StringToIntMapper2().Map(new StringSrc { V = "   " }));

    [Fact]
    public void String_to_int_not_a_number_throws_FormatException()
        => Assert.Throws<FormatException>(() => new StringToIntMapper2().Map(new StringSrc { V = "not-a-number" }));

    [Fact]
    public void String_to_int_huge_number_throws_OverflowException()
        => Assert.Throws<OverflowException>(() =>
            new StringToIntMapper2().Map(new StringSrc { V = "99999999999999999999" }));

    [Fact]
    public void String_to_bool_True_maps()
        => Assert.True(new StringToBoolMapper().Map(new StringSrc { V = "True" }).V);

    [Fact]
    public void String_to_bool_False_maps()
        => Assert.False(new StringToBoolMapper().Map(new StringSrc { V = "False" }).V);

    [Fact]
    public void String_to_bool_bad_input_throws()
        => Assert.ThrowsAny<Exception>(() => new StringToBoolMapper().Map(new StringSrc { V = "yes" }));

    [Fact]
    public void String_to_Guid_valid_round_trips()
    {
        var g = Guid.Parse("12345678-1234-5678-1234-567812345678");
        Assert.Equal(g, new StringToGuidMapper().Map(new StringSrc { V = g.ToString() }).V);
    }

    [Fact]
    public void String_to_Guid_malformed_throws()
        => Assert.ThrowsAny<Exception>(() => new StringToGuidMapper().Map(new StringSrc { V = "not-a-guid" }));

    [Fact]
    public void String_to_decimal_round_trips()
    {
        var result = new StringToDecimalMapper().Map(new StringSrcE { V = "123.456" });
        Assert.Equal(123.456m, result.V);
    }

    [Fact]
    public void String_to_decimal_bad_input_throws()
        => Assert.ThrowsAny<Exception>(() => new StringToDecimalMapper().Map(new StringSrcE { V = "abc" }));

    [Fact]
    public void String_to_TimeSpan_known_value_round_trips()
    {
        var ts = TimeSpan.FromSeconds(90);
        var result = new StringToTimeSpanMapper().Map(new StringSrcE { V = ts.ToString() });
        Assert.Equal(ts, result.V);
    }

    [Fact]
    public void String_to_TimeSpan_bad_input_throws()
        => Assert.ThrowsAny<Exception>(() => new StringToTimeSpanMapper().Map(new StringSrcE { V = "not-a-timespan" }));

    [Fact]
    public void String_to_DateTime_ISO_round_trips()
    {
        // Use an unambiguous ISO 8601 round-trip value that InvariantCulture parses reliably.
        var dt = new DateTime(2025, 6, 9, 12, 0, 0, DateTimeKind.Unspecified);
        var s = dt.ToString("o");   // "2025-06-09T12:00:00.0000000"
        var result = new StringToDateTimeMapper().Map(new StringSrcE { V = s });
        Assert.Equal(dt, result.V);
    }

    [Fact]
    public void String_to_DateTime_bad_input_throws()
        => Assert.ThrowsAny<Exception>(() => new StringToDateTimeMapper().Map(new StringSrcE { V = "not-a-date" }));

    // ── T→string round-trips ─────────────────────────────────────────────────

    [Fact]
    public void Int_to_string_then_parse_back_round_trips()
    {
        var dto = new IntToStringMapper().Map(new IntSrc3 { V = -99999 });
        Assert.Equal(-99999, int.Parse(dto.V, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Decimal_to_string_round_trips_via_invariant_culture()
    {
        // 123.456m → string → decimal (InvariantCulture uses '.' as decimal separator).
        var dto = new DecimalToStringMapper().Map(new DecimalSrcE { V = 123.456m });
        Assert.Contains('.', dto.V); // decimal separator must be '.' not ','
        Assert.Equal(123.456m, decimal.Parse(dto.V, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Guid_to_string_then_parse_back_round_trips()
    {
        var g = new Guid("12345678-1234-5678-1234-567812345678");
        var dto = new GuidToStringMapper().Map(new GuidSrc { V = g });
        Assert.Equal(g, Guid.Parse(dto.V));
    }

    // ── Culture independence ──────────────────────────────────────────────────

    [Fact]
    public void Decimal_string_conversion_under_de_DE_culture_uses_invariant()
    {
        // Sets CurrentCulture to German (comma as decimal separator) temporarily.
        // The emitted code uses InvariantCulture, so "123.456" must still parse correctly.
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            // decimal → string: must produce "123.456" (InvariantCulture dot), NOT "123,456"
            var strDto = new DecimalToStringMapper().Map(new DecimalSrcE { V = 123.456m });
            Assert.Contains('.', strDto.V);  // dot, not comma
            Assert.DoesNotContain(',', strDto.V);

            // string → decimal: "123.456" must parse correctly even under de-DE
            var decDto = new StringToDecimalMapper().Map(new StringSrcE { V = "123.456" });
            Assert.Equal(123.456m, decDto.V);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Int_string_conversion_under_non_invariant_culture_is_stable()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var strDto = new IntToStringMapper().Map(new IntSrc3 { V = 12345 });
            Assert.Equal("12345", strDto.V); // invariant format, no locale grouping
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // ── Precedence / interaction holes ────────────────────────────────────────

    [Fact]
    public void Explicit_Use_method_wins_over_automatic_Parse_doubles_value()
    {
        // UseMethodOverrideMapper has Use=DoubleParse which multiplies by 2.
        // If auto-Parse were used instead, the result would be 42, not 84.
        var result = new UseMethodOverrideMapper().Map(new StringSrcWithUse { Amount = "42" });
        Assert.Equal(84, result.Amount); // DoubleParse returns int.Parse(v) * 2
    }

    [Fact]
    public void Identity_int_to_int_in_BasicsMapper_still_works()
    {
        // Regression: identity mapping must not be disrupted by numeric/parsable converters.
        var src = new Basics
        {
            I = 99, Str = "", G = Guid.Empty,
            Dt = DateTime.MinValue, Dto = DateTimeOffset.MinValue, Ts = TimeSpan.Zero
        };
        var dto = new BasicsMapper().Map(src);
        Assert.Equal(99, dto.I);
    }
}
