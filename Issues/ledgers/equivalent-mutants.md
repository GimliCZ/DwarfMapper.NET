<!-- SPDX-License-Identifier: GPL-2.0-only -->

# The equivalents ledger — every adjudicated-equivalent mutant, machine-readable

Round 22, task P1. Created 2026-08-21, per maintainer ruling (b) in
`Issues/round22/RESEARCH-97-PERCENT-GATES.md`: **adjudication is ledger-only**. No in-source
score-adjudication marker exists or may exist (`RatchetInvariantScanTests` pins the single grandfathered
`// Stryker disable` — the H7 progress guard, test-infrastructure protection, not adjudication — at
exactly one). Every gate operates on the RAW measured score; this file is the **documented offset** that
explains the gap between each leg's raw score and 100 %: the proven-equivalent residue stays in the
denominator and can never be detected, so each leg's honest asymptote is its `rawCeiling` below.

**This file transcribes; it never adjudicates.** Every entry's proof was written in an existing ledger
(`T3-mutation-survivors.md`, `E3-E1-report.md`, `H7-timeout-dissection.md`, or a recorded maintainer
ruling); the `anchor` field points at it. Growing an entry requires the case-analysis proof in the same
commit (the T3 bar: *original and mutant agree on every reachable input*); shrinking one requires the
correction that invalidates the proof. `RatchetInvariantScanTests` (R3) pins the per-leg, per-category
counts exactly and cross-checks them against the row sums, so neither direction can move silently.

**Identity convention.** Line numbers drift as files are edited (the DocTooling entry already moved
83 → 97 when the H7 guard landed above it), so a mutant's identity is
`leg + file + member + mutator + original → mutated expression`; `lineAtProof` records where the proof's
source run saw it (informational), `lineCurrent` where the expression sits at this file's writing.

**Categories** (the plan's three proof grades):

- `proven-equivalent` — a written case analysis shows original and mutant agree on every reachable input.
- `ruled-in-practice` — a maintainer ruling that divergence requires an input no honest test produces.
- `probably-equivalent` — the ledger argues equivalence but stops short of a full proof; explicitly
  low-priority, never "do not attempt" — a future proof may move it either way, with the entry.

## Anchor maintenance, 2026-08-27 — the identity fields were drifting

Round 27's own review asked whether the relationship structures around the gates were being maintained. For
this file the answer was "mostly": every `file` resolved and every `anchor` ledger existed, but the
*positional* and *textual* fields had rotted.

**One row named source that no longer exists.** `DwarfRefContext..ctor (lower clamp)` recorded
`maxDepth < 1`, and round 27 introduced `src/Shared/DwarfLimits.cs`, replacing the literal with
`DwarfLimits.MinMaxDepth`. A row's identity is `leg + file + member + mutator + original → mutated`, so an
`original` naming source that is gone matches **no mutant Stryker can generate** — the entry was still being
counted in `provenEquivalent`, and the count still reconciled, while describing a mutant that did not exist.
The expression is updated; **the proof is unchanged**, because `MinMaxDepth == 1` and the two forms still
differ only at `maxDepth == 1`, where both yield 1.

**Seventeen `lineCurrent` values were stale**, several by hundreds of lines — `BlittableProof` entries at 29,
80, 81, 85 and 86 actually sit at 274, 370, 371, 378 and 379 after the repo-wide reformat. That field is
documented as informational, and it is: nothing gates on it. But a reader checking a proof follows the line
number, and one that lands in unrelated code costs exactly the trust the ledger exists to hold. All
seventeen are recomputed by locating the row's own `original` text; four rows whose `original` is prose
(`<empty-quotes literal>`, "one `or` in the SpecialType pattern") or ambiguous (`continue;`) are left
alone, because for those there is nothing unambiguous to locate.

## Denominators moved on 2026-08-27 — recorded, not absorbed

Two legs' scoreable populations changed with no edit to the code they mutate, so the reason is written down
here rather than left as an unexplained number.

**generator, 258 → 338.** The four files this leg mutates — `EquatableArray`, `BlittableProof`,
`ConstructorSelector`, `LocationInfo` — are byte-identical on this branch: zero commits, empty diff. What
changed is the classification around them. Stryker mutates and compiles the WHOLE project and only then
filters, and compile-error rollback is a *compilation-dependent* verdict: 4,142 rollbacks became 3,240, and
the whole-project mutant population grew 11,389 → 13,129 with the code round 27 added. Round 27 also
switched the legs from Debug to Release builds when repairing the launcher. The previous figure dates from
2026-08-23, with rounds 25, 26 and 27 in between, so it was stale by more than one cause.

The score moved 84.88 % → 84.32 %, still above its floor of 84. Worth reading as an arithmetic rather than
a decline: ~219 detected of 258 became 285 of 338, so the 80 newly-scoreable mutants are being killed at
about 82 % — slightly below the existing rate, which is exactly why the overall figure dips a third of a
point while more mutants die than before.

**runtime, 119 → 125.** This one has an ordinary cause: round 27 bound each map table to its own ambiguity
set (`RegistryTable<TDelegate>`), which changed what there is to mutate in `DwarfMapperRegistry.cs`. Score
97.48 % → 97.60 %.

Both ceilings are recomputed from the new denominators in the same commit, as the rule below requires.

## Per-leg summary — counts, raw ceilings, offsets

`rawCeiling` = `(scoreable − provenEquivalent) / scoreable`, truncated to two decimals: the highest raw
score the leg can reach while every proven-equivalent mutant stays in the denominator (ruling (b): it
never leaves). Denominators are the current authoritative runs; a re-measure that moves a denominator
recomputes the ceilings in the same commit.

| Leg | Config | Scoreable | Raw score (measured) | proven | ruled-in-practice | probably | rawCeiling |
|---|---|---:|---:|---:|---:|---:|---:|
| generator | `stryker-config.json` | 338 | 84.32 % (2026-08-27, round-27 battery) | 24 | 0 | 6 | 92.89 % |
| doctooling | `stryker-config.doctooling.json` | 289 | 95.85 % (2026-08-23, round-24 kill program) | 10 | 0 | 0 | 96.53 % |
| runtime | `stryker-config.runtime.json` | 125 | 97.60 % (2026-08-27, round-27 battery) | 2 | 1 | 1 | 98.40 % |
| codefixes | `stryker-config.codefixes.json` | 177 | 87.01 % (2026-08-26, round-27 kill program) | 22 | 0 | 1 | 87.57 % |
| pipeline | `stryker-config.pipeline.json` | 239 | 76.99 % (2026-08-27, first measurement) | 0 | 0 | 0 | 100.00 % |

Fuller arithmetic, carried from the research and updated by P5 (context, not gates): the generator leg's
*realistic* raw ceiling is lower than 88.05 — the 6 probably-equivalent survivors and the 3 NoCoverage
mutants T3 judged dead-code-question (BlittableProof L30's short-circuited conjunct, ConstructorSelector
L281/L285) plus the L88 flag question cap the currently killable set at the 3 named real holes
(`EquatableArray.GetHashCode` ×2 and `IsSourceSequential`'s `Any → All`), ≈ 167/201 = 83.08 % raw, until
the maintainer's dead-code rulings (research Q2) land. The runtime figure net of the ruled-in-practice and
probably-equivalent entries is 109/113 = 96.46 % raw — and net of the filed-uncoverable
`Key.Equals(object)` override (E3-E1 hole 7, a denominator question awaiting the maintainer, not a ledger
entry) the leg's currently killable set is exactly that 109. The research's oft-quoted
"generator ≈ 89.8 ceiling" is 167/186 — an
**adjudicated-denominator** figure that predates ruling (b); it is not a raw number and no raw gate may
be set from it.

### Discrepancy flag — the generator proven count is 16, not the "15" the research/plan carried

`T3-mutation-survivors.md` says "thirteen" `LayoutIdentical`/`IsPrimitive` equivalents in prose, and the
research/plan carried "15 proven" (13 + ConstructorSelector L58 + L243). But T3's own row-level breakdown
is L28 ×1 + L29 ×1 + L58 ×12 = **14**, and two independent tallies force it: the per-file table
(BlittableProof Survived 36 = 14 equivalents + 20 `InstanceFields` + 2 `IsSourceSequential`) and the leg
total (46 Survived = 2 EquatableArray + 8 ConstructorSelector + 36 BlittableProof). The count word was an
arithmetic slip; the addends are primary, so this ledger pins **16** (14 + 2). Flagged here rather than
silently corrected; a maintainer recount against a fresh report that lands elsewhere shrinks/grows this
file with the correction in the same commit (R3).

## The entries

The fenced JSON below is the machine-readable table `RatchetInvariantScanTests` parses; the prose above
is its documentation. Edit both together — the scan cross-checks the summary numbers against the rows.

```json
{
  "comment": "Ledger-only equivalent-mutant adjudications (ruling (b), 2026-08-21). Transcribed from the anchored ledgers; gates stay on RAW scores. occurrences counts mutants sharing one identity row (e.g. the 12 or->and flips in one pattern).",
  "sources": [
    "Issues/ledgers/T3-mutation-survivors.md",
    "Issues/ledgers/E3-E1-report.md",
    "Issues/ledgers/H7-timeout-dissection.md",
    "Issues/round20/CARRY-FORWARD.md",
    "Issues/ledgers/codefixes-mutation-survivors.md"
  ],
  "legs": {
    "generator": {
      "config": "stryker-config.json",
      "scoreable": 338,
      "measuredRawScore": 84.32,
      "measuredOn": "2026-08-27",
      "provenEquivalent": 24,
      "ruledInPractice": 0,
      "probablyEquivalent": 6,
      "rawCeiling": 92.89,
      "rawCeilingFormula": "(338 - 24) / 338"
    },
    "doctooling": {
      "config": "stryker-config.doctooling.json",
      "scoreable": 289,
      "measuredRawScore": 95.85,
      "measuredOn": "2026-08-23",
      "provenEquivalent": 10,
      "ruledInPractice": 0,
      "probablyEquivalent": 0,
      "rawCeiling": 96.53,
      "rawCeilingFormula": "(289 - 10) / 289"
    },
    "runtime": {
      "config": "stryker-config.runtime.json",
      "scoreable": 125,
      "measuredRawScore": 97.6,
      "measuredOn": "2026-08-27",
      "provenEquivalent": 2,
      "ruledInPractice": 1,
      "probablyEquivalent": 1,
      "rawCeiling": 98.4,
      "rawCeilingFormula": "(125 - 2) / 125"
    },
    "codefixes": {
      "config": "stryker-config.codefixes.json",
      "scoreable": 177,
      "measuredRawScore": 87.01,
      "measuredOn": "2026-08-26",
      "provenEquivalent": 22,
      "ruledInPractice": 0,
      "probablyEquivalent": 1,
      "rawCeiling": 87.57,
      "rawCeilingFormula": "(177 - 22) / 177"
    },
    "pipeline": {
      "config": "stryker-config.pipeline.json",
      "scoreable": 239,
      "measuredRawScore": 76.99,
      "measuredOn": "2026-08-27",
      "provenEquivalent": 0,
      "ruledInPractice": 0,
      "probablyEquivalent": 0,
      "rawCeiling": 100.0,
      "rawCeilingFormula": "(239 - 0) / 239 — nothing is adjudicated equivalent yet, so every undetected mutant here is an open worklist item rather than a proven equivalence"
    }
  },
  "entries": [
    {
      "leg": "generator",
      "file": "src/DwarfMapper.Generator/Pipeline/BlittableProof.cs",
      "member": "LayoutIdentical",
      "lineAtProof": 28,
      "lineCurrent": 28,
      "mutator": "Logical",
      "original": "!a.IsUnmanagedType || !b.IsUnmanagedType",
      "mutated": "!a.IsUnmanagedType && !b.IsUnmanagedType",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Differs from the original only when exactly one operand is managed; managed-ness always enters at a reference-type leaf, and every such leaf returns false anyway (fails TypeKind != Struct, is not primitive), so the mutant never returns true where the original returns false.",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § BlittableProof.LayoutIdentical / IsPrimitive"
    },
    {
      "leg": "generator",
      "file": "src/DwarfMapper.Generator/Pipeline/BlittableProof.cs",
      "member": "LayoutIdentical",
      "lineAtProof": 29,
      "lineCurrent": 274,
      "mutator": "Logical",
      "original": "IsPrimitive(a) || IsPrimitive(b)",
      "mutated": "IsPrimitive(a) && IsPrimitive(b)",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The primitive branch's true return requires two DISTINCT symbols sharing one non-None SpecialType, which cannot occur inside a single compilation (the identity check at L26 already returned true for the same symbol), so the branch's outcome is unreachable and mutating its guard changes nothing observable.",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § BlittableProof.LayoutIdentical / IsPrimitive"
    },
    {
      "leg": "generator",
      "file": "src/DwarfMapper.Generator/Pipeline/BlittableProof.cs",
      "member": "IsPrimitive",
      "lineAtProof": 58,
      "lineCurrent": 52,
      "mutator": "Logical (pattern)",
      "original": "one `or` in the SpecialType pattern (12 distinct flips)",
      "mutated": "that `or` -> `and`",
      "occurrences": 12,
      "category": "proven-equivalent",
      "proof": "Same unreachability as the L29 entry: IsPrimitive's result only feeds the branch whose true return is unreachable within one compilation, and a primitive is always a metadata symbol, so the fall-through path rejects it at IsSourceSequential either way.",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § BlittableProof.LayoutIdentical / IsPrimitive"
    },
    {
      "leg": "generator",
      "file": "src/DwarfMapper.Generator/Pipeline/ConstructorSelector.cs",
      "member": "Select (hasExplicitNonParameterlessCtor predicate)",
      "lineAtProof": 58,
      "lineCurrent": 83,
      "mutator": "Equality",
      "original": "c.Parameters.Length > 0",
      "mutated": "c.Parameters.Length >= 0",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The predicate also requires !c.IsImplicitlyDeclared, so the only constructor the widened comparison newly admits is an explicitly declared parameterless one - and for that constructor anyParameterless still matches via the same fact, so the selection outcome is unchanged.",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § ConstructorSelector notes"
    },
    {
      "leg": "generator",
      "file": "src/DwarfMapper.Generator/Pipeline/ConstructorSelector.cs",
      "member": "AllParametersHaveASource",
      "lineAtProof": 243,
      "lineCurrent": 279,
      "mutator": "Equality",
      "original": "src.IndexOf('.') >= 0",
      "mutated": "src.IndexOf('.') > 0",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "T3 verdict: Equivalent, listed do-not-attempt (the two comparisons differ only for a path whose FIRST character is '.', i.e. an empty leading segment, which no accepted [MapProperty] source produces). Recorded as the ledger's verdict; T3 carries no longer-form case analysis for this row.",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § kill-first ranking, do-not-attempt list"
    },
    {
      "leg": "generator",
      "file": "src/DwarfMapper.Generator/Pipeline/BlittableProof.cs",
      "member": "InstanceFields (sort comparator, file-path key, a-side)",
      "lineAtProof": 80,
      "lineCurrent": 370,
      "mutator": "Conditional/Equality/String (4 distinct: cond->true, > -> >=, both string.Empty literals -> \"Stryker was here!\")",
      "original": "a.Locations.Length > 0 ? a.Locations[0].SourceTree?.FilePath ?? string.Empty : string.Empty",
      "mutated": "the four forms above",
      "occurrences": 4,
      "category": "proven-equivalent",
      "proof": "InstanceFields is reached only after IsSourceSequential(na) AND IsSourceSequential(nb) both pass (LayoutIdentical's gate), which demands SOURCE-declared structs. Every field symbol of a source-declared struct - explicit fields, fixed buffers, auto-property and record-primary-constructor backing fields (whose Locations delegate to the property/parameter identifier in source) - carries Locations.Length >= 1 with Locations[0] a source location whose SourceTree is non-null (and SyntaxTree.FilePath is non-null by contract, empty at worst). So 'Length > 0' is true on every reachable input (cond->true and >= 0 agree with the original) and both string.Empty fallback positions (the coalesce right operand and the ternary else arm) are never evaluated, making their literal mutants unobservable - the two String rows were NoCoverage in every accepted run, consistent with this proof. The SIBLING mutants on the same expression (cond->false, < 0, coalesce-remove-left) ARE reachable-divergent and were killed by the round-22 partial-file fixture, so the proof discriminates rather than blanket-excuses the line. (Reverses T3's 'real hole, same cause as L78' grouping for these four; precedent for reversing a T3 judgement with a case analysis: the P2 facade-TryGet adjudication.)",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § P5 adjudications"
    },
    {
      "leg": "generator",
      "file": "src/DwarfMapper.Generator/Pipeline/BlittableProof.cs",
      "member": "InstanceFields (sort comparator, file-path key, b-side)",
      "lineAtProof": 81,
      "lineCurrent": 371,
      "mutator": "Conditional/Equality/String (4 distinct: cond->true, > -> >=, both string.Empty literals -> \"Stryker was here!\")",
      "original": "b.Locations.Length > 0 ? b.Locations[0].SourceTree?.FilePath ?? string.Empty : string.Empty",
      "mutated": "the four forms above",
      "occurrences": 4,
      "category": "proven-equivalent",
      "proof": "Mirror of the L80 entry, same case analysis: every reachable field has a source location with a non-null SourceTree, so the guard is always true and the string.Empty arms are dead on every reachable input; the b-side cond->false / < 0 / coalesce-remove-left siblings were killed by the same fixture.",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § P5 adjudications"
    },
    {
      "leg": "generator",
      "file": "src/DwarfMapper.Generator/Pipeline/BlittableProof.cs",
      "member": "InstanceFields (sort comparator, position tie-break, a-side)",
      "lineAtProof": 85,
      "lineCurrent": 378,
      "mutator": "Conditional/Equality (4 distinct: cond->true, cond->false, > -> <, > -> >=)",
      "original": "a.Locations.Length > 0 ? a.Locations[0].SourceSpan.Start : 0",
      "mutated": "the four guard mutations above",
      "occurrences": 4,
      "category": "probably-equivalent",
      "proof": "Within a single file GetMembers() is already position-ordered, so the SourceSpan.Start tie-break is a no-op there and a test cannot easily distinguish it; T3: treat as probably-equivalent, low priority, do not count toward a raised break. P5 caveat: the b-side cond->false and < 0 mirrors WERE killed by the partial-file fixture (the framework's SwapIfGreater(keys[0], keys[1]) argument order makes a neutered b-side position observable on a same-file pair), so the a-side cond->true / > -> >= remain equivalent-shaped (guard always true, as the L80 proof) while a-side cond->false / < 0 are plausibly killable with a deliberately mis-ordered same-file pair - kept probably-equivalent, not do-not-attempt.",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § BlittableProof.InstanceFields; § P5 adjudications"
    },
    {
      "leg": "generator",
      "file": "src/DwarfMapper.Generator/Pipeline/BlittableProof.cs",
      "member": "InstanceFields (sort comparator, position tie-break, b-side)",
      "lineAtProof": 86,
      "lineCurrent": 379,
      "mutator": "Conditional/Equality (2 remaining: cond->true, > -> >=)",
      "original": "b.Locations.Length > 0 ? b.Locations[0].SourceSpan.Start : 0",
      "mutated": "the two guard mutations above",
      "occurrences": 2,
      "category": "probably-equivalent",
      "proof": "CORRECTED by P5 (shrink 4 -> 2 with the invalidating evidence, per this ledger's rule): T3's no-op claim was refuted for the cond->false and < 0 forms - both were KILLED by the round-22 partial-file fixture (a zeroed b-side position flips SwapIfGreater's single comparison on the destination's same-file pair). The surviving cond->true and > -> >= forms diverge only on a zero-location field, unreachable per the L80/L81 proof - they stay probably-equivalent only because this row predates that proof's grade; a future pass may promote them with it.",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § P5 adjudications"
    },
    {
      "leg": "doctooling",
      "file": "src/DwarfMapper.DocTooling/DocSnippetInjector.cs",
      "member": "LongestBacktickRun",
      "lineAtProof": 83,
      "lineCurrent": 102,
      "mutator": "Equality",
      "original": "run > longest",
      "mutated": "run >= longest",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The two forms differ on exactly one input, run == longest, and there they agree anyway: the mutant assigns longest = run where run already equals longest, which changes nothing. No test can detect it. (Line drifted 83 -> 97 when the H7 progress guard landed above it.)",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § DocTooling family E"
    },
    {
      "leg": "doctooling",
      "file": "src/DwarfMapper.DocTooling/DocSnippetInjector.cs",
      "member": "ParseId (malformed-marker guard)",
      "lineAtProof": 110,
      "lineCurrent": 118,
      "mutator": "Equality",
      "original": "end < 0",
      "mutated": "end <= 0",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "ParseId is called only on a line whose TrimStart() begins with '<!-- snippet:', so characters 0-2 are '<!-' and IndexOf(\"-->\") can never return 0; the comparisons differ only at end == 0, which is unreachable.",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § P3 adjudication 1"
    },
    {
      "leg": "doctooling",
      "file": "src/DwarfMapper.DocTooling/SnippetScanner.cs",
      "member": "ParseId (malformed-marker guard)",
      "lineAtProof": 121,
      "lineCurrent": 137,
      "mutator": "Equality",
      "original": "close < 0",
      "mutated": "close <= 0",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Same shape as the injector entry: the line begins with '// <snippet:', character 0 is '/', so IndexOf('>') can never return 0 and the comparisons differ only at an unreachable input.",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § P3 adjudication 2"
    },
    {
      "leg": "doctooling",
      "file": "src/DwarfMapper.DocTooling/DocTableInjector.cs",
      "member": "Inject (unclosed-table guard)",
      "lineAtProof": 31,
      "lineCurrent": 37,
      "mutator": "Equality",
      "original": "end < 0",
      "mutated": "end <= 0",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "end = Array.FindIndex(lines, start + 1, ...) with start >= 0 returns -1 or a value >= start + 1 >= 1; 0 is not in its range, so the widened comparison admits no new input. (The open-marker sibling start < 0 -> <= 0 IS reachable - a marker on the first line - and is Killed.)",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § P3 adjudication 3"
    },
    {
      "leg": "doctooling",
      "file": "src/DwarfMapper.DocTooling/ExampleCatalogue.cs",
      "member": "Build (ambiguous-match message ternary)",
      "lineAtProof": 74,
      "lineCurrent": 75,
      "mutator": "Equality",
      "original": "matches.Count > 1",
      "mutated": "matches.Count >= 1",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The ternary sits inside the matches.Count != 1 throw's message, so it is evaluated only for counts {0, 2, 3, ...}; > 1 and >= 1 agree on every one of those, and the only distinguishing count, 1, never reaches it.",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § P3 adjudication 4"
    },
    {
      "leg": "doctooling",
      "file": "src/DwarfMapper.DocTooling/SnippetScanner.cs",
      "member": "ScanFile (close-marker branch)",
      "lineAtProof": 105,
      "lineCurrent": 105,
      "mutator": "Statement",
      "original": "continue;",
      "mutated": ";",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The only statement the deleted continue would fall through to is 'if (openId is not null) body.Add(lines[i]);', and the branch sets openId = null on its previous line - the fall-through is a guaranteed no-op.",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § P3 adjudication 5"
    },
    {
      "leg": "doctooling",
      "file": "src/DwarfMapper.DocTooling/SnippetScanner.cs",
      "member": "Dedent (common-prefix loop guard)",
      "lineAtProof": 170,
      "lineCurrent": 189,
      "mutator": "Equality",
      "original": "prefix.Length > 0",
      "mutated": "prefix.Length >= 0",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The loop's other conjunct is !w.StartsWith(prefix); at prefix == \"\", StartsWith(\"\") is true for every string, so the conjunction is false either way and the loop exits identically.",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § P3 adjudication 6"
    },
    {
      "leg": "doctooling",
      "file": "src/DwarfMapper.DocTooling/OptionTableRenderer.cs",
      "member": "ExistingProse (header/separator skip)",
      "lineAtProof": 94,
      "lineCurrent": 94,
      "mutator": "String",
      "original": "name is \"Option\" or \"---\" (the \"---\" literal)",
      "mutated": "that \"---\" -> \"\"",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The mutant stops skipping separator-shaped rows, so '---' can enter the prose/order dictionaries - but both consumers key them by PropertyInfo.Name, a valid C# identifier which '---' can never be, and real keys keep their relative insertion order so OrderBy is unaffected. (The sibling 'Option' arm is NOT equivalent - a property CAN be named Option - and is Killed by the header-masquerade test.)",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § P3 adjudication 7"
    },
    {
      "leg": "doctooling",
      "file": "src/DwarfMapper.DocTooling/OptionTableRenderer.cs",
      "member": "TryCreate (TargetInvocationException catch)",
      "lineAtProof": 109,
      "lineCurrent": 109,
      "mutator": "Block removal",
      "original": "{ return null; }",
      "mutated": "{}",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The removed catch block contains exactly 'return null;'. Stryker keeps block-removal mutants compilable by appending a 'return default' epilogue to the method, and default for object? IS null - the mutant returns null on the same exception path. Identical by the mutation tooling's own mechanics; empirically covered-and-passing under A_throwing_constructor_falls_back_to_em_dashes.",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § P3 adjudication 8"
    },
    {
      "leg": "doctooling",
      "file": "src/DwarfMapper.DocTooling/OptionTableRenderer.cs",
      "member": "Format (empty-string arm)",
      "lineAtProof": 118,
      "lineCurrent": 118,
      "mutator": "Conditional (false)",
      "original": "s.Length == 0 ? <empty-quotes literal> : <interpolated quoted s>",
      "mutated": "false ? ... (always the interpolated arm)",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "At s == \"\" the interpolated arm renders the byte-identical text to the literal arm - the literal is a readability duplicate of the interpolated arm's empty case - so the one input the conditional-false changes is the one input where the arms agree. (The sibling conditional-true and s.Length != 0 mutants DO diverge for non-empty strings and are Killed.)",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § P3 adjudication 9"
    },
    {
      "leg": "runtime",
      "file": "src/DwarfMapper/DwarfRefContext.cs",
      "member": "DwarfRefContext..ctor (lower clamp)",
      "lineAtProof": 77,
      "lineCurrent": 105,
      "mutator": "Equality",
      "original": "maxDepth < DwarfLimits.MinMaxDepth",
      "mutated": "maxDepth <= DwarfLimits.MinMaxDepth",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The two forms differ on exactly one input, maxDepth == 1, and agree there: the original falls through '1 > AbsoluteMaxDepth' (false) and yields maxDepth = 1; the mutant takes the clamp branch and yields the literal 1. Identical output for every input. (The sibling L77 Conditional-false mutant is a REAL hole - E3-E1 #8 - not this entry.) UPDATED 2026-08-27: round 27 introduced src/Shared/DwarfLimits.cs and replaced the literal 1 with DwarfLimits.MinMaxDepth. The proof is UNCHANGED because MinMaxDepth == 1: the two forms still differ only at maxDepth == 1, where both yield 1. The expression text is updated because the row’s identity is leg+file+member+mutator+original, and an identity naming source that no longer exists matches no mutant Stryker can generate.",
      "anchor": "Issues/ledgers/E3-E1-report.md § the equivalent mutant (DwarfRefContext L77 Equality)"
    },
    {
      "leg": "runtime",
      "file": "src/DwarfMapper/IDwarfMapper.cs",
      "member": "DwarfMapperFacade.Map<TSource, TDestination>(TSource) (TryGet fast-path guard)",
      "lineAtProof": 73,
      "lineCurrent": 73,
      "mutator": "Logical",
      "original": "DwarfMapperRegistry.TryGet(typeof(TSource), typeof(TDestination), out var map) && map is not null",
      "mutated": "that && -> ||",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The operands co-vary on every reachable input: ConcurrentDictionary.TryGetValue sets the out value to default (null) exactly when it returns false, and the dictionary can never hold a null delegate because Register ThrowIfNull-guards it before TryAdd. Only (true, true) and (false, false) are reachable, and && and || agree on both; evaluation order is unchanged (TryGet stays the left operand). The 'map is not null' arm exists for nullable flow analysis, not as a reachable branch. (E3-E1 hole 13 re-examined in round-22 P2: the 'independently asserted' framing presumed the operands could be driven apart.)",
      "anchor": "Issues/ledgers/E3-E1-report.md § Round-22 P2 appendix, proven equivalent (facade TryGet guard)"
    },
    {
      "leg": "runtime",
      "file": "src/DwarfMapper/DwarfMapExceptions.cs",
      "member": "FormatMessage (ambiguous-branch guard)",
      "lineAtProof": 86,
      "lineCurrent": 94,
      "mutator": "Equality (recursive pattern)",
      "original": "ambiguousInterfaces is { Count: > 1 }",
      "mutated": "ambiguousInterfaces is { Count: >= 1 }",
      "occurrences": 1,
      "category": "probably-equivalent",
      "proof": "Diverges only on a ONE-element list, which DwarfMapperRegistry.Map can never construct the exception with: a single accepting interface resolves (candidates.Count == 1 returns before the throw), so Map passes null or a >= 2-element list. The only distinguishing input is a direct public-ctor call with a 1-element list, which the ctor's own doc excludes ('when there was more than one') - a test on it would pin undocumented off-contract behaviour. Probably rather than proven because that call IS expressible; the grade records the plan's P2 disposition: adjudicate, do not chase.",
      "anchor": "Issues/ledgers/E3-E1-report.md § Round-22 P2 appendix, probably equivalent (Count: > 1 boundary); Issues/ledgers/round21-sdd-ledger.md § T7"
    },
    {
      "leg": "runtime",
      "file": "src/DwarfMapper/DwarfMapperRegistry.cs",
      "member": "Key.Equals(Key)",
      "lineAtProof": 291,
      "lineCurrent": 356,
      "mutator": "Logical",
      "original": "Source == other.Source && Destination == other.Destination",
      "mutated": "Source == other.Source || Destination == other.Destination",
      "occurrences": 1,
      "category": "ruled-in-practice",
      "proof": "Maintainer ruling, round-20 CF 5.4: Key is a private readonly struct whose only consumer is ConcurrentDictionary, which compares hash codes BEFORE consulting Equals; a half-matching key differs in hash, so Equals is never reached with one. Divergence requires an engineered hash collision with exactly one matching component, which no honest test produces. (E3-E1 listed it as hole #6 before the ruling; line drifted 291 -> 282 by T7's measurement.)",
      "anchor": "Issues/round20/CARRY-FORWARD.md item 5.4; stryker-config.runtime.json comment"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/AddMapIgnoreCodeFixProvider.cs",
      "member": "RegisterCodeFixesAsync",
      "lineAtProof": 35,
      "lineCurrent": 35,
      "mutator": "Boolean mutation",
      "original": "ConfigureAwait(false)",
      "mutated": "ConfigureAwait(true)",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "ConfigureAwait selects whether the continuation resumes on a captured SynchronizationContext. It cannot change what the awaited call RETURNS, and the syntax root is the only thing read from it; the provider then does no thread-affine work. Under xunit there is no context to capture, so both forms resume on the thread pool. Distinguishing them would require observing WHICH thread resumed, which asserts nothing about the fix.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/AddMapIgnoreCodeFixProvider.cs",
      "member": "RegisterCodeFixesAsync",
      "lineAtProof": 38,
      "lineCurrent": 38,
      "mutator": "Statement mutation",
      "original": "return;",
      "mutated": ";",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The mutated statement is the early return taken when GetSyntaxRootAsync yields null. A C# source document always has a syntax root -- null is returned only for a document that does not support syntax trees -- and a code fix is only ever registered against a diagnostic in one. The branch is unreachable, so removing its return changes nothing. The guard stays in the source deliberately: the API contract permits null, so deleting it would trade a dead line for a NullReferenceException if that contract is ever met.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/AddMapIgnoreCodeFixProvider.cs",
      "member": "RegisterCodeFixesAsync",
      "lineAtProof": 49,
      "lineCurrent": 49,
      "mutator": "Boolean mutation",
      "original": "getInnermostNodeForTie: true",
      "mutated": "getInnermostNodeForTie: false",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "getInnermostNodeForTie chooses between a node and its direct parent when the two share an identical span. Every caller immediately walks upward with FirstAncestorOrSelf<T>, and a tie means one candidate is the parent of the other, so both have the same ancestors above the tied pair and the search lands on the same node either way.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/AddReverseMapInverseCodeFixProvider.cs",
      "member": "RegisterCodeFixesAsync",
      "lineAtProof": 37,
      "lineCurrent": 37,
      "mutator": "Boolean mutation",
      "original": "ConfigureAwait(false)",
      "mutated": "ConfigureAwait(true)",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "ConfigureAwait selects whether the continuation resumes on a captured SynchronizationContext. It cannot change what the awaited call RETURNS, and the syntax root is the only thing read from it; the provider then does no thread-affine work. Under xunit there is no context to capture, so both forms resume on the thread pool. Distinguishing them would require observing WHICH thread resumed, which asserts nothing about the fix.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/AddReverseMapInverseCodeFixProvider.cs",
      "member": "RegisterCodeFixesAsync",
      "lineAtProof": 40,
      "lineCurrent": 40,
      "mutator": "Statement mutation",
      "original": "return;",
      "mutated": ";",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The mutated statement is the early return taken when GetSyntaxRootAsync yields null. A C# source document always has a syntax root -- null is returned only for a document that does not support syntax trees -- and a code fix is only ever registered against a diagnostic in one. The branch is unreachable, so removing its return changes nothing. The guard stays in the source deliberately: the API contract permits null, so deleting it would trade a dead line for a NullReferenceException if that contract is ever met.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/AddReverseMapInverseCodeFixProvider.cs",
      "member": "RegisterCodeFixesAsync",
      "lineAtProof": 45,
      "lineCurrent": 45,
      "mutator": "Boolean mutation",
      "original": "getInnermostNodeForTie: true",
      "mutated": "getInnermostNodeForTie: false",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "getInnermostNodeForTie chooses between a node and its direct parent when the two share an identical span. Every caller immediately walks upward with FirstAncestorOrSelf<T>, and a tie means one candidate is the parent of the other, so both have the same ancestors above the tied pair and the search lands on the same node either way.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/AddReverseMapInverseCodeFixProvider.cs",
      "member": "InverseName",
      "lineAtProof": 98,
      "lineCurrent": 98,
      "mutator": "Equality mutation",
      "original": "dot >= 0",
      "mutated": "dot > 0",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "LastIndexOf and IndexOf over a type's source text; the two comparisons differ only when the index is exactly 0, i.e. a type written starting with \".\" or \"<\". Neither is valid C# type syntax, so no parse tree can produce one.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/AddReverseMapInverseCodeFixProvider.cs",
      "member": "InverseName",
      "lineAtProof": 104,
      "lineCurrent": 104,
      "mutator": "Equality mutation",
      "original": "generic >= 0",
      "mutated": "generic > 0",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "LastIndexOf and IndexOf over a type's source text; the two comparisons differ only when the index is exactly 0, i.e. a type written starting with \".\" or \"<\". Neither is valid C# type syntax, so no parse tree can produce one.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/ResolveExplicitOnlyMemberCodeFixProvider.cs",
      "member": "RegisterCodeFixesAsync",
      "lineAtProof": 46,
      "lineCurrent": 46,
      "mutator": "Boolean mutation",
      "original": "ConfigureAwait(false)",
      "mutated": "ConfigureAwait(true)",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "ConfigureAwait selects whether the continuation resumes on a captured SynchronizationContext. It cannot change what the awaited call RETURNS, and the syntax root is the only thing read from it; the provider then does no thread-affine work. Under xunit there is no context to capture, so both forms resume on the thread pool. Distinguishing them would require observing WHICH thread resumed, which asserts nothing about the fix.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/ResolveExplicitOnlyMemberCodeFixProvider.cs",
      "member": "RegisterCodeFixesAsync",
      "lineAtProof": 49,
      "lineCurrent": 49,
      "mutator": "Statement mutation",
      "original": "return;",
      "mutated": ";",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The mutated statement is the early return taken when GetSyntaxRootAsync yields null. A C# source document always has a syntax root -- null is returned only for a document that does not support syntax trees -- and a code fix is only ever registered against a diagnostic in one. The branch is unreachable, so removing its return changes nothing. The guard stays in the source deliberately: the API contract permits null, so deleting it would trade a dead line for a NullReferenceException if that contract is ever met.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/ResolveExplicitOnlyMemberCodeFixProvider.cs",
      "member": "RegisterCodeFixesAsync",
      "lineAtProof": 60,
      "lineCurrent": 60,
      "mutator": "Boolean mutation",
      "original": "getInnermostNodeForTie: true",
      "mutated": "getInnermostNodeForTie: false",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "getInnermostNodeForTie chooses between a node and its direct parent when the two share an identical span. Every caller immediately walks upward with FirstAncestorOrSelf<T>, and a tie means one candidate is the parent of the other, so both have the same ancestors above the tied pair and the search lands on the same node either way.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/RestateBaseConfigurationCodeFixProvider.cs",
      "member": "RegisterCodeFixesAsync",
      "lineAtProof": 50,
      "lineCurrent": 50,
      "mutator": "Boolean mutation",
      "original": "ConfigureAwait(false)",
      "mutated": "ConfigureAwait(true)",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "ConfigureAwait selects whether the continuation resumes on a captured SynchronizationContext. It cannot change what the awaited call RETURNS, and the syntax root is the only thing read from it; the provider then does no thread-affine work. Under xunit there is no context to capture, so both forms resume on the thread pool. Distinguishing them would require observing WHICH thread resumed, which asserts nothing about the fix.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/RestateBaseConfigurationCodeFixProvider.cs",
      "member": "RegisterCodeFixesAsync",
      "lineAtProof": 53,
      "lineCurrent": 53,
      "mutator": "Statement mutation",
      "original": "return;",
      "mutated": ";",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The mutated statement is the early return taken when GetSyntaxRootAsync yields null. A C# source document always has a syntax root -- null is returned only for a document that does not support syntax trees -- and a code fix is only ever registered against a diagnostic in one. The branch is unreachable, so removing its return changes nothing. The guard stays in the source deliberately: the API contract permits null, so deleting it would trade a dead line for a NullReferenceException if that contract is ever met.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/RestateBaseConfigurationCodeFixProvider.cs",
      "member": "RegisterCodeFixesAsync",
      "lineAtProof": 58,
      "lineCurrent": 58,
      "mutator": "Boolean mutation",
      "original": "getInnermostNodeForTie: true",
      "mutated": "getInnermostNodeForTie: false",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "getInnermostNodeForTie chooses between a node and its direct parent when the two share an identical span. Every caller immediately walks upward with FirstAncestorOrSelf<T>, and a tie means one candidate is the parent of the other, so both have the same ancestors above the tied pair and the search lands on the same node either way.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/RestateBaseConfigurationCodeFixProvider.cs",
      "member": "WithRestatement",
      "lineAtProof": 168,
      "lineCurrent": 168,
      "mutator": "Null coalescing mutation (remove left)",
      "original": "classDecl.AttributeLists.LastOrDefault() ?? (SyntaxNode)classDecl",
      "mutated": "(SyntaxNode)classDecl",
      "occurrences": 1,
      "category": "probably-equivalent",
      "proof": "Chooses the node whose trivia the added attribute list copies: the last existing attribute list, or the class declaration when there is none. The result is re-annotated with Formatter.Annotation immediately afterwards, so the normalised output has been identical in every case tried. NOT a proof -- a formatting-sensitive input may yet distinguish them, which is why this is probably- rather than proven-equivalent.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/RestateBaseConfigurationCodeFixProvider.cs",
      "member": "WithRestatement",
      "lineAtProof": 172,
      "lineCurrent": 173,
      "mutator": "Block removal mutation",
      "original": "return document;",
      "mutated": "{ }",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Removes the early \"return document\" taken when there is nothing to restate. Falling through runs ReplaceNodes and AddRange over empty collections -- both no-ops -- and re-annotates the class for formatting, producing byte-identical text. Pinned by RestateBaseRefusalTests.Restating_a_pair_that_has_not_drifted_leaves_the_document_alone.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/RestateBaseConfigurationCodeFixProvider.cs",
      "member": "WithRestatement",
      "lineAtProof": 177,
      "lineCurrent": 177,
      "mutator": "Equality mutation",
      "original": "replacements.Count > 0",
      "mutated": "replacements.Count >= 0",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "With >= 0 the guarded call also runs when the collection is empty, and ReplaceNodes over an empty collection is a no-op that returns an equal node. The emitted text is identical.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/RestateBaseConfigurationCodeFixProvider.cs",
      "member": "WithRestatement",
      "lineAtProof": 182,
      "lineCurrent": 182,
      "mutator": "Equality mutation",
      "original": "additions.Count > 0",
      "mutated": "additions.Count >= 0",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "With >= 0 the guarded call also runs when the collection is empty, and WithAttributeLists(... AddRange over an empty collection is a no-op that returns an equal node. The emitted text is identical.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/RestateBaseConfigurationCodeFixProvider.cs",
      "member": "Short",
      "lineAtProof": 285,
      "lineCurrent": 285,
      "mutator": "Conditional (false) mutation",
      "original": "cut < 0 ? name : name.Substring(cut + 1)",
      "mutated": "name.Substring(cut + 1)",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "When cut is -1 the true branch returns name and the false branch computes name.Substring(0), which IS name. The conditional is redundant for its own guard value, so forcing the false branch changes nothing.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/RestateBaseConfigurationCodeFixProvider.cs",
      "member": "Short",
      "lineAtProof": 285,
      "lineCurrent": 285,
      "mutator": "Equality mutation",
      "original": "cut < 0",
      "mutated": "cut <= 0",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Differs from the original only at cut == 0, i.e. a name beginning with \".\", which no type produces.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/RestateBaseConfigurationCodeFixProvider.cs",
      "member": "TryReadPair",
      "lineAtProof": 347,
      "lineCurrent": 347,
      "mutator": "String mutation",
      "original": "source = target = string.Empty",
      "mutated": "source = target = \"Stryker was here!\"",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Initialises the out parameters immediately before \"return false\", so the compiler is satisfied. Every caller is of the form \"if (TryRead... && TryRead...)\" and reads the outs only on the true path. WEAKER THAN THE OTHERS: this rests on caller discipline rather than on the language, and becomes killable the day a caller reads the outs after a false return.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/RestateBaseConfigurationCodeFixProvider.cs",
      "member": "TryReadPair",
      "lineAtProof": 349,
      "lineCurrent": 349,
      "mutator": "Logical mutation",
      "original": "|| string.IsNullOrEmpty(pair)",
      "mutated": "&& string.IsNullOrEmpty(pair)",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "With && the guard stops returning false when the property is PRESENT but empty -- and the empty string then fails the separator check downstream, so TryReadPair returns false on that path anyway. When the property is absent, name is null and IsNullOrEmpty(null) is true, so both forms return false. The two agree on every input.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "codefixes",
      "file": "src/DwarfMapper.CodeFixes/RestateBaseConfigurationCodeFixProvider.cs",
      "member": "TryReadDerivedPair",
      "lineAtProof": 367,
      "lineCurrent": 367,
      "mutator": "String mutation",
      "original": "source = target = string.Empty",
      "mutated": "source = target = \"Stryker was here!\"",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Initialises the out parameters immediately before \"return false\", so the compiler is satisfied. Every caller is of the form \"if (TryRead... && TryRead...)\" and reads the outs only on the true path. WEAKER THAN THE OTHERS: this rests on caller discipline rather than on the language, and becomes killable the day a caller reads the outs after a false return.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    }
  ]
}
```

## What this ledger is NOT

- **Not a denominator edit.** No entry leaves any leg's scoreable count; `break` values gate raw scores.
- **Not a place for holes.** DocTooling's 43 write-back-exposed survivors, the runtime kill-list families,
  and the generator's triaged-real survivors are corpus holes catalogued in T3/E3-E1 — killing them is
  work, not adjudication, and they must never appear here without a proof.
- **Not self-authorizing.** The dead-code questions (BlittableProof L29–L30's unreachable `true` return,
  ConstructorSelector L281/L285, the L88 flag) stay maintainer decisions (research Q2); they are NoCoverage
  denominator questions, not equivalence entries, and are deliberately absent.
