// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Globalization;

namespace DwarfMapper.UnionBenchmarks
{
    /// <summary>
    ///     Proves that every arm maps what the hand-written arm maps, through the benchmark methods themselves and on the
    ///     benchmark payloads, before a single number is trusted. A fast arm that drops a case is not fast, it is wrong.
    ///     Prints one <c>UNION-BENCH-DRY ok|WRONG</c> line per comparison and returns the number of WRONG lines.
    /// </summary>
    internal static class DryRun
    {
        public static int Run()
        {
            var failures = 0;

            var payments = new PaymentBenchmarks();
            payments.Setup();
            var reference = Collect(payments, b => b.One_Union_Hand());
            failures += Check("one payment: the hand-written union arm maps every case", Array.ConvertAll(reference, Describe), NoEmpty(reference));
            failures += Check("one payment: Union_HandCaseMethods", Collect(payments, b => b.One_Union_HandCaseMethods()), reference, Describe);
            failures += Check("one payment: Union_DwarfMapper", Collect(payments, b => b.One_Union_DwarfMapper()), reference, Describe);
            var hierarchy = Collect(payments, b => b.One_Hierarchy_Hand());
            failures += Check("one payment: Hierarchy_Hand holds the union's data", hierarchy, reference, Describe, Describe);
            failures += Check("one payment: Hierarchy_DwarfMapper", Collect(payments, b => b.One_Hierarchy_DwarfMapper()), hierarchy, Describe);
            failures += Check("one payment: Hierarchy_Mapperly", Collect(payments, b => b.One_Hierarchy_Mapperly()), hierarchy, Describe);
            failures += Check("one payment: Hierarchy_Mapster", Collect(payments, b => b.One_Hierarchy_Mapster()), hierarchy, Describe);

            var batch = payments.Batch_Union_Hand().Items.ToArray();
            failures += Check("batch: the hand-written union arm maps every case", Array.ConvertAll(batch, Describe), NoEmpty(batch));
            failures += Check("batch: Union_HandCaseMethods", payments.Batch_Union_HandCaseMethods().Items.ToArray(), batch, Describe);
            failures += Check("batch: Union_DwarfMapper", payments.Batch_Union_DwarfMapper().Items.ToArray(), batch, Describe);
            var batchHierarchy = payments.Batch_Hierarchy_Hand().Items.ToArray();
            failures += Check("batch: Hierarchy_Hand holds the union's data", batchHierarchy, batch, Describe, Describe);
            failures += Check("batch: Hierarchy_DwarfMapper", payments.Batch_Hierarchy_DwarfMapper().Items.ToArray(), batchHierarchy, Describe);
            failures += Check("batch: Hierarchy_Mapperly", payments.Batch_Hierarchy_Mapperly().Items.ToArray(), batchHierarchy, Describe);
            failures += Check("batch: Hierarchy_Mapster", payments.Batch_Hierarchy_Mapster().Items.ToArray(), batchHierarchy, Describe);

            var readings = new ReadingBenchmarks();
            readings.Setup();
            var readingReference = Collect(readings, b => b.One_Hand());
            failures += Check("one reading: the hand-written arm widens every int", Array.ConvertAll(readingReference, Describe), WidenedAll(readingReference));
            failures += Check("one reading: DwarfMapper", Collect(readings, b => b.One_DwarfMapper()), readingReference, Describe);
            var readingBatch = readings.Batch_Hand().Items.ToArray();
            failures += Check("reading batch: the hand-written arm widens every int", Array.ConvertAll(readingBatch, Describe), WidenedAll(readingBatch));
            failures += Check("reading batch: DwarfMapper", readings.Batch_DwarfMapper().Items.ToArray(), readingBatch, Describe);

            Console.WriteLine(failures == 0 ? "UNION-BENCH-DRY all arms agree" : $"UNION-BENCH-DRY {failures} WRONG");
            return failures;
        }

        private static T[] Collect<T>(PaymentBenchmarks benchmarks, Func<PaymentBenchmarks, T> arm)
        {
            benchmarks.ResetRing();
            var results = new T[Payloads.RingSize];
            for (var i = 0; i < results.Length; i++)
            {
                results[i] = arm(benchmarks);
            }

            return results;
        }

        private static T[] Collect<T>(ReadingBenchmarks benchmarks, Func<ReadingBenchmarks, T> arm)
        {
            benchmarks.ResetRing();
            var results = new T[Payloads.RingSize];
            for (var i = 0; i < results.Length; i++)
            {
                results[i] = arm(benchmarks);
            }

            return results;
        }

        /// <summary>The reference itself must be sound: no empty union, and the three cases all present.</summary>
        private static string? NoEmpty(PaymentDto[] reference)
        {
            int cards = 0, transfers = 0, cash = 0;
            foreach (var payment in reference)
            {
                switch (payment)
                {
                    case CardDto:
                        cards++;
                        break;
                    case TransferDto:
                        transfers++;
                        break;
                    case CashDto:
                        cash++;
                        break;
                    case null:
                        return "an empty union";
                }
            }

            return cards > 0 && transfers > 0 && cash > 0 ? null : $"cases {cards}/{transfers}/{cash}";
        }

        private static string? WidenedAll(ReadingDto[] reference)
        {
            int longs = 0, doubles = 0, strings = 0;
            foreach (var reading in reference)
            {
                switch (reading.Value)
                {
                    case long:
                        longs++;
                        break;
                    case double:
                        doubles++;
                        break;
                    case string:
                        strings++;
                        break;
                    default:
                        return "a value of type " + (reading.Value?.GetType().Name ?? "null");
                }
            }

            return longs > 0 && doubles > 0 && strings > 0 ? null : $"cases {longs}/{doubles}/{strings}";
        }

        private static int Check(string what, string[] described, string? problem)
        {
            if (problem is null)
            {
                Console.WriteLine($"UNION-BENCH-DRY ok {what} ({described.Length} values)");
                return 0;
            }

            Console.WriteLine($"UNION-BENCH-DRY WRONG {what}: {problem}");
            return 1;
        }

        private static int Check<T>(string what, T[] actual, T[] expected, Func<T, string> describe)
        {
            return Check(what, actual, expected, describe, describe);
        }

        private static int Check<TActual, TExpected>(string what, TActual[] actual, TExpected[] expected, Func<TActual, string> describeActual, Func<TExpected, string> describeExpected)
        {
            if (actual.Length != expected.Length)
            {
                Console.WriteLine($"UNION-BENCH-DRY WRONG {what}: {actual.Length} results against {expected.Length}");
                return 1;
            }

            for (var i = 0; i < actual.Length; i++)
            {
                var a = describeActual(actual[i]);
                var e = describeExpected(expected[i]);
                if (!string.Equals(a, e, StringComparison.Ordinal))
                {
                    Console.WriteLine($"UNION-BENCH-DRY WRONG {what}: at {i} \"{a}\", expected \"{e}\"");
                    return 1;
                }
            }

            Console.WriteLine($"UNION-BENCH-DRY ok {what} ({actual.Length} values)");
            return 0;
        }

        // One description per payment, whichever shape and whichever library made it: the cross-shape comparison and
        // the per-arm comparisons all read the same text. The CLR type of the case is part of it.

        private static string Describe(PaymentDto payment)
        {
            return payment switch
            {
                CardDto c => Card(c.GetType().Name, c.Holder, c.Last4, c.ExpiryMonth, c.ExpiryYear),
                TransferDto t => Transfer(t.GetType().Name, t.Iban, t.Bic, t.Reference),
                CashDto m => Cash(m.GetType().Name, m.Amount, m.Currency),
                null => "empty",
            };
        }

        private static string Describe(PaymentNodeDto? node)
        {
            return node switch
            {
                CardNodeDto c => Card(c.GetType().Name, c.Holder, c.Last4, c.ExpiryMonth, c.ExpiryYear),
                TransferNodeDto t => Transfer(t.GetType().Name, t.Iban, t.Bic, t.Reference),
                CashNodeDto m => Cash(m.GetType().Name, m.Amount, m.Currency),
                null => "null",
                _ => "unexpected " + node.GetType().Name,
            };
        }

        private static string Describe(ReadingDto reading)
        {
            return reading.Value is null
                ? "empty"
                : string.Create(CultureInfo.InvariantCulture, $"{reading.Value.GetType().Name} {reading.Value}");
        }

        // The case's type name is compared without its "Node" infix, so the union's CardDto and the hierarchy's
        // CardNodeDto describe alike when they hold the same data.
        private static string Card(string type, string holder, string last4, int month, int year)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{type.Replace("Node", "", StringComparison.Ordinal)} {holder}|{last4}|{month}|{year}");
        }

        private static string Transfer(string type, string iban, string bic, string reference)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{type.Replace("Node", "", StringComparison.Ordinal)} {iban}|{bic}|{reference}");
        }

        private static string Cash(string type, decimal amount, string currency)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{type.Replace("Node", "", StringComparison.Ordinal)} {amount}|{currency}");
        }
    }
}
