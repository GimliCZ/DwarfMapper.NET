// SPDX-License-Identifier: GPL-2.0-only

// 04 — Nested objects (auto-nesting).
// Map the outer pair only; the generator synthesizes the nested Address -> AddressDto mapper for you
// (auto-nesting is on by default). No need to declare every nested pair.
using System;
using DwarfMapper;

namespace DwarfMapper.Gallery.Ex04;

public sealed class Address { public string City { get; set; } = ""; public string Country { get; set; } = ""; }
public sealed class AddressDto { public string City { get; set; } = ""; public string Country { get; set; } = ""; }

public sealed class Order { public int Id { get; set; } public Address ShipTo { get; set; } = new(); }
public sealed class OrderDto { public int Id { get; set; } public AddressDto ShipTo { get; set; } = new(); }

[DwarfMapper]
public partial class Mapper
{
    public partial OrderDto ToDto(Order o);   // ShipTo (Address -> AddressDto) auto-nested
}

public static class Example
{
    public static void Run()
    {
        OrderDto dto = new Mapper().ToDto(new Order { Id = 12, ShipTo = new Address { City = "Erebor", Country = "Under the Mountain" } });
        Console.WriteLine($"04 Nested object      -> order {dto.Id}, ships to {dto.ShipTo.City}");
    }
}
