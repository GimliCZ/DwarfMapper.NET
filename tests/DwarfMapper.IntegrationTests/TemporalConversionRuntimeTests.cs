// SPDX-License-Identifier: GPL-2.0-only
using System;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

// ── Domain types ──────────────────────────────────────────────────────────────

// DateTime ↔ string
public class DateTimeSrc      { public DateTime       V { get; set; } }
public class DateTimeOffsetSrc { public DateTimeOffset V { get; set; } }
public class DateTimeDstT     { public DateTime        V { get; set; } }
public class DateTimeOffsetDstT { public DateTimeOffset V { get; set; } }
public class StringDstTemporal { public string         V { get; set; } = ""; }
public class StringSrcTemporal { public string         V { get; set; } = ""; }
public class TimeSpanSrc      { public TimeSpan        V { get; set; } }
public class TimeSpanDstT     { public TimeSpan        V { get; set; } }

[DwarfMapper] public partial class DateTimeToStringMapper     { public partial StringDstTemporal Map(DateTimeSrc      s); }
[DwarfMapper] public partial class DateTimeOffsetToStringMapper { public partial StringDstTemporal Map(DateTimeOffsetSrc s); }
[DwarfMapper] public partial class StringToDateTimeMapperT    { public partial DateTimeDstT      Map(StringSrcTemporal s); }
[DwarfMapper] public partial class StringToDateTimeOffsetMapper { public partial DateTimeOffsetDstT Map(StringSrcTemporal s); }
[DwarfMapper] public partial class TimeSpanToStringMapper     { public partial StringDstTemporal Map(TimeSpanSrc       s); }
[DwarfMapper] public partial class StringToTimeSpanMapperT    { public partial TimeSpanDstT      Map(StringSrcTemporal s); }

// ── Tests ─────────────────────────────────────────────────────────────────────

public class TemporalConversionRuntimeTests
{
    // ── DateTime → string → DateTime round-trip (precision + Kind) ───────────

    [Fact]
    public void DateTime_to_string_to_DateTime_round_trips_utc_subsecond()
    {
        // This test was FAILING before the fix: ToString(null, Invariant) used "G" format
        // which drops sub-second precision and does not encode DateTimeKind.
        var original = new DateTime(2025, 6, 9, 12, 0, 0, 500, DateTimeKind.Utc).AddTicks(1234);
        var stringDto = new DateTimeToStringMapper().Map(new DateTimeSrc { V = original });
        var restoredDto = new StringToDateTimeMapperT().Map(new StringSrcTemporal { V = stringDto.V });

        // Full round-trip: ticks must match exactly, Kind must be preserved.
        Assert.Equal(original, restoredDto.V);
        Assert.Equal(original.Kind, restoredDto.V.Kind);
        Assert.Equal(original.Ticks, restoredDto.V.Ticks);
    }

    [Fact]
    public void DateTime_to_string_utc_produces_Z_suffix()
    {
        var dt = new DateTime(2025, 6, 9, 12, 0, 0, DateTimeKind.Utc);
        var result = new DateTimeToStringMapper().Map(new DateTimeSrc { V = dt });
        // "o" format for UTC must end with "Z"
        Assert.EndsWith("Z", result.V, StringComparison.Ordinal);
    }

    [Fact]
    public void DateTime_to_string_to_DateTime_round_trips_unspecified_kind()
    {
        var original = new DateTime(2025, 1, 15, 8, 30, 0, 999, DateTimeKind.Unspecified).AddTicks(9999);
        var stringDto = new DateTimeToStringMapper().Map(new DateTimeSrc { V = original });
        var restoredDto = new StringToDateTimeMapperT().Map(new StringSrcTemporal { V = stringDto.V });

        Assert.Equal(original, restoredDto.V);
        Assert.Equal(DateTimeKind.Unspecified, restoredDto.V.Kind);
    }

    [Fact]
    public void String_to_DateTime_bad_input_throws()
        => Assert.ThrowsAny<Exception>(() =>
            new StringToDateTimeMapperT().Map(new StringSrcTemporal { V = "not-a-date" }));

    // ── DateTimeOffset round-trip ─────────────────────────────────────────────

    [Fact]
    public void DateTimeOffset_to_string_to_DateTimeOffset_round_trips()
    {
        var original = new DateTimeOffset(2025, 6, 9, 14, 0, 0, 500, TimeSpan.FromHours(2)).AddTicks(5678);
        var stringDto = new DateTimeOffsetToStringMapper().Map(new DateTimeOffsetSrc { V = original });
        var restoredDto = new StringToDateTimeOffsetMapper().Map(new StringSrcTemporal { V = stringDto.V });

        Assert.Equal(original, restoredDto.V);
        Assert.Equal(original.Offset, restoredDto.V.Offset);
        Assert.Equal(original.Ticks, restoredDto.V.Ticks);
    }

    [Fact]
    public void DateTimeOffset_utc_produces_plus_zero_offset()
    {
        var dto = DateTimeOffset.UtcNow;
        var result = new DateTimeOffsetToStringMapper().Map(new DateTimeOffsetSrc { V = dto });
        // "o" format for UTC DateTimeOffset ends with "+00:00"
        Assert.Contains("+00:00", result.V, StringComparison.Ordinal);
    }

    // ── TimeSpan round-trip (should already be lossless; test proves no regression) ──

    [Fact]
    public void TimeSpan_to_string_round_trips()
    {
        // TimeSpan.ToString(null, Invariant) → "c" format which is lossless.
        // This test verifies the existing behaviour is preserved.
        var original = TimeSpan.FromSeconds(90).Add(TimeSpan.FromTicks(12345));
        var stringDto = new TimeSpanToStringMapper().Map(new TimeSpanSrc { V = original });
        var restoredDto = new StringToTimeSpanMapperT().Map(new StringSrcTemporal { V = stringDto.V });
        Assert.Equal(original, restoredDto.V);
    }

    [Fact]
    public void TimeSpan_to_string_negative_round_trips()
    {
        var original = TimeSpan.FromSeconds(-45);
        var stringDto = new TimeSpanToStringMapper().Map(new TimeSpanSrc { V = original });
        var restoredDto = new StringToTimeSpanMapperT().Map(new StringSrcTemporal { V = stringDto.V });
        Assert.Equal(original, restoredDto.V);
    }
}
