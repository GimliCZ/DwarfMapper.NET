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
