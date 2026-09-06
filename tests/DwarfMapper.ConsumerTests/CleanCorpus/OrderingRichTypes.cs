// SPDX-License-Identifier: GPL-2.0-only

using System.ComponentModel;

namespace CleanCorpus.Ordering.Presentation
{
    // ═══ The types that are not transfer models ═══════════════════════════════════════════════════════════════
// Every application maps into types that carry more than data: a view model that raises change notifications,
// something that owns a resource, a view with a computed total, a polymorphic step. They are ordinary code
// and they are mapped as often as DTOs are — so the corpus maps them, in collections, and the report says
// what the engine had to say about them.

    /// <summary>An invoice line as the print view wants it: the stored numbers plus the one it derives.</summary>
    public sealed class InvoiceLineView
    {
        public string Sku { get; init; } = "";

        public int Quantity { get; init; }

        public decimal UnitPrice { get; init; }

        public decimal LineTotal => Quantity * UnitPrice;
    }

    /// <summary>
    ///     A grid row. Bound to by the UI, so it raises change notifications — the reason a view model is a
    ///     class with an event rather than a record.
    /// </summary>
    public sealed class OrderRowViewModel : INotifyPropertyChanged
    {
        private string _status = "";

        public Guid Id { get; set; }

        public string Reference { get; set; } = "";

        public string Status
        {
            get => _status;

            set
            {
                if (string.Equals(_status, value, StringComparison.Ordinal))
                {
                    return;
                }

                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed class PriceWatchRequest
    {
        public string Sku { get; set; } = "";

        public decimal Threshold { get; set; }
    }

    public sealed class PriceBreachedEventArgs(decimal price) : EventArgs
    {
        public decimal Price { get; } = price;
    }

    /// <summary>
    ///     A standing price watch. It raises an event when the threshold is crossed and implements no
    ///     interface to do it — a plain <c>event</c> is all a subscriber needs.
    /// </summary>
    public sealed class PriceWatch
    {
        public string Sku { get; set; } = "";

        public decimal Threshold { get; set; }

        public event EventHandler<PriceBreachedEventArgs>? Breached;

        public void Observe(decimal price)
        {
            if (price <= Threshold)
            {
                Breached?.Invoke(this, new PriceBreachedEventArgs(price));
            }
        }
    }

    public sealed class EmailNotificationRequest
    {
        public string To { get; set; } = "";

        public string Subject { get; set; } = "";

        public string Body { get; set; } = "";
    }

    /// <summary>
    ///     A message on its way out. It owns the streams behind its attachments, so it owns a lifetime — which
    ///     is why the queue drain disposes every one it built.
    /// </summary>
    public sealed class OutboundEmail : IDisposable
    {
        private readonly List<MemoryStream> _attachments = [];

        public string To { get; set; } = "";

        public string Subject { get; set; } = "";

        public string Body { get; set; } = "";

        public IReadOnlyList<Stream> Attachments => _attachments;

        public void Dispose()
        {
            foreach (var attachment in _attachments)
            {
                attachment.Dispose();
            }

            _attachments.Clear();
        }

        public void Attach(byte[] content)
        {
            _attachments.Add(new MemoryStream(content));
        }
    }
}
