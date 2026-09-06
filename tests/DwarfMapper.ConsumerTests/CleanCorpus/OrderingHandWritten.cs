// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using CleanCorpus.Ordering.Contracts;
using CleanCorpus.Ordering.Legacy;
using CleanCorpus.Ordering.Persistence;

namespace CleanCorpus.Ordering
{
    /// <summary>
    ///     The mapping written by hand — the code every one of these projects has before it adopts a mapper,
    ///     and the only oracle worth checking a generated mapper against. Where the generated map and this
    ///     disagree, one of them is wrong and the test says which member.
    /// </summary>
    internal static class HandWritten
    {
        public static OrderResponse ToResponse(OrderEntity source)
        {
            return new OrderResponse
            {
                Id = source.Id,
                Reference = source.Reference,
                CustomerName = source.Customer.DisplayName,
                CustomerEmail = source.Customer.EmailAddress,
                BillingTown = source.Customer.BillingAddress.Town,
                Status = source.Status.ToString(),
                PlacedAt = source.PlacedAt,
                DispatchedAt = source.DispatchedAt,
                FreightCharge = source.FreightCharge,
                Lines = source.Lines.Select(ToLineResponse).ToList()
            };
        }

        public static OrderLineResponse ToLineResponse(OrderLineEntity source)
        {
            return new OrderLineResponse
            {
                Sku = source.Sku,
                Description = source.Description,
                Quantity = source.Quantity,
                UnitPrice = source.UnitPrice,
                DiscountPercent = source.DiscountPercent
            };
        }

        public static OrderSummaryResponse ToSummary(OrderEntity source)
        {
            return new OrderSummaryResponse
            {
                Id = source.Id,
                Reference = source.Reference,
                Status = source.Status.ToString(),
                PlacedAt = source.PlacedAt
            };
        }

        public static OrderEntity ToEntity(PlaceOrderRequest source)
        {
            return new OrderEntity
            {
                Reference = source.Reference,
                CustomerId = source.CustomerId,
                FreightCharge = source.FreightCharge,
                Lines = source.Lines.Select(ToLineEntity).ToList()
            };
        }

        public static OrderLineEntity ToLineEntity(OrderLineRequest source)
        {
            return new OrderLineEntity
            {
                Sku = source.Sku,
                Description = source.Description,
                Quantity = source.Quantity,
                UnitPrice = source.UnitPrice,
                DiscountPercent = source.DiscountPercent
            };
        }

        public static CustomerProfileResponse ToProfile(CustomerEntity source)
        {
            return new CustomerProfileResponse
            {
                Id = source.Id,
                DisplayName = source.DisplayName,
                EmailAddress = source.EmailAddress,
                TelephoneNumber = source.TelephoneNumber,
                RegisteredOn = source.RegisteredOn,
                BillingTown = source.BillingAddress.Town,
                BillingCountryCode = source.BillingAddress.CountryCode,
                Segments = [.. source.Segments],
                Contacts = source.Contacts.Select(ToContactResponse).ToList(),
                RecentOrders = source.Orders.Select(ToSummary).ToList(),
                Preferences = new Dictionary<string, string>(source.Preferences, StringComparer.Ordinal)
            };
        }

        public static ContactResponse ToContactResponse(ContactEntity source)
        {
            return new ContactResponse
            {
                Role = source.Role,
                FullName = source.FullName,
                EmailAddress = source.EmailAddress
            };
        }

        public static SettlementRowDto ToRow(SettlementRecord source)
        {
            return new SettlementRowDto
            {
                MerchantId = source.MerchantId,
                SettlementBatchId = source.SettlementBatchId,
                TransactionId = source.TransactionId,
                AuthorisationCode = source.AuthorisationCode,
                CardScheme = source.CardScheme,
                MaskedPan = source.MaskedPan,
                CardCountryCode = source.CardCountryCode,
                TerminalId = source.TerminalId,
                StoreCode = source.StoreCode,
                OrderReference = source.OrderReference,
                GrossAmount = source.GrossAmount,
                SchemeFee = source.SchemeFee,
                InterchangeFee = source.InterchangeFee,
                AcquirerFee = source.AcquirerFee,
                NetAmount = source.NetAmount,
                CurrencyCode = source.CurrencyCode,
                SettlementCurrencyCode = source.SettlementCurrencyCode,
                ExchangeRate = source.ExchangeRate,
                CapturedAt = source.CapturedAt,
                SettledOn = source.SettledOn,
                ValueDate = source.ValueDate,
                IsRefund = source.IsRefund,
                IsChargeback = source.IsChargeback,
                ChargebackReason = source.ChargebackReason,
                RefundOfTransactionId = source.RefundOfTransactionId,
                InstalmentCount = source.InstalmentCount,
                InstalmentNumber = source.InstalmentNumber,
                PayoutBatchReference = source.PayoutBatchReference,
                PayoutStatus = source.PayoutStatus.ToString(),
                ProcessorMessage = source.ProcessorMessage
            };
        }

        public static LegacyCustomerView ToView(LegacyCustomerRecord source)
        {
            return new LegacyCustomerView
            {
                CustomerNumber = source.CustomerNumber,
                Name = source.Name,
                Email = source.Email,
                Phone = source.Phone,
                LastOrderedOn = source.LastOrderedOn,
                Lines = source.Lines.Select(line => new LegacyLineDto
                    {
                        ProductCode = line.ProductCode,
                        Quantity = line.Quantity,
                        LinePrice = line.LinePrice
                    })
                    .ToList()
            };
        }

        /// <summary>
        ///     Every member of a settlement row as text, so a thirty-member comparison is one assertion that
        ///     names the member it fails on rather than thirty that do not.
        /// </summary>
        public static string Describe(SettlementRowDto row)
        {
            return string.Join("\n",
                $"{nameof(row.MerchantId)}={row.MerchantId}",
                $"{nameof(row.SettlementBatchId)}={row.SettlementBatchId}",
                $"{nameof(row.TransactionId)}={row.TransactionId}",
                $"{nameof(row.AuthorisationCode)}={row.AuthorisationCode}",
                $"{nameof(row.CardScheme)}={row.CardScheme}",
                $"{nameof(row.MaskedPan)}={row.MaskedPan}",
                $"{nameof(row.CardCountryCode)}={row.CardCountryCode}",
                $"{nameof(row.TerminalId)}={row.TerminalId}",
                $"{nameof(row.StoreCode)}={row.StoreCode}",
                $"{nameof(row.OrderReference)}={row.OrderReference}",
                string.Create(CultureInfo.InvariantCulture, $"{nameof(row.GrossAmount)}={row.GrossAmount}"),
                string.Create(CultureInfo.InvariantCulture, $"{nameof(row.SchemeFee)}={row.SchemeFee}"),
                string.Create(CultureInfo.InvariantCulture, $"{nameof(row.InterchangeFee)}={row.InterchangeFee}"),
                string.Create(CultureInfo.InvariantCulture, $"{nameof(row.AcquirerFee)}={row.AcquirerFee}"),
                string.Create(CultureInfo.InvariantCulture, $"{nameof(row.NetAmount)}={row.NetAmount}"),
                $"{nameof(row.CurrencyCode)}={row.CurrencyCode}",
                $"{nameof(row.SettlementCurrencyCode)}={row.SettlementCurrencyCode}",
                string.Create(CultureInfo.InvariantCulture, $"{nameof(row.ExchangeRate)}={row.ExchangeRate}"),
                string.Create(CultureInfo.InvariantCulture, $"{nameof(row.CapturedAt)}={row.CapturedAt:O}"),
                string.Create(CultureInfo.InvariantCulture, $"{nameof(row.SettledOn)}={row.SettledOn:O}"),
                string.Create(CultureInfo.InvariantCulture, $"{nameof(row.ValueDate)}={row.ValueDate:O}"),
                $"{nameof(row.IsRefund)}={row.IsRefund}",
                $"{nameof(row.IsChargeback)}={row.IsChargeback}",
                $"{nameof(row.ChargebackReason)}={row.ChargebackReason}",
                $"{nameof(row.RefundOfTransactionId)}={row.RefundOfTransactionId}",
                string.Create(CultureInfo.InvariantCulture, $"{nameof(row.InstalmentCount)}={row.InstalmentCount}"),
                string.Create(CultureInfo.InvariantCulture, $"{nameof(row.InstalmentNumber)}={row.InstalmentNumber}"),
                $"{nameof(row.PayoutBatchReference)}={row.PayoutBatchReference}",
                $"{nameof(row.PayoutStatus)}={row.PayoutStatus}",
                $"{nameof(row.ProcessorMessage)}={row.ProcessorMessage}");
        }
    }
}
