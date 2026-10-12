# Round 33 — tasks

Branched from master `80b5a68` (round 32, merged as PR #7) on 2026-10-12. Round 31's evidence rule stands: a
performance change becomes a task at **≥ 1.3x or ≥ 20 % allocation, consistently, measured on the emitted shape**,
not on a model of it. Every fix ships its RED→GREEN regression test in the same commit, and every new branch is
reachable and tested or removed (2026-09-10 full-coverage ruling).

## On its own branch, ahead of this round

- **T01 CI: Linux jobs pinned to `ubuntu-24.04`, an `ubuntu-26.04` canary, and Node 24 actions** —
  `ci/ubuntu-pin-node24`, cut from master because GitHub starts moving `ubuntu-latest` to 26.04 on **2026-10-19**
  (finished by 2026-11-19, actions/runner-images#14748), and a round branch cannot carry a seven-day fix. Workflow
  edits, so the owner pushes and opens the PR; this branch merges master once it lands.
  - `c142578` — all 17 Linux labels pinned to `ubuntu-24.04`; the non-blocking `ubuntu-26-canary` runs build-test's
    suite and the linux AOT gate on 26.04 on every event; `CiRunnerImagePinScanTests` holds the rule (RED 3 of 21 on
    master's workflows, GREEN 541 of 541 SelfValidation).
  - `17c5c28` — the eight Node 20 actions moved to their first Node 24 major; `codecov/test-results-action` (never
    on Node 24, deprecated by its README) replaced by `codecov-action` with `report_type: test_results`.
  - Proven only by the PR's own run: the canary's verdict, and no Node 20 deprecation annotation on any job.

## Tasks

- **T02 Widening into a `List<T>` takes the SIMD path an array target already has.** Round 31 T15 measured the
  generated `int[] → List<long>` fill **1.6x slower** than `Select(x => (long)x).ToList()` at 1,024 elements (739 vs
  465 ns, same allocation; ShortRun on Windows, so read the ratio, not the times —
  `benchmarks/results/2026-09-27-round31-t19-matrix.md`). The cause, read from the emitted helper: the vectorised
  `Vector.Widen` path is gated to array → array (`MapperExtractor.Conversions.Arms.cs`, the SIMD widening fast-path),
  so a `List` target falls through to `SetCount` + `AsSpan` + `foreach … __d[__i++] = __item`, an index the JIT
  cannot prove in range.
  1. Baseline on master first: `Round31T15Benchmarks` Widen arms, same payload, before any change.
  2. Extend the widen path to `List` targets (and `List` sources where the span is available): `SetCount`, `AsSpan`,
     the same `Vector.Widen` loop.
  3. Re-measure; an emission change, so the whole solution builds (samples included), `GeneratedCodeIsWarningFreeTests`
     passes, and the snapshots move only where the shape moved.

- **T03 Pre-size the Preserve identity map from a collection root.** Round 31 P8 measured a **model** (a
  `Dictionary` with `ReferenceEqualityComparer`, 1,000 nodes): 14.2 → 6.0 µs (2.3x), 102 → 31 KB (−70 %). The emitted
  shape has never been measured. `DwarfRefContext`'s constructor is in `PublicAPI.Shipped.txt`, so sizing the map
  needs an API addition — **the owner signs off on the surface first.**
  1. Benchmark the real Preserve map of a collection root on master.
  2. Build only if that clears the threshold; then the generated entry sizes the map from the root's count.

## Waiting on data or on the owner

- **The first master nightly on round-32 code** (03:17 UTC, 2026-10-13). PR #7 had no `full-ci` label, so mutation,
  `preview-sdk-canary`, deep-test, cross-platform, package-size, perf-tests and reproducible-build have never run on
  this code. Read it before changing anything below.
- **Generator mutation leg instability** (timeouts, runner starvation) — decided from that nightly. A workflow edit,
  so it rides the owner's next CI push.
- **FusedChat A/B stats** — the round-31 scripts in `Issues/round31/fusedchat-stats/`; the owner's tree and machine
  time, so the owner schedules it.
- **DWARF066 on a positional record's inherited member** — a known false positive, pinned on purpose by
  `WhenGuardInitializerTests`; reading base-constructor arguments is a candidate if a slot opens.
- **SECURITY.md "Supported versions"** — one line, when the version actually moves.
- **OSS Scanner** — repository side audited 2026-10-12 against anthropics/oss-scanner `b95d419`: every documented rule
  met, its validator passes. Its `tools/check` has not run yet (about 75 minutes of the machine). Enrolment waits on
  two owner decisions below.

## Owner decisions

1. T03's API surface: a `DwarfRefContext` constructor overload, or a capacity member.
2. `LICENSE`: the verbatim GPL-2.0 text alone, with the project notice moved to README or a `NOTICE` file — GitHub
   reads the file as "Other" today, which the scanner's triage flags.
3. A public security contact for SECURITY.md and the scanner's `primary_contact`.
4. T01's shape: pin plus canary, rather than letting `ubuntu-latest` float through the move.
