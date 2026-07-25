// SPDX-License-Identifier: GPL-2.0-only
#nullable enable
using System;
using System.Collections.Generic;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

public class NeSrc
{
    public List<int?> Vals { get; set; } = new();
    public List<string?> Refs { get; set; } = new();
    public string?[] Arr { get; set; } = Array.Empty<string?>();
}

public class NeDst
{
    public List<int> Vals { get; set; } = new();
    public List<string> Refs { get; set; } = new();
    public string[] Arr { get; set; } = Array.Empty<string>();
}

// Nullable-element source collections mapped to non-nullable-element targets. This must COMPILE (the
// reference-element case previously produced CS8620 / a silent null-passing array clone) and must throw
// loudly on an actual null element rather than smuggle it into the non-null target.
[DwarfMapper]
[GenerateMap<NeSrc, NeDst>]
public partial class NullableElemMapper { }

public class NullableElementRuntimeTests
{
    [Fact]
    public void Maps_nullable_element_collections_to_non_null_when_no_nulls_present()
    {
        var d = new NullableElemMapper().Map(new NeSrc
        {
            Vals = new() { 1, 2 },
            Refs = new() { "a", "b" },
            Arr = new[] { "x" }
        });

        Assert.Equal(new[] { 1, 2 }, d.Vals);
        Assert.Equal(new[] { "a", "b" }, d.Refs);
        Assert.Equal(new[] { "x" }, d.Arr);
    }

    [Fact]
    public void Throws_on_null_reference_element_into_non_null_list()
    {
        var src = new NeSrc { Refs = new List<string?> { "a", null } };
        Assert.Throws<InvalidOperationException>(() => new NullableElemMapper().Map(src));
    }

    [Fact]
    public void Throws_on_null_reference_element_into_non_null_array()
    {
        var src = new NeSrc { Arr = new string?[] { "a", null } };
        Assert.Throws<InvalidOperationException>(() => new NullableElemMapper().Map(src));
    }

    [Fact]
    public void Throws_on_null_value_element_into_non_null_list()
    {
        var src = new NeSrc { Vals = new List<int?> { 1, null } };
        Assert.Throws<InvalidOperationException>(() => new NullableElemMapper().Map(src));
    }
}
