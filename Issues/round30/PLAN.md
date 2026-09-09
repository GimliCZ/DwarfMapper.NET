<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 30 — the plan, ordered by what is already proven

Round 29 closed with three items that are **specified by measurement rather than by intuition**, which is
the difference between this plan and the backlog it replaces. Each one below names the evidence that opened
it and the instrument that will close it.

Two items that looked like round-30 work at the end of round 29 are already **closed and must not be
re-proposed**:

* **The constant-index switch alternative** to the dense fill — built and shipped in `0f1f5d2`.
* **The `List<T>` reference-element span fill** — measured as a non-win twice
  (`Issues/round26/FINDING-list-fill-strategy.md`), and the gap it was meant to close does not reproduce
  across seven decades (`benchmarks/results/2026-09-09-collection-decade-sweep.md`).

---

## A. WITHDRAWN — the null ternary is a decided behaviour, not a defect

**Withdrawn 2026-09-09 before any work started**, after the owner pointed at the git history. `6fa7308`
introduced `NullableProjectRefForgiving` for exactly this cell so that a failed `Result<T>`/`Outcome<T>`
maps to null rather than throwing, and DwarfMapper's own synthesized element helper makes the same decision
inside its body (`if (s is null) return null!;`). The two paths agree; my claim that they disagreed came
from reading one side and not the other.

The `Array` category's 8-29 % gap against Mapperly is therefore **the measured price of mapping a null
element to null** where Mapperly dereferences it. See `Issues/round30/FINDING-array-null-ternary.md`.

What survives is small and is documentation, not emission:

1. **One generator test** documenting which arm `FlatSrc[] -> FlatDst[]` takes under `enable`, `disable`
   and an explicit `FlatSrc?[]` source. Two predicates for "may be null" coexist in the pipeline
   (`== Annotated` at `CollectionConverter.cs:296`/`:405` against `!= NotAnnotated` in `SourceMayBeNullRef`);
   the test pins which one governs this cell so the next reader does not re-derive it from emitted code.
2. **A sentence in `docs/COMPARISON.md`** saying the `Array` row is a trade rather than a loss.

## B. Map fusion — the emitter, now that the measurement is closed on the right population

**Evidence:** `Issues/round29/SPIKE-map-fusion.md`, re-measured 2026-09-09 against real generated maps and
byte-identical to the hand-written probe. Approved by the owner ("2 ok").

The measurement half is finished. **What remains is entirely design**, and the spike says so:

1. **The refusal list** (spike caveat 3) — fusion changes observable behaviour wherever `B`'s construction
   is observable: `BeforeMap`/`AfterMap` hooks on the `A->B` pair, a `[RoundTrip]` verifier over `B`, a
   converter with a side effect, a cycle-preserving reference map whose identity table is keyed on `B`.
   Enumerating these is the first task, not the emitter.
2. **The private-chain requirement** (caveat 4) — if `A->B` is a public endpoint the consumer can call,
   fusing `A->C` does not remove `B`, it adds a second path.
3. **Scoped to element loops.** Straight-line fusion is dead weight: both arms allocate 40 B and tie on
   time, because the JIT already stack-allocates the intermediate.

## C. Benchmark axes 2-4 — the rest of what the owner asked for

**Evidence:** `Issues/round30/DESIGN-collection-benchmarks.md`. Axis 1 (N) shipped in round 29's close.
Three axes remain, and the third is a correctness instrument rather than a performance one.

* **Axis 2 — element kind.** Every reference-element number in this repository is `FlatSrc -> FlatDst`: four
  members, one string, settable properties. Add a wide DTO (~20 members), a string-heavy element, an element
  owning a nested object, a **constructor/record destination** (different emitted code, never timed in a
  collection at any size), and the `class[]` against `struct[]` pair that shows the storage lever
  `RESEARCH-hardware-mode.md` already identified.
* **Axis 3 — blittability by SIZE.** One 12-byte struct is one point. Sizes 4/8/16/32/64/128 B across the N
  sweep; the blit's usage space along size is still undeclared, and round 29's "always on" was concluded
  from 12 bytes.
* **Axis 3b — the refusal controls, which are the real gap.** Nothing anywhere proves the blit REFUSES a
  reordered, reference-carrying or differently-padded struct. `[StructLayout(Auto)]` stands in for the whole
  eligibility rule. A refusal that silently became a byte copy would transpose or corrupt user data — the
  worst defect available in this design — and today no test or benchmark would notice.

## D. Carried, unchanged

* The two **netstandard2.0 analyzer assemblies** sit outside every ILVerify target (need a netstandard ref
  set). Recorded when ruling 1 shipped.
* `ObjectFactoryV2` (326 lines) and `GraphOracleComparer` (394 lines) — the named mutation follow-on for the
  testing leg; fixture machinery, out of scope for the verifier leg.
* The **17 survivors + 2 uncovered** in the testing leg. The 2 uncovered are named:
  `StructuralComparer.cs:37`/`:38`, both the `"<null>"` operand of a `??` — no test renders a diff where a
  side is null. First item of that leg's kill programme.
* The **29 survivors + 2 uncovered** in the pipeline leg.
* **R3's staleness gap** and the generator's fresh-destination idempotence fuzz.
* **README-in-package** size-gate item; **DWARF092** silently discarding `[MapShare]`/`[Reinterpret]`.

## E. CI, which is not ours but is red

`Issues/round30/CI-NIGHTLY-REVIEW.md` predicted the mutation leg's exposure as HIGH and structural; the
2026-09-09 nightly confirmed it empirically for the first time. **Invariant R2 pins `break` to the floor of
the measurement, so every leg sits less than one point above failing, and the floor is measured on one
machine and enforced on another.** The review names three options and the choice is the owner's:

1. measure floors in the CI environment and pin them there;
2. keep R2 and allow a stated cross-machine tolerance below break;
3. accept the fragility and treat a one-mutant red as a re-measure trigger rather than a regression.

`preview-sdk-canary` is `continue-on-error: true` — an alert, not a gate — and died in 18 s at SDK
acquisition, so the 11.0 preview channel is the suspect. It will paint the nightly red every night while
that holds, which is an instrument crying wolf: worth splitting "channel unavailable" from "preview SDK
broke our build" so only the second is red.

**Neither failure is round 29's**, and neither can be: `master` is at `0c30156` and every round-29 commit is
still local.
