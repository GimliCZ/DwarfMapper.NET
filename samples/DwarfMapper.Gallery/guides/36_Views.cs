// SPDX-License-Identifier: GPL-2.0-only

// 36 — A zero-copy VIEW. [GenerateView<Customer, CustomerCard>] emits a nested `readonly ref struct
// CustomerCardView` on the mapper whose properties evaluate the same member resolution Map would — lazily, on
// access, against the customer you hand it. Nothing is allocated and nothing is copied.
//
// The `ref struct` is the contract, not a limitation. The compiler will not let a view be stored in a field,
// boxed, captured by a lambda or held across an `await`, so it cannot outlive the source it borrows — which
// is what makes reading straight through to a live object safe. CONSUME A VIEW AT ONCE, and use Map where the
// result is stored, returned or kept: serialize it, render it, compare it, and let it go.
//
// A view is a WINDOW, not a snapshot: mutate the customer afterwards and the view shows the new value. That
// follows from borrowing rather than copying, and it is the reason the type refuses to be stored.
//
// Measured against map-then-consume (Issues/round29/plan3-results.md): 0.20x at 1k, 0.04x at 100k, zero bytes
// allocated. The zero is the view's own — a converter it calls may still allocate on its own account (an
// int -> string edge builds a string), and it does so once per READ rather than once per map, so a view read
// many times can cost more than one Map.

namespace DwarfMapper.Gallery.Guides.G36
{
// <snippet: zero-copy-view>
    [DwarfMapper]
    [MapProperty<Customer, CustomerCard>(nameof(Customer.Total), nameof(CustomerCard.Total), Use = nameof(CustomerViews.Money))]
    [GenerateMap<Customer, CustomerCard>]
    [GenerateView<Customer, CustomerCard>]
    public partial class CustomerViews
    {
        // A converted member. The view calls this very method — the same one Map calls — and calls it on every
        // READ of row.Total rather than once per mapping, which is the half of "zero allocation" the headline
        // does not cover: the VIEW allocates nothing, a converter allocates whatever it allocates.
        internal string Money(decimal value)
        {
            return value.ToString("0.00", global::System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    internal static class Render
    {
        // The view is created, read and dropped inside one call. It never escapes, which is exactly what the
        // `ref struct` guarantees — try to return it, store it in a field or close over it and the build fails.
        internal static string Line(CustomerViews views, Customer customer)
        {
            var row = views.View(customer);
            return $"#{row.Id} {row.FullName} owes {row.Total}";
        }
    }
// </snippet>

    [DocExample(36,
        Tier.Guides,
        "Zero-copy views",
        Shows = "reading a DTO shape straight off the source, with no allocation and no copy")]
    public static class Example
    {
        public static void Run()
        {
            var views = new CustomerViews();
            var customer = new Customer
            {
                Id = 9,
                FullName = "Grace Hopper",
                Total = 12m,
                Address = new Address
                {
                    City = "New York",
                    Zip = "10001"
                }
            };

            var line = Render.Line(views, customer);

            // A window, not a snapshot: the same view reads the new value.
            var row = views.View(customer);
            customer.FullName = "Rear Admiral Hopper";

            Console.WriteLine($"36 Zero-copy view     -> {line}, now {row.FullName}");
        }
    }
}
