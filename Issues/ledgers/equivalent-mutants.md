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

## Per-leg summary — counts, raw ceilings, offsets

`rawCeiling` = `(scoreable − provenEquivalent) / scoreable`, truncated to two decimals: the highest raw
score the leg can reach while every proven-equivalent mutant stays in the denominator (ruling (b): it
never leaves). Denominators are the current authoritative runs; a re-measure that moves a denominator
recomputes the ceilings in the same commit.

| Leg | Config | Scoreable | Raw score (measured) | proven | ruled-in-practice | probably | rawCeiling |
|---|---|---:|---:|---:|---:|---:|---:|
| generator | `stryker-config.json` | 201 | 71.64 % (2026-08-19, run `12-10-18`) | 16 | 0 | 8 | 92.03 % |
| doctooling | `stryker-config.doctooling.json` | 284 | 67.96 % (2026-08-21, H7 phase-2 re-measure) | 1 | 0 | 0 | 99.64 % |
| runtime | `stryker-config.runtime.json` | 113 | 87.61 % (2026-08-21, T7 re-measure) | 1 | 1 | 0 | 99.11 % |

Fuller arithmetic, carried from the research (context, not gates): the generator leg's *realistic* raw
ceiling is lower than 92.03 — the 8 probably-equivalent survivors and the 11 NoCoverage mutants T3 judged
unreachable-branch/defensive/dead-code-question cap the killable set at ≈ 167/201 = 83.08 % raw until the
maintainer's dead-code rulings (research Q2) land. The runtime figure net of the ruled-in-practice entry
is 111/113 = 98.23 %. The research's oft-quoted "generator ≈ 89.8 ceiling" is 167/186 — an
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
    "Issues/round20/CARRY-FORWARD.md"
  ],
  "legs": {
    "generator": {
      "config": "stryker-config.json",
      "scoreable": 201,
      "measuredRawScore": 71.64,
      "measuredOn": "2026-08-19",
      "provenEquivalent": 16,
      "ruledInPractice": 0,
      "probablyEquivalent": 8,
      "rawCeiling": 92.03,
      "rawCeilingFormula": "(201 - 16) / 201"
    },
    "doctooling": {
      "config": "stryker-config.doctooling.json",
      "scoreable": 284,
      "measuredRawScore": 67.96,
      "measuredOn": "2026-08-21",
      "provenEquivalent": 1,
      "ruledInPractice": 0,
      "probablyEquivalent": 0,
      "rawCeiling": 99.64,
      "rawCeilingFormula": "(284 - 1) / 284"
    },
    "runtime": {
      "config": "stryker-config.runtime.json",
      "scoreable": 113,
      "measuredRawScore": 87.61,
      "measuredOn": "2026-08-21",
      "provenEquivalent": 1,
      "ruledInPractice": 1,
      "probablyEquivalent": 0,
      "rawCeiling": 99.11,
      "rawCeilingFormula": "(113 - 1) / 113"
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
      "lineCurrent": 29,
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
      "lineCurrent": 58,
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
      "lineCurrent": 243,
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
      "member": "InstanceFields (sort comparator, position tie-break)",
      "lineAtProof": 85,
      "lineCurrent": 85,
      "mutator": "Conditional/Equality (4 distinct: cond->true, cond->false, > -> <, > -> >=)",
      "original": "a.Locations.Length > 0 ? a.Locations[0].SourceSpan.Start : 0",
      "mutated": "the four guard mutations above",
      "occurrences": 4,
      "category": "probably-equivalent",
      "proof": "Within a single file GetMembers() is already position-ordered, so the SourceSpan.Start tie-break is a no-op there and a test cannot easily distinguish it; T3: treat as probably-equivalent, low priority, do not count toward a raised break.",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § BlittableProof.InstanceFields"
    },
    {
      "leg": "generator",
      "file": "src/DwarfMapper.Generator/Pipeline/BlittableProof.cs",
      "member": "InstanceFields (sort comparator, position tie-break)",
      "lineAtProof": 86,
      "lineCurrent": 86,
      "mutator": "Conditional/Equality (4 distinct: cond->true, cond->false, > -> <, > -> >=)",
      "original": "b.Locations.Length > 0 ? b.Locations[0].SourceSpan.Start : 0",
      "mutated": "the four guard mutations above",
      "occurrences": 4,
      "category": "probably-equivalent",
      "proof": "Mirror of the L85 entry: the tie-break is a no-op within a single file; T3: probably-equivalent, low priority.",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § BlittableProof.InstanceFields"
    },
    {
      "leg": "doctooling",
      "file": "src/DwarfMapper.DocTooling/DocSnippetInjector.cs",
      "member": "LongestBacktickRun",
      "lineAtProof": 83,
      "lineCurrent": 97,
      "mutator": "Equality",
      "original": "run > longest",
      "mutated": "run >= longest",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The two forms differ on exactly one input, run == longest, and there they agree anyway: the mutant assigns longest = run where run already equals longest, which changes nothing. No test can detect it. (Line drifted 83 -> 97 when the H7 progress guard landed above it.)",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § DocTooling family E"
    },
    {
      "leg": "runtime",
      "file": "src/DwarfMapper/DwarfRefContext.cs",
      "member": "DwarfRefContext..ctor (lower clamp)",
      "lineAtProof": 77,
      "lineCurrent": 77,
      "mutator": "Equality",
      "original": "maxDepth < 1",
      "mutated": "maxDepth <= 1",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The two forms differ on exactly one input, maxDepth == 1, and agree there: the original falls through '1 > AbsoluteMaxDepth' (false) and yields maxDepth = 1; the mutant takes the clamp branch and yields the literal 1. Identical output for every input. (The sibling L77 Conditional-false mutant is a REAL hole - E3-E1 #8 - not this entry.)",
      "anchor": "Issues/ledgers/E3-E1-report.md § the equivalent mutant (DwarfRefContext L77 Equality)"
    },
    {
      "leg": "runtime",
      "file": "src/DwarfMapper/DwarfMapperRegistry.cs",
      "member": "Key.Equals(Key)",
      "lineAtProof": 291,
      "lineCurrent": 282,
      "mutator": "Logical",
      "original": "Source == other.Source && Destination == other.Destination",
      "mutated": "Source == other.Source || Destination == other.Destination",
      "occurrences": 1,
      "category": "ruled-in-practice",
      "proof": "Maintainer ruling, round-20 CF 5.4: Key is a private readonly struct whose only consumer is ConcurrentDictionary, which compares hash codes BEFORE consulting Equals; a half-matching key differs in hash, so Equals is never reached with one. Divergence requires an engineered hash collision with exactly one matching component, which no honest test produces. (E3-E1 listed it as hole #6 before the ruling; line drifted 291 -> 282 by T7's measurement.)",
      "anchor": "Issues/round20/CARRY-FORWARD.md item 5.4; stryker-config.runtime.json comment"
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
