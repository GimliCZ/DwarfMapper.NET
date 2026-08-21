// SPDX-License-Identifier: GPL-2.0-only

using System.Text.Json;
using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     The ratchet invariant (R1–R4) as a scan — round 22 P1, per
///     <c>Issues/round22/RESEARCH-97-PERCENT-GATES.md</c> §3 and the maintainer rulings appended there.
///     <para>
///         <b>R1</b> — floors are measurements: every gated floor/break (the three Stryker configs'
///         <c>break</c>, housekeeping's <c>$coverageFloors</c>) equals a measured value at the gate's
///         stated precision and carries dated provenance in the gate's own comment. <b>R2</b> — the
///         mandatory raise exists and is wired (the band functions in <c>scripts/gate-checks.ps1</c>;
///         their both-direction behaviour is proven by <see cref="GateBandLogicTests" />). <b>R3</b> —
///         adjudications are counted categories: the equivalents ledger's per-leg counts are exactly
///         pinned, every entry proof-anchored, no duplicate identities; coverage exclusions in
///         <c>src/</c> are exactly pinned. <b>R4</b> — no gate on a nondeterministic oracle: branch %
///         and wall-clock stay informational, and the in-source <c>Stryker disable</c> population is
///         exactly the one grandfathered H7 progress guard, so no adjudication marker can ride in under
///         ruling (b).
///     </para>
///     <para>
///         The scan reads configs and ledgers; it never writes. Mirrors, not replaces, the runtime-side
///         checks: <c>Assert-StrykerConfigSane</c> (H4) fails a broken config when housekeeping RUNS —
///         this fails it on every test run, which is the measurement-side complement the research asked
///         for.
///     </para>
/// </summary>
public class RatchetInvariantScanTests
{
    // ── R1: floors are measurements with provenance ───────────────────────────

    [Theory]
    [InlineData("stryker-config.json")]
    [InlineData("stryker-config.doctooling.json")]
    [InlineData("stryker-config.runtime.json")]
    public void R1_every_stryker_break_is_internally_consistent_and_carries_dated_measured_provenance(string configFile)
    {
        var path = Path.Combine(RepoPaths.Root, configFile);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        var thresholds = root.GetProperty("stryker-config").GetProperty("thresholds");
        var breakValue = thresholds.GetProperty("break").GetInt32();
        var low = thresholds.GetProperty("low").GetInt32();

        // The H4 shape, statically: Stryker 4.16 refuses low < break WITH EXIT CODE 0, so a mis-ordered
        // config is a silently dead leg. Assert-StrykerConfigSane catches it when housekeeping runs;
        // this catches it on every test run.
        Assert.True(breakValue <= low,
            $"{configFile}: thresholds.break ({breakValue}) > thresholds.low ({low}) — Stryker 4.16 "
            + "refuses to run this config and exits 0, so the leg would be silently dead. Whenever break "
            + "moves, move low with it.");

        // Provenance (R1): the gate's own comment must carry a dated measurement...
        var comment = root.GetProperty("comment").GetString() ?? string.Empty;
        Assert.True(Regex.IsMatch(comment, @"(RE-)?MEASURED \d{4}-\d{2}-\d{2}"),
            $"{configFile}: the comment carries no 'MEASURED YYYY-MM-DD' / 'RE-MEASURED YYYY-MM-DD' "
            + "provenance — a break value without its measurement date is a guess (invariant R1).");

        // ...and at least one measured score in that comment must FLOOR to the configured break, so the
        // break provably equals a measurement at the gate's stated precision (integer truncation), not a
        // number someone liked.
        var scores = Regex.Matches(comment, @"score (\d+\.\d+)\s*%")
            .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        Assert.True(scores.Count > 0,
            $"{configFile}: the comment quotes no 'score NN.NN %' measurement at all — break {breakValue} "
            + "has no measured value to equal (invariant R1).");
        var someScoreFloorsToBreak = scores.Exists(s => (int)Math.Floor(s) == breakValue);
        Assert.True(someScoreFloorsToBreak,
            $"{configFile}: no measured score in the comment floors to break {breakValue} (scores found: "
            + string.Join(", ", scores) + ") — the floor no longer equals any recorded measurement. "
            + "Re-measure and move break (and low) in the same commit as the new figure (invariant R1).");
    }

    [Fact]
    public void R1_the_coverage_floors_are_one_decimal_measured_values_with_dated_provenance()
    {
        var script = HousekeepingText;

        // The floors table itself: exactly the five gated assemblies, each value in the gate's stated
        // precision (one decimal — the truncation is the only slack, no cushion on top).
        var block = Regex.Match(script, @"\$coverageFloors = \[ordered\]@\{(?<body>[^}]*)\}", RegexOptions.Singleline);
        Assert.True(block.Success, "scripts/housekeeping.ps1: the $coverageFloors [ordered]@{...} table is gone — "
                                   + "the coverage gate has nothing to enforce.");
        var floors = Regex.Matches(block.Groups["body"].Value, @"'(?<name>[\w.]+)'\s*=\s*(?<value>\d+(\.\d+)?)")
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value, StringComparer.Ordinal);

        string[] gatedAssemblies =
        [
            "DwarfMapper", "DwarfMapper.Generator", "DwarfMapper.DocTooling",
            "DwarfMapper.CodeFixes", "DwarfMapper.Testing"
        ];
        var exactlyTheFiveGated = floors.Count == gatedAssemblies.Length && gatedAssemblies.All(floors.ContainsKey);
        Assert.True(exactlyTheFiveGated,
            "scripts/housekeeping.ps1: $coverageFloors no longer lists exactly the five gated assemblies "
            + $"(found: {string.Join(", ", floors.Keys)}). Adding or dropping an assembly is a deliberate "
            + "gate change — do it with a measurement and update this scan in the same commit.");

        Assert.All(floors, kvp => Assert.True(Regex.IsMatch(kvp.Value, @"^\d+\.\d$"),
            $"scripts/housekeeping.ps1: floor {kvp.Key} = {kvp.Value} is not in the gate's stated "
            + "one-decimal-truncation format — a floor at the wrong precision cannot equal the "
            + "measurement (invariant R1)."));

        // Provenance: a dated re-measure line, and every floor value appearing as the line half of a
        // 'line/branch' measurement pair in that comment — so each number is traceable to the recorded
        // measurement, not merely present in the table.
        Assert.True(Regex.IsMatch(script, @"Re-measured \d{4}-\d{2}-\d{2}"),
            "scripts/housekeeping.ps1: the $coverageFloors provenance comment lost its "
            + "'Re-measured YYYY-MM-DD' line — floors without a measurement date are guesses (invariant R1).");
        Assert.All(floors, kvp => Assert.True(script.Contains(kvp.Value + "/", StringComparison.Ordinal),
            $"scripts/housekeeping.ps1: floor {kvp.Key} = {kvp.Value} does not appear as the line half of "
            + "a 'line/branch' pair in the provenance comment — the floor and its recorded measurement "
            + "have drifted apart. Re-measure and update both in the same commit (invariant R1)."));
    }

    // ── R2: the mandatory-raise mechanism exists, non-vacuously ───────────────

    [Fact]
    public void R2_the_mandatory_raise_mechanism_exists_and_the_gates_are_wired_to_it()
    {
        // The behaviour (below fails / within passes / a quantum above demands the raise) is proven
        // against the REAL functions by the battery; this asserts the mechanism cannot silently vanish
        // or come unwired. The typeof reference makes deleting the battery a compile error here.
        _ = typeof(GateBandLogicTests);

        var gateChecksPath = Path.Combine(RepoPaths.Root, "scripts", "gate-checks.ps1");
        Assert.True(File.Exists(gateChecksPath),
            "scripts/gate-checks.ps1 is gone — the R2 mandatory-raise band checks no longer exist.");
        var gateChecks = File.ReadAllText(gateChecksPath);

        Assert.Contains("function Test-CoverageWithinBand", gateChecks, StringComparison.Ordinal);
        Assert.Contains("function Assert-MutationScoreWithinBand", gateChecks, StringComparison.Ordinal);
        Assert.Contains("function Assert-LegScoreWithinBand", gateChecks, StringComparison.Ordinal);
        Assert.Contains("raise the floor to the measured value in this commit", gateChecks, StringComparison.Ordinal);

        var housekeeping = HousekeepingText;
        Assert.Contains("gate-checks.ps1", housekeeping, StringComparison.Ordinal);
        Assert.True(housekeeping.Contains("Test-CoverageWithinBand", StringComparison.Ordinal),
            "scripts/housekeeping.ps1 no longer calls Test-CoverageWithinBand — the coverage gate lost "
            + "its R2 raise direction and floors can silently lag measurements again.");

        var legCalls = Regex.Matches(housekeeping, @"Assert-LegScoreWithinBand ").Count;
        Assert.True(legCalls == 3,
            $"scripts/housekeeping.ps1 calls Assert-LegScoreWithinBand {legCalls} time(s), expected "
            + "exactly 3 (one per mutation leg) — a leg whose score is not band-checked can bank slack "
            + "(invariant R2).");
    }

    // ── R3: adjudications are counted categories with proofs ──────────────────

    /// <summary>
    ///     The exact pins for <c>Issues/ledgers/equivalent-mutants.md</c>. Changing any of these numbers
    ///     requires the corresponding proof entry (grow) or proof-invalidating correction (shrink) in the
    ///     SAME commit — that is the R3 rule, and the small-population lesson: populations of ten or
    ///     fewer get exact pins, and so does this one at any size.
    /// </summary>
    private static readonly Dictionary<string, int> PinnedEquivalentCounts = new(StringComparer.Ordinal)
    {
        ["generator|proven-equivalent"] = 16,
        ["generator|probably-equivalent"] = 8,
        // Round-22 P3 grew the doctooling rows by nine, each with its case-analysis proof in the T3
        // ledger's P3 section (same commit): two unreachable-zero IndexOf/FindIndex boundaries, the
        // ambiguous-match ternary evaluated only outside its distinguishing count, a fall-through
        // guaranteed no-op continue, the StartsWith("") loop-exit identity, the '---' separator key no
        // property name can collide with, Stryker's own return-default epilogue reproducing the removed
        // 'return null', and the Format empty-string arm whose two branches agree at the one changed input.
        ["doctooling|proven-equivalent"] = 10,
        // Round-22 P2 grew the runtime rows by two, each with its proof in the E3-E1 round-22 appendix
        // (same commit): the facade TryGet-guard && -> || (proven — the operands co-vary via the
        // TryGetValue out-contract and Register's ThrowIfNull) and the FormatMessage 'Count: > 1'
        // boundary (probably — divergence needs a 1-element list only an off-contract direct ctor call
        // can supply).
        ["runtime|proven-equivalent"] = 2,
        ["runtime|ruled-in-practice"] = 1,
        ["runtime|probably-equivalent"] = 1,
    };

    private const int PinnedEntryRows = 21;
    private const int PinnedTotalOccurrences = 38;

    [Fact]
    public void R3_the_equivalents_ledger_counts_are_exactly_pinned_and_every_entry_is_proof_anchored()
    {
        var ledgerPath = Path.Combine(RepoPaths.Root, "Issues", "ledgers", "equivalent-mutants.md");
        Assert.True(File.Exists(ledgerPath),
            "Issues/ledgers/equivalent-mutants.md is gone — the documented offset behind every raw "
            + "mutation floor has no machine-readable source (ruling (b): adjudication is ledger-only).");

        var fence = Regex.Match(File.ReadAllText(ledgerPath), @"```json\s*(?<json>\{.*?\})\s*```", RegexOptions.Singleline);
        Assert.True(fence.Success, "equivalent-mutants.md: the fenced JSON table is gone or unfenced — "
                                   + "the ledger is no longer machine-readable.");
        using var doc = JsonDocument.Parse(fence.Groups["json"].Value);

        string[] legs = ["generator", "doctooling", "runtime"];
        string[] categories = ["proven-equivalent", "ruled-in-practice", "probably-equivalent"];

        // Row-level obligations: sanctioned leg + category, nothing empty, occurrences positive, the
        // mutated file and the proof anchor both resolving to real files, identities unique.
        var occurrencesByLegCategory = new Dictionary<string, int>(StringComparer.Ordinal);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var rows = 0;
        var totalOccurrences = 0;
        foreach (var entry in doc.RootElement.GetProperty("entries").EnumerateArray())
        {
            rows++;
            var leg = entry.GetProperty("leg").GetString()!;
            var category = entry.GetProperty("category").GetString()!;
            Assert.Contains(leg, legs);
            Assert.Contains(category, categories);

            foreach (var field in new[] { "file", "member", "mutator", "original", "mutated", "proof", "anchor" })
                Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty(field).GetString()),
                    $"equivalent-mutants.md: an entry in leg '{leg}' has an empty '{field}' — every "
                    + "adjudication carries its full identity and proof (invariant R3).");

            var occurrences = entry.GetProperty("occurrences").GetInt32();
            Assert.True(occurrences >= 1, $"equivalent-mutants.md: an entry in leg '{leg}' pins occurrences "
                                          + $"{occurrences} — a row that counts nothing adjudicates nothing.");
            totalOccurrences += occurrences;

            var file = entry.GetProperty("file").GetString()!;
            Assert.True(File.Exists(Path.Combine(RepoPaths.Root, file)),
                $"equivalent-mutants.md: entry file '{file}' does not exist — the adjudicated code moved "
                + "or was deleted; update or retire the entry with the reason in the same commit.");

            var anchorFile = entry.GetProperty("anchor").GetString()!.Split(' ')[0].TrimEnd(';');
            Assert.True(File.Exists(Path.Combine(RepoPaths.Root, anchorFile)),
                $"equivalent-mutants.md: proof anchor '{anchorFile}' does not resolve to a file — an "
                + "adjudication whose proof cannot be found is an allowlist (invariant R3).");

            var identity = string.Join("|", leg, file,
                entry.GetProperty("member").GetString(), entry.GetProperty("mutator").GetString(),
                entry.GetProperty("original").GetString(), entry.GetProperty("mutated").GetString());
            Assert.True(identities.Add(identity),
                $"equivalent-mutants.md: duplicate mutant identity '{identity}' — one mutant, one entry, "
                + "or a count can be inflated without a proof.");

            var key = leg + "|" + category;
            occurrencesByLegCategory[key] = occurrencesByLegCategory.GetValueOrDefault(key) + occurrences;
        }

        // The exact pins (R3), then the summary cross-checks so the human-readable numbers cannot drift
        // from the rows they summarize.
        Assert.True(rows == PinnedEntryRows && totalOccurrences == PinnedTotalOccurrences,
            $"equivalent-mutants.md: {rows} entry rows / {totalOccurrences} adjudicated mutants; pinned "
            + $"{PinnedEntryRows} / {PinnedTotalOccurrences}. Growing requires the case-analysis proof in "
            + "the same commit; shrinking requires the correction that invalidates one. Move the pin WITH "
            + "the proof (invariant R3).");
        foreach (var (key, pinned) in PinnedEquivalentCounts)
            Assert.True(occurrencesByLegCategory.GetValueOrDefault(key) == pinned,
                $"equivalent-mutants.md: {key} sums to {occurrencesByLegCategory.GetValueOrDefault(key)}, "
                + $"pinned {pinned} — same rule: the pin moves only with the proof, in the same commit.");
        Assert.True(occurrencesByLegCategory.Count == PinnedEquivalentCounts.Count,
            "equivalent-mutants.md: a leg/category combination exists in the rows that carries no pin "
            + "here — pin it in the same commit as its first entry.");

        foreach (var leg in legs)
        {
            var summary = doc.RootElement.GetProperty("legs").GetProperty(leg);
            var scoreable = summary.GetProperty("scoreable").GetInt32();
            var proven = summary.GetProperty("provenEquivalent").GetInt32();
            Assert.True(proven == occurrencesByLegCategory.GetValueOrDefault(leg + "|proven-equivalent"),
                $"equivalent-mutants.md: leg '{leg}' summary says {proven} proven-equivalent, the rows "
                + "sum differently — the summary is lying about its own table.");
            Assert.True(summary.GetProperty("ruledInPractice").GetInt32()
                        == occurrencesByLegCategory.GetValueOrDefault(leg + "|ruled-in-practice"),
                $"equivalent-mutants.md: leg '{leg}' ruledInPractice summary != row sum.");
            Assert.True(summary.GetProperty("probablyEquivalent").GetInt32()
                        == occurrencesByLegCategory.GetValueOrDefault(leg + "|probably-equivalent"),
                $"equivalent-mutants.md: leg '{leg}' probablyEquivalent summary != row sum.");

            // rawCeiling = (scoreable − proven)/scoreable truncated to two decimals — the documented
            // offset arithmetic, recomputed rather than trusted.
            var expectedCeiling = Math.Floor((scoreable - proven) * 10000.0 / scoreable) / 100;
            Assert.True(Math.Abs(summary.GetProperty("rawCeiling").GetDouble() - expectedCeiling) < 0.001,
                $"equivalent-mutants.md: leg '{leg}' rawCeiling {summary.GetProperty("rawCeiling").GetDouble()} "
                + $"!= (({scoreable} - {proven}) / {scoreable}) truncated = {expectedCeiling} — the offset "
                + "must be arithmetic over the pinned counts, never a hand-typed number.");

            // R1 ↔ R3 coupling: the ledger's recorded raw score must floor to the config's break, so a
            // leg re-measure that moves the gate refreshes the ledger summary in the same commit.
            var configFile = summary.GetProperty("config").GetString()!;
            using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, configFile)));
            var breakValue = config.RootElement.GetProperty("stryker-config")
                .GetProperty("thresholds").GetProperty("break").GetInt32();
            var measured = summary.GetProperty("measuredRawScore").GetDouble();
            Assert.True((int)Math.Floor(measured) == breakValue,
                $"equivalent-mutants.md: leg '{leg}' records measuredRawScore {measured} but {configFile} "
                + $"has break {breakValue} — the leg was re-measured without refreshing the ledger summary "
                + "(and its scoreable/ceiling arithmetic) in the same commit.");
        }
    }

    [Fact]
    public void R3_coverage_exclusions_in_src_are_exactly_pinned_and_individually_justified()
    {
        // P4's instrument, guarded from day one (the ruling-(b) boundary: [ExcludeFromCodeCoverage] with
        // a sanctioned justification is the coverage-denominator mechanism the reframe covers — it is NOT
        // a score adjudication, and it is pinned exactly like one anyway). Round-22 P4 classified every
        // 0%-covered class in the five gated assemblies (all ten sat in src/DwarfMapper) and excluded the
        // NINE that fit the one sanctioned category — compile-time-only attributes the generator reads
        // from the semantic model, every one carrying [DwarfSurface] so the surface catalog is its
        // non-vacuity anchor: AutoNest, FlattenGraph, GenerateWrapperMap, MapCollectionKey,
        // MapDerivedType (non-generic), MapIgnoreSource, and the pair-scoped MapProperty<,>, MapIgnore<>,
        // MapValue<> (51 by-design-dead lines out of the denominator). The tenth, MapToAttribute, STAYS
        // at 0/4: its ctor's `targets ?? Array.Empty<Type>()` is a defensive arm — research Q3's example
        // of a category that needs a maintainer ruling first, a hole by definition until ruled.
        const int pinnedExclusionCount = 9;

        var occurrences = new List<string>();
        foreach (var file in RepoPaths.SourceFiles(RepoPaths.Src))
        {
            var text = File.ReadAllText(file);
            for (var i = text.IndexOf("ExcludeFromCodeCoverage", StringComparison.Ordinal); i >= 0;
                 i = text.IndexOf("ExcludeFromCodeCoverage", i + 1, StringComparison.Ordinal))
            {
                var relative = Path.GetRelativePath(RepoPaths.Root, file).Replace('\\', '/');
                occurrences.Add(relative);

                // The justification obligation, live from the first occurrence: non-empty and naming the
                // one sanctioned category (research Q3 — further categories need a maintainer ruling and
                // get added HERE when ruled).
                var window = text.Substring(i, Math.Min(400, text.Length - i));
                Assert.True(Regex.IsMatch(window, @"Justification\s*=\s*""[^""]+"""),
                    $"{relative}: an [ExcludeFromCodeCoverage] without a non-empty Justification — an "
                    + "unexplained exclusion is an allowlist (invariant R3).");
                Assert.True(window.Contains("compile-time-only", StringComparison.Ordinal),
                    $"{relative}: [ExcludeFromCodeCoverage] justification does not name a sanctioned "
                    + "category (the only pre-approved one is 'compile-time-only attribute, consumed by "
                    + "the generator' — research Q3; further categories need a maintainer ruling first).");
            }
        }

        Assert.True(occurrences.Count == pinnedExclusionCount,
            $"src/ carries {occurrences.Count} [ExcludeFromCodeCoverage] use(s), pinned "
            + $"{pinnedExclusionCount} ({string.Join(", ", occurrences)}) — every exclusion is a "
            + "denominator change: move this pin in the same commit, with the sanctioned justification "
            + "on the attribute (invariant R3 / P4).");
    }

    // ── R4: no gate on a nondeterministic oracle; no new in-source disable ────

    [Fact]
    public void R4_the_in_source_stryker_disable_population_is_exactly_the_grandfathered_H7_guard()
    {
        // Ruling (b) rejected in-source adjudication markers outright. The single grandfathered
        // 'Stryker disable' is H7's DocSnippetInjector progress guard — test-infrastructure honesty
        // (its 7 mutants are reachable only when the loop body is already defective), NOT score
        // adjudication. Pinned at exactly one, in exactly that file, with the guard's own reason on the
        // line — so no adjudication comment can ride in under the exception, anywhere in src/.
        var disables = new List<(string File, string Line)>();
        var restores = new List<string>();
        foreach (var file in RepoPaths.SourceFiles(RepoPaths.Src))
        {
            var relative = Path.GetRelativePath(RepoPaths.Root, file).Replace('\\', '/');
            foreach (var line in File.ReadLines(file))
            {
                if (line.Contains("Stryker disable", StringComparison.OrdinalIgnoreCase))
                    disables.Add((relative, line.Trim()));
                if (line.Contains("Stryker restore", StringComparison.OrdinalIgnoreCase))
                    restores.Add(relative);
            }
        }

        Assert.True(disables.Count == 1,
            $"src/ carries {disables.Count} 'Stryker disable' comment(s), pinned exactly 1 (the H7 "
            + "progress guard). A new one is an in-source adjudication marker — REJECTED by ruling (b): "
            + "equivalents go in Issues/ledgers/equivalent-mutants.md with a proof, and the raw score "
            + "stays depressed as a documented offset.\n  "
            + string.Join("\n  ", disables.Select(d => $"{d.File}: {d.Line}")));
        Assert.EndsWith("DocSnippetInjector.cs", disables[0].File, StringComparison.Ordinal);
        Assert.Contains("progress guard", disables[0].Line, StringComparison.Ordinal);
        Assert.True(restores.Count == 1 && restores[0] == disables[0].File,
            "src/: the single 'Stryker disable all' must be paired with exactly one 'Stryker restore all' "
            + "in the same file — an unrestored disable silently widens the Ignored population to the "
            + "rest of the file.");
    }

    [Fact]
    public void R4_no_gate_reads_branch_coverage_or_a_wall_clock()
    {
        // Branch % wobbles with run order (measured: Testing 81.9–82.2 with no code change) and
        // wall-clock moves with load — the H7 Timeout lesson generalized: a gate on a nondeterministic
        // oracle converts scheduler noise into red builds. Both stay informational; the gates' own
        // comments name their deterministic oracles.
        foreach (var scriptName in new[] { "housekeeping.ps1", "gate-checks.ps1" })
        {
            var lines = File.ReadAllLines(Path.Combine(RepoPaths.Root, "scripts", scriptName));
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("branchcoverage", StringComparison.OrdinalIgnoreCase))
                    Assert.False(Regex.IsMatch(line, @"-(lt|le|ge|gt)\b")
                                 || line.Contains("throw", StringComparison.Ordinal)
                                 || line.Contains("$failures", StringComparison.Ordinal),
                        $"scripts/{scriptName}:{i + 1}: branch coverage feeds a comparison or a failure — "
                        + "branch % is a nondeterministic oracle and must stay informational (invariant "
                        + $"R4): {line.Trim()}");

                if (Regex.IsMatch(line, @"Elapsed|TotalSeconds|TotalMinutes"))
                    Assert.False(line.Contains("throw", StringComparison.Ordinal)
                                 || line.Contains("$failures", StringComparison.Ordinal),
                        $"scripts/{scriptName}:{i + 1}: a wall-clock value feeds a failure — wall-clock is "
                        + $"measured and recorded, never gated (invariant R4): {line.Trim()}");
            }
        }

        // The declaration itself must survive: the comment naming line coverage as the gate and branch
        // as informational is the R4 provenance the research asked the gates to carry. (Asserted as a
        // regex because the sentence wraps across comment lines in the script.)
        Assert.True(
            Regex.IsMatch(HousekeepingText, @"branch coverage is printed as\s*(\r?\n#\s*)?informational only"),
            "scripts/housekeeping.ps1: the floors comment no longer declares 'branch coverage is printed "
            + "as informational only' — the gate stopped naming its oracle (invariant R4).");
    }

    // ── shared ────────────────────────────────────────────────────────────────

    private static string? _housekeepingText;

    private static string HousekeepingText => _housekeepingText ??=
        File.ReadAllText(Path.Combine(RepoPaths.Root, "scripts", "housekeeping.ps1"));
}
