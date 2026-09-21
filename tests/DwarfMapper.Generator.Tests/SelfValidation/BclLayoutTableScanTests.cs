// SPDX-License-Identifier: GPL-2.0-only

// BCL layout table coverage scan (round 29, T0.3d). LayoutHygiene.FixedLayoutBclSize is a hard-coded table
// of BCL struct layouts a netstandard2.0 generator cannot otherwise know, and it exists on exactly one
// promise: "a table entry with no runtime assertion is not allowed to exist" (see its own doc comment,
// clause 3). T0.3c's seven entries keep that promise today only because the person who added the seventh
// arm also, by hand, added its runtime assertion in BclLayoutFactsTests.cs — nothing forces the pairing.
// Add an eighth arm and no test fails; the table goes back to stating a layout that nothing executes,
// repeating one level up the exact defect (an unmeasured member cascading DWARF101/DWARF103 silent) this
// table was built to remove.
//
// This scan enforces the pairing mechanically: every metadata type name switched on in
// LayoutHygiene.FixedLayoutBclSize must appear, as a bounded token in a CODE line (not a comment), in
// DwarfMapper.IntegrationTests.BclLayoutFactsTests. It does NOT re-derive or re-check the numbers the
// oracle asserts — that would duplicate the oracle inside the scan and let the two drift apart. The scan's
// job is "nothing was forgotten"; the oracle's job stays "the numbers are right". It also does not prove a
// SIZE and an ALIGNMENT were each asserted for the name — only that the name is referenced in real code —
// which is the split the brief drew and the reason clause 3's "both numbers" is the oracle's job, not this
// scan's.
//
// The name search excludes comment lines and matches on a bounded token deliberately, not out of caution:
// the oracle's own class summary narrates the table in prose ("the half a size check cannot see"), and a
// naive whole-file case-insensitive substring search let that sentence alone satisfy a future `Half` arm
// with no assertion anywhere — the exact vacuity this scan exists to prevent, caught before it shipped.
//
// No reflection: FixedLayoutBclSize is `private` by design (round 29 narrowed a sibling constant back to
// `private` once its reason ended, and re-widening one for a test's convenience would undo exactly that
// discipline), so this reads the source text the way the other SelfValidation scans do.

using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    public sealed class BclLayoutTableScanTests
    {
        private static readonly string LayoutHygienePath =
            Path.Combine(RepoPaths.PipelineDir, "LayoutHygiene.cs");

        private static readonly string BclLayoutFactsPath =
            Path.Combine(RepoPaths.Tests, "DwarfMapper.IntegrationTests", "BclLayoutFactsTests.cs");

        /// <summary>
        ///     Pulls the <c>"Name" => (size, align)</c> arms out of a
        ///     <c>FixedLayoutBclSize</c>-shaped switch expression. Isolated from file I/O so
        ///     <see cref="Parser_and_matcher_reject_a_bogus_arm_and_do_not_launder_unrelated_names" /> can drive
        ///     it over synthetic, known-shaped text.
        /// </summary>
        private static List<string> ParseArmNames(string layoutHygieneSource)
        {
            return Regex.Matches(layoutHygieneSource, @"""(\w+)""\s*=>\s*\(\s*\d+\s*,\s*\d+\s*\)")
                .Select(m => m.Groups[1].Value)
                .ToList();
        }

        /// <summary>
        ///     Drops every line whose trimmed text starts with <c>//</c> (a plain comment or a <c>///</c> doc
        ///     line). The oracle's own doc comments narrate the table in prose — <c>BclLayoutFactsTests</c>'s
        ///     class summary literally writes "the <b>half</b> a size check cannot see" — so a name search over
        ///     the WHOLE file text would let a doc comment mentioning a type discharge the requirement for an
        ///     assertion that was never written. Only the code lines are allowed to count as "asserted".
        /// </summary>
        private static string CodeLinesOnly(string source)
        {
            return string.Join('\n',
                source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        }

        /// <summary>
        ///     Whether <paramref name="name" /> is asserted in <paramref name="codeText" />, matched as a
        ///     BOUNDED token rather than a bare substring. Two things a substring search gets wrong: it would
        ///     let <c>DateTimeOffset</c> discharge <c>DateTime</c> with no assertion of its own, and — the
        ///     opposite failure — an over-strict exact-case match would reject <c>Decimal</c>, which the oracle
        ///     exercises only through the C# keyword alias <c>decimal</c>
        ///     (<c>Unsafe.SizeOf&lt;decimal&gt;()</c>, <c>AlignProbe&lt;decimal&gt;</c>) and never writes with
        ///     the capitalised CLR name at all. Case-insensitive word-boundary matching accepts that one alias
        ///     without opening a door for an unrelated name to stand in for it.
        /// </summary>
        private static bool IsAssertedIn(string codeText, string name)
        {
            return Regex.IsMatch(codeText, $@"\b{Regex.Escape(name)}\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        [Fact]
        public void Every_FixedLayoutBclSize_arm_has_a_runtime_assertion()
        {
            var armNames = ParseArmNames(File.ReadAllText(LayoutHygienePath));
            var oracleCode = CodeLinesOnly(File.ReadAllText(BclLayoutFactsPath));

            var unasserted = armNames
                .Where(name => !IsAssertedIn(oracleCode, name))
                .ToList();

            Assert.True(unasserted.Count == 0,
                "FixedLayoutBclSize arm(s) with no runtime assertion in BclLayoutFactsTests.cs — add a size " +
                "AND an alignment assertion for: " + string.Join(", ", unasserted));
        }

        // Non-vacuity control (the repo's B9 lesson: a scan whose corpus contains no needles reports success
        // while measuring nothing). A regex that drifts and matches zero arms would leave `unasserted` empty
        // above for the wrong reason — an empty haystack, not a covered one. Pin the arm count at the value
        // measured today (seven: Guid, DateTime, DateTimeOffset, TimeSpan, TimeOnly, DateOnly, Decimal) so a
        // broken parser fails loudly instead of passing by finding nothing to check.
        [Fact]
        public void Scan_actually_parses_the_seven_arms_it_is_meant_to_check()
        {
            var armNames = ParseArmNames(File.ReadAllText(LayoutHygienePath));

            Assert.True(armNames.Count == 7,
                $"FixedLayoutBclSize has {armNames.Count} arm(s); this pin says 7. If a new arm was added " +
                "with its size AND alignment assertion already in BclLayoutFactsTests.cs, re-pin this count " +
                "to the new value. If not, Every_FixedLayoutBclSize_arm_has_a_runtime_assertion above should " +
                "also be failing — a green run there while this pin is stale means the parser stopped " +
                "matching arms, not that the table stopped growing.");
        }

        // The soundness precondition of CodeLinesOnly, enforced rather than assumed. That stripper is
        // line-based: it drops lines whose trimmed text starts with `//`, which is exactly right for the
        // oracle as written and wrong the moment a block comment appears. A `/* ... Half ... */` would
        // survive stripping, and its prose would then discharge the assertion requirement for a `Half` arm
        // that nobody ever asserted — the precise laundering CodeLinesOnly exists to prevent, re-entering
        // through the one comment form it cannot see. Rather than build a lexer for a file that has never
        // needed one, pin the assumption: if the oracle ever grows a block comment, this fails and tells
        // whoever wrote it which of the two fixes to make.
        [Fact]
        public void Oracle_contains_no_block_comment_that_the_line_based_stripper_would_miss()
        {
            var oracle = File.ReadAllText(BclLayoutFactsPath);

            Assert.False(oracle.Contains("/*", StringComparison.Ordinal),
                "BclLayoutFactsTests.cs now contains a block comment, which CodeLinesOnly does not strip. " +
                "Its text can therefore satisfy Every_FixedLayoutBclSize_arm_has_a_runtime_assertion for a " +
                "type that has no assertion. Either rewrite that comment as `//` lines, or teach " +
                "CodeLinesOnly to strip block comments — do not delete this pin.");
        }

        /// <summary>
        ///     Isolates the parsing and matching predicates the way AssemblyScanTests' Scan6a/Scan9 controls
        ///     do, so the scan above can be shown to REJECT things and not just to pass.
        /// </summary>
        [Fact]
        public void Parser_and_matcher_reject_a_bogus_arm_and_do_not_launder_unrelated_names()
        {
            // The parser extracts an unexpected arm rather than silently dropping it.
            const string tableWithBogusArm = """
                                              "Guid" => (16, 4),
                                              "Int128" => (16, 16),
                                              """;
            Assert.Equal(["Guid", "Int128"], ParseArmNames(tableWithBogusArm));

            // The known-bad input that made the FIRST version of this scan vacuous: `Half` never appears in
            // real oracle code, only in BclLayoutFactsTests.cs's own doc comment (verbatim below) — "the half
            // a size check cannot see". A whole-file case-insensitive substring search let that PROSE discharge
            // the requirement for an assertion that was never written; CodeLinesOnly + IsAssertedIn must not.
            const string docCommentMentioningHalf =
                "        ///     Alignment is the half a size check cannot see, and it is what decides where " +
                "the NEXT field lands:\n" +
                "        [Fact]\n" +
                "        public void Real_assertion()\n" +
                "        {\n" +
                "            Assert.Equal(16, Unsafe.SizeOf<Guid>());\n" +
                "        }\n";

            var codeOnly = CodeLinesOnly(docCommentMentioningHalf);
            Assert.False(IsAssertedIn(codeOnly, "Half"));
            Assert.True(IsAssertedIn(codeOnly, "Guid"));

            // A longer name must not discharge a shorter one that is its prefix: `DateTimeOffset` is not an
            // assertion of `DateTime`.
            Assert.False(IsAssertedIn("Unsafe.SizeOf<DateTimeOffset>()", "DateTime"));
            Assert.True(IsAssertedIn("Unsafe.SizeOf<DateTimeOffset>()", "DateTimeOffset"));

            // The one alias the comparison IS meant to tolerate: `Decimal`/`decimal`, a casing difference in
            // the SAME name, never a different name standing in for it.
            Assert.True(IsAssertedIn("Unsafe.SizeOf<decimal>()", "Decimal"));
            Assert.False(IsAssertedIn("Unsafe.SizeOf<decimal>()", "Guid"));
        }
    }
}
