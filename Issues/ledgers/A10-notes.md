<!-- SPDX-License-Identifier: GPL-2.0-only -->

# A10 — `CS8795` was read as `NotCompilable` where it means `Refused`

**Done.** The surface matrix is green at **865/865**, the whole solution builds with samples at 0/0, and
`NotCompilableCellCeiling` fell **96 → 10**. No ratchet was raised.

## The discrimination rule (`SurfaceProbe.IsGeneratorRefusal`, applied in `Classify`)

A new CS error is re-read as `Refused` **iff**:

1. **every** CS id the case introduced is an absent-emission id — `AbsentEmissionErrorIds = ["CS8795"]`; **and**
2. at least one **new, non-`DWARF078`, Error-severity** DWARF diagnostic exists.

Otherwise `NotCompilable`. `DWARF078` is filtered out of the added-diagnostic list *before* the Error test
reads it, so the cascade signpost can never be the sole evidence of a refusal.

Both halves are load-bearing. Dropping (1) lets a `CS8795` + genuine `CS0111` case read as a clean refusal,
hiding a real placement defect. Dropping (2) absorbs "declined to emit and said nothing" into the judged
population, which is the worst outcome available here. `FirstNewOccurrence` became **`NewOccurrences`** (all
new ids) because (1) is a universal quantifier and a first-only function answers it about one id while the
case introduced two.

The rule is a **pure predicate** so its whole truth table is testable: two of the four rows cannot be built
from any cell the matrix currently contains (`CS8795` with no blocking DWARF; `CS8795` mixed with a placement
error), and an untested branch guarding 86 cells is exactly the confidently-wrong instrument this file exists
to prevent.

`Classify` now **names the compiler error it re-read** in the detail — `DWARF005 (behind CS8795)`. Exactly the
86 reclassified rows carry that suffix. It keeps the end-to-end pin test non-vacuous without a second compile,
and it lets a reader of the `Refused` population tell a refusal that also breaks the build from one that does
not.

## Measured, `aa8262c` vs. this commit (whole-matrix dump, 854 cells)

| Effect | Before | After |
|---|---:|---:|
| `Refused` | 275 | **361** |
| `NotCompilable` | **96** | **10** |
| `Honoured` / `Silent` / `NoSuchSite` / `Unasked` / `UnhonouredButLoud` | 160 / 147 / 137 / 25 / 14 | unchanged |

**One transition, 86 × `NotCompilable → Refused`. Nothing else moved.** `96 = 86 + 10`, exact.

All 86 carried a new blocking DWARF: `DWARF064`×18, `084`×15, `005`×15, `038`×14, `042`×7, `072`×6, `040`×6,
`041`×5, `028`×5, `077`×4, `050`×3, `049`×3, `014`×3, `082`×2, `035`×2, `087`×1, `052`×1. **None** was the
"declined to emit while saying nothing" case.

The residual 10 are honest compiler rejections: `[DwarfMapper(ReferenceHandling = Preserve)]` @ `SpanMap` and
`AsyncStream` (`CS7036` — `Preserve` adds a reference-tracker parameter, so the template's partial declaration
fits no generated overload), and eight duplicate `[GenerateMap<Src, Dst>]` cells (`CS0111,CS0121`).

## The 86 satisfy their claims — measured, not inferred

The parity theory's own body was executed over every cell and its verdict recorded. **All 86 read
`PASS-claimed-branch`**; none reached a failing branch. Whole-matrix verdict census: 535 pass-claimed,
104 pass-unclaimed, 147 skipped-unjudged, 44 skipped-no-question, 24 would-fail-claimed — and those 24 are
exactly the 12 declared divergences plus the 12 structurally-excused option cells, both pre-existing and both
handled before the theory fails.

## FINDING — zero new divergences, and none is *possible*

This is stronger than "none were found", and it is stated so it can be falsified. Three checkable claims:

1. **The rule's codomain is `{Refused, NotCompilable}`.** The `newCompilerErrorIds.Count > 0` branch returns
   in *both* arms — `NotCompilable` when `IsGeneratorRefusal` is false, `Refused` when it is true. No cell
   entering that branch can reach the `Silent`, `Honoured` or `UnhonouredButLoud` paths below it. Read
   `Classify` and check.
2. **A divergence requires `Silent`.** `Every_cell_matches_the_elements_own_AppliesTo_claim` fails a claimed
   cell only when it is not `Honoured`/`Refused`/`UnhonouredButLoud`, and
   `Every_declared_divergence_is_still_a_divergence` demands `Silent` of every declared cell. `Silent` is
   unreachable from (1), so no reclassified cell can become one.
3. **All 96 were already `CLAIMED`** — measured in the before-dump: 86 `CS8795`, 8 `CS0111`, 2 `CS7036`, zero
   unclaimed. So the *under-reach* direction ("does not claim this endpoint but is `Refused` there", which
   would be a red cell demanding an `AppliesTo` widening) has an empty population by construction.

Corroborated by measurement either way: `Silent` totals 147 before and after; `claimed ∧ Silent` is 39 in
both; `unclaimed ∧ judged ∧ failing` is 0 in both.

**So A10's premise is half right.** The 96 were indeed skipped — but judging them exposes nothing new, because
the only verdict available to them is one their claim already accepts. **A10's value is not new findings; it
is that 86 cells stop being exempt from the instrument.** If any of them ever goes quiet, the matrix now goes
red instead of shrugging.

## The seven ratchets, all re-measured in this commit

| Ratchet | Before | Now |
|---|---:|---:|
| `NotCompilableCellCeiling` | 96 | **10** |
| `NoSuchSiteCellCeiling` | 137 | 137 |
| `UnaskableCellCeiling` | 44 | 44 |
| `UnhonouredButLoudCellCeiling` | 14 | 14 |
| `DivergenceFindingCeiling` | 6 | 6 |
| `DivergentCellCeiling` | 12 | 12 |
| `StructurallyExcusedCellCeiling` | 12 | 12 |

An eighth ratchet was touched and **deliberately not raised**: the first draft of the `Refused` pin test called
`GeneratorTestHarness.RunAndGetCompilationErrors` directly to prove the cell really carries `CS8795`, which
pushed `DirectCompileErrorCallBaseline` 53 → 54 and turned `Direct_compile_error_calls_have_not_grown` red.
Rather than raise it, `Classify` now reports the compiler error in its detail, so the test asserts the same
fact out of the probe's own output and the baseline stays at **53**.

## Out of scope, untouched by ruling

`DWARF088`'s Warning → Error escalation (**A12**); the `[MapNullSkip]`/`[MapValue]` projection one-liners
parked at their call sites by A6 and A8 (**A12**); A11's template slots. Stryker configs,
`scripts/housekeeping.ps1` and `.github/workflows/ci.yml` were not touched.
