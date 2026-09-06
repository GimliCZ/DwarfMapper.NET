// SPDX-License-Identifier: GPL-2.0-only

using System.ComponentModel.DataAnnotations;
using CleanCorpus.Ordering.Contracts;
using CleanCorpus.Ordering.Legacy;
using CleanCorpus.Ordering.Persistence;
using CleanCorpus.Ordering.Presentation;
using DwarfMapper;

namespace CleanCorpus.Ordering.Mapping
{
    // ═══ The maps ═════════════════════════════════════════════════════════════════════════════════════════════
// One mapper class per slice of the application, which is how a project this size ends up organised: the read
// side, the write side, the envelopes, the reports, the presentation layer, and the corner where the legacy
// assembly is still un-annotated.

    /// <summary>
    ///     Entity to API response — the dominant map in .NET line-of-business code — and the envelopes the
    ///     endpoints wrap it in. <c>Result&lt;T&gt;</c> and <c>ApiResponse&lt;T&gt;</c> carry a single payload,
    ///     so one <c>[GenerateWrapperMap]</c> each covers every pair on the class; <c>PagedResult&lt;T&gt;</c>
    ///     holds a collection instead, which is not a single payload, so its instantiation is declared by hand.
    /// </summary>
    [DwarfMapper(AllowNonPublic = true)]
    [GenerateWrapperMap(typeof(Result<>))]
    [GenerateWrapperMap(typeof(ApiResponse<>))]
    [GenerateMap<PagedResult<OrderEntity>, PagedResult<OrderResponse>>]
    public partial class OrderReadMappers
    {
        [MapProperty(nameof(OrderEntity.Customer) + "." + nameof(CustomerEntity.DisplayName),
            nameof(OrderResponse.CustomerName))]
        [MapProperty(nameof(OrderEntity.Customer) + "." + nameof(CustomerEntity.EmailAddress),
            nameof(OrderResponse.CustomerEmail))]
        [MapProperty(
            nameof(OrderEntity.Customer) + "." + nameof(CustomerEntity.BillingAddress) + "." +
            nameof(PostalAddressEntity.Town),
            nameof(OrderResponse.BillingTown))]
        public partial OrderResponse ToResponse(OrderEntity source);

        public partial OrderSummaryResponse ToSummary(OrderEntity source);

        public partial Result<OrderResponse> ToResult(Result<OrderEntity> source);

        public partial ApiResponse<OrderResponse> ToEnvelope(ApiResponse<OrderEntity> source);
    }

    /// <summary>
    ///     The write side. Everything the server owns — the key, the status, the timestamps and the
    ///     concurrency token — is refused explicitly, which is the point: a request cannot set them, and the
    ///     build says so rather than the request quietly winning.
    /// </summary>
    [DwarfMapper]
    [GenerateMap<OrderLineRequest, OrderLineEntity>]
    [MapIgnore<OrderLineEntity>(nameof(OrderLineEntity.Id))]
    [MapIgnore<OrderLineEntity>(nameof(OrderLineEntity.OrderId))]
    public partial class OrderWriteMappers
    {
        [MapIgnore(nameof(OrderEntity.Id))]
        [MapIgnore(nameof(OrderEntity.Customer))]
        [MapIgnore(nameof(OrderEntity.Status))]
        [MapIgnore(nameof(OrderEntity.PlacedAt))]
        [MapIgnore(nameof(OrderEntity.DispatchedAt))]
        [MapIgnore(nameof(OrderEntity.RowVersion))]
        public partial OrderEntity ToEntity(PlaceOrderRequest source);

        /// <summary>
        ///     The validator's answer, handed to the framework in the framework's own type. That type is
        ///     declared in a referenced assembly rather than in this project — the ordinary case of mapping
        ///     into something you did not write.
        /// </summary>
        public partial List<ValidationResult> ToValidationResults(IReadOnlyList<ValidationFailure> source);
    }

    /// <summary>
    ///     The promotion import endpoint: a batch of requests in, entities out. The entity validates in its
    ///     constructor, so the mapper has to go through it.
    /// </summary>
    [DwarfMapper]
    [GenerateMap<CreatePromotionRequest, PromotionEntity>]
    [MapIgnore<PromotionEntity>(nameof(PromotionEntity.Id))]
    public partial class PromotionMappers
    {
        public partial List<PromotionEntity> ToEntities(IReadOnlyList<CreatePromotionRequest> source);

        /// <summary>The hop back out, closing request → entity → response over the validating type.</summary>
        public partial PromotionResponse ToResponse(PromotionEntity source);
    }

    /// <summary>
    ///     The nightly tax-rate sync. The feed owns the key, so the whole row copies across by name and
    ///     nothing is configured — the shape where a mapper costs one declaration and nothing else.
    /// </summary>
    [DwarfMapper]
    public partial class ReferenceDataMappers
    {
        public partial List<TaxRateEntity> ToEntities(IReadOnlyList<TaxRateFeedRow> source);
    }

    /// <summary>
    ///     The customer profile endpoint: an array, a read-only list of DTOs, a list of DTOs and a dictionary,
    ///     on one response.
    /// </summary>
    [DwarfMapper]
    public partial class CustomerProfileMappers
    {
        [MapProperty(nameof(CustomerEntity.BillingAddress) + "." + nameof(PostalAddressEntity.Town),
            nameof(CustomerProfileResponse.BillingTown))]
        [MapProperty(nameof(CustomerEntity.BillingAddress) + "." + nameof(PostalAddressEntity.CountryCode),
            nameof(CustomerProfileResponse.BillingCountryCode))]
        [MapProperty(nameof(CustomerEntity.Orders), nameof(CustomerProfileResponse.RecentOrders))]
        public partial CustomerProfileResponse ToProfile(CustomerEntity source);

        /// <summary>The list endpoint's row — a positional record, which is how it would be written today.</summary>
        public partial CustomerSummary ToSummary(CustomerEntity source);

        /// <summary>The enrichment provider's answer: a key and ten members it may or may not know.</summary>
        public partial CustomerEnrichmentResponse ToEnrichment(CustomerEnrichmentRecord source);
    }

    /// <summary>
    ///     Reporting. A primary-constructor class from a query projection, and a price breakdown whose element
    ///     is a <c>readonly record struct</c> — the shape a collection of values costs least in.
    /// </summary>
    [DwarfMapper]
    public partial class ReportingMappers
    {
        public partial OrderTotals ToTotals(OrderTotalsRow source);

        public partial List<MoneyLine> ToMoneyLines(List<OrderLineEntity> source);

        /// <summary>The settlement file export: thirty members, one of them converted.</summary>
        public partial List<SettlementRowDto> ToRows(IReadOnlyList<SettlementRecord> source);
    }

    /// <summary>The delivery timeline: one step type per event, dispatched on the runtime type.</summary>
    [DwarfMapper]
    public partial class DeliveryMappers
    {
        [MapDerivedType<PickupStepEntity, PickupStepView>]
        [MapDerivedType<HandoverStepEntity, HandoverStepView>]
        public partial DeliveryStepView ToView(DeliveryStepEntity source);

        public partial PickupStepView ToPickupView(PickupStepEntity source);

        public partial HandoverStepView ToHandoverView(HandoverStepEntity source);

        public partial List<DeliveryStepView> ToTimeline(List<DeliveryStepEntity> source);
    }

    /// <summary>
    ///     The presentation layer, where the targets are not transfer models: a view with a computed total, a
    ///     grid row that raises change notifications, and a message that owns the streams it carries.
    /// </summary>
    [DwarfMapper]
    public partial class PresentationMappers
    {
        public partial List<InvoiceLineView> ToInvoiceLines(List<OrderLineEntity> source);

        public partial OrderRowViewModel ToRow(OrderResponse source);

        public partial List<OrderRowViewModel> ToRows(List<OrderResponse> source);

        public partial List<OutboundEmail> ToOutbound(List<EmailNotificationRequest> source);

        public partial List<PriceWatch> ToWatches(List<PriceWatchRequest> source);
    }

    /// <summary>
    ///     The boundary with the un-annotated assembly. The source members are oblivious — neither nullable
    ///     nor non-nullable — and the target's are annotated, which is the state every half-migrated codebase
    ///     is in.
    /// </summary>
    [DwarfMapper]
    public partial class LegacyMappers
    {
        public partial LegacyCustomerView ToView(LegacyCustomerRecord source);

        public partial LegacyCustomerRecord ToRecord(LegacyCustomerView source);
    }
}
