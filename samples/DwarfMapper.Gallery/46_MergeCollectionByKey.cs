// SPDX-License-Identifier: GPL-2.0-only

// 46 — [MapCollectionKey]: merge a list BY KEY instead of replacing it.
// On an update-into map, a collection member is normally rebuilt wholesale. That throws away the existing
// element instances, which matters when something else holds references to them — a change-tracking ORM, a
// UI bound to the list. Keyed merge keeps the LIST instance, updates matched elements in place, appends new
// keys, and leaves elements the update never mentioned exactly as they were — which is what makes a partial
// update expressible at all.

namespace DwarfMapper.Gallery.Ex46
{
    public sealed class Line
    {
        public int Sku { get; set; }

        public int Quantity { get; set; }
    }

    public sealed class Cart
    {
        public List<Line> Lines { get; set; } = new();
    }

    /// <summary>A partial update: it mentions only the lines it wants to change.</summary>
    public sealed class CartUpdate
    {
        public List<Line> Lines { get; set; } = new();
    }

// <snippet: merge-collection-by-key>
    [DwarfMapper]
    public partial class Mapper
    {
        [MapCollectionKey(nameof(Cart.Lines), nameof(Line.Sku))]
        public partial void Merge(CartUpdate source, Cart destination);
    }
// </snippet>

    [DocExample(46,
        Tier.Advanced,
        "Merge a collection by key",
        Shows = "updating list elements in place instead of rebuilding the list")]
    public static class Example
    {
        public static void Run()
        {
            var cart = new Cart
            {
                Lines =
                [
                    new Line { Sku = 1, Quantity = 1 },
                    new Line { Sku = 2, Quantity = 5 }
                ]
            };

            var listInstance = cart.Lines;
            var untouched = cart.Lines[0]; // sku 1 — the update never mentions it

            // Mentions sku 2 (matched) and sku 3 (new). Says nothing about sku 1.
            new Mapper().Merge(new CartUpdate
                {
                    Lines =
                    [
                        new Line { Sku = 2, Quantity = 9 },
                        new Line { Sku = 3, Quantity = 4 }
                    ]
                },
                cart);

            var sameList = ReferenceEquals(listInstance, cart.Lines);
            var keptSku1 = ReferenceEquals(untouched, cart.Lines.Single(l => l.Sku == 1));
            Console.WriteLine($"46 Keyed merge        -> {cart.Lines.Count} lines; list kept: {sameList}; "
                + $"sku 1 untouched: {keptSku1}; sku 2 now {cart.Lines.Single(l => l.Sku == 2).Quantity}");
        }
    }
}
