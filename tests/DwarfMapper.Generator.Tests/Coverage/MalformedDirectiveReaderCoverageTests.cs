// SPDX-License-Identifier: GPL-2.0-only

// Coverage suite for MapperExtractor.cs directive readers, on applications the compiler itself refuses. The consumer is
// told by that compile error, in their own source. The readers must drop what they cannot read, neither crashing nor
// adding a report of their own:
//   - ReadIgnoreSources on a [MapIgnoreSource] with no argument (CS7036);
//   - ReadMapValues on a [MapValue] with no argument (CS1729).
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class MalformedDirectiveReaderCoverageTests
    {
        private const string Pair = """
                                    public class Src { public int Id { get; set; } public string Name { get; set; } = ""; }
                                    public class Dst { public int Id { get; set; } public string Name { get; set; } = ""; }
                                    """;

        [Fact]
        public void A_map_ignore_source_with_no_argument_is_dropped()
        {
            var src = "using DwarfMapper;\nnamespace Demo;\n" + Pair + """

                                                                       [DwarfMapper]
                                                                       public partial class M
                                                                       {
                                                                           [MapIgnoreSource]
                                                                           public partial Dst Map(Src s);
                                                                       }
                                                                       """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("DWARF", StringComparison.Ordinal));
            Assert.Contains("Name = s.Name", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_map_value_with_no_argument_is_dropped_before_the_element_wise_gate()
        {
            var src = "using System;\nusing DwarfMapper;\nnamespace Demo;\n" + Pair + """

                                                                                     [DwarfMapper]
                                                                                     public partial class M
                                                                                     {
                                                                                         [MapValue]
                                                                                         public partial void MapSpan(ReadOnlySpan<Src> src, Span<Dst> dst);
                                                                                     }
                                                                                     """;

            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("DWARF", StringComparison.Ordinal));
        }
    }
}
