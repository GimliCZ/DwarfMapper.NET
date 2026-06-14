// SPDX-License-Identifier: GPL-2.0-only
using System.Linq;
using DwarfMapper;

namespace DwarfMapper.CorpusTests;

// Real-world mapping parity corpus — Group 1: flattening / deep paths / unflattening / rename / ignore.
// Each scenario mirrors a transformation real OSS projects implement with Mapperly / Mapster / AutoMapper
// (cited in CORPUS.md); the assertions are the exact outputs those libraries' documented semantics produce.
// DwarfMapper reproduces them. File-scoped `Cf_` prefixed types stay unique within the project.

// #1 Basic nested flatten Customer.Name → CustomerName (eShopOnWeb Order→OrderDto; AutoMapper flattening)
public class Cf_Customer { public string Name { get; set; } = ""; public string Email { get; set; } = ""; }
public class Cf_Order { public Cf_Customer Customer { get; set; } = new(); public decimal Amount { get; set; } }
public class Cf_OrderDto { public string CustomerName { get; set; } = ""; public decimal Amount { get; set; } }
[DwarfMapper] public partial class Cf_OrderMapper
{
    [MapProperty("Customer.Name", nameof(Cf_OrderDto.CustomerName))]
    public partial Cf_OrderDto Map(Cf_Order o);
}

// #2 PascalCase flatten Make.Id→MakeId, Make.Name→MakeName (Mapperly Car flattening)
public class Cf_Make { public int Id { get; set; } public string Name { get; set; } = ""; }
public class Cf_Car { public Cf_Make Make { get; set; } = new(); }
public class Cf_CarDto { public int MakeId { get; set; } public string MakeName { get; set; } = ""; }
[DwarfMapper] public partial class Cf_CarMapper
{
    [MapProperty("Make.Id", nameof(Cf_CarDto.MakeId))]
    [MapProperty("Make.Name", nameof(Cf_CarDto.MakeName))]
    public partial Cf_CarDto Map(Cf_Car c);
}

// #4 Deep multi-level flatten Customer.Address.City → CustomerAddressCity (AutoMapper PascalCase traversal)
public class Cf_Address { public string City { get; set; } = ""; public string Country { get; set; } = ""; }
public class Cf_Cust2 { public Cf_Address Address { get; set; } = new(); }
public class Cf_DeepSrc { public Cf_Cust2 Customer { get; set; } = new(); }
public class Cf_DeepDst { public string CustomerAddressCity { get; set; } = ""; }
[DwarfMapper] public partial class Cf_DeepMapper
{
    [MapProperty("Customer.Address.City", nameof(Cf_DeepDst.CustomerAddressCity))]
    public partial Cf_DeepDst Map(Cf_DeepSrc s);
}

// #9 Single-level unflatten MakeId→Make.Id, MakeName→Make.Name (Mapperly: explicit only)
[DwarfMapper] public partial class Cf_UnflattenMapper
{
    [MapProperty(nameof(Cf_CarDto.MakeId), "Make.Id")]
    [MapProperty(nameof(Cf_CarDto.MakeName), "Make.Name")]
    public partial Cf_Car Map(Cf_CarDto d);
}

// #11 Bidirectional rename round-trip Id ↔ Code (Mapster TwoWays)
public class Cf_Poco { public int Id { get; set; } }
public class Cf_CodeDto { public int Code { get; set; } }
[DwarfMapper] public partial class Cf_TwoWayMapper
{
    [ReverseMap]
    [MapProperty(nameof(Cf_Poco.Id), nameof(Cf_CodeDto.Code))]
    public partial Cf_CodeDto ToDto(Cf_Poco p);
    public partial Cf_Poco FromDto(Cf_CodeDto d);
}

// #13 Field rename Type→Name (eShopOnWeb CatalogType→CatalogTypeDto)
public class Cf_CatalogType { public int Id { get; set; } public string Type { get; set; } = ""; }
public class Cf_CatalogTypeDto { public int Id { get; set; } public string Name { get; set; } = ""; }
[DwarfMapper] public partial class Cf_CatalogTypeMapper
{
    [MapProperty(nameof(Cf_CatalogType.Type), nameof(Cf_CatalogTypeDto.Name))]
    public partial Cf_CatalogTypeDto Map(Cf_CatalogType s);
}

// #14/#15 Drop nav props + ignore sensitive field (eShopOnWeb CatalogItem; nopCommerce user)
public class Cf_Brand { public string BrandName { get; set; } = ""; }
public class Cf_CatalogItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int CatalogBrandId { get; set; }
    public Cf_Brand CatalogBrand { get; set; } = new(); // navigation — must NOT leak into the DTO
    public string PasswordHash { get; set; } = "";      // sensitive — must NOT leak into the DTO
}
public class Cf_CatalogItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int CatalogBrandId { get; set; }
}
[DwarfMapper] public partial class Cf_CatalogItemMapper { public partial Cf_CatalogItemDto Map(Cf_CatalogItem s); }

public class CorpusFlatteningRenameTests
{
    [Fact]
    public void S01_Basic_nested_flatten()
    {
        var d = new Cf_OrderMapper().Map(new Cf_Order { Customer = new Cf_Customer { Name = "Ada Lovelace", Email = "ada@x.io" }, Amount = 42.50m });
        Assert.Equal("Ada Lovelace", d.CustomerName);
        Assert.Equal(42.50m, d.Amount);
    }

    [Fact]
    public void S02_PascalCase_flatten_make()
    {
        var d = new Cf_CarMapper().Map(new Cf_Car { Make = new Cf_Make { Id = 7, Name = "Audi" } });
        Assert.Equal(7, d.MakeId);
        Assert.Equal("Audi", d.MakeName);
    }

    [Fact]
    public void S04_Deep_three_level_flatten()
    {
        var d = new Cf_DeepMapper().Map(new Cf_DeepSrc { Customer = new Cf_Cust2 { Address = new Cf_Address { City = "Prague", Country = "CZ" } } });
        Assert.Equal("Prague", d.CustomerAddressCity);
    }

    [Fact]
    public void S09_Single_level_unflatten()
    {
        var car = new Cf_UnflattenMapper().Map(new Cf_CarDto { MakeId = 7, MakeName = "Audi" });
        Assert.NotNull(car.Make);
        Assert.Equal(7, car.Make.Id);
        Assert.Equal("Audi", car.Make.Name);
    }

    [Fact]
    public void S11_Bidirectional_rename_round_trip()
    {
        var m = new Cf_TwoWayMapper();
        Assert.Equal(5, m.ToDto(new Cf_Poco { Id = 5 }).Code);
        Assert.Equal(5, m.FromDto(new Cf_CodeDto { Code = 5 }).Id);
    }

    [Fact]
    public void S13_Field_rename()
    {
        var d = new Cf_CatalogTypeMapper().Map(new Cf_CatalogType { Id = 3, Type = "Mug" });
        Assert.Equal(3, d.Id);
        Assert.Equal("Mug", d.Name);
    }

    [Fact]
    public void S14_15_Drops_nav_and_sensitive_keeps_fk()
    {
        var src = new Cf_CatalogItem { Id = 1, Name = "Mug", Price = 9.99m, CatalogBrandId = 4, CatalogBrand = new Cf_Brand { BrandName = "ACME" }, PasswordHash = "$2a$secret" };
        var d = new Cf_CatalogItemMapper().Map(src);
        Assert.Equal(1, d.Id);
        Assert.Equal("Mug", d.Name);
        Assert.Equal(9.99m, d.Price);
        Assert.Equal(4, d.CatalogBrandId); // FK retained
        // Nav object + password hash are not on the DTO shape — structurally cannot leak.
        Assert.DoesNotContain("PasswordHash", typeof(Cf_CatalogItemDto).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain("CatalogBrand", typeof(Cf_CatalogItemDto).GetProperties().Select(p => p.Name));
    }
}
