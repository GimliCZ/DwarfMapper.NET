// SPDX-License-Identifier: GPL-2.0-only
#nullable enable
using System.Collections.Generic;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

// IMPROVEMENT-PLAN item 9: the power-set fuzz pools contain only mutable classes and one trivial struct,
// so they cannot reach assignment-strategy bugs in init-only / required / readonly-struct / record-struct /
// deep-nullability shapes. These shapes also exercise the recently fixed init-only update-into (#109) and
// nullable-element (#100) paths. Each case asserts the mapped value, not merely that it compiles.

// ── init-only + required (CREATE: object-initializer assignment) ──────────────
public class NtSrc { public int A { get; set; } public string B { get; set; } = ""; public int C { get; set; } }
public class NtInitDst { public int A { get; init; } public required string B { get; init; } public int C { get; set; } }

// ── readonly struct (ctor-only) ───────────────────────────────────────────────
public readonly struct NtPoint { public NtPoint(int x, int y) { X = x; Y = y; } public int X { get; } public int Y { get; } }
public class NtPointSrc { public int X { get; set; } public int Y { get; set; } }

// ── record struct (positional, value semantics) ───────────────────────────────
public record struct NtRecStruct(int X, string Y);
public class NtRecSrc { public int X { get; set; } public string Y { get; set; } = ""; }

// ── deep nullability ──────────────────────────────────────────────────────────
public class NtDeepSrc { public List<int?> Keep { get; set; } = new(); }
public class NtDeepDst { public List<int?> Keep { get; set; } = new(); } // null-preserving element

[DwarfMapper]
[GenerateMap<NtSrc, NtInitDst>]
[GenerateMap<NtPointSrc, NtPoint>]
[GenerateMap<NtRecSrc, NtRecStruct>]
[GenerateMap<NtDeepSrc, NtDeepDst>]
public partial class NonTrivialMapper
{
    // init-only update-into: the init-only target members are immutable post-construction (#109),
    // so they must be ignored; only the settable member is updated.
    [MapIgnore("A")]
    [MapIgnore("B")]
    public partial void Update(NtSrc src, NtInitDst dest);
}

public class NonTrivialShapeRuntimeTests
{
    [Fact]
    public void InitOnly_and_required_members_assigned_via_object_initializer()
    {
        var d = new NonTrivialMapper().Map(new NtSrc { A = 1, B = "b", C = 3 });
        Assert.Equal(1, d.A);
        Assert.Equal("b", d.B);
        Assert.Equal(3, d.C);
    }

    [Fact]
    public void InitOnly_update_into_preserves_init_members_updates_settable()
    {
        var dest = new NtInitDst { A = 10, B = "orig", C = 0 };
        new NonTrivialMapper().Update(new NtSrc { A = 99, B = "new", C = 42 }, dest);
        Assert.Equal(10, dest.A);     // init-only preserved
        Assert.Equal("orig", dest.B); // init-only preserved
        Assert.Equal(42, dest.C);     // settable updated
    }

    [Fact]
    public void Readonly_struct_constructed_via_ctor()
    {
        var p = new NonTrivialMapper().Map(new NtPointSrc { X = 4, Y = 5 });
        Assert.Equal(4, p.X);
        Assert.Equal(5, p.Y);
    }

    [Fact]
    public void Record_struct_mapped_by_value()
    {
        var r = new NonTrivialMapper().Map(new NtRecSrc { X = 7, Y = "z" });
        Assert.Equal(new NtRecStruct(7, "z"), r);
    }

    [Fact]
    public void Deep_nullable_element_list_preserves_nulls()
    {
        var d = new NonTrivialMapper().Map(new NtDeepSrc { Keep = new List<int?> { 1, null, 3 } });
        Assert.Equal(new int?[] { 1, null, 3 }, d.Keep);
    }
}
