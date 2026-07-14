// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.IntegrationTests;

public class MoneySource
{
    public string Amount { get; set; } = "";
}

public class MoneyTarget
{
    public int Amount { get; set; }
}

[DwarfMapper]
public partial class MoneyMapper
{
    [MapProperty("Amount", "Amount", Use = nameof(Parse))]
    public partial MoneyTarget Map(MoneySource s);

    private static int Parse(string v)
    {
        return int.Parse(v, CultureInfo.InvariantCulture);
    }
}

public class Addr
{
    public string City { get; set; } = "";
}

public class AddrDto
{
    public string City { get; set; } = "";
}

public class Person2
{
    public Addr Home { get; set; } = new();
}

public class Person2Dto
{
    public AddrDto Home { get; set; } = new();
}

[DwarfMapper]
public partial class NestedMapper
{
    public partial Person2Dto ToDto(Person2 p);
    public partial AddrDto ToDto(Addr a);
}

public class ConversionRuntimeTests
{
    [Fact]
    public void Explicit_converter_runs()
    {
        var t = new MoneyMapper().Map(new MoneySource { Amount = "42" });
        Assert.Equal(42, t.Amount);
    }

    [Fact]
    public void Nested_mapping_runs()
    {
        var t = new NestedMapper().ToDto(new Person2 { Home = new Addr { City = "Moria" } });
        Assert.Equal("Moria", t.Home.City);
    }
}
