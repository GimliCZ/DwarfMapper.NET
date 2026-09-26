// SPDX-License-Identifier: GPL-2.0-only

// Coverage suite for MapperExtractor.Conversions.cs's HasSuppressMessage, which honours [SuppressMessage] on the MAPPER
// CLASS as the in-file escape hatch for generator diagnostics (#pragma cannot reach them). It matches the attribute by
// class NAME, so it has to reject the look-alikes a name match lets in: a user-defined SuppressMessageAttribute with
// one constructor argument, one whose check id is not a string, and a real one whose id merely STARTS with the
// diagnostic's id (the documented prefix form needs a colon: "DWARF076:reason"). Each must leave DWARF076 reported.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class SuppressMessageLookalikeCoverageTests
    {
        private const string SelfMap = "public class Src { public int A { get; set; } }\n";

        [Fact]
        public void A_single_argument_user_suppress_message_attribute_does_not_suppress()
        {
            var src = "using DwarfMapper;\nnamespace Demo;\n" + SelfMap + """
                                                                          namespace Local { [System.AttributeUsage(System.AttributeTargets.All)] public sealed class SuppressMessageAttribute : System.Attribute { public SuppressMessageAttribute(string checkId) { } } }
                                                                          [DwarfMapper]
                                                                          [Local.SuppressMessage("DWARF076")]
                                                                          public partial class M { public partial Src Map(Src s); }
                                                                          """;

            Assert.Single(GeneratorAssert.Reports(src, "DWARF076"));
        }

        [Fact]
        public void A_user_suppress_message_attribute_with_a_non_string_check_id_does_not_suppress()
        {
            var src = "using DwarfMapper;\nnamespace Demo;\n" + SelfMap + """
                                                                          namespace Local { [System.AttributeUsage(System.AttributeTargets.All)] public sealed class SuppressMessageAttribute : System.Attribute { public SuppressMessageAttribute(string category, int checkId) { } } }
                                                                          [DwarfMapper]
                                                                          [Local.SuppressMessage("DwarfMapper", 76)]
                                                                          public partial class M { public partial Src Map(Src s); }
                                                                          """;

            Assert.Single(GeneratorAssert.Reports(src, "DWARF076"));
        }

        [Fact]
        public void A_check_id_that_only_shares_the_prefix_does_not_suppress()
        {
            // "DWARF0761" starts with "DWARF076" but is a different id; only "DWARF076" or "DWARF076:<reason>" match.
            var src = "using System.Diagnostics.CodeAnalysis;\nusing DwarfMapper;\nnamespace Demo;\n" + SelfMap + """
                                                                                                                     [DwarfMapper]
                                                                                                                     [SuppressMessage("DwarfMapper", "DWARF0761")]
                                                                                                                     public partial class M { public partial Src Map(Src s); }
                                                                                                                     """;

            Assert.Single(GeneratorAssert.Reports(src, "DWARF076"));
        }
    }
}
