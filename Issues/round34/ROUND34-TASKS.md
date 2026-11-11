# Round 34 — tasks

Branched from master `4a169a4` (round 33 merged as PR #9 `b58c6e3`, then PR #10) on 2026-10-27. Round 31's evidence
rule stands: a performance change becomes a task at **≥ 1.3x or ≥ 20 % allocation, consistently, measured on the
emitted shape**, not on a model of it. Every fix ships its RED→GREEN regression test in the same commit, and every new
branch is reachable and tested or removed (2026-09-10 full-coverage ruling).

## Tasks

The owner's pick on 2026-10-27, out of five candidates: all of them, as four tasks. Lettered, because round 33's T03
is still open under its own name.

- **A. Nightly reliability.**
  - **A1. Memory headroom on the mutation legs** (see "Next steps — CI"): in round 33's Docker rig and in Stryker's
    serial mode, the mode the legs run in, find what the test host holds at its peak (a gcdump), then try the fix
    the dump points at. Any heap-limit arm reports the suite's time as well as its peak.

    **Done — `3144bab`; the evidence is in `benchmarks/results/2026-10-27-round34-a1-testhost-memory.md`.**
    - **Not the GC heap.** The host's working set reached 6.8 GB while the GC's committed heap never passed 0.41 GB,
      and the gcdump found 214 MB of live objects at 4.2 GB RSS.
    - **Native memory.** The rest was anonymous memory in glibc malloc arenas. It rose in steps while the
      symbol-level unit tests ran: about fifty sites called `MetadataReference.CreateFromFile` on every compile.
      That call copies the whole image into native memory, and the copy is freed only at finalization.
    - **The fix.** `GeneratorTestHarness.ReferenceTo` creates one reference per file for the host.
      `MetadataReferenceReuseScanTests` holds the rest of the project to it: 52 sites before, RED; none after.
    - **A/B in Stryker's serial mode** (three trials per arm, alternating):
      - peak host 5.63 / 6.92 / 8.33 GB before, against 0.83 / 0.85 / 0.82 GB after (8.3x lower at the median);
      - lowest available memory 5.97-8.38 GB before, 13.26-13.48 GB after;
      - suite 1 m 43 s - 2 m 1 s before, 1 m 28 s every time after.
    - **The GC heap limit is moot.** It would not have touched memory outside the GC heap.
    - **The `2f3b290` PLINQ arm** measured default mode, which build-test and deep-test already have headroom for.
      It is not pursued.
    - **Confirmed on CI** — PR #11's full-ci run 40538713221 (`71908b0`), all 23 jobs green, scores unchanged:

      | Leg | Largest test host | Lowest available | Leg time |
      |---|---|---|---|
      | generator | 8.1 → **0.9 GB** | 1.5 → 11.3 GB | 1:33:45 → 1:15:33 |
      | pipeline | 8.7 → **0.9 GB** | 0.6 → 11.3 GB | 1:36:38 → 1:11:26 |
      | runtime | 6.8 → 1.0 GB | 3.2 → 11.7 GB | 0:10:52 → 0:16:09 |
      | doctooling | 6.6 → 0.8 GB | 6.3 → 11.9 GB | 0:08:03 → 0:10:05 |
      | testing | 5.9 → 0.8 GB | 7.5 → 12.1 GB | 0:27:09 → 0:29:01 |
      | codefixes | 4.7 → 0.9 GB | 8.8 → 12.1 GB | 0:10:32 → 0:09:51 |

      Before is the 2026-10-27 nightly (run 40477294097, `b58c6e3`). The two long legs are 19 % and 26 % faster;
      the short legs' times moved by single minutes, both ways, which is runner noise at their length.
  - **A2. The dashboard upload retries once** on a 5xx or a failed connection, after a pause; never on a 4xx, which
    the step already explains as configuration.

    **Done.** The PUT moved out of the workflow into `Send-DashboardReport` (`scripts/gate-checks.ps1`), with its
    transport and its clock injectable. `DashboardUploadTests` drives the real function through a pwsh battery
    (200 and 201 once; 504 then 200, and 000 then 201, once more after 120 s; 413 once and fails; 503 then 502 twice
    and fails; no status once and fails), and holds the mutation job to calling it with no `curl` of its own. Both
    facts were RED before the change (the function missing, the step still sending its own PUT); SelfValidation
    632/632 after. The step's comment that the `?module=` spelling was unverified is corrected: the six badges read
    six separate scores.

    **Then the real transport.** The battery's scripted transport left curl's own failed-connection path untested.
    With PowerShell's native-command preference on, curl's non-zero exit throws before the policy sees the 000 it
    exists to retry: a case running the real curl against a closed local port read `Program "curl.exe" ended with a
    non-zero exit code` (RED). The default transport now turns that preference off for itself (GREEN): pause, a
    second send, then `HTTP 000 after 2 attempt(s)`.
  - **A3. The Perf lane's one test timed the JIT, not the registry** (found by PR #11's first full-ci run).
    `InterfaceBucketTests.Collection_dispatch_cost_does_not_grow_with_unrelated_registrations` failed `perf-tests`
    five times, every time on that test: on the master nightlies of 2026-10-04, 10-09, 10-16 and 10-20, with no
    code change between them, and on `fc0ae37`. After over before was 2.60x, 3.01x, 2.10x, 1.61x and 2.00x,
    against a 1.5x bound.

    **Done — `deb63df` makes it report every run, the next commit fixes it.** Measured on 6 cores / 12 threads:
    - The project alone, ten runs: before 192-218 ns every time; after either 205-227 ns or 279-293 ns, so the
      ratio was 1.00-1.11x or 1.34-1.41x.
    - Interleaved with ten default runs, `DOTNET_TieredCompilation=0`: 39-46 ns, 0.89-1.01x, ten of ten. The
      rounds had been timing unoptimised code (~200 ns a call against ~40 ns), and the JIT tier each phase caught
      decided the ratio. The registry is flat.
    - The fix: before each phase the test calls the path until the JIT has compiled nothing for 500 ms
      (`JitInfo.GetCompiledMethodCount`), five times the runtime's call-counting delay, and fails loudly after 30 s.
      Ten runs alone: 26-37 ns, 0.97-1.00x.
    - The bound stays at 1.5x. The defect the test exists for, a scan over every interface-keyed entry, cost 272 ns
      → 6.3 µs at 1,000 extra pairs (round 31): far outside it.
    - Then the lane itself. In the perf-tests job's own shape (`sdk:10.0.101`, four CPUs, the job's command), the
      fixed test passed 16 runs of 16. In 3 of them a phase still moved by 1.45-1.76x, each time while another
      project's test host was starting: `dotnet test` on the solution starts all ten at once. The same command with
      `-m:1` runs one project at a time: 8 of 8 at 0.91-1.02x, about six seconds slower. The perf-tests job and
      housekeeping's stage 1c now pass `-m:1`, and `PerfCategoryIsolationScanTests` requires it of every run that
      selects the category (RED on both, then GREEN).
- **E. Test start-up and the long legs** (the owner, 2026-10-27: "starting tests commonly takes longer than running
  them"; then "target lowest times at maximum surface without current coverage reduction"). The evidence is in
  `benchmarks/results/2026-10-28-round34-e-test-startup.md`.
  - **Where the time is.** The six mutation legs are 215 of a full-ci run's 273 job-minutes; the generator and
    pipeline legs set its wall-clock. A test host costs ~1.5 s to start before any test of ours runs (a trivial
    xunit project: 1.46 s), and Stryker 4.16 starts fresh hosts for every session, serially inside each.
  - **E1 done — `2c9995d`. The static mutants were an artifact.** 160 of the generator leg's 415 mutants and 178 of
    the pipeline leg's 314 were "static", so Stryker ran each against the whole suite. Its coverage phase at debug
    verbosity showed why: five theories over records (`MatrixCell`, `FimMatrixCase`) were each ONE xunit test case
    reporting a result per row, and Stryker's coverage window closed at the first, so rows two to N ran outside
    every window. Each row is now its own test case (it passes a key the test resolves);
    `TheoryRowsAreTestCasesScanTests` holds that. In the 4-CPU container, at concurrency 2:
    - generator leg 1:14:03 → 34:01 (-54 %), 97.11 % both times, no mutant's status different, 0 static;
    - test hosts started 1,038 → 277.

    On CI (run 40637130998, Stryker step against the previous run's): generator 79.6 → 62.6 min at the same 403
    killed / 12 survived / 0 timeouts, pipeline 107.4 → 29.8 (72 in another run before E1), testing 24.1 → 18.2.
    The generator leg gained less than in the container: its sessions are now short, so their start-up decides.
  - **E3 done — `35f1a20`. The test hosts ran Roslyn on unoptimised code.** One cell's compile costs ~25 ms early in
    a host and 7.3 ms in a warm one; the mapper itself runs in microseconds. Roslyn's package is IL only, and every
    generator test loads a fresh assembly, so the runtime kept postponing call counting and tier-1 recompiles
    (`TC_CallCountingDelayMs`). Every test project's runtimeconfig.json now carries call counting from the start,
    no PGO pass and no background GC (Directory.Build.targets). Generator.Tests against the defaults run beside it:
    full run 62.8 → 29.8 s and 288 → 140 CPU-s, serial 226-test session (a Stryker session's shape) 8.1 → 5.5 s.
    - IntegrationTests keeps the defaults (`DwarfKeepRuntimeDefaults`): its Perf ratio times interface dispatch,
      which PGO compiles, and its allocation contracts count what optimised code may keep off the heap.
    - Locks: `TestHostRuntimeTuningTests` (RED 4/4 before), `PerfCategoryIsolationScanTests`' new fact, and
      `PerfHostRuntimeDefaultsTests` (both RED with the opt-out removed).
    - Rejected with numbers: tiering off (serial session slower), server GC, Roslyn precompiled ReadyToRun (+9 %
      for a crossgen step in every clean build), a fixed assembly name (0.16 ms), cells batched into one
      compilation. Generator work prepared ahead of a test or cached between tests would make its mutants static
      again: the safe place to prepare is the runtime.
    - The owner, 2026-10-28: "keep it local, push once at the end". The clean before/after (the whole suite, the
      surface matrix, the deep tier, the power set, coverage, and the container mutation legs) runs after the owner's
      game, which skews every timing taken beside it.
  - **The long legs, measured where their time goes** (the owner, 2026-10-28: "that is still way too long. The
    longest tests run over an hour and half"; "merge short session tests and surface tests and swap them into full
    scale ones"; "the overall testing should be in matter of single minutes"). E3 on the generator leg, container,
    concurrency 2: 51:32 → 35:41 (-31 %), mutation testing 43:00 → 27:36, 0 of 867 statuses changed.
    - Generator.Tests compiles the same input twice or more in 51 % of its compilations (surface matrix 84 %, deep
      tier 56 %), mostly inside one test. Ranked against the mutation reports, the costliest tests are the
      combinatorial cells' rows, the pairwise feature rows, the warning rows, and two facts that looped the generator
      over all 1,021 golden cases.
    - **E5 done — `c81e014`.** Those two facts are one row per golden case running the generator once for both
      checks; `CorpusWideSweepScanTests` holds it (red on `3204dc6`, three sweeps). Coverage unchanged line for line.
      No measurable gain on the leg (37:26 against 35:41, within this machine's drift): 46 mutants reach nearly every
      row. Kept for half the golden generator runs and per-case failures.
    - **E4 — killers first.** `KillersFirstOrderer` runs the test classes and methods with the most kills in the last
      mutation reports first (`MutationKillRanking.tsv`, written by `scripts/kill-ranking.py`), so a killed mutant's
      session ends at an early killer. On top of E5: mutation testing 29:11 → 20:25 (-30 %), the leg 37:26 → 28:25,
      0 of 867 statuses changed. `KillersFirstOrdererTests` holds the wiring, the file and the order.
    - Measured and dropped: Stryker on a Release build (-1 to -5 % on its sessions; the cost is Roslyn's, which ships
      optimised).
  - **Concurrency, measured with the owner's approval** (the guard's 2026-08-26 basis predates A1 and assumed xunit
    parallelises inside Stryker's sessions, which its run settings forbid): concurrency 4 is 23 % faster on the
    testing leg and 19 % on the generator leg (27:39), with identical statuses and no extra timeouts; the memory
    floor drops to 4.75 GB. **E2 not applied:** measured again on top of E3, E5 and E4 it bought 3 % (27:38 against
    28:25, statuses identical) while the memory floor fell to 3.3 GB; two short sessions already keep four CPUs busy.
  - **Not a lever yet:** Stryker's MTP runner keeps hosts warm but cannot yet run only a mutant's covering tests.
    The runtime leg's static mutants are module-initializer registration, the product's own start-up.
  - **E7 done — `3a0a03c`. A check compiles its source once.** `GeneratorAssert`'s helpers ran the generator for the
    diagnostics and again for the compile errors (790 call sites), and every combinatorial row ran it again to emit.
    `RunWithCompilationErrors` and `RunAndEmit` answer both from one run; `CompileOnceTests` holds them (red: "Expected
    1, Actual 2" three times).
  - **E8 done — `3f430d4`. Errors-only checks skip the nullable walker.** Binding was most of a power-set variation
    (19.1 CPU-ms of about 27); without the walker it fell 15.0 → 7.7 ms. `ErrorsOnlyCheckTests` holds it. E7 and E8
    together, alternating against `93b1bb4`: build-test's run -10 %, surface matrix -7 %, deep tier -14 %, power set
    7:43 → 6:19 (-18 %).
  - **E9 done — `500b5f2`. The surface matrix's populations from one sweep.** Nine facts each classified the matrix;
    one fact sweeps once and runs the nine checks. Surface category 12.4 → 8.4 s wall. `CorpusWideSweepScanTests`.
  - **E10 done — `98e0d9c`. What the GC pays for** (the owner, 2026-10-28: "shouldn't we just handle disposal by
    ourselves instead of pressuring garbage collection?"). The harness kept a baseline compilation for every unique
    assembly name, 5,319 in the deep tier; it caches from a name's second request now. The live heap did not move (430
    MB before, 419-444 after): the GC pays for allocation, about 30 GB and 11-12 s of pauses per 50 s deep run, which
    nothing the tests hold or could dispose changes. `BaselineCacheTests`.
  - **E11 done — `8c98435`. Three fuzz families compile each row once** (feature combinations, compiles-always and
    cross-config ran the E7 shape in their bodies). Deep tier 26,307 → 21,280 compilations and 57.7 → 47.8 s wall
    (means of two). The feature-combination rows read their compile errors under nullable Enable now, as they read
    their diagnostics; coverage unchanged. `CompileOnceTests`.
  - **E13 — server GC where it measured a gain.** Deep tier: 53.3 → 44.9 s on this machine's 12 logical CPUs, 80.0 →
    68.2 s on four (the container, a hosted runner's shape); build-test's run on four: no gain; DATAS off adds nothing.
    CI's `fuzz-exhaustion` job and housekeeping's `-Deep` stage 1 and exhaustion stage set `DOTNET_gcServer` and put it
    back before the stages that time code or run Stryker (a serial session is slower under it). `ServerGcScopeScanTests`
    holds the three places and keeps it out of every runtimeconfig (red without the change: 0 stages, no job).
  - **What bounds a parallel run.** A class's tests run one after another, so the heaviest class sets a run's length:
    Generator.Tests in build-test's run took 21 s against 16 s spread evenly (`CombinatorialEngineTests` ended last),
    the deep tier 48.5 s against 32 s (`FeatureCombinationFuzzTests`, alone for its last 14 s).
  - **E12 done — `b27ab09`. The heaviest theories' rows are dealt across classes.** Seven theories split into
    `<Class>PartN` classes by `TheoryParts`; the killers-first ranking ranks a part as its outer class. Generator.Tests
    alone: plain 24.9 → 23.4 s, deep 46.8 → 43.1 s (means); the whole solution's plain run is unchanged at ~30 s, CPU-bound
    with eight other hosts beside Generator.Tests. `TheoryPartsTests` holds every row dealt exactly once (red with a part
    index planted twice), `KillersFirstOrdererTests` the parts' rank (red with the fallback off). Left: the deep tier's
    tail is mid-sized classes the kills-first order starts late; a longest-first order in parallel runs (kills first
    under Stryker) would be worth about 5 s.
  - **E14 done — `b1db8e9`. One way to run the suite** (the owner, 2026-10-28: "convert preliminary tests into full
    deep ones (removal or merge) so we have single way of testing entire infra"; the owner's choices: the power set
    keeps its own job, the mutation legs run the same full populations). Every population runs what was its deep size
    (`tests/Shared/TestPopulations.cs`, was `DeepTier.cs`); `DWARF_DEEP`, `.runsettings`, housekeeping's `-Deep` and the
    `surface-matrix` job are gone; every job runs `--filter "Category!=Perf"`. Coverage identical to the old deep run, a
    superset of the old per-push run (+9 generator lines, +8 branch points). The per-push suite on four CPUs 57 s → 2:01;
    on this machine 29 → 60 s. `TestPopulationsSelfTests` pins the sizes and bans the variable (red on both planted).
    Day's result before E14, same session: build-test's run -52 %, surface -67 %, deep -65 %, power set -52 %,
    generator mutation leg 51:32 → 26:33.
  - **Test-writing rules** (the owner, 2026-10-28: "Are there any rules about test creations? … we should have been
    avoiding these problems right from the start"). The published ones (Microsoft's unit-testing practices, xUnit's
    parallelism and shared context, Google's test sizes, Stryker's `since` and `with-baseline`) are mapped onto this
    PR's fixes; a rules page naming each rule's enforcing test is drafted and waits for the owner's go-ahead.
  - **The mutation legs after E14** (the owner, 2026-10-31: "For active defensive testing the 30 minute tests are no
    go. Each iteration must have 1 minute at most"; 2026-11-01: "we are searching order of 10x issue ... We need like
    90% improvement"). The evidence is in `benchmarks/results/2026-11-11-round34-mutation-legs.md`; four CPUs in the
    container unless said.
    - **r1, r2, r4 done — `ff278c1`, `4fee98a`, `28ca173`. CompilerTests 36.7 → 29.9 s**, one change a round, five
      interleaved repetitions each: one analysis pass per emitted sample (−11.5 %), samples compiled on the thread that
      runs them (−3.5 %), server GC for this one project (−11.5 %; `ServerGcScopeScanTests` narrowed to it with the
      owner's approval). Coverage identical each round.
    - **r5 done — `544ea02`. The killers-first order ranks by not-yet-killed mutants per second.** A narrowed leg's
      mutant runs 3:38 → 0:52 on the same project order, statuses identical.
    - **Found: Stryker 4.16 draws its test-project order once per launch** (a `ConcurrentBag` filled in parallel; no
      option, and the solution file does not steer it), and the draw that runs CompilerTests ahead of Generator.Tests
      took the same code's mutant phase from 20:55 to 48:29: 73 runs that Generator.Tests kills each paid CompilerTests'
      covering run first (median 60 s, 54.6 % of runner time). Three of twelve full legs on file drew it, and three of
      five probe launches. Three findings are written up for Stryker and wait for the owner to file them.
    - **r6 done — `a0fc32f`, fixed by `99a688e`. CI's generator-project legs compile only what they mutate.** Sources
      no glob names get an `<auto-generated>` header in the throwaway checkout, which Stryker compiles without
      mutating: mutate-and-compile 2:20 → 0:17, mutants created 16,314 → 1,428, the report 114 → 55.5 MB, every
      status the same. The fix: the job's `git diff --exit-code` after the leg would have failed both legs on the
      marking itself on a runner; the marking is staged now, and the assertion still fails on a stray write.
      `UnmutatedSourcesMarkingTests`, `MutationLaunchContractTests`.
    - **Measured and rejected: dealing CompilerTests' population facts into parts.** 53 to 77 % of a fact's covered
      mutants are reached by every one of 60 slices; four parts would save 4.4 % of the costly runs, ten 7.4 %.
    - **r7 done — `2e4670f`, `100e950`, `180acea`, `f46ab7b`. CI's generator-project legs reroll the bad draw.**
      `Invoke-StrykerRerollingBadOrder` reads the order from the two projects' test hosts before anything is mutated,
      kills Stryker on the bad draw and launches again, three launches at most; a launch already on its mutants is
      never killed. Five of five verdicts right on real launches; the full leg through it 31:04 against the bad
      draw's 58:37, statuses identical. `ProjectOrderRerollTests` (its fake hosts scheduled in polls, so no verdict
      depends on the machine's speed; the "already on its mutants" question asked over a real test-bin tree, with
      and without Stryker's backup file).
    - **Measured, rig only: the legs without Stryker's per-run machinery.** An in-process runner on Stryker's own
      mutated assembly and `MutantControl` switch: the pipeline leg in 3:40 against Stryker's 21:53 on the same tree,
      the generator leg in 6:55 against 31:04 (58:37 on a bad draw), every status identical to Stryker's report (435
      of 435, 625 of 625); 5:31 at this PR's head. Not in the repository; integrating it is owner decision 6 below.
    - **c1 done — `6f6e00f`. CompilerTests' two smoke facts fold into the oracles that already run their check** (the
      owner, 2026-11-01: "We want to colaps simplified ones, because surface testing is not enough for active
      development"): the same 1,000 graphs, the same first assertion, the mutants they reach a subset, their one
      recorded kill made by each full fact alone, and the same 5,866 generator lines and 2,908 branch conditions
      covered without them. Measured without and with the fold, five interleaved repetitions: CompilerTests 28.2 →
      24.9 s (−3.3 s ± 1.5 s), the whole suite's coverage identical in every assembly, and the generator leg's
      CompilerTests stage through the in-process runner 171 → 137 s.
    - **Measured, no gain: power-set subsets batched into one compilation** (2.65 ms a subset alone, 2.65 to 3.47 in
      batches of 16 to 256).
  - **The advisory that turned every restore red — `ed19b7c`.** GHSA-p3jp-9v83-76w9 (2026-11-03) is against
    `Microsoft.DotNet.ILCompiler` 10.0.1, the compiler the pinned SDK gives a `PublishAot` project, and the restore
    audit failed 18 of 23 nightly jobs on master for eight nights. Such a project is now published against runtime
    patch 10.0.15 (`RuntimeFrameworkVersion`; the SDK pin stays): restore red → green on Windows and in the container,
    the AOT gate's steps green on both RIDs. `AotCompilerPatchScanTests`. Two other ways were tried and fail; the
    audit was not touched.
  - **A job's real steps, rehearsed before a push.** r6's defect passed two full container legs because the rig ran
    the marking and Stryker and not the job. `ci_job.py` (rig) runs a workflow job's `run:` steps as written, in
    order, with their `if:`, env and shell: the mutation job red on r6 as committed and green with the fix. Before
    the push the jobs this work changed were rehearsed at the head in the runner's image on four CPUs: `mutation`
    (the generator leg, narrowed), `deep-test`, `fuzz-exhaustion` and `perf-tests` green; the codefixes leg and
    `build-test` were still running when this was written. The table is in the results file.
    - **Found, left as it is: housekeeping's ILVerify stage looks for the reference pack beside the `dotnet` on
      `PATH` without following a symlink.** The first `deep-test` rehearsal failed there ("no net10 ref pack found"):
      the SDK container has `/usr/bin/dotnet` as a link and no `DOTNET_ROOT`. A hosted runner sets
      `DOTNET_ROOT=/usr/share/dotnet` (run 41622723016's log), so CI is not affected, and the stage fails loudly,
      naming where it looked. A distribution's packaged SDK has the same shape; following the link is a small fix
      for a later round.
- **B. The silenced-checks inventory** ("Owed between rounds"), diffed against round 30's.

  **Inventory taken** (`Issues/round34/SILENCED-CHECKS-INVENTORY.md`, agent-produced, headline claims spot-checked).
  Round 30's de-silencing plan, including Task 30.4's reason-required scans, was never built. Its 23 proposed tasks
  wait for the owner's pick; the top three are shipped code: four directive readers drop a `null` name without a
  diagnostic, a probe for a misspelled `[MapProperty]` destination under `[MapTo]`, and Task 30.5. Also verified:
  the AOT sample's two checks cannot fail the gate, and master requires no status checks (owner's setting).

  **B1 done — null directive names are reported.** `[MapIgnore<T>(null)]`, method-level `[MapValue(null!, …)]`,
  `[Flatten(null!)]` and `[FlattenGraph(null!, …)]` vanished with a green build while an empty name was reported.
  Their readers now read null as the empty name, as `d474e9f` and `fc58fb0` did for the three pair-scoped siblings
  on 2026-09-13, so each reports what an empty name does (DWARF095/056, DWARF042, DWARF016, DWARF034; DWARF090 and
  DWARF092 at element-wise and update-into endpoints), and no message renders the argument as null. Three tests had
  pinned the silence (`PairScopedIgnoreReaderCoverageTests`, `FlattenAndMapValueReachTests`,
  `DirectiveEndpointReachTests`); they now assert the report, and `MethodDirectiveNullNameTests` is new: RED before,
  GREEN after. Whole solution 0 warnings; every test project green.

  **B2 done — DWARFR17.** Under `[MapTo]`, a `[MapProperty("Nmae")]` naming a destination member the target does not
  have was skipped in silence (`MapToGenerator`'s `if (w.Symbol is null) continue;`). The probe confirmed the
  inventory's inference: with `Name` mapped by name, nothing at all was reported. DWARFR17 is the registry's DWARF008,
  an Error at the directive; a member's own name matching nothing is still just an unused source member.
  `RegistryDiagnosticsGenTests` holds both (RED before, GREEN after); documented in `docs/diagnostics.md` and
  release-tracked. Whole solution 0 warnings; every test project green.

  **B3 done — the AOT gate's two blind checks.** CI judges the published NativeAOT binary by its exit code alone,
  and the sample's narrow-overflow and string-to-int checks printed `ERROR:` and carried on, so they could never fail
  the gate. Both return 1 now. `AotSampleGateScanTests` holds every `ERROR:` line to a non-zero return (RED on
  `Program.cs:32` and `:54`, GREEN after). The stale "36 `return 1` sites" in ci.yml and CORRECTNESS.md (39 now)
  became the rule the scan holds.

  **B4 done — round 30's Task 30.4, the reason-required scans** (the owner asked on 2026-10-27 for the whole
  inventory in this PR). `SuppressionReasonScanTests` holds five rules, each with a positive control:
  - every `<NoWarn>` id is named by the comment directly above it, and that comment names no id the element
    does not suppress;
  - no project suppresses a DWARF id in `<NoWarn>` (the 2026-09-27 ruling);
  - every real `#pragma warning disable` (parsed, so string literals and doc comments do not count) has a `//`
    reason on its line or the line above, as Task 30.4 specified;
  - every `[SuppressMessage]` has a non-empty Justification;
  - every `.globalconfig` downgrade has a comment directly above it.

  They found 60 `<NoWarn>` findings, 5 DWARF ids, and 6 pragmas. Before writing reasons, `nowarn-audit.ps1`
  (fixed first, `dd47ae2`) separated live suppressions from dead ones: 8 of 105 suppressed nothing and were
  deleted (CA5394 in Benchmarks, Conformance and Gallery; DWARF044 in AotSample and IntegrationTests; CA2227 in
  ProviderA; CA1002 and CA2227 in ProviderB). Every surviving id got its reason from the sites that raise it.
  DWARF038 moved from three `<NoWarn>` lists to per-project `.globalconfig` files with their reasons. Stale
  comment ids went (the empty CodeFixes `<NoWarn>` among them), and `samples/.globalconfig`'s premise that every
  sample suppresses CA1050 was corrected: CA1050 fires on none of them. The six pragmas got reasons; one
  file-header pragma suppressing five ids, IDE0079 included, shrank to the two that fire, scoped to the two types
  that raise them. Whole solution 0 warnings; every test project green.

  **B5 done — `DwarfMapper.Testing`** (inventory item 4, the owner's choice of record-and-report for SafeTypes):
  - CA1510 is fixed rather than suppressed: fourteen `if (x is null) throw new ArgumentNullException(nameof(x))`
    became `ArgumentNullException.ThrowIfNull(x)` (same exception, same parameter name), and the id left the
    project's `<NoWarn>`.
  - `GraphOracleComparer`'s two catch blocks were not swallows; each now says why: an overflow falls back to the
    scalar comparison and still records a difference, and an element whose ToString throws can only cause a loud
    false difference, never a false pass.
  - `ObjectFactoryV2.SafeTypes` records what did not load instead of dropping it. When no loadable type implements
    an abstract type and an assembly loaded from a file declares one that failed to load (read through its
    metadata), the factory throws, naming the type, its assembly and the loader's reason, instead of leaving the
    member null. An assembly loaded from memory stays recorded but unreported: naming it on a guess would fail
    tests that have nothing to do with it (`IUnimplemented` and the dangling test's own assembly showed how).
    `DanglingDependencyTests` holds it: RED (no exception), GREEN after; Testing.Tests 140/140.
  - The mutation-scope audit table is re-measured in the same commit: the testing leg's share fell 18.2 % -> 17.0 %.

  **B6 done — the emitted pragma documented** (`docs/CORRECTNESS.md`), and DocTooling's swallowed catch states its
  reason (`fc0ae37`).

  **B7 done — `Directory.Build.targets` no longer claims an `.editorconfig` section that does not exist.** The
  `generated_code = false` section is deliberately not committed (round 24, `d38cda1`). Round 24's blocker was
  re-tested on SDK 10.0.101: per-rule `none` under that section silences nothing a generator emitted (1,404 CA1822
  and 32 others, identical with and without it), so the always-on gate stays blocked. The two FILED findings,
  CA1852 (4) and CA1508 (2), stand; CA1019 (8) is new. Recorded in `Issues/round24/ROUND24-GENERATED-CODE-AUDIT.md`.

  **B8 done — scripts and the tools they need** (inventory items 14 and 15):
  - `mutation-battery.sh` ends by proving src/ and docs/ are clean again: every restore swallowed its own failure.
    `MutationMethodologyScanTests.M1c` holds it (RED without the check, GREEN with it).
  - `corpus-diagnostic-density.ps1` prints "revision UNKNOWN" when git fails, instead of an empty revision
    labelled "(src/ clean)". Not under test: it is the provenance line of a manual measurement script.
  - `gate-checks.ps1`'s ledger reader says what a json block that does not parse costs: the band alone, never a
    passed regression.
  - ci.yml's Codecov comment no longer says the deleted inline gate still runs, and says the status check blocks a
    merge only where master requires it, which today it does not.
  - CONTRIBUTING.md declares `pwsh` and Python 3 (standard library only), and that `full-ci` takes effect from the
    next push.

  **B9 done — two scans that passed over nothing, and ReSharper suppressions held to a reason** (items 19, 20):
  - `ConformanceArtifactScanTests` returned when `conformance/results/` was missing ("the gate creates it on first
    run"), which stopped being true on 2026-07-26: six artifacts are committed. Both tests now fail on a missing or
    empty directory (checked by moving it away: 2 of 3 red; restored: 3/3 green).
  - Round 30's Task 30.6 asked for the 19 InconsistentNaming headers in one reasoned place. Each already carries its
    own specific reason line; one place would have traded 19 reasons for one generic one. Instead
    `SuppressionReasonScanTests` holds every `// ReSharper disable` and `disable once` (40 today, all reasoned) to
    a reason line directly above, with a positive control.

  **B5, what the first full-ci run caught (`fc0ae37`, run 40558858454).** Two of its three reds were B5's. The third,
  `perf-tests`, was not (A3).
  - deep-test: `DwarfMapper.Testing` line coverage fell to 99 % against its 100 % floor. The run's coverage report
    put every gap in B5's metadata reader. No test made it name a type definition, a generic instantiation, a
    nested definition, a type nested in another assembly's type or a global-namespace type, or list two missing
    implementations at once. One fallback, `FullName ?? Name`, could not be taken. Loaded types are now matched by
    metadata token, which drops that fallback and compares exactly, and `TypeName` lost its default branch, which no
    base type or interface can reach. `DanglingDependencyTests`' probe declares every shape, all in sources compiled
    at run time (as test-assembly types they tripped CA1040 and CA1034). Measured: `Testing.Tests` alone leaves no
    line or branch of the new code uncovered.
  - package-size: the `DwarfMapper.Testing` package went from 53 to 56 KB, and B5 did not re-measure: invariant R1
    missed a fourth time, stated rather than backdated. Re-measured in its own commit (B5c).

  **B5c done — the package-size ceiling re-measured** (`scripts/gate-checks.ps1`).
  - `DwarfMapper.Testing` goes from 53 to 56 KB; `DwarfMapper` stays at 362.
  - Measured in `sdk:10.0.101` with the job's own pack commands and `CI=true`:
    - master (`4a169a4`): 370,900 and 54,598 B;
    - round 34 (`a3b135d`): 371,023 and 57,883 B;
    - Windows, at the same commit: 371,224 and 57,934 B.
  - Entry by entry, the Testing package grew only in its own DLL (+2,732 B compressed) and its XML docs (+557):
    B5's metadata reader. Nothing new ships.
  - The real gate function against those packages: RED with the old ceiling, GREEN with the new one.
  - `DwarfMapper`'s headroom is down to 488 B on Windows.

  **B10 done — the complete fuzz proof and the ISA matrix run in CI** (inventory item 13). Until now both ran only
  on a maintainer's machine.
  - `FeatureCombinationFuzzTests.Full_power_set_compiles_on_every_emit_path` returns at once unless
    `DWARF_FUZZ_FULL=1`, and no CI job set it: housekeeping `-Nightly`, which deep-test runs, skips the exhaustion
    stage. Its own comment said CI ran it. A nightly job, `fuzz-exhaustion`, now sets the variable and runs that one
    fact. The test asserts that every subset compiled on every path, then writes a line that the job's vacuity step
    requires. Measured 2026-10-27: 65,536 subsets × 4 emit paths = 262,144 compilations, 0 failures, 10.5 min on
    6 cores / 12 threads. The job's 90-minute limit is an estimate for the first full-ci run to pin. The old
    "~6 min" figures in the test and in housekeeping were corrected.
  - `scripts/isa-matrix.ps1` ran by hand only. A nightly job, `isa-matrix`, now runs it. The script leaves out
    `Category=Perf`, like every other test run CI reaches, and it shows a failed build's errors. Piped to `Out-Null`,
    its first run for this item said only that the build had failed (two analyzer errors in an edited test file).
    `IsaMatrixScriptTests` drives the real script with a planted failing `dotnet`: RED, the error is not shown;
    GREEN, it is. Locally all five configurations pass, 1,017 integration and 70 differential tests each, in 50 s.
  - Two scans had to learn about the new jobs:
    - `CiGateScanTests` gained two rows.
    - `CiToolPrerequisiteScanTests` now exempts a job whose every test run selects tests by name, provided none of
      the names can reach the ILVerify tests. The first version of that check looked for "IlVerif", which
      `EmittedIlIsVerifiableTests` does not contain, so it would have waived the install for a job that selects
      those tests by name. Review caught it. A theory over nine filters failed 6 of 9 against that version.
  - CONTRIBUTING's list of the expensive tier gained `perf-tests` (missing since round 31) and the two new jobs.

  **B11 done — three non-blocking jobs block** (inventory item 6; the Ubuntu canary by the owner's choice,
  2026-10-27). Their job-level `continue-on-error` is gone.
  - `roslyn-forward-compat` and `preview-sdk-canary` set their own criterion: blocking once seen green, and for
    the preview canary on pushed master, with the run cited in the flip commit. Both were green on every master
    nightly read for the flip, 2026-10-22 to 10-27. The run cited is 40477294097 (`b58c6e3`).
  - `ubuntu-26-canary`'s comment planned to move every pin to 26.04 and delete the job. The owner chose instead to
    make the canary blocking and keep every other Linux job on 24.04. It was green on the same six nightlies, and
    the header note on the pins now says so.
  - `CiGateScanTests`: the three rows no longer say "declaration only". A new theory fails if a job-level
    `continue-on-error` comes back on any of them: planted on `ubuntu-26-canary`, 1 of 3 red; removed, green.
    Step-level ones (`bench-wall-time-alert`, `build-test`'s test-analytics step) are out of its scope by design.

  **B12 done — zizmor blocks** (inventory item 7). `actions-security.yml` was authored in reporting mode, to become
  blocking once the owner had triaged a first baseline. Every run read, six on master and PR #11 on 2026-10-27,
  reported 0 findings: the baseline was empty and there was nothing to triage.
  - What fails the job now is `Assert-ZizmorReportClean` (`scripts/gate-checks.ps1`) instead of inline Python. It
    fails on any open finding, annotating each on its file and line. A finding suppressed in the workflow's text
    (`# zizmor: ignore[...]`) does not count. A missing, empty or run-less report fails, and so does a run without a
    results array, which SARIF reserves for a tool that produced none (the latest code-scanning analysis and a local
    run both carry `results: []`).
  - The SARIF upload runs even when the gate fails, so the findings that failed it show in code scanning.
  - `ZizmorGateTests` drives the real function over seven fixtures: RED, the function missing; GREEN, 7 of 7.
    zizmor 1.30.1 run locally over the changed workflows: exit 0, 0 findings.

  **B13 done — the scanner image tells the agent whether its suite was red** (inventory item 9).
  `.oss-scanner/Dockerfile` runs the push-time suite and must not fail on a red result, because an audit of a red tree
  is still worth running. Until now it said so only with `|| echo WARNING` in the build log, which the scanning agent
  never reads. It could audit a red tree as green, and take a test that was already failing for its own finding.
  - The image now keeps the outcome in `/suite-outcome.txt`: GREEN, or RED with the exit code and the tests that were
    failing.
  - `threat_model.md` tells the agent to read that file before anything else.
  - `OssScannerImageTests` pins both, and that no `|| echo` is left: RED against the old pair, GREEN.
  - The instruction itself, joined as Docker joins it, ran under the SDK image's `/bin/sh` with a stub `dotnet`. Green
    wrote GREEN; red wrote RED and the failing test. Both left the build step at exit 0.

  **B14 done — the mutation configs stop denying their filter, and its cost is measured** (inventory item 11).
  Round 33 gave every leg `test-case-filter: Category!=Perf`. The generator config's comment still said no
  `test-case-filter` "appear[s] here, deliberately", and the runtime config's was headed "WHY THERE IS NO TEST FILTER".
  Both now name the one filter they carry and what it cost. `PerfCategoryIsolationScanTests` fails if a comment denies
  the filter beside it: RED on those two, GREEN.

  The cost was measured from CI's reports: run 40299439526 (`1ca6232`, the last before the filter) against the
  filtered nightly, run 40477294097 (`b58c6e3`).
  - The reports name only `Generator.Tests`' 7,101 tests; every other project's killers are bare ids. A test id is a
    stable GUID of name and path, and the ratio test is the only id absent from the filtered run that covers anything
    (22 registry mutants).
  - doctooling, codefixes and testing: it killed nothing.
  - runtime: it is the only recorded killer of three `DwarfMapperRegistry.Map` mutants (both `ThrowIfNull`
    deletions and the base-type loop condition). Those were bail artifacts: Stryker stops a session at its first
    failing test, and the flaky ratio test failed first. The filtered run kills all three with other tests.
  - generator: one static `BlittableProof` mutant, in code the ratio test never runs, so a timing failure. It reads
    Survived since, which is honest.
  - pipeline has no report in the old run; it mutates generator code, which the ratio test does not run.

  **B15 done — Task 30.5, the widening-primitive near-miss, measured** (inventory item 1). Round 30 asked whether
  `BlittableProof.TryExplainNearMiss` should report a pair whose field names line up and whose differing field is a
  widening primitive (`int` against `long`). It said: measure the noise first; ship only if the count is small and
  every new report is a genuine mistake; otherwise put the number in the doc comment.
  - Measured with temporary instrumentation placed after the method's field-count gate, never committed. It recorded
    every pair asked about, every pair with equal field counts, and every pair whose names line up with a type
    differing (widening, or any other difference).
  - Golden corpus (`GoldenCorpus.Cases()`, 1,021 cases): 59 distinct pairs asked about, none with equal field counts.
  - Gallery (its 50 files plus its implicit usings): 2 asked about, none with equal field counts.
  - So it is 0 new reports either way. The silence stays: zero reports means no noise, but also no evidence that a
    report would name a mistake rather than a width chosen on purpose. And a wider field changes the bytes, unlike a
    rename or a `?`. The numbers and that reasoning are now in the doc comment; reporting it stays the owner's call.

  **B16 done — the NoWarn audit fails a stale id every night** (inventory item 5b). `scripts/nowarn-audit.ps1` rebuilds
  every project that declares a `<NoWarn>` with the list cleared. Its help said housekeeping would turn `-FailOnStale`
  on once the lists were trimmed; B4 trimmed them, and nothing turned it on.
  - Measured first: 140 s on 6 cores / 12 threads, and 187 s in `sdk:10.0.101` on four CPUs. 0 of 93 declared
    suppressions are stale on either platform, so Linux and Windows agree on what fires.
  - It now runs under housekeeping's `-Deep`, and so in the nightly deep-test job (15-16 min against 45). It runs after
    the stages that read Release output, because its forced rebuilds replace that output.
  - `SuppressionReasonScanTests` holds the wiring: RED before it, GREEN.

  **B17 done — the independence oracle fails on what it could not examine** (inventory item 16).
  `IndependenceOracle` used to skip a member whose getter threw, and stopped at depth 32. Either way it returned what
  it had seen, so "the mapped graph shares no collection with the source" could pass for parts of a graph it never
  looked at.
  - It now records both, naming the path, and fails, as `GeneratorCacheAssert`'s symbol-leak walk does.
  - An object already visited is checked before the depth cap, so a cycle is never mistaken for depth.
  - Two facts are RED against the old walk (no failure for a throwing getter, none for a 40-link chain) and GREEN now;
    a self-loop still passes.
  - The fuzz theory stays green with the stricter walk: 60 seeds in the fast tier and 600 in the deep tier, so no real
    graph hits either case.

  **B18 done — the samples' analyzer "downgrade" never existed** (round 30's Task 30.2, the rest of inventory item 18).
  The Gallery and Conformance samples set `AnalysisMode=Recommended`, explained as relaxing the repository's profile.
  - The SDK takes the analysis mode from `AnalysisLevel`'s suffix first, and reads `AnalysisMode` only when there is
    none (`Microsoft.CodeAnalysis.NetAnalyzers.targets`, SDK 10.0.101). `Directory.Build.props` sets `latest-all`.
  - Built with their `NoWarn` cleared, both samples raised exactly the same ids under either mode (Gallery 9 ids,
    Conformance 12), every one already on its reasoned `NoWarn` list.
  - The inert line and the comment claiming a relaxation are gone. `SuppressionReasonScanTests` keeps any project below
    the root from setting its own mode or level: RED on the two samples, GREEN. Both samples build clean with
    warnings as errors.
- **C. DwarfMapper's share of a large build** — a `-p:ReportAnalyzer=true` build of a fresh copy of FusedChat's
  validation tree (32 projects, 8 mappers), repinned to master packed under a version nobody will reuse: the
  generator's time, cold and warm, against the compiler's and the whole build's. The generator alone is measured
  (round 23 S3, repeated in round 33: linear, ~0.5 ms per mapped pair warm); what is new is its share of a real
  build, which decides whether compile-time work earns a round. No earlier FusedChat build timing exists in
  `benchmarks/results` or `Issues/`; ReportAnalyzer was used before only on this repository's own analyzers
  (rounds 21-22).

  **Done — `benchmarks/results/2026-10-27-round34-c-fusedchat-build-share.md`.** Six `--no-incremental` Release builds
  of a fresh copy (SDK 10.0.400, `1.1.0-r34val.1`), cold and warm alternating. DwarfMapper's generators run in 20
  compiler calls per build, summing 2.75-2.94 s cold and 1.93-2.19 s warm: about 5 % of the compiler's 40-57 s.
  Cold start is worth ~0.8 s a build, which is everything ReadyToRun could recover, so the cold-JIT lead is closed.
  In the same builds `Microsoft.VisualStudio.Threading.Analyzers` costs 12.1 s and Razor 8.3 s: a slow FusedChat
  build is mostly not DwarfMapper.
- **D. Mutation-testing accuracy** (owner decision 4): can `SurfaceProbe.Baselines`, a process-wide memo of generator
  output, make Stryker credit a kill to the wrong mutant or hide one? Stryker 4.16's source first (how mutants are
  grouped into a session, whether a test host outlives one), a narrowed Stryker A/B only if that leaves it open. The
  deliverable is the answer and a proposal; the memo's design stays the owner's call.

  **Answered: no, not with today's coverage.**
  - How Stryker 4.16 runs mutants, read from its source:
    - A session tests several mutants only when no test covers two of them (`MutationTestProcess.BuildMutantGroupsForTest`,
      `!assessingTests.ContainsAny(…)`).
    - A mutant whose tests are "every test" (the static ones) gets a session to itself.
    - Every session starts its own test host: `StrykerVsTestHostLauncher` starts a process per `RunTestsWithCustomTestHost`.
    - xUnit runs serially in Stryker's settings.

    So the memo lives for one session, and each key is filled by the first test, in a fixed order, that needs it,
    under that test's mutant. A test reads a baseline computed under another mutant only if two conditions hold. A
    mutant covered by one memo test shares a session with a mutant covered by another memo test. And the second test
    does not cover the first mutant.
  - Measured on the six legs' reports of run 40477294097 (`b58c6e3`):
    - 1,003 tests read the memo: the per-cell theory's 966 rows, plus 37 others.
    - They cover 13 non-static mutants in the generator leg, 25 in the pipeline leg, and none in the other four.
    - No such pair exists in any leg.
    - The screen is not blind: two planted mutants, each covered by a single cell row, give 810 pairs.
  - The memo's key leaves out `types`, but both callers derive it from the key's `probeKey` (`SurfaceFixtures.Get`).
    So no two cases share a baseline they should not.
  - **Proposal (owner decision 4):** keep the memo, which roughly halves the matrix. What protects it is today's
    coverage, not a rule: a new memo-reading test whose coverage shares nothing with another's would open the hazard.
    The same screen could run over every nightly's reports and fail, naming the pair.

## Next steps — the owner's

1. **Enrol in Anthropic's OSS Scanner.** Nothing blocks it now. Since PR #10, master's `.oss-scanner/project.yaml`
   carries exactly the `key: value` lines of the prepared entry. On 2026-10-27, after that merge, upstream
   (`anthropics/oss-scanner`, main at `cae19c8`) had no `projects/dwarfmapper` and no DwarfMapper PR. The kit is in
   `~/.dwarf-tools/osc-enrol/`:
   1. Open the link in `new-file-link.txt`. GitHub forks the repository and fills in `projects/dwarfmapper/project.yaml`;
      commit it and open the PR.
   2. Paste `pr-body.md` (upstream's template) as the description and tick the boxes. Every box is left empty: being
      a core maintainer, accepting the terms and the eligibility claim are the owner's statements. The PR's `terms`
      check stays red until the terms box is ticked.
   3. Sign the CLA with a comment that contains only `I have read the CLA Document and I hereby sign the CLA`.

   Reports then go to `dwarfmapper@seznam.cz` and, through private vulnerability reporting (on since 2026-10-27), to a
   private advisory on this repository, both at every severity. Upstream's own test of "the scanner will likely work"
   passed on 2026-10-27: `tools/check dwarfmapper` (`cae19c8`) built master at `b58c6e3`, and in the finished image,
   offline at the agent's 2 CPUs / 8 GB, the push-time suite passed 9,621 of 9,621 in 154 s, with no out-of-memory
   event. Nothing in README or SECURITY.md may claim the scanner until the enrolment PR is merged.
2. **FusedChat A/B stats** — round 31's scripts in `Issues/round31/fusedchat-stats/`; the owner's tree and machine
   time, so the owner schedules it.

## Next steps — CI

Both items below were the state when the round opened; this PR answered them as A2 (the retry) and A1 (the memory).

- **The first nightly on round 33's code was red only on the dashboard.** Run 40477294097 (`b58c6e3`) started at
  08:58 UTC, 5 h 41 min after its 03:17 cron. Everything round 33 targeted passed: package-size, deep-test, and the
  initial run of all six mutation legs, with scores identical to PR #9's. The pipeline report uploaded, so the 413 is
  gone. Two of the 23 jobs went red, the runtime and codefixes legs, and only at their upload step: the Stryker
  dashboard answered both PUTs (09:09:41 and 09:09:59) with **HTTP 504** four minutes later. Doctooling's upload at
  09:08 and testing's at 09:28 succeeded, and codefixes' report is the smallest of the six, so size is not the cause.
  At 12:30 UTC all six badges showed that run's scores: the 504s cost no displayed number.
  - Offered: re-run the two failed jobs. It only turns the run green.
  - Offered: the upload step retries once on a 5xx. The workflow is the code under test, so its test is a scan in
    SelfValidation, the way `PerfCategoryIsolationScanTests` holds a workflow rule.
- **Memory headroom on the mutation legs** — the open risk that can still kill a nightly. On that run:

  | Leg | Score | Largest test host | Lowest available |
  |---|---|---|---|
  | pipeline | 95.22 % | 8.7 GB | 0.6 GB (09:23:44 UTC) |
  | generator | 97.11 % | 8.1 GB | 1.5 GB |
  | runtime | 97.48 % | 6.8 GB | 3.2 GB |
  | doctooling | 96.55 % | 6.6 GB | 6.3 GB |
  | testing | 100.00 % | 5.9 GB | 7.5 GB |
  | codefixes | 87.64 % | 4.7 GB | 8.8 GB |

  Stryker itself peaked at 0.5-1.5 GB; the test hosts are the risk (round 33, "Generator.Tests' own peak memory").
  The experiments specified there are still open: Generator.Tests without `2f3b290`'s `.AsParallel().AsOrdered()`
  under `DOTNET_PROCESSOR_COUNT=4`; a gcdump at the peak, to find what grows; a GC heap limit for the mutation job's
  test hosts (`DOTNET_GCHeapHardLimitPercent` is read as hexadecimal: 40 % is `28`). Not Stryker concurrency 1, which
  would roughly double the generator leg's run toward its 200-minute timeout.

## Carried from round 33

- **T03 Pre-size the Preserve identity map** — waits on its API surface (owner decision 1).
- **The fast-path audit's untested cells**, in its rank order: `default(ImmutableArray<T>)` as a source (needs owner
  decision 5 before a test can pin it); a FlattenGraph leaf under Preserve; `Nullable<struct>` wrappers losing Preserve
  identity; an array blit under SetNull against a method-level config on the element pair's partial.
- **`2a10fb3`** — the null-element store shape, 1.12x: below the bar, waiting for a quiet-machine confirmation at 100
  and 10,000 elements and the owner's call.
- **Generator cold start** — closed by task C: ~0.8 s per cold FusedChat build, not worth ReadyToRun's costs.
- **T02b** — a widen span map (`void Map(ReadOnlySpan<int>, Span<long>)`) emits a scalar loop, never `Vector.Widen`;
  unmeasured.
- **DWARF066 on a positional record's inherited member** — a known false positive, pinned on purpose by
  `WhenGuardInitializerTests`; `KeepDefault = true` answers it.
- **SECURITY.md "Supported versions"** — still `1.0.2-rc.1`; one line, when the version moves.

## Owed between rounds

- **The de-silencing and exclusion inventory** (standing mandate, 2026-09-03): pragmas, `NoWarn`,
  `ExcludeFromCodeCoverage`, Stryker ignores, skipped tests, swallowed catches and unreported generator conditions.
  Each item without a stated reason becomes a task.

## Owner decisions

1. T03's API surface: a `DwarfRefContext` constructor overload, or a capacity member.
2. `[Reinterpret]` under Preserve: it forces the block copy before the gates the Preserve fix added, so a source
   reached twice becomes two targets. Keep honouring it (today), honour it and report the identity it gives up
   (`180510e`'s shape for `[MapConstructor]`), or refuse it under Preserve.
3. `[Reinterpret]` under AsNull: the same decision on a second axis; a null source array still maps to `[]`.
4. `SurfaceProbe.Baselines` under Stryker: a process-wide memo of generator output, shared across tests by design.
   Task D answered the question: with today's coverage no test can read a baseline built under another mutant. Left
   to decide: whether a nightly gate should hold that.
5. What a `default(ImmutableArray<T>)` source maps to. The block copy, the widen and the share answer empty; the
   general path enumerates `src.GetValueOrDefault()` and should throw.
6. The in-process mutation runner (E, "Measured, rig only"). It reproduces Stryker's statuses on the two generator
   legs at a fifth of the time, and it is a rig. Bringing it into the repository means a tool project, a scan that
   keeps the list of test-side caches complete (decision 4's `SurfaceProbe.Baselines` is one: in one process it does
   leak between mutants, and has to be emptied before each), the catalogue step in CI, the other four legs, and both
   reports side by side until they agree every night. Recommended. The owner, 2026-11-11: "I am leaving you work
   autonomously, meaning I always choose recomended option" - so it is the work of the branch after this one, with
   Stryker's legs staying the gate until the two agree.
7. The three Stryker findings (the whole test list per session, the unordered project bag, the quadratic test
   mapping): written up, the owner's to file upstream.
8. The SDK pin. 10.0.101 is thirteen servicing releases old; the 2026-11-03 advisory was answered for the two
   ahead-of-time samples alone (`ed19b7c`). Moving the pin re-measures every gate tied to it (sizes, allocation pins,
   mutation scores).

Decided and not carried: LICENSE stays as it is; the public contact is `dwarfmapper@seznam.cz`; T01's pin plus canary
landed (`c142578`, `17c5c28`); private vulnerability reporting is on; scanner reports go by email and GitHub advisory
at every severity (PR #10).

## Done in PR #11, and what is left

The round opened with four tasks (A-D). The owner then asked for the whole silenced-checks inventory in the same
PR. Everything that is code landed here:
- tasks A1-A3, B1-B18, C and D above, each commit carrying its RED and GREEN in its message;
- the three reds of the PR's first full-ci run (`fc0ae37`): the Testing package's coverage (B5b), its size (B5c), and
  the Perf lane's ratio test, which had been red on four master nightlies too (A3).

**Left, and why. The owner's settings, not code:**
- **Required status checks** (inventory item 8). `master` requires none, so every gate in this PR, Codecov's
  included, is advisory at merge time. Branch protection is the owner's setting. The checks every pull request runs,
  which a required list would name:
  - CI: `build-test`, `codeql`, `conformance-gate`, `sbom`, `roslyn-forward-compat`, `ubuntu-26-canary`,
    `aot-trim-gate (linux-x64, ubuntu-24.04)`, `aot-trim-gate (win-x64, windows-latest)` (the `surface-matrix` job
    went with E14: the matrix runs in every job's suite);
  - actions-security: `zizmor`;
  - code scanning: `CodeQL`.

  The expensive tier runs only on a `full-ci` PR. A job skipped by its condition reports success, so requiring one
  costs an unlabelled PR nothing.
- **harden-runner block mode** (inventory item 10). Every job audits its egress and none blocks. Block mode needs an
  allow-list built from StepSecurity's recorded baseline, which only the owner's StepSecurity view shows.

Also open, above: owner decisions 1-8, and what was carried from round 33.

**Added to the same PR after its first push (`2c9995d`): task E from E3 on.** The test hosts' runtime settings, the
compile-once and errors-only harness paths, the one suite at one size (E14), the CompilerTests rounds, the three
mutation-leg changes (r5, r6, r7), the folded smoke facts (c1), and the fix for the advisory that had master's
nightly red from 2026-11-04. Its CI changes were rehearsed step by step in the runner's image before the push; the
`full-ci` run on the PR is their first on a hosted runner.
