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
- **`2f3b290` — test speed: the surface matrix was Generator.Tests' critical path.** Six ratchet facts re-classified
  all 966 cells serially, and every cell ran the generator twice; now parallel within each test, one run per cell,
  966/966 verdicts byte-identical. Generator.Tests 89/84 s -> 69/76 s, peak memory within noise on Windows at 12
  threads (on Linux at 4 CPUs the round's default-mode peak is ~1 GB above master's: "Generator.Tests' own peak
  memory" below). Deliberately no cross-test cache: Stryker switches mutants per test case inside one test host.
- **The fast-path axis audit** (a read-only agent asked, of every specialised path, which axes its gate does not
  read). Two of its cells were real, and the probe written to confirm the second found two more defects beside it:
  - `2a0b270` — under `NullCollections = AsNull` a null source array became `[]` on the array block-copy, enum
    block-copy and array widen paths (run and observed); they now give way where null is kept.
  - `6cdce1b` — `ImmutableList<string?>`-style targets warned CS8619 in generated code (identity `CreateRange` inferred
    the element; the share's `.Empty` dropped the `?`), and the share passed `ImmutableList<string?>` into
    `ImmutableList<string>` by reference, skipping the element-null check the copy makes. Three defects from one
    audit cell, the same "probe to confirm, find more" pattern as every earlier corpus hole.
- **Package-size headroom after those fixes** — re-packed at `6cdce1b` on Windows (SDK 10.0.101): 371,190 B, still
  362 KB, gate passes. Headroom to the first red byte is now **522 B** (was 937 B at `38ce1a5`); the next generator
  change of that size needs the two-floor re-measure, not a guess.
- **`daff726` — deep-test timed a Perf ratio inside the parallel suite.** Round 31 T19 took `Category=Perf` out of
  every ci.yml and release.yml test step but not out of `housekeeping.ps1`'s stage 1, which is what deep-test runs.
  The 2026-10-24 master nightly and PR #9 went red on `InterfaceBucketTests`' 1.5x bound (787.8 -> 1383.4 and
  857.2 -> 1394.0 ns). Stage 1 filters the category now, stage 1c runs it alone (not under `-Nightly`, where the
  perf-tests job owns it), and `PerfCategoryIsolationScanTests` holds every `dotnet test` the workflows reach,
  scripts included — the sentence CiGateScanTests' perf-tests row only asserted.
- **`eae75be` — the mutation legs' deaths, the dashboard 413 and CI's scores had one cause: no ilverify in the job.**
  Every leg's initial test run failed `EmittedIlIsVerifiableTests`' two facts ("2 tests are failing" in every log
  read, 2026-10-04 to PR #9; a local reproduction's trace named them). Read in Stryker 4.16's binary, an initial
  failure moves every static mutant from "every test" to an explicit list of the ~7,040 others (`CoverageAnalyser`),
  which `VsTestRunner` sends as `TestCase` objects on every run:
  - A local A/B shaped like CI (`DOTNET_PROCESSOR_COUNT=4`, Debug, generator leg, one worktree, only ilverify
    differing): Stryker held **6.8-8.2 GB without the tool, ~1.1 GB with it**, at the same phase (tree peaks 9.4 vs
    4.8 GB). On a 16 GB runner that is the likeliest cause of 21 of the 26 nightly generator/pipeline legs on
    `6abd8c1` ending in "The runner has received a shutdown signal" (3 left no log, 1 timed out, 1 completed). The
    Stryker step now writes memory, disk and top processes into its live log every 30 s, to confirm it there.
  - The lists were 108.7 MB of the 119 MB 2026-10-16 pipeline report. The dashboard refused it (413) and accepted
    the testing leg's 87.8 MB that night, so round 32's `Write-DashboardReport` could never fit it (every test was
    "referenced"), and its "~10 MB" premise came from local reports, which carry no such lists. Comments corrected.
  - Stryker drops an initially failing test from the tests that may kill, so CI scored without two facts the local
    floors were measured with. They cover 27-37 mutants a leg (local reports), so installing the tool costs minutes.
  Both launch sites now also pass `--break-on-initial-test-failure` (verified: the leg stops after 3.5 min and prints
  both test names and messages), so the next initial failure is a named red in minutes, not a death in hours.
- **`aad3cd8` — the Perf ratio inside Stryker's initial run.** On PR #9's re-run (head `1ca6232`) the flag stopped the
  pipeline leg 5 min in, naming its one failing test: `InterfaceBucketTests`' ratio, 255.1 -> 394.8 ns. That is T19's
  noisy neighbour inside the mutation legs. Stryker 4.16 reads a test filter only from its config file
  (`test-case-filter`; the command line has none), so all six configs carry `Category!=Perf`. A local run accepted the
  key, and its coverage pass ran the class's two guard facts but not the ratio. **Confirmed on CI** (run 40313612562,
  head `d11bb78`): the ratio covered 22 of the runtime leg's 203 non-static mutants on run 40299439526 (`1ca6232`) and
  0 on this one, and the runtime score was 97.48 % both times. It costs no real kill, but the commit's "no score cost" is
  too broad: the 2026-10-16 pipeline report credited the ratio as the SOLE killer of 6 static mutants in
  `MapperExtractor.Members.Phases.cs` although it covers none of the leg's 354 non-static mutants (IntegrationTests
  run the generator at build time, not test time), and 4 more in that file timed out in the same stretch - false
  credit from a loaded runner. The pipeline's 98.41 % -> 95.22 % is that credit gone, 1.2 pp above its break of 94.
  (CI reports name only Generator.Tests' tests; a vstest id is the same on every machine, so a local report named the
  unnamed killer `1befc324-...`.)

  Run 40313612562 was green on all 26 checks, with no initial failure in any leg and Stryker itself at 0.5-1.6 GB:

  | Leg | Score | Break | Stryker run |
  |---|---|---|---|
  | generator | 97.11 % | 96 | 1:40 |
  | pipeline | 95.22 % | 94 | 1:34 |
  | runtime | 97.48 % | 97 | 0:32 |
  | testing | 100.00 % | 100 | 0:21 |
  | codefixes | 87.64 % | 87 | 0:11 |
  | doctooling | 96.55 % | 96 | 0:10 |

  What it did not show is memory bounded well under 16 GB: see "The generator and pipeline legs die at random" and
  "Generator.Tests' own peak memory".

## The audit's cells not yet tested (in its rank order; "read" = gate read, not run)

- **`default(ImmutableArray<T>)` source** — the blit/widen and the share answer empty; the general path enumerates
  `src.GetValueOrDefault()` and should throw. Which one runs depends on Preserve / AsNull / whether the element blits.
  Needs a decision of what `default` SHOULD map to before a test can pin it.
- **FlattenGraph leaf under Preserve** — leaves resolve with Preserve off, so a `List<int>` -> `List<long>` or blittable
  leaf is flattened through a static helper with no tracking, while an Obj/Coll/Dict leaf reports DWARF075 (read).
- **`Nullable<struct>` wrappers** recurse with `WithoutReferenceTracking()` - a struct carrying references loses
  Preserve identity through `PsHolder?` (inferred; `PreserveThroughStructRuntimeTests` covers non-nullable holders).
- **Array blit under SetNull vs a method-level config on the element pair's partial** (inferred).

## Measured, not acted on

- **Generator.Tests' own peak memory is what is left of the mutation legs' memory problem.** Measured 2026-10-26 in
  the `sdk:10.0.101` image (Docker, `--cpuset-cpus 0-3`, no memory limit: a cgroup limit would cap the GC heap, and
  the runner has none). Each trial was a fresh container running Generator.Tests in Debug (Stryker's configuration)
  and reading every test host's VmHWM each second; master `6abd8c1` and this branch `d11bb78` alternated, three
  trials per cell. "Stryker's settings" adds the line Stryker 4.16 writes into its run settings for xUnit,
  `<DisableParallelization>true</DisableParallelization>` (`VsTestContextInformation.GenerateRunSettings`, beside
  MaxCpuCount 1); xUnit then reports "parallel test collections = off" (checked with its diagnostic messages).

  | Mode | master `6abd8c1` | round 33 `d11bb78` | Verdict |
  |---|---|---|---|
  | default (xUnit parallel) | 4.81, 4.85, 5.33 GB; 101-104 s | 5.84, 5.91, 6.98 GB; 81-102 s | raised ~1 GB: no overlap |
  | Stryker's settings | 5.38, 6.73, 7.19 GB; 114-121 s | 6.40, 7.35, 8.07 GB; 108-119 s | not shown: the arms overlap |

  The rule was set before the runs: "raised" only if the arms do not overlap, or their medians differ by more than
  the within-arm spread (in Stryker's settings the medians are 6.73 and 7.35 GB, the spreads 1.81 and 1.67 GB).
  - CI matches the second row. On run 40313612562 the largest Generator.Tests host per leg was 5.4-9.8 GB (generator
    9.8, pipeline 8.3, runtime 8.0, doctooling 6.5, testing 6.3, codefixes 5.4), and the 9.8 GB came in the generator
    leg's INITIAL run, 1:42 into the host, with no mutant active: the suite alone does it.
  - The arithmetic: the runner holds ~1.7 GB before Stryker starts and Stryker 0.5-1.6 GB, which leaves ~12 GB for
    Stryker's two concurrent test hosts, each able to reach 5.4-9.8 GB. Legs most likely survive on timing: RSS
    climbed until the host ended in every trial, so a host peaks as it finishes, and two finishes rarely coincide.
    Master carries the same risk; before `eae75be`, Stryker's own 6.8-8.2 GB came on top of it.
  - The default-mode rise is most likely `2f3b290`: `AllCells()`' PLINQ is the only parallel construct that runs
    (`FeatureCombinationFuzzTests`' `Parallel.For` waits for `DWARF_FUZZ_FULL=1`). Untested; the arm that settles it
    is `d11bb78` without `.AsParallel().AsOrdered()`. That mode has headroom: build-test and deep-test are green.
  - Unexplained, a lead: on master, Stryker's settings peak higher than the default (5.4-7.2 vs 4.8-5.3 GB).
  - Limits: three trials per cell; the two ILVerify facts fail within a second in the container (no tool); Windows
    figures (2.56 GB on 2026-10-13, `2f3b290`'s 2.1-2.2 GB at 12 threads in Release) do not predict Linux.
  - Not done, offered: find what grows (a gcdump at the peak); a GC heap limit for the mutation job's test hosts
    (`DOTNET_GCHeapHardLimitPercent`, whose value is read as hexadecimal: 40 % is `28`); not Stryker concurrency 1,
    which would roughly double the generator leg's 1:40 toward its 200-minute timeout.
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
  mutation legs, both **cancelled well short of their timeouts** (126 of 200 and 57 of 350 min).
- **The generator and pipeline legs die at random, not at their timeouts.** ROOT-CAUSED 2026-10-26, see `eae75be`
  under "Found and fixed"; what follows is the record that led there. PR #9's run 40313612562 showed no "tests are
  failing" line and both legs completing (generator 1:40, pipeline 1:34), but NOT a trace bounded well under 16 GB:
  at 11:59 UTC the generator leg had 0.4 GB available, with two Generator.Tests hosts holding 9.5 and 3.7 GB, and six
  of its 30 s samples were under 2 GB (the run before it: low 1.6 GB, largest host 7.8 GB). Stryker is no longer the
  large process; the test host is ("Generator.Tests' own peak memory" under "Measured, not acted on"). The 413's end
  shows only on the next nightly. The THIRD test failing the 2026-10-25 generator leg's initial run (its pipeline leg
  that night had the usual two) was most likely the Perf ratio, which failed PR #9's re-run the same way
  (`aad3cd8`). If it was something else, the flag will now stop the leg and name it.
  Read from the public jobs and annotations API over the fourteen nightlies 2026-10-02..13:
  - On `24a31de`, the same commit and config for twelve nights, the generator leg ran 27.8, 54, 56, 66, 126, 151, 177,
    195, 198, 200 and 200 minutes. Only 10-04 and 10-09 carry GitHub's "exceeded the maximum execution time"
    annotation; 10-08 says "the hosted runner lost communication with the server"; every other cancellation says only
    "The operation was canceled." The pipeline leg shows the same spread: 30 to 299 minutes, cancelled at 57, 69, 87
    and 107. A 7x spread on identical input is the runner, not the suite, so **raising the timeout would not fix it.**
  - No `concurrency`/`cancel-in-progress` in `ci.yml` explains it, and the two legs died at different times on 10-13
    (11:31 and 12:40 UTC), so it is not a run-level cancel. The job logs are 403 without auth; **the owner reads the
    stryker step's log of check-run 115252055645 (generator, 10-13)** for the last lines before the cancel.
  - Measured locally (2026-10-13, Windows, Release, xUnit parallel): Generator.Tests' testhost peaks at **2.56 GB
    private**, IntegrationTests 98 MB, CompilerTests 197 MB; on Linux Generator.Tests reaches 4.8-8.1 GB
    ("Generator.Tests' own peak memory"). Two of those at Stryker's cores/2 on a 4-core, 16 GB hosted runner,
    beside Stryker's own whole-project mutant compile, is the next suspect if the log names nothing. Round 31 already
    saw this leg family exhaust host memory locally (`ROUND31-CHECKLIST.md`: both 2026-09-27 pipeline runs, 266 and
    263 timeouts of 284).
  - The other red, now gone: the **runtime** leg failed its `break <= low` config check in 30 s on every nightly from
    10-02 to 10-12. Round 32's `3442b4c` fixed it (the config had a UTF-8 BOM), and it passed on 10-13. Earlier nights' generator/pipeline reds that did
    complete died on the dashboard's HTTP 413, which round 32's `Write-DashboardReport` (`98abd91`, `f7f07de`)
    was meant to address. It did not: the 2026-10-16 pipeline leg completed (98.41 %) and was still refused, at
    116 MB after trimming 3 MB (see `eae75be`).
  - Separately, round 22's K3 (`Issues/round22/RESEARCH-STATIC-MUTANT-ATTRIBUTION.md`) still holds: static mutants
    are most of the leg's work, so a faster serial suite shortens it in proportion. That helps, but it does not
    explain a 7x spread.
- **FusedChat A/B stats** — the round-31 scripts in `Issues/round31/fusedchat-stats/`; the owner's tree and machine
  time, so the owner schedules it.
- **DWARF066 on a positional record's inherited member** — a known false positive, pinned on purpose by
  `WhenGuardInitializerTests`; reading base-constructor arguments is a candidate if a slot opens.
- **SECURITY.md "Supported versions"** — one line, when the version actually moves.
- **OSS Scanner** — repository side audited 2026-10-12 against anthropics/oss-scanner `b95d419`: every documented rule
  met, its validator passes. Its `tools/check` has not run yet (about 75 minutes of the machine). Enrolment waits on
  two owner decisions below.

## Owner decisions

- **`[Reinterpret]` under AsNull** — like `[Reinterpret]` under Preserve, it forces its block copy before the gates
  above run, so a null source array still maps to `[]` where AsNull would keep null. Same decision, second axis.
- **`SurfaceProbe.Baselines` under Stryker** — a process-wide memo of GENERATOR OUTPUT, shared across tests by design.
  Stryker.NET 4.16 activates mutants per test case inside one shared test host (read from its `CoverageCollector`), so
  in a batched session a baseline computed under one mutant can be read by a test running under another: a possible
  false kill or masked mutant. Unmeasured; the memo was deliberate, so it is a question, not a fix.

1. T03's API surface: a `DwarfRefContext` constructor overload, or a capacity member.
2. ~~`LICENSE`: the verbatim GPL-2.0 text alone, with the project notice moved to README or a `NOTICE` file.~~
   **Decided 2026-10-26: keep LICENSE as it is** ("the original is good"). The notice carries the explicit
   version-2-only grant; GitHub's "Other" costs a warning row in the scanner triage comment, which quotes the
   file's licence line to the reviewer. Round 23 had left it untouched for the same reason.
3. ~~A public security contact for SECURITY.md and the scanner's `primary_contact`.~~ **Decided 2026-10-26:**
   `dwarfmapper@seznam.cz`, a new project address (`152f286`). GitHub private vulnerability reporting is OFF on
   the repository, so SECURITY.md names only the email until the owner switches it on.
4. T01's shape: pin plus canary, rather than letting `ubuntu-latest` float through the move.
5. `[Reinterpret]` under Preserve. It forces the block copy as an explicit instruction, before the gates the Preserve
   fix added, so it still takes the static copy - and a source reached twice becomes two targets. Options: keep
   honouring it (today), honour it and report the identity it gives up (`180510e`'s shape for `[MapConstructor]`), or
   refuse it under Preserve.
