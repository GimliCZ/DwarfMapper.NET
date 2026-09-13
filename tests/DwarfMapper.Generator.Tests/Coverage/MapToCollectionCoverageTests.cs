// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Coverage for the [MapTo] registry's TryCollection: a U[] or List<U> destination fed by a source that is not
// enumerable at all. The "source has no element type" refusal had never executed in the full suite — every collection
// fixture fed a collection from a collection — so a scalar into a list was never shown to be refused rather than emitted.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class MapToCollectionCoverageTests
    {
        [Fact]
        public void A_list_destination_fed_by_a_non_enumerable_source_member_is_refused()
        {
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Dst { public List<int> Items { get; set; } = new(); }
                               [MapTo(typeof(Dst))] public class Src { public int Items { get; set; } }
                               """;

            var refusal = Assert.Single(GeneratorTestHarness.RunMapTo(src), d => d.Id == "DWARFR05");
            Assert.Contains("'Items' → 'Items' on 'Dst'", refusal.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }
}
