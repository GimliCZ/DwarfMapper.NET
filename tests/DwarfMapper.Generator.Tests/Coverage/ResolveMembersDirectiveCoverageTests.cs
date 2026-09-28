// SPDX-License-Identifier: GPL-2.0-only

// Covers: MapperExtractor.ResolveMembers — an obsolete member a [MapValue] names is kept under IgnoreObsoleteMembers
// Coverage for an outcome of MapperExtractor.ResolveMembers that had never executed in the full suite:
// IgnoreObsoleteMembers folds obsolete destination members into the ignore set, but a member a [MapValue] names
// explicitly is left out of it, so the caller can opt one obsolete member back in without tripping DWARF012.
//
// A second case lived here - a [MapShare] that is also [MapIgnore]d, refused as DWARF012 - and was REMOVED in
// round-31 T05 as a duplicate. IgnoreConflictDirectiveNameTests already drives that exact source as its `Share`
// row and asserts strictly more about it: the same single DWARF012 naming 'Items', plus that the message names
// the directive that was written. Two tests over one source, one of them a subset, is a maintenance cost with no
// extra evidence; the older home keeps it.
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
    }
}
