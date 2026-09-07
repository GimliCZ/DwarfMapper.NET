// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.CompilerServices;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests
{
    /// <summary>The platforms the counters are kept per. 1-based, which is what <c>Offset</c> exists for.</summary>
    public enum DensePlatform
    {
        Web = 1,
        Ios = 2,
        Android = 3,
        Desktop = 4
    }

    /// <summary>Four <c>int</c> slots, inline: they live inside whatever object declares this member.</summary>
    [InlineArray(4)]
    public struct DenseCounts4
    {
        private int _e0;
    }

    public sealed class DenseStatsSource
    {
        public int UserId { get; set; }

        public Dictionary<DensePlatform, int> Counts { get; set; } = new();

        public IReadOnlyDictionary<DensePlatform, int> Errors { get; set; } =
            new Dictionary<DensePlatform, int>();
    }

    public sealed class DenseStatsTarget
    {
        public int UserId { get; set; }

        public DenseCounts4 Counts { get; set; }

        // A FIELD, deliberately, beside the property above. An inline array reached through a PROPERTY is a
        // struct copy, so `t.Counts[0] = x` does not compile for a consumer; through a field it does. Both are
        // writable destinations as far as the mapper is concerned — it assigns the whole array either way —
        // and the difference only shows up in what the consumer can do afterwards.
        public DenseCounts4 Errors;
    }

    /// <summary>
    ///     A <c>long</c>-backed enum, which is where the emitted range check earns its keep. Its declared
    ///     members are 0 and 1, so the proof passes — and a key of <c>0x1_0000_0001</c> that no member declares
    ///     casts to <c>1</c> in <c>int</c>, which is INSIDE the array.
    /// </summary>
    public enum DenseWide : long
    {
        Zero = 0,
        One = 1
    }

    [InlineArray(2)]
    public struct DenseWideSlots2
    {
        private int _e0;
    }

    public sealed class DenseWideSource
    {
        public Dictionary<DenseWide, int> Counts { get; set; } = new();
    }

    public sealed class DenseWideTarget
    {
        public DenseWideSlots2 Counts { get; set; }
    }

    [DwarfMapper]
    public partial class DenseWideMapper
    {
        [MapDenseEnumKeys(nameof(DenseWideTarget.Counts))]
        public partial DenseWideTarget Map(DenseWideSource s);
    }

    [DwarfMapper]
    public partial class DenseStatsMapper
    {
        // Two members, two source shapes: a concrete Dictionary and an IReadOnlyDictionary. Both yield
        // KeyValuePair<DensePlatform, int>, which is the only thing the proof asks of the source.
        [MapDenseEnumKeys(nameof(DenseStatsTarget.Counts), Offset = 1)]
        [MapDenseEnumKeys(nameof(DenseStatsTarget.Errors), Offset = 1)]
        public partial DenseStatsTarget Map(DenseStatsSource s);

        [MapDenseEnumKeys(nameof(DenseStatsTarget.Counts), Offset = 1)]
        [MapDenseEnumKeys(nameof(DenseStatsTarget.Errors), Offset = 1)]
        public partial void Update(DenseStatsSource s, DenseStatsTarget t);
    }

    /// <summary>
    ///     What the dense fill does at run time. The generator tests pin the emitted TEXT and every refusal;
    ///     these pin the only two things a consumer can observe — that every entry lands in the slot its key
    ///     names, and that a key the proof could not have seen fails loudly instead of writing somewhere else.
    /// </summary>
    public class MapDenseEnumKeysRuntimeTests
    {
        [Fact]
        public void Every_source_entry_lands_in_the_slot_its_key_names()
        {
            var src = new DenseStatsSource
            {
                UserId = 7,
                Counts = new Dictionary<DensePlatform, int>
                {
                    [DensePlatform.Web] = 11,
                    [DensePlatform.Android] = 33,
                    [DensePlatform.Desktop] = 44
                }
            };

            var dst = new DenseStatsMapper().Map(src);

            Assert.Equal(7, dst.UserId);
            Assert.Equal(11, dst.Counts[0]); // Web = 1, Offset = 1
            Assert.Equal(0, dst.Counts[1]); // Ios was absent — the slot keeps its default
            Assert.Equal(33, dst.Counts[2]);
            Assert.Equal(44, dst.Counts[3]);
        }

        [Fact]
        public void The_mapping_is_total_over_the_whole_declared_enum()
        {
            // Every declared member, so no slot is left untested by an accident of the sample above.
            var src = new DenseStatsSource();
            foreach (var p in Enum.GetValues<DensePlatform>())
                src.Counts[p] = (int)p * 10;

            var dst = new DenseStatsMapper().Map(src);

            foreach (var p in Enum.GetValues<DensePlatform>())
                Assert.Equal((int)p * 10, dst.Counts[(int)p - 1]);
        }

        [Fact]
        public void An_IReadOnlyDictionary_source_fills_the_array_the_same_way()
        {
            var src = new DenseStatsSource
            {
                Errors = new Dictionary<DensePlatform, int>
                {
                    [DensePlatform.Ios] = 5
                }
            };

            var dst = new DenseStatsMapper().Map(src);

            Assert.Equal(5, dst.Errors[1]);
        }

        [Fact]
        public void A_null_source_dictionary_yields_a_zeroed_array_rather_than_throwing()
        {
            // The helper's guard, and it matters for the same reason the share's does: generated code is public
            // API reachable from assemblies that made no nullable promise.
            var src = new DenseStatsSource
            {
                Counts = null!
            };

            var dst = new DenseStatsMapper().Map(src);

            Assert.Equal(0, dst.Counts[0]);
            Assert.Equal(0, dst.Counts[3]);
        }

        [Fact]
        public void An_undeclared_key_throws_naming_the_key_instead_of_writing_another_slot()
        {
            // The residue no compile-time proof can reach: (DensePlatform)999 is legal C#. The emitted loop
            // range-checks the index, so this is a diagnosable exception rather than an IndexOutOfRangeException
            // from inside a file the consumer cannot edit — and, for an enum wider than int, rather than a
            // silent write into a slot that belongs to a different key.
            var src = new DenseStatsSource
            {
                Counts = new Dictionary<DensePlatform, int>
                {
                    [(DensePlatform)999] = 1
                }
            };

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new DenseStatsMapper().Map(src));
            Assert.Contains("dense enum key is outside the mapped range", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Update_into_fills_the_array_on_an_instance_the_caller_already_built()
        {
            var target = new DenseStatsTarget
            {
                UserId = 1
            };
            var src = new DenseStatsSource
            {
                UserId = 2,
                Counts = new Dictionary<DensePlatform, int>
                {
                    [DensePlatform.Desktop] = 9
                }
            };

            new DenseStatsMapper().Update(src, target);

            Assert.Equal(2, target.UserId);
            Assert.Equal(9, target.Counts[3]);
        }

        [Fact]
        public void The_destination_slots_live_inside_the_destination_object()
        {
            // The allocation claim, stated as a property rather than measured as one: the slots are storage
            // inside the destination, so reading the member yields a COPY and two destinations built from one
            // source cannot share it. Nothing was allocated for either array.
            var src = new DenseStatsSource
            {
                Counts = new Dictionary<DensePlatform, int>
                {
                    [DensePlatform.Web] = 1
                }
            };

            var mapper = new DenseStatsMapper();
            var a = mapper.Map(src);
            var b = mapper.Map(src);

            var copy = b.Counts;
            copy[0] = 42;

            Assert.Equal(1, a.Counts[0]);
            Assert.Equal(1, b.Counts[0]);
            Assert.Equal(42, copy[0]);
        }

        [Fact]
        public void A_wide_enum_key_that_would_WRAP_into_range_throws_instead_of_writing_another_slot()
        {
            // The finding this guard exists for, as a test rather than as a claim. DenseWide is long-backed;
            // (int)(DenseWide)0x1_0000_0001 is 1 — measured — which is a legal index into a two-slot array. A
            // bare (int) cast would therefore write DenseWide.One's slot with NOTHING thrown: no exception, no
            // diagnostic, and a number that is simply wrong. The emitted index is computed in long and tested
            // as an unsigned quantity, so the write never happens.
            var src = new DenseWideSource
            {
                Counts = new Dictionary<DenseWide, int>
                {
                    [DenseWide.One] = 11,
                    [(DenseWide)0x1_0000_0001L] = 99
                }
            };

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new DenseWideMapper().Map(src));
            Assert.Contains("dense enum key is outside the mapped range", ex.Message, StringComparison.Ordinal);

            // And the slot the wrapped index would have landed in is untouched, because the map threw.
            var clean = new DenseWideMapper().Map(new DenseWideSource
            {
                Counts = new Dictionary<DenseWide, int>
                {
                    [DenseWide.One] = 11
                }
            });
            Assert.Equal(11, clean.Counts[1]);
        }

        [Fact]
        public void An_inline_array_FIELD_is_a_writable_destination_and_stays_writable_afterwards()
        {
            // The distinction a consumer meets first: through a property the inline array is a struct copy, so
            // element assignment does not compile; through a field it is storage, so it does.
            var src = new DenseStatsSource
            {
                Errors = new Dictionary<DensePlatform, int>
                {
                    [DensePlatform.Android] = 3
                }
            };

            var dst = new DenseStatsMapper().Map(src);
            Assert.Equal(3, dst.Errors[2]);

            dst.Errors[2] = 4;
            Assert.Equal(4, dst.Errors[2]);
        }
    }
}
