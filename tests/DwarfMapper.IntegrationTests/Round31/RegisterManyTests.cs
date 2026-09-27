// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests.Round31
{
    /// <summary>
    ///     Research P6: every mapped pair registers six collection shapes keyed on <c>IEnumerable&lt;S&gt;</c>, and the
    ///     interface list grew by one copied array per entry — startup allocation quadratic in the application's map
    ///     count (412 MB measured at 1,000 pairs). <see cref="DwarfMapperRegistry.RegisterMany" /> grows it once per
    ///     batch. Its contract is "exactly a sequence of <see cref="DwarfMapperRegistry.Register" /> calls", so most of
    ///     these rows are that equivalence; the allocation row is the reason it exists.
    /// </summary>
    /// <remarks>
    ///     Private marker types per test: the registry is static and add-only for the process, so shared shapes would
    ///     make these tests order-dependent on each other and on every generated registration in this assembly.
    /// </remarks>
    public sealed class RegisterManyTests
    {
        [Fact]
        public void A_batch_equals_sequential_registration()
        {
            Func<object, object> firstA = _ => new BatchDst("a1");
            Func<object, object> secondA = _ => new BatchDst("a2");
            DwarfMapperRegistry.RegisterMany(new (Type, Type, Func<object, object>)[]
            {
                (typeof(BatchA), typeof(BatchDst), firstA),
                (typeof(BatchB), typeof(BatchDst), _ => new BatchDst("b")),
                (typeof(BatchA), typeof(BatchDst), secondA),
            });

            Assert.True(DwarfMapperRegistry.IsAmbiguous(typeof(BatchA), typeof(BatchDst)));
            Assert.True(DwarfMapperRegistry.TryGet(typeof(BatchA), typeof(BatchDst), out var got));
            Assert.Same(firstA, got);
            Assert.True(DwarfMapperRegistry.IsProvided(typeof(BatchB), typeof(BatchDst)));
            Assert.False(DwarfMapperRegistry.IsAmbiguous(typeof(BatchB), typeof(BatchDst)));
        }

        [Fact]
        public void Interface_keyed_entries_in_a_batch_resolve_like_sequential_ones()
        {
            DwarfMapperRegistry.RegisterMany(new (Type, Type, Func<object, object>)[]
            {
                (typeof(IEnumerable<IfaceMarker>), typeof(IfaceDst), s => new IfaceDst(((IEnumerable<IfaceMarker>)s).Count())),
            });

            var mapped = (IfaceDst)DwarfMapperRegistry.Map(new List<IfaceMarker> { new(), new() }, typeof(IfaceDst));
            Assert.Equal(2, mapped.Count);
            mapped = (IfaceDst)DwarfMapperRegistry.Map(new[] { new IfaceMarker() }, typeof(IfaceDst));
            Assert.Equal(1, mapped.Count);
        }

        [Fact]
        public void A_pair_in_two_batches_is_ambiguous_and_the_first_batch_wins()
        {
            Func<object, object> first = _ => new CrossDst("first");
            DwarfMapperRegistry.RegisterMany(new (Type, Type, Func<object, object>)[] { (typeof(IEnumerable<CrossMarker>), typeof(CrossDst), first) });
            DwarfMapperRegistry.RegisterMany(new (Type, Type, Func<object, object>)[] { (typeof(IEnumerable<CrossMarker>), typeof(CrossDst), _ => new CrossDst("second")) });

            Assert.True(DwarfMapperRegistry.IsAmbiguous(typeof(IEnumerable<CrossMarker>), typeof(CrossDst)));
            Assert.True(DwarfMapperRegistry.TryGet(typeof(IEnumerable<CrossMarker>), typeof(CrossDst), out var got));
            Assert.Same(first, got);
            // The duplicate never reached the interface list, so the lookup is not ambiguous at run time either.
            Assert.Equal("first", ((CrossDst)DwarfMapperRegistry.Map(new List<CrossMarker>(), typeof(CrossDst))).Tag);
        }

        [Fact]
        public void A_null_mid_batch_throws_after_registering_what_came_before_it()
        {
            Assert.Throws<ArgumentNullException>(() => DwarfMapperRegistry.RegisterMany(new (Type, Type, Func<object, object>)[]
            {
                (typeof(IEnumerable<ThrowMarker>), typeof(ThrowDst), _ => new ThrowDst()),
                (typeof(ThrowMarker), typeof(ThrowDst), null!),
            }));

            // Sequential Register would have registered the first entry before throwing on the second; the interface
            // list is published in a finally, so the lookup sees it too.
            Assert.IsType<ThrowDst>(DwarfMapperRegistry.Map(new List<ThrowMarker>(), typeof(ThrowDst)));
            Assert.False(DwarfMapperRegistry.IsProvided(typeof(ThrowMarker), typeof(ThrowDst)));
        }

        [Fact]
        public void Registering_3000_interface_entries_allocates_linearly()
        {
            var markers = new[]
            {
                typeof(M0), typeof(M1), typeof(M2), typeof(M3), typeof(M4), typeof(M5), typeof(M6), typeof(M7),
                typeof(M8), typeof(M9), typeof(M10), typeof(M11), typeof(M12), typeof(M13), typeof(M14),
            };
            Func<object, object> map = _ => new ManyDst();
            var entries = (from a in markers from b in markers from c in markers select (a, b, c))
                .Take(3000)
                .Select(t => (typeof(IEnumerable<>).MakeGenericType(typeof(Tri<,,>).MakeGenericType(t.a, t.b, t.c)), typeof(ManyDst), map))
                .ToArray();
            Assert.Equal(3000, entries.Length);

            var before = GC.GetAllocatedBytesForCurrentThread();
            DwarfMapperRegistry.RegisterMany(entries);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // A loop over Register copies the whole interface list once per entry: ~108 MB for 3,000 entries on an
            // empty list, more on top of this assembly's own registrations. One copy is a few hundred KB.
            Assert.True(allocated < 1024 * 1024, $"RegisterMany allocated {allocated:N0} bytes for 3,000 interface entries");
        }

        private sealed class BatchA;

        private sealed class BatchB;

        private sealed record BatchDst(string Tag);

        private sealed class IfaceMarker;

        private sealed record IfaceDst(int Count);

        private sealed class CrossMarker;

        private sealed record CrossDst(string Tag);

        private sealed class ThrowMarker;

        private sealed class ThrowDst;

        private sealed class ManyDst;

        private sealed class Tri<T1, T2, T3>;

        private sealed class M0;

        private sealed class M1;

        private sealed class M2;

        private sealed class M3;

        private sealed class M4;

        private sealed class M5;

        private sealed class M6;

        private sealed class M7;

        private sealed class M8;

        private sealed class M9;

        private sealed class M10;

        private sealed class M11;

        private sealed class M12;

        private sealed class M13;

        private sealed class M14;
    }
}
