// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // Locks the RUNTIME semantics of a nullable-reference source mapped to a non-nullable target.
//
// DWARF070 suppresses the CS8601 that the generated raw assignment would otherwise leak into the consumer's
// build. That suppression must stay a *suppression*: it must not quietly become a throw, an empty string, or a
// skipped assignment. Changing observable behaviour in order to silence a warning would be exactly the kind of
// mapper-decides-for-you magic DwarfMapper exists to avoid. The null still flows — and the developer is told
// at BUILD time (DWARF070) instead of being surprised at run time.
//
// This fixture deliberately maps `string?` -> `string`, and since round 32 it says so where it does it:
// [MapProperty(AllowNull = true)] accepts the null for this one member, so DWARF070 is not reported for it and the
// project carries no NoWarn for the id. The statement changes nothing that is generated, which makes this fixture the
// runtime half of that promise too: with the acceptance written, the null still flows exactly as before.

    public sealed class NullRefSrc
    {
        public string? Name { get; set; }
    }

    public sealed class NullRefDst
    {
        public string Name { get; set; } = "untouched";
    }

    [DwarfMapper]
    public partial class NullRefMapper
    {
        [MapProperty(nameof(NullRefSrc.Name), nameof(NullRefDst.Name), AllowNull = true)]
        public partial NullRefDst Map(NullRefSrc src);
    }

    public class NullableRefAssignRuntimeTests
    {
        [Fact]
        public void Null_source_still_flows_through_to_the_non_nullable_target()
        {
            var result = new NullRefMapper().Map(new NullRefSrc
            {
                Name = null
            });

            // Not a throw, and not the destination's own initializer ("untouched"): the raw assign is preserved
            // verbatim. A null really is sitting in a `string` — which is exactly why DWARF070 warns at compile
            // time rather than the library silently picking a value for you.
            Assert.Null(result.Name);
        }

        [Fact]
        public void Non_null_source_maps_normally()
        {
            var result = new NullRefMapper().Map(new NullRefSrc
            {
                Name = "durin"
            });

            Assert.Equal("durin", result.Name);
        }
    }
}
