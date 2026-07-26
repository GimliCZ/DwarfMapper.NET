// SPDX-License-Identifier: GPL-2.0-only

// 30 — One mapper doing three things at once: a rename, a custom scalar conversion, and a flatten.
// This is the shape a migrating reader arrives with, and the shape the README and the AutoMapper guide use.
// Examples 02, 07 and 08 each show one of these in isolation; this shows them composed, which is the part
// that actually looks intimidating in someone else's codebase.

using System.Globalization;

namespace DwarfMapper.Gallery.Guides.G30;

// <snippet: composite-mapper>
[DwarfMapper]
public partial class CustomerMapper
{
    [MapProperty(nameof(Customer.FullName), nameof(CustomerDto.Name))]                           // rename
    [MapProperty(nameof(Customer.Total), nameof(CustomerDto.Total), Use = nameof(FormatMoney))]  // conversion
    [Flatten(nameof(Customer.Address))]                                                          // Address.City -> City
    public partial CustomerDto ToDto(Customer src);

    private static string FormatMoney(decimal d) => d.ToString("C", CultureInfo.GetCultureInfo("en-US"));
}
// </snippet>

[DocExample(30, Tier.Guides, "A composite mapper",
    Shows = "rename, `Use=` conversion, and `[Flatten]` in one mapper")]
public static class Example
{
    public static void Run()
    {
        var dto = new CustomerMapper().ToDto(new Customer
        {
            Id = 1,
            FullName = "Ada Lovelace",
            Total = 12.5m,
            Address = new Address { City = "London", Zip = "NW1" }
        });

        Console.WriteLine($"30 Composite mapper   -> {dto.Name}, {dto.Total}, {dto.City} {dto.Zip}");
    }
}
