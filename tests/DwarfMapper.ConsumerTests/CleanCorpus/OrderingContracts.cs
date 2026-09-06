// SPDX-License-Identifier: GPL-2.0-only

namespace CleanCorpus.Ordering.Contracts
{
    // ═══ The wire ═════════════════════════════════════════════════════════════════════════════════════════════
// Request and response types for an order API, written the way the ASP.NET Core template and every tutorial
// after it writes them: init-only responses, a request per verb, and an envelope around everything that
// leaves the process.

    // ── Read side ────────────────────────────────────────────────────────────────────────────────────────────

    public sealed class OrderResponse
    {
        public Guid Id { get; init; }

        public string Reference { get; init; } = "";

        /// <summary>Flattened from the customer navigation property.</summary>
        public string CustomerName { get; init; } = "";

        public string CustomerEmail { get; init; } = "";

        public string BillingTown { get; init; } = "";

        /// <summary>The status as text, because a wire contract does not ship an integer nobody can read.</summary>
        public string Status { get; init; } = "";

        public DateTimeOffset PlacedAt { get; init; }

        public DateTimeOffset? DispatchedAt { get; init; }

        public decimal FreightCharge { get; init; }

        public IReadOnlyList<OrderLineResponse> Lines { get; init; } = [];
    }

    public sealed class OrderLineResponse
    {
        public string Sku { get; init; } = "";

        public string Description { get; init; } = "";

        public int Quantity { get; init; }

        public decimal UnitPrice { get; init; }

        public decimal DiscountPercent { get; init; }
    }

    // ── Write side ───────────────────────────────────────────────────────────────────────────────────────────

    public sealed class PlaceOrderRequest
    {
        public string Reference { get; set; } = "";

        public Guid CustomerId { get; set; }

        public decimal FreightCharge { get; set; }

        public IReadOnlyList<OrderLineRequest> Lines { get; set; } = [];
    }

    public sealed class OrderLineRequest
    {
        public string Sku { get; set; } = "";

        public string Description { get; set; } = "";

        public int Quantity { get; set; }

        public decimal UnitPrice { get; set; }

        public decimal DiscountPercent { get; set; }
    }

    public sealed class CreatePromotionRequest
    {
        public string Code { get; set; } = "";

        public decimal PercentOff { get; set; }

        public DateTimeOffset? ExpiresAt { get; set; }
    }

    // ── Envelopes ────────────────────────────────────────────────────────────────────────────────────────────
    // Three of them, because applications accumulate three of them: one for the application layer's own
    // success-or-failure, one for the HTTP body, and one for a page of results.

    /// <summary>The application layer's return type — a payload or a reason it is absent.</summary>
    public sealed class Result<T>
        where T : class
    {
        public T Value { get; set; } = default!;

        public string? Error { get; set; }
    }

    /// <summary>The HTTP body every endpoint on this API returns.</summary>
    public sealed class ApiResponse<T>
        where T : class
    {
        public T Data { get; set; } = default!;

        public string TraceId { get; set; } = "";

        public DateTimeOffset GeneratedAt { get; set; }
    }

    /// <summary>A page of results. The payload is a collection, which is what makes it the awkward one.</summary>
    public sealed class PagedResult<T>
        where T : class
    {
        public IReadOnlyList<T> Items { get; set; } = [];

        public int Page { get; set; }

        public int PageSize { get; set; }

        public int TotalCount { get; set; }
    }

    // ── The customer profile: every collection shape the engine supports, on one response ────────────────────

    public sealed class CustomerProfileResponse
    {
        public Guid Id { get; init; }

        public string DisplayName { get; init; } = "";

        public string EmailAddress { get; init; } = "";

        public string? TelephoneNumber { get; init; }

        public DateOnly RegisteredOn { get; init; }

        public string BillingTown { get; init; } = "";

        public string BillingCountryCode { get; init; } = "";

        /// <summary>An array — what a schema generator emits and what JSON deserialises into.</summary>
        public string[] Segments { get; init; } = [];

        /// <summary>Read-only over the elements — the contract idiom current C# reaches for.</summary>
        public IReadOnlyList<ContactResponse> Contacts { get; init; } = [];

        /// <summary>A <c>List&lt;T&gt;</c> of DTOs, the shape most application code actually holds.</summary>
        public List<OrderSummaryResponse> RecentOrders { get; init; } = [];

        /// <summary>A dictionary of scalars: user preferences, feature flags, tenant settings.</summary>
        public Dictionary<string, string> Preferences { get; init; } = [];
    }

    public sealed class ContactResponse
    {
        public string Role { get; init; } = "";

        public string FullName { get; init; } = "";

        public string EmailAddress { get; init; } = "";
    }

    public sealed class OrderSummaryResponse
    {
        public Guid Id { get; init; }

        public string Reference { get; init; } = "";

        public string Status { get; init; } = "";

        public DateTimeOffset PlacedAt { get; init; }
    }

    // ── Modern C# DTO idiom, used as the target ──────────────────────────────────────────────────────────────

    /// <summary>A positional record: the shortest correct way to declare a response type in current C#.</summary>
    public sealed record CustomerSummary(Guid Id, string DisplayName, string EmailAddress);

    /// <summary>
    ///     A primary-constructor class with computed-once totals. Common where a response needs a constructor
    ///     but a record's value equality is not wanted.
    /// </summary>
    public sealed class OrderTotals(decimal net, decimal tax, decimal gross)
    {
        public decimal Net { get; } = net;

        public decimal Tax { get; } = tax;

        public decimal Gross { get; } = gross;
    }

    /// <summary>
    ///     A <c>readonly record struct</c> element — the shape this round argues people should reach for, and
    ///     the one a collection of DTOs costs least in.
    /// </summary>
    public readonly record struct MoneyLine(string Sku, int Quantity, decimal UnitPrice);

    /// <summary>
    ///     The annotated view of a customer record that comes out of the un-annotated legacy assembly. The
    ///     migration writes the new type properly and leaves the old one alone, which is what puts an oblivious
    ///     source and an annotated target on the two ends of one map.
    /// </summary>
    public sealed class LegacyCustomerView
    {
        public string CustomerNumber { get; init; } = "";

        public string Name { get; init; } = "";

        public string? Email { get; init; }

        public string? Phone { get; init; }

        public DateTime? LastOrderedOn { get; init; }

        public IReadOnlyList<LegacyLineDto> Lines { get; init; } = [];
    }

    public sealed class LegacyLineDto
    {
        public string ProductCode { get; init; } = "";

        public int Quantity { get; init; }

        public decimal LinePrice { get; init; }
    }

    // ── Nullable-heavy: the shape an optional-everything integration produces ────────────────────────────────
    // A company-data provider answers with whatever it happens to know, so every member but the key is
    // optional on both sides. Nothing here is a five-field fixture's `string? Maybe`.

    public sealed class CustomerEnrichmentRecord
    {
        public Guid Id { get; set; }

        public string? LegalName { get; set; }

        public string? TradingName { get; set; }

        public string? RegistrationNumber { get; set; }

        public string? VatNumber { get; set; }

        public int? EmployeeCount { get; set; }

        public decimal? AnnualTurnover { get; set; }

        public DateOnly? IncorporatedOn { get; set; }

        public DateTimeOffset? LastVerifiedAt { get; set; }

        public bool? IsPubliclyListed { get; set; }

        public string? IndustryCode { get; set; }
    }

    public sealed class CustomerEnrichmentResponse
    {
        public Guid Id { get; init; }

        public string? LegalName { get; init; }

        public string? TradingName { get; init; }

        public string? RegistrationNumber { get; init; }

        public string? VatNumber { get; init; }

        public int? EmployeeCount { get; init; }

        public decimal? AnnualTurnover { get; init; }

        public DateOnly? IncorporatedOn { get; init; }

        public DateTimeOffset? LastVerifiedAt { get; init; }

        public bool? IsPubliclyListed { get; init; }

        public string? IndustryCode { get; init; }
    }

    // ── The flat integration row, at the width a real integration has ────────────────────────────────────────
    // A card acquirer's settlement file. Thirty members, most of them name-matched, a few converted — which is
    // exactly the case a mapper is bought for and exactly the case a five-field fixture cannot stand in for.

    public enum PayoutStatus
    {
        Pending,

        Paid,

        Held,

        Reversed
    }

    public sealed class SettlementRecord
    {
        public string MerchantId { get; set; } = "";

        public string SettlementBatchId { get; set; } = "";

        public string TransactionId { get; set; } = "";

        public string AuthorisationCode { get; set; } = "";

        public string CardScheme { get; set; } = "";

        public string MaskedPan { get; set; } = "";

        public string CardCountryCode { get; set; } = "";

        public string TerminalId { get; set; } = "";

        public string StoreCode { get; set; } = "";

        public string OrderReference { get; set; } = "";

        public decimal GrossAmount { get; set; }

        public decimal SchemeFee { get; set; }

        public decimal InterchangeFee { get; set; }

        public decimal AcquirerFee { get; set; }

        public decimal NetAmount { get; set; }

        public string CurrencyCode { get; set; } = "";

        public string SettlementCurrencyCode { get; set; } = "";

        public decimal ExchangeRate { get; set; }

        public DateTimeOffset CapturedAt { get; set; }

        public DateOnly SettledOn { get; set; }

        public DateOnly ValueDate { get; set; }

        public bool IsRefund { get; set; }

        public bool IsChargeback { get; set; }

        public string? ChargebackReason { get; set; }

        public string? RefundOfTransactionId { get; set; }

        public int InstalmentCount { get; set; }

        public int InstalmentNumber { get; set; }

        public string PayoutBatchReference { get; set; } = "";

        public PayoutStatus PayoutStatus { get; set; }

        public string? ProcessorMessage { get; set; }
    }

    public sealed class SettlementRowDto
    {
        public string MerchantId { get; init; } = "";

        public string SettlementBatchId { get; init; } = "";

        public string TransactionId { get; init; } = "";

        public string AuthorisationCode { get; init; } = "";

        public string CardScheme { get; init; } = "";

        public string MaskedPan { get; init; } = "";

        public string CardCountryCode { get; init; } = "";

        public string TerminalId { get; init; } = "";

        public string StoreCode { get; init; } = "";

        public string OrderReference { get; init; } = "";

        public decimal GrossAmount { get; init; }

        public decimal SchemeFee { get; init; }

        public decimal InterchangeFee { get; init; }

        public decimal AcquirerFee { get; init; }

        public decimal NetAmount { get; init; }

        public string CurrencyCode { get; init; } = "";

        public string SettlementCurrencyCode { get; init; } = "";

        public decimal ExchangeRate { get; init; }

        public DateTimeOffset CapturedAt { get; init; }

        public DateOnly SettledOn { get; init; }

        public DateOnly ValueDate { get; init; }

        public bool IsRefund { get; init; }

        public bool IsChargeback { get; init; }

        public string? ChargebackReason { get; init; }

        public string? RefundOfTransactionId { get; init; }

        public int InstalmentCount { get; init; }

        public int InstalmentNumber { get; init; }

        public string PayoutBatchReference { get; init; } = "";

        /// <summary>The enum as text — the one member on this row that is not a straight copy.</summary>
        public string PayoutStatus { get; init; } = "";

        public string? ProcessorMessage { get; init; }
    }

    // ── The delivery hierarchy, on the wire ──────────────────────────────────────────────────────────────────

    public abstract class DeliveryStepView
    {
        public DateTimeOffset OccurredAt { get; init; }

        public string Location { get; init; } = "";
    }

    public sealed class PickupStepView : DeliveryStepView
    {
        public string CourierName { get; init; } = "";
    }

    public sealed class HandoverStepView : DeliveryStepView
    {
        public string SignedBy { get; init; } = "";
    }
}
