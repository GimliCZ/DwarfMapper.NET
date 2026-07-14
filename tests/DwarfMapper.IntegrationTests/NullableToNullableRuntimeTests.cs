// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

// ── long? → int? ──────────────────────────────────────────────────────────────
public class LongNullableSrc
{
    public long? V { get; set; }
}

public class IntNullableDst
{
    public int? V { get; set; }
}

[DwarfMapper]
public partial class LongNullableToIntNullableMapper
{
    public partial IntNullableDst Map(LongNullableSrc s);
}

// ── E1? → E2? (by-name) ──────────────────────────────────────────────────────
public enum NNE1
{
    A,
    B,
    C
}

public enum NNE2
{
    A,
    B,
    C
}

public class NNE1NullableSrc
{
    public NNE1? V { get; set; }
}

public class NNE2NullableDst
{
    public NNE2? V { get; set; }
}

[DwarfMapper]
public partial class NullableEnumToNullableEnumMapper
{
    public partial NNE2NullableDst Map(NNE1NullableSrc s);
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class NullableToNullableRuntimeTests
{
    // ── long? → int? ─────────────────────────────────────────────────────────

    [Fact]
    public void NullableLong_to_NullableInt_null_maps_to_null()
    {
        // This was the failing probe: null source must propagate null, not throw.
        var result = new LongNullableToIntNullableMapper().Map(new LongNullableSrc { V = null });
        Assert.Null(result.V);
    }

    [Fact]
    public void NullableLong_to_NullableInt_value_maps()
    {
        var result = new LongNullableToIntNullableMapper().Map(new LongNullableSrc { V = 42L });
        Assert.Equal(42, result.V);
    }

    [Fact]
    public void NullableLong_to_NullableInt_overflow_throws()
    {
        // Non-null out-of-range value still throws (CreateChecked applies to the value path).
        var mapper = new LongNullableToIntNullableMapper();
        Assert.Throws<OverflowException>(() => mapper.Map(new LongNullableSrc { V = int.MaxValue + 1L }));
    }

    // ── E1? → E2? ────────────────────────────────────────────────────────────

    [Fact]
    public void NullableEnum_to_NullableEnum_null_maps_to_null()
    {
        var result = new NullableEnumToNullableEnumMapper().Map(new NNE1NullableSrc { V = null });
        Assert.Null(result.V);
    }

    [Fact]
    public void NullableEnum_to_NullableEnum_value_maps()
    {
        var result = new NullableEnumToNullableEnumMapper().Map(new NNE1NullableSrc { V = NNE1.B });
        Assert.Equal(NNE2.B, result.V);
    }
}
