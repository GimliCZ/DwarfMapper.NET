// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;
using Xunit;

namespace RegistryProto;

// Proves the [MapTo] front door end-to-end: no user `partial`, called as an extension. Member directives
// are stacked and read in source order, each aligned to the [MapTo] target at the same index.

public class OrderDto
{
    public string Name { get; set; } = "";
    public decimal Total { get; set; }
}

public class OrderSummary
{
    public string FullName { get; set; } = "";
    public decimal Total { get; set; }
}

[MapTo(typeof(OrderDto), typeof(OrderSummary))]
public class Order
{
    // attribute 0 → target 0 (OrderDto.Name); attribute 1 → target 1 (OrderSummary.FullName).
    [MapProperty("Name"), MapProperty("FullName")]
    public string FullName { get; set; } = "";

    public decimal Total { get; set; }

    [MapIgnore] // single directive → ignored in every target
    public string Secret { get; set; } = "";
}

// Per-target map/ignore mixing, order-sensitive. Two source members feed the SAME destination "Name"
// but each in a different target.
public class PName { public string Name { get; set; } = ""; }
public class QName { public string Name { get; set; } = ""; }

[MapTo(typeof(PName), typeof(QName))]
public class Aliased
{
    [MapProperty("Name"), MapIgnore]   // PName.Name ← Formal ; QName: Formal ignored
    public string Formal { get; set; } = "";

    [MapIgnore, MapProperty("Name")]   // PName: Casual ignored ; QName.Name ← Casual
    public string Casual { get; set; } = "";
}

public class RegistryMapToPrototypeRuntimeTests
{
    [Fact]
    public void Positional_rename_maps_each_target_to_its_slot()
    {
        var order = new Order { FullName = "Ada Lovelace", Total = 42.5m, Secret = "hunter2" };

        OrderDto dto = order.MapTo<OrderDto>();          // generic dispatch, no instance, no partial
        OrderSummary summary = order.ToOrderSummary();   // named per-target extension

        Assert.Equal("Ada Lovelace", dto.Name);
        Assert.Equal(42.5m, dto.Total);
        Assert.Equal("Ada Lovelace", summary.FullName);
        Assert.Equal(42.5m, summary.Total);
    }

    [Fact]
    public void Generic_dispatch_resolves_both_targets()
    {
        var order = new Order { FullName = "Grace", Total = 7m };

        Assert.Equal("Grace", order.MapTo<OrderDto>().Name);
        Assert.Equal("Grace", order.MapTo<OrderSummary>().FullName);
    }

    [Fact]
    public void Mixed_map_ignore_is_positional_and_order_sensitive()
    {
        var a = new Aliased { Formal = "Dr. Hopper", Casual = "Grace" };

        // Target 0 (PName): Formal mapped, Casual ignored → Name = Formal.
        Assert.Equal("Dr. Hopper", a.MapTo<PName>().Name);

        // Target 1 (QName): Formal ignored, Casual mapped → Name = Casual.
        Assert.Equal("Grace", a.MapTo<QName>().Name);
    }
}
