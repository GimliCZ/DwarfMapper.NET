// SPDX-License-Identifier: GPL-2.0-only

// 48 — [ProvidesMap]: register a hand-written method as a real map.
// Some conversions are not a mapping SHAPE the generator can infer — a document that holds a collection,
// mapped to that collection. Writing it by hand is fine; what you lose is that it stops being a map anyone
// can resolve. [ProvidesMap] puts your method back in the registry, so callers reach it exactly as they
// reach a generated one.

namespace DwarfMapper.Gallery.Ex48
{
    public sealed class QuoteDocument
    {
        public List<Quote> Quotes { get; set; } = new();
    }

    public sealed class Quote
    {
        public string Text { get; set; } = "";
    }

    public sealed class QuoteDto
    {
        public string Text { get; set; } = "";
    }

// <snippet: provides-map>
    [DwarfMapper]
    public partial class Mapper
    {
        public partial QuoteDto ToQuote(Quote quote);

        // Not an inferable shape — a document mapped to the collection it holds. Declared as a provided map
        // so it is resolvable like any other.
        [ProvidesMap]
        public List<QuoteDto> ToQuotes(QuoteDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);
            return document.Quotes.ConvertAll(ToQuote);
        }
    }
// </snippet>

    [DocExample(48,
        Tier.Advanced,
        "Hand-written provided map",
        Shows = "registering your own method as a resolvable map")]
    public static class Example
    {
        public static void Run()
        {
            var dtos = new Mapper().ToQuotes(new QuoteDocument
            {
                Quotes =
                [
                    new Quote { Text = "Faithless is he that says farewell" },
                    new Quote { Text = "when the road darkens" }
                ]
            });
            Console.WriteLine($"48 Provided map       -> {dtos.Count} quotes, first: \"{dtos[0].Text}\"");
        }
    }
}
