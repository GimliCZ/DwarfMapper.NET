// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

public class CAddr
{
    public string City { get; set; } = "";
}

public class CAddrDto
{
    public string City { get; set; } = "";
}

public class CSrc
{
    public int[] Nums { get; set; } = Array.Empty<int>();
    public List<CAddr> Addrs { get; set; } = new();
}

public class CDst
{
    public List<int> Nums { get; set; } = new();
    public CAddrDto[] Addrs { get; set; } = Array.Empty<CAddrDto>();
}

[DwarfMapper]
public partial class CollMapper
{
    public partial CDst Map(CSrc s);
    public partial CAddrDto ToDto(CAddr a);
}

// ── Top-level collection-returning mappers (Fix 1) ────────────────────────────

public class TLSrc
{
    public int Value { get; set; }
}

public class TLDto
{
    public int Value { get; set; }
}

[DwarfMapper]
public partial class TopLevelCollMapper
{
    public partial List<int> MapInts(List<long> xs);
    public partial List<int> MapArray(int[] xs);
    public partial Dictionary<string, int> MapDict(Dictionary<string, long> d);
}

[DwarfMapper(AutoNest = true)]
public partial class TopLevelNestedCollMapper
{
    public partial List<TLDto> MapAll(List<TLSrc> xs);
}

public class CollectionRuntimeTests
{
    [Fact]
    public void Maps_array_to_list_and_nested_list_to_array()
    {
        var d = new CollMapper().Map(new CSrc
        {
            Nums = new[] { 1, 2, 3 },
            Addrs = new List<CAddr> { new() { City = "Erebor" }, new() { City = "Moria" } }
        });
        Assert.Equal(new[] { 1, 2, 3 }, d.Nums);
        Assert.Equal(2, d.Addrs.Length);
        Assert.Equal("Erebor", d.Addrs[0].City);
        Assert.Equal("Moria", d.Addrs[1].City);
    }

    [Fact]
    public void TopLevel_List_long_to_List_int_maps_values()
    {
        var m = new TopLevelCollMapper();
        var result = m.MapInts(new List<long> { 1L, 2L, 3L });
        Assert.Equal(new[] { 1, 2, 3 }, result);
    }

    [Fact]
    public void TopLevel_array_to_List_int_maps_values()
    {
        var m = new TopLevelCollMapper();
        var result = m.MapArray(new[] { 10, 20, 30 });
        Assert.Equal(new[] { 10, 20, 30 }, result);
    }

    [Fact]
    public void TopLevel_Dictionary_string_long_to_Dictionary_string_int_maps_values()
    {
        var m = new TopLevelCollMapper();
        var input = new Dictionary<string, long> { ["a"] = 1L, ["b"] = 2L };
        var result = m.MapDict(input);
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result["a"]);
        Assert.Equal(2, result["b"]);
    }

    [Fact]
    public void TopLevel_AutoNest_List_Src_to_List_Dto_maps_values()
    {
        var m = new TopLevelNestedCollMapper();
        var input = new List<TLSrc> { new() { Value = 42 }, new() { Value = 99 } };
        var result = m.MapAll(input);
        Assert.Equal(2, result.Count);
        Assert.Equal(42, result[0].Value);
        Assert.Equal(99, result[1].Value);
    }
}
