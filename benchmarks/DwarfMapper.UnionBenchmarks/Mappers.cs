// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using Mapster;

namespace DwarfMapper.UnionBenchmarks
{
    // Every library is written the way its own documentation writes the shape - nothing tuned for the benchmark.

    // ── DwarfMapper ─────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The union map, its cases paired by the declared method that maps each one.</summary>
    [DwarfMapper]
    public partial class DwarfPaymentMapper
    {
        public partial PaymentDto ToDto(Payment payment);

        public partial CardDto ToDto(Card card);

        public partial TransferDto ToDto(Transfer transfer);

        public partial CashDto ToDto(Cash cash);

        public partial PaymentBatchDto ToDto(PaymentBatch batch);
    }

    /// <summary>The class hierarchy, dispatched by <c>[MapDerivedType]</c> arms.</summary>
    [DwarfMapper]
    public partial class DwarfPaymentNodeMapper
    {
        [MapDerivedType<CardNode, CardNodeDto>]
        [MapDerivedType<TransferNode, TransferNodeDto>]
        [MapDerivedType<CashNode, CashNodeDto>]
        public partial PaymentNodeDto ToDto(PaymentNode node);

        public partial PaymentNodeBatchDto ToDto(PaymentNodeBatch batch);
    }

    /// <summary>Value cases: the int case is named into the long case, double and string pass through.</summary>
    [DwarfMapper]
    public partial class DwarfReadingMapper
    {
        [MapDerivedType(typeof(int), typeof(long))]
        public partial ReadingDto ToDto(Reading reading);

        public partial ReadingBatchDto ToDto(ReadingBatch batch);
    }

    // ── Mapperly: the class hierarchy (it has no union support to measure - see the results document) ──────────────

    [Riok.Mapperly.Abstractions.Mapper]
    public partial class MapperlyPaymentNodeMapper
    {
        [Riok.Mapperly.Abstractions.MapDerivedType<CardNode, CardNodeDto>]
        [Riok.Mapperly.Abstractions.MapDerivedType<TransferNode, TransferNodeDto>]
        [Riok.Mapperly.Abstractions.MapDerivedType<CashNode, CashNodeDto>]
        public partial PaymentNodeDto ToDto(PaymentNode node);

        public partial PaymentNodeBatchDto ToDto(PaymentNodeBatch batch);
    }

    // ── Mapster: the class hierarchy, on its own configuration rather than the global one ───────────────────────────

    public static class MapsterPaymentNodeConfig
    {
        public static TypeAdapterConfig Create()
        {
            var config = new TypeAdapterConfig();
            config.NewConfig<PaymentNode, PaymentNodeDto>()
                .Include<CardNode, CardNodeDto>()
                .Include<TransferNode, TransferNodeDto>()
                .Include<CashNode, CashNodeDto>();
            config.Compile();
            return config;
        }
    }

    // ── The control: hand-written, in the generated code's STRUCTURE ────────────────────────────────────────────────

    /// <summary>
    ///     The union map written the way DwarfMapper emits it: the switch calls a public method per case, and each case
    ///     method checks its argument (its own contract as a public method) before constructing. Measured beside
    ///     <see cref="HandWritten" />, which constructs inline, it says how much of the generated map's cost is that
    ///     structure; the batch keeps <see cref="HandWritten" />'s loop, so only the element mapping differs.
    /// </summary>
    public sealed class HandWrittenCaseMethods
    {
        public PaymentDto ToDto(Payment payment)
        {
            return payment switch
            {
                Card c => ToDto(c),
                Transfer t => ToDto(t),
                Cash m => ToDto(m),
                null => default(PaymentDto),
            };
        }

        public CardDto ToDto(Card card)
        {
            ArgumentNullException.ThrowIfNull(card);
            return new CardDto(card.Holder, card.Last4, card.ExpiryMonth, card.ExpiryYear);
        }

        public TransferDto ToDto(Transfer transfer)
        {
            ArgumentNullException.ThrowIfNull(transfer);
            return new TransferDto(transfer.Iban, transfer.Bic, transfer.Reference);
        }

        public CashDto ToDto(Cash cash)
        {
            ArgumentNullException.ThrowIfNull(cash);
            return new CashDto(cash.Amount, cash.Currency);
        }

        public PaymentBatchDto ToDto(PaymentBatch batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            var items = new List<PaymentDto>(batch.Items.Count);
            foreach (var payment in batch.Items)
            {
                items.Add(ToDto(payment));
            }

            return new PaymentBatchDto(items);
        }
    }

    // ── Hand-written: what a careful developer writes for each shape ────────────────────────────────────────────────

    public static class HandWritten
    {
        public static PaymentDto ToDto(Payment payment)
        {
            return payment switch
            {
                Card c => new CardDto(c.Holder, c.Last4, c.ExpiryMonth, c.ExpiryYear),
                Transfer t => new TransferDto(t.Iban, t.Bic, t.Reference),
                Cash m => new CashDto(m.Amount, m.Currency),
                null => default(PaymentDto),
            };
        }

        public static PaymentBatchDto ToDto(PaymentBatch batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            var items = new List<PaymentDto>(batch.Items.Count);
            foreach (var payment in batch.Items)
            {
                items.Add(ToDto(payment));
            }

            return new PaymentBatchDto(items);
        }

        public static PaymentNodeDto ToDto(PaymentNode node)
        {
            return node switch
            {
                CardNode c => new CardNodeDto(c.Holder, c.Last4, c.ExpiryMonth, c.ExpiryYear),
                TransferNode t => new TransferNodeDto(t.Iban, t.Bic, t.Reference),
                CashNode m => new CashNodeDto(m.Amount, m.Currency),
                _ => throw new ArgumentOutOfRangeException(nameof(node), node, "No case for this payment."),
            };
        }

        public static PaymentNodeBatchDto ToDto(PaymentNodeBatch batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            var items = new List<PaymentNodeDto>(batch.Items.Count);
            foreach (var node in batch.Items)
            {
                items.Add(ToDto(node));
            }

            return new PaymentNodeBatchDto(items);
        }

        public static ReadingDto ToDto(Reading reading)
        {
            return reading switch
            {
                int i => (long)i,
                double d => d,
                string s => s,
                null => default(ReadingDto),
            };
        }

        public static ReadingBatchDto ToDto(ReadingBatch batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            var items = new List<ReadingDto>(batch.Items.Count);
            foreach (var reading in batch.Items)
            {
                items.Add(ToDto(reading));
            }

            return new ReadingBatchDto(items);
        }
    }
}
