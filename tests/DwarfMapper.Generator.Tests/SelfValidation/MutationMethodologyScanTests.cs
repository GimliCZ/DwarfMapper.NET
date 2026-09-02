// SPDX-License-Identifier: GPL-2.0-only

using System.Text.Json;
using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     ARCHITECTURAL RULES for the mutation methodology itself — the catalogue and the leg wiring, not
    ///     the product.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         WHY THIS EXISTS. Until round 27 no test read <c>scripts/mutation-battery.sh</c> at all, and the
    ///         catalogue had quietly rotted to where <b>16 of its 33 entries measured nothing</b>: 9 anchors
    ///         matched no line after a refactor renamed the locals they named, and 8 planted mutants did not
    ///         compile — which the compiler caught, so the run printed "killed by &lt;guard&gt;" for a guard
    ///         that never ran. The script reported a score throughout. Every one of those failures is
    ///         statically detectable, and none was detected, because the only thing checking the catalogue
    ///         was the catalogue.
    ///     </para>
    ///     <para>
    ///         These rules are about SHAPE, not score. A score needs the lane, which takes an hour; these run
    ///         in milliseconds and fail the ordinary build, which is where a stale anchor should be caught.
    ///     </para>
    /// </remarks>
    public class MutationMethodologyScanTests
    {
        private static string BatteryPath => Path.Combine(RepoPaths.Root, "scripts", "mutation-battery.sh");

        private static string Battery()
        {
            return File.ReadAllText(BatteryPath);
        }

        private sealed record Entry(string Id, string File, string Expression, string Guard, string Description);

        /// <summary>
        ///     Reads the <c>MUTANTS=( … )</c> array, undoing the shell's escaping exactly as bash does.
        /// </summary>
        /// <remarks>
        ///     The checker that first audited this catalogue did NOT do that — it parsed the rows with awk,
        ///     which applies no shell escaping, and silently mis-read the seven entries containing a
        ///     backslash. A parser must match its consumer, or it measures a different catalogue than the one
        ///     that runs.
        /// </remarks>
        private static List<Entry> Catalogue()
        {
            var entries = new List<Entry>();
            var inArray = false;
            foreach (var raw in Battery().Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.StartsWith("MUTANTS=(", StringComparison.Ordinal))
                {
                    inArray = true;
                    continue;
                }

                if (!inArray)
                {
                    continue;
                }

                if (line.StartsWith(')'))
                {
                    break;
                }

                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] != '"')
                {
                    continue;
                }

                var end = trimmed.LastIndexOf('"');
                if (end <= 0)
                {
                    continue;
                }

                var body = trimmed.Substring(1, end - 1)
                                  .Replace("\\\"", "\"", StringComparison.Ordinal)
                                  .Replace("\\\\", "\\", StringComparison.Ordinal);
                var parts = body.Split('|');
                if (parts.Length != 5)
                {
                    continue;
                }

                entries.Add(new Entry(parts[0], parts[1], parts[2], parts[3], parts[4]));
            }

            return entries;
        }

        /// <summary>The literal text a row searches for, plus its optional address anchor.</summary>
        /// <remarks>
        ///     An APPROXIMATION of sed's BRE, and a conservative one: every metacharacter the catalogue uses
        ///     is either escaped or already literal in BRE, so treating the pattern as literal text can only
        ///     make this check stricter than sed, never looser. A row needing real regex fails here and
        ///     should — these anchors are meant to be readable literals.
        /// </remarks>
        private static (string? Address, string Pattern, bool AnchoredAtLineEnd, string Flags) Anchor(
            string expression)
        {
            var expr = expression;
            string? address = null;

            if (expr.Length > 0 && expr[0] == '/')
            {
                var close = expr.IndexOf('/', 1);
                address = expr.Substring(1, close - 1);
                expr = expr[(close + 1)..];
                expr = expr[expr.IndexOf("s/", StringComparison.Ordinal)..];
            }

            var body = expr.StartsWith("s/", StringComparison.Ordinal) ? expr[2..] : expr;
            var fields = body.Split('/');
            var pattern = fields[0];
            var flags = fields.Length >= 3 ? fields[^1] : string.Empty;

            var anchored = pattern.EndsWith('$');
            if (anchored)
            {
                pattern = pattern[..^1];
            }

            pattern = pattern.Replace("\\*", "*", StringComparison.Ordinal)
                             .Replace("\\[", "[", StringComparison.Ordinal)
                             .Replace("\\]", "]", StringComparison.Ordinal)
                             .Replace("\\.", ".", StringComparison.Ordinal);
            return (address, pattern, anchored, flags);
        }

        private static int Hits(string[] lines, string pattern, bool anchoredAtLineEnd)
        {
            return lines.Count(l => anchoredAtLineEnd
                                        ? l.TrimEnd().EndsWith(pattern, StringComparison.Ordinal)
                                        : l.Contains(pattern, StringComparison.Ordinal));
        }

        [Fact]
        public void M1_no_catalogue_entry_is_stale()
        {
            var offenders = new List<string>();
            foreach (var e in Catalogue())
            {
                var path = Path.Combine(RepoPaths.Root, e.File);
                if (!File.Exists(path))
                {
                    offenders.Add($"{e.Id}: file does not exist — {e.File}");
                    continue;
                }

                var lines = File.ReadAllLines(path);
                var (address, pattern, anchored, _) = Anchor(e.Expression);

                var hits = address is not null
                    ? Hits(lines, address, false)
                    : Hits(lines, pattern, anchored);

                if (hits == 0)
                {
                    offenders.Add($"{e.Id}: matches NOTHING in {e.File} — {e.Description}");
                }
            }

            Assert.True(offenders.Count == 0,
                "scripts/mutation-battery.sh: an entry whose expression matches nothing plants no defect, so " +
                "the run scores it as a defect nobody introduced rather than one nothing caught. Nine entries " +
                "were in this state at once, six of them because a refactor renamed the locals their anchors " +
                "named. Re-anchor the expression, or drop the entry if the behaviour is genuinely gone.\n  " +
                string.Join("\n  ", offenders));
        }

        [Fact]
        public void M1b_an_entry_that_mutates_several_sites_says_so()
        {
            // sed applies to EVERY matching line, so breadth is easy to acquire by accident: M27's guard
            // `policy.RequiredMapping == 1` occurs five times, and an unaddressed anchor would have mutated
            // every endpoint while its description claimed one — silently overlapping M26 instead of testing
            // projection. Breadth is legitimate (M17 deliberately flips all 15 InvariantCulture uses); what
            // is not legitimate is breadth nobody chose. So an entry touching more than one line must DECLARE
            // it, either with the `g` flag or by narrowing with a sed address.
            var offenders = new List<string>();
            foreach (var e in Catalogue())
            {
                var path = Path.Combine(RepoPaths.Root, e.File);
                if (!File.Exists(path))
                {
                    continue;
                }

                var (address, pattern, anchored, flags) = Anchor(e.Expression);
                if (address is not null || flags.Contains('g', StringComparison.Ordinal))
                {
                    continue;
                }

                var hits = Hits(File.ReadAllLines(path), pattern, anchored);
                if (hits > 1)
                {
                    offenders.Add($"{e.Id}: matches {hits} lines in {e.File} with neither a `g` flag nor an " +
                                  "address");
                }
            }

            Assert.True(offenders.Count == 0,
                "scripts/mutation-battery.sh: these entries mutate more than one site without saying so. " +
                "Add the `g` flag if mutating every site is the point, or narrow the row with a sed address " +
                "(/unique nearby text/,+N s/…/…/) if exactly one site is.\n  " +
                string.Join("\n  ", offenders));
        }

        [Fact]
        public void M2_no_entry_plants_a_constant_condition()
        {
            // `if (false)` makes the guarded block unreachable, and CS0162 is an ERROR here
            // (Directory.Build.props: TreatWarningsAsErrors=true, WarningsNotAsErrors empty). Such a mutant
            // never compiles, so the build fails, so every test fails, so the run reports it "killed by" a
            // guard that never ran. Five of round 27's eight compile-breaks were exactly this.
            var constant = new Regex(@"\b(?:if|while)\s*\(\s*(?:false|true)\s*\)", RegexOptions.CultureInvariant);
            var offenders = Catalogue()
                            .Where(e => constant.IsMatch(e.Expression))
                            .Select(e => $"{e.Id}: {e.Expression}")
                            .ToList();

            Assert.True(offenders.Count == 0,
                "scripts/mutation-battery.sh: a constant condition is not a usable mutation form here — the " +
                "block becomes unreachable and CS0162 is an error, so the COMPILER catches the mutant rather " +
                "than a test. Invert the condition instead: same class of defect, both branches reachable.\n  " +
                string.Join("\n  ", offenders));
        }

        [Fact]
        public void M3_every_entry_carries_a_full_identity()
        {
            var entries = Catalogue();
            Assert.True(entries.Count > 0, "scripts/mutation-battery.sh: the MUTANTS catalogue parsed EMPTY.");

            var duplicates = entries.GroupBy(e => e.Id, StringComparer.Ordinal)
                                    .Where(g => g.Count() > 1)
                                    .Select(g => g.Key)
                                    .ToList();
            Assert.True(duplicates.Count == 0,
                "scripts/mutation-battery.sh: duplicate mutant id(s), so a survivor could not be attributed: " +
                string.Join(", ", duplicates));

            foreach (var e in entries)
            {
                Assert.False(string.IsNullOrWhiteSpace(e.Expression), $"{e.Id}: empty expression.");
                Assert.False(string.IsNullOrWhiteSpace(e.Guard), $"{e.Id}: no named guard.");
                Assert.False(string.IsNullOrWhiteSpace(e.Description),
                    $"{e.Id}: no description, so a survivor's report would name no behaviour.");
            }
        }

        [Fact]
        public void M4_the_lane_keeps_the_gates_that_stop_it_rotting_silently()
        {
            var battery = Battery();

            Assert.True(battery.Contains("baseline", StringComparison.OrdinalIgnoreCase),
                "scripts/mutation-battery.sh lost its green-baseline check — against a red suite every " +
                "mutant reads as killed and the run measures nothing.");

            Assert.True(battery.Contains("COMPILE-BREAK", StringComparison.Ordinal),
                "scripts/mutation-battery.sh no longer reports COMPILE-BREAK — a mutant that fails to build " +
                "goes back to being counted as killed by a guard that never ran.");

            Assert.True(battery.Contains("project_for", StringComparison.Ordinal),
                "scripts/mutation-battery.sh lost project_for — without it a mutant is built against the " +
                "wrong project, and a non-compiling one reads as compiling.");

            Assert.True(Regex.IsMatch(battery, @"\$\{#BROKEN\[@\]\}\s*==\s*0"),
                "scripts/mutation-battery.sh no longer FAILS on COMPILE-BREAK entries — reporting a " +
                "catalogue defect without failing is how eight of them survived unnoticed.");

            Assert.True(Regex.IsMatch(battery, @"\$\{#STALE\[@\]\}\s*==\s*0"),
                "scripts/mutation-battery.sh no longer fails on STALE entries.");
        }

        [Fact]
        public void M6_every_adjudicated_mutant_still_names_source_that_exists()
        {
            // A ledger row's identity is leg + file + member + mutator + original → mutated, so an `original`
            // naming source that is gone matches NO mutant Stryker can generate. Found for real in round 27:
            // a runtime row recorded `maxDepth < 1` after DwarfLimits.MinMaxDepth had replaced the literal,
            // and it went on being counted in provenEquivalent — with every total reconciling around it,
            // because the counts are checked against each other and never against the code.
            //
            // Rows whose `original` is deliberately PROSE are skipped: several describe a mutation that has no
            // short literal form ("one `or` in the SpecialType pattern (12 distinct flips)"). Those are
            // identified by their placeholder syntax rather than guessed at.
            var ledgerPath = Path.Combine(RepoPaths.Root, "Issues", "ledgers", "equivalent-mutants.md");
            var fence = Regex.Match(File.ReadAllText(ledgerPath), @"```json\s*(?<json>\{.*?\})\s*```",
                RegexOptions.Singleline);
            Assert.True(fence.Success, "equivalent-mutants.md: the fenced JSON table is gone or unfenced.");

            using var doc = JsonDocument.Parse(fence.Groups["json"].Value);
            var offenders = new List<string>();

            foreach (var entry in doc.RootElement.GetProperty("entries").EnumerateArray())
            {
                var file = entry.GetProperty("file").GetString()!;
                var original = entry.GetProperty("original").GetString()!;

                // Skipped: originals that are a DESCRIPTION rather than a quotation of the source.
                //  * placeholder syntax — "s.Length == 0 ? <empty-quotes literal> : <interpolated quoted s>"
                //  * a counted family — "one `or` in the SpecialType pattern (12 distinct flips)"
                //  * a rendered BLOCK — "{ return null; }" for a Block-removal mutant whose real source is
                //    three indented lines. Writing the block inline is the readable convention and does not
                //    mean the row has rotted; what would is the block's CONTENT no longer existing, which the
                //    literal check below still catches for every non-block row.
                var trimmedOriginal = original.Trim();
                var isDescription = (original.Contains('<', StringComparison.Ordinal)
                                     && original.Contains('>', StringComparison.Ordinal))
                                    || original.StartsWith("one ", StringComparison.OrdinalIgnoreCase)
                                    || original.Contains("(the ", StringComparison.Ordinal)
                                    || (trimmedOriginal.StartsWith('{') && trimmedOriginal.EndsWith('}'));
                if (isDescription)
                {
                    continue;
                }

                var path = Path.Combine(RepoPaths.Root, file);
                if (!File.Exists(path))
                {
                    offenders.Add($"{entry.GetProperty("leg").GetString()}: {file} does not exist");
                    continue;
                }

                if (!File.ReadAllText(path).Contains(original, StringComparison.Ordinal))
                {
                    offenders.Add($"{entry.GetProperty("leg").GetString()}/{entry.GetProperty("member").GetString()}: " +
                                  $"'{original}' no longer appears in {file}");
                }
            }

            Assert.True(offenders.Count == 0,
                "Issues/ledgers/equivalent-mutants.md: these adjudications name source that no longer exists, " +
                "so each describes a mutant that cannot be generated while still being counted in its leg's " +
                "equivalent total. Update the expression if a rename moved it and the proof still holds; " +
                "delete the row if the proof does not.\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void M5_every_mutation_leg_is_named_by_every_site_that_must_know_about_it()
        {
            // Five sites are maintained independently, and a leg gets added by editing some of them: a config
            // with no CI row runs nightly nowhere; a ledger row with no config has no floor to gate; a
            // housekeeping call with no ledger row breaks the badge table. Rather than more hand-kept counts,
            // this asserts the sites name the SAME SET.
            static HashSet<string> Names(string text, string pattern)
            {
                return Regex.Matches(text, pattern)
                            .Select(m => m.Groups[1].Value)
                            .ToHashSet(StringComparer.Ordinal);
            }

            var onDisk = Directory.GetFiles(RepoPaths.Root, "stryker-config*.json")
                                  .Select(p => Path.GetFileName(p))
                                  .ToHashSet(StringComparer.Ordinal);

            var ledger = Names(
                File.ReadAllText(Path.Combine(RepoPaths.Root, "Issues", "ledgers", "equivalent-mutants.md")),
                @"\|\s*`(stryker-config[^`]*\.json)`\s*\|");

            var ci = Names(
                File.ReadAllText(Path.Combine(RepoPaths.Root, ".github", "workflows", "ci.yml")),
                // `*` not `+`: the base leg's file is literally `stryker-config.json`, so a quantifier
                // demanding at least one character between the stem and the extension silently misses it —
                // and a consistency check with a blind spot is worse than none, because it reports a
                // mismatch that is its own.
                @"config:\s*(stryker-config[^\s]*\.json)");

            var housekeeping = Names(
                File.ReadAllText(Path.Combine(RepoPaths.Root, "scripts", "housekeeping.ps1")),
                @"-ConfigFile\s+'(stryker-config[^']*\.json)'");

            Assert.True(onDisk.SetEquals(ledger),
                "the Stryker configs on disk and the legs in equivalent-mutants.md disagree. on disk only: [" +
                string.Join(", ", onDisk.Except(ledger)) + "]; ledger only: [" +
                string.Join(", ", ledger.Except(onDisk)) + "]. The ledger's rows are the badge table's " +
                "source, so a config missing there is a leg with no published score.");

            Assert.True(onDisk.SetEquals(ci),
                "the Stryker configs on disk and the nightly CI matrix disagree. on disk only: [" +
                string.Join(", ", onDisk.Except(ci)) + "]; CI only: [" +
                string.Join(", ", ci.Except(onDisk)) + "]. A config with no CI row is a floor nothing " +
                "measures nightly, which is free to regress silently.");

            Assert.True(onDisk.SetEquals(housekeeping),
                "the Stryker configs on disk and scripts/housekeeping.ps1 disagree. on disk only: [" +
                string.Join(", ", onDisk.Except(housekeeping)) + "]; housekeeping only: [" +
                string.Join(", ", housekeeping.Except(onDisk)) + "]. A config housekeeping never launches is " +
                "a leg no local run exercises.");
        }
    }
}
