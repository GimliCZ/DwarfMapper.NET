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

## Rows retired on 2026-09-01 — the sort comparator no longer exists

Four generator rows — `InstanceFields (sort comparator, file-path key, a-side / b-side)` (proven, 4 + 4)
and `InstanceFields (sort comparator, position tie-break, a-side / b-side)` (probably, 4 + 2) — adjudicated
mutants of the `fields.Sort(...)` comparator in `BlittableProof.InstanceFields`. That sort is deleted: it was
the defect behind an unsound blit acceptance (a struct split across partial files, sorted by ordinal path
into an order the compiler never used, lined up by name with a twin whose real layout was the reverse — see
`CHANGELOG.md`, Unreleased/Fixed). An `original` naming source that is gone matches no mutant Stryker can
generate, so the rows are **retired, not corrected**: there is no expression left to carry the identity.
Their proofs were sound about the code they described; the code was wrong.

Counts move with them: generator `provenEquivalent` 24 → 16, `probablyEquivalent` 6 → 0, and the
`generator|probably-equivalent` category is now empty (its pin is removed rather than set to zero — the scan
requires every pinned category to have rows). The **denominator is not re-measured here**: `scoreable` stays
at the 2026-08-27 run's 338, so the ceiling is recomputed as (338 − 16) / 338 = 95.26 % on a population that
still includes the comparator's own mutants (14 adjudicated here plus the 6 siblings the partial-file fixture
killed). The next authoritative run shrinks both the denominator and the kill count by exactly those; the
ceiling is recomputed then, in that commit, as the rule below requires. Three surviving `BlittableProof`
rows have their `lineCurrent` refreshed (28 → 320, 274 → 325, 52 → 409) because the same change added the
partial-declaration, `Size`, `[InlineArray]` and fixed-buffer comparisons above them.

## Rows retired on 2026-09-13 — the DWARF080 lookup no longer carries its own ternary

One pipeline row — `ResolveAutoMatchedMembers (DWARF080 source lookup)` (proven, 1) — adjudicated the
Conditional-false mutant of `lookups.Flexible ? NormalizeName(target.Name) : target.Name` inside the DWARF080
source lookup. Its proof was sound, and the round-30 coverage sweep reached the same conclusion independently: the
Flexible arm there is unreachable, because the only callers that pass `FactoryExcludedMembers` build their options with
`NameConvention: 0`. That unreachable arm is exactly what the sweep's per-branch rule removes. The three identical
`Flexible ? NormalizeName(x) : x` keys in `MapperExtractor.Members.Phases.cs` (the DWARF064 shadow lookup, the DWARF080
lookup and the auto-match lookup) became one `SourceGroupKey(lookups, name)`. The DWARF080 site now calls it and
carries no conditional of its own, so the adjudicated mutant can no longer be generated. The row is **retired, not
re-anchored**: the same mutation inside `SourceGroupKey` is NOT equivalent, because the DWARF064 and auto-match lookups
reach its Flexible arm under `NameConvention.Flexible` and tests kill it there.

Counts move with it: pipeline `provenEquivalent` 16 → 15. The **denominator is not re-measured here**: `scoreable`
stays at the 2026-09-11 run's 304, so the ceiling is recomputed as (304 − 15) / 304 = 95.06 %. The next authoritative
pipeline run re-measures both, and moves the ceiling in that commit.

## Rows retired on 2026-09-14 — `DwarfMapperRegistry.Key` equality is compiler-generated

One runtime row — `Key.Equals(Key)` (ruled-in-practice, 1) — adjudicated the Logical mutant `Source ==
other.Source && Destination == other.Destination` → `||` under the round-20 CF 5.4 ruling: `ConcurrentDictionary`
compares hash codes before `Equals`, so a half-matching key never reaches it. The ruling was sound. What changed
is the code: by owner ruling (Issues/ledgers/round30-ledger.md § Owner rulings), `Key` is now a
`private readonly record struct Key(Type Source, Type Destination)`, so the compiler generates `Equals(Key)`,
`Equals(object)` and `GetHashCode`. Stryker mutates source, not compiler-generated members, so neither this mutant
nor the `Equals(object)` NoCoverage block (never a ledger row, filed as round-20 I1) can be generated any more. The
row is **retired, not re-anchored**: there is no expression left to carry it.

Why the owner took it: the runtime leg measured 95.45 % (`StrykerOutput/2026-09-14.19-15-03`) against `break` 97, and
with the hand-written equality the honest ceiling was 127/132 = 96.21 %. Two `DwarfRefContext` depth-clamp boundary
mutants had been counted as killed at the 97.60 % pin only by an accidental static kill (`killedBy` =
`GeneratedDocsAreCurrentTests.The_api_reference_matches_the_public_surface`, `coveredBy = 0`). Removing the two
permanently-undetected `Key` mutants makes 97 honestly reachable again instead of lowering the floor.

Counts move with it: runtime `ruledInPractice` 1 → 0. `rawCeiling` excludes ruled rows, so it does not move. The
**denominator is not re-measured here**; the next runtime run re-measures it, and adjudicates the upper
clamp mutant (`maxDepth > AbsoluteMaxDepth` → `>=`, the same proof as the existing lower-clamp row) in that commit.

## Rows retired on 2026-09-15 — an added attribute list has no fallback trivia source left

One codefixes row — `WithRestatement` (probably-equivalent, 1) — covered the Null-coalescing mutant
`classDecl.AttributeLists.LastOrDefault() ?? (SyntaxNode)classDecl` → `(SyntaxNode)classDecl`. It never had a proof.
It had only evidence: `Formatter.Annotation` normalised the two trivia sources identically in every case tried.

The round-30 coverage sweep then found the RIGHT operand unreachable:
- `additions.Add` runs only inside the loop over `toCopy`;
- `toCopy` is filled only while iterating `classDecl.AttributeLists`;
- `classDecl` is a parameter the method never reassigns, and a `SyntaxList` is immutable.

So whenever an addition is made, the class has at least one attribute list, and `LastOrDefault()` is never null there.
The fallback was removed: the trivia now comes from `AttributeLists[AttributeLists.Count - 1]`, which is the same node
`LastOrDefault()` returned on every input that reaches the line. With no `??` left, the adjudicated mutant can no
longer be generated. The row is **retired, not re-anchored**. No equivalent mutant takes its place. Stryker generates
no mutant for the index arithmetic, and a hand-planted `Count + 1` fails ten RestateBase tests with
`ArgumentOutOfRangeException`.

Counts move with it: codefixes `probablyEquivalent` 1 → 0. `rawCeiling` excludes only proven rows, so it does not
move on the retirement itself. **Re-measured the same day** (`StrykerOutput/2026-09-15.18-55-18`, clean tree at
48213d0): 156/178 = 87.64 %. That is one scoreable mutant fewer than the 7f96555 run, and the missing one is exactly
this survivor. The leg's 22 undetected mutants are its 22 proven rows, so the measured score equals `rawCeiling`.

## Rows re-adjudicated on 2026-09-21 — three of the twelve `IsPrimitive` flips were never equivalent

The generator row `IsPrimitive` / `lineAtProof: 58` carried **12 occurrences** of the same pattern flip (one `or`
in the `SpecialType` list becomes `and`, which drops the two types it joins) with one shared proof: *"a primitive
is always a metadata symbol, so the fall-through path rejects it at `IsSourceSequential` either way."*

That proof was written before `LayoutIdentical` grew its `byBytesOnly` parameter, and `byBytesOnly` is exactly the
path where the primitive branch's `true` return is reachable: `[Reinterpret]` asks only for the same WIDTH, so two
DIFFERENT primitives of equal width are accepted there. Three of the flips join two such types — `Boolean`/`Byte`,
`Byte`/`SByte`, `Int16`/`UInt16` — and dropping both members of one of those pairs turns `SameBytesIgnoringNames`
from `true` into `false` for it. They are **not equivalent**, and the ledger said they were. They are now killed by
tests (`SameBytesIgnoringNames_accepts_two_different_primitives_of_the_same_width`), each watched go RED with the
mutant hand-applied.

The two that remain join types of DIFFERENT width — `SByte`/`Int16` and `UInt16`/`Int32` — and for those the
original proof's conclusion still holds, by a case analysis rather than by the old blanket claim:

- `IsPrimitive` is read at exactly two sites, both as `IsPrimitive(x) || IsPrimitive(y)`. Dropping a pair changes
  an answer only when BOTH operands are in the dropped pair; with one operand outside it the `||` still fires.
- For the four such pairs, `(sbyte, sbyte)` and `(short, short)` — likewise `(ushort, ushort)`, `(int, int)` —
  return at the identity check above, before `IsPrimitive` is consulted at all.
- That leaves `(sbyte, short)` and `(ushort, int)`, in both orders. The original enters the primitive branch and
  refuses them: their widths differ (1 against 2, 2 against 4), and their `SpecialType`s differ. The mutant skips
  the branch and reaches the struct rules, where `IsSourceSequential` refuses both — a primitive is a metadata
  symbol and has no source `[StructLayout]` to read. Both answers are `false`.

Counts move with the correction: generator `provenEquivalent` 16 → 14 (12 → 2 here, plus the six rows added below
for sites this file had never dispositioned). The lesson is recorded rather than smoothed over: **a shared proof
over N occurrences ages as badly as its weakest occurrence**, and this one aged the moment a parameter widened the
branch it called unreachable. Occurrence rows that fold together are now written with the property that makes them
fold — here, the widths — so the next reader can see what would break them.

## The 2026-09-23 generator run — a score ABOVE the ceiling, and what it proved

This is the run the `rawCeiling` column exists for, so it is written up rather than absorbed.

The 2026-09-21 commit recorded a prediction: 14 adjudicated rows over 415 scoreable means the next run should
survive exactly 14 mutants and measure 96.62 %, sitting AT the ceiling, and *"a fifteenth survivor means
something here is wrong"*. The run came back with **twelve** survivors and **97.11 %** — the other direction,
and the one the sentence did not cover. A measurement above a proven ceiling is arithmetically impossible: either
a proof is wrong, or the measurement is.

The two mutants separating the numbers are `IsPrimitive`'s `or` → `and` flips dropping SByte/Int16 and
UInt16/Int32. Each was **planted permanently in `src/` and the whole solution run**: 10,146 tests green across 9
assemblies, both times. Stryker names
`SameBytesIgnoringNames_accepts_two_different_primitives_of_the_same_width` as their killer — and that test
passes with either mutant planted. Nothing this repository owns kills them; the proofs were right.

**The mechanism.** All 12 scoreable mutants on that line carry `"static": true` in the report. Stryker cannot
attribute per-test coverage to a static mutant, so it runs the mutant against the entire suite and counts any
failure as a kill. Between 2026-09-21 and 2026-09-23 the suite gained nine tests, **no source changed**, and the
five survivors on that line became zero. The ledger already carried the outward form of this hazard — a leg
dropping because accidental static kills stop landing as a suite grows. This is the same mechanism running
inward, and it is more dangerous, because a score that goes UP looks like progress.

**Consequences recorded, not smoothed:**
- The floor is pinned at **96**, the reproducible score, which equals the ceiling exactly. Pinning 97 would gate
  the leg on two accidental kills and turn the next unlucky run into a reported *regression* that never happened.
- The 515 row keeps its 2 occurrences and gains the planting evidence, with a warning: the leg will report these
  killed. **A report that says Killed is not sufficient grounds to retire an equivalence row — plant the
  mutant first.**
- The band check learns the rule that caught this: a measurement may not exceed the leg's proven ceiling.
- Not established, and deliberately left open: whether the seven kills on that line that predate 2026-09-21 are
  also accidental. They have been stable across many runs, which is weak evidence of genuineness and no more.
  Anyone re-opening that line should plant, not read.

**`reportedPhantomKills` (generator, 2)** — the field exists because a leg can report a kill that no test
performs. Stryker cannot attribute per-test coverage to a mutant flagged `"static": true`, so it runs that
mutant against the WHOLE suite and counts any failure as its kill. Two of `IsPrimitive`'s twelve static
mutants are reported Killed while being unkillable: each was planted permanently in `src/` and the whole
solution run, 10,146 tests green across 9 assemblies. The number is PINNED rather than tolerated as a range,
so `Assert-MutationScoreWithinBand` can demand that survivors equal `provenEquivalent - reportedPhantomKills`
exactly. If the accidental kills stop landing, the survivor count stops matching and the gate fails until this
row is corrected — which is the point: the anomaly is recorded, not absorbed.

## Per-leg summary — counts, raw ceilings, offsets

`rawCeiling` = `(scoreable − provenEquivalent) / scoreable`, truncated to two decimals: the highest raw
score the leg can reach while every proven-equivalent mutant stays in the denominator (ruling (b): it
never leaves). Denominators are the current authoritative runs; a re-measure that moves a denominator
recomputes the ceilings in the same commit.

| Leg | Config | Scoreable | Raw score (measured) | proven | ruled-in-practice | probably | rawCeiling |
|---|---|---:|---:|---:|---:|---:|---:|
| generator | `stryker-config.json` | 415 | 96.62 % (2026-09-23, AT the ceiling; the report's 97.11 % is two static-mutant phantom kills) | 14 | 0 | 0 | 96.62 % |
| doctooling | `stryker-config.doctooling.json` | 290 | 96.55 % (2026-09-26, AT the ceiling; the search-pattern pair closed) | 10 | 0 | 0 | 96.55 % |
| runtime | `stryker-config.runtime.json` | 126 | 97.62 % (2026-09-14, round-30 Key record-struct ruling) | 3 | 0 | 1 | 97.61 % |
| codefixes | `stryker-config.codefixes.json` | 178 | 87.64 % (2026-09-15, round-30 trivia-row retirement) | 22 | 0 | 0 | 87.64 % |
| pipeline | `stryker-config.pipeline.json` | 284 | 94.72 % (2026-09-26, AT the ceiling; the two leading-dot rows closed by a source fix) | 15 | 0 | 0 | 94.71 % |
| testing | `stryker-config.testing.json` | 110 | 100.00 % (2026-09-21, round-30 survivor kill programme) | 0 | 0 | 0 | 100.00 % |

**Generator denominator refreshed 2026-09-06** (round-29 Phase 2 gate, task 2.10): 338 → 409 scoreable,
84.32 % → 87.04 %, and `break`/`low` moved 84 → 87 in `stryker-config.json` in the same commit, which is
what R1↔R3 requires and what forced this row to move with it. **Only the denominator moved.** The 16 proven
rows below are untouched — no mutant was re-adjudicated, retired or newly proved by this run, so the
`rawCeiling` is fresh arithmetic over a stale adjudication and should be read as a bound, not as a claim
that 393 mutants are killable today. The 41 survivors and 12 uncovered mutants of the 2026-09-06 run
(BlittableProof 36 + 5, ConstructorSelector 4 + 7, EquatableArray 1) have **not** been dispositioned here;
that is the next kill program's work, and `break` must not move again before it happens.

**Generator RE-MEASURED 2026-09-11** (round-30 coverage sweep checkpoint): same 409 scoreable
(the denominator did not move this time), 356 → 361 killed, 41 → 40 survived, 12 → 8 uncovered, 0
timeouts in either run, so `break`/`low` moved 87 → 88 in `stryker-config.json` in the same
commit. This IS a kill-program result, not a denominator effect: BlittableProof.cs moved
191/36/5 (killed/survived/uncovered) → 196/35/1 — one Survived→Killed, four NoCoverage→Killed —
attributed to the coverage sweep's `TryExplainNearMiss` branch closures (commit `f3ae553`), which
happened to close branches this leg was already generating mutants against. EquatableArray,
ConstructorSelector and LocationInfo are unchanged. The remaining 35 BlittableProof survivors + 1
uncovered, and ConstructorSelector's 4 survivors + 7 uncovered, are still the next kill program's
worklist.

**Generator RE-MEASURED 2026-09-14** (round-30 generator coverage sweep, assembly-end checkpoint;
`StrykerOutput/2026-09-14.12-57-57`, detached from a hash-verified clean tree): 409 → **415** scoreable,
361 → **378** killed, 40 → 37 survived, 8 → 0 uncovered, 0 timeouts, so `break`/`low` moved 88 → 91 and
`high` 90 → 92 in `stryker-config.json` in the same commit. Per file, killed/survived/uncovered:
ConstructorSelector 135/4/7 → 141/4/0 (the sweep's DWARF098-reason and accessibility-word extractions and
the static-constructor fix), LocationInfo 10/0/0 → 27/1/0 (the population grew with
`LocationInfo.FromFirstInSource`), EquatableArray unchanged, BlittableProof 196/35/1 → 190/31/0 with no
commit to that file (its scoreable count fell 232 → 221 as Ignored rose 78 → 83 — recorded as observed,
not attributed). The 16 proven rows are untouched, so `rawCeiling` is (415 − 16) / 415 = 96.14 %. The
same day's first attempt (91.33 %, two timeouts) ran on a tree contaminated by a harness-killed run's
planted mutants and is not the measurement.

**Pipeline RE-MEASURED 2026-09-14** (same checkpoint, `StrykerOutput/2026-09-14.12-00-50`, clean tree):
304 → **283** scoreable, 284 → 266 killed, 19 → 17 survived, 1 → 0 uncovered, 0 timeouts — **93.99 %**,
inside the pinned [93, 94) band, so `break` stays 93. Every scoreable mutant is in
`MapperExtractor.Members.Phases.cs`, as in the 2026-09-11 run (the two context-record files carried none
then either); the denominator moved inside that one file. The 15 proven rows are untouched, so `rawCeiling`
is (283 − 15) / 283 = 94.69 %, and the score sits two undetected mutants under it.

**Pipeline RE-MEASURED again 2026-09-14** (round-30 de-silence batch, assembly-end checkpoint;
`StrykerOutput/2026-09-14.17-59-19`, clean tree): 283 → **284** scoreable, 266 → **267** killed, 17 survived,
0 uncovered, 0 timeouts — **94.01 %**, which floors to 94, so R2 threw and `break`/`low` moved 93 → 94 (`high`
94 → 95) in `stryker-config.pipeline.json` in the same commit. A position-tolerant diff against `12-00-50`
shows exactly one changed population: the `"[MapProperty]"` `MessageArg2` string literal that `0d62dd1`
(DWARF012 names its directive) added to `MapperExtractor.Members.Phases.cs`, killed by that commit's own
`IgnoreConflictDirectiveNameTests`. That commit also inserted a line, so a position-keyed pairing reports every
mutant below it as removed-and-added; none changed status. The 17 survivors are the same 17. The 15 proven rows
are untouched, so `rawCeiling` is (284 − 15) / 284 = 94.71 %, and the score sits two undetected mutants under it.
Headroom is zero: 94 % of 284 needs 267 detected.

**Generator re-run 2026-09-14** (same checkpoint, `StrykerOutput/2026-09-14.17-04-32`): 91.08 % (378/415)
again, so the floor and the row above do not move. One status changed with no commit to the file:
`BlittableProof.cs:336`'s `sizeA != sizeB → ==` went Killed → **Timeout**. A timeout still counts as
detected, so the score is identical, but it is exactly the reclassification R2's error text warns about.
Recorded as observed, not attributed.

**Runtime RE-MEASURED 2026-09-14** (round-30 owner ruling; `StrykerOutput/2026-09-14.20-10-34`, detached from a
clean tree): **97.62 %** (123 killed of 126 scoreable; 3 survived, 0 uncovered, 0 timeouts), inside [97, 98), so
`break` stays 97. The route here: the previous run (`19-15-03`) measured 95.45 % (126/132) against break 97. A
position-tolerant diff against the 2026-09-07 pin attributed that to three things. First, one real survivor from
`77dc345`, killed by `5fe0d76`. Second, two `DwarfRefContext` depth-clamp boundary mutants whose earlier kills were
accidental static kills by a docs-generation test. Third, the two hand-written `Key` equality mutants, which could
never be detected. By owner ruling `DwarfMapperRegistry.Key` became a `readonly record struct` (`e01ff23`), so
compiler-generated equality took those two, and the hand-written `GetHashCode` arithmetic with them, out of the
population: 132 → 126. The three undetected mutants are now exactly the three proven rows — the lower clamp (existing),
the **upper clamp (added here, same proof mirrored)** and the facade TryGet guard — so `rawCeiling` is
(126 − 3) / 126 = 97.61 % and the score sits at the leg's honest ceiling.


**Testing leg added 2026-09-09**, and it is the first row here whose reason is a REGRESSION rather than a
kill programme. `Issues/round27/AUDIT-mutation-scope.md` had recorded `DwarfMapper.Testing` at 0 % mutation
coverage since round 27, on the argument that the package is test-only and never AOT-published. Round 29's
Phase 4 added 189 lines of **consumer-facing verification code** to it (`LensLaws`, `LensLawException`), and
the repository's mutation share fell **11.134 % → 11.085 %** — a real regression that nothing could see,
because the package sat in no leg. The maintainer named it a release block; this row is the answer.

Scoped to the five VERIFIER files (402 lines), not the package: a wrong answer from `ObjectFactoryV2` makes
a fixture nobody asked for, which a test notices; a wrong answer from `RoundTrip.Verify` or `LensLaws`
**certifies a broken map**, which nothing notices. Whole-repository share now **11.1 % → 12.0 %**.

**This row's Timeout bucket is FIVE, and every sibling row's is zero.** The distinction matters because a
Timeout counts as *detected*, so the score would move on a re-classification with no test having changed —
`gate-checks.ps1`'s own R2 error text says to check this first. All five were read: three are `i++` → `i--`
in a `for` loop (`LensLaws` ×2, `RoundTrip` ×1) and two unbind `StructuralComparer`'s recursion. **Every one
removes TERMINATION rather than merely slowing the code**, and a non-terminating mutant times out on every
machine — so unlike a merely-slow mutant, this classification is stable across boxes. That is why 82.73 is
pinned rather than the timeout-free 78.18 (86/110), and it is stated here because the sibling rows cite a
zero bucket as their evidence and this one cannot. **Re-measured 2026-09-09 through
`scripts/housekeeping.ps1 -MutationLeg testing`** — the first run was a bare `Invoke-StrykerLeg`, which
executes none of the four post-leg proofs and whose Stryker build had ended in an IOException. The second run
reproduces the first exactly (86/5/17/2 of 110) **and the five timeouts are the same five mutants**, which
makes the termination argument a repeated measurement rather than a reading.

No mutant is adjudicated equivalent, so the 100 % `rawCeiling` is arithmetic over an empty adjudication, not
a claim. The **17 survivors plus 2 uncovered are the first kill programme's worklist**: 15 in
`StructuralComparer` (its float/double epsilon comparisons and its render formatting), one each in
`LensLaws` and `RoundTrip` — both the `iterations` loop bound, where an off-by-one still verifies the same
laws and may well prove equivalent when someone adjudicates it. **The 2 uncovered are named rather than
counted**, because "uncovered" in a 100 %-line-covered file is a claim that needs a location: both are string
mutations on `StructuralComparer.cs:37-38`, and both are the `"<null>"` operand of a `??` — one for
`d.Expected`, one for `d.Actual`. `Render` itself IS executed by `Render_produces_readable_lines`, which is
why the neighbouring literals on the same two lines are Survived rather than uncovered; the `??` right-hand
side is not, because that test's diff has a value on both sides. **No test renders a diff where a side is
null**, so the one branch whose whole job is to name absence is the one branch never observed. That is the
worklist's first item: render a null-vs-value diff and assert the text, which should also reach several of
the 15 `StructuralComparer` survivors.

**Pipeline denominator refreshed 2026-09-09** (round-29 Phase 3 gate): 242 -> 304 scoreable,
78.28 % -> 89.80 % (273 killed, 0 timeout, 29 survived, 2 not covered by any test).
The population grew because round 29's `[MapShare]` and `[MapDenseEnumKeys]` added code to
`MapperExtractor.Members.Phases.cs`, which is one of this leg's three `mutate` files.

**Unlike the generator row above, BOTH numbers moved here, and the score moved because of kills.** That is
established by a mutant-by-mutant diff of the two reports over the identical 646-mutant population, not by
comparing two headline percentages: every status change runs one way — **33 Survived → Killed and 9
NoCoverage → Killed, and not one Killed → Survived.** Sixteen of the 42 sit on lines 365–367 and 403–405,
the four-way ternaries the eight new tests target; the other 26 are spread across the file because those
tests compile whole mapper sources and so drive the surrounding resolution phases too. The Timeout bucket
is zero in both runs, which is the first thing invariant R2's own error text says to check.

No mutant was adjudicated: `provenEquivalent` stays 0, so the 100 % `rawCeiling` is not a claim that every
mutant is killable — it is the arithmetic that follows from nothing having been proved equivalent yet, and
every one of the **29 survivors plus 2 uncovered mutants is an open worklist item**, all of them in
`MapperExtractor.Members.Phases.cs`. Read the ceiling as "no equivalence work has been done here", not as
headroom. **Actual headroom is two mutants:** `break` 89 requires 271 detected of 304 and 273 are, so a
three-mutant regression reds the gate.

The two remaining NoCoverage mutants are at lines 66 and 105 — deferrable-target selection and the
group-naming fallback — **not** in the `[MapShare]` / `[MapDenseEnumKeys]` region. That was checked rather
than assumed, because still-unexecuted code in a feature shipped this round would have been a blocker for
the release rather than a worklist entry.

**Pipeline kill program, 2026-09-11 (round 30).** The leg re-measured at **81.25 %** (247 of 304 — 56
survived, 1 uncovered, 0 timeouts), reproduced identically twice, with zero commits to the three mutated
files since the pin: every survivor sits on a diagnostic site or branch the suite reaches but asserts only
the id of. The first adjudication for this leg lands here — **16 proven-equivalent rows**, all in
`MapperExtractor.Members.Phases.cs`, with the case analyses in `Issues/ledgers/pipeline-mutation-survivors.md`
(the leg's own survivors ledger, in the code-fixes leg's shape). Three shared lemmas carry most of them: the
skip-null pass's `srcTypeByName` cannot match `""` or a dotted name; no `MemberMap` carries both a non-empty
`SourceName` and a `ValueExpression`; every `ExtrasByTarget` entry was admitted with `When` or a
`NullSubstitute`. Three rows are marked WEAKER: two rest on the error-suppresses-emission invariant
(DWARF078), one on the fact that both callers that pass `FactoryExcludedMembers` construct their options with
`NameConvention: 0`, so DWARF080's Flexible lookup arm is unreachable today. `rawCeiling` becomes
(304 − 16) / 304 = 94.73 %. Three survivors are deliberately **not** adjudicated and
are named in that file: two leading-dot path refusals where the mutant's message is the better one, and the
line-670 `ConsumedCtorParams` flip, which the probe showed to be a latent defect (a `[MapIgnore]` on a
required constructor-bound member emits CS9035 with no DWARF079) — a source question, not a test one.

**Pipeline RE-MEASURED 2026-09-11** after the kill program (`StrykerOutput/2026-09-11.13-02-15`): same 304
scoreable, 247 → **284 killed**, 56 → 19 survived, 1 uncovered in both, 0 timeouts in both — **93.42 %**, so
`break`/`low` moved 89 → 93 (and `high` 90 → 94) in `stryker-config.pipeline.json` in the same commit. The
mutant-by-mutant diff against the 81.25 % run is one-way: 37 Survived → Killed, not one Killed → Survived,
and the 37 are exactly the mutants commit `0b94ad0` was written against (each RED-proven against its plant
before GREEN). The 20 still undetected are the 16 rows here plus the four `pipeline-mutation-survivors.md`
leaves open by name, so the measurement sits four mutants under the 94.73 % ceiling with every one of the
four accounted for. Not a denominator effect, not a timeout reclassification, and — like the 09-09 raise —
one commit later than the tests that earned it, because the 45-minute run cannot live inside the test commit.

The run that produced this row was itself a regression fix, and it is worth recording why. The first
Phase 3 attempt scored 75.99 % against `break` 78 -- **below the floor** -- with 11 mutants NoCoverage,
clustered on the four-way ternaries at lines 365, 367 and 405 that name which `[MapProperty]` modifier
conflicts with a share or a dense fill. Those refusal messages had **never been executed** by any of the
7,838 tests then green: both new features shipped with their "which modifier" branch untested, and only
the mutation tier saw it. Eight `[Theory]` tests (four arms each, commit `7d50320`) closed it.

One measurement worth recording beside the score, because it bounds what this leg can mean: the run created
roughly **15,029 mutants and could test only 302** -- the rest were dropped as compile errors or removed
by the mutate filter. About 98 % of what the pipeline globs generate never runs, so the score
describes the small fraction that compiles. That is not an error (an uncompilable mutant cannot be killed),
but the leg's reach over the pipeline is far narrower than its `mutate` list suggests, and the gap should be
quantified deliberately rather than rediscovered.

Fuller arithmetic, carried from the research and updated by P5 (context, not gates — the figures below
predate the 2026-09-06 denominator refresh above and are kept for their reasoning, not their totals): the generator leg's
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
    "Issues/ledgers/codefixes-mutation-survivors.md",
    "Issues/ledgers/pipeline-mutation-survivors.md"
  ],
  "legs": {
    "generator": {
      "config": "stryker-config.json",
      "scoreable": 415,
      "measuredRawScore": 96.62,
      "measuredOn": "2026-09-23",
      "provenEquivalent": 14,
      "ruledInPractice": 0,
      "probablyEquivalent": 0,
      "reportedPhantomKills": 2,
      "rawCeiling": 96.62,
      "rawCeilingFormula": "(415 - 14) / 415 — and on 2026-09-23 the leg MET it: 401 of 415 killed, 14 survived, and the 14 are exactly the 14 adjudicated rows, one for one. The prediction recorded here on 2026-09-21 was the check, and it worked in the direction nobody plans for: the run (StrykerOutput/2026-09-23.18-45-29) REPORTED 403/415 = 97.11 %, which is ABOVE this ceiling and therefore impossible unless a proof is wrong or the report is contaminated. It was the report. The two extra kills are IsPrimitive's SByte/Int16 and UInt16/Int32 flips; each was planted permanently in src/ and the whole solution run, 10,146 tests green across 9 assemblies, so neither is killable by anything this repository owns. All 12 scoreable mutants on that line carry \"static\": true, and Stryker runs a static mutant against the entire suite because it cannot attribute coverage to one - so any failure anywhere counts as its kill. The suite grew by nine tests between the runs with no source change, and five survivors became zero. The floor is pinned at the reproducible 96, not the reported 97. A ceiling is usually a bound on ambition; this is the run where it worked as an instrument."
    },
    "doctooling": {
      "config": "stryker-config.doctooling.json",
      "scoreable": 290,
      "measuredRawScore": 96.55,
      "measuredOn": "2026-09-26",
      "provenEquivalent": 10,
      "ruledInPractice": 0,
      "probablyEquivalent": 0,
      "rawCeiling": 96.55,
      "rawCeilingFormula": "(290 - 10) / 290 — and on 2026-09-26 the leg REACHED it: 280 killed of 290, 10 survived, and the 10 are the 10 proven rows below, matched one for one. The denominator moved 289 -> 290 because 4fbd936 extracted the two file-listing seams that made the search pattern testable, and the score moved because that pinned this leg's only two undispositioned survivors: the `\"*.cs\"` argument blanked to `\"\"` in ExampleCatalogue and SnippetScanner. They had survived on a fact that reads backwards - a blank searchPattern does not match NOTHING, Directory.GetFiles widens it to EVERY file - so the mutant read too much rather than too little, and no non-.cs file in the corpus happened to carry an example ordinal or a snippet marker. Every undetected mutant in this leg is now adjudicated."
    },
    "runtime": {
      "config": "stryker-config.runtime.json",
      "scoreable": 126,
      "measuredRawScore": 97.62,
      "measuredOn": "2026-09-14",
      "provenEquivalent": 3,
      "ruledInPractice": 0,
      "probablyEquivalent": 1,
      "rawCeiling": 97.61,
      "rawCeilingFormula": "(126 - 3) / 126 — denominator re-measured after the Key record-struct ruling (StrykerOutput/2026-09-14.20-10-34, 123 killed of 126 scoreable, 0 timeouts, clean tree; break stays 97). The Key.Equals(Key) ruled-in-practice row was retired the same day; the DwarfRefContext upper-clamp row was added with its proof. The three undetected mutants are exactly the three proven rows (lower clamp, upper clamp, facade TryGet guard), so the score sits at the leg's honest ceiling."
    },
    "codefixes": {
      "config": "stryker-config.codefixes.json",
      "scoreable": 178,
      "measuredRawScore": 87.64,
      "measuredOn": "2026-09-15",
      "provenEquivalent": 22,
      "ruledInPractice": 0,
      "probablyEquivalent": 0,
      "rawCeiling": 87.64,
      "rawCeilingFormula": "(178 - 22) / 178 — denominator re-measured after the probably-equivalent trivia row was retired (StrykerOutput/2026-09-15.18-55-18, 156 killed of 178 scoreable, clean tree at 48213d0; break stays 87). Against the 7f96555 run (156/179) the only change is that retired survivor, gone because the `??` it mutated is gone; Stryker generates no mutant for the indexer that replaced it. The 22 undetected mutants are exactly the 22 proven rows below, so the measured score IS the leg's honest ceiling."
    },
    "testing": {
      "config": "stryker-config.testing.json",
      "scoreable": 110,
      "measuredRawScore": 100.0,
      "measuredOn": "2026-09-21",
      "provenEquivalent": 0,
      "ruledInPractice": 0,
      "probablyEquivalent": 0,
      "rawCeiling": 100.0,
      "rawCeilingFormula": "(110 - 0) / 110 — nothing is adjudicated equivalent in this leg, and nothing needs to be: the measurement now EQUALS the ceiling. Re-measured on a clean tree at 220fabe (StrykerOutput/2026-09-21.19-22-14): 110 detected of 110 scoreable = 100.00 %, 106 killed and 4 timeouts, 0 survived, 0 uncovered. The eleven survivors of 2026-09-15 fell to tests that state contracts nobody had stated: the field walk, the depth cap through all three recursions, the scalar return that stops a string being walked as characters, both epsilon comparisons as EXCLUSIVE, and the iteration bounds of both verifiers. The four timeouts remove TERMINATION (three `i++` -> `i--`, one `depth + 1` -> `depth - 1`), which is why they count as detections here and why the leg fuse moved 30 -> 45 minutes in 220fabe: killing a cheap survivor can make a leg slower."
    },
    "pipeline": {
      "config": "stryker-config.pipeline.json",
      "scoreable": 284,
      "measuredRawScore": 94.72,
      "measuredOn": "2026-09-26",
      "provenEquivalent": 15,
      "ruledInPractice": 0,
      "probablyEquivalent": 0,
      "rawCeiling": 94.71,
      "rawCeilingFormula": "(284 - 15) / 284 — and on 2026-09-26 the leg REACHED it: 269 killed of 284, 15 survived, and the 15 are the 15 proven rows below, matched one for one. Every undetected mutant in this leg is adjudicated, so 94.72 % is both the measurement and the most this leg can score without a row being retired. The two that closed were not killed by new tests alone: they were the pair this ledger carried as 'left open, on purpose' (the leading-dot IndexOf tests), where the MUTANT produced the more legible diagnostic and pinning the original would have locked an awkward message in place. 3f51061 adopted the mutant in source instead - DWARF009/DWARF008 naming the string the consumer wrote, rather than DWARF043/DWARF045 blaming an empty member they did not - and both mutants died with the behaviour they described. The denominator did not move (284 before and after), because an equality mutator generates the same variants for `> 0` as for `>= 0`. Note that the THIRD copy of that comparison, the ApplySkipNullSourceMembers row at line 59, remains proven-equivalent and its proof is now STRONGER: it rests on no SourceName beginning with '.', and a leading-dot name is refused even earlier than before."
    }
  },
  "entries": [
    {
      "leg": "generator",
      "file": "src/DwarfMapper.Generator/Pipeline/BlittableProof.cs",
      "member": "LayoutIdentical",
      "lineAtProof": 28,
      "lineCurrent": 396,
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
      "lineCurrent": 411,
      "mutator": "Logical",
      "original": "IsPrimitive(a) || IsPrimitive(b)",
      "mutated": "IsPrimitive(a) && IsPrimitive(b)",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Re-proved 2026-09-21; the original reason (the branch's true return is unreachable) is FALSE since byBytesOnly was added - [Reinterpret] accepts two different primitives of equal width there. The conclusion survives on the guard itself: the mutant differs only when exactly one side is primitive, and for such a pair the original enters the branch and refuses it (different widths under byBytesOnly, different SpecialTypes otherwise, since a non-primitive's size is 0 and its SpecialType is None), while the mutant skips the branch and is refused by IsSourceSequential - a primitive is a metadata symbol with no source [StructLayout] to read. Both answers are false.",
      "anchor": "Issues/ledgers/T3-mutation-survivors.md § BlittableProof.LayoutIdentical / IsPrimitive"
    },
    {
      "leg": "generator",
      "file": "src/DwarfMapper.Generator/Pipeline/BlittableProof.cs",
      "member": "IsPrimitive",
      "lineAtProof": 58,
      "lineCurrent": 515,
      "mutator": "Logical (pattern)",
      "original": "one `or` in the SpecialType pattern, where the two types it joins have DIFFERENT widths (2 of the 5 flips Stryker generates)",
      "mutated": "that `or` -> `and`, dropping both types it joined",
      "occurrences": 2,
      "category": "proven-equivalent",
      "proof": "VERIFIED BY PLANTING, 2026-09-23: each of the two was planted permanently in src/ and the whole solution run - 10,146 tests green across 9 assemblies - so neither is killable by anything this repository owns. NOTE FOR THE NEXT READER: the leg REPORTS them killed anyway; all 12 scoreable mutants on this line carry \"static\": true and Stryker runs a static mutant against the entire suite, counting any failure as its kill. Do not retire these rows on the strength of a report that says Killed - plant the mutant first. RE-ADJUDICATED 2026-09-21, 12 -> 2 (see the section 'Rows re-adjudicated on 2026-09-21'): the three flips joining two types of the SAME width - Boolean/Byte, Byte/SByte, Int16/UInt16 - were NOT equivalent and are now killed by tests, because [Reinterpret] accepts two different primitives of equal width. The two that remain join SByte/Int16 and UInt16/Int32. A dropped pair changes an answer only when both operands are inside it (otherwise the `||` at each of the two call sites still fires); of those four pairs the two same-type ones return at the identity check above, leaving (sbyte, short) and (ushort, int), which the original refuses on width and SpecialType and the mutant refuses at IsSourceSequential. Both answers are false.",
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
      "member": "CanReinterpret / SameBytesIgnoringNames (top-level Nullable<T> guard)",
      "lineAtProof": 37,
      "lineCurrent": 37,
      "mutator": "Logical",
      "original": "IsNullableValueType(src) || IsNullableValueType(dst)",
      "mutated": "IsNullableValueType(src) && IsNullableValueType(dst)",
      "occurrences": 2,
      "category": "proven-equivalent",
      "proof": "Proved 2026-09-21 (lines 37 and 67, the same guard in both entry points). The mutant differs only when exactly ONE side is Nullable<T>, and it then falls through to LayoutIdentical, which returns false for every such pair: the both-Nullable arm needs both sides; the primitive arm gives false (a Nullable<T>'s width is 0 and its SpecialType is None, so neither the byBytesOnly width test nor SameSpecialType can hold); and the struct rules refuse at IsSourceSequential, because Nullable<T> is a metadata type with no source [StructLayout] to read. The guard is a documented EARLY refusal naming the real reason (CS0453 - MemoryMarshal.Cast's `struct` constraint rejects Nullable<T>), not a load-bearing one. The THIRD copy of this guard, at line 217 in TryExplainNearMiss, is NOT equivalent - its fall-through reaches the metadata blocker and speaks - and is killed by TryExplainNearMiss_a_top_level_nullable_is_refused_even_when_its_fields_line_up.",
      "anchor": "tests/DwarfMapper.Generator.Tests/Coverage/BlittableProofCoverageTests.cs § CanReinterpret_nullable_*"
    },
    {
      "leg": "generator",
      "file": "src/DwarfMapper.Generator/Pipeline/BlittableProof.cs",
      "member": "TryExplainNearMiss (primitive shortcut)",
      "lineAtProof": 223,
      "lineCurrent": 223,
      "mutator": "Logical",
      "original": "IsPrimitive(src) || IsPrimitive(dst)",
      "mutated": "IsPrimitive(src) && IsPrimitive(dst)",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Proved 2026-09-21, and MEASURED rather than assumed: the mutant differs only when exactly one side is primitive, and it then falls through to the shape check, where InstanceFields of a primitive is EMPTY - a probe over this test suite's own reference set confirms 0 fields for Int32, SByte and UInt16 - so `fa.Count == 0` refuses the pair. Both answers are false. Recorded with its dependency stated: this holds because the fields of a primitive are not visible here, which is a property of the reference set, so a run whose corlib exposed a primitive's private backing field would make the pair speak and this row would need re-adjudicating.",
      "anchor": "Issues/ledgers/equivalent-mutants.md § Rows re-adjudicated on 2026-09-21"
    },
    {
      "leg": "generator",
      "file": "src/DwarfMapper.Generator/Pipeline/BlittableProof.cs",
      "member": "TryExplainNearMiss (managed shortcut)",
      "lineAtProof": 244,
      "lineCurrent": 244,
      "mutator": "Logical",
      "original": "!a.IsUnmanagedType || !b.IsUnmanagedType",
      "mutated": "!a.IsUnmanagedType && !b.IsUnmanagedType",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Proved 2026-09-21, the same shape as the LayoutIdentical row above and for the same reason one level up. The mutant differs only when exactly one side is managed; managed-ness enters at a reference-type leaf, so somewhere in the positional walk a field position holds a reference type against a value type, and LayoutIdentical refuses that position (TypeKind != Struct, or the managed guard at line 396). The near-miss loop turns that refusal into `return false` at its own line 287, so the pair stays silent either way.",
      "anchor": "Issues/ledgers/equivalent-mutants.md § Rows re-adjudicated on 2026-09-21"
    },
    {
      "leg": "generator",
      "file": "src/DwarfMapper.Generator/Pipeline/BlittableProof.cs",
      "member": "TryExplainNearMiss (metadata blockers)",
      "lineAtProof": 294,
      "lineCurrent": 294,
      "mutator": "Linq method (Any -> All)",
      "original": "!a.Locations.Any(l => l.IsInSource)",
      "mutated": "!a.Locations.All(l => l.IsInSource)",
      "occurrences": 2,
      "category": "proven-equivalent",
      "proof": "Proved 2026-09-21 (lines 294 and 300, the same predicate for each side). Any and All differ only on an EMPTY sequence or on a MIXED one. Neither reaches this line: the type has already passed `TypeKind == Struct`, so it is a real named struct, and a named struct's Locations are never empty - a probe confirms a metadata type carries exactly one MetadataLocation - nor mixed, because a symbol is either from source (every location a source location, however many partial declarations it has) or from metadata, never both. With a uniform non-empty sequence Any and All coincide, so the two predicates agree on every input that gets here.",
      "anchor": "Issues/ledgers/equivalent-mutants.md § Rows re-adjudicated on 2026-09-21"
    },
    {
      "leg": "generator",
      "file": "src/DwarfMapper.Generator/Pipeline/ConstructorSelector.cs",
      "member": "Select (Policy 4, single-candidate fast path)",
      "lineAtProof": 157,
      "lineCurrent": 157,
      "mutator": "Block removal",
      "original": "if (candidates.Count == 1)",
      "mutated": "its block emptied - the `return candidates[0];` removed, so Policy 4 falls through to Policy 5",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Proved 2026-09-21 by following Policy 5 with a one-element list, which is what the mutant falls through to. The satisfiability filter either keeps that element or produces an empty list, which the `satisfiable.Count > 0` guard discards; Max over one element is its own arity; withMax is therefore that same element; `withMax.Count > 1` is false, so line 193 returns the identical symbol Policy 4 would have returned. No diagnostic is added on either path and AllParametersHaveASource is a pure predicate, so there is no side effect to distinguish them. Policy 4 is a readability fast path, not a rule.",
      "anchor": "Issues/ledgers/equivalent-mutants.md § Rows re-adjudicated on 2026-09-21"
    },
    {
      "leg": "generator",
      "file": "src/DwarfMapper.Generator/Diagnostics/LocationInfo.cs",
      "member": "FromFirstInSource",
      "lineAtProof": 49,
      "lineCurrent": 49,
      "mutator": "Conditional (false)",
      "original": "declared is null ? null : From(declared)",
      "mutated": "false ? null : From(declared)",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Proved 2026-09-21 by reading From: its first statement is `if (location is null || location.SourceTree is null) return null`. So when `declared` is null - the only input on which the two differ - the original yields null by the ternary and the mutant yields null by that guard, and the `?? fallback` that consumes both turns either into `fallback`. The ternary is a null-check written twice, once at each level; it is not redundant in the source (it keeps a nullable value out of a non-nullable parameter), but it is unobservable at runtime.",
      "anchor": "tests/DwarfMapper.Generator.Tests/Coverage/LocationInfoFromFirstInSourceUnitTests.cs § the fallback arm"
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
      "lineCurrent": 150,
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
      "lineCurrent": 87,
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
      "lineCurrent": 129,
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
      "lineCurrent": 202,
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
      "lineCurrent": 110,
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
      "lineCurrent": 133,
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
      "lineCurrent": 144,
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
      "file": "src/DwarfMapper/DwarfRefContext.cs",
      "member": "DwarfRefContext..ctor (upper clamp)",
      "lineAtProof": 106,
      "lineCurrent": 106,
      "mutator": "Equality",
      "original": "maxDepth > DwarfLimits.AbsoluteMaxDepth",
      "mutated": "maxDepth >= DwarfLimits.AbsoluteMaxDepth",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The two forms differ on exactly one input, maxDepth == AbsoluteMaxDepth, and agree there: the original takes the fall-through arm and yields maxDepth, which IS AbsoluteMaxDepth; the mutant takes the clamp arm and yields the constant AbsoluteMaxDepth. Identical value for every input, so no test can distinguish them — the mirror of the lower-clamp row above. Its 2026-09-07 'kill' was an accidental static kill (static=True, coveredBy=0, killedBy only GeneratedDocsAreCurrentTests.The_api_reference_matches_the_public_surface); it survived honestly in StrykerOutput/2026-09-14.19-15-03 and 2026-09-14.20-10-34.",
      "anchor": "Issues/ledgers/round30-ledger.md § Owner rulings (runtime mutation floor); Issues/ledgers/E3-E1-report.md § the equivalent mutant (DwarfRefContext L77 Equality)"
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
      "lineAtProof": 172,
      "lineCurrent": 177,
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
      "lineCurrent": 181,
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
      "lineCurrent": 186,
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
      "lineCurrent": 294,
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
      "lineCurrent": 294,
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
      "lineCurrent": 352,
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
      "lineCurrent": 354,
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
      "lineCurrent": 372,
      "mutator": "String mutation",
      "original": "source = target = string.Empty",
      "mutated": "source = target = \"Stryker was here!\"",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Initialises the out parameters immediately before \"return false\", so the compiler is satisfied. Every caller is of the form \"if (TryRead... && TryRead...)\" and reads the outs only on the true path. WEAKER THAN THE OTHERS: this rests on caller discipline rather than on the language, and becomes killable the day a caller reads the outs after a false return.",
      "anchor": "Issues/ledgers/codefixes-mutation-survivors.md"
    },
    {
      "leg": "pipeline",
      "file": "src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs",
      "member": "ApplySkipNullSourceMembers (empty-list guard)",
      "lineAtProof": 37,
      "lineCurrent": 37,
      "mutator": "Equality mutation",
      "original": "acc.Result.Count > 0",
      "mutated": "acc.Result.Count >= 0",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "With >= 0 the block also runs on an empty member list: it builds two lookups and iterates zero members. Nothing is added, changed or reported; the forms differ only in an allocation.",
      "anchor": "Issues/ledgers/pipeline-mutation-survivors.md § ApplySkipNullSourceMembers"
    },
    {
      "leg": "pipeline",
      "file": "src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs",
      "member": "ApplySkipNullSourceMembers (dotted-source test)",
      "lineAtProof": 59,
      "lineCurrent": 59,
      "mutator": "Equality mutation",
      "original": "m.SourceName.IndexOf('.') >= 0",
      "mutated": "m.SourceName.IndexOf('.') > 0",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Differs only for a SourceName beginning with '.'. Every SourceName in acc.Result is \"\", a symbol name, Root + \".\" + Leaf with a symbol-name root, or a dotted path TryResolvePath accepted (whose first segment equalled a readable member's name, so is non-empty). None begins with '.'.",
      "anchor": "Issues/ledgers/pipeline-mutation-survivors.md § ApplySkipNullSourceMembers"
    },
    {
      "leg": "pipeline",
      "file": "src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs",
      "member": "ApplySkipNullSourceMembers (skip chain, first ||)",
      "lineAtProof": 58,
      "lineCurrent": 58,
      "mutator": "Logical mutation",
      "original": "string.IsNullOrEmpty(m.SourceName) ||",
      "mutated": "string.IsNullOrEmpty(m.SourceName) &&",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Differs iff exactly one of {empty source, dotted source} holds and every later term is false; the mutant then falls through to srcTypeByName.TryGetValue with \"\" or a dotted name, which fails (lemma L1: keys are member names under Ordinal/OrdinalIgnoreCase), so nothing is assigned.",
      "anchor": "Issues/ledgers/pipeline-mutation-survivors.md § ApplySkipNullSourceMembers"
    },
    {
      "leg": "pipeline",
      "file": "src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs",
      "member": "ApplySkipNullSourceMembers (skip chain, second ||)",
      "lineAtProof": 59,
      "lineCurrent": 59,
      "mutator": "Logical mutation",
      "original": "m.SourceName.IndexOf('.') >= 0 ||",
      "mutated": "m.SourceName.IndexOf('.') >= 0 &&",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Differs iff (empty-or-dotted source) xor (ValueExpression set), with every later term false. The first case falls through to a lookup that fails (L1); the second needs a ValueExpression member with a non-empty undotted SourceName, and none exists (L2: the only two ValueExpression constructions pass \"\").",
      "anchor": "Issues/ledgers/pipeline-mutation-survivors.md § ApplySkipNullSourceMembers"
    },
    {
      "leg": "pipeline",
      "file": "src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs",
      "member": "ApplySkipNullSourceMembers (skip chain, third ||)",
      "lineAtProof": 60,
      "lineCurrent": 60,
      "mutator": "Logical mutation",
      "original": "m.ValueExpression is not null ||",
      "mutated": "m.ValueExpression is not null &&",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Differs iff (empty/dotted/ValueExpression) xor (unflatten), with When, SkipIfSourceNull and the deferrable test all false. An unflatten member has a dotted TargetName, never in deferrableTargets (L3), so its case contradicts the premise; the other case falls through to a lookup that fails (L1) or needs a member L2 rules out.",
      "anchor": "Issues/ledgers/pipeline-mutation-survivors.md § ApplySkipNullSourceMembers"
    },
    {
      "leg": "pipeline",
      "file": "src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs",
      "member": "ResolveMapValues (DWARF040 continue)",
      "lineAtProof": 153,
      "lineCurrent": 161,
      "mutator": "Statement mutation",
      "original": "continue;",
      "mutated": "(the continue after the MapValueTypeMismatch report removed)",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The fall-through adds a MemberMap with ValueExpression \"\". DWARF040 is an Error, and an Error withholds the mapper's whole emission (DWARF078 names the mechanism), so it is never emitted; every later reader of acc.Result (skip-null pass, DWARF070 report, source coverage, dense post-pass) produces nothing from it. WEAKER THAN THE OTHERS: rests on the error-suppresses-emission invariant.",
      "anchor": "Issues/ledgers/pipeline-mutation-survivors.md § ResolveMapValues"
    },
    {
      "leg": "pipeline",
      "file": "src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs",
      "member": "ResolveMapValues (DWARF041 continue)",
      "lineAtProof": 167,
      "lineCurrent": 175,
      "mutator": "Statement mutation",
      "original": "continue;",
      "mutated": "(the continue after the MapValueUseInvalid report removed)",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Same shape as the DWARF040 row: the fall-through adds a MemberMap carrying Escape(mv.Use) + \"()\" for a mapper whose emission the Error already withholds (DWARF078), and no later reader of acc.Result reports off it. WEAKER THAN THE OTHERS: rests on the error-suppresses-emission invariant.",
      "anchor": "Issues/ledgers/pipeline-mutation-survivors.md § ResolveMapValues"
    },
    {
      "leg": "pipeline",
      "file": "src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs",
      "member": "ResolveMapValues (constant member SourceName)",
      "lineAtProof": 156,
      "lineCurrent": 164,
      "mutator": "String mutation",
      "original": "new MemberMap(mvTgt, \"\", ValueExpression: literal)",
      "mutated": "new MemberMap(mvTgt, \"Stryker was here!\", ValueExpression: literal)",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Every consumer of SourceName either short-circuits on ValueExpression (skip-null pass, AddPlanLine, AppendValueExpression), is gated on NullRefIntoNonNullable (DWARF070 report), or looks the string up among real source members (AddConsumed/ReportUnconsumed, MemberTypeByName), where a non-identifier matches nothing exactly as \"\" does.",
      "anchor": "Issues/ledgers/pipeline-mutation-survivors.md § ResolveMapValues"
    },
    {
      "leg": "pipeline",
      "file": "src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs",
      "member": "ResolveMapValues (Use= member SourceName)",
      "lineAtProof": 174,
      "lineCurrent": 182,
      "mutator": "String mutation",
      "original": "new MemberMap(mvTgt, \"\", ValueExpression: Identifiers.Escape(mv.Use) + \"()\")",
      "mutated": "new MemberMap(mvTgt, \"Stryker was here!\", ValueExpression: Identifiers.Escape(mv.Use) + \"()\")",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Same consumer enumeration as the constant-member row: ValueExpression short-circuits the emitter and the skip-null pass, NullRefIntoNonNullable gates DWARF070, and the remaining readers resolve the string against real source members, where it matches nothing.",
      "anchor": "Issues/ledgers/pipeline-mutation-survivors.md § ResolveMapValues"
    },
    {
      "leg": "pipeline",
      "file": "src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs",
      "member": "ResolveExplicitMaps (unflatten extras guard)",
      "lineAtProof": 222,
      "lineCurrent": 245,
      "mutator": "Logical mutation",
      "original": "TryGetValue(tgtName, out var uex) && (uex.When is not null || uex.HasNullSub)",
      "mutated": "TryGetValue(tgtName, out var uex) || (uex.When is not null || uex.HasNullSub)",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "On a miss the out tuple is default (When null, HasNullSub false) and both forms are false. On a hit both are true, because all four producers of ExtrasByTarget (ReadMapPropertyExtras, MatchPairProps, the co-located host reader, MapConfig MapOr) admit an entry only when HasNullSub || When is not null.",
      "anchor": "Issues/ledgers/pipeline-mutation-survivors.md § ResolveExplicitMaps"
    },
    {
      "leg": "pipeline",
      "file": "src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs",
      "member": "ResolveExplicitMaps (hasExtras)",
      "lineAtProof": 347,
      "lineCurrent": 368,
      "mutator": "Logical mutation",
      "original": "lookups.ExtrasByTarget.TryGetValue(tgtName, out var shareExtras) &&",
      "mutated": "lookups.ExtrasByTarget.TryGetValue(tgtName, out var shareExtras) ||",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "Identical argument to the unflatten extras guard: a miss yields a default tuple whose HasNullSub/When test is false, a hit yields an entry every producer admitted only with HasNullSub || When is not null, so the right operand equals the lookup result on every input.",
      "anchor": "Issues/ledgers/pipeline-mutation-survivors.md § ResolveExplicitMaps"
    },
    {
      "leg": "pipeline",
      "file": "src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs",
      "member": "ResolveExplicitMaps (synthBeforeConversion snapshot)",
      "lineAtProof": 330,
      "lineCurrent": 351,
      "mutator": "Conditional (true) mutation",
      "original": "var synthBeforeConversion = req.StringFormats is not null && req.StringFormats.ContainsKey(tgtName)",
      "mutated": "var synthBeforeConversion = true",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The snapshot is read at exactly one site, inside the StringFormats.TryGetValue branch; for a member with no format the forced snapshot is a HashSet nothing reads, for one with a format the original took it too. Emitted text identical.",
      "anchor": "Issues/ledgers/pipeline-mutation-survivors.md § ResolveExplicitMaps"
    },
    {
      "leg": "pipeline",
      "file": "src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs",
      "member": "ResolveExplicitMaps (When-predicate search)",
      "lineAtProof": 536,
      "lineCurrent": 558,
      "mutator": "Statement mutation",
      "original": "break;",
      "mutated": "(the break after ok = true removed)",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The loop body's only effect is ok = true; nothing resets ok and a later iteration can only set it again. Removing the early exit changes the iteration count, not the outcome.",
      "anchor": "Issues/ledgers/pipeline-mutation-survivors.md § ResolveExplicitMaps"
    },
    {
      "leg": "pipeline",
      "file": "src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs",
      "member": "ResolveAutoMatchedMembers (extra-parameter HandledTargets)",
      "lineAtProof": 758,
      "lineCurrent": 786,
      "mutator": "Statement mutation",
      "original": "acc.HandledTargets.Add(target.Name);",
      "mutated": "(the HandledTargets.Add in the extra-parameter arm removed)",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "HandledTargets is read afterwards only for LATER targets (WritableMembers yields each name once) and by the read-only silent-loss guard (names disjoint from writable targets); every other reader ran before this pass.",
      "anchor": "Issues/ledgers/pipeline-mutation-survivors.md § ResolveAutoMatchedMembers"
    },
    {
      "leg": "pipeline",
      "file": "src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs",
      "member": "ResolveAutoMatchedMembers (extra-parameter SourceName)",
      "lineAtProof": 731,
      "lineCurrent": 758,
      "mutator": "String mutation",
      "original": "<empty-quotes literal> as the extra-parameter MemberMap's SourceName",
      "mutated": "\"Stryker was here!\" as the extra-parameter MemberMap's SourceName",
      "occurrences": 1,
      "category": "proven-equivalent",
      "proof": "The member carries SourceAccessExpression and every consumer prefers it (DWARF070 report, AddPlanLine, AppendValueExpression, the ThrowIfNull message); the skip-null pass falls through to a lookup that fails (L1); AddConsumed/ReportUnconsumed resolve the string against real source members, where it matches nothing.",
      "anchor": "Issues/ledgers/pipeline-mutation-survivors.md § ResolveAutoMatchedMembers"
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
