// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Renders the README's quality badges from the GATES' OWN FILES — the coverage floors out of
    ///     <c>scripts/housekeeping.ps1</c>, the mutation raw scores out of
    ///     <c>Issues/ledgers/equivalent-mutants.md</c>, and each leg's <c>break</c> out of the
    ///     <c>stryker-config*.json</c> the ledger itself names.
    ///     <para>
    ///         The badges are plain shields.io static markdown, so there is no external service and no token:
    ///         nothing is read at page-view time, and nothing can silently start answering a different number.
    ///         The price of that is staleness, which is why every value here is READ rather than typed and the
    ///         result is byte-compared by <c>DocsAreSnippetCurrentTests</c>. Move a floor and the README stops
    ///         matching in the same commit; hand-edit a rendered number and the doc test goes red naming
    ///         <c>README.md</c>. A quality badge that can quietly lie is worse than no badge at all — this
    ///         repository exists to turn that class of drift into a build failure.
    ///     </para>
    ///     <para>
    ///         <b>Why this lives in the test project</b> rather than in <c>DwarfMapper.DocTooling</c>, where the
    ///         snippet/table injectors live: DocTooling is a coverage-floored assembly (95.7 %) and a mutation
    ///         target, so a renderer added there moves the R2 coverage band and — if listed in the leg's
    ///         <c>mutate</c> globs — forces a Stryker re-measure and a ledger edit. The measured numbers must
    ///         not move because the thing that RENDERS them was added. <c>GeneratedDocsAreCurrentTests</c> sets
    ///         the precedent: its diagnostics-index and option-matrix renderers are test-project code for the
    ///         same reason.
    ///     </para>
    /// </summary>
    internal static partial class QualityBadgeRenderer
    {
        /// <summary>The marker name of the README region this renderer owns.</summary>
        public const string TableName = "quality-badges";

        private const string HousekeepingPath = "scripts/housekeeping.ps1";
        private const string LedgerPath = "Issues/ledgers/equivalent-mutants.md";

        /// <summary>
        ///     The R2 band width, in points, shared by BOTH gates: <c>Test-CoverageWithinBand</c> refuses a
        ///     measurement <c>&gt;= floor + 1.0</c> pp and <c>Assert-MutationScoreWithinBand</c> refuses a score
        ///     that floors to <c>break + 1</c>. It is the only quantum either gate recognises, and it is what the
        ///     colours below are derived from — see <see cref="BandColour" />.
        /// </summary>
        private const double BandWidth = 1.0;

        // ─────────────────────────────────────────────────────────────────────────
        // Readers. Each one refuses loudly when the file it reads no longer has the shape it expects: a reader
        // that silently found nothing would render an empty region, the committed README would match it, and
        // the badges would vanish with every gate still green.
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>How many assemblies <c>$coverageFloors</c> must declare. A vacuity floor, not a list.</summary>
        private const int ExpectedCoverageFloors = 5;

        /// <summary>How many legs the mutation ledger's per-leg summary must carry.</summary>
        /// <remarks>
        ///     Four since round 27 added the code-fix leg. This is a VACUITY floor, not a list: it exists so a
        ///     reader that silently parsed fewer rows than are gated cannot render a short table that the
        ///     committed document then matches.
        /// </remarks>
        private const int ExpectedMutationLegs = 5;

        /// <summary>
        ///     Colour, derived from the gate's own verdict on the rendered number — never hand-assigned per
        ///     badge, because a hand-assigned colour is a second thing to keep in step with the number beside it
        ///     and it rots silently (green on a number that has fallen below its floor is exactly the lie this
        ///     region exists to prevent).
        ///     <para>
        ///         The derivation is `scripts/gate-checks.ps1`'s two band checks, which have one shape between
        ///         them: below the gate is a FAILURE (red); inside <c>[gate, gate + 1)</c> the gate passes and
        ///         the pinned number still equals the measurement (bright green); at or above <c>gate + 1</c> the
        ///         run passes but invariant R2 is broken — the pin no longer equals what was measured and the
        ///         gate demands a re-measure (yellow, because that is a stale pin rather than a regression).
        ///     </para>
        ///     <para>
        ///         <paramref name="step" /> is the granularity the corresponding gate truncates to before
        ///         comparing, so the badge cannot disagree with the check: coverage truncates to tenths of a
        ///         point, mutation floors to whole points.
        ///     </para>
        /// </summary>
        internal static string BandColour(double measured, double gate, double step)
        {
            // Same truncate-then-compare the two PowerShell checks do (they round to 6 places first, so a
            // measurement that is a hair under a tenth by float representation is not truncated a step down).
            var truncated = Math.Floor(Math.Round(measured / step, 6)) * step;

            if (truncated < gate)
            {
                return "red";
            }

            if (truncated >= gate + BandWidth)
            {
                return "yellow";
            }

            return "brightgreen";
        }

        /// <summary>
        ///     The rendered region body: the coverage badges, the mutation badges, and a note carrying each
        ///     leg's break value and the files everything was read from.
        /// </summary>
        public static IReadOnlyList<string> RenderRows()
        {
            var floors = CoverageFloors();
            var legs = MutationLegs();

            var rows = new List<string>();

            foreach (var (assembly, floor) in floors)
            {
                // The floor IS the measurement (invariant R2), so it is graded against itself: the badge says
                // "this is the enforced floor and the gate is satisfied at it". A floor that ever stopped being
                // the measurement is caught by Test-CoverageWithinBand, not by a colour.
                var colour = BandColour(floor, floor, 0.1);
                rows.Add(Badge($"coverage {assembly}", Percent(floor, 1), colour, HousekeepingPath));
            }

            rows.Add(string.Empty);

            foreach (var leg in legs)
            {
                var colour = BandColour(leg.RawScore, leg.Break, 1.0);
                rows.Add(Badge($"mutation {leg.Name}", Percent(leg.RawScore, 2), colour, leg.ConfigFile));
            }

            rows.Add(string.Empty);
            rows.Add("<sub>Coverage figures are the enforced per-assembly line-coverage floors from " +
                     $"[`{HousekeepingPath}`]({HousekeepingPath}); mutation figures are the RAW measured scores " +
                     $"from [`{LedgerPath}`]({LedgerPath}), gated at " +
                     string.Join(", ",
                         legs.Select(l =>
                             string.Create(CultureInfo.InvariantCulture, $"break {l.Break} ({l.Name})"))) +
                     ". Every number here is read from those files and byte-compared by the doc suite, so a " +
                     "stale badge is a failing build rather than a quiet lie. Each score describes only the " +
                     "files its own leg names, which is a minority of `src/`; " +
                     $"[what sits outside every leg]({ScopeAuditPath}) is measured there.</sub>");

            return rows;
        }

        /// <summary>
        ///     The scope audit, LINKED rather than quoted. A percentage baked in here would be a second copy
        ///     of a measured number with nothing comparing the two — precisely the failure this generated
        ///     table exists to prevent — so the caption points at the file that measures it instead.
        /// </summary>
        private const string ScopeAuditPath = "Issues/round27/AUDIT-mutation-scope.md";

        private static string Percent(double value, int decimals)
        {
            return value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + "%25";
        }

        /// <summary>
        ///     One shields.io static badge. The label is percent-escaped for spaces and the value already
        ///     carries its <c>%25</c>; a literal dash would have to be doubled, which is why no label here
        ///     contains one — the separator in shields' <c>/badge/label-message-colour</c> path IS the dash.
        /// </summary>
        internal static string Badge(string label, string value, string colour, string linkTarget)
        {
            if (label.Contains('-', StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Badge label '{label}' contains a dash, which shields.io reads as the label/value separator. " + "Double it ('--') or rename the label; rendering it raw would silently mangle the badge.");
            }

            var escaped = label.Replace(" ", "%20", StringComparison.Ordinal);
            return $"[![{label}](https://img.shields.io/badge/{escaped}-{value}-{colour})]({linkTarget})";
        }

        private static IReadOnlyList<CoverageFloor> CoverageFloors()
        {
            return ParseCoverageFloors(File.ReadAllText(Path.Combine(RepoLayout.Root, HousekeepingPath)));
        }

        /// <summary>Split from the file read so the reader can be driven against fixture text in its own tests.</summary>
        internal static IReadOnlyList<CoverageFloor> ParseCoverageFloors(string housekeeping)
        {
            const string opener = "$coverageFloors = [ordered]@{";
            var open = housekeeping.IndexOf(opener, StringComparison.Ordinal);
            if (open < 0)
            {
                throw new InvalidOperationException(
                    $"{HousekeepingPath}: no '{opener}' block. The README's coverage badges are rendered from it; " + "if the floors moved somewhere else, point this reader at the new home rather than letting " + "the badges render from nothing.");
            }

            var close = housekeeping.IndexOf('}', open + opener.Length);
            if (close < 0)
            {
                throw new InvalidOperationException($"{HousekeepingPath}: '{opener}' is never closed with '}}'.");
            }

            var body = housekeeping[(open + opener.Length)..close];
            var floors = new List<CoverageFloor>();
            foreach (var line in body.Split('\n'))
            {
                var match = FloorLine().Match(line);
                if (!match.Success)
                {
                    continue;
                }

                floors.Add(new CoverageFloor(match.Groups["name"].Value,
                    double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture)));
            }

            if (floors.Count != ExpectedCoverageFloors)
            {
                throw new InvalidOperationException(
                    $"{HousekeepingPath}: parsed {floors.Count} coverage floor(s), expected {ExpectedCoverageFloors}. " +
                    "Either an assembly joined or left the gate — in which case update ExpectedCoverageFloors in " +
                    "the same commit as the badge it adds or removes — or the block's line shape changed and this " +
                    "reader is now silently seeing fewer floors than the gate enforces.");
            }

            foreach (var floor in floors)
                if (floor.Floor is <= 0 or > 100)
                {
                    throw new InvalidOperationException(
                        $"{HousekeepingPath}: coverage floor for {floor.Assembly} parsed as {floor.Floor}, which is " + "not a percentage. The parse is wrong, not the gate.");
                }

            return floors;
        }

        private static List<MutationLeg> MutationLegs()
        {
            var ledger = File.ReadAllText(Path.Combine(RepoLayout.Root, LedgerPath));
            var rows = ParseLedgerRows(ledger);

            return rows.Select(row =>
            {
                var configPath = Path.Combine(RepoLayout.Root, row.ConfigFile);
                if (!File.Exists(configPath))
                {
                    throw new InvalidOperationException(
                        $"{LedgerPath}: leg '{row.Name}' names config '{row.ConfigFile}', which does not exist. " + "The ledger row and the gate it describes have come apart.");
                }

                return row with
                {
                    Break = ReadBreak(row.ConfigFile, File.ReadAllText(configPath))
                };
            }).ToList();
        }

        /// <summary>Split from the file read so the reader can be driven against fixture text in its own tests.</summary>
        internal static IReadOnlyList<MutationLeg> ParseLedgerRows(string ledger)
        {
            var legs = new List<MutationLeg>();
            foreach (Match match in LedgerRow().Matches(ledger))
                legs.Add(new MutationLeg(
                    match.Groups["leg"].Value,
                    match.Groups["config"].Value,
                    double.Parse(match.Groups["raw"].Value, CultureInfo.InvariantCulture),
                    0));

            if (legs.Count != ExpectedMutationLegs)
            {
                throw new InvalidOperationException(
                    $"{LedgerPath}: parsed {legs.Count} mutation leg row(s), expected {ExpectedMutationLegs}. A leg " + "was added or removed (update ExpectedMutationLegs with its badge), or the per-leg summary " + "table's shape changed and this reader is now reading fewer legs than are gated.");
            }

            foreach (var leg in legs)
                if (leg.RawScore is <= 0 or > 100)
                {
                    throw new InvalidOperationException(
                        $"{LedgerPath}: raw score for leg {leg.Name} parsed as {leg.RawScore}, which is not a " + "percentage. The parse is wrong, not the ledger.");
                }

            return legs;
        }

        /// <summary>Split from the file read so the reader can be driven against fixture text in its own tests.</summary>
        internal static int ReadBreak(string configFile, string json)
        {
            using var document = JsonDocument.Parse(json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

            if (!document.RootElement.TryGetProperty("stryker-config", out var config) || !config.TryGetProperty("thresholds", out var thresholds) || !thresholds.TryGetProperty("break", out var breakValue) || !breakValue.TryGetInt32(out var result))
            {
                throw new InvalidOperationException(
                    $"{configFile}: no integer 'stryker-config.thresholds.break'. The badge grades the ledger's raw " + "score against that number; without it there is nothing to grade against.");
            }

            if (result is <= 0 or >= 100)
            {
                throw new InvalidOperationException(
                    $"{configFile}: break parsed as {result}, which is not a percentage threshold.");
            }

            return result;
        }

        [GeneratedRegex(@"^\s*'(?<name>[^']+)'\s*=\s*(?<value>\d+(?:\.\d+)?)\s*$")]
        private static partial Regex FloorLine();

        // The ledger's per-leg summary row: "| generator | `stryker-config.json` | 201 | 81.59 % (…) | …".
        // Anchored on the backticked config filename so the prose tables further down the file — which have a
        // different column order and no config column — cannot be mistaken for it.
        [GeneratedRegex(@"^\|\s*(?<leg>[a-z][a-z0-9.-]*)\s*\|\s*`(?<config>stryker-config[^`]*\.json)`\s*\|" + @"[^|]*\|\s*(?<raw>\d+(?:\.\d+)?)\s*%", RegexOptions.Multiline)]
        private static partial Regex LedgerRow();

        /// <summary>A coverage floor as <c>scripts/housekeeping.ps1</c> declares it.</summary>
        internal readonly record struct CoverageFloor(string Assembly, double Floor);

        /// <summary>
        ///     A mutation leg, joined across the two files that describe it: the ledger row names the leg and
        ///     its config and carries the RAW measured score, the config carries the <c>break</c> the gate
        ///     enforces. Rendering both from their own file is what makes a divergence between them visible —
        ///     a ledger re-measure that forgets to move <c>break</c> renders the badge off-green.
        /// </summary>
        internal readonly record struct MutationLeg(string Name, string ConfigFile, double RawScore, int Break);
    }
}
