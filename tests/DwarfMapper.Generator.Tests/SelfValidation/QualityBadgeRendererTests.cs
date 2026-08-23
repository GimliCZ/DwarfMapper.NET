// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Drives <see cref="QualityBadgeRenderer" /> directly. The byte-compare in
    ///     <c>DocsAreSnippetCurrentTests</c> proves the README matches whatever this renderer produces; it says
    ///     nothing about whether what it produces is right. These are the tests that say that — in particular
    ///     that every reader FAILS LOUDLY when the file it reads changes shape, because a reader that silently
    ///     matched nothing would render an empty region, the committed README would be healed to match it, and
    ///     the badges would disappear with every gate still green.
    /// </summary>
    public class QualityBadgeRendererTests
    {
        /// <summary>
        ///     The five assemblies the coverage gate floors. Pinned by NAME, deliberately not by value: the
        ///     numbers are measurements that move by design (invariant R1 re-measures them), and a copy of them
        ///     here would be the second hand-typed number this whole region exists to abolish. The identities
        ///     are not measurements — an assembly silently leaving the gate is a finding.
        /// </summary>
        private static readonly string[] FlooredAssemblies =
        [
            "DwarfMapper", "DwarfMapper.Generator", "DwarfMapper.DocTooling", "DwarfMapper.CodeFixes",
            "DwarfMapper.Testing"
        ];

        [Fact]
        public void The_coverage_floors_are_read_from_the_gate_script_itself()
        {
            var floors = QualityBadgeRenderer.ParseCoverageFloors(
                File.ReadAllText(Path.Combine(RepoLayout.Root, "scripts", "housekeeping.ps1")));

            Assert.Equal(FlooredAssemblies, floors.Select(f => f.Assembly).ToArray());
            Assert.All(floors, f => Assert.InRange(f.Floor, 1.0, 100.0));
        }

        [Fact]
        public void A_renamed_floors_block_is_a_loud_failure_not_an_empty_badge_row()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => QualityBadgeRenderer.ParseCoverageFloors("$somethingElse = [ordered]@{\n  'A' = 1.0\n}\n"));

            Assert.Contains("scripts/housekeeping.ps1", ex.Message, StringComparison.Ordinal);
            Assert.Contains("no '$coverageFloors", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_floors_block_that_lost_an_assembly_is_refused_rather_than_rendered_short()
        {
            // The realistic drift: the block is still there and still parses, but one line's shape changed (or
            // an assembly left the gate) and the region would render four badges where five gates exist. Silence
            // there is the exact failure this file is about, so the count is asserted, not inferred.
            var ex = Assert.Throws<InvalidOperationException>(() => QualityBadgeRenderer.ParseCoverageFloors(
                "$coverageFloors = [ordered]@{\n    'A' = 91.2\n    'B' = 93.7\n}\n"));

            Assert.Contains("parsed 2 coverage floor(s), expected 5", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_floor_that_is_not_a_percentage_means_the_parse_is_wrong()
        {
            var block = "$coverageFloors = [ordered]@{\n" + string.Concat(FlooredAssemblies.Select(a => $"    '{a}' = 000.0\n")) + "}\n";

            var ex = Assert.Throws<InvalidOperationException>(() => QualityBadgeRenderer.ParseCoverageFloors(block));
            Assert.Contains("is not a percentage", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void The_mutation_legs_are_read_from_the_equivalents_ledger()
        {
            var legs = QualityBadgeRenderer.ParseLedgerRows(File.ReadAllText(
                Path.Combine(RepoLayout.Root, "Issues", "ledgers", "equivalent-mutants.md")));

            Assert.Equal(["generator", "doctooling", "runtime"], legs.Select(l => l.Name).ToArray());
            Assert.Equal(
                ["stryker-config.json", "stryker-config.doctooling.json", "stryker-config.runtime.json"],
                legs.Select(l => l.ConfigFile).ToArray());
            Assert.All(legs, l => Assert.InRange(l.RawScore, 1.0, 100.0));
        }

        [Fact]
        public void A_ledger_whose_summary_table_changed_shape_is_refused()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => QualityBadgeRenderer.ParseLedgerRows("| leg | scoreable |\n|---|---|\n| generator | 201 |\n"));

            Assert.Contains("parsed 0 mutation leg row(s), expected 3", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void The_break_is_read_from_the_stryker_config_the_ledger_names()
        {
            const string config = """
                                  {
                                    "stryker-config": {
                                      "comment": "irrelevant",
                                      "thresholds": { "high": 90, "low": 81, "break": 81 }
                                    }
                                  }
                                  """;

            Assert.Equal(81, QualityBadgeRenderer.ReadBreak("fixture.json", config));
        }

        [Theory]
        [InlineData("""{ "stryker-config": { "thresholds": { "high": 90 } } }""")]
        [InlineData("""{ "stryker-config": { } }""")]
        [InlineData("""{ }""")]
        public void A_config_with_no_break_is_refused_rather_than_graded_against_zero(string config)
        {
            var ex = Assert.Throws<InvalidOperationException>(() => QualityBadgeRenderer.ReadBreak("f.json", config));
            Assert.Contains("no integer 'stryker-config.thresholds.break'", ex.Message, StringComparison.Ordinal);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // The colour derivation — the constraint that says a colour may not be hand-assigned. Every case below
        // is one of the three verdicts scripts/gate-checks.ps1 can reach on the same number.
        // ─────────────────────────────────────────────────────────────────────────

        [Theory]
        // Mutation: Assert-MutationScoreWithinBand FLOORS the score to a whole point before comparing.
        [InlineData(81.59, 81, 1.0, "brightgreen")] // inside [81, 82) — the pin equals the measurement
        [InlineData(81.00, 81, 1.0, "brightgreen")] // exactly at break: the gate passes
        [InlineData(81.99, 81, 1.0, "brightgreen")] // still floors to 81
        [InlineData(80.99, 81, 1.0, "red")] //         below break: the gate FAILS
        [InlineData(82.00, 81, 1.0, "yellow")] //      floors to 82 = break + 1: R2 demands a re-measure
        // Coverage: Test-CoverageWithinBand truncates to TENTHS before comparing, same 1.0 pp band.
        [InlineData(91.2, 91.2, 0.1, "brightgreen")]
        [InlineData(92.1, 91.2, 0.1, "brightgreen")] // 0.9 pp above the floor — inside the band
        [InlineData(91.1, 91.2, 0.1, "red")]
        [InlineData(92.2, 91.2, 0.1, "yellow")] //      1.0 pp above the floor — outside it
        public void The_colour_is_the_gates_own_verdict_on_the_number(
            double measured,
            double gate,
            double step,
            string expected)
        {
            Assert.Equal(expected, QualityBadgeRenderer.BandColour(measured, gate, step));
        }

        [Fact]
        public void A_dash_in_a_badge_label_is_refused_because_shields_reads_it_as_the_separator()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => QualityBadgeRenderer.Badge("code-coverage", "91.2%25", "brightgreen", "x"));

            Assert.Contains("shields.io reads as the label/value separator", ex.Message, StringComparison.Ordinal);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // The rendered region.
        // ─────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Every_gate_gets_exactly_one_badge_and_none_of_them_is_blank()
        {
            var rows = QualityBadgeRenderer.RenderRows();
            var badges = rows.Where(r => r.StartsWith("[![", StringComparison.Ordinal)).ToList();

            Assert.Equal(FlooredAssemblies.Length + 3, badges.Count);
            Assert.All(badges,
                b =>
                {
                    Assert.Contains("https://img.shields.io/badge/", b, StringComparison.Ordinal);
                    Assert.Contains("%25-", b, StringComparison.Ordinal); // the value ends in a percent sign
                    Assert.DoesNotContain("--", b, StringComparison.Ordinal);
                });

            foreach (var assembly in FlooredAssemblies)
                Assert.Contains(badges, b => b.Contains($"coverage%20{assembly}-", StringComparison.Ordinal));
        }

        [Fact]
        public void The_note_carries_every_legs_break_value()
        {
            // The break values are the half of the mutation gate the badge VALUE cannot show (it shows the raw
            // score), and the plan requires both. Rendered from the configs, so this asserts they are present
            // and consistent with what the reader read rather than restating them.
            var note = Assert.Single(QualityBadgeRenderer.RenderRows(),
                r => r.StartsWith("<sub>", StringComparison.Ordinal));

            var legs = QualityBadgeRenderer.ParseLedgerRows(File.ReadAllText(
                Path.Combine(RepoLayout.Root, "Issues", "ledgers", "equivalent-mutants.md")));

            foreach (var leg in legs)
            {
                var expected = QualityBadgeRenderer.ReadBreak(leg.ConfigFile,
                    File.ReadAllText(Path.Combine(RepoLayout.Root, leg.ConfigFile)));
                Assert.Contains(
                    string.Create(CultureInfo.InvariantCulture, $"break {expected} ({leg.Name})"),
                    note,
                    StringComparison.Ordinal);
            }
        }

        [Fact]
        public void The_committed_readme_carries_the_rendered_region_verbatim()
        {
            // The same comparison DocsAreSnippetCurrentTests makes across every document, narrowed to this region
            // so its failure names the badges rather than "README.md is stale". This is the guard the sabotage
            // demo trips: hand-edit one rendered number and this reds naming the line.
            var readme = DocSet.Read("README.md").Replace("\r\n", "\n", StringComparison.Ordinal);
            var open = readme.IndexOf($"<!-- table: {QualityBadgeRenderer.TableName} -->\n", StringComparison.Ordinal);
            Assert.True(open >= 0, "README.md no longer carries the quality-badges marker.");

            var bodyStart = open + $"<!-- table: {QualityBadgeRenderer.TableName} -->\n".Length;
            var close = readme.IndexOf("<!-- endtable -->", bodyStart, StringComparison.Ordinal);
            Assert.True(close > bodyStart, "The quality-badges region is never closed.");

            Assert.Equal(
                string.Join("\n", QualityBadgeRenderer.RenderRows()) + "\n",
                readme[bodyStart..close]);
        }
    }
}
