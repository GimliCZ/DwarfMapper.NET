// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Coverage suite for DwarfGenerator's DescribeDivergence, the clause of DWARF081 that says WHAT differs between two
// mappers' copies of one synthesized nested pair. Two wordings no fixture reached:
//   - copies whose members are all treated alike, which differ only in construction or hooks (a [BeforeMap] one mapper
//     replicates into its copy). Naming "some member" there would be false;
//   - more than three differing members: the first three are named, then the total.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class DivergentPairDescriptionCoverageTests
    {
        private static string Dwarf081(string source) =>
            Assert.Single(GeneratorAssert.Reports(source, "DWARF081")).GetMessage(CultureInfo.InvariantCulture);

        [Fact]
        public void Copies_that_differ_only_in_a_hook_say_so_rather_than_naming_a_member()
        {
            var message = Dwarf081("""
                                   using DwarfMapper;
                                   namespace Demo;
                                   public class Inner { public int Note { get; set; } }
                                   public class InnerDto { public int Note { get; set; } }
                                   public class Outer { public Inner Child { get; set; } = new(); }
                                   public class OuterDto { public InnerDto Child { get; set; } = new(); }

                                   [DwarfMapper]
                                   [GenerateMap<Outer, OuterDto>]
                                   public partial class Plain;

                                   [DwarfMapper]
                                   [GenerateMap<Outer, OuterDto>]
                                   public partial class Hooked
                                   {
                                       [BeforeMap] private static void Touch(Inner i) { }
                                   }
                                   """);

            Assert.Contains("(they differ in construction or hooks rather than in a single member)", message, StringComparison.Ordinal);
        }

        [Fact]
        public void More_than_three_differing_members_name_three_and_the_total()
        {
            var message = Dwarf081("""
                                   using DwarfMapper;
                                   namespace Demo;
                                   public class Inner { public string? A { get; set; } public string? B { get; set; } public string? C { get; set; } public string? D { get; set; } }
                                   public class InnerDto { public string? A { get; set; } public string? B { get; set; } public string? C { get; set; } public string? D { get; set; } }
                                   public class Outer { public Inner Child { get; set; } = new(); }
                                   public class OuterDto { public InnerDto Child { get; set; } = new(); }

                                   [DwarfMapper]
                                   [GenerateMap<Outer, OuterDto>]
                                   public partial class Replace;

                                   [DwarfMapper(SkipNullSourceMembers = true)]
                                   [GenerateMap<Outer, OuterDto>]
                                   public partial class Patch;
                                   """);

            Assert.Contains("(they treat A, B, C, … (4 in total) differently)", message, StringComparison.Ordinal);
        }
    }
}
