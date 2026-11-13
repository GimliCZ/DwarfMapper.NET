# Round 34 E, continued — the mutation legs: where the hour goes, what was fixed, and what is left (2026-10-29 to 2026-11-11)

`2026-10-28-round34-e-test-startup.md` ends with the suite at one size and the generator mutation leg at 26:33. This
picks up there. Everything is measured in `mcr.microsoft.com/dotnet/sdk:10.0.101` on four CPUs (`--cpuset-cpus 0-3`,
a hosted runner's shape) unless it says otherwise, from a fresh clone, with no game running beside it. A leg's
statuses are compared mutant for mutant, keyed by span, mutator and replacement, never by score alone.

## CompilerTests, one change at a time (r1, r2, r4)

CompilerTests is the one costly test project that can run ahead of Generator.Tests in a mutation session, and its five
population facts are 76 serial seconds of work. A hill-climb on its wall time: one change per round, best against
candidate interleaved over five repetitions (a commit against itself spans ±5.0 s over three, ±2.5 s over five), every
round with its own RED → GREEN lock and the whole suite's coverage compared line for line.

| Round | Change | CompilerTests, mean of 5 | Paired difference (95 % CI) |
|---|---|---|---|
| r1 `ff278c1` | `RunAndEmit` reads its errors from the emit: one analysis pass per sample, not two | 36.7 → 32.4 s | −4.2 s ± 1.6 s (−11.5 %) |
| r2 `4fee98a` | a sample compiles with `concurrentBuild` off, on the thread that runs it | 32.7 → 31.5 s | −1.1 s ± 0.5 s (−3.5 %) |
| r3 | runtime settings only, on r2: server GC 27.1 s against 31.2 s; a 64 MB gen0 budget, no concurrent GC, no PGO and no tiering each within noise | — | server GC −4.1 s ± 0.4 s |
| r4 `28ca173` | CompilerTests alone runs under server GC (its facts run their samples in parallel even inside a Stryker host) | 33.8 → 29.9 s, CPUs busy 82 → 92 % | −3.9 s ± 0.8 s (−11.5 %) |

Coverage is identical in every round. `SingleAnalysisPassTests`, `SampleCompilationOptionsTests` and
`ServerGcScopeScanTests` hold the three changes.

## Killers first, by what is still alive (r5, `544ea02`)

E4 ranked test classes by how many mutants they had killed. Stryker puts mutants with disjoint covering tests into one
run and ends the run only when every one of them is decided, so a class that re-kills the same broad mutants ran ahead
of the cheap class that alone kills the narrow ones: one run took 269 s for mutants killable in seconds.
`scripts/kill-ranking.py --durations <trx>` now ranks greedily by not-yet-killed mutants per serial second (a min-sum
set cover). On a leg narrowed to `EquatableArray.cs`, both arms on the same project order: mutant runs 3:38 → 0:52,
statuses identical. `KillersFirstOrdererTests` holds the order.

## Stryker draws a test-project order per launch, and one draw doubles a leg

Two legs of the same commit took 20:55 and 48:29 of mutant testing. The cause is in Stryker, tag
`dotnet-stryker@4.16.0` (5.0.0 and master are the same):

- `InputFileResolver.AnalyzeAllNeededProjects` analyses the projects with `Parallel.ForEach` into a `ConcurrentBag`,
  and `TestProjectsInfo.AnalyzerResults` keeps the bag's enumeration order. No option sets it. The solution file does
  not steer it either: four debug logs of one tree began their analysis with four different pairs of projects.
- Every run — discovery, the initial run, the coverage run, each mutant run — visits the test projects in that order,
  one session after another (`MaxCpuCount` 1).
- `VsTestRunner.RunTestSession` hands each session the run's whole test list, not that project's share, so a mutant
  that k projects cover and none kills runs its covering set k times: three sessions of 56 / 83 / 73 s where one long
  and two short ones were expected.
- `ProjectMutator.EnrichTestProjectsWithTestInfo` looks every test up in a list it rebuilds and re-hashes per test:
  92 s of a leg at 19,816 tests.

When CompilerTests is drawn ahead of Generator.Tests, every mutant that Generator.Tests kills pays CompilerTests'
covering run first. The r6 leg below drew it; its 183 mutant runs, by the project that killed:

| Killed by | Runs | Median run | Share of runner time |
|---|---|---|---|
| Generator.Tests | 73 | 60 s | 54.6 % |
| nobody (survivors) | 6 | 237 s | 24.6 % |
| NegativeCases | 77 | 5 s | 7.4 % |
| CompilerTests | 14 | 31 s | 6.9 % |

How often: a mutant both projects kill is credited to whichever ran first, which fingerprints a finished leg from its
report alone. Of the twelve full generator legs on file, three credited CompilerTests with 15 or more kills (the
others 0 to 3). Of five real launches killed during discovery to read their order, three drew it. The two samples
disagree on the rate and agree that it is not rare.

Three findings are written up for Stryker (the whole list per session, the unordered bag, the quadratic test mapping)
and not filed; that is the owner's to send.

## Compile only what a leg mutates (r6, `a0fc32f`, fixed by `99a688e`)

Stryker 4.16 mutates and compiles every source of the project under test whatever `mutate` says, and drops what the
globs exclude afterwards: on the generator leg 16,314 mutants created, 3,808 compile errors rolled back over 28 compile
passes, 12,004 thrown away as "Removed by mutate filter". It skips a source whose first token follows an
`<auto-generated>` comment entirely. `Set-UnmutatedSourcesGenerated` gives every source no glob names that header
(plus `#nullable enable`) in CI's throwaway checkout; sources declaring a `DiagnosticDescriptor` stay unmarked, because
the release-tracking analyzer reads them only from hand-written code.

| Generator leg, same statuses for all four files (29 / 361 / 201 / 34 mutants) | r5 | r6 |
|---|---|---|
| mutate and compile | 2:20 | 0:17 |
| everything before the first mutant run | 11:36 | 10:08 |
| mutants created | 16,314 | 1,428 |
| report | 114 MB | 55.5 MB |

**A defect in r6, found before its first run on a runner.** The same job asserts `git diff --exit-code` after Stryker
("the tracked tree survived the leg"), and the marking edits 74 tracked sources: both legs would have passed Stryker and
gone red at that step every time. The container legs above ran the marking and Stryker and none of the job's later
steps. `99a688e` stages the marking (`git add --update`), so `git diff`, the working tree against the index, compares
against the marked tree and still fails on a stray write (checked: exit 1 before staging, 0 after, 1 again after a
write into a tracked doc or into a marked source). See "Rehearsing a job's real steps" below for how it was shown red
and green.

## Splitting CompilerTests' population facts: measured, rejected

If a mutant is reached by few of a fact's samples, dealing the samples into parts (as E12 did for the heavy theories)
would charge it only those parts. Measured before building anything: the leg's mutated generator, copied out of an
interrupted Stryker run, planted into a scratch CompilerTests whose `PinnedSampling` read Stryker's own
`MutantControl` coverage after each of 60 slices of every population. Each fact's union matched the r6 report's
per-test coverage exactly, and the reach is broad: 53 to 77 % of a fact's covered mutants are reached by every slice.
CompilerTests' seconds over the 73 costly runs: 4,254 as dealt today, 4,067 at four parts (−4.4 %), 3,940 at ten
(−7.4 %). Not built.

## Reroll the bad draw (r7: `2e4670f`, `100e950`, `180acea`, `f46ab7b`)

`Invoke-StrykerRerollingBadOrder` (`scripts/gate-checks.ps1`) launches Stryker unchanged, follows the two projects'
test hosts in the process table, and on the bad draw kills the process tree and launches again; at most three launches,
the last one kept. The draw shows before anything is mutated: Stryker discovers the projects in that order, then runs
them unmutated in it. Discovery hosts are brief (CompilerTests' showed in a single 0.25 s sample; Generator.Tests'
lives about four seconds) and the initial run's sessions long (34 s and 152 s), so a CompilerTests host seen before any
Generator.Tests host rerolls at once, and otherwise the first host alive ten seconds decides. What it cannot read
keeps the run. A launch already on its mutants is never killed (`180acea`): Stryker has by then moved the original
assembly out of the test bins.

- Five real launches on four CPUs, each killed once both hosts had shown: five verdicts right, decided 61 to 81 s after
  launch, nothing left running two seconds after a kill.
- The full generator leg through the launcher: kept its first draw (Generator.Tests first), 31:04, mutant phase 20:44,
  every status identical to the r5 and r6 legs, 97.11 %.
- A focused run with the roles forced so the first launch is always killed: two launches, one report, 29 of 29
  statuses identical to the full leg's.

The same code, by draw: 58:37 (r6, CompilerTests first) against 31:04. The launcher costs a bad draw about 1:10 to
1:40 per reroll.

`ProjectOrderRerollTests` drives the real functions through `pwsh` with a fake Stryker and a scripted process list.
Its hosts are scheduled in the launcher's own polls, not in seconds (`100e950`): in seconds, a 0.15 s host was missed
whenever a poll came late, and with the poll slowed to 5 s the old schedule kept the bad draw (`launches=1` where 2
was asserted) while the new one passes all cases. The question "has this launch started on its mutants?" is also
asked the way the launcher really asks it, over a test bin with and without Stryker's backup file (`f46ab7b`): until
then every case had injected its own answer, and a default that answered "begun" to everything would have left the
launcher keeping every bad draw on a runner with the battery green. Planted both ways (always begun, never begun),
each turns one of the new cases red.

## What a leg costs without Stryker's per-run machinery (an in-process runner; rig only, not in the repository)

The owner, 2026-11-01: "we are searching order of 10x issue ... We need like 90 % improvement". Rerolling removes a
penalty; it does not make a good draw faster. From a leg's report and the tests' own serial durations, what the tests
themselves cost: the pipeline leg's 299 killed mutants need about 2 s of their cheapest killers in all, its 15
survivors about a minute of covering tests, plus one coverage pass of the suite — five to eight minutes of test work
in a leg that took 131.9 minutes on master's nightly. The rest is a fresh VsTest session and test host per project per
run, 18,000 test cases resolved again each time, cancel latency, and the whole-list re-runs above.

A runner built to measure that floor: Stryker still builds the mutated assembly and names the mutants (a 50 s run whose
test filter covers none of the leg's mutants, so it stops before any mutant run). The runner then loads each test
project once per worker with xunit's own runner library, switches Stryker's `MutantControl.ActiveMutant` per mutant
in-process, runs that mutant's covering tests cheapest first and stops at the first failure. It writes Stryker's
report shape.

| Leg, four CPUs, same tree (`72bb006`) | Stryker 4.16 | In-process, one worker | In-process, four workers |
|---|---|---|---|
| pipeline (314 tested mutants) | 21:53 | 5:12 | 3:40 |
| generator (415 tested mutants) | 31:04 good draw, 58:37 bad | 7:45 | 6:55 |

Statuses identical to Stryker's own report on the same tree: 435 of 435 mutants on the pipeline leg (95.22 %), 625 of
625 on the generator leg (97.11 %).

At this PR's head (`180acea`: the smoke facts folded, and CompilerTests' mutant stage on one worker, because its
facts already use every core and four workers took 198 s where one took 171 s) the generator leg runs in 5:31, the
same 403 killed and 12 survived, every status still Stryker's.

It took four corrections to get there, each caught by the comparison with Stryker's report, each of a kind any
in-process runner has to answer:

1. It must start the way a VsTest host does, from the test project's folder with its runtimeconfig and deps file.
2. The harnesses build their Roslyn reference set from the assemblies the process has loaded; a test host has loaded
   about 65 more than a bare runner (`System.ComponentModel` among them: without them 11,286 of 18,188 test cases
   failed their unmutated run, most on CS0012).
3. Verify allows one verification per snapshot path per process; its own switch turns that check off, not the
   comparison.
4. `SurfaceProbe.Baselines` keeps generator output across tests; it has to be emptied before each mutant.

Before 3 and 4 the runner killed five proven equivalents falsely; after them, none.

What is left is test work on four cores: Generator.Tests' coverage pass (about 95 s), and the generator leg's seven
equivalent mutants, each run against CompilerTests' whole populations (about 171 s). Past that needs either results
reused when nothing they depend on changed, which the owner's "run every test" rule excludes, or more cores.

Putting it into the repository is not done and is the owner's decision: a tool project, a scan that keeps the list
of test-side caches complete, the catalogue step in CI, and both reports side by side until they agree every night.

## Simplified tests folded into the full ones (c1, `6f6e00f`)

The owner, 2026-11-01: "We still want to speed up full scale tests, not their simplified versions. We want to colaps
simplified ones, because surface testing is not enough for active development."

`TypeGraphSmokeTests` held two simplified versions of full-scale oracles over the same cases. Its K0 fact ran the
must-compile-or-refuse check alone over 250 sampled graphs; `DifferentialOracleTests` (K1) runs 1,000, starts each
with that same check, then emits, executes and compares. Its projection fact did the same against
`ProjectionAgreementTests`' 1,000. A test is folded only if all four of these hold, and here they do:

| Check | Result |
|---|---|
| same cases | graph k of the pair generator is graph k of the graph-only one: 1,000 of 1,000 identical at the pinned seeds, the first 250 among them |
| same contract first | both full facts assert compile-clean-or-refused before anything else; their vacuity floors are the smoke's |
| mutants reached | K0's 179 within K1's 194; the projection smoke's 194 are the agreement's 194 (generator leg's assembly); identical sets of 47 on the pipeline leg's |
| mutants killed | their one recorded kill (r6's mutant 829) is killed by K1 alone and by the agreement alone; no status of either leg depends on CompilerTests |
| lines and branches | CompilerTests with and without them: the same 5,866 generator lines and 2,908 branch conditions, 0 lost |

Measured on four CPUs, the tree without the fold against the tree with it, five interleaved repetitions:

| | Without the fold | With it |
|---|---|---|
| CompilerTests, wall (mean of 5) | 28.2 s | 24.9 s |
| paired difference (95 % CI) | | −3.3 s ± 1.5 s (−11.7 %) |
| CompilerTests, summed test time | 71.9 s | 60.2 s |
| the whole suite as build-test runs it, coverage collected | 129.2 s | 128.8 s |
| the whole suite's coverage, every assembly | | identical: no line lost or gained, no line with fewer branch conditions hit |

The suite's own length does not move, because CompilerTests is not its longest host. The gain is where CompilerTests
is paid in full: a mutation run that reaches it, above all the generator leg's seven equivalent mutants, each of which
runs every CompilerTests fact that covers it. Through the in-process runner, one worker, that stage went from 171 s
to 137 s (−20 %), with no status changed.

## What did not help

- **Batching power-set subsets into one compilation.** 512 subsets cost 2.65 ms each compiled one at a time, 2.65 ms
  in batches of 16, 2.78 in 64s and 3.47 in 256s, with the same 0 errors: the cost is binding the generated code, and
  it grows with the code. The power set stays 262,144 compilations.
- **One knob in Generator.Tests.** Its 554 s of summed test time is spread over 514 classes, none above 5 %.

## The advisory that turned every restore red (`ed19b7c`)

Master's nightly failed in 18 of 23 jobs on each of eight nights, 2026-11-04 to 2026-11-11, at the first step of
nearly every job: `error NU1903 ... 'Microsoft.DotNet.ILCompiler' 10.0.1 has a known high severity vulnerability`
(GHSA-p3jp-9v83-76w9, published 2026-11-03; fixed in 10.0.14). `PublishAot` makes the SDK reference that package at
the runtime patch the SDK shipped with — 10.0.1 for the pinned 10.0.101 — and the audit in `Directory.Build.props`
fails a restore on any advisory, as it is there to. Nothing in the repository had changed.

`Directory.Build.targets` now gives a `PublishAot` project on net10.0 `RuntimeFrameworkVersion` 10.0.15, the SDK's own
property for the runtime patch a project is published against, from which it also takes the compiler pack. The SDK
pin stays.

| Tried on the pinned SDK | Restore | `publish -warnaserror`, run |
|---|---|---|
| `RuntimeFrameworkVersion` | passes the audit | builds; "AOT gate: all checks passed." on win-x64 and linux-x64 |
| explicit `PackageReference`, version in `Directory.Packages.props` | passes | the package's own targets warn "Delete explicit 'Microsoft.DotNet.ILCompiler' package reference": an error under the gate |
| updating the SDK's `KnownILCompilerPack` item | MSB4191 in the form that touches net10.0 alone | — |

CI's first step, `dotnet restore DwarfMapper.NET.sln --locked-mode`: exit 1 with 2 × NU1903 → exit 0, on Windows and
in the container. `AotCompilerPatchScanTests` holds every `PublishAot` project's lock file to that patch and fails
when the SDK pin reaches it. The price is stated in the build file: the managed build of such a project asks for
runtime 10.0.15, so running one without publishing needs it installed; every gate publishes and runs the native
binary. The SDK pin itself is thirteen servicing releases old, and moving it is a decision of its own.

## Rehearsing a job's real steps

r6's defect got as far as it did because the rig ran what mattered and not the job. `ci_job.py` (rig) reads the
workflow and runs one job as a runner would: each `run:` step as written after `${{ }}` substitution, in order, with
its `if:`, its env and its shell, a container to itself; `uses:` steps are listed as skipped, and anything it does not
understand stops it. For the mutation job the generator leg is narrowed to one file and three cheap test classes, so
the whole job takes about three minutes.

| Mutation job, generator leg | Steps 4–9 (config, tools, marking, Stryker, vacuity) | Step 10, `git diff --exit-code` | Job |
|---|---|---|---|
| r6 as committed (`ed19b7c`) | exit 0; 77 of 81 sources marked; 21 scoreable mutants | exit 1, the marker lines in the diff | RED |
| marking staged (`99a688e`) | exit 0 | exit 0; 77 files staged, none unstaged | GREEN |

The red arm's Stryker step also rerolled for real inside the real step — attempt 1 killed on a CompilerTests-first
draw, attempt 2 kept — and still ended with one report.

Every job whose definition or inputs changed since this PR's last run on a runner (`2c9995d`, all 28 checks green)
was then rehearsed the same way before the push, at `180acea`, four CPUs:

| Job | Rehearsed as | Result |
|---|---|---|
| `mutation`, generator leg | narrowed to one file and three test classes | green, 2:09 |
| `mutation`, codefixes leg (no marking, no reroll launcher: the launch step's other branch) | whole | green, 9:53 |
| `build-test` | whole | green, 2:51 |
| `deep-test` (`housekeeping.ps1 -Nightly`) | whole | green, 14:03: restore, suite, coverage floors, ILVerify, benchmark smoke and allocation pins, NoWarn audit |
| `fuzz-exhaustion` (the power set, server GC) | whole | green, 11:53 |
| `perf-tests` | whole | green, 0:59 |
| `aot-trim-gate`, both RIDs | its three `run:` steps by hand, linux-x64 in the container, win-x64 on this machine | green |

What a rehearsal cannot show: the `uses:` steps (checkout, the SDK install, Codecov and artifact uploads), macOS, and
the hosted image itself. It has to be given what `actions/setup-dotnet` gives a job: the first `deep-test` rehearsal
went red in the ILVerify stage, "no net10 ref pack found", because the SDK container has `dotnet` on `PATH` as a
symlink and no `DOTNET_ROOT`, where a runner has `DOTNET_ROOT=/usr/share/dotnet` (read from the last green nightly's
log). With that variable set, the stage passes. The lookup itself is left as it is; it would also miss the pack on a
machine whose `dotnet` is a distribution's symlink with no `DOTNET_ROOT`, and says so loudly when it does.

A forced reroll on real Stryker at `f46ab7b` (roles forced so the first launch is always the bad draw) exercised
the launcher's own answer to "has mutation begun?": attempt 1 killed and drawn again, two launches, two output folders,
one report.

The first run of the suite without `global.json` (under SDK 11.0.100) found one more: two jobs delete that file to
run on a newer SDK, and `AotCompilerPatchScanTests` read it unconditionally. It now has nothing to compare there and
says why.
