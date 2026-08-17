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

**The score fell, so per this branch's rules the work STOPS here and `break` was NOT lowered.**
`stryker-config.runtime.json` is reverted to its committed state; `break` remains **66**. Nothing was committed
that changes what the leg measures.

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

### The four coverage holes this exposed

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

## C1 — NOT reached, and deliberately so

E1 came in at 02:50, comfortably under the ~5-minute gate, so the *timing* precondition is met. C1 is
nevertheless **not** done, because the leg as scoped **exits non-zero against the committed `break: 66`** — it
scores 61.02 %. Adding a leg to CI that fails on the first push would be worse than the current gap, and the
only ways to make it green are the two things this branch forbids: lowering the ratchet, or narrowing what is
mutated.

**C1 is unblocked the moment the `break` question is decided** — it is one small job, and it is the actual prize
(§5b.1: the leg runs in no CI job at all today, so the score is free to regress silently).

## What the maintainer has to decide

This needs a judgement call that is explicitly not the agent's to make, because it means **loosening a
ratchet**:

- **Accept 61 as the honest floor.** Set `break: 61`, keep `test-case-filter`, and the leg costs ~3 minutes and
  can go into CI immediately. The recorded 66.95 % is not reproducible and never described real coverage; 7 of
  its detections were timing artifacts. This is the recommended path — it trades a number that was never true
  for a leg that runs on every push.
- **Or keep 66 and first close the holes.** Kill the seven newly-honest survivors (a `RegisterUpdate` null-guard
  test, a hash-distribution assertion, two clamp-boundary tests) and the scoped score rises back through 66 on
  its own merits. Then `break: 66` is real for the first time, and CI can adopt the leg without a ratchet
  change. Slower to land; strictly better ratchet.

Either way the filter itself is sound: it loses **zero** kills, and the only thing it "lost" was the run being
slow enough to time out.

## What is and is not committed

- **Committed:** this report only.
- **Reverted, deliberately:** the `test-case-filter` key and the widened `test-projects` list in
  `stryker-config.runtime.json`. The file is byte-identical to `7fe1b80`, so the committed leg still measures
  66.95 % and still passes `break: 66`. No unmeasured behaviour change is being merged.
- **Not changed:** `scripts/housekeeping.ps1` (its guard needed no edit and still holds), `.github/workflows/ci.yml`.

The exact filter that produced 02:50 / 61.02 %, ready to paste back once `break` is decided, is the OR-chain of
`FullyQualifiedName~<Class>` over the 23 classes tabulated above, with `test-projects` widened to include
`tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj`.
