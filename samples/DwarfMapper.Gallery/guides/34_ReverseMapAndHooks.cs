// SPDX-License-Identifier: GPL-2.0-only

// 34 — Three features the migration guides reach for that no earlier example covers: an inverse map declared
// once, a dependency reaching a converter, and an imperative tail for what you cannot say declaratively.
//
// Each mapper targets a different type on purpose where it has to. Two STATELESS mappers providing the same
// (source, target) pair is DWARF063 — a Warning, and warnings are errors here — so StampedOrderMapper uses
// OrderReceipt. RatedOrderMapper may share OrderDto because a mapper with constructor dependencies is not
// ambient-registered at all (DWARF062, Info).

namespace DwarfMapper.Gallery.Guides.G34;

/// <summary>A dependency a converter needs — the thing a static method cannot reach.</summary>
public interface IRateService
{
    decimal Convert(decimal amount);
}

public sealed class DoublingRates : IRateService
{
    public decimal Convert(decimal amount) => amount * 2;
}

// <snippet: reverse-map>
[DwarfMapper]
public partial class ReversibleOrderMapper
{
    [ReverseMap]
    [MapProperty(nameof(Order.FullName), nameof(OrderDto.Name))]
    [MapIgnore(nameof(OrderDto.Source))]
    public partial OrderDto ToDto(Order o);

    public partial Order FromDto(OrderDto d);   // inherits the inverted Name -> FullName rename
}
// </snippet>

// <snippet: ctor-injection>
[DwarfMapper]
public partial class RatedOrderMapper(IRateService rates)   // primary constructor
{
    [MapProperty(nameof(Order.FullName), nameof(OrderDto.Name))]
    [MapProperty(nameof(Order.Total), nameof(OrderDto.Total), Use = nameof(ToLocal))]
    [MapValue(nameof(OrderDto.Source), "api-v2")]
    public partial OrderDto ToDto(Order o);

    private decimal ToLocal(decimal amount) => rates.Convert(amount);
}
// </snippet>

[DwarfMapper]
public partial class StampedOrderMapper
{
    [MapProperty(nameof(Order.FullName), nameof(OrderReceipt.Name))]
    [MapIgnore(nameof(OrderReceipt.Source))]
    public partial OrderReceipt ToReceipt(Order o);

    // <snippet: after-map-hook>
    [AfterMap]  // the imperative tail you couldn't express declaratively
    private static void Stamp(Order o, OrderReceipt r) => r.Source = $"api-v2/{o.Id}";
    // </snippet>
}

[DocExample(34, Tier.Guides, "Inverse maps, injected dependencies, and hooks",
    Shows = "`[ReverseMap]`, a primary-constructor dependency in a `Use=` converter, and `[AfterMap]`")]
public static class Example
{
    public static void Run()
    {
        var reversible = new ReversibleOrderMapper();
        var order = new Order { Id = 1, FullName = "Ada", Total = 5m };
        var back = reversible.FromDto(reversible.ToDto(order));

        var rated = new RatedOrderMapper(new DoublingRates())
            .ToDto(new Order { Id = 2, FullName = "Grace", Total = 21m });

        var stamped = new StampedOrderMapper().ToReceipt(new Order { Id = 9, FullName = "Alan", Total = 1m });

        Console.WriteLine(
            $"34 Reverse/ctor/hook  -> round-trip {back.FullName}, rated {rated.Total}, {stamped.Source}");
    }
}
