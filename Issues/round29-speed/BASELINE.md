# Test-velocity baseline — measured 2026-09-05 on `feat/round29-hardware-mode` @ 034238e

Every number here was measured on this machine today, in one run, by
`scripts/housekeeping.ps1 -Deep -Coverage -ILVerify -BenchSmoke` plus the two whole-solution builds ahead of it.
This is the "before" the speed round is measured against. Nothing is estimated.

## The deep gate: 1,502 s (25 min 02 s), PASSED

| Leg | Time | Tests | Per test |
|---|---|---|---|
| `dotnet build` Debug | 33 s | — | — |
| `dotnet build` Release | 27 s | — | — |
| **1/4 full self-test suite** | | | |
| &nbsp;&nbsp;Generator.Tests | 2 m 41 s | 15,458 | **10 ms** |
| &nbsp;&nbsp;CompilerTests | 1 m 17 s | 52 | **1,480 ms** |
| &nbsp;&nbsp;IntegrationTests | 25 s | 920 | 27 ms |
| &nbsp;&nbsp;NegativeCases | 7 s | 132 | 53 ms |
| &nbsp;&nbsp;Testing.Tests / Differential / Corpus / Consumer ×3 | < 2 s total | 217 | — |
| **2/4 exhaustion** (`DWARF_FUZZ_FULL=1`) | **12 m 55 s** | 766 | **1,012 ms** |
| 3/4 AOT publish + execute | included below | — | — |
| ILVerify | included below | — | — |
| 4/4 benchmark smoke + allocation pins | included below | 57 benchmarks | — |
| Coverage instrumentation + report | included below | — | — |

Legs 3–4 plus coverage account for the remaining ≈ 6 min.

## Separately: the generator mutation leg

51 min at ~391 scoreable mutants (measured 2026-09-02, unchanged config). Not run in this gate.

## What the distribution says

1. **Generator.Tests is already fast.** 15,458 tests at 10 ms each is a well-tuned Roslyn harness — the
   metadata-reference caching from round 18's fix is doing its job. There is little left to win here, and
   attacking it would be the obvious-looking mistake.
2. **The exhaustion tier is the largest single leg of the gate** — 12 m 55 s, 52 % of the whole deep run, for
   766 tests at ~1 s each. Two orders of magnitude slower per test than the fast tier. That ratio is the
   question to answer first: a fuzz case should not cost 100× a normal generator case unless it is doing
   something structurally different (fresh compilation per case, uncached references, full output re-compile,
   or a payload that grows with the seed).
3. **CompilerTests costs 1.48 s per test** — 52 tests, each compiling a corpus. Expected to be expensive, but
   worth confirming it is not rebuilding references per case.
4. **The mutation leg is still the worst single number** (51 min), and its cause is already known and
   documented rather than hypothetical: `stryker-config.json` declares four `mutate` globs but no
   `test-projects` and no `coverage-analysis`, so discovery is wide — including the 920 runtime tests that
   cannot kill a compile-time layout-proof mutant. Round 20 measured the consequence directly: 91 tests do
   all the killing, 5,592 run, a 61× waste factor.

## Ranked by time recoverable, before research

| Target | Now | Mechanism believed available | Risk to what the gate proves |
|---|---|---|---|
| Mutation leg | 51 min | scope `test-projects`, pin `coverage-analysis` | none if the score's meaning is preserved — must be verified, not assumed |
| Exhaustion tier | 12 m 55 s | unknown until the per-case cost is profiled | unknown |
| CompilerTests | 1 m 17 s | possible reference reuse | none |
| Per-commit verification | 5–8 min × every commit | tier by risk instead of running all three projects uniformly | low; the tiering rule must name what each tier does not cover |
| Benchmark smoke | ~15 min when run | only run when emission changed; in-process toolchain (needs proof that exact allocation pins survive) | HIGH for the toolchain change — the pins are exact bytes |

## Per-test profiling, measured 2026-09-06 (TRX durations, `--no-build` so build time is not charged)

Two findings overturn the leg-level reading above.

**1. The exhaustion tier is ONE test, not 766 slow ones.**
`FeatureCombinationFuzzTests.Full_power_set_compiles_on_every_emit_path` takes **600.4 s — 97.6 %** of the leg.
The other 765 cases total 14.8 s (p50 = 14 ms). It is a single `[Fact]` running `Parallel.For` over the whole
2^16 feature power set × 3 construction emit paths + update-into ≈ **262,144 Roslyn compilations**, so it is
already parallel; the cost is per-compilation, roughly 27 ms of CPU each. Nothing about it is accidental — it is
an exhaustive proof — but it is 40 % of the entire deep gate in one test case.

**2. The gate's per-project times were mostly BUILD, not test execution.**

| Leg | In the gate | With the build shared (`--no-build`) |
|---|---|---|
| CompilerTests | 1 m 17 s | **7 s** |
| IntegrationTests | 25 s | **4 s** |
| Generator.Tests (deep) | 2 m 41 s | 2 m 00 s |

Stage 1 of `housekeeping.ps1` runs `dotnet test` on the solution WITHOUT `--no-build`, so a full analyzers-on
Release build is billed inside the suite legs. Building once up front and passing `--no-build` to the test
invocations recovers most of that ≈ 1 m 30 s, changes nothing about what runs, and is the cheapest win available.

Slowest individual tests outside the fuzz case: `SurfaceParityTests` contributes four tests at 25.2 s, 13.6 s,
12.8 s and 9.9 s (866 tests, 102 s total), and `GoldenCorpusTests.Generated_output_matches_the_golden_manifest`
at 9.3 s. Generator.Tests overall is healthy: 15,458 tests, p50 = 37 ms, p90 = 109 ms.

## Intervention 1 — shared baseline compilation (APPLIED 2026-09-06)

`GeneratorTestHarness.BuildCompilation` called `CSharpCompilation.Create` per test. The `MetadataReference`
objects were already cached (round 18), but every new compilation still rebuilt a `ReferenceManager` and
re-bound each referenced assembly's symbols. It now derives from a cached empty baseline keyed on the exact
`(assemblyName, nullableContext, allowUnsafe)` triple, so that binding is paid once per triple.

| Leg | Before | After | Δ |
|---|---|---|---|
| Exhaustion (`DWARF_FUZZ_FULL=1`) | 620 s | **580 s** | −6.5 % |
| Generator.Tests (deep, 15,458) | 120 s | **111 s** | −7.5 % |
| CompilerTests | 7 s | 7 s | — |
| IntegrationTests | 4 s | 4 s | — |

Correctness unchanged: 7,386 fast-tier + 15,458 deep + 52 compiler + 880 integration, 0 failures.
Conclusion: reference binding was NOT the dominant per-compilation cost. What remains is the generator run
itself plus the full semantic bind of the generated output — which is the work the test exists to do.

## Coverage floors measured in the same run (all PASS)

DwarfMapper 91.5 (floor 91.2) · Generator 95.6 (floor 95.5) · DocTooling 96.3 (floor 96.0) ·
CodeFixes 96.2 (floor 96.2) · Testing 96.4 (floor 96.4). Allocation pins: 22 scenarios exact-matched,
57/57 benchmarks executed. Blit ratios: array 2.40×, list 2.23× (floor 1.50×).
