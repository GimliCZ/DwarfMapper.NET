// SPDX-License-Identifier: GPL-2.0-only

// The pair-scoped [MapIgnore<TTarget>] reader skips an application that names no member: [MapIgnore<Dst>(null)] binds
// the string constructor with a null value (at most CS8625), and [MapIgnore<Dst>()] fails to bind at all, which leaves
// no constructor arguments behind. A generator runs on every keystroke, so both half-typed shapes reach it. Neither
// may take the mapper down; the pair maps as if the directive were absent.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class PairScopedIgnoreReaderCoverageTests
    {
        // shared-fixture: the same two-member Src/Dst pair UnscopedIgnoreNoMatchTests.PairTypes declares, on
        // purpose. That file asserts what a pair-scoped [MapIgnore<T>] with a MISSPELLED member name reports
        // (DWARF095); this one asserts what one with NO member name does (nothing - it is skipped). Same types,
        // different directive, different outcome, so neither test subsumes the other.
        private const string Types = """
                                     using DwarfMapper;
                                     namespace Demo;
                                     public class Src { public int Id { get; set; } public int Extra { get; set; } }
                                     public class Dst { public int Id { get; set; } public int Extra { get; set; } }

                                     """;

        [Fact]
        public void A_pair_ignore_with_a_null_member_name_is_skipped()
        {
            var source = Types + "[DwarfMapper][GenerateMap<Src, Dst>][MapIgnore<Dst>(null)] public partial class M { }\n";

            var generated = GeneratorAssert.EmitsCompilableCode(source);

            Assert.Contains("Extra = src.Extra", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_pair_ignore_whose_constructor_did_not_bind_is_skipped_without_a_generator_diagnostic()
        {
            var source = Types + "[DwarfMapper][GenerateMap<Src, Dst>][MapIgnore<Dst>()] public partial class M { }\n";

            var (diagnostics, generated) = GeneratorTestHarness.Run(source);

            Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("DWARF", StringComparison.Ordinal));
            Assert.Contains("Extra = src.Extra", generated, StringComparison.Ordinal);
        }
    }
}
