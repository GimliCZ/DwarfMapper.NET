<!-- SPDX-License-Identifier: GPL-2.0-only -->

# E3 / E1 — scoping the runtime mutation leg

Branch base: `7fe1b80` (`feat/surface-coverage-architecture`). Stryker 4.16.0, 12 logical cores, Windows.

All E3 numbers below are re-parsed out of process from the existing
`StrykerOutput/2026-08-16.18-50-35/reports/mutation-report.json` (14 MB). **No re-run was needed for E3.**
That report is confirmed to be the runtime leg's: its statuses reproduce the config comment's recorded figures
exactly — 68 Killed + 11 Timeout = **79 detected**, 32 Survived + 7 NoCoverage = 39 undetected,
**118 scoreable**, 8 CompileError excluded → **66.95 %**.

---

## E3 — does scoping lose a kill?

### Answer: YES for the list as given, NO for the list the data actually supports

The task supplied seven "intentional" classes. Derived from the report instead, **23 distinct test classes do
real killing** (127 distinct killer tests). All seven named classes do kill — none was wrong — but the list is
**missing 16 classes**, and filtering to only those seven would have **lost 18 killed mutants outright**
(79 detected → 61, i.e. 66.95 % → 51.69 %). This is exactly the failure mode E3 exists to predict, and it
fires on the list as given.

### The real per-class kill distribution (whole report, every killed mutant)

| Kills | Test class | Project |
|---:|---|---|
| 144 | `ConsumerSurfaceTests` | ConsumerTests.Host |
| 36 | `RegistryInterfaceLookupTests` | IntegrationTests |
| 36 | `CollectionShapeRegistrationTests` | IntegrationTests |
| 20 | `TopologyOracleFuzzTests` | **Generator.Tests** |
| 17 | `RegistryConcurrencyTortureTests` | IntegrationTests |
| 16 | `CollectionGraphNodeRuntimeTests` | IntegrationTests |
| 16 | `PreserveReferenceRuntimeTests` | IntegrationTests |
| 15 | `AmbientRegistryTests` | IntegrationTests |
| 14 | `FacadeUpdateIntoTests` | IntegrationTests |
| 14 | `CrossConfigFuzzTests` | **Generator.Tests** |
| 12 | `SharedObjectGraphRuntimeTests` | IntegrationTests |
| 10 | `PreserveGraphEdgeCasesRuntimeTests` | IntegrationTests |
| 9 | `RegistryUpdateContractTests` | IntegrationTests |
| 8 | `GraphReconstructionRuntimeTests` | IntegrationTests |
| 8 | `PreserveThroughStructRuntimeTests` | IntegrationTests |
| 8 | `SetNullAdversarialRuntimeTests` | IntegrationTests |
| 7 | `SetNullCycleRuntimeTests` | IntegrationTests |
| 7 | `SetNullCollectionCycleRuntimeTests` | IntegrationTests |
| 5 | `LibraryParityRuntimeTests` | IntegrationTests |
| 4 | `GoldenSharedListTests` | IntegrationTests |
| 4 | `MapDerivedTypePreserveRegressionTests` | IntegrationTests |
| 3 | `AutoValidateRuntimeTests` | **Generator.Tests** |
| 2 | `AmbientRegistrationRuntimeTests` | IntegrationTests |

Those 23 classes hold **288 of the 5,592 discovered tests (5.2 %)** — so the waste factor is real and the
headroom is ~19×, close to the ~61× figure R21-1 derived from the two hot files alone.

### The 18 mutants killed *only* by classes outside the named seven

`FacadeUpdateIntoTests` (4), `AmbientRegistryTests` (5), `AutoValidateRuntimeTests` (1),
`SetNullCycleRuntimeTests` (2), and a six-mutant `DwarfRefContext` cluster killed only by the
preserve/graph family (`CollectionGraphNodeRuntimeTests`, `GoldenSharedListTests`,
`GraphReconstructionRuntimeTests`, `LibraryParityRuntimeTests`, `MapDerivedTypePreserveRegressionTests`,
`PreserveGraphEdgeCasesRuntimeTests`, `PreserveReferenceRuntimeTests`, `PreserveThroughStructRuntimeTests`,
`SharedObjectGraphRuntimeTests`).

Full list: `DwarfMapExceptions.cs` L80/L82/L83/L102; `DwarfMapperRegistry.cs` L56/L87/L140/L258(×2);
`DwarfRefContext.cs` L78/L111/L130(×3)/L168; `IDwarfMapper.cs` L60/L73(×2).

**Conclusion: the filter must be the 23 derived classes, not the 7.** With all 23 in the filter, **zero**
killed mutants lose all their killers.

### Timeouts — the second loss channel, which `killedBy` alone cannot see

All **11 Timeout** mutants (7 in `DwarfMapperRegistry.cs`, 4 in `DwarfRefContext.cs`) have an **empty
`killedBy`** — they are detected without any test being recorded as the killer. Checking `coveredBy` instead:
each is covered by 5,589 tests spanning **all 23** derived classes, so a class-level filter keeps every timeout
covered. Timeouts remain the one genuinely timing-dependent risk in the after-measurement, since they depend on
a hang being observed rather than on an assertion.

---

## A separate defect E3 turned up: `test-projects` is inert, so the recorded 66.95 % is not reproducible from the config's stated intent

The config states that `test-projects` **deliberately excludes** `DwarfMapper.Generator.Tests`. That exclusion
**never took effect**. Evidence, all from the same report:

- `testFiles` holds **5,592 tests across seven test projects** — Generator.Tests 4,601, IntegrationTests 782,
  ConsumerTests 47, plus NegativeCases 55, DifferentialTests 52, Testing.Tests 35, CorpusTests 20. No test id
  appears in two files, so this is not a name-collision artefact.
- Three killer classes (`TopologyOracleFuzzTests`, `CrossConfigFuzzTests`, `AutoValidateRuntimeTests`) live in
  Generator.Tests and account for **37 kill events**. A test that never ran cannot kill.
- The new run's log shows Stryker announcing `Stryker will mutate solution DwarfMapper.NET` and then probing
  *every* test project in the solution — DifferentialTests, CleanCorpus, NegativeCases, Testing.Tests,
  CorpusTests included. With a solution in scope, the `test-projects` list is not what selects the test set.

Two consequences worth recording:

1. **Enforcing the config's own stated intent would LOWER the score.** Restricting to Integration+Consumer only
   would lose **8 kills** (`DwarfMapperRegistry.cs` L87 via `AutoValidateRuntimeTests`; `DwarfRefContext.cs`
   L77×2/L83/L88 via `TopologyOracleFuzzTests`; L114/L130×2 via `CrossConfigFuzzTests`) → 71/118 = 60.17 %.
2. **The comment's "measured cost is nevertheless ZERO" claim for excluding Generator.Tests is falsified.** It
   was measured on a run in which the exclusion was not applied, so it measured the cost of nothing. The cost
   is 8 kills, not zero — and it is not the `DwarfMapValidationException` constructors the comment predicted,
   but `DwarfRefContext` cycle/equality logic reached by the two fuzzers.

`test-projects` is therefore kept and **widened to the three projects that actually contain killers**, as
belt-and-braces documentation of intent rather than as an enforcement mechanism.

---

## E1 — the filtering mechanism

**Used: `test-case-filter`.** It is a genuine Stryker 4.16 `stryker-config` key, though it is **not in
`dotnet stryker --help`**. Verified against the installed tool rather than from recall:
`Stryker.CLI.dll` contains the literal `test-case-filter` in its config-key table, adjacent to
`test-projects`; `Stryker.Configuration.dll` and `Stryker.Abstractions.dll` expose
`TestCaseFilter`/`TestCaseFilterInput`; and `Stryker.TestRunner.VsTest.dll` — the default runner — consumes
`TestCaseFilter` alongside `FullyQualifiedName`. So it is a VSTest TestCaseFilter expression, the same syntax
as `dotnet test --filter`.

The filter is an OR-chain of `FullyQualifiedName~<Class>` over the 23 derived classes. `~` is a
contains-match, so it can only ever *over*-include, never under-include — which is the score-safe direction.

**Why the other mechanism was not viable.** Narrowing `test-projects` cannot express this filter:

- It does not restrict anything in this setup (see the defect above), so it is not a mechanism at all here.
- Even if it did, the killers span **three** projects, and the largest of them — Generator.Tests, 4,601 of the
  5,592 tests — is the bulk of the cost. Selecting projects would retain 5,430/5,592 tests: no speedup.
- Confining the killers to a dedicated project would mean moving test classes or adding a project, which is
  outside this branch's granted scope (three Stryker configs, `scripts/housekeeping.ps1`, the CI workflow).

Confirmation the filter bound, independent of the clock: four test projects that contributed tests to the
baseline now log `did not report any test` — DifferentialTests (52 tests before), NegativeCases (55),
Testing.Tests (35), CorpusTests (20). A filter that failed to bind would have left all 5,592 discovered.
(ConsumerTests.CleanCorpus logs the same line, but it contributed no tests to the baseline either, so it is
not evidence.) The mutant set is also unchanged from the baseline — 111 tested, 7 NoCoverage, 8 CompileError —
so the filter narrowed the test set without de-covering a single mutant.

---

## E1 — measured, 2026-08-17

| | Baseline 2026-08-16 | Scoped 2026-08-17 |
|---|---:|---:|
| Tests discovered | 5,592 | **288** |
| Wall-clock | 44:02 | **02:50** |
| Mutants tested | 111 | 111 |
| Killed | 68 | **72** |
| Timeout | 11 | **0** |
| Survived | 32 | **39** |
| NoCoverage | 7 | 7 |
| CompileError (excluded) | 8 | 8 |
| Scoreable | 118 | 118 |
| **Score** | **66.95 %** | **61.02 %** |

**The speedup is real and large: 44:02 → 02:50, a 15.5× reduction**, from a 19.4× reduction in the test set.
Nothing about *what is mutated* changed — same 111 mutants, same 7 NoCoverage, same 8 CompileError.

**The score fell, so the work stopped here and `break` was not lowered unilaterally** — a dropped score is
exactly the signal this branch's no-lowering rule exists to catch, and the config was reverted pending a ruling.
The evidence below then showed the drop to be **baseline inflation, not a lost kill**, and the maintainer ruled
to accept 61 as the honest floor. `break: 61` is committed on that ruling, with the reasoning recorded in the
config comment so the number is not "restored" to 66 by someone who sees only that it went down.

### But the drop is not a lost kill — the baseline was inflated

Every one of the 7 lost detections was `Timeout → Survived`. Not one was `Killed → anything`. Inspecting what
those mutants actually replace shows most of them **cannot hang, so a Timeout was never a genuine detection**:

| Mutant | Replacement | Baseline | Scoped |
|---|---|---|---|
| `DwarfMapperRegistry.cs` L200/201/202 | `ArgumentNullException.ThrowIfNull(…)` → `;` | Timeout | **Survived** |
| `DwarfMapperRegistry.cs` L301 | `Source.GetHashCode() * 397` → `/ 397` | Timeout | **Survived** |
| `DwarfRefContext.cs` L77 (×2), L78 (cond-false) | depth-clamp ternary | Timeout | **Survived** |

Deleting three `ArgumentNullException.ThrowIfNull` calls and changing a hash-mixing multiply to a divide
introduce **no loop, no recursion, and no blocking call**. Such a mutant can be Killed or it can Survive; it
cannot hang. The only mechanism that produces a Timeout for it is the *test session* exceeding Stryker's
timeout — and that is exactly what a 5,589-test covering set does inside a
`[CollectionBehavior(DisableTestParallelization = true)]` assembly, run end to end, once per mutant.

**One of the seven settles it outright: it is an equivalent mutant.** `DwarfRefContext.cs` L77's Equality
mutation turns the clamp's lower bound `maxDepth < 1` into `maxDepth <= 1`. The two differ on exactly one input,
`maxDepth == 1`, and there they agree anyway: the original falls through to `1 > AbsoluteMaxDepth` (1000), which
is false, and yields `maxDepth` = 1; the mutant takes the true branch and yields the literal 1. Identical output
for every input, so **no test can possibly detect it** — that is what "equivalent mutant" means. It is
nonetheless recorded as a *detected* Timeout in the baseline. A mutation that provably cannot be detected being
credited as a detection is not an implausibility argument; it is proof that the Timeout bucket was measuring the
clock rather than the mutant.

**Why the artifact preferentially collects survivors.** Stryker's bail is on by default
(`--disable-bail` defaults to False): a mutant with a killer aborts its session at the first failing test, but a
mutant that **nothing** kills has to run all 5,589 covering tests serially to completion — the slowest possible
session, and the one most likely to exceed the timeout. So the baseline's Timeout bucket is biased towards
exactly the mutants that survive. That is why 7 of the 11 came back Survived, while the 4 that had real (but
late-ordered) killers came back Killed.

The same effect ran the other way for four mutants, which is the corroborating evidence: `DwarfMapperRegistry.cs`
L68/69/70 and `DwarfRefContext.cs` L78 (equality) were **Timeout in the baseline and cleanly Killed in the
scoped run** — L68-70 by `AmbientRegistryTests`, L78 by `CollectionGraphNodeRuntimeTests`,
`PreserveGraphEdgeCasesRuntimeTests` and `SetNullCycleRuntimeTests`. Those were always detectable; the slow run
simply timed out before the assertion was reached and credited the timeout instead.

**So `Timeout` in the 2026-08-16 run was largely a symptom of the covering-set explosion, not a diagnosis of the
mutant.** The 66.95 % ratchet was resting on timing noise: 7 of its 79 detections have no demonstrated
detection, and one of the seven provably cannot be detected at all. **61.02 % is the honest score of the same
118 mutants**, and it is *more* precise, not less — four
mutants moved from the vague "Timeout" bucket into a named killer.

### The four coverage holes the re-classification exposed

Previously masked as "detected", now correctly Survived — genuine, actionable gaps:

1. **`RegisterUpdate` has no null-argument test at all** (L200/201/202). All three `ThrowIfNull` guards can be
   deleted with no test noticing. `Register`'s equivalents (L68-70) *are* covered, by `AmbientRegistryTests` —
   so this is an asymmetry between the create and update tables, exactly the shape round 18 found elsewhere.
2. **`Key.GetHashCode`'s hash mixing is unpinned** (L301): `* 397` → `/ 397` survives. The registry still
   *works* with a degenerate hash (equality is what decides lookups), so only a distribution or collision
   assertion would catch it.
3. **Two of the four `DwarfRefContext` depth-clamp branches are unasserted.** Only the
   `maxDepth > AbsoluteMaxDepth` equality boundary is pinned. The L77 Equality mutant is *equivalent* (see above)
   and should be ignored rather than chased — it belongs in an `ignore-mutations` entry, not in a test.

**One honest caveat on the seventh mutant.** `DwarfRefContext.cs` L78's conditional-false mutation removes the
*upper* clamp, so `MaxDepth` becomes unbounded — and that is the one mutation among the seven that genuinely
*could* hang, by recursing far deeper before `DwarfMappingDepthException` fires. For that one, "no test detects
it" is stronger than the evidence supports; the defensible claim is **no demonstrated detection** — no test in
the 288 detects it, and whether some excluded test hangs on it was not measured. The other six are settled: four
cannot hang by construction, and one cannot be detected at all.

### Coverage itself did not move — only the timeout artifacts did

Worth stating precisely, because it is what rules out scoping having hidden something: the **7 `NoCoverage`
mutants are a byte-identical set in both runs** (same files, mutators, lines, replacements). Scoping changed
which tests *run*, and it changed nothing about which mutants are *covered*. Combined with "no mutant went
`Killed → anything`", the entire 5.93 pp delta is the 7 timeout re-classifications and nothing else.

### `Assert-MutantsWereTested` still passes

Verified against the new run's report by re-implementing the guard's own logic — a regex count of
`"status"\s*:\s*"(Killed|Survived|Timeout|NoCoverage)"` — which returns **118**, well above the `> 0` it
demands. The guard is unaffected by scoping: it keys off the scoreable denominator, and scoping changed the test
set, not the mutant set. It would still fire on a vacuous run, because a vacuous run drops every mutant to
`Ignored` ("Removed by mutate filter") and none of the four scoreable statuses would appear.

### Caveats on both numbers

Both wall-clocks are **loaded-machine** figures: other agents were building and testing this same solution
during each run. R21-1 already flagged the 44:02 as an upper bound on a loaded machine. The 02:50 is measured
under similar conditions, so the 15.5× *ratio* is the trustworthy part.

---

## The concrete holes behind the honest 61.02 %

**Not fixed here — this is the list to file tasks from.** All 46 undetected mutants (39 Survived + 7
NoCoverage), grouped into **13 holes** by member and missing case. Lines are `src/DwarfMapper/`. "Newly honest"
marks the ones the re-classification exposed; the rest were already Survived at 66.95 % and are simply
unaddressed.

### Argument-guard holes — a whole family, and an asymmetry

Every one of these is a `ArgumentNullException.ThrowIfNull(x)` statement that can be **deleted** with no test
noticing. `Register`'s first three guards (L68-70) *are* pinned, by `AmbientRegistryTests` — so the create path
is partly tested and the rest of the surface is not.

| # | Member | Mutants | Missing case |
|---|---|---|---|
| 1 | `DwarfMapperRegistry.RegisterUpdate` | L200, L201, L202 — **newly honest** | No null-argument test **at all**: each of `source`, `destination`, `map` being null must throw `ArgumentNullException`. |
| 2 | `DwarfMapperRegistry.Update` | L252, L253, L254, L255 | Four guards, none asserted — the update entry point has no null-argument test. |
| 3 | `DwarfMapperRegistry.Map` | L133, L134 | Two guards unasserted on the primary map entry point. |
| 4 | `DwarfMapperRegistry.Register` | L76 | The fourth statement of an otherwise-pinned guard block. |

### `Key` — the registry's dictionary key is barely pinned

| # | Member | Mutants | Missing case |
|---|---|---|---|
| 5 | `Key.GetHashCode` | L301 Arithmetic (`* 397` → `/ 397`) — **newly honest**; L301 Bitwise (`~(...)`) | Nothing asserts the hash *mixing*. Lookups are decided by `Equals`, so a degenerate hash still works — only a distribution/collision assertion catches this. |
| 6 | `Key.Equals(Key)` | L291 Logical (`&&` → `\|\|`) | Two keys sharing **only** `Source` **or** only `Destination` must **not** be equal. Today that mutation survives, i.e. a half-matching key compares equal. This is the most consequential survivor in the list — it is a registry mis-lookup. |
| 7 | `Key.Equals(object)` | L295 Block removal — `NoCoverage` | The `object`-typed override is never invoked by any test; the generic overload is always used. |

### `DwarfRefContext` — the depth clamp and one uncovered guard

| # | Member | Mutants | Missing case |
|---|---|---|---|
| 8 | `DwarfRefContext` ctor, lower clamp | L77 Conditional-false — **newly honest** | `maxDepth <= 0` must clamp to `MaxDepth == 1`. (The sibling L77 Equality mutant is **equivalent** — see above — and should be silenced via `ignore-mutations`, not chased with a test.) |
| 9 | `DwarfRefContext` ctor, upper clamp | L78 Conditional-false — **newly honest**; L78 Equality (`>=`) | `maxDepth > AbsoluteMaxDepth` (1000) must clamp to 1000, and the boundary `maxDepth == 1000` must pass through unchanged. The `<` form is killed; the `>=` boundary is not. |
| 10 | `DwarfRefContext.TryEnterNode` | L155 Boolean → `false` — `NoCoverage` | Not reached by any test in the leg. |

### Exception message text is entirely unasserted

| # | Member | Mutants | Missing case |
|---|---|---|---|
| 11 | `DwarfMapMissingException.FormatMessage` | 16 survivors, L81-L106 | Every literal fragment can be emptied and the equality/boolean/conditional branches flipped with no test noticing. Nothing asserts the message a consumer actually reads — including the compiler-generated-type branch (L95, L97) that exists to explain a specific failure. |
| 12 | `DwarfMappingDepthException` ctor | L28-L31 String, L32 Block removal | Same shape: the depth-exhaustion message is unpinned, and the ctor body can be emptied. |

### Facade

| # | Member | Mutants | Missing case |
|---|---|---|---|
| 13 | `IDwarfMapper.Map` (facade) | L73 Logical (`&&` → `\|\|`) | The `TryGet` guard's two conditions are not independently asserted. |

**`DwarfMapperRegistry.ResetForTests` (L271-275, 5× `NoCoverage`)** is deliberately excluded from the list
above: it is a test-only reset hook that **no test in this leg calls**. It is uncovered in *both* runs, so it is
not a scoping artefact. Either something should call it or it is dead code — that is a question, not a hole.

---

## C1 — landed

The maintainer ruled on the `break` question — **accept 61 as the honest floor** — on the grounds recorded
above: no kill was lost, and the old number counted non-detections as detections. The no-lowering rule exists to
stop a ratchet absorbing a *regression*, not to freeze a number that was measured wrong. So C1 proceeded.

**Added: the `runtime-mutation` job in `.github/workflows/ci.yml`**, placed immediately after `surface-matrix`
(the other filtered-test-quality leg) and before `aot-trim-gate`. The workflow's structure is unchanged — it is
one more independent job following the same checkout / setup-dotnet shape as its siblings. It:

- pins the tool (`dotnet tool install --global dotnet-stryker --version 4.16.0`), matching the `sbom` job's
  pinned `CycloneDX`. The pin matters more here than for most tools: because `test-projects` is inert
  (NOTE 3), a future Stryker that *started* honouring it would change which tests run — and therefore the
  score — with no config edit to point at;
- runs only the runtime config. The generator and doc-tooling legs remain housekeeping-only: neither has been
  scoped and both are still far too slow for every push;
- carries `timeout-minutes: 30`, so a hung run cannot burn the default 6-hour budget. Generous on purpose — the
  measured 3 min is on 12 cores and a hosted runner has far fewer;
- **re-implements the vacuity guard inline**, mirroring `Assert-MutantsWereTested`, because CI is precisely
  where that hazard was unguarded;
- uploads the report with `if: always()`, so a score drop can be diagnosed from the run that failed.

**One CI risk checked rather than assumed:** `dotnet-stryker` 4.16 targets `net8.0` while the job installs only
SDK 10.0.101. Its `Stryker.CLI.runtimeconfig.json` sets `rollForwardOnNoCandidateFx: 2` (Major), so it runs on
the .NET 10 runtime with no .NET 8 present — confirmed locally on a machine carrying only runtimes 7, 9 and 10.
No extra `dotnet-version` entry is needed.

## Confirming run — the committed config, measured

The committed config (filter + `break: 61`) was re-run to prove the gate passes rather than inferring it from
the earlier `break: 66` run:

| | |
|---|---|
| Wall-clock | **03:07** (18:30:45 → 18:33:52) |
| Score | **61.02 %** — Killed 72, Survived 39, Timeout 0, NoCoverage 7 |
| Exit code | **0** (passes `break: 61`) |

Two independent runs of the scoped config produced **the identical score**, so 61.02 % is reproducible and not a
one-off. The 03:07 vs 02:50 spread between them is machine load, not configuration.

`Assert-MutantsWereTested` was then verified by executing its **actual logic** from `scripts/housekeeping.ps1`
against this run's report — the `Get-ChildItem`/`LastWriteTime` selection followed by the
`"status"\s*:\s*"(Killed|Survived|Timeout|NoCoverage)"` regex count — which picked
`StrykerOutput/2026-08-17.18-30-46/reports/mutation-report.json` and returned **118**, passing its `> 0` demand.
The CI job's `grep` form of the same count returns 118 on the same file, so the two implementations agree.

## What is committed

- `stryker-config.runtime.json` — `test-case-filter` (23 derived killer classes), `test-projects` widened to
  the three projects that contain killers, `break: 61`, and a comment rewritten to record the new measurement,
  why 66 was inflated and must not be "restored", that `test-projects` is inert (NOTE 3), and that the former
  "measured cost is ZERO" claim for excluding `Generator.Tests` is false (NOTE 4 — 37 kills, 8 exclusive).
- `.github/workflows/ci.yml` — the `runtime-mutation` job.
- `Issues/ledgers/E3-E1-report.md` — this report.
- **Not changed:** `scripts/housekeeping.ps1`. Its guard needed no edit; it was verified, not modified.

`dotnet build DwarfMapper.NET.sln -c Release` is 0 warnings / 0 errors at this base, samples included.
