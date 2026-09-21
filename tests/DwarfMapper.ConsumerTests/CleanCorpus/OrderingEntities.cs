// SPDX-License-Identifier: GPL-2.0-only

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CleanCorpus.Ordering.Persistence
{
    // ═══ The persistence layer ════════════════════════════════════════════════════════════════════════════════
// Entity Framework Core entities as an ordinary line-of-business project writes them: a surrogate key, the
// annotations the ORM reads, navigation properties in both directions, an address owned by the customer row,
// and a [Timestamp] concurrency token. This is the single most common thing a .NET mapper is ever pointed at
// — "entity in, DTO out" is most of what mapping libraries are downloaded for.

    public enum OrderStatus
    {
        Draft,

        Placed,

        Dispatched,

        Cancelled
    }

    [Table("orders")]
    public sealed class OrderEntity
    {
        [Key]
        public Guid Id { get; set; }

        public string Reference { get; set; } = "";

        public Guid CustomerId { get; set; }

        /// <summary>The navigation property. Read through dotted paths rather than mapped as an object.</summary>
        public CustomerEntity Customer { get; set; } = new();

        public List<OrderLineEntity> Lines { get; set; } = [];

        public OrderStatus Status { get; set; }

        public DateTimeOffset PlacedAt { get; set; }

        public DateTimeOffset? DispatchedAt { get; set; }

        public decimal FreightCharge { get; set; }

        /// <summary>
        ///     The optimistic-concurrency token. It belongs to the database and never to the wire, so every
        ///     read map has to leave it behind and every write map has to refuse to invent one.
        /// </summary>
        [Timestamp]
        public byte[] RowVersion { get; set; } = [];
    }

    [Table("order_lines")]
    public sealed class OrderLineEntity
    {
        [Key]
        public int Id { get; set; }

        public Guid OrderId { get; set; }

        public string Sku { get; set; } = "";

        public string Description { get; set; } = "";

        public int Quantity { get; set; }

        public decimal UnitPrice { get; set; }

        public decimal DiscountPercent { get; set; }
    }

    [Table("customers")]
    public sealed class CustomerEntity
    {
        [Key]
        public Guid Id { get; set; }

        public string DisplayName { get; set; } = "";

        public string EmailAddress { get; set; } = "";

        public string? TelephoneNumber { get; set; }

        public DateOnly RegisteredOn { get; set; }

        public PostalAddressEntity BillingAddress { get; set; } = new();

        public List<ContactEntity> Contacts { get; set; } = [];

        public Dictionary<string, string> Preferences { get; set; } = [];

        public string[] Segments { get; set; } = [];

        /// <summary>The other half of the navigation. Nothing maps it; it is here because EF models have it.</summary>
        public List<OrderEntity> Orders { get; set; } = [];
    }

    /// <summary>
    ///     The billing address, owned by the customer row rather than a table of its own. Configured as owned
    ///     in the context's model builder, which is where most projects put it.
    /// </summary>
    public sealed class PostalAddressEntity
    {
        public string Line1 { get; set; } = "";

        public string? Line2 { get; set; }

        public string Town { get; set; } = "";

        public string Postcode { get; set; } = "";

        public string CountryCode { get; set; } = "GB";
    }

    [Table("customer_contacts")]
    public sealed class ContactEntity
    {
        [Key]
        public int Id { get; set; }

        public Guid CustomerId { get; set; }

        public string Role { get; set; } = "";

        public string FullName { get; set; } = "";

        public string EmailAddress { get; set; } = "";
    }

    /// <summary>
    ///     A promotion that cannot exist in an invalid state: the constructor is the invariant, and the setters
    ///     that remain are the ones the database owns.
    /// </summary>
    public sealed class PromotionEntity
    {
        public PromotionEntity(string code, decimal percentOff)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);

            if (percentOff is <= 0m or > 90m)
            {
                throw new ArgumentOutOfRangeException(nameof(percentOff),
                    percentOff,
                    "A promotion discounts more than nothing and no more than ninety percent.");
            }

            Code = code;
            PercentOff = percentOff;
        }

        public int Id { get; set; }

        public string Code { get; }

        public decimal PercentOff { get; }

        public DateTimeOffset? ExpiresAt { get; set; }
    }

    /// <summary>
    ///     Reference data synced from the tax provider's feed and keyed by the provider's own identifier, so
    ///     the sync writes the key rather than the database. No <c>[Table]</c>: the convention name is right.
    /// </summary>
    public sealed class TaxRateEntity
    {
        [Key]
        public int Id { get; set; }

        public string Code { get; set; } = "";

        public string Description { get; set; } = "";

        public decimal Percent { get; set; }
    }

    /// <summary>One row of that feed, as the provider sends it.</summary>
    public sealed class TaxRateFeedRow
    {
        public int Id { get; set; }

        public string Code { get; set; } = "";

        public string Description { get; set; } = "";

        public decimal Percent { get; set; }
    }

    /// <summary>The shape a reporting query projects into: three columns, no key, no tracking.</summary>
    public sealed class OrderTotalsRow
    {
        public decimal Net { get; set; }

        public decimal Tax { get; set; }

        public decimal Gross { get; set; }
    }

    // ── The delivery hierarchy ───────────────────────────────────────────────────────────────────────────────
    // Every logistics integration has one: a step whose payload depends on what kind of step it is.

    public abstract class DeliveryStepEntity
    {
        public int Id { get; set; }

        public DateTimeOffset OccurredAt { get; set; }

        public string Location { get; set; } = "";
    }

    public sealed class PickupStepEntity : DeliveryStepEntity
    {
        public string CourierName { get; set; } = "";
    }

    public sealed class HandoverStepEntity : DeliveryStepEntity
    {
        public string SignedBy { get; set; } = "";
    }
}
