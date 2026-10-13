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

  **Done — `4c9d9a7`.** Baseline 1.48x slower than LINQ at 1,024; after, 1.94x / 2.77x / 2.15x faster than the
  previous emission (array → List, List → List, List → array) and ahead of LINQ in every shape
  (`benchmarks/results/2026-10-12-round33-t02-widen-list.md`). Two defects in the paths it sits beside were found on
  the way and fixed in their own commits — see the next section.

- **T03 Pre-size the Preserve identity map from a collection root.** Round 31 P8 measured a **model** (a
  `Dictionary` with `ReferenceEqualityComparer`, 1,000 nodes): 14.2 → 6.0 µs (2.3x), 102 → 31 KB (−70 %). The emitted
  shape has never been measured. `DwarfRefContext`'s constructor is in `PublicAPI.Shipped.txt`, so sizing the map
  needs an API addition — **the owner signs off on the surface first.**
  1. Benchmark the real Preserve map of a collection root on master.
  2. Build only if that clears the threshold; then the generated entry sizes the map from the root's count.

## Found and fixed during the round

- **`5fc0a13` — CS8604 in generated code for a nullable source on a fast path.** The block-copy and array-widen helpers
  took a non-nullable source parameter while null-guarding inside; a nullable-annotated member (`int[]?`) therefore
  warned in the consumer's `.g.cs`, unsuppressible there and a build break under `TreatWarningsAsErrors`. Found
  because T02's new helper copied the same signature.
- **Preserve's shared-source identity on the fast paths.** Under `ReferenceHandling = Preserve` one source collection
  reached twice must be one target (EmitArray's B3 guarantee); the struct and enum block copies, the list block copy
  and the array widen are static helpers that cannot register, and were reached under Preserve anyway. They are now
  not taken there, and `DWARF100` is not reported there. Found because T02's list shapes had to be gated off Preserve.
- **`38ce1a5` — the package-size gate was red on the first master nightly.** Round 32 grew the main package ~8.4 KB
  without re-measuring; ceiling 353 -> 362 KB, measured in the gate's container and on Windows.
- **`63a4f9f` — build time: the `Dwarf.Map` call-site scan bound every `.Map(…)` call.** 793 of 4,216 invocations on
  IntegrationTests were sent to a semantic bind to find 17 possible call sites; now 95. Generator time on those
  sources 4.1-4.2 s -> 2.3-2.9 s cold, 0.84-0.98 -> 0.65 s warm.

## Measured, not acted on

- **`2a10fb3` — the array path's null-element cost is the store shape.** Keeping the null test but storing inside it
  is 11 % faster than the emitted loop (and than Mapperly's test-free loop) with the same behaviour; 1.12x is below the
  1.3x bar, so it waits for a quiet-machine confirmation at 100 and 10,000 elements and the owner's call.
- **Generator scaling** — linear, ~0.5 ms per mapped pair warm; this repeats round 23 S3
  (`CompilerTests/GeneratorCompileCostTests`) and adds nothing to it. Cold runs are two-thirds JIT of the generator
  itself (IntegrationTests: ~2.3-2.9 s cold against 0.65 s warm) — a candidate lead (ready-to-run), unresearched.
- **T02b candidate** — a widen span map (`void Map(ReadOnlySpan<int>, Span<long>)`) emits a scalar loop, never
  `Vector.Widen`; unmeasured.

## Waiting on data or on the owner

- **The first master nightly on round-32 code** (2026-10-13, run 38384275767) — READ. Green: preview-sdk-canary
  (the round-32 global.json fallback proven in its own job), ubuntu-26-canary, deep-test, cross-platform, perf-tests,
  reproducible-build, AOT, four mutation legs. Red: package-size (fixed, `38ce1a5`) and the generator and pipeline
  mutation legs, cancelled at their timeouts (126 and 57 min).
- **Generator mutation leg timeouts.** Round 22's K3 (`Issues/round22/RESEARCH-STATIC-MUTANT-ATTRIBUTION.md`) found
  static mutants ~99 % of the leg's work, each surviving one running the whole suite, and every direct lever closed
  by standing rulings; its one ruling-clean option is a per-file split. What no ruling closes: a faster suite cuts the
  leg in proportion. Being profiled. A timeout raise is a workflow edit the owner pushes.
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
5. `[Reinterpret]` under Preserve. It forces the block copy as an explicit instruction, before the gates the Preserve
   fix added, so it still takes the static copy - and a source reached twice becomes two targets. Options: keep
   honouring it (today), honour it and report the identity it gives up (`180510e`'s shape for `[MapConstructor]`), or
   refuse it under Preserve.
