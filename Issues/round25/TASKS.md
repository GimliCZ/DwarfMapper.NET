<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 25 — SIMD expansion of the collection fast paths, ordered by layer

## What this file is

`ROUND25-SIMD-RFC.md` is research and it is finished: v1 proposed seven entries, v2 measured them and
falsified two, v3 hardened the survivors against primary sources. **This file does not re-derive any of
that.** It transcribes v3's landing order into executable tasks, resolves the two places where the RFC
contradicts itself or the machine, and carries the standing rules forward.

Prep lands on `master`. Execution branches — `feat/round25-simd`, per the round-23 precedent.

## Two corrections the RFC needs before anyone executes it

**1. The v2 measurements were taken on hardware this repository does not have, and the harness is gone.**
The RFC says the benchmark source is "preserved at `/home/claude/bench/Program.cs` for re-runs". That is a
cloud-container path; it does not exist here, and no SIMD or blit benchmark source exists in `benchmarks/`.
The v2 environment was an AVX2+AVX-512 container with `Vector<long>.Count=4`, on a shared core. **Every
ratio in the v2 table is therefore DIRECTION-ONLY until re-measured locally** — the house rule the
Dict-vs-Mapperly figure taught us (2.13x on Windows against ~1.14x on Linux, for the same code) applies with
full force to numbers taken on a machine nobody here can re-run. In particular the **1.5x gate constant must
be pinned from a local measurement, in the commit that introduces the gate**, never inherited from the
container.

**2. v1 and v3 disagree about when the differential harness lands.** v1 says R25-05 "lands FIRST, before any
entry above"; v3's final order puts it fifth. Both are right about different halves, and v3 is what split
them: the **semantic** differential dissolved into per-entry regression tests (scalar-oracle round-trips,
byte-equality rows against the element loop), while **R25-05-final is only the perf gate**. So the rule for
this round is:

> The scalar oracle test ships in the SAME COMMIT as the blit it guards — no exceptions, it is the
> correctness proof. The perf gate trails, because a gate calibrated before there is anything to measure is
> a constant someone invented.

---

## Layer 0 — instruments, before any emission change

The house rule, learned six times over: a product fix graded by a broken instrument produces a green result
that means nothing. Both tasks here are instruments, not features.

### T0-A — rebuild the kernel-isolation harness in-repo, and re-measure locally

Rebuild what the container had: each candidate measured against its **scalar twin in the same process**, so
shared noise cancels; preallocated destinations, so allocation cost is identical between the arms and the
kernel is what is actually being timed. Sizes must straddle the two regimes v2 identified — in-cache
(n≈16 and n≈1,024) and bandwidth-bound (n≈65,536) — because the ratios differ by an order of magnitude
between them, and a single-size measurement picks a winner by accident.

Payloads come from the fuzzer and fixtures, not hand-built uniform literals (standing rule: uniform data
misrepresents branch prediction and cache behaviour alike).

Output: a dated file in `benchmarks/results/`, carrying local ratios for R25-02, R25-03 and R25-06's three
classes. **Exit criterion: a class that does not clear the bar locally does not ship, regardless of what the
container measured.**

### T0-B — R25-07, the layout-equivalence gate, minting **DWARF100**

`DWARF099` is the highest id in the generator today, so this is `DWARF100`.

This is Layer 0 rather than a feature because **the gate is the safety property every later task leans on**.
A wrong blit is silent memory corruption — the one failure class this project treats as forbidden — and
widening the allowlist before the refusal logic exists would make each widening carry its own ad-hoc proof.

Gate: unmanaged ∧ `Sequential`/`Explicit` layout ∧ field-by-field offset-and-type equality ∧ equal pack.
Anything else refuses and takes the scalar path.

Hard refusals to encode together with the reason, because they LOOK blittable: `DateTime` and
`DateTimeOffset` are `[StructLayout(Auto)]`, so their layout is not guaranteed and they must never blit.

The diagnostic is **informational** — the mapping still works. The id exists so a consumer who expected the
fast path learns why they did not get it.

Five-file sync, since this mints an id and `Scan9` fails the build without the CHANGELOG entry:
`AnalyzerReleases.Unshipped.md` · `docs/diagnostics.md` (fence-exempt, non-compiling illustration) · a
`NegativeCases` row pinning id AND remedy wording · `CHANGELOG.md` · `docs/generated/diagnostics-index.md`.

---

## Layer 1 — the two confirmed wins, largest first

Both were confirmed by v2 and hardened by v3, and both ride `Buffer.Memmove` behind a generation-time proof.
Neither may land before T0-B, whose gate they call.

### T1 — R25-03, enum arrays as underlying-primitive blit

The largest measured win (container: up to 76x in-cache, 6.4x at bandwidth) and the simplest proof, which is
why it goes first. Covers `Status[] → Status[]`, `Status[] → StatusDto[]` where underlying types match and
value sets are identical, and `Status[] ↔ int[]`. These are reinterpretations, not conversions.

The value-set analysis **already exists** — the enum converter computes it for its diagnostics — so only the
emission is scalar. Differing underlying types, or mismatched value sets, stay in the loop: those are
genuine conversions.

### T2 — R25-02, struct blit, with the `List<T>` guard

Identical sequential blittable layout on both sides, name-independent — offsets and types, not member names.
For `List<T>` targets, `CollectionsMarshal.SetCount` plus a span copy.

Two constraints v3 extracted that are easy to lose:

* the small-`n` guard is real — below the threshold, `Add` wins about 2x — and the threshold is expressed as
  a **`Vector<T>.Count` multiple**, following dotnet/runtime's own idiom, not as a bare `32`;
* an emitted-shape pin must assert **no throwing statement between `SetCount` and the copy**. `SetCount`
  exposes uninitialized memory until the copy completes, and a throw inside that window is observable.
  Making it structurally impossible beats documenting it.

---

## Layer 2 — widening, once the gate exists and the wins are proven

### T3 — R25-06, expand the allowlist: `Guid`, same-nullability `Nullable<T>`, same-type `decimal`

All three ride the identical template. The load-bearing test is the **cross-nullability refusal**:
`T?[] → T[]` must take the loud path and never blit. That single test is what fails if the gate is subtly
wrong, so it matters more than the three positive cases combined.

---

## Layer 3 — the standing perf gate

### T4 — R25-05-final, ratio-based and same-process, never absolute-time

The research is unambiguous that absolute-time gating on shared runners does not work: a 2% absolute gate
yields roughly 45% false positives, and holding a 1% false-positive rate needs about a 7% threshold. A
same-process SIMD-vs-scalar ratio cancels shared contention, because both arms feel it equally.

Gate at the large-`n` regime, using the constant pinned by **T0-A's local measurement**. The small-`n` guard
region is explicitly **excluded from hard gating** and asserted only directionally — `Add` winning below the
threshold IS the guard's own regression test.

Sabotage rule (round 13, non-negotiable): de-vectorize one kernel in a scratch branch, show the gate goes
red, revert. An unproven gate is a gate that passes because it measures nothing.

---

## Layer 4 — semantics, not performance

### T5 — R25-04, skip-if-identical, as an opt-in whose cost is documented

v2 falsified this **as a perf feature**, and the RFC retracts the implied benefit: `SequenceEqual` reads two
streams where `CopyTo` reads one and writes one, so even the identical case costs about 2x a plain copy. It
never wins.

It survives on its real justification — EF change-tracker semantics, not dirtying unchanged entities — and
therefore ships opt-in, documented so that the **cost is stated in the same breath as the benefit**. A
feature documented as a speed-up when it is measurably a slow-down is precisely the kind of claim this
repository exists to prevent.

---

## Parked

### T6 — R25-01, checked narrowing: parked permanently, with a pin that keeps it parked

Not "not yet" — **parked on evidence**. `TensorPrimitives.ConvertChecked` is scalar-only by design (its
fallback operator declares `Vectorizable => false`, and the Vector128/256/512 paths throw
`NotSupportedException`), it throws without identifying the faulting element, and it leaves partial writes
behind. Both contestants being scalar, the container's 1.49x is anomalous and **must not be cited**. RyuJIT
compiles `checked((int)x)` to a flags test plus a not-taken `jo` — near-free, which is why nothing beats it.

Ship an **emitted-source pin** asserting the checked-narrow emission contains no `TensorPrimitives` call and
no vector kernel. The pin is the point: it prevents silent adoption without a parity-and-perf proof.

Record the one credible future route in the source comment, rather than in a document nobody will find: a
`ConvertTruncating`-style vectorized narrow (vectorized since dotnet/runtime PR #116895) plus a vectorized
any-lane-out-of-range mask, falling to the scalar loop only on a nonzero mask.

---

## Standing rules, carried forward

* **`dotnet build DwarfMapper.NET.sln` — the whole solution, samples included.** `dotnet test` does not
  cover `samples/`, and that is exactly where an over-eager emission change surfaces.
* **House invariants no SIMD path may alter:** checked narrowing is exact-at-bound and throws one past;
  output is byte-deterministic; no SIMD path changes a diagnostic outcome; AOT- and trim-safe, intrinsics
  only.
* **The scalar path stays the permanent semantic oracle.** Every blit is proved against it, never against a
  hand-written expectation.
* **Endianness** deserves one pinned comment-test: the blit is a same-endian reinterpret, not serialization.
  Both supported targets are little-endian, which is a fact with a shelf life.
* **No floor or threshold moves except to a value measured in the same commit.**
* **Explicit pathspec on every commit.** No `git commit -a`, no `git add .`.
* **No push without explicit per-instance approval.**

## Open question for the maintainer

`bool[]` blits are semantically identical to the loop — a non-0/1 byte is preserved either way — but the
**non-normalization** becomes an observable contract the moment a test pins it. Worth deciding deliberately
rather than inheriting: pin it as documented behaviour, or normalize and give up the blit.
