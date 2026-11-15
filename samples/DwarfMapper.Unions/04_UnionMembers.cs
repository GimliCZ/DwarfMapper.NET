// SPDX-License-Identifier: GPL-2.0-only

// 04 - Unions inside other types. A union member, a list of unions, and a case-typed member assigned into a union
// member: the order map below declares no union map, so the generator builds a private one from the case maps, and
// the last member is the language's own union conversion (Card into Payment).

using System.Collections.Generic;
using System.Linq;

namespace DwarfMapper.Unions.Ex04
{
    public record class Card(string Last4);

    public record class Transfer(string Iban);

    public record class CardDto(string Last4);

    public record class TransferDto(string Iban);

    public union Payment(Card, Transfer);

    public union PaymentDto(CardDto, TransferDto);

    public class Order
    {
        public Payment Paid { get; set; }

        public List<Payment> Refunds { get; set; } = new();

        public CardDto Preferred { get; set; } = new("0000");
    }

    public class OrderDto
    {
        public PaymentDto Paid { get; set; }

        public List<PaymentDto> Refunds { get; set; } = new();

        public PaymentDto Preferred { get; set; }
    }

// <snippet: union-members>
    [DwarfMapper]
    public partial class OrderMapper
    {
        public partial OrderDto ToDto(Order order); // Paid, Refunds and Preferred go through PaymentDto's cases

        public partial CardDto ToDto(Card card);

        public partial TransferDto ToDto(Transfer transfer);
    }
// </snippet>

    public static class Example
    {
        public static string Run()
        {
            var order = new Order
            {
                Paid = new Card("4242"),
                Refunds = new() { new Transfer("CZ65"), new Card("1111") },
                Preferred = new CardDto("9999"),
            };
            var dto = new OrderMapper().ToDto(order);
            return dto.Paid.Value + " | " + string.Join(", ", dto.Refunds.Select(r => r.Value)) + " | " + dto.Preferred.Value;
        }

        public const string Expected = "CardDto { Last4 = 4242 } | TransferDto { Iban = CZ65 }, CardDto { Last4 = 1111 } | CardDto { Last4 = 9999 }";
    }
}
