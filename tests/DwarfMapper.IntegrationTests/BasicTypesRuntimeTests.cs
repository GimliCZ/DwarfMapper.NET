// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

// ── Test domain types ─────────────────────────────────────────────────────────

public enum BasicColor
{
    Red,
    Green,
    Blue
}

public class Basics
{
    public bool B { get; set; }
    public byte By { get; set; }
    public sbyte Sb { get; set; }
    public short S { get; set; }
    public ushort Us { get; set; }
    public int I { get; set; }
    public uint Ui { get; set; }
    public long L { get; set; }
    public ulong Ul { get; set; }
    public float F { get; set; }
    public double D { get; set; }
    public decimal M { get; set; }
    public char C { get; set; }
    public string Str { get; set; } = "";
    public Guid G { get; set; }
    public DateTime Dt { get; set; }
    public DateTimeOffset Dto { get; set; }
    public TimeSpan Ts { get; set; }

    // Nullable subset
    public int? NI { get; set; }
    public double? ND { get; set; }
    public DateTime? NDt { get; set; }
    public BasicColor? NE { get; set; }
}

public class BasicsDto
{
    public bool B { get; set; }
    public byte By { get; set; }
    public sbyte Sb { get; set; }
    public short S { get; set; }
    public ushort Us { get; set; }
    public int I { get; set; }
    public uint Ui { get; set; }
    public long L { get; set; }
    public ulong Ul { get; set; }
    public float F { get; set; }
    public double D { get; set; }
    public decimal M { get; set; }
    public char C { get; set; }
    public string Str { get; set; } = "";
    public Guid G { get; set; }
    public DateTime Dt { get; set; }
    public DateTimeOffset Dto { get; set; }
    public TimeSpan Ts { get; set; }

    // Nullable subset
    public int? NI { get; set; }
    public double? ND { get; set; }
    public DateTime? NDt { get; set; }
    public BasicColor? NE { get; set; }
}

[DwarfMapper]
public partial class BasicsMapper
{
    public partial BasicsDto Map(Basics src);
}

// ── Test fixture ──────────────────────────────────────────────────────────────

public class BasicTypesRuntimeTests
{
    private static BasicsDto Map(Basics src)
    {
        return new BasicsMapper().Map(src);
    }

    // ── Integers ──────────────────────────────────────────────────────────────

    [Fact]
    public void Integer_boundaries_survive_MaxValue()
    {
        var src = new Basics
        {
            B = true,
            By = byte.MaxValue,
            Sb = sbyte.MaxValue,
            S = short.MaxValue,
            Us = ushort.MaxValue,
            I = int.MaxValue,
            Ui = uint.MaxValue,
            L = long.MaxValue,
            Ul = ulong.MaxValue,
            F = 0f,
            D = 0d,
            M = 0m,
            C = '\0',
            Str = "",
            G = Guid.Empty,
            Dt = DateTime.MinValue,
            Dto = DateTimeOffset.MinValue,
            Ts = TimeSpan.Zero
        };
        var dto = Map(src);
        Assert.True(dto.B);
        Assert.Equal(byte.MaxValue, dto.By);
        Assert.Equal(sbyte.MaxValue, dto.Sb);
        Assert.Equal(short.MaxValue, dto.S);
        Assert.Equal(ushort.MaxValue, dto.Us);
        Assert.Equal(int.MaxValue, dto.I);
        Assert.Equal(uint.MaxValue, dto.Ui);
        Assert.Equal(long.MaxValue, dto.L);
        Assert.Equal(ulong.MaxValue, dto.Ul);
    }

    [Fact]
    public void Integer_boundaries_survive_MinValue()
    {
        var src = new Basics
        {
            B = false,
            By = byte.MinValue,
            Sb = sbyte.MinValue,
            S = short.MinValue,
            Us = ushort.MinValue,
            I = int.MinValue,
            Ui = uint.MinValue,
            L = long.MinValue,
            Ul = ulong.MinValue,
            F = 0f,
            D = 0d,
            M = 0m,
            C = '\0',
            Str = "",
            G = Guid.Empty,
            Dt = DateTime.MinValue,
            Dto = DateTimeOffset.MinValue,
            Ts = TimeSpan.Zero
        };
        var dto = Map(src);
        Assert.False(dto.B);
        Assert.Equal(byte.MinValue, dto.By);
        Assert.Equal(sbyte.MinValue, dto.Sb);
        Assert.Equal(short.MinValue, dto.S);
        Assert.Equal(ushort.MinValue, dto.Us);
        Assert.Equal(int.MinValue, dto.I);
        Assert.Equal(uint.MinValue, dto.Ui);
        Assert.Equal(long.MinValue, dto.L);
        Assert.Equal(ulong.MinValue, dto.Ul);
    }

    [Fact]
    public void Integer_typical_values_survive()
    {
        var src = new Basics
        {
            By = 42,
            Sb = -10,
            S = -1000,
            Us = 1000,
            I = 123456,
            Ui = 123456,
            L = -9876543210L,
            Ul = 9876543210UL,
            F = 0f,
            D = 0d,
            M = 0m,
            Str = "",
            G = Guid.Empty,
            Dt = DateTime.MinValue,
            Dto = DateTimeOffset.MinValue,
            Ts = TimeSpan.Zero
        };
        var dto = Map(src);
        Assert.Equal(42, dto.By);
        Assert.Equal((sbyte)-10, dto.Sb);
        Assert.Equal((short)-1000, dto.S);
        Assert.Equal((ushort)1000, dto.Us);
        Assert.Equal(123456, dto.I);
        Assert.Equal(123456u, dto.Ui);
        Assert.Equal(-9876543210L, dto.L);
        Assert.Equal(9876543210UL, dto.Ul);
    }

    // ── Floating-point specials ───────────────────────────────────────────────

    [Fact]
    public void FloatingPoint_specials_survive()
    {
        // NaN
        var srcNaN = new Basics
        {
            Str = "", G = Guid.Empty, Dt = DateTime.MinValue, Dto = DateTimeOffset.MinValue, Ts = TimeSpan.Zero,
            F = float.NaN, D = double.NaN
        };
        var dtoNaN = Map(srcNaN);
        Assert.True(float.IsNaN(dtoNaN.F), "float NaN should survive");
        Assert.True(double.IsNaN(dtoNaN.D), "double NaN should survive");

        // +Infinity
        var srcPosInf = new Basics
        {
            Str = "", G = Guid.Empty, Dt = DateTime.MinValue, Dto = DateTimeOffset.MinValue, Ts = TimeSpan.Zero,
            F = float.PositiveInfinity, D = double.PositiveInfinity
        };
        var dtoPosInf = Map(srcPosInf);
        Assert.Equal(float.PositiveInfinity, dtoPosInf.F);
        Assert.Equal(double.PositiveInfinity, dtoPosInf.D);

        // -Infinity
        var srcNegInf = new Basics
        {
            Str = "", G = Guid.Empty, Dt = DateTime.MinValue, Dto = DateTimeOffset.MinValue, Ts = TimeSpan.Zero,
            F = float.NegativeInfinity, D = double.NegativeInfinity
        };
        var dtoNegInf = Map(srcNegInf);
        Assert.Equal(float.NegativeInfinity, dtoNegInf.F);
        Assert.Equal(double.NegativeInfinity, dtoNegInf.D);

        // Epsilon
        var srcEps = new Basics
        {
            Str = "", G = Guid.Empty, Dt = DateTime.MinValue, Dto = DateTimeOffset.MinValue, Ts = TimeSpan.Zero,
            F = float.Epsilon, D = double.Epsilon
        };
        var dtoEps = Map(srcEps);
        Assert.Equal(float.Epsilon, dtoEps.F);
        Assert.Equal(double.Epsilon, dtoEps.D);

        // MinValue / MaxValue
        var srcMM = new Basics
        {
            Str = "", G = Guid.Empty, Dt = DateTime.MinValue, Dto = DateTimeOffset.MinValue, Ts = TimeSpan.Zero,
            F = float.MaxValue, D = double.MaxValue
        };
        var dtoMM = Map(srcMM);
        Assert.Equal(float.MaxValue, dtoMM.F);
        Assert.Equal(double.MaxValue, dtoMM.D);

        var srcmm = new Basics
        {
            Str = "", G = Guid.Empty, Dt = DateTime.MinValue, Dto = DateTimeOffset.MinValue, Ts = TimeSpan.Zero,
            F = float.MinValue, D = double.MinValue
        };
        var dtomm = Map(srcmm);
        Assert.Equal(float.MinValue, dtomm.F);
        Assert.Equal(double.MinValue, dtomm.D);
    }

    // ── Decimal, char, temporal ───────────────────────────────────────────────

    [Fact]
    public void Decimal_char_temporal_boundaries_survive()
    {
        // decimal.MinValue
        var src1 = new Basics
        {
            Str = "", G = Guid.Empty, Dt = DateTime.MinValue, Dto = DateTimeOffset.MinValue, Ts = TimeSpan.Zero,
            M = decimal.MinValue, C = '\0'
        };
        var dto1 = Map(src1);
        Assert.Equal(decimal.MinValue, dto1.M);
        Assert.Equal('\0', dto1.C);

        // decimal.MaxValue + high-precision value
        var src2 = new Basics
        {
            Str = "", G = Guid.Empty, Dt = DateTime.MinValue, Dto = DateTimeOffset.MinValue, Ts = TimeSpan.Zero,
            M = decimal.MaxValue, C = char.MaxValue
        };
        var dto2 = Map(src2);
        Assert.Equal(decimal.MaxValue, dto2.M);
        Assert.Equal(char.MaxValue, dto2.C);

        // High-precision decimal
        var preciseDecimal = 1.0000000000000000000000000001m;
        var src3 = new Basics
        {
            Str = "", G = Guid.Empty, Dt = DateTime.MinValue, Dto = DateTimeOffset.MinValue, Ts = TimeSpan.Zero,
            M = preciseDecimal
        };
        var dto3 = Map(src3);
        Assert.Equal(preciseDecimal, dto3.M);

        // string: empty and unicode
        var src4 = new Basics
        {
            G = Guid.Empty, Dt = DateTime.MinValue, Dto = DateTimeOffset.MinValue, Ts = TimeSpan.Zero,
            Str = ""
        };
        var dto4 = Map(src4);
        Assert.Equal("", dto4.Str);

        var src5 = new Basics
        {
            G = Guid.Empty, Dt = DateTime.MinValue, Dto = DateTimeOffset.MinValue, Ts = TimeSpan.Zero,
            Str = "Moria äöü世界"
        };
        var dto5 = Map(src5);
        Assert.Equal("Moria äöü世界", dto5.Str);

        // Guid: Empty and fixed
        var fixedGuid = new Guid("12345678-1234-5678-1234-567812345678");
        var src6 = new Basics
        {
            Str = "", Dt = DateTime.MinValue, Dto = DateTimeOffset.MinValue, Ts = TimeSpan.Zero,
            G = Guid.Empty
        };
        var dto6 = Map(src6);
        Assert.Equal(Guid.Empty, dto6.G);

        var src7 = new Basics
        {
            Str = "", Dt = DateTime.MinValue, Dto = DateTimeOffset.MinValue, Ts = TimeSpan.Zero,
            G = fixedGuid
        };
        var dto7 = Map(src7);
        Assert.Equal(fixedGuid, dto7.G);

        // DateTime: MinValue, MaxValue, Utc Kind
        var src8 = new Basics
        {
            Str = "", G = Guid.Empty, Dto = DateTimeOffset.MinValue, Ts = TimeSpan.Zero,
            Dt = DateTime.MinValue
        };
        var dto8 = Map(src8);
        Assert.Equal(DateTime.MinValue, dto8.Dt);

        var src9 = new Basics
        {
            Str = "", G = Guid.Empty, Dto = DateTimeOffset.MinValue, Ts = TimeSpan.Zero,
            Dt = DateTime.MaxValue
        };
        var dto9 = Map(src9);
        Assert.Equal(DateTime.MaxValue, dto9.Dt);

        var utcDt = new DateTime(2025, 6, 9, 12, 0, 0, DateTimeKind.Utc);
        var src10 = new Basics
        {
            Str = "", G = Guid.Empty, Dto = DateTimeOffset.MinValue, Ts = TimeSpan.Zero,
            Dt = utcDt
        };
        var dto10 = Map(src10);
        Assert.Equal(utcDt, dto10.Dt);
        Assert.Equal(DateTimeKind.Utc, dto10.Dt.Kind);

        // DateTimeOffset: MinValue, MaxValue, non-zero offset
        var src11 = new Basics
        {
            Str = "", G = Guid.Empty, Dt = DateTime.MinValue, Ts = TimeSpan.Zero,
            Dto = DateTimeOffset.MinValue
        };
        var dto11 = Map(src11);
        Assert.Equal(DateTimeOffset.MinValue, dto11.Dto);

        var src12 = new Basics
        {
            Str = "", G = Guid.Empty, Dt = DateTime.MinValue, Ts = TimeSpan.Zero,
            Dto = DateTimeOffset.MaxValue
        };
        var dto12 = Map(src12);
        Assert.Equal(DateTimeOffset.MaxValue, dto12.Dto);

        var offset = TimeSpan.FromHours(5.5);
        var dtWithOffset = new DateTimeOffset(2025, 3, 15, 10, 30, 0, offset);
        var src13 = new Basics
        {
            Str = "", G = Guid.Empty, Dt = DateTime.MinValue, Ts = TimeSpan.Zero,
            Dto = dtWithOffset
        };
        var dto13 = Map(src13);
        Assert.Equal(dtWithOffset, dto13.Dto);
        Assert.Equal(offset, dto13.Dto.Offset);

        // TimeSpan: MinValue, MaxValue, Zero
        var src14 = new Basics
        {
            Str = "", G = Guid.Empty, Dt = DateTime.MinValue, Dto = DateTimeOffset.MinValue,
            Ts = TimeSpan.MinValue
        };
        var dto14 = Map(src14);
        Assert.Equal(TimeSpan.MinValue, dto14.Ts);

        var src15 = new Basics
        {
            Str = "", G = Guid.Empty, Dt = DateTime.MinValue, Dto = DateTimeOffset.MinValue,
            Ts = TimeSpan.MaxValue
        };
        var dto15 = Map(src15);
        Assert.Equal(TimeSpan.MaxValue, dto15.Ts);

        var src16 = new Basics
        {
            Str = "", G = Guid.Empty, Dt = DateTime.MinValue, Dto = DateTimeOffset.MinValue,
            Ts = TimeSpan.Zero
        };
        var dto16 = Map(src16);
        Assert.Equal(TimeSpan.Zero, dto16.Ts);
    }

    // ── Nullables ─────────────────────────────────────────────────────────────

    [Fact]
    public void Nullables_survive_including_null()
    {
        // All non-null
        var src1 = new Basics
        {
            Str = "",
            G = Guid.Empty,
            Dt = DateTime.MinValue,
            Dto = DateTimeOffset.MinValue,
            Ts = TimeSpan.Zero,
            NI = int.MaxValue,
            ND = double.MaxValue,
            NDt = DateTime.MaxValue,
            NE = BasicColor.Blue
        };
        var dto1 = Map(src1);
        Assert.Equal(int.MaxValue, dto1.NI);
        Assert.Equal(double.MaxValue, dto1.ND);
        Assert.Equal(DateTime.MaxValue, dto1.NDt);
        Assert.Equal(BasicColor.Blue, dto1.NE);

        // All null
        var src2 = new Basics
        {
            Str = "",
            G = Guid.Empty,
            Dt = DateTime.MinValue,
            Dto = DateTimeOffset.MinValue,
            Ts = TimeSpan.Zero,
            NI = null,
            ND = null,
            NDt = null,
            NE = null
        };
        var dto2 = Map(src2);
        Assert.Null(dto2.NI);
        Assert.Null(dto2.ND);
        Assert.Null(dto2.NDt);
        Assert.Null(dto2.NE);

        // int? with MinValue
        var src3 = new Basics
        {
            Str = "",
            G = Guid.Empty,
            Dt = DateTime.MinValue,
            Dto = DateTimeOffset.MinValue,
            Ts = TimeSpan.Zero,
            NI = int.MinValue,
            ND = double.NegativeInfinity,
            NDt = DateTime.MinValue,
            NE = BasicColor.Red
        };
        var dto3 = Map(src3);
        Assert.Equal(int.MinValue, dto3.NI);
        Assert.Equal(double.NegativeInfinity, dto3.ND);
        Assert.Equal(DateTime.MinValue, dto3.NDt);
        Assert.Equal(BasicColor.Red, dto3.NE);
    }
}
