// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

// ── Safe path 1: by-name across mismatched underlying width ──────────────────
// ESrcByte : byte  →  EDstLong : long  (widening underlying, but mapped BY NAME)
public enum ESrcByte : byte  { A, B, C }
public enum EDstLong : long  { A, B, C }

public class EnumSrcByteHolder { public ESrcByte V { get; set; } }
public class EnumDstLongHolder { public EDstLong V { get; set; } }

[DwarfMapper]
public partial class EnumByteToLongMapper
{
    public partial EnumDstLongHolder Map(EnumSrcByteHolder s);
}

// Reverse: long → byte by name (narrowing underlying, still safe via NAME switch)
public class EnumSrcLongHolder { public EDstLong V { get; set; } }
public class EnumDstByteHolder { public ESrcByte V { get; set; } }

[DwarfMapper]
public partial class EnumLongToByteMapper
{
    public partial EnumDstByteHolder Map(EnumSrcLongHolder s);
}

// ── Safe path 2: by-name with :ulong including large value ───────────────────
public enum BigULong : ulong { Lo = 0, Hi = 9223372036854775809 }  // Hi > ulong.MaxValue/2, non-power-of-2 (avoids CA1027 Flags hint)
public class BigSrcHolder { public BigULong V { get; set; } }
public class BigDstHolder { public BigULong V { get; set; } }

[DwarfMapper]
public partial class BigULongMapper
{
    public partial BigDstHolder Map(BigSrcHolder s);
}

// ── Safe path 3: enum → wider numeric (widening cast, value preserved) ────────
public enum EByte : byte { X = 200 }
public class EByteHolder   { public EByte V { get; set; } }
public class IntHolder     { public int   V { get; set; } }

[DwarfMapper]
public partial class EnumByteToIntMapper { public partial IntHolder Map(EByteHolder s); }

public enum EInt : int { Y = int.MaxValue }
public class EIntHolder    { public EInt V { get; set; } }
public class LongHolder    { public long V { get; set; } }

[DwarfMapper]
public partial class EnumIntToLongMapper { public partial LongHolder Map(EIntHolder s); }

// ── FINDING path: enum → narrower numeric (silent truncation) ─────────────────
// ELongBig : long { Big = 4294967296L }  mapped to int target.
// The generator emits:  (int)v   which truncates via C# unchecked cast.
// (int)4294967296L == 0  — the value is silently destroyed.
public enum ELongBig : long { Big = 4294967296L }   // 2^32 — does NOT fit int
public class ELongBigHolder { public ELongBig V { get; set; } }
public class IntHolderNarrow { public int     V { get; set; } }

[DwarfMapper]
public partial class EnumLongToIntMapper { public partial IntHolderNarrow Map(ELongBigHolder s); }

// ── ByValue: enum → enum narrowing (enum L : long { X = 256 } → enum B : byte) ─
// EnumStrategy.ByValue is user-reachable via [DwarfMapper(EnumStrategy = EnumStrategy.ByValue)].
// Generator emits: (BValEnum)(long)v — (byte)256L == 0 (truncation, silently).
public enum LValEnum : long  { X = 256 }
public enum BValEnum : byte  { }          // no members — any cast lands outside defined values
public class LValHolder { public LValEnum V { get; set; } }
public class BValHolder { public BValEnum V { get; set; } }

[DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]
public partial class EnumByValNarrowMapper { public partial BValHolder Map(LValHolder s); }

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
        Assert.Equal(200, result.V);  // (int)(byte)200 == 200
    }

    // Safe path 3b: enum : int → long (widening cast, value preserved)
    [Fact]
    public void Enum_int_to_long_widening_preserves_value()
    {
        var result = new EnumIntToLongMapper().Map(new EIntHolder { V = EInt.Y });
        Assert.Equal((long)int.MaxValue, result.V);  // widening, no truncation
    }

    // FINDING: enum : long { Big = 4294967296L } → int is a SILENT narrowing truncation.
    // The generator emits `(int)v` (plain cast), which in unchecked C# truncates the
    // low 32 bits: (int)4294967296L == 0.
    // There is NO diagnostic (DWARF warning or error) for this case — the value is
    // silently destroyed. This conflicts with the project's "no silent surprises" thesis
    // and should be addressed with a diagnostic (e.g. DWARF-NNN: narrowing enum cast).
    [Fact]
    public void FINDING_Enum_long_to_int_narrowing_truncates_silently()
    {
        var result = new EnumLongToIntMapper().Map(new ELongBigHolder { V = ELongBig.Big });

        // FINDING: (int)4294967296L == 0 — the value 2^32 is silently truncated to 0.
        // No generator diagnostic is emitted. The caller receives a completely wrong value
        // with no indication that data was lost. A DWARF diagnostic for narrowing enum→numeric
        // casts should be considered.
        Assert.Equal(0, result.V);
    }

    // ByValue reachable: enum L : long { X = 256 } → enum B : byte via ByValue.
    // Generator emits: (BValEnum)(long)v — (byte)256L == 0 (wraps/truncates, no diagnostic).
    // FINDING: ByValue narrowing enum→enum cast is also silently lossy.
    [Fact]
    public void FINDING_EnumByValue_long_to_byte_underlying_truncates_silently()
    {
        var result = new EnumByValNarrowMapper().Map(new LValHolder { V = LValEnum.X });

        // FINDING: (BValEnum)(long)LValEnum.X where X=256 → (byte)256L == 0.
        // The ByValue path uses (Tgt)(srcUnderlying)v which silently truncates when the
        // source underlying value exceeds the target underlying range.
        // No diagnostic is emitted. A DWARF diagnostic for narrowing ByValue casts is warranted.
        Assert.Equal((BValEnum)0, result.V);
    }
}
