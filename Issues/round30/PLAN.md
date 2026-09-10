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

## B. Map fusion — an ANALYZER, not an emitter (reframed 2026-09-10)

**Evidence:** `Issues/round30/FINDING-fusion-has-no-emission-site.md`.

This item read "build the emitter, now that the measurement is closed on the right population" until the
emitter was actually started and the question nobody had asked got asked: **does the generator ever emit
`MapBC(MapAB(x))`?** It does not. `MemberMap.ConverterMethod` is a single `string?` — a member carries ONE
converter, so the model cannot represent "convert, then convert again" — and 624 generated files across the
corpus, gallery and clean-corpus consumer contain zero `A -> B -> C`. The chain the spike measured is
written by the probe, in a consumer's own method body, which a source generator does not rewrite.

So the work is a **DWARF diagnostic + code fix**:

1. **The analyzer** — a consumer chains two declared maps, especially inside a loop. Report that the
   intermediate is allocated per element and a direct `A -> C` map would remove it.
2. **The code fix** — declare `partial C MapAC(A a);` and rewrite the call site.
   `ConvertToRecordStructCodeFixProvider` is the working precedent for a solution-wide rewrite.
3. **The suppressions are already written and measured** — every REFUSE row in
   `SPEC-fusion-refusal-list.md` becomes a reason NOT to suggest it, and the observability criterion is the
   sharpest: if `C` ignores a member of `B` whose production runs user code, the suggestion would delete
   that work.
4. **A non-firing rule:** outside a loop the JIT already stack-allocates the intermediate (both arms
   allocate 40 B and tie on time), so a suggestion there is pure noise.
5. **The oracle exists:** `MapFusionEquivalenceTests` — the fix must not change behaviour.

**Open design questions,** and they are the whole remaining risk: which diagnostic id; and whether an
analyzer can see enough of a loop to be confident the chain is per-element rather than incidental.

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
* **Axis 3b — the refusal controls. CORRECTED 2026-09-10: the gap is far smaller than this plan claimed.**
  The original text said *"nothing anywhere proves the blit REFUSES a reordered, reference-carrying or
  differently-padded struct"*. That was written from the BENCHMARK suite, where `[StructLayout(Auto)]` is
  indeed the only negative control — and then generalised to the whole repository without looking.
  `tests/DwarfMapper.Generator.Tests/BlitSoundnessTests.cs` has **27 tests**, each asserting runtime values
  AND `AssertNoBlitHelper`. Field ORDER is covered, and covered by a test written from a real production
  bug: a partial struct split across two files where the proof re-sorted by ordinal path, so `P{X=1,Y=2}`
  came out as `Q{X=2,Y=1}`. `StructLayout(Size)`, `[InlineArray]` length, fixed-buffer length,
  nullable-on-one-side, user converters, conversion operators, pair-scoped directives, hooks and
  `[MapConstructor]` are all covered too.

  **The genuine holes, verified by scanning that file: `Pack` and `LayoutKind.Explicit`/`FieldOffset` —
  zero occurrences.** Both are the dangerous shape rather than the loud one: names, types and ORDER all
  match, so every check that reads the symbol model sees an identical pair, and only the byte offsets
  differ. A blit there does not shorten the copy, it reads each member from the wrong offset. Two tests,
  not an axis.

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
