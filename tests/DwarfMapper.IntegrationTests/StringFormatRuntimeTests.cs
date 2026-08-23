// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // [MapProperty(StringFormat = "...")] — format a value into a string with an explicit .NET format, always
// with InvariantCulture (stable across deployments/threads, matching the library's culture stance).

    public sealed class SfSrc
    {
        public DateTime When { get; set; }

        public decimal Amount { get; set; }

        public int Count { get; set; }
    }

    public sealed class SfDst
    {
        public string When { get; set; } = "";

        public string Amount { get; set; } = "";

        public string Count { get; set; } = "";
    }

    [DwarfMapper]
    public partial class StringFormatMapper
    {
        [MapProperty(nameof(SfSrc.When), nameof(SfDst.When), StringFormat = "yyyy-MM-dd")]
        [MapProperty(nameof(SfSrc.Amount), nameof(SfDst.Amount), StringFormat = "F2")]
        [MapProperty(nameof(SfSrc.Count), nameof(SfDst.Count), StringFormat = "N0")]
        public partial SfDst Map(SfSrc s);
    }

// B17's control, at runtime. The remedy for the unused-helper defect DROPS the converter a StringFormat
// replaced, and the way to get that wrong is to drop one a DIFFERENT member still needs. Plain (unformatted)
// int -> string members sit on both sides of a formatted one, so the two routes the remedy takes are both
// executed: a helper an earlier member added must SURVIVE the removal, and a later member's must be re-added.
    public sealed class SfMixedSrc
    {
        public int Before { get; set; }

        public int Formatted { get; set; }

        public int After { get; set; }
    }

    public sealed class SfMixedDst
    {
        public string Before { get; set; } = "";

        public string Formatted { get; set; } = "";

        public string After { get; set; } = "";
    }

    [DwarfMapper]
    public partial class StringFormatMixedMapper
    {
        [MapProperty(nameof(SfMixedSrc.Before), nameof(SfMixedDst.Before))]
        [MapProperty(nameof(SfMixedSrc.Formatted), nameof(SfMixedDst.Formatted), StringFormat = "N0")]
        [MapProperty(nameof(SfMixedSrc.After), nameof(SfMixedDst.After))]
        public partial SfMixedDst Map(SfMixedSrc s);
    }

    public class StringFormatRuntimeTests
    {
        [Fact]
        public void Each_format_is_applied_with_invariant_culture()
        {
            var src = new SfSrc
            {
                When = new DateTime(2026, 7, 15),
                Amount = 1234.5m,
                Count = 1_000_000
            };

            var dto = new StringFormatMapper().Map(src);

            Assert.Equal("2026-07-15", dto.When);
            Assert.Equal("1234.50", dto.Amount); // F2, invariant '.' decimal separator
            Assert.Equal("1,000,000", dto.Count); // N0, invariant ',' group separator
        }

        [Fact]
        public void A_formatted_member_does_not_disturb_the_plain_conversions_on_either_side_of_it()
        {
            var dto = new StringFormatMixedMapper().Map(
                new SfMixedSrc
                {
                    Before = 1_000_000,
                    Formatted = 1_000_000,
                    After = 1_000_000
                });

            // The two plain members are the CONTROLS: same source value, same source type, no format. They must
            // still convert, and must NOT pick up the neighbour's format.
            Assert.Equal("1000000", dto.Before);
            Assert.Equal("1000000", dto.After);
            Assert.Equal("1,000,000", dto.Formatted);
        }
    }
}
