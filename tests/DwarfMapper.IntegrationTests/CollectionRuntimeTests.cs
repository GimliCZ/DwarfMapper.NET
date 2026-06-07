// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

public class CAddr { public string City { get; set; } = ""; }
public class CAddrDto { public string City { get; set; } = ""; }

public class CSrc { public int[] Nums { get; set; } = System.Array.Empty<int>(); public List<CAddr> Addrs { get; set; } = new(); }
public class CDst { public List<int> Nums { get; set; } = new(); public CAddrDto[] Addrs { get; set; } = System.Array.Empty<CAddrDto>(); }

[DwarfMapper]
public partial class CollMapper
{
    public partial CDst Map(CSrc s);
    public partial CAddrDto ToDto(CAddr a);
}

public class CollectionRuntimeTests
{
    [Fact]
    public void Maps_array_to_list_and_nested_list_to_array()
    {
        var d = new CollMapper().Map(new CSrc
        {
            Nums = new[] { 1, 2, 3 },
            Addrs = new List<CAddr> { new() { City = "Erebor" }, new() { City = "Moria" } },
        });
        Assert.Equal(new[] { 1, 2, 3 }, d.Nums);
        Assert.Equal(2, d.Addrs.Length);
        Assert.Equal("Erebor", d.Addrs[0].City);
        Assert.Equal("Moria", d.Addrs[1].City);
    }
}
