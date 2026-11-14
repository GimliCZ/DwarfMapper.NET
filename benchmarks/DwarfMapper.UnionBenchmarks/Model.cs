// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Generic;

namespace DwarfMapper.UnionBenchmarks
{
    // The same payment data in the two shapes a model can give it. The case types are the SAME kind of type in both
    // (positional records, built by their constructors), so what differs between the shapes is only the container -
    // a union struct holding its case in `Value`, or a reference to an abstract base - and the dispatch over it.

    // ── As a C# 15 union ────────────────────────────────────────────────────────────────────────────────────────────

    public sealed record class Card(string Holder, string Last4, int ExpiryMonth, int ExpiryYear);

    public sealed record class Transfer(string Iban, string Bic, string Reference);

    public sealed record class Cash(decimal Amount, string Currency);

    public union Payment(Card, Transfer, Cash);

    public sealed record class CardDto(string Holder, string Last4, int ExpiryMonth, int ExpiryYear);

    public sealed record class TransferDto(string Iban, string Bic, string Reference);

    public sealed record class CashDto(decimal Amount, string Currency);

    public union PaymentDto(CardDto, TransferDto, CashDto);

    public sealed record class PaymentBatch(List<Payment> Items);

    public sealed record class PaymentBatchDto(List<PaymentDto> Items);

    // ── As the class hierarchy a model without unions uses ──────────────────────────────────────────────────────────

    public abstract record class PaymentNode;

    public sealed record class CardNode(string Holder, string Last4, int ExpiryMonth, int ExpiryYear) : PaymentNode;

    public sealed record class TransferNode(string Iban, string Bic, string Reference) : PaymentNode;

    public sealed record class CashNode(decimal Amount, string Currency) : PaymentNode;

    // Concrete, not abstract, for Mapster alone: compiling `Include` arms into an ABSTRACT record destination throws
    // Mapster.CompileException (a NullReferenceException in RecordTypeAdapter -> GetConstructorModel: an abstract record
    // has no public constructor), measured on Mapster 10.0.8. The other three arms map either; the source base stays
    // abstract.
    public record class PaymentNodeDto;

    public sealed record class CardNodeDto(string Holder, string Last4, int ExpiryMonth, int ExpiryYear) : PaymentNodeDto;

    public sealed record class TransferNodeDto(string Iban, string Bic, string Reference) : PaymentNodeDto;

    public sealed record class CashNodeDto(decimal Amount, string Currency) : PaymentNodeDto;

    public sealed record class PaymentNodeBatch(List<PaymentNode> Items);

    public sealed record class PaymentNodeBatchDto(List<PaymentNodeDto> Items);

    // ── A union with value-type cases: a value case lives boxed in the union's `Value` ──────────────────────────────

    public union Reading(int, double, string);

    public union ReadingDto(long, double, string);

    public sealed record class ReadingBatch(List<Reading> Items);

    public sealed record class ReadingBatchDto(List<ReadingDto> Items);
}
