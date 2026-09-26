// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Regression tests for a silently ignored directive: [MapConstructor<S,T>(null)] names no factory, yet the pair was
// built with its ordinary constructor and nothing was reported, so the author's stated construction override vanished
// with a green build. A null factory name is as invalid as one naming no method, and must say so exactly as that does.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class MapConstructorNullFactoryTests
    {
        [Fact]
        public void A_null_factory_name_on_a_declared_pair_reports_DWARF059()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Inner { public int A { get; set; } }
                               public class InnerDto { public int A { get; set; } }
                               [DwarfMapper]
                               [GenerateMap<Inner, InnerDto>]
                               [MapConstructor<Inner, InnerDto>(null!)]
                               public partial class M { }
                               """;

            var message = Assert.Single(GeneratorAssert.Reports(src, "DWARF059")).GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("[MapConstructor<Demo.Inner, Demo.InnerDto>", message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_null_factory_name_matching_no_pair_reports_DWARF056()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Inner { public int A { get; set; } }
                               public class InnerDto { public int A { get; set; } }
                               public class Other { public int A { get; set; } }
                               public class OtherDto { public int A { get; set; } }
                               [DwarfMapper]
                               [GenerateMap<Other, OtherDto>]
                               [MapConstructor<Inner, InnerDto>(null!)]
                               public partial class M { }
                               """;

            var message = Assert.Single(GeneratorAssert.Reports(src, "DWARF056")).GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("[MapConstructor<Demo.Inner, Demo.InnerDto>", message, StringComparison.Ordinal);
        }
    }
}
