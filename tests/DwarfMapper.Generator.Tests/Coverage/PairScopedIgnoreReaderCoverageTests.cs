// SPDX-License-Identifier: GPL-2.0-only

// Covers: MapperExtractor.ReadPairIgnores — a null member name reports DWARF095/DWARF056 like an empty one; an unbound application is skipped
using System.Globalization;

// The pair-scoped [MapIgnore<TTarget>] reader sees two half-typed shapes, because a generator runs on every keystroke:
// [MapIgnore<Dst>(null)] binds the string constructor with a null value (at most CS8625), and [MapIgnore<Dst>()] fails
// to bind at all, which leaves no constructor arguments behind. Neither may take the mapper down; the pair maps as if
// the directive were absent.
// Round 34 B: the null form used to vanish as well, with a green build, while its three siblings in the same file had
// reported a null name since 2026-09-13 (d474e9f, fc58fb0). A null name is now the empty name, which names no member,
// so it reports what an empty name does: DWARF095 on a pair it matches, DWARF056 when it matches no pair. The unbound
// form is still the compiler's error alone.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class PairScopedIgnoreReaderCoverageTests
    {
        // shared-fixture: the same two-member Src/Dst pair UnscopedIgnoreNoMatchTests.PairTypes declares, on
        // purpose. That file asserts what a pair-scoped [MapIgnore<T>] with a MISSPELLED member name reports
        // (DWARF095); this one asserts that a NULL member name reports the same thing an empty one does.
        private const string Types = """
                                     using DwarfMapper;
                                     namespace Demo;
                                     public class Src { public int Id { get; set; } public int Extra { get; set; } }
                                     public class Dst { public int Id { get; set; } public int Extra { get; set; } }

                                     """;

        private static string Mapper(string directive) =>
            Types + "[DwarfMapper][GenerateMap<Src, Dst>]" + directive + " public partial class M { }\n";

        private static string[] Messages(string source, string id) =>
            GeneratorTestHarness.Run(source).Diagnostics
                .Where(d => d.Id == id)
                .Select(d => d.GetMessage(CultureInfo.InvariantCulture))
                .ToArray();

        [Fact]
        public void A_pair_ignore_with_a_null_member_name_reports_DWARF095_like_an_empty_one()
        {
            var message = Assert.Single(Messages(Mapper("[MapIgnore<Dst>(null)]"), "DWARF095"));

            Assert.Equal(Assert.Single(Messages(Mapper("[MapIgnore<Dst>(\"\")]"), "DWARF095")), message);
            // Reported, not taken down: the pair still maps the member it would have mapped anyway.
            Assert.Contains("Extra = src.Extra", GeneratorAssert.EmitsCompilableCode(Mapper("[MapIgnore<Dst>(null)]")),
                StringComparison.Ordinal);
        }

        [Fact]
        public void A_null_member_name_on_a_type_no_pair_targets_reports_DWARF056_like_an_empty_one()
        {
            var message = Assert.Single(Messages(Mapper("[MapIgnore<Src>(null)]"), "DWARF056"));

            Assert.Equal(Assert.Single(Messages(Mapper("[MapIgnore<Src>(\"\")]"), "DWARF056")), message);
        }

        [Fact]
        public void A_pair_ignore_whose_constructor_did_not_bind_is_skipped_without_a_generator_diagnostic()
        {
            var source = Mapper("[MapIgnore<Dst>()]");

            var (diagnostics, generated) = GeneratorTestHarness.Run(source);

            Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("DWARF", StringComparison.Ordinal));
            Assert.Contains("Extra = src.Extra", generated, StringComparison.Ordinal);
        }
    }
}
