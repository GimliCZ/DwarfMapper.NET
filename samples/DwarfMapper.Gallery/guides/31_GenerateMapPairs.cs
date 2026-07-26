// SPDX-License-Identifier: GPL-2.0-only

// 31 — Declaring several pairs on one class with [GenerateMap], no method per pair. This is the shape an
// AutoMapper CreateMap profile becomes.
//
// The Map overload is resolved by the SOURCE type, which is why one class cannot map one source to two
// targets — declare the second pair on another class. Both pairs here need no configuration because every
// destination member pairs by name; Customer.Address is simply not consumed, which the default
// RequiredMapping = Target permits.

namespace DwarfMapper.Gallery.Guides.G31;

// <snippet: generate-map-pairs>
[DwarfMapper]
[GenerateMap<Order, OrderRow>]
[GenerateMap<Customer, CustomerRow>]
public partial class Mappers { }
// </snippet>

[DocExample(31, Tier.Guides, "Several pairs on one class",
    Shows = "`[GenerateMap<A,B>]` stacked — the AutoMapper `CreateMap` shape")]
public static class Example
{
    public static void Run()
    {
        var mappers = new Mappers();

        // The overload is picked by the source type — no cast, no generic argument at the call site.
        var order = mappers.Map(new Order { Id = 7, FullName = "Grace Hopper", Total = 3m });
        var customer = mappers.Map(new Customer { Id = 8, FullName = "Katherine Johnson", Total = 9m });

        Console.WriteLine($"31 GenerateMap pairs  -> {order.FullName} ({order.Total}), {customer.FullName}");
    }
}
