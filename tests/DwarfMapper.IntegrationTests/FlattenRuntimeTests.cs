// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

public class FAddress
{
    public string City { get; set; } = "";
    public string Zip { get; set; } = "";
}

public class FCustomer
{
    public string Name { get; set; } = "";
    public FAddress Address { get; set; } = new();
}

public class FCustomerDto
{
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string Zip { get; set; } = "";
}

[DwarfMapper]
public partial class FlattenMapper
{
    [Flatten("Address")]
    public partial FCustomerDto ToDto(FCustomer c);
}

public class FlattenRuntimeTests
{
    [Fact]
    public void Flattens_address_members_to_top_level()
    {
        var dto = new FlattenMapper().ToDto(new FCustomer
        {
            Name = "Balin",
            Address = new FAddress { City = "Khazad-dum", Zip = "00001" }
        });
        Assert.Equal("Balin", dto.Name);
        Assert.Equal("Khazad-dum", dto.City);
        Assert.Equal("00001", dto.Zip);
    }
}
