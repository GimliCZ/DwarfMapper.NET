// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.CorpusTests;

// Real-world mapping parity corpus — Group 3: computed / constant / currency / null-substitute / audit /
// record+value-object. Mirrors AutoMapper resolvers + Before/After, Mapster computed, Jason Taylor /
// eShop / FastEndpoints flows. Clock/culture are PINNED for determinism. `Cc_` prefixed types.

// #33 Computed full name (AutoMapper resolver / FastEndpoints)
public class Cc_Person
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
}

public class Cc_PersonDto
{
    public string FullName { get; set; } = "";
}

[DwarfMapper]
public partial class Cc_FullNameMapper
{
    [MapIgnore(nameof(Cc_PersonDto.FullName))]
    public partial Cc_PersonDto Map(Cc_Person s);

    [AfterMap]
    private static void Fill(Cc_Person s, Cc_PersonDto d)
    {
        d.FullName = $"{s.FirstName} {s.LastName}";
    }
}

// #34 Computed aggregate total (dotnet/eShop OrderSummary)
public class Cc_OrderItem
{
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class Cc_Order
{
    public List<Cc_OrderItem> Items { get; set; } = new();
}

public class Cc_OrderDto
{
    public decimal Total { get; set; }
}

[DwarfMapper]
public partial class Cc_TotalMapper
{
    [MapIgnore(nameof(Cc_OrderDto.Total))]
    public partial Cc_OrderDto Map(Cc_Order s);

    [AfterMap]
    private static void Fill(Cc_Order s, Cc_OrderDto d)
    {
        d.Total = s.Items.Sum(i => i.UnitPrice * i.Units);
    }
}

// #35 Constant target field (eShopOnWeb Status="Pending")
public class Cc_Req
{
    public int Id { get; set; }
}

public class Cc_Vm
{
    public int Id { get; set; }
    public string Status { get; set; } = "";
}

[DwarfMapper]
public partial class Cc_ConstMapper
{
    [MapValue(nameof(Cc_Vm.Status), "Pending")]
    public partial Cc_Vm Map(Cc_Req s);
}

// #40 Decimal → currency string (AutoMapper value converter / Mapperly StringFormat) — culture PINNED en-US
public class Cc_PriceSrc
{
    public decimal Price { get; set; }
}

public class Cc_PriceDto
{
    public string Price { get; set; } = "";
}

[DwarfMapper]
public partial class Cc_CurrencyMapper
{
    [MapProperty(nameof(Cc_PriceSrc.Price), nameof(Cc_PriceDto.Price), Use = nameof(ToCurrency))]
    public partial Cc_PriceDto Map(Cc_PriceSrc s);

    private static string ToCurrency(decimal v)
    {
        return v.ToString("C", CultureInfo.GetCultureInfo("en-US"));
    }
}

// #42 Null substitution (AutoMapper NullSubstitute("N/A"))
public class Cc_NsSrc
{
    public string Name { get; set; } = "";
    public string? MiddleName { get; set; }
}

public class Cc_NsDst
{
    public string Name { get; set; } = "";
    public string MiddleName { get; set; } = "";
}

[DwarfMapper]
public partial class Cc_NullSubMapper
{
    [MapProperty(nameof(Cc_NsSrc.MiddleName), nameof(Cc_NsDst.MiddleName), NullSubstitute = "N/A")]
    public partial Cc_NsDst Map(Cc_NsSrc s);
}

// #47 AfterMap audit stamping (Command→Entity; Jason Taylor create flow) — clock PINNED
public class Cc_CreateCmd
{
    public string Title { get; set; } = "";
    public int ListId { get; set; }
}

public class Cc_Todo
{
    public string Title { get; set; } = "";
    public int ListId { get; set; }
    public bool Done { get; set; }
    public DateTime CreatedUtc { get; set; }
}

[DwarfMapper]
public partial class Cc_AuditMapper
{
    public static readonly DateTime PinnedClock = new(2026, 6, 14, 9, 0, 0, DateTimeKind.Utc);

    [MapValue(nameof(Cc_Todo.Done), false)]
    [MapIgnore(nameof(Cc_Todo.CreatedUtc))]
    public partial Cc_Todo Map(Cc_CreateCmd s);

    [AfterMap]
    private static void Stamp(Cc_Todo d)
    {
        d.CreatedUtc = PinnedClock;
    }
}

// #48 Record/init ctor + value-object → string via implicit operator (Mapperly ctor; Jason Taylor Colour)
public sealed class Cc_Colour
{
    private readonly string _hex;

    public Cc_Colour(string hex)
    {
        _hex = hex;
    }

    public static implicit operator string(Cc_Colour c)
    {
        return c._hex;
    }
}

public class Cc_Item
{
    public string Name { get; set; } = "";
}

public class Cc_ItemDto
{
    public string Name { get; set; } = "";
}

public class Cc_TodoList
{
    public string Title { get; set; } = "";
    public Cc_Colour Colour { get; set; } = new("#000000");
    public List<Cc_Item> Items { get; set; } = new();
}

public record Cc_TodoListDto(string Title, string Colour, IReadOnlyCollection<Cc_ItemDto> Items);

[DwarfMapper]
public partial class Cc_RecordMapper
{
    // Value-object → string: the implicit operator is applied inside an explicit Use= converter.
    [MapProperty(nameof(Cc_TodoList.Colour), "Colour", Use = nameof(ColourToString))]
    public partial Cc_TodoListDto Map(Cc_TodoList s);

    private static string ColourToString(Cc_Colour c)
    {
        return c;
    }
}

public class CorpusComputedNullTests
{
    [Fact]
    public void S33_Computed_full_name()
    {
        Assert.Equal("Margaret Hamilton",
            new Cc_FullNameMapper().Map(new Cc_Person { FirstName = "Margaret", LastName = "Hamilton" }).FullName);
    }

    [Fact]
    public void S34_Computed_aggregate_total()
    {
        var d = new Cc_TotalMapper().Map(new Cc_Order
        {
            Items = new List<Cc_OrderItem> { new() { UnitPrice = 10, Units = 2 }, new() { UnitPrice = 5, Units = 3 } }
        });
        Assert.Equal(35m, d.Total);
    }

    [Fact]
    public void S35_Constant_field()
    {
        Assert.Equal("Pending", new Cc_ConstMapper().Map(new Cc_Req { Id = 1 }).Status);
    }

    [Fact]
    public void S40_Decimal_to_currency_string()
    {
        Assert.Equal("$1,234.50", new Cc_CurrencyMapper().Map(new Cc_PriceSrc { Price = 1234.50m }).Price);
    }

    [Fact]
    public void S42_Null_substitution()
    {
        Assert.Equal("N/A", new Cc_NullSubMapper().Map(new Cc_NsSrc { Name = "Grace", MiddleName = null }).MiddleName);
        Assert.Equal("Brewster",
            new Cc_NullSubMapper().Map(new Cc_NsSrc { Name = "Grace", MiddleName = "Brewster" }).MiddleName);
    }

    [Fact]
    public void S47_AfterMap_audit_stamping()
    {
        var d = new Cc_AuditMapper().Map(new Cc_CreateCmd { Title = "Write tests", ListId = 7 });
        Assert.Equal("Write tests", d.Title);
        Assert.Equal(7, d.ListId);
        Assert.False(d.Done);
        Assert.Equal(Cc_AuditMapper.PinnedClock, d.CreatedUtc);
    }

    [Fact]
    public void S48_Record_init_ctor_with_value_object()
    {
        var src = new Cc_TodoList
        {
            Title = "Work", Colour = new Cc_Colour("#0000FF"), Items = new List<Cc_Item> { new() { Name = "task" } }
        };
        var d = new Cc_RecordMapper().Map(src);
        Assert.Equal("Work", d.Title);
        Assert.Equal("#0000FF", d.Colour); // value-object → string via implicit operator
        Assert.Single(d.Items);
        Assert.Equal("task", d.Items.First().Name);
    }
}
