// SPDX-License-Identifier: GPL-2.0-only

// 35 — Injecting IDwarfMapper when the caller cannot name the concrete mapper: the map is declared in an
// assembly the consumer does not reference, or one service maps many pairs. This is the AutoMapper IMapper
// ergonomic, but the lookup is a Type-keyed dictionary of typed delegates — no member reflection, so it stays
// AOT- and trim-safe.
//
// Prefer the concrete mapper or the generated extension whenever both types are reachable: those are fully
// compile-checked, with no dictionary hop. Reach for the facade only for genuinely ambient resolution.

using Microsoft.Extensions.DependencyInjection;

namespace DwarfMapper.Gallery.Guides.G35;

[DwarfMapper]
[GenerateMap<Customer, CustomerSummary>]
public partial class AmbientMappers { }

// <snippet: ambient-facade>
public sealed class SettingsService(IDwarfMapper mapper)
{
    public CustomerSummary Summarise(Customer customer) => mapper.Map<CustomerSummary>(customer);
}
// </snippet>

[DocExample(35, Tier.Guides, "The ambient IDwarfMapper facade",
    Shows = "mapping when the caller cannot name the concrete mapper type")]
public static class Example
{
    public static void Run()
    {
        // AddDwarfMappers() registers IDwarfMapper (DwarfMapperFacade) along with every mapper in the assembly.
        using var provider = new ServiceCollection().AddDwarfMappers().BuildServiceProvider();

        var summary = new SettingsService(provider.GetRequiredService<IDwarfMapper>())
            .Summarise(new Customer
            {
                Id = 4,
                FullName = "Edsger Dijkstra",
                Total = 2m,
                Address = new Address { City = "Rotterdam", Zip = "3011" }
            });

        Console.WriteLine($"35 Ambient facade     -> #{summary.Id} {summary.FullName}");
    }
}
