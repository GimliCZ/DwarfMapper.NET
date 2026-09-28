// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests.Round31
{
    /// <summary>
    ///     Research P4 / round 31 T14: a collection handed to the ambient registry never hits an exact key, and the
    ///     interface lookup used to test EVERY interface-keyed entry in the application — 272 ns with no extra pairs,
    ///     6.3 µs with 1,000 (<c>AmbientScanBenchmarks</c>). The entries are now bucketed by destination, the one type
    ///     the caller always supplies, so only the entries that could answer are tested.
    /// </summary>
    /// <remarks>
    ///     Private marker types per test, for the registry's usual reason: it is static and add-only for the process.
    /// </remarks>
    public sealed class InterfaceBucketTests
    {
        /// <summary>GUARD: bucketing must not change ambiguity, which is always an intra-destination question.</summary>
        [Fact]
        public void Two_interfaces_accepting_one_source_for_one_destination_still_throw_naming_both()
        {
            DwarfMapperRegistry.RegisterMany(new (Type, Type, Func<object, object>)[]
            {
                (typeof(IFirst), typeof(AmbDst), _ => new AmbDst()),
                (typeof(ISecond), typeof(AmbDst), _ => new AmbDst()),
            });

            var e = Assert.Throws<DwarfMapMissingException>(() => DwarfMapperRegistry.Map(new Both(), typeof(AmbDst)));
            Assert.Contains(nameof(IFirst), e.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(ISecond), e.Message, StringComparison.Ordinal);
        }

        /// <summary>GUARD: an entry that accepts the source but maps to ANOTHER destination is not a candidate.</summary>
        [Fact]
        public void An_accepting_entry_under_another_destination_is_not_a_candidate()
        {
            DwarfMapperRegistry.RegisterMany(new (Type, Type, Func<object, object>)[]
            {
                (typeof(IEnumerable<OtherMarker>), typeof(OtherDstA), _ => new OtherDstA()),
                (typeof(IEnumerable<OtherMarker>), typeof(OtherDstB), _ => new OtherDstB()),
            });

            Assert.IsType<OtherDstB>(DwarfMapperRegistry.Map(new List<OtherMarker>(), typeof(OtherDstB)));
            Assert.IsType<OtherDstA>(DwarfMapperRegistry.Map(new List<OtherMarker>(), typeof(OtherDstA)));
        }

        /// <summary>
        ///     The defect itself. Timed in ONE process, before and after the registry gains 6,000 unrelated
        ///     interface-keyed entries — six per pair for 1,000 pairs, the load 1,000 real mappers put on it. A flat
        ///     lookup moves by noise; the old scan grew with every one of them.
        /// </summary>
        [Fact]
        [Trait("Category", "Perf")]
        public void Collection_dispatch_cost_does_not_grow_with_unrelated_registrations()
        {
            DwarfMapperRegistry.RegisterMany(new (Type, Type, Func<object, object>)[]
            {
                (typeof(IEnumerable<ScanMarker>), typeof(ScanDst), _ => new ScanDst()),
            });
            var source = new List<ScanMarker> { new() };

            var before = NanosPerCall(source);

            var markers = new[]
            {
                typeof(int), typeof(long), typeof(short), typeof(byte), typeof(sbyte), typeof(uint), typeof(ulong),
                typeof(ushort), typeof(float), typeof(double), typeof(decimal), typeof(char), typeof(bool),
                typeof(string), typeof(DateTime),
            };
            var shapes = new[] { typeof(List<>), typeof(HashSet<>), typeof(Queue<>), typeof(Stack<>), typeof(LinkedList<>), typeof(SortedSet<>) };
            Func<object, object> never = _ => throw new InvalidOperationException("an unrelated entry was dispatched");
            DwarfMapperRegistry.RegisterMany((from a in markers from b in markers from c in markers select (a, b, c))
                .Take(1000)
                .SelectMany(t =>
                {
                    var src = typeof(ValueTuple<,,>).MakeGenericType(t.a, t.b, t.c);
                    return shapes.Select(shape => (typeof(IEnumerable<>).MakeGenericType(src), shape.MakeGenericType(src), never));
                })
                .ToArray());

            var after = NanosPerCall(source);

            Assert.True(after <= before * 1.5,
                $"collection dispatch went from {before:F1} ns to {after:F1} ns per call after 6,000 unrelated registrations");
        }

        /// <summary>Best of several rounds, so a scheduler hiccup cannot fail the ratio.</summary>
        private static double NanosPerCall(object source)
        {
            const int Calls = 20_000;
            for (var i = 0; i < 2_000; i++) DwarfMapperRegistry.Map(source, typeof(ScanDst));
            var best = double.MaxValue;
            for (var round = 0; round < 7; round++)
            {
                var sw = Stopwatch.StartNew();
                for (var i = 0; i < Calls; i++) DwarfMapperRegistry.Map(source, typeof(ScanDst));
                sw.Stop();
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1_000_000 / Calls);
            }

            return best;
        }

        private interface IFirst;

        private interface ISecond;

        private sealed class Both : IFirst, ISecond;

        private sealed class AmbDst;

        private sealed class OtherMarker;

        private sealed class OtherDstA;

        private sealed class OtherDstB;

        private sealed class ScanMarker;

        private sealed class ScanDst;
    }
}
