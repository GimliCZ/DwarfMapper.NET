# Round 34 E — where the long test runs spend their time, and the static mutants that were an artifact (2026-10-28)

**Question (the owner, 2026-10-27).** "Starting tests commonly takes longer than running them": find where the
long-running test work goes, and cut it at the largest surface without reducing coverage.

**Rig.** Round 33's Linux reproduction of a hosted runner, under the heavy-test lock:
- `mcr.microsoft.com/dotnet/sdk:10.0.101` in Docker Desktop (16.7 GB), `--cpuset-cpus 0-3`, so `ProcessorCount`
  reads 4 and Stryker's default concurrency is 2, as on CI.
- Each arm clones the branch from a bundle into a fresh container and runs the mutation job's own command:
  `dotnet stryker --config-file <leg> --break-on-initial-test-failure`.
- A sampler reads `ps` every 0.25 s for test hosts (`testhost.dll`) and `/proc/meminfo` every 5 s.

## Where CI's time goes (run 40538713221, all 23 jobs green)

| Job | Minutes |
|---|---|
| mutation generator / pipeline / testing / runtime / doctooling / codefixes | 76.2 / 72.0 / 29.5 / 16.7 / 10.7 / 10.4 |
| deep-test | 15.5 |
| every other job | 2.6 - 6.6 each |

The six legs are 215 of the run's 273 job-minutes, and the generator and pipeline legs set its wall-clock.

## What a test host costs to start

`dotnet vstest <dll> --TestCaseFilter:FullyQualifiedName=No.Such.Test` (host start plus discovery, nothing run),
6 cores / 12 threads, three runs each:

| Assembly | Start-up |
|---|---|
| a trivial xunit project, one test, same packages | 1.46 s |
| Testing.Tests / CompilerTests / DifferentialTests | 1.49 / 1.50 / 1.57 s |
| IntegrationTests | 2.03 s |
| Generator.Tests | 2.37 s |

About 1.5 s is the framework's own and no test project can shrink it. For small projects (`dotnet test`) start-up is
50-95 % of the run; for Generator.Tests' full suite, 4 %.

Stryker 4.16 starts fresh hosts for every test session (`RunTestsWithCustomTestHost`; it never calls VSTest's
test-session API), and its run settings carry `DisableParallelization=true`, so xunit runs each session serially.
The legs whose mutants are covered only by Generator.Tests (doctooling, codefixes) cost ~2 s a mutant on CI; the
testing and runtime legs, whose mutants are covered across several projects, ~6-13 s. In the container the testing
leg started 251 hosts for 105 mutants, at a median life of 1.8 s.

## The static mutants were an artifact (E1, `2c9995d`)

Stryker tests a "static" mutant against the whole suite. The generator leg had 160 of 415, the pipeline leg 178 of
314: the hot helpers every mapping runs (`IsPrimitive`, `CanReinterpret`, `ConstructorSelector.Select`,
`LocationInfo.From` ...), each run against ~9,700 tests instead of the handful that cover them.

Stryker's coverage collector (`Stryker.DataCollector/CoverageCollector.cs`) takes, at each test's start, whatever ran
since the previous test ended and reports it as covered outside any test; one such report makes a mutant static
(`CoverageAnalyser`). The generator leg's coverage phase at debug verbosity showed exactly five such reports, each
right after one of five theories whose rows xunit could not serialize (four over `MatrixCell`, one over
`FimMatrixCase`). Each ran as ONE test case reporting a result per row (1,919 extra results), the collector's window
closed at the first, and rows two to N ran outside every window.

E1 makes each row its own test case: the theory passes a key (`MatrixCell.Key`, the golden corpus's own unique id;
`FimMatrixCase.Id`) and the test resolves it. Same cells, same assertions. `TheoryRowsAreTestCasesScanTests` holds
every theory parameter to a type xunit serializes (RED on exactly those five).

| Coverage phase (generator leg) | before (`910d41f`) | after (`2c9995d`) |
|---|---|---|
| reports of mutations run outside any test | 5 | 0 |
| extra results | 1,919 | 0 |
| test cases Stryker sees | 8,728 | 10,654 |
| mutants to be tested against all tests (every mutant Stryker created) | 6,379 | 804 |

| Generator leg, container | Stryker time | Score | Static among tested | Lowest available |
|---|---|---|---|---|
| before, concurrency 2 | 1:14:03 | 97.11 % (403 / 12) | 160 | 11,275 MB |
| after, concurrency 2 | 34:01 | 97.11 % (403 / 12) | 0 | 6,248 MB |
| after, concurrency 4 | 27:39 | 97.11 % (403 / 12) | 0 | 4,752 MB |

No mutant's status differs between the three runs: the 160 were killed by tests that cover them, and now only those
tests run. The 54 % cut costs no coverage.

It is also the start-ups. A static mutant runs the whole suite, so it starts a host in every test project:

| Generator leg, container | Hosts started | of which NegativeCases / IntegrationTests / DifferentialTests | Mean hosts alive |
|---|---|---|---|
| before, concurrency 2 | 1,038 | 194 / 163 / 163 | 2.4 |
| after, concurrency 2 | 277 | 29 / 3 / 0 | 4.2 |
| after, concurrency 4 | 286 | 33 / 0 / 3 | 6.3 |

After E1 nearly every host is a short Generator.Tests session for the tests that cover the mutant. The memory floor
fell because hosts outlive their session: a few CompilerTests hosts lived up to 25 minutes.

The runtime leg's static mutants are a different kind and stay: its coverage phase shows three reports of registry
code (`Register`, `RegisterMany`) run outside any test, when a test assembly loads and its generated module
initializers register their maps. That is the product's self-registration doing its job before any test starts.

## Concurrency (container, testing leg)

The guard against raising Stryker's concurrency rests on one 2026-08-26 local run (cores-2, the generator leg past its
fuse) whose premise was that xunit parallelises inside each worker; Stryker's run settings forbid that, and in
August each Generator.Tests host still held gigabytes of native memory (round 34 A1). Re-measured with the owner's
approval:

| Testing leg | Mutation phase | Score | Timeouts | Lowest available |
|---|---|---|---|---|
| concurrency 2 | 1,019 s | 100 % | 5 | 12,046 MB |
| concurrency 4 | 780 s | 100 % | 4 | 12,086 MB |

One mutant moved Timeout → Killed; every other status matched. With the generator leg above (34:01 → 27:39, 0
timeouts either way), concurrency 4 is 19-23 % faster at no cost in score, at a lower memory floor.

## What a warm host would need

Stryker's MTP runner (preview since 4.13, `--test-runner mtp`) keeps the test host alive across mutants. It needs the
suite on Microsoft.Testing.Platform (xunit v3), and per its announcement it "cannot yet filter individual test cases
per mutant", so every covered mutant would run every test. Not usable until it can.

## What a test spends its time on (E3)

The owner's follow-up: "Mapper is extremely fast, for that reason most of the time is startup"; aggregate the kinds of
tests and prepare their spin-ups in front. Measured first, on one combinatorial cell at a time (Generator.Tests'
harness, Windows, a host that had compiled ten cells before):

| Phase of one cell | ms |
|---|---|
| parse + compilation object | 0.25 |
| generator run | 5.5 |
| parse check of the generated files | 1.6 |
| emit | 17.8 |
| `Assembly.Load` | 0.2 |
| compiler diagnostics | 1.0 |

The mapper itself runs in microseconds; the cost is Roslyn's. The same `EmitAssembly` in a host that had run 2,400
compilations before took 7.3 ms, not ~25 ms, so the question was why a test host runs Roslyn slowly for so long.

**The NuGet package's Roslyn is IL only** (the SDK's own copy is ReadyToRun), so every test host JIT-compiles the
compiler before its first generator test: a host's first four generator tests cost 1.1 s more than a symbol-only test.
And every generator test emits and loads a fresh assembly, so the host compiles new methods at tier 0 all the time.
The runtime holds back call counting, and with it every tier-1 recompile, while that start-up-like activity goes on
(`TC_CallCountingDelayMs`, 100 ms), so Roslyn stayed on unoptimised tier-0 code for long stretches.

Generator.Tests under runtime settings set by environment variable, `dotnet vstest` on one Release build: the full run
(8,097 tests, `Category!=SurfaceMatrix&Category!=Perf`) and a serial 226-test session
(`DisableParallelization=true`, the shape of every Stryker session). In-host figures from a temporary probe read at
process exit (JIT time, GC pause). Two runs each, 06:55-07:08, before the owner started a game:

| Setting | Full run | CPU | JIT | GC pause | Serial session |
|---|---|---|---|---|---|
| runtime defaults | 55.3 s | 300 s | 19.1 s | 10.7 s | 7.6 s |
| `TC_CallCountingDelayMs=0` | 38.8 s | 195 s | 51.5 s | 10.8 s | 6.5 s |
| `TieredPGO=0` | 47.9 s | 234 s | 15.8 s | 10.7 s | 7.0 s |
| `gcConcurrent=0` | 49.8 s | 263 s | 15.6 s | 5.7 s | 7.4 s |
| `GCgen0size` 64 MB | 51.3 s | 277 s | 16.2 s | 6.2 s | 7.5 s |
| `TieredCompilation=0` | 33.0 s | 126 s | 29.2 s | 9.2 s | 8.7 s |
| `ReadyToRun=0` (control) | 64.8 s | 325 s | 21.8 s | 11.1 s | 10.3 s |

Combinations, one run each beside a defaults run of their own (07:16-07:22; the game started at about the end):

| Setting | Full run | CPU | Serial session |
|---|---|---|---|
| runtime defaults | 62.8 s | 288 s | 8.1 s |
| call counting at once + no PGO | 34.3 s | 154 s | 5.7 s |
| **+ no background GC (adopted)** | **29.8 s** | **140 s** | **5.5 s** |
| tiering off + no background GC | 27.6 s | 122 s | 8.8 s |
| call counting at once + no PGO + server GC | 26.7 s | 141 s | 7.0 s |
| Roslyn precompiled (ReadyToRun), defaults | 47.7 s | 233 s | 6.3 s |
| Roslyn precompiled + the adopted three | 27.2 s | 137 s | 5.0 s |

**Adopted (`35f1a20`):** every test project's runtimeconfig.json carries
`System.Runtime.TieredCompilation.CallCountingDelayMs=0`, `System.Runtime.TieredPGO=false` and `System.GC.Concurrent=false`
(Directory.Build.targets), so dotnet test, the IDE, coverage runs and Stryker's sessions all get them. The runtime reads
them from there: on the serial session with no `DOTNET_` variable set, JIT 7.4-7.5 s and 10 gen-0 GCs; with
environment variables forcing the defaults back on the same build, JIT 3.0-3.2 s and 66-67 gen-0 GCs (the defaults'
signature: 2.8 s, 67). Peak working set rises ~100 MB per host (non-concurrent GC grows a larger gen 0).

**Kept at defaults:** IntegrationTests (`DwarfKeepRuntimeDefaults`). Its oracles measure the runtime itself:
`InterfaceBucketTests` (Category=Perf) times interface dispatch, which PGO decides how to compile, and the allocation
contracts count bytes that optimised code may keep off the heap. Every test that reads the allocation counters lives
there. `PerfCategoryIsolationScanTests` holds every project with such an oracle to the opt-out.

**Rejected, with the measurement:**
- Tiering off altogether: fastest full run, but the serial session got slower (8.7 s, 8.8 s), and every Stryker
  session is serial.
- Server GC: slower serial session (7.0 s) and a heap per core in each of Stryker's hosts.
- Roslyn precompiled ReadyToRun: a further ~9 % on both shapes, for a crossgen2 step over ~10 MB of IL in every
  clean build, and CI builds clean in every job.
- A fixed assembly name, to reuse the harness's bound reference set (`EmitAssembly` names each compilation uniquely):
  7.13 vs 7.29 ms per cell in a warm host. Roslyn already shares the referenced assemblies' symbols.
- Several cells in one compilation, one generator run, one emit: 16.4-17.6 ms per cell at 10 cells, 16.9-20.8 at 40,
  26.2-26.5 at 120 (two runs). The assembly-wide files (registration, extensions) grow with every cell, and the cells
  would share an assembly again, which is what E1 separated.

**"Prepare the spin-ups in front", answered.** Generator work done ahead of a test, in a fixture or in a cache shared
between tests, runs outside every test's coverage window, and Stryker then classifies each mutant it touches as
static and runs it against the whole suite. That is the artifact E1 removed (160 mutants, 1:14:03 -> 34:01 in the
container). Stryker also activates mutants per test inside one host, so a result cached by one test would answer for
another test's mutant. The safe place to prepare is the runtime: E3 does that.

**Aggregating test projects.** A host costs ~1.5 s of framework start-up before any test (above), and `dotnet test`
starts the projects' hosts in parallel; after E1 almost every Stryker session runs only Generator.Tests. Merging
projects would save at most one host start per project and run, so it is not pursued.

### E3 before and after, on this machine (2026-10-28, 07:55-08:34)

After the owner's game closed (a sentinel polling every 15 s logged none during the runs). One Release build of
`35f1a20`; the defaults arm forces the runtime's defaults back with environment variables, which override
runtimeconfig.json (`DOTNET_TieredPGO=1`, `DOTNET_TC_CallCountingDelayMs=100`, `DOTNET_gcConcurrent=1`). The arms
alternate.

| Run | Defaults | E3 | Change |
|---|---|---|---|
| build-test's run, all nine test assemblies, 3 each | 68.3 / 67.8 / 66.5 s | 36.9 / 37.8 / 39.1 s | -44 % |
| surface matrix, 3 each | 27.0 / 27.0 / 26.5 s | 13.8 / 14.0 / 13.8 s | -48 % |
| build-test's run with coverage, as CI runs it | 71.8 s | 41.4 s | -42 % |
| deep tier (`DWARF_DEEP=1`, `Category!=Perf`) | 173.7 s | 93.5 s | -46 % |
| power set (`DWARF_FUZZ_FULL=1`, 262,144 compilations in both) | 10:27 | 8:05 | -23 % |
| Perf (IntegrationTests, which keeps the defaults) | | passes | |

The test counts are the same in both arms: 9,685 tests in build-test's run, 18,775 in the deep tier. The defaults
arm fails exactly one, `TestHostRuntimeTuningTests`' GC check, which sees the background GC come back.

Within build-test's run, Generator.Tests went from 57.7 to 29.0 s. IntegrationTests went from 6.0 to 9.3 s (deep
tier: 21 to 33 s) although its settings are the same in both arms: the assemblies run in parallel, and early in the
run it now shares the CPU with the tuned hosts' tier-1 JIT threads.

**Coverage is unchanged, line for line.** The coverage files merged by absolute source path, the way ReportGenerator
and Codecov merge them:

| Package | Lines covered (both arms) | Branches covered (both arms) |
|---|---|---|
| DwarfMapper.Generator | 14,249 / 14,258 | 8,138 / 8,154 |
| DwarfMapper.Testing | 851 / 851 | 670 / 670 |
| DwarfMapper (runtime) | 394 / 406 | 112 / 112 |
| DwarfMapper.CodeFixes | 465 / 472 | 292 / 302 |
| DwarfMapper.DocTooling | 444 / 446 | 242 / 244 |

No line and no branch point is covered in one arm and not the other. Within a single test project's file one line
does differ: Generator.Tests' own hit on `GraphOracleComparer.cs:884` (a sort comparator's arm for a null second
element). Over three runs per arm it was hit 4, 4 and 0 times with E3 and 0, 0 and 4 with the defaults: a coin toss
in both. String hashing is randomised per process, so a set's enumeration order, and with it the comparisons a sort
makes, changes from run to run. Testing.Tests covers the line in every run.

### E3 on the generator leg (container, 2026-10-28)

The rig above, the CI job's own Stryker command, concurrency 2, the commit before E3 and E3 itself, one after the
other on the same day:

| Generator leg | Before E3 (`95c7433`) | E3 (`35f1a20`) |
|---|---|---|
| Stryker time | 51:32 | 35:41 (-31 %) |
| mutation testing (after the initial run and coverage capture) | 43:00 | 27:36 (-36 %) |
| initial run and coverage capture | 7:09 | 6:41 |
| score | 97.11 % (403 killed / 12 survived) | 97.11 % (403 / 12) |
| mutants whose status differs | | 0 of 867 |
| lowest available memory | 7,301 MB | 6,885 MB |

Early in mutation testing E3's sessions ran more than twice as fast: 61 Generator.Tests sessions in the first seven
minutes against 27. The same commit ran 34:01 on the evening of 2026-10-27, against 51:32 on this morning's machine;
that run's statuses match this morning's mutant for mutant, so the setup repeats and only the machine's speed moved.
Arms are compared within one session for that reason.

## Where a mutation session's time goes, and the two changes it led to (E5, E4)

**Repeated compilations.** A temporary probe in `GeneratorTestHarness.BuildCompilation` hashed every compiled input
(source, nullable context, unsafe) and the test class that compiled it:

| Generator.Tests | Compilations | Distinct inputs | Repeats |
|---|---|---|---|
| build-test's run | 14,332 | 6,992 | 7,340 (51 %) |
| surface matrix | 5,868 | 942 | 4,926 (84 %) |
| deep tier | 36,605 | 16,153 | 20,452 (56 %) |

Most repeats are inside one test: `GeneratorAssert`'s helpers run the generator twice per call (once for its
diagnostics, once with both generators for the compile errors), a combinatorial cell's row runs it for the
diagnostics and again to emit, and `SurfaceProbe` and `OptionProbe` recompute their matrices in several tests. Some
repeats are the point: the determinism tests compile twice to compare.

**The tests that cost the legs most.** Each covering test's duration (a serial Debug run, Stryker's own shape) times
the number of tested mutants it covers, in the CI reports of run 40637130998, summed per class. This is an upper
bound: Stryker stops a killed mutant's session at the first failure.

| Class | Generator leg | Pipeline leg |
|---|---|---|
| `CombinatorialEngineTests` | 11.0 min | 4.6 min |
| `FeatureCombinationFuzzTests` | 6.5 | 6.2 |
| `GeneratedCodeIsWarningFreeTests` | 5.2 | 2.1 |
| `EmittedCodePolicyScanTests` (one fact over 1,021 cases) | 3.9 | 4.4 |
| `GoldenCorpusTests` (one fact over the same cases) | 3.8 | 4.3 |
| `GeneratedDocsAreCurrentTests` | 2.1 | 1.3 |

**E5 (`c81e014`): one row per golden case.** The golden manifest check and the banned-API scan were facts that each
ran the generator over all 1,021 cases. They are one row per case now, running the generator once for both; the
corpus-wide checks that run no generator stay facts. `CorpusWideSweepScanTests` holds the project to it (red on
`3204dc6` with three sweeps named). Coverage unchanged line for line. On the generator leg it gained nothing measurable:
46 of the 153 mutants that reach golden rows reach nearly all 1,021, and for them a thousand small rows cost about what
the two loops did. Kept for what it does buy: half the golden generator runs, and a failure named by its case.

**E4: the tests that kill the most run first.** Stryker stops a session at its first failing test, and xunit's
default order is arbitrary, so a mutant covered by thousands of tests often ran most of them before its killer; the
40 Generator.Tests sessions over 20 s took 54 % of the session time. `KillersFirstOrderer` orders the collections, and
the tests within each, by the kills `MutationKillRanking.tsv` records for them (`scripts/kill-ranking.py` writes it from
the mutation reports). The order changes no verdict.

| Generator leg, container, concurrency 2, 2026-10-28 | Stryker time | Mutation testing | Score | Statuses changed |
|---|---|---|---|---|
| E3 (`35f1a20`) | 35:41 | 27:36 | 97.11 % | |
| E3 + E5 (`c81e014`) | 37:26 | 29:11 | 97.11 % | 0 of 867 |
| E3 + E5 + E4 | 28:25 | 20:25 | 97.11 % | 0 of 867 |
| E3 + E5 + E4, concurrency 4 | 27:38 | 19:18 | 97.11 % | 0 of 867 |
| before E3 (`95c7433`), for scale | 51:32 | 43:00 | 97.11 % | |

Run-to-run drift on this machine is large (the same commit ran 34:01 one evening and 51:32 the next morning), so a
difference of a few percent between arms is not an effect; E4's -30 % on the mutation phase is.

**Concurrency 4 no longer pays (E2 not applied).** Before E3 and E4 it bought 19-23 %. On top of them it bought 3 %
of the leg (-5 % of mutation testing), inside this machine's drift, while the lowest available memory fell from
5,127 MB to 3,339 MB. A session is short now and runs its own tier-1 JIT on background threads, so two sessions already
keep four CPUs busy. CI keeps Stryker's default (half the logical processors).

## Compiling less, and collecting in parallel (E7-E11, server GC)

### E7 and E8 before and after (2026-10-28, 12:52-13:18)

`93b1bb4` (a detached worktree) against `3f430d4`, alternating, one Release build each. E7 (`3a0a03c`): a check that
needs one source's generator output compiles that source once. E8 (`3f430d4`): the checks that read only errors, and
the emits, bind without Roslyn's nullable walker.

| Run | Before | After | Change |
|---|---|---|---|
| build-test's run, all nine test assemblies, 3 each | 37.2 / 36.8 / 41.0 s | 36.6 / 34.4 / 32.0 s | -10 % (means 38.3 → 34.3 s) |
| surface matrix, 3 each | 13.7 / 14.7 / 14.8 s | 13.1 / 14.6 / 12.4 s | -7 % |
| deep tier (`DWARF_DEEP=1`, `Category!=Perf`) | 88.5 s | 76.2 s | -14 % (Generator.Tests in the host 1:17 → 1:04) |
| power set (262,144 compilations in each) | 7:46 (7:43 in the host) | 6:22 (6:19) | -18 % |
| power set, server GC on top | | 5:08 (5:05) | -34 % against before |

The test counts match, apart from the five tests E7 and E8 add.

### E9: the surface matrix's counted populations from one sweep (`500b5f2`)

Nine facts each classified the matrix for themselves; one fact now sweeps once and runs all nine checks over it.

| Surface category, 3 alternating runs each | E8 | E9 | Change |
|---|---|---|---|
| wall | 12.8 / 12.2 / 12.2 s | 8.5 / 8.4 / 8.4 s | -32 % |
| in the host | 10 / 9 / 9 s | 5 / 5 / 5 s | -45 % |
| tests | 978 | 970 | nine facts are one |

### E10: what the GC pays for (`98e0d9c`)

The owner asked whether the suite should release memory itself rather than lean on the GC. A temporary timer probe in
the deep tier (Generator.Tests alone), E8 against E9 + E10; there is one clean run before, because the other crashed on
the probe itself:

| | Before | After (2 runs) |
|---|---|---|
| total GC pause | 12.4 s | 11.6 / 11.1 s |
| allocated | 31.5 GB | 29.4 / 29.3 GB |
| live after the last full collection | 430 MB | 444 / 419 MB |
| peak working set | 1,444 MB | 1,349 / 1,353 MB |
| emitted assemblies loaded | 5,431 | 5,431 |
| time in the host | 51 s | 50 / 48 s |

The harness cached a baseline compilation for every unique assembly name (5,319 in the deep tier, none read again).
E10 caches from a name's second request, and the live heap did not move: the entries were small. The GC pays for
allocation, about 30 GB per deep run, which stops the whole process for 11-12 s of a 50 s run. Nothing the tests hold,
or could dispose, changes that. Compiling less does (E9 and E10 together took 2.2 GB off), and so does collecting in
parallel.

### Server GC on the parallel tiers

Deep tier, Generator.Tests alone, after E10, two runs each, alternating, on this machine's 12 logical CPUs:

| | Workstation (committed) | Server GC | Server GC, DATAS off |
|---|---|---|---|
| wall | 53.1 / 53.5 s | 44.5 / 45.4 s | 44.4 / 44.2 s |
| in the host | 48 / 48 s | 39 / 40 s | 39 / 39 s |

-16 % wall, all 18,178 tests passing; turning DATAS off adds nothing. Server GC stays out of the runtimeconfig, because a
serial Stryker session is slower under it (5.5 → 7.0 s, the E3 table above).

On four CPUs, a hosted runner's shape (the container rig, `8c98435`, two runs each, alternating):

| Generator.Tests | Workstation | Server GC | Change |
|---|---|---|---|
| deep tier, wall | 76.5 / 83.4 s | 66.2 / 70.2 s | -15 % |
| deep tier, in the host | 71 / 78 s | 61 / 65 s | -15 % |
| build-test's run, wall | 35.6 / 34.1 s | 32.3 / 36.5 s | none measurable |

Both arms fail the same two `EmittedIlIsVerifiableTests`: the container has no `ilverify` tool. So server GC is set where
it measured a gain and nowhere else (E13): CI's `fuzz-exhaustion` job sets `DOTNET_gcServer`, and housekeeping sets it
for stage 1 under `-Deep` (which CI's `deep-test` job runs) and for the exhaustion stage, and puts it back before the
stages that time code or run Stryker. `ServerGcScopeScanTests` holds all three places, and no test project's
runtimeconfig may turn it on.

### What bounds a parallel run

The span of every class (first start to last end) in each run's results file, against the run's length:

| | Run (in the host) | Summed test time | Spread evenly over 12 threads | The class that ends last |
|---|---|---|---|---|
| build-test's run, Generator.Tests | 21.0 s | 192.9 s | 16.1 s | `CombinatorialEngineTests`: 923 tests, 20.1 s in one class |
| deep tier | 48.5 s | 380.6 s | 31.7 s | `FeatureCombinationFuzzTests`: 3,566 tests, 47.8 s in one class |

xUnit runs a class's tests one after another ("By default, there is a test collection per test class"; "tests within a
single test collection will not be run in parallel against each other"), so a heavy class sets a run's length. The
deep tier's last 14 s ran that one class alone.

**E12 (`b27ab09`): the heaviest theories' rows dealt across classes.** Seven theories are split by `TheoryParts` into
classes named after them with a part number (four parts for the feature combinations, the all-emit-paths seeds, the
identity cells and the warning-free cells; two for the compiles-always, behavioural and round-trip seeds). The
killers-first ranking ranks a part as the class it was split from. Alternating against E11:

| | E11 | E12 |
|---|---|---|
| Generator.Tests, build-test's run (3 each) | 25.0 / 25.1 / 24.6 s | 24.8 / 23.8 / 21.7 s |
| Generator.Tests, deep tier (2 each) | 45.8 / 47.7 s (41 / 43 in the host) | 42.5 / 43.6 s (37 / 39) |
| the whole solution, build-test's run (2 each) | 30.0 / 30.8 s | 30.0 / 29.8 s |

Smaller than the class spans promised, and the results files say why:
- Generator.Tests' plain run now keeps 10-12 tests running until its last second (19.4 s against 17.1 s for an even
  spread); it is CPU-bound, and ends on `SnapshotSuite`, which the kills-first order starts at 5.7 s;
- in the whole solution Generator.Tests shares the 12 CPUs with eight other test hosts and was CPU-bound before the
  split (21 s in its host in both arms), so only less work moves that run now;
- the deep tier's tail is mid-sized classes the kills-first order starts late (`FlattenGraphFuzzTests`,
  `DeterminismFuzzTests`, `ExtendedBehavioralFuzzTests`, `IndependenceOracleFuzzTests`, between 24 and 29 s). An order
  that starts the longest classes first in a parallel run, and keeps kills first under Stryker (its data collector is
  loaded in the host), would be worth about 5 s there; not done.

### Repeats that remain, and E11 (`8c98435`)

A second probe hashed the source text alone and recorded the test method, at E10:

| Generator.Tests | Compilations | Distinct texts | Repeats of a text | Inside one test method |
|---|---|---|---|---|
| build-test's run | 10,697 | 5,259 | 5,438 (51 %) | 2,042 |
| surface matrix | 1,809 | 942 | 867 (48 %) | 37 |
| deep tier | 26,307 | 11,816 | 14,491 (55 %) | 8,152 |

Three fuzz families ran the generator twice in the test body, the E7 shape one level up:
`Feature_combination_resolves_as_expected` (`Run`, then `EmitsCompilableCode`), `CompilesAlwaysFuzzTests` (`Run`, then
`RunAndGetCompilationErrors`) and `CrossConfigFuzzTests` (`Run`, then `EmitAssembly`). E11 makes one run per row:

| Generator.Tests | E10 | E11 |
|---|---|---|
| compilations, build-test's run | 10,697 | 9,634 |
| compilations, deep tier | 26,307 | 21,280 |
| repeats inside one test method, build-test's run / deep tier | 2,042 / 8,152 | 979 / 3,125 |
| deep tier, 2 alternating runs | 59.1 / 56.2 s (54 / 51 in the host) | 49.2 / 46.5 s (44 / 41) |
| build-test's run, 2 alternating runs | 24.3 / 24.9 s | 24.2 / 24.1 s |

The plain run does not move: its last class to finish is `CombinatorialEngineTests`, which E11 does not touch. Coverage
is unchanged line for line and branch point for branch point.

Left on purpose:
- the determinism and culture tests compile twice to compare;
- `FlattenGraphFuzzTests` and `TopologyOracleFuzzTests` compile one constant source in every row (25-250 rows). The rows
  are separate tests and Stryker switches a mutant on per test, so an emit shared across rows would hide a mutant's
  effect on the emission from every row after the first.

Across classes, a combinatorial cell is compiled by `CombinatorialEngineTests`, `GeneratedCodeIsWarningFreeTests` and
`GoldenCaseTests`, and the fuzz families that share seeds compile the same schema four or five times in the deep tier.
The golden check cannot share the combinatorial row's compilation: `GoldenFingerprint` hashes every emitted file, the
assembly-wide files are named after the assembly, and the emit needs a unique assembly name.
