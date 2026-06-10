// SPDX-License-Identifier: GPL-2.0-only
using System;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

// ── Domain types ──────────────────────────────────────────────────────────────

public class StringSrc   { public string V { get; set; } = ""; }
public class IntDst2     { public int    V { get; set; } }
public class GuidDst     { public Guid   V { get; set; } }
public class BoolDst     { public bool   V { get; set; } }

[DwarfMapper] public partial class StringToIntMapper2  { public partial IntDst2 Map(StringSrc s); }
[DwarfMapper] public partial class StringToGuidMapper  { public partial GuidDst  Map(StringSrc s); }
[DwarfMapper] public partial class StringToBoolMapper  { public partial BoolDst  Map(StringSrc s); }

public class IntSrc3     { public int    V { get; set; } }
public class GuidSrc     { public Guid   V { get; set; } }
public class StringDst2  { public string V { get; set; } = ""; }

[DwarfMapper] public partial class IntToStringMapper   { public partial StringDst2 Map(IntSrc3 s); }
[DwarfMapper] public partial class GuidToStringMapper  { public partial StringDst2 Map(GuidSrc  s); }

// ── Tests ─────────────────────────────────────────────────────────────────────

public class ParsableConversionRuntimeTests
{
    // ── string → int ──────────────────────────────────────────────────────────

    [Fact]
    public void String_to_int_known_value_maps_correctly()
    {
        var result = new StringToIntMapper2().Map(new StringSrc { V = "42" });
        Assert.Equal(42, result.V);
    }

    [Fact]
    public void String_to_int_bad_input_throws_FormatException()
    {
        Assert.Throws<FormatException>(() => new StringToIntMapper2().Map(new StringSrc { V = "x" }));
    }

    [Fact]
    public void String_to_int_overflow_throws_OverflowException()
    {
        Assert.Throws<OverflowException>(() =>
            new StringToIntMapper2().Map(new StringSrc { V = "99999999999999" }));
    }

    // ── string → Guid ─────────────────────────────────────────────────────────

    [Fact]
    public void String_to_Guid_round_trips()
    {
        var g = new Guid("12345678-1234-5678-1234-567812345678");
        var result = new StringToGuidMapper().Map(new StringSrc { V = g.ToString() });
        Assert.Equal(g, result.V);
    }

    [Fact]
    public void String_to_Guid_bad_input_throws()
    {
        Assert.ThrowsAny<Exception>(() => new StringToGuidMapper().Map(new StringSrc { V = "not-a-guid" }));
    }

    // ── string → bool ─────────────────────────────────────────────────────────

    [Fact]
    public void String_to_bool_true_maps_correctly()
    {
        Assert.True(new StringToBoolMapper().Map(new StringSrc { V = "True" }).V);
    }

    [Fact]
    public void String_to_bool_false_maps_correctly()
    {
        Assert.False(new StringToBoolMapper().Map(new StringSrc { V = "False" }).V);
    }

    // ── int → string ──────────────────────────────────────────────────────────

    [Fact]
    public void Int_to_string_round_trips()
    {
        var result = new IntToStringMapper().Map(new IntSrc3 { V = 42 });
        Assert.Equal("42", result.V);
    }

    [Fact]
    public void Int_to_string_negative_maps_correctly()
    {
        var result = new IntToStringMapper().Map(new IntSrc3 { V = -100 });
        Assert.Equal("-100", result.V);
    }

    // ── Guid → string ─────────────────────────────────────────────────────────

    [Fact]
    public void Guid_to_string_round_trips()
    {
        var g = new Guid("12345678-1234-5678-1234-567812345678");
        var result = new GuidToStringMapper().Map(new GuidSrc { V = g });
        // The round-trip: parse the result back and compare
        Assert.Equal(g, Guid.Parse(result.V));
    }

    // ── Explicit Use= precedence ──────────────────────────────────────────────

    [Fact]
    public void Explicit_converter_still_wins_over_automatic_Parse()
    {
        // MoneyMapper uses explicit Use= — verify it still works after ParsableConverter is wired in.
        var t = new MoneyMapper().Map(new MoneySource { Amount = "42" });
        Assert.Equal(42, t.Amount);
    }
}
