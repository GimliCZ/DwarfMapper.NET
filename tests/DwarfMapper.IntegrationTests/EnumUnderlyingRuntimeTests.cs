// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

// ── Safe path 1: by-name across mismatched underlying width ──────────────────
// ESrcByte : byte  →  EDstLong : long  (widening underlying, but mapped BY NAME)
public enum ESrcByte : byte
{
    A,
    B,
    C
}

public enum EDstLong : long
{
    A,
    B,
    C
}

public class EnumSrcByteHolder
{
    public ESrcByte V { get; set; }
}

public class EnumDstLongHolder
{
    public EDstLong V { get; set; }
}

[DwarfMapper]
public partial class EnumByteToLongMapper
{
    public partial EnumDstLongHolder Map(EnumSrcByteHolder s);
}

// Reverse: long → byte by name (narrowing underlying, still safe via NAME switch)
public class EnumSrcLongHolder
{
    public EDstLong V { get; set; }
}

public class EnumDstByteHolder
{
    public ESrcByte V { get; set; }
}

[DwarfMapper]
public partial class EnumLongToByteMapper
{
    public partial EnumDstByteHolder Map(EnumSrcLongHolder s);
}

// ── Safe path 2: by-name with :ulong including large value ───────────────────
public enum BigULong : ulong
{
    Lo = 0,
    Hi = 9223372036854775809
} // Hi > ulong.MaxValue/2, non-power-of-2 (avoids CA1027 Flags hint)

public class BigSrcHolder
{
    public BigULong V { get; set; }
}

public class BigDstHolder
{
    public BigULong V { get; set; }
}

[DwarfMapper]
public partial class BigULongMapper
{
    public partial BigDstHolder Map(BigSrcHolder s);
}

// ── Safe path 3: enum → wider numeric (widening cast, value preserved) ────────
public enum EByte : byte
{
    X = 200
}

public class EByteHolder
{
    public EByte V { get; set; }
}

public class IntHolder
{
    public int V { get; set; }
}

[DwarfMapper]
public partial class EnumByteToIntMapper
{
    public partial IntHolder Map(EByteHolder s);
}

public enum EInt
{
    Y = int.MaxValue
}

public class EIntHolder
{
    public EInt V { get; set; }
}

public class LongHolder
{
    public long V { get; set; }
}

[DwarfMapper]
public partial class EnumIntToLongMapper
{
    public partial LongHolder Map(EIntHolder s);
}

// ── Narrowing path: enum → narrower numeric (now uses CreateChecked — loud on overflow) ──
// ELongBig : long { Big = 4294967296L }  mapped to int target.
// The generator now emits: global::System.Int32.CreateChecked((global::System.Int64)v)
// which throws OverflowException for 4294967296L (does not fit int).
public enum ELongBig : long
{
    Big = 4294967296L
} // 2^32 — does NOT fit int

public class ELongBigHolder
{
    public ELongBig V { get; set; }
}

public class IntHolderNarrow
{
    public int V { get; set; }
}

[DwarfMapper]
public partial class EnumLongToIntMapper
{
    public partial IntHolderNarrow Map(ELongBigHolder s);
}

// Also test a small in-range value for enum→int narrowing (must still map correctly)
public enum ELongSmall : long
{
    Small = 100L
}

public class ELongSmallHolder
{
    public ELongSmall V { get; set; }
}

public class IntHolderSmall
{
    public int V { get; set; }
}

[DwarfMapper]
public partial class EnumLongSmallToIntMapper
{
    public partial IntHolderSmall Map(ELongSmallHolder s);
}

// ── ByValue: enum → enum narrowing (now uses CreateChecked — loud on overflow) ─
// EnumStrategy.ByValue is user-reachable via [DwarfMapper(EnumStrategy = EnumStrategy.ByValue)].
// Generator now emits: (BValEnum)global::System.Byte.CreateChecked((global::System.Int64)v)
// which throws OverflowException for 256 (does not fit byte).
public enum LValEnum : long
{
    X = 256,
    Y = 10
}

public enum BValEnum : byte
{
    Y = 10
} // Y=10 fits, X=256 does not

public class LValHolder
{
    public LValEnum V { get; set; }
}

public class BValHolder
{
    public BValEnum V { get; set; }
}

[DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]
public partial class EnumByValNarrowMapper
{
    public partial BValHolder Map(LValHolder s);
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class EnumUnderlyingRuntimeTests
{
    // Safe path 1a: byte → long by name, value preserved by member NAME
    [Fact]
    public void ByName_byte_underlying_to_long_underlying_preserves_value()
    {
        var mapper = new EnumByteToLongMapper();
        Assert.Equal(EDstLong.A, mapper.Map(new EnumSrcByteHolder { V = ESrcByte.A }).V);
        Assert.Equal(EDstLong.B, mapper.Map(new EnumSrcByteHolder { V = ESrcByte.B }).V);
        Assert.Equal(EDstLong.C, mapper.Map(new EnumSrcByteHolder { V = ESrcByte.C }).V);
    }

    // Safe path 1b: long → byte by name, still safe regardless of underlying
    [Fact]
    public void ByName_long_underlying_to_byte_underlying_preserves_value()
    {
        var mapper = new EnumLongToByteMapper();
        Assert.Equal(ESrcByte.A, mapper.Map(new EnumSrcLongHolder { V = EDstLong.A }).V);
        Assert.Equal(ESrcByte.B, mapper.Map(new EnumSrcLongHolder { V = EDstLong.B }).V);
        Assert.Equal(ESrcByte.C, mapper.Map(new EnumSrcLongHolder { V = EDstLong.C }).V);
    }

    // Safe path 2: ulong with a member value > int.MaxValue, mapped by name to same enum
    [Fact]
    public void ByName_ulong_underlying_large_value_preserved_by_name()
    {
        var mapper = new BigULongMapper();
        Assert.Equal(BigULong.Lo, mapper.Map(new BigSrcHolder { V = BigULong.Lo }).V);
        Assert.Equal(BigULong.Hi, mapper.Map(new BigSrcHolder { V = BigULong.Hi }).V);
    }

    // Safe path 3a: enum : byte → int (widening cast, value preserved)
    [Fact]
    public void Enum_byte_to_int_widening_preserves_value()
    {
        var result = new EnumByteToIntMapper().Map(new EByteHolder { V = EByte.X });
        Assert.Equal(200, result.V); // (int)(byte)200 == 200
    }

    // Safe path 3b: enum : int → long (widening cast, value preserved)
    [Fact]
    public void Enum_int_to_long_widening_preserves_value()
    {
        var result = new EnumIntToLongMapper().Map(new EIntHolder { V = EInt.Y });
        Assert.Equal(int.MaxValue, result.V); // widening, no truncation
    }

    // enum : long { Big = 4294967296L } → int now throws OverflowException (checked via CreateChecked).
    [Fact]
    public void Enum_long_to_int_narrowing_throws_on_overflow()
    {
        var mapper = new EnumLongToIntMapper();
        // 4294967296L does not fit int — CreateChecked must throw.
        Assert.Throws<OverflowException>(() => mapper.Map(new ELongBigHolder { V = ELongBig.Big }));
    }

    // In-range enum : long → int still maps correctly.
    [Fact]
    public void Enum_long_to_int_in_range_preserves_value()
    {
        var result = new EnumLongSmallToIntMapper().Map(new ELongSmallHolder { V = ELongSmall.Small });
        Assert.Equal(100, result.V);
    }

    // ByValue enum L : long { X=256, Y=10 } → enum B : byte { Y=10 } — X=256 overflows byte.
    [Fact]
    public void EnumByValue_long_to_byte_narrowing_throws_on_overflow()
    {
        var mapper = new EnumByValNarrowMapper();
        // LValEnum.X has value 256 which does not fit byte — CreateChecked must throw.
        Assert.Throws<OverflowException>(() => mapper.Map(new LValHolder { V = LValEnum.X }));
    }

    // In-range ByValue: Y=10 fits byte — must still map correctly.
    [Fact]
    public void EnumByValue_long_to_byte_in_range_preserves_value()
    {
        var result = new EnumByValNarrowMapper().Map(new LValHolder { V = LValEnum.Y });
        Assert.Equal(BValEnum.Y, result.V);
    }
}
