// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Coverage suite for MapperExtractor.RestatesBase.cs's DWARF085 report, on two shapes no fixture reached:
//   - Join's cap: more than three members named in one list print the first three and "(and N more)", so a pair that
//     drops a whole block of base members still gets a readable message;
//   - MemberTypeOf on FIELD targets: the "same member type on both targets" guard must find a public field exactly
//     as it finds a property. Otherwise every drift on a field-shaped DTO would be skipped as a "redeclared" member.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class RestatesBaseDriftReportCoverageTests
    {
        private static string Dwarf085(string source) =>
            Assert.Single(GeneratorAssert.Reports(source, "DWARF085")).GetMessage(CultureInfo.InvariantCulture);

        [Fact]
        public void More_than_three_dropped_members_are_capped_with_a_count()
        {
            var message = Dwarf085("""
                                   using DwarfMapper;
                                   namespace Demo;
                                   public class Src { public int A { get; set; } public int B { get; set; } public int C { get; set; } public int D { get; set; } }
                                   public class DerivedSrc : Src { }
                                   public class Dto { public int A { get; set; } public int B { get; set; } public int C { get; set; } public int D { get; set; } }
                                   public class DerivedDto : Dto { }
                                   [DwarfMapper]
                                   [GenerateMap<Src, Dto>]
                                   [GenerateMap<DerivedSrc, DerivedDto>]
                                   [RestatesBase<DerivedSrc, DerivedDto>]
                                   [MapIgnore<DerivedDto>("A")]
                                   [MapIgnore<DerivedDto>("B")]
                                   [MapIgnore<DerivedDto>("C")]
                                   [MapIgnore<DerivedDto>("D")]
                                   public partial class M { }
                                   """);

            Assert.Contains("does not map A, B, C (and 1 more), which the base pair maps", message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_dropped_field_member_is_reported_like_a_property()
        {
            var message = Dwarf085("""
                                   using DwarfMapper;
                                   namespace Demo;
                                   public class Src { public int F; }
                                   public class DerivedSrc : Src { }
                                   public class Dto { public int F; }
                                   public class DerivedDto : Dto { }
                                   [DwarfMapper]
                                   [GenerateMap<Src, Dto>]
                                   [GenerateMap<DerivedSrc, DerivedDto>]
                                   [RestatesBase<DerivedSrc, DerivedDto>]
                                   [MapIgnore<DerivedDto>("F")]
                                   public partial class M { }
                                   """);

            Assert.Contains("does not map F, which the base pair maps", message, StringComparison.Ordinal);
        }
    }
}
