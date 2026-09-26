// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// A span or async-stream map does not resolve members itself: its ELEMENT pair is resolved by the nested-pair drain,
// and DrainNestedMappingQueue runs RequiredMapping = Both's source-coverage check for exactly the element pairs those
// endpoints registered (ElementPairsOwedCoverage) — never for a genuinely nested member pair, where source coverage has
// never applied. The loop that matches a drained pair against the owed list had only ever seen a drained pair whose
// SOURCE matched an owed one; a nested pair over a different source type had not drained beside an owed element pair.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ElementPairSourceCoverageTests
    {
        [Fact]
        public void Source_coverage_runs_for_the_span_element_pair_and_not_for_a_nested_pair_over_another_source()
        {
            const string src = """
                               using System;
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int A { get; set; } public int Unused { get; set; } }
                               public class Dst { public int A { get; set; } }
                               public class Child { public int C { get; set; } public int Extra { get; set; } }
                               public class ChildDto { public int C { get; set; } }
                               public class Holder { public Child Kid { get; set; } = new(); }
                               public class HolderDto { public ChildDto Kid { get; set; } = new(); }
                               [DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]
                               public partial class M
                               {
                                   public partial void Map(ReadOnlySpan<Src> source, Span<Dst> destination);
                                   public partial HolderDto ToHolder(Holder h);
                               }
                               """;

            var (diagnostics, _) = GeneratorTestHarness.Run(src);

            var leftover = Assert.Single(diagnostics, d => d.Id == "DWARF039");
            Assert.Contains("'Unused'", leftover.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }
}
