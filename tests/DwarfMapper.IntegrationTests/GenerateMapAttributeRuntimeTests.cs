// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

// Low-ceremony declaration: [GenerateMap<Src,Dst>] on the mapper class generates a public
// `Dst Map(Src)` overload — no per-pair partial method, POCOs stay clean. Near-mechanical
// replacement for AutoMapper's CreateMap<A,B>() / migration target.

public class GmOrder
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class GmOrderDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class GmCustomer
{
    public long Num { get; set; }
    public GmAddr Addr { get; set; } = new();
}

public class GmCustomerDto
{
    public int Num { get; set; }
    public GmAddrDto Addr { get; set; } = new();
}

public class GmAddr
{
    public string City { get; set; } = "";
}

public class GmAddrDto
{
    public string City { get; set; } = "";
}

[DwarfMapper]
[GenerateMap<GmOrder, GmOrderDto>]
[GenerateMap<GmCustomer, GmCustomerDto>]
[GenerateMap<GmAddr, GmAddrDto>]
public partial class GmMappers
{
}

public class GenerateMapAttributeRuntimeTests
{
    [Fact]
    public void Generated_overload_maps_flat()
    {
        var dto = new GmMappers().Map(new GmOrder { Id = 7, Name = "vein" });
        Assert.Equal(7, dto.Id);
        Assert.Equal("vein", dto.Name);
    }

    [Fact]
    public void Generated_overload_applies_conversions_and_nesting()
    {
        var dto = new GmMappers().Map(new GmCustomer { Num = 42L, Addr = new GmAddr { City = "Khazad" } });
        Assert.Equal(42, dto.Num); // long → int CreateChecked
        Assert.Equal("Khazad", dto.Addr.City); // nested via the GmAddr→GmAddrDto pair
    }

    [Fact]
    public void Overload_resolves_by_source_type()
    {
        var m = new GmMappers();
        var o = m.Map(new GmOrder { Id = 1 });
        var c = m.Map(new GmCustomer { Num = 2 });
        Assert.Equal(1, o.Id);
        Assert.Equal(2, c.Num);
    }
}
