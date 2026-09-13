// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Coverage for the [MapTo] registry's SynthNested, which builds a nested object one level down with the same rules the
// target itself is held to. Its two refusals had never executed in the full suite: a nested destination member with no
// source member, and a nested member with no built-in conversion. Both must be named at the NESTED type, where the fix
// belongs, not only as the outer member's failure.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class MapToNestedCoverageTests
    {
        [Fact]
        public void A_nested_destination_member_with_no_source_member_is_named_on_the_nested_type()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Inner { public int X { get; set; } }
                               public class InnerDto { public int X { get; set; } public int Extra { get; set; } }
                               public class Dst { public InnerDto I { get; set; } = new(); }
                               [MapTo(typeof(Dst))] public class Src { public Inner I { get; set; } = new(); }
                               """;

            var unmapped = Assert.Single(GeneratorTestHarness.RunMapTo(src), d => d.Id == "DWARFR02");
            Assert.Contains("'Extra' on 'InnerDto'", unmapped.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        [Fact]
        public void A_nested_member_with_no_conversion_is_named_on_the_nested_type()
        {
            const string src = """
                               using System;
                               using DwarfMapper;
                               namespace Demo;
                               public class Inner { public Guid X { get; set; } }
                               public class InnerDto { public int X { get; set; } }
                               public class Dst { public InnerDto I { get; set; } = new(); }
                               [MapTo(typeof(Dst))] public class Src { public Inner I { get; set; } = new(); }
                               """;

            Assert.Contains(GeneratorTestHarness.RunMapTo(src),
                d => d.Id == "DWARFR05" && d.GetMessage(CultureInfo.InvariantCulture).Contains("'X' → 'X' on 'InnerDto'", StringComparison.Ordinal));
        }
    }
}
