// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using DwarfMapper.Testing;

namespace DwarfMapper.UnionBenchmarks
{
    /// <summary>
    ///     The payloads, drawn the way the main suite draws its own (benchmarks/DwarfMapper.Benchmarks/RealisticPayloads.cs):
    ///     the CONTENT of every case from <see cref="ObjectFactoryV2" />, the fixture and fuzz source the test suites use
    ///     (nulls below the root, boundary numbers, empty and varied strings), and the COUNT pinned here.
    ///     <para>
    ///         The MIX of cases is drawn here too, from its own seeded generator: 60 % cards, 30 % transfers, 10 % cash, in
    ///         a seeded order. A uniform mix in a fixed rotation would let the branch predictor learn the sequence and
    ///         flatter every arm alike; the skew is what a payments table looks like.
    ///     </para>
    ///     <para>
    ///         ONE draw feeds both shapes: the hierarchy payload is the union payload's content, element for element, so
    ///         the shapes carry identical data and the comparison between them measures the shape alone.
    ///     </para>
    /// </summary>
    internal static class Payloads
    {
        /// <summary>Pinned: changing it re-rolls every payload and breaks comparison with recorded results.</summary>
        public const int Seed = 20261114;

        /// <summary>Length of every single-payment ring: a power of two, so the cycling index is a mask.</summary>
        public const int RingSize = 512;

        /// <summary>The draw, both shapes; the hierarchy's element <c>i</c> holds the union's element <c>i</c>.</summary>
        public static (Payment[] Unions, PaymentNode[] Nodes) Payments(int count, int salt)
        {
            var mix = new Random(Seed + salt);
            var unions = new Payment[count];
            var nodes = new PaymentNode[count];
            for (var i = 0; i < count; i++)
            {
                var contentSeed = Seed + (salt * 100_000) + i;
                var roll = mix.Next(100);
                if (roll < 60)
                {
                    var card = ObjectFactoryV2.Create<Card>(contentSeed);
                    unions[i] = card;
                    nodes[i] = new CardNode(card.Holder, card.Last4, card.ExpiryMonth, card.ExpiryYear);
                }
                else if (roll < 90)
                {
                    var transfer = ObjectFactoryV2.Create<Transfer>(contentSeed);
                    unions[i] = transfer;
                    nodes[i] = new TransferNode(transfer.Iban, transfer.Bic, transfer.Reference);
                }
                else
                {
                    var cash = ObjectFactoryV2.Create<Cash>(contentSeed);
                    unions[i] = cash;
                    nodes[i] = new CashNode(cash.Amount, cash.Currency);
                }
            }

            return (unions, nodes);
        }

        /// <summary>Value cases, the same 60/30/10 skew: ints, doubles, strings.</summary>
        public static Reading[] Readings(int count, int salt)
        {
            var mix = new Random(Seed + salt);
            var readings = new Reading[count];
            for (var i = 0; i < count; i++)
            {
                var contentSeed = Seed + (salt * 100_000) + i;
                var roll = mix.Next(100);
                if (roll < 60)
                {
                    readings[i] = ObjectFactoryV2.Create<int>(contentSeed);
                }
                else if (roll < 90)
                {
                    readings[i] = ObjectFactoryV2.Create<double>(contentSeed);
                }
                else
                {
                    // A root draw is never null, so no reading is an empty union.
                    readings[i] = ObjectFactoryV2.Create<string>(contentSeed);
                }
            }

            return readings;
        }

        /// <summary>
        ///     Fails the run when the draw is degenerate: a missing case, or content with no null and no boundary value
        ///     - the uniform data the factory exists to replace. Called from <c>[GlobalSetup]</c>.
        /// </summary>
        public static void AssertRealistic(IReadOnlyList<Payment> payments, string label)
        {
            int cards = 0, transfers = 0, cash = 0, unusual = 0;
            foreach (var payment in payments)
            {
                // Over Value, not over the union itself: switching over the union, this loop drew CA1508 from the
                // SDK 11.0.100 analyzers ("'transfers == 0' is always 'true'", the same for cash) - a false positive on
                // the union pattern, an error under warnings-as-errors. Over Value the same loop is clean.
                switch (payment.Value)
                {
                    case Card c:
                        cards++;
                        if (c.Holder is null or "" || c.Last4 is null or "" || c.ExpiryMonth is 0 or int.MinValue or int.MaxValue)
                        {
                            unusual++;
                        }

                        break;
                    case Transfer t:
                        transfers++;
                        if (t.Iban is null or "" || t.Bic is null or "" || t.Reference is null or "")
                        {
                            unusual++;
                        }

                        break;
                    case Cash m:
                        cash++;
                        if (m.Currency is null or "" || m.Amount == 0 || m.Amount == decimal.MaxValue || m.Amount == decimal.MinValue)
                        {
                            unusual++;
                        }

                        break;
                }
            }

            if (cards == 0 || transfers == 0 || cash == 0 || unusual == 0)
            {
                throw new InvalidOperationException(
                    $"{label}: degenerate draw over {payments.Count} payments - {cards} cards, {transfers} transfers, {cash} cash, " +
                    $"{unusual} with a null, empty or boundary member. Investigate before trusting any number from this run.");
            }
        }
    }
}
