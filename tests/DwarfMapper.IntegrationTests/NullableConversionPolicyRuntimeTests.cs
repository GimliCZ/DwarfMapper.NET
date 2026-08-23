// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // TASKS.md I20, the runtime half. The generator now REPORTS a lossy cross-category conversion through every
// Nullable<> permutation of a pair (DWARF038; an Error under ImplicitConversions = false). Reporting is only
// half the claim: the conversion it reports must still be the one that actually happens, the null must still
// propagate, and the widening neighbours must be untouched. Emission evidence cannot say any of that.

    public sealed class NcSrc
    {
        public long? BothWrapped { get; set; }

        public long TargetWrapped { get; set; }

        public long? SourceWrapped { get; set; }

        public int? WideningControl { get; set; }
    }

    public sealed class NcDst
    {
        public double? BothWrapped { get; set; }

        public double? TargetWrapped { get; set; }

        public double SourceWrapped { get; set; }

        public long? WideningControl { get; set; }
    }

// Permissive on purpose: under ImplicitConversions = false this mapper is a build ERROR and there is nothing
// to execute. The strict half is pinned at the generator, where a refusal is the only observable.
    [DwarfMapper]
    public partial class NullableConversionMapper
    {
        public partial NcDst Map(NcSrc s);
    }

    public class NullableConversionPolicyRuntimeTests
    {
        [Fact]
        public void The_reported_conversion_is_the_one_that_runs_and_it_loses_exactly_what_it_says()
        {
            // long.MaxValue is not representable in a double — the precision loss DWARF038 warns about is real,
            // and asserting the exact double is what makes this a measurement rather than a shrug.
            var dto = new NullableConversionMapper().Map(new NcSrc
            {
                BothWrapped = long.MaxValue,
                TargetWrapped = long.MaxValue,
                SourceWrapped = long.MaxValue,
                WideningControl = 42
            });

            Assert.Equal((double)long.MaxValue, dto.BothWrapped);
            Assert.Equal((double)long.MaxValue, dto.TargetWrapped);
            Assert.Equal(long.MaxValue, dto.SourceWrapped);

            // The loss, DEMONSTRATED rather than implied: two source values one apart arrive as the SAME
            // destination value, through all three nullable permutations. That is what DWARF038 is warning about,
            // and it is why the option exists — a consumer who sets ImplicitConversions = false is asking to be
            // stopped before this ships.
            var nearby = new NullableConversionMapper().Map(new NcSrc
            {
                BothWrapped = long.MaxValue - 1,
                TargetWrapped = long.MaxValue - 1,
                SourceWrapped = long.MaxValue - 1,
                WideningControl = 41
            });
            Assert.Equal(dto.BothWrapped, nearby.BothWrapped);
            Assert.Equal(dto.TargetWrapped, nearby.TargetWrapped);
            Assert.Equal(dto.SourceWrapped, nearby.SourceWrapped);

            // The widening control: same shape, no loss, and it must map exactly — two distinct sources stay
            // distinct, which is the whole difference between a flagged conversion and a silent one.
            Assert.Equal(42L, dto.WideningControl);
            Assert.Equal(41L, nearby.WideningControl);
        }

        [Fact]
        public void A_null_still_propagates_through_the_conversion_that_is_now_reported()
        {
            // Making the generator LOUD about a conversion must not change what it does with a null. Both
            // nullable-target members stay null; the widening control does too.
            var dto = new NullableConversionMapper().Map(new NcSrc
            {
                BothWrapped = null,
                TargetWrapped = 7,
                SourceWrapped = 9,
                WideningControl = null
            });

            Assert.Null(dto.BothWrapped);
            Assert.Null(dto.WideningControl);
            Assert.Equal(7d, dto.TargetWrapped);
            Assert.Equal(9d, dto.SourceWrapped);
        }
    }
}
