<!-- SPDX-License-Identifier: GPL-2.0-only -->

# A10 — WIP notes: `CS8795` misread as `NotCompilable`

**Incomplete.** The classifier change is in and compiles; the ceiling that must accompany it is not. The
surface matrix is **RED** at this commit, on the ratchet's lower bound and nothing else.

## The discrimination rule (implemented in `SurfaceProbe.Classify`)

A new CS error is re-read as `Refused` **iff**:

1. **every** CS id the case introduced is an absent-emission id — `AbsentEmissionErrorIds = ["CS8795"]`; **and**
2. at least one **new, non-`DWARF078`, Error-severity** DWARF diagnostic exists.

Otherwise `NotCompilable`. `DWARF078` is filtered out of the added-diagnostic list *before* the Error test
reads it, so the cascade signpost can never be the sole evidence of a refusal.

Both halves are load-bearing. Dropping (1) lets a `CS8795` + genuine `CS0111` case read as a clean refusal,
hiding a real placement defect. Dropping (2) absorbs "declined to emit and said nothing" into the judged
population, which is the worst outcome available here.

`FirstNewOccurrence` → **`NewOccurrences`** (all new ids, ordinal order) because (1) is a universal quantifier
and a first-only function answers it about one id while the case introduced two.

## Measured, at `aa8262c` vs. this commit (whole-matrix dump, 854 cells)

| Effect | Before | After |
|---|---:|---:|
| `Refused` | 275 | **361** |
| `NotCompilable` | **96** | **10** |
| `Honoured` / `Silent` / `NoSuchSite` / `Unasked` / `UnhonouredButLoud` | 160 / 147 / 137 / 25 / 14 | unchanged |

**One transition, 86 × `NotCompilable → Refused`. Nothing else moved.** `96 = 86 + 10`, exact.

All 86 carried a new blocking DWARF: `DWARF064`×18, `084`×15, `005`×15, `038`×14, `042`×7, `072`×6, `040`×6,
`041`×5, `028`×5, `077`×4, `050`×3, `049`×3, `014`×3, `082`×2, `035`×2, `087`×1, `052`×1. **None** was the
"declined to emit while saying nothing" case.

Residual 10, all honest compiler rejections: `[DwarfMapper(ReferenceHandling = Preserve)]` @ SpanMap and
AsyncStream (`CS7036` — `Preserve` changes the generated signature, so the template's partial declaration fits
no overload), and eight `GenerateMap<Src, Dst>` duplicate-declaration cells (`CS0111,CS0121`).

## FINDING — the brief's expectation was wrong, and this is the item to carry forward

**Zero new divergences. Not "none found yet" — none is possible under this rule, and the dumps confirm none
appeared.**

- All 96 `NotCompilable` cells were already **`CLAIMED`** (measured: 86/8/2, zero unclaimed). The under-reach
  direction has an empty population by construction.
- The rule can only produce `Refused` or `NotCompilable`; it can never produce `Silent`. `claimed ∧ Silent` is
  **39** in both dumps; `Silent` totals 147 in both.
- `unclaimed ∧ judged ∧ failing` = **0** before *and* after.

So A10's premise is half right: the 96 were indeed skipped, but judging them exposes nothing new, because the
only verdict available to them is one their claim already accepts. **A10's value is not new findings — it is
that 86 cells stop being exempt from the instrument.** If any of them ever goes quiet, the matrix now goes red
instead of shrugging.

## Residual doubt about the rule, for whoever resumes

The rule is safe in the direction that matters: it cannot manufacture a false `Silent` and cannot move a live
cell into an unjudged population — it does the reverse. **But two of its four truth-table combinations have no
real cell to exercise them**: `CS8795` with *no* blocking DWARF, and `CS8795` *mixed with* a genuine placement
error. Both are covered only at the unit level. If an endpoint template ever declares a second partial mapping
method, those paths go live untested by any cell.

## Left to do

1. **`NotCompilableCellCeiling` 96 → 10** (`Contracts/SurfaceParityTests.cs:202`). Measured. The only edit
   between here and green. No ratchet is raised; the other six re-measured unchanged.
2. Rewrite the XML doc on `The_cells_the_compiler_rejects_are_counted` — it narrates "becomes `Refused` when
   R4 does", and A10 *is* that event. Its `howToClose` still prescribes fixing R4.
3. Two end-to-end pin tests in `SurfaceProbeTests` (untraited, keeps the `SurfaceMatrix` leg at 865):
   `AutoNest(ctor(1))` on a `Method` @ `CreateMap` → must read `Refused` (`DWARF005` behind `CS8795`);
   `[GenerateMap<Src, Dst>]` ×2 on a `Class` @ `CreateMap` → must stay `NotCompilable` (`CS0111,CS0121`).
   Keep the existing illegal-site fact; it pins a different thing.
4. Extract `IsGeneratorRefusal(newCompilerErrorIds, addedDiagnostics)` as an internal pure predicate so the
   full truth table is unit-testable — this answers the residual doubt above. Was in progress when work
   stopped.
5. Full suite + `dotnet build DwarfMapper.NET.sln` (samples included).

`NotCompilable`'s detail string is now the joined list of new CS ids, not just the first — which is why the
residual rows read `CS0111,CS0121` where they used to read `CS0111`. The second id was always there and was
being dropped.
