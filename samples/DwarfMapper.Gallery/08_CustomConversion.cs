// SPDX-License-Identifier: GPL-2.0-only

// 08 — Custom per-member conversion (where the "lambda logic" lives).
// Other mappers inline an expression: `o.MapFrom(s => s.Total.ToString("C"))`. DwarfMapper can't carry a
// closure into an attribute, so you name a method with `Use =` — the method body is exactly where that logic
// goes. The method takes the source member type and returns the destination member type.
//   AutoMapper:  .ForMember(d => d.Total, o => o.MapFrom(s => FormatMoney(s.Total)))
//   DwarfMapper: [MapProperty(nameof(Order.Total), nameof(OrderDto.Total), Use = nameof(FormatMoney))]

using System.Globalization;

namespace DwarfMapper.Gallery.Ex08;

public sealed class Order
{
    public int Id { get; set; }
    public decimal Total { get; set; }
}

public sealed class OrderDto
{
    public int Id { get; set; }
    public string Total { get; set; } = "";
}

[DwarfMapper]
public partial class Mapper
{
    [MapProperty(nameof(Order.Total), nameof(OrderDto.Total), Use = nameof(FormatMoney))]
    public partial OrderDto ToDto(Order o);

    private static string FormatMoney(decimal d)
    {
        return d.ToString("C", CultureInfo.GetCultureInfo("en-US"));
    }
}

public static class Example
{
    public static void Run()
    {
        var dto = new Mapper().ToDto(new Order { Id = 5, Total = 1234.5m });
        Console.WriteLine($"08 Custom (Use=)      -> order {dto.Id} total {dto.Total}");
    }
}
