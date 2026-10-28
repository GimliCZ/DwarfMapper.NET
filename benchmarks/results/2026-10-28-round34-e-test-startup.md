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
