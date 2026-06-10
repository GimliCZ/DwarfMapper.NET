// SPDX-License-Identifier: GPL-2.0-only
using System;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

// ── Positional record → positional record ─────────────────────────────────────

public record CtorSrcRecord(int X, string Y);
public record CtorDstRecord(int X, string Y);

[DwarfMapper]
public partial class RecordToRecordMapper { public partial CtorDstRecord Map(CtorSrcRecord s); }

// ── Class source → positional record target ───────────────────────────────────

public class CtorClassSrc { public int X { get; set; } public string Y { get; set; } = ""; }
public record CtorRecordDst(int X, string Y);

[DwarfMapper]
public partial class ClassToRecordMapper { public partial CtorRecordDst Map(CtorClassSrc s); }

// ── Record with extra init/set props beyond positional params ─────────────────

public class CtorSrcWithExtra { public int X { get; set; } public string Y { get; set; } = ""; public int Z { get; set; } }
public record CtorRecordWithExtra(int X, string Y) { public int Z { get; init; } }

[DwarfMapper]
public partial class RecordWithExtraMapper { public partial CtorRecordWithExtra Map(CtorSrcWithExtra s); }

// ── Ctor param needing conversion ─────────────────────────────────────────────

// long→int via CreateChecked
public class LongSrc2 { public long X { get; set; } }
public class IntCtorDst { public IntCtorDst(int X) { this.X = X; } public int X { get; } }

[DwarfMapper]
public partial class LongToIntCtorMapper { public partial IntCtorDst Map(LongSrc2 s); }

// string→Guid via IParsable
public class StringGuidSrc { public string G { get; set; } = ""; }
public class GuidCtorDst { public GuidCtorDst(Guid G) { this.G = G; } public Guid G { get; } }

[DwarfMapper]
public partial class StringToGuidCtorMapper { public partial GuidCtorDst Map(StringGuidSrc s); }

// int? ctor param (nullable→non-nullable with ThrowIfNull)
public class NullableCtorSrc { public int? X { get; set; } }
public class NonNullableCtorDst { public NonNullableCtorDst(int X) { this.X = X; } public int X { get; } }

[DwarfMapper]
public partial class NullableToCtorMapper { public partial NonNullableCtorDst Map(NullableCtorSrc s); }

// ── Constructor-only class (no parameterless ctor, no setters) ────────────────

public class CtorSrc2 { public int A { get; set; } public string B { get; set; } = ""; }
public class CtorOnlyDst
{
    public CtorOnlyDst(int A, string B) { this.A = A; this.B = B; }
    public int A { get; }
    public string B { get; } = "";
}

[DwarfMapper]
public partial class CtorOnlyMapper { public partial CtorOnlyDst Map(CtorSrc2 s); }

// ── record struct ─────────────────────────────────────────────────────────────

public class RecStructSrc { public int X { get; set; } public int Y { get; set; } }
public record struct CtorRecordStruct(int X, int Y);

[DwarfMapper]
public partial class RecordStructMapper { public partial CtorRecordStruct Map(RecStructSrc s); }

// ── readonly record struct ────────────────────────────────────────────────────

public class RoRecStructSrc { public int X { get; set; } public int Y { get; set; } }
public readonly record struct CtorReadonlyRecordStruct(int X, int Y);

[DwarfMapper]
public partial class ReadonlyRecordStructMapper { public partial CtorReadonlyRecordStruct Map(RoRecStructSrc s); }

// ── readonly struct with explicit ctor ────────────────────────────────────────

public class RoStructSrc { public int X { get; set; } public int Y { get; set; } }
public readonly struct CtorReadonlyStruct
{
    public CtorReadonlyStruct(int X, int Y) { this.X = X; this.Y = Y; }
    public int X { get; }
    public int Y { get; }
}

[DwarfMapper]
public partial class ReadonlyStructMapper { public partial CtorReadonlyStruct Map(RoStructSrc s); }

// ── required members + ctor combo ─────────────────────────────────────────────

public class RequiredCtorSrc { public int X { get; set; } public string Y { get; set; } = ""; public int Z { get; set; } }
public class RequiredCtorDst
{
    public RequiredCtorDst(int X) { this.X = X; }
    public int X { get; }
    public required string Y { get; set; }
    public int Z { get; set; }
}

[DwarfMapper]
public partial class RequiredCtorMapper { public partial RequiredCtorDst Map(RequiredCtorSrc s); }

// ── Regression: class with parameterless ctor + settable props ────────────────

public class RegSrc { public int X { get; set; } public string Y { get; set; } = ""; }
public class RegDst { public int X { get; set; } public string Y { get; set; } = ""; }

[DwarfMapper]
public partial class RegressionSettableMapper { public partial RegDst Map(RegSrc s); }

// ── Nested: ctor param whose type is itself a mapped record ───────────────────
// Outer has an Inner member of class type; inner mapper converts it to a record.

public record InnerRecord(int V);
public class InnerClassSrc { public int V { get; set; } }
public class OuterClassSrc { public InnerClassSrc Inner { get; set; } = new(); public int Outer { get; set; } }
public record OuterRecord(InnerRecord Inner, int Outer);

[DwarfMapper]
public partial class NestedRecordMapper
{
    public partial OuterRecord Map(OuterClassSrc s);
    public partial InnerRecord Map(InnerClassSrc s);
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class ConstructorMappingRuntimeTests
{
    [Fact]
    public void Record_to_record_maps_all_values()
    {
        var mapper = new RecordToRecordMapper();
        var result = mapper.Map(new CtorSrcRecord(42, "hello"));
        Assert.Equal(42, result.X);
        Assert.Equal("hello", result.Y);
    }

    [Fact]
    public void Class_source_to_record_target_maps_correctly()
    {
        var mapper = new ClassToRecordMapper();
        var result = mapper.Map(new CtorClassSrc { X = 7, Y = "world" });
        Assert.Equal(7, result.X);
        Assert.Equal("world", result.Y);
    }

    [Fact]
    public void Record_with_extra_init_maps_ctor_params_and_init_props()
    {
        var mapper = new RecordWithExtraMapper();
        var result = mapper.Map(new CtorSrcWithExtra { X = 1, Y = "test", Z = 99 });
        Assert.Equal(1, result.X);
        Assert.Equal("test", result.Y);
        Assert.Equal(99, result.Z);
    }

    [Fact]
    public void Long_to_int_ctor_param_in_range_succeeds()
    {
        var mapper = new LongToIntCtorMapper();
        var result = mapper.Map(new LongSrc2 { X = 100L });
        Assert.Equal(100, result.X);
    }

    [Fact]
    public void Long_to_int_ctor_param_overflow_throws()
    {
        var mapper = new LongToIntCtorMapper();
        Assert.Throws<OverflowException>(() => mapper.Map(new LongSrc2 { X = (long)int.MaxValue + 1L }));
    }

    [Fact]
    public void String_to_guid_ctor_param_parses()
    {
        var mapper = new StringToGuidCtorMapper();
        var g = Guid.Parse("12345678-1234-5678-1234-567812345678");
        var result = mapper.Map(new StringGuidSrc { G = g.ToString() });
        Assert.Equal(g, result.G);
    }

    [Fact]
    public void Nullable_to_ctor_param_with_value_succeeds()
    {
        var mapper = new NullableToCtorMapper();
        var result = mapper.Map(new NullableCtorSrc { X = 42 });
        Assert.Equal(42, result.X);
    }

    [Fact]
    public void Nullable_to_ctor_param_null_throws()
    {
        var mapper = new NullableToCtorMapper();
        Assert.Throws<InvalidOperationException>(() => mapper.Map(new NullableCtorSrc { X = null }));
    }

    [Fact]
    public void Constructor_only_class_maps_correctly()
    {
        var mapper = new CtorOnlyMapper();
        var result = mapper.Map(new CtorSrc2 { A = 5, B = "five" });
        Assert.Equal(5, result.A);
        Assert.Equal("five", result.B);
    }

    [Fact]
    public void Record_struct_maps_correctly()
    {
        var mapper = new RecordStructMapper();
        var result = mapper.Map(new RecStructSrc { X = 10, Y = 20 });
        Assert.Equal(10, result.X);
        Assert.Equal(20, result.Y);
    }

    [Fact]
    public void Readonly_record_struct_maps_correctly()
    {
        var mapper = new ReadonlyRecordStructMapper();
        var result = mapper.Map(new RoRecStructSrc { X = 3, Y = 4 });
        Assert.Equal(3, result.X);
        Assert.Equal(4, result.Y);
    }

    [Fact]
    public void Readonly_struct_with_explicit_ctor_maps_correctly()
    {
        var mapper = new ReadonlyStructMapper();
        var result = mapper.Map(new RoStructSrc { X = 11, Y = 22 });
        Assert.Equal(11, result.X);
        Assert.Equal(22, result.Y);
    }

    [Fact]
    public void Required_members_plus_ctor_maps_all_fields()
    {
        var mapper = new RequiredCtorMapper();
        var result = mapper.Map(new RequiredCtorSrc { X = 1, Y = "req", Z = 3 });
        Assert.Equal(1, result.X);
        Assert.Equal("req", result.Y);
        Assert.Equal(3, result.Z);
    }

    [Fact]
    public void Regression_settable_class_still_uses_object_initializer()
    {
        var mapper = new RegressionSettableMapper();
        var result = mapper.Map(new RegSrc { X = 9, Y = "nine" });
        Assert.Equal(9, result.X);
        Assert.Equal("nine", result.Y);
    }

    [Fact]
    public void Nested_record_ctor_param_invokes_auto_nested_mapper()
    {
        var mapper = new NestedRecordMapper();
        var result = mapper.Map(new OuterClassSrc { Inner = new InnerClassSrc { V = 7 }, Outer = 100 });
        Assert.Equal(7, result.Inner.V);
        Assert.Equal(100, result.Outer);
    }

}
