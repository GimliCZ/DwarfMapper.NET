// SPDX-License-Identifier: GPL-2.0-only

// 06 — Deep property access ("lambda territory").
// This is what AutoMapper/Mapster express with a lambda: `o.MapFrom(s => s.Customer.Address.City)`.
// DwarfMapper reads through the graph with a dotted *string path* instead — there is no closure to carry into
// an attribute, so the path is a literal. The leaf goes through the same conversion rules as any member.
//   AutoMapper:  .ForMember(d => d.City, o => o.MapFrom(s => s.Customer.Address.City))
//   DwarfMapper: [MapProperty("Customer.Address.City", nameof(OrderSummary.City))]
// A nullable hop on the path surfaces DWARF044 (a visible suggestion), so it's never a silent NRE.
using System;
using DwarfMapper;

namespace DwarfMapper.Gallery.Ex06;

public sealed class Address { public string City { get; set; } = ""; }
public sealed class Customer { public string Name { get; set; } = ""; public Address Address { get; set; } = new(); }
public sealed class Order { public int Id { get; set; } public Customer Customer { get; set; } = new(); }

public sealed class OrderSummary
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = "";
    public string City { get; set; } = "";
}

[DwarfMapper]
public partial class Mapper
{
    [MapProperty("Customer.Name", nameof(OrderSummary.CustomerName))]
    [MapProperty("Customer.Address.City", nameof(OrderSummary.City))]
    public partial OrderSummary ToSummary(Order o);
}

public static class Example
{
    public static void Run()
    {
        OrderSummary summary = new Mapper().ToSummary(new Order
        {
            Id = 99,
            Customer = new Customer { Name = "Balin", Address = new Address { City = "Moria" } },
        });
        Console.WriteLine($"06 Deep paths         -> #{summary.Id}: {summary.CustomerName} of {summary.City}");
    }
}
