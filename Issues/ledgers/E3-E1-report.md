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

**Conclusion at the time: a filter would have to name the 23 derived classes, not the 7** — with all 23, zero
killed mutants lose all their killers.

> **Superseded — read on before acting on this.** No filter shipped. The maintainer declined test-set exclusion
> outright, and the corrected data later showed this derivation was itself incomplete: there are **26** killer
> classes, not 23, because 11 mutants in the source report had an empty `killedBy`. See *E1 — the filtering
> mechanism: found, measured, and DECLINED*.

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

**The `test-projects` key is therefore deleted outright**, not corrected. A populated list tells the next reader
that an exclusion is in force when none is; removing it makes the config honest about what it actually does. The
three projects that contain killers are recorded here and in the config comment (NOTE 3) instead — as
documentation, which is all such a list could ever have been.

---

## E1 — the filtering mechanism: found, measured, and DECLINED

> **Outcome first, because it reverses what the rest of this section was written to support.** The filter works
> and it is fast — 44:02 → 02:50, measured twice. **It is not shipped.** The maintainer ruled that the leg
> retains **no exclusion from the test set**, and that ruling is right for a reason this very report supplies:
> see *Why the filter was declined* below. What is shipped instead is a **timeout fix on the full suite**. The
> measurements are kept because they are the evidence that the old score was wrong.

**The mechanism exists: `test-case-filter`.** It is a genuine Stryker 4.16 `stryker-config` key, though it is
**not in `dotnet stryker --help`** — worth recording, because its absence from the help output is exactly why
it was thought not to exist. Verified against the installed tool rather than from recall:
`Stryker.CLI.dll` contains the literal `test-case-filter` in its config-key table, adjacent to
`test-projects`; `Stryker.Configuration.dll` and `Stryker.Abstractions.dll` expose
`TestCaseFilter`/`TestCaseFilterInput`; and `Stryker.TestRunner.VsTest.dll` — the default runner — consumes
`TestCaseFilter` alongside `FullyQualifiedName`. So it is a VSTest TestCaseFilter expression, the same syntax
as `dotnet test --filter`.

The filter tried was an OR-chain of `FullyQualifiedName~<Class>` over the 23 derived classes. `~` is a
contains-match, so it can only ever *over*-include, never under-include — which is the score-safe direction
**for a fixed test suite**, and that qualifier turns out to be the whole problem.

### Why the filter was declined

**A filter is a list, and lists drift** — silently, and in the one direction nothing catches. When a test becomes
the sole killer of a mutant, an exclusive filter drops that kill, **the score goes UP**, and coverage goes down,
with nothing in the repository noticing, because a rising score is exactly what a ratchet is built to welcome.
That is the failure shape this branch exists to delete, and a coverage-measuring tool is the worst possible place
to install a fresh instance of it.

**And here the list could not even be derived correctly — that part is measured, not feared.** The 23 classes
came from every `killedBy` set in the 2026-08-16 report. But **11 mutants in that report had an empty
`killedBy`**, because they were recorded as timeouts (see below) — so any class whose only kills landed on those
11 was **invisible to the derivation**. Fixing the timeout proved it: the corrected run credits **26** killer
classes, and three of them — `DeepRecursionGuardRuntimeTests`, `DepthSafetyRuntimeTests`,
`NoneModeCollectionDepthRuntimeTests`, 5 kills between them on the `DwarfRefContext` depth clamp — **are not in
the 23**. They were in the suite all along; the data used to build the list had simply not recorded what they
killed.

In this instance the filter would still have lost **zero** kills, because those mutants have redundant killers —
so this is not a near-miss story. It is a **method** story: a derivation blind to 3 of 26 killers is not a sound
basis for excluding tests, and the blindness came from the very defect the filter was being used to work around.

The 15.5× was real, but it bought wall-clock with a silent-loss mechanism. **The honest lever was never the test
count — it was the clock.**

**Why the other mechanism was not viable either.** Narrowing `test-projects` cannot express such a filter:

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

## The scoped experiment, 2026-08-17 — not shipped, but it is what diagnosed the real defect

This run is the diagnostic, not the deliverable. Its value is that shrinking the test set made **seven mutants
change status**, which is what exposed the old score as wrong.

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
The evidence below then showed the drop to be **baseline inflation, not a lost kill**.

What the maintainer then ruled — and it is a better call than treating this as a `break` question — is that the
**inflation itself** is the defect and must be fixed at its cause: **raise the timeout, keep the whole suite.**
Scoping was rejected outright (*Why the filter was declined*, above). See *The fix that shipped* below.

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

## The fix that shipped — `additional-timeout`, on the full suite

**Diagnosis.** Stryker computes `total timeout = initialTestTime + additional-timeout` (stated in its own
configuration docs). Measured on this branch, the initial full-suite run is **193 s** (5,650 tests,
18:39:28 → 18:42:41, serial because the assembly sets
`CollectionBehavior(DisableTestParallelization = true)`). The default `additional-timeout` is small — a value in
seconds, not minutes; Stryker does not log the computed ceiling at info level, so the exact default is not
sourced here and no number is claimed for it.

The ratio is what matters, and it does not depend on that number. For the two hot files a mutant's covering set
*is* essentially the whole suite, so each of those sessions runs a **~193 s workload under a ceiling only
seconds above it**. Any load spike — a concurrent build, another agent's test run — pushes it over, and Stryker
records **Timeout, which it counts as detected.**

The empirical proof that the cushion was too small needs no default value at all: **11 mutants timed out, and
four of them provably cannot hang** (deleted `ThrowIfNull` calls; `* 397` → `/ 397`) while a fifth is a provably
equivalent mutant. A ceiling that fires on code with no loop in it is a ceiling set too close to the workload.
That is the whole mechanism behind the inflated 66.95 %: not a hang, not a diagnosis, just a stopwatch losing a
race it was never given room to win.

**The fix is `"additional-timeout": 120000`** — a 120 s cushion on a 193 s suite, about **62 % headroom** instead
of 2.5 %. It targets the actual defect (the ceiling was too close to the workload) and costs nothing in
coverage, because it changes no test and no mutant. Only a mutant that genuinely hangs pays the extra wait.

**Why this is the right lever and scoping was not.** Shrinking the test set also removed the timeouts — by
removing the workload — but it did so by *not running tests*, buying accuracy and wall-clock with the
silent-loss mechanism described above. Raising the ceiling removes the artefact while keeping every test. One
fixes the measurement; the other changes what is measured and hopes the list stays right.

### Measured: the full suite with the raised timeout

| | Baseline 2026-08-16 | Scoped (rejected) | **Full suite + timeout fix** |
|---|---:|---:|---:|
| Tests run | 5,592 | 288 | **5,650** |
| Wall-clock | 44:02 | 02:50 | **12:25** |
| Mutants tested | 111 | 111 | **111** |
| Killed | 68 | 72 | **71** |
| Timeout | 11 | 0 | **1** |
| Survived | 32 | 39 | **39** |
| NoCoverage | 7 | 7 | **7** |
| Detected / scoreable | 79 / 118 | 72 / 118 | **72 / 118** |
| **Score** | 66.95 % | 61.02 % | **61.02 %** |
| Exit code at `break: 61` | — | 0 | **0** |

**`break` is 61**, the floor of the measured 61.02 %.

**This table is the evidence the ruling asked for.** The full suite and the scoped run reach the **identical
61.02 %** — and reach it via the identical 7 `Timeout → Survived` re-classifications, with **0 mutants gaining
detection**. So the filter was never buying accuracy; it was only buying wall-clock, and the honest score is a
property of the mutants rather than of how many tests are run. The old 66.95 % is the outlier, and the reason is
the ceiling, not the suite.

**Two corrections to earlier framing, both now measured:**

1. **The 44 minutes was mostly contention, not test count.** The same full suite finishes in **12:25** — and that
   too was on a busy machine. R21-1's covering-set explosion is real, but it is a ~12-minute problem, not a
   ~44-minute one; the rest of the original figure was other builds competing for the same 12 cores. The
   44-minute number should not be quoted as the cost of this configuration.
2. **A 12-minute leg is comfortably automatable**, which is what makes the "run it in CI" outcome reachable
   without excluding a single test.

**The one remaining Timeout is a false alarm, and it does not touch the score.** It is
`DwarfMapExceptions.cs` L107, a string mutation to `""` with just 4 covering tests — it cannot hang. That mutant
is **Killed** in the 2026-08-16 baseline and in both scoped runs, so it is genuinely detected; here its 4-test
session simply ran long on a loaded box. Timeout and Killed both count as detected, so 61.02 % is unaffected
either way. It is, however, a live reminder that load can still manufacture a timeout even at a 62 % cushion.

**A caveat for whoever tunes this next: the headroom is proportional, and the cushion is not.** On a slower
machine `initialTestTime` grows while the +120 s stays fixed, so the *percentage* of headroom shrinks. If a
future run — especially on a CI runner with fewer cores — shows Timeouts reappearing on mutants that cannot
hang (the `ThrowIfNull` deletions, the `* 397` → `/ 397`), the correct response is to **raise
`additional-timeout` again, never to filter the suite.**

---

## The concrete holes behind the honest score

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

**Added: the `runtime-mutation` job in `.github/workflows/ci.yml`**, placed immediately after `surface-matrix`
and before `aot-trim-gate`. The workflow is not restructured — it is one more independent job in the same
checkout / setup-dotnet shape as its siblings. It:

- **runs nightly (`cron: '17 3 * * *'`) plus `workflow_dispatch`, never on push or pull_request**, guarded by
  `if: github.event_name == 'schedule' || github.event_name == 'workflow_dispatch'`. A 12-minute leg on a
  12-core box will be appreciably slower on a hosted runner, and the ruling is explicit that slow-and-honest
  beats fast-and-quietly-narrowed. The job comment says so, so nobody "fixes" the runtime with a filter;
- carries **`timeout-minutes: 120`** — roughly 10× the measured 12:25, sized for a runner with ~4 cores
  (Stryker's default concurrency is cores/2, so 2 instead of 6) rather than for this machine;
- pins the tool (`dotnet tool install --global dotnet-stryker --version 4.16.0`), like `sbom`'s `CycloneDX`.
  Both Stryker's test discovery and its timeout arithmetic feed the score directly, so an unpinned upgrade could
  move the number with no repo change to point at;
- runs only the runtime config — the generator and doc-tooling legs stay housekeeping-only;
- **re-implements the vacuity guard inline**, mirroring `Assert-MutantsWereTested`, because CI is exactly where
  that hazard was unguarded;
- uploads the report with `if: always()`, so a score change can be diagnosed from the run that reported it.

**Two operational facts worth knowing rather than discovering:**

1. **`schedule` fires only on the repository's default branch.** The nightly leg stays dormant until this reaches
   master; on a feature branch it can only be started by hand via `workflow_dispatch`. "It is in CI" and "it has
   run" are different claims until the merge.
2. **`dotnet-stryker` 4.16 targets `net8.0`** while the job installs only SDK 10.0.101. Its
   `Stryker.CLI.runtimeconfig.json` sets `rollForwardOnNoCandidateFx: 2` (Major), so it runs on the .NET 10
   runtime with no .NET 8 present — confirmed locally on a machine carrying only runtimes 7, 9 and 10. No extra
   `dotnet-version` entry is needed.

## `Assert-MutantsWereTested` — verified, not modified

Checked by executing the guard's **actual logic** from `scripts/housekeeping.ps1` against the full-suite report —
the `Get-ChildItem`/`LastWriteTime` selection followed by the
`"status"\s*:\s*"(Killed|Survived|Timeout|NoCoverage)"` regex count. It selected
`StrykerOutput/2026-08-17.18-38-48/reports/mutation-report.json` and returned **118**, passing its `> 0` demand.
The CI job's `grep` form of the same count returns **118** on the same file, so the two implementations agree.

The guard needed no edit. It keys off the scoreable denominator, which none of this work changes, and it would
still fire on a vacuous run: a vacuous run drops every mutant to `Ignored` ("Removed by mutate filter") and none
of the four scoreable statuses would appear.

## What is committed

- **`stryker-config.runtime.json`** — `"additional-timeout": 120000`; **no `test-case-filter`** and **no
  `test-projects` key at all**; `break: 61`. The comment now records the new measurement, why 66 was inflated by
  load-induced timeouts and must not be "restored", why a test filter is refused (NOTE on derivation blindness),
  that a `test-projects` key would be inert (NOTE 3), that the old "measured cost is ZERO" claim for excluding
  `Generator.Tests` is false (NOTE 4 — 37 kills, 8 exclusive), and that the 44-minute figure is
  contention-confounded (NOTE 5).
- **`.github/workflows/ci.yml`** — the nightly `runtime-mutation` job, plus the `schedule` and
  `workflow_dispatch` triggers it needs.
- **`Issues/ledgers/E3-E1-report.md`** — this report.
- **Not changed:** `scripts/housekeeping.ps1`. Verified, not modified.

Only `.json`, `.yml` and `.md` files changed, so the last full build remains valid:
`dotnet build DwarfMapper.NET.sln -c Release` is **0 warnings / 0 errors** at this base, samples included.

## A side effect to know about: running this leg locally pollutes a generated doc

Noticed while committing this work, and worth recording because it is silent and easy to commit by accident.
Stryker instruments the assembly under test by injecting a `MutantControl` class in a randomly-named namespace.
The full-suite run executes the doc-generation tests, which reflect over the *instrumented* assembly — so
`docs/generated/api-reference.md` came back with a spurious section appended:

```
## `StrykerCjVUiCkRKRTFuH4`
### class `MutantControl`
_No public settable surface._
```

It was reverted, not committed. Two consequences:

- **Locally:** after `housekeeping.ps1 -Mutation`, check `git status` for generated-doc churn and discard it.
  The namespace is randomised per run, so it never collides and never conflicts — it just quietly accumulates.
- **In CI:** harmless. The nightly job runs on a throwaway checkout and commits nothing.

It is not worth a guard on its own, but if generated docs ever gain a ratchet that runs in the same session as
the mutation leg, the two will fight, and this is the reason.

## Round-22 P2 appendix (2026-08-21) — the post-T7 remainder, dispositioned

T7 (round 21) killed the three C6-named survivors and re-measured the leg at 87.61 % (99 K / 12 S / 2 NC of
113 scoreable). Round-22 P2 works the remainder. Holes 2–3 (the `Map`/`Update` guard deletions), the three
remedy string tails of hole 11 (L98/L101/L104 at the T7 measurement) and no-coverage hole 10 (`TryEnterNode`)
are killed test-side — `AmbientRegistryTests.Map_null_arguments_throw_with_the_offending_parameter_named`,
`RegistryUpdateContractTests.Update_null_arguments_throw_with_the_offending_parameter_named`, the extended
remedy-tail assertions in `RegistryInterfaceLookupTests.A_lazy_iterator_with_no_map_at_all_is_told_to_materialize`
/ `DwarfExceptionContractTests.A_plain_type_is_told_to_declare_the_pair`, and the new
`DwarfRefContextOnStackGuardTests`. Two survivors are adjudicated into
`Issues/ledgers/equivalent-mutants.md` with the proofs below, and hole 7 is FILED as killable only via a
product change. This section is the proof anchor the ledger entries point at.

### Proven equivalent — the facade `TryGet` guard `&&` → `||` (hole 13, `IDwarfMapper.cs` L73)

The guard is `DwarfMapperRegistry.TryGet(typeof(TSource), typeof(TDestination), out var map) && map is not
null`. The two operands cannot disagree on any reachable input, so flipping the connective changes nothing:

- `TryGet` forwards `ConcurrentDictionary.TryGetValue`, whose contract sets the `out` value to `default`
  (null) exactly when it returns `false` — so **false ⇒ `map is null`**.
- The dictionary's values can never BE null: `Register` is the only writer and it
  `ThrowIfNull`-guards the delegate before `TryAdd` — so **true ⇒ `map is not null`**.

The operands therefore co-vary — the only reachable states are (true, true) and (false, false) — and on
those `&&` and `||` agree (evaluation order and side effects are unchanged: the `TryGet` call is the left
operand under both connectives). E3's "missing case" framing ("the two conditions are not independently
asserted") presumed the operands could be driven apart; they cannot, which is the T3 bar for
proven-equivalent. No test is attempted — the `map is not null` arm exists to satisfy nullable flow
analysis, not as a reachable branch.

### Probably equivalent — the ambiguous-branch guard `Count: > 1` → `>= 1` (hole-11 residue, `DwarfMapExceptions.cs` L86)

`FormatMessage` takes the ambiguous branch on `ambiguousInterfaces is { Count: > 1 }`. The mutant diverges
only on a **one-element** list. `DwarfMapperRegistry.Map` can never produce one: a single accepting
interface RESOLVES (the `candidates!.Count == 1` path returns before the throw), so the exception is
constructed with either null or a ≥2-element list. The only remaining path is calling the public ctor
directly with a 1-element list — an input its own documentation excludes (`ambiguousInterfaces` is passed
"when there was more than one") — so a killing test would pin undocumented off-contract behaviour, which is
implementation trivia, not an invariant. Graded probably-equivalent rather than proven because that direct
ctor call IS expressible; the grade records the judgement that it should not be written, per the plan's P2
disposition ("adjudicate rather than chase").

### FILED, not killed — `Key.Equals(object)` no-coverage (hole 7, `DwarfMapperRegistry.cs` L285–288)

`Key` is a **private** nested struct. Its `IEquatable<Key>.Equals` is what `ConcurrentDictionary`'s default
comparer calls; the `object`-typed override is reachable only by boxing a `Key`, which no public surface
does. A test-side kill therefore requires bypassing accessibility by reflection — rejected by the house
accessibility stance — so per the P2 rule ("killable only via product change: stop and file") the item is
filed rather than killed: the candidate remedy is the one-word `private` → `internal` on the nested struct
(`[InternalsVisibleTo("DwarfMapper.Generator.Tests")]` already exists) plus a contract test pinning
equal-pair → true, half-matching pair → false, non-`Key` object → false, null → false. NOT entered in the
equivalents ledger — an uncovered override is a coverage/denominator question, which that ledger's own
rules exclude; the maintainer may instead prefer a denominator ruling (the D-b `ResetForTests` precedent).
Recorded in `Issues/round20/TASKS.md` as a new finding.

### The re-measure

See the RE-MEASURED 2026-08-21 (round-22 P2) entry in `stryker-config.runtime.json`'s comment and the
refreshed runtime row of `Issues/ledgers/equivalent-mutants.md` — the score, `break` move and ledger
summary land in the same commit as this appendix.

## Superseded history on this branch

Two earlier decisions are left in the history deliberately, because both were measured and the reasoning is
worth keeping:

- `96e5911` shipped the `test-case-filter` and `break: 61` under the first ruling. It is **superseded** by this
  work: the filter is gone, `break: 61` survives on a different and better basis (a full-suite measurement
  rather than a scoped one).
- `8274cd9` / `5cde3b8` recorded E3 and the stop-and-report when the score first fell. That stop was correct
  procedure, and the evidence it produced is what identified the timeout defect.
