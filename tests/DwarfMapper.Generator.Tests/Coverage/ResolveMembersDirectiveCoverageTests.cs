// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Coverage for two outcomes of MapperExtractor.ResolveMembers that had never executed in the full suite:
//   - IgnoreObsoleteMembers folds obsolete destination members into the ignore set, but a member a [MapValue] names
//     explicitly is left out of it, so the caller can opt one obsolete member back in without tripping DWARF012;
//   - a [MapShare] naming a member that is also [MapIgnore]d is a contradiction, refused as DWARF012 rather than
//     silently resolved one way.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ResolveMembersDirectiveCoverageTests
    {
        [Fact]
        public void An_obsolete_member_a_map_value_names_is_kept_under_ignore_obsolete_members()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int A { get; set; } }
                               public class Dst { public int A { get; set; } [System.Obsolete] public int C { get; set; } }
                               [DwarfMapper(IgnoreObsoleteMembers = true)]
                               public partial class M { [MapValue("C", 7)] public partial Dst Map(Src s); }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            Assert.Contains("C = 7,", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_shared_member_that_is_also_ignored_is_refused()
        {
            const string src = """
                               using System.Collections.Immutable;
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public ImmutableArray<int> Items { get; set; } }
                               public class Dst { public ImmutableArray<int> Items { get; set; } }
                               [DwarfMapper]
                               public partial class M { [MapShare("Items")] [MapIgnore("Items")] public partial Dst Map(Src s); }
                               """;

            var conflict = Assert.Single(GeneratorTestHarness.Run(src).Diagnostics, d => d.Id == "DWARF012");
            Assert.Contains("'Items'", conflict.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }
}
