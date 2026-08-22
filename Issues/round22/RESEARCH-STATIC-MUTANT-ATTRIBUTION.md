<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Research: static-mutant attribution on the generator mutation leg (round 22, K3)

Status: research, 2026-08-22. **Read-only — no build, no test run, no Stryker run was launched** (other
agents hold the machine). Every number below comes from a report already on disk, from a ledger, or from
Stryker.NET's own source and documentation; nothing was re-measured.

**Sources.** The r22 worktree at `1f79718` (branch `feat/round22-gates`) — its four
`StrykerOutput/*/reports/mutation-report.json` files, the three `stryker-config*.json`, the mutated
generator sources, `tests/`, `.github/workflows/ci.yml`; the ledgers `Issues/ledgers/T3-mutation-survivors.md`
and `Issues/ledgers/equivalent-mutants.md` (the latter exists only on the r22 branch); the plan
`docs/superpowers/plans/2026-08-21-round22-compiler-testing-and-97-gates.md`; and, for the mechanism,
Stryker.NET at `master` on GitHub plus `stryker-mutator.io`, both cited inline by path and by quoted line.

**Standing — say it plainly.** The maintainer's ruling (c) of 2026-08-21 **raised** the deep-tier ceiling:
all legs run every night, no rotation, no total-duration gate. The three legs already run as a parallel CI
matrix, so the nightly critical path *is* the generator leg. Nothing here is blocking, nothing is urgent,
and no gate depends on the answer. This is a wall-time optimisation question and it is written as one.

**The one-paragraph answer.** 85 of the generator leg's 201 scoreable mutants are `static`, they account
for essentially the whole of the leg's mutant-testing phase (~99 % of the work, ~70 % of the 21:19
wall-clock — estimate, arithmetic below), and the flag is **not** a property of the C# `static` keyword: it
is Stryker's runtime coverage attribution, confirmed from Stryker's source, so making the generator's
members non-static would not move it. Worse for the optimiser, the cost is concentrated in the mutants that
*cannot be killed*: **16 of the 85 statics survive, and 15 of those 16 are already adjudicated
proven-equivalent**, and a survivor is exactly the mutant that cannot bail early and must run all 5,897
tests to completion. Every lever that would remove that cost is closed by a standing ruling (in-source
`// Stryker disable` — ruling (b); narrowing `mutate` or the test set — the binding mutation-leg-integrity
ruling; de-static-ing the product — mutation appeasement, and ineffective anyway). The one ruling-clean
lever left is splitting the leg into per-file parallel CI jobs, worth an estimated ~6 minutes off the
critical path for 4× the fixed compute and four ratchets to keep in lockstep. **Recommendation: leave it,
and write the cost down where the leg is read.**

---

## 1. The measured census

Source: `StrykerOutput/2026-08-22.01-14-14/reports/mutation-report.json` (the P5 re-measure, the newest
generator report in the worktree; 5,897 tests discovered, Stryker 4.16.0, 12 logical cores).
"Scoreable" = `Killed | Survived | Timeout | NoCoverage`, the same set `Assert-MutantsWereTested` counts.
"Static" = `"static": true` on the mutant object.

| File | Scoreable | Static | Static % | Static Killed | Static Survived |
|---|---:|---:|---:|---:|---:|
| `Pipeline/ConstructorSelector.cs` | 87 | 50 | 57.5 % | 48 | 2 |
| `Pipeline/BlittableProof.cs` | 88 | 27 | 30.7 % | 13 | 14 |
| `Diagnostics/LocationInfo.cs` | 5 | 5 | 100 % | 5 | 0 |
| `Collections/EquatableArray.cs` | 21 | 3 | 14.3 % | 3 | 0 |
| **Leg** | **201** | **85** | **42.3 %** | **69** | **16** |

Non-static scoreable: 116 (95 Killed, 14 Survived, 7 NoCoverage). Leg score 164/201 = 81.59 %.

**This confirms the ledgers exactly.** `stryker-config.json`'s NOTE 2 and `T3-mutation-survivors.md` both
record "85 of the 201 scoreable mutants are static" with the per-file split 50/87, 27/88, 5/5, 3/21. The
P5 re-measure reproduces all five figures unchanged, three days and twenty kills later. The static
*population* has not moved; only its Killed/Survived split has.

### Cross-leg, from the same worktree's reports

| Leg | Report | Tests | Scoreable | Static | Σ `coveredBy` over non-static | Wall-clock (config) |
|---|---|---:|---:|---:|---:|---:|
| generator | `2026-08-22.01-14-14` | 5,897 | 201 | **85** | 1,696 | **21:19** |
| runtime | `2026-08-21.23-10-31` | 5,857 | 113 | **22** | 1,763 | 8:08 |
| doctooling | `2026-08-21.23-56-18` | 4,846 | 284 | **0** | 2,516 | 4:36 |

The ordering is the whole argument in one table: DocTooling has the **largest** mutant population and the
**shortest** wall-clock, because none of its mutants is static. The runtime leg sits in between, and its
22 statics are the ones the config already explains — `DwarfMapperRegistry` is populated by
`[ModuleInitializer]` code that runs at assembly load, before any test.

**Every static mutant in all four reports has an empty `coveredBy`.** The distribution is total: of the 201
generator mutants, the 85 static ones have `coveredBy: []` and the 7 `NoCoverage` ones do; every other
mutant has a non-empty covering set (1 to 129 tests, mean 14.6). Static and "no per-test attribution" are
the same fact in this report.

---

## 2. What it costs — the arithmetic

### 2.1 The bound that needs no assumptions

- The 116 non-static scoreable mutants have **1,696 test executions between them** (the sum of `coveredBy`
  sizes). The initial serial run of this suite measures **203 s for 5,798 tests** (`T3-mutation-survivors.md`,
  phase table) ≈ 28.6 tests/s, so 1,696 executions ≈ **59 s of single-threaded test time**, total, for the
  whole non-static population.
- The 16 **surviving** static mutants cannot bail. Stryker's bail (on by default) aborts a mutant's session
  at the first failing test; a mutant nothing kills runs its assessing set to completion. Their assessing
  set is every test, so they cost **16 × 5,897 tests ≈ 16 × 206 s = 3,296 s** of single-threaded test time.

So, before any model of concurrency, per-mutant overhead or bail position: **the static survivors alone are
≈ 56× the entire non-static population's work.** That ratio is invariant to concurrency, because both sides
scale with it. Add the 69 killed statics — each of which runs some prefix of all 5,897 tests rather than a
14-test covering set — and the non-static population is arithmetic noise in this leg.

The oft-quoted "~493,000 test executions" is the ceiling: 85 × 5,797 (the T3 run's suite) if none bailed. At
the P5 suite size the ceiling is 85 × 5,897 = **501,245**. Neither number is what actually ran; 16 mutants ×
5,897 = **94,352 executions is the part that provably ran to completion.** Prefer the second when quoting.

### 2.2 The share of wall-clock — estimate, with the assumptions named

The only phase breakdown that exists is T3's, for the 21:02 run:

| Phase | Cost | Static-sensitive? |
|---|---:|---|
| Analysis + solution build | 0:45 | no |
| Initial (serial) test run, 5,798 tests | 3:23 | no |
| Mutate + compile + rollback of the WHOLE project | 0:46 | no |
| Coverage capture | 1:29 | no |
| **Mutation testing, 190 tested mutants** | **14:39** | **yes** |
| Total | 21:02 | |

Fixed overhead is **6:23 (30 %)** and is paid whatever the mutants look like — Stryker mutates and compiles
the whole project regardless of `mutate`. The mutation-testing phase is **14:39 (70 %)**.

Carrying that structure onto the P5 run (21:19, 5,897 tests) and taking Stryker's concurrency as 6
(`ProcessorCount / 2` on this 12-core machine — the figure `T3-mutation-survivors.md` uses when it notes a
4-core hosted runner gets 2):

- fixed ≈ 386 s → mutation phase ≈ **893 s wall** ≈ 5,358 s of single-threaded work at concurrency 6;
- 16 static survivors: **3,296 s (62 % of the work budget)**;
- 116 non-static mutants: **≈ 59 s (1.1 %)**;
- residual for the 69 killed statics: ≈ 2,003 s → ~29 s each → they bail at ≈ 14 % of the suite on average.

**Estimate: static mutants are ~99 % of the mutation-testing phase, i.e. ~14:50 of the 21:19 leg, ~70 % of
wall-clock.** The residual line is the soft one — it is a subtraction, so it absorbs every modelling error
(per-mutant test-host startup, imperfect packing across the six runners, the difference between the T3 and
P5 phase profiles). The 62 % and 1.1 % lines are direct arithmetic on measured inputs.

**If attribution were perfect** and the 85 statics had covering sets like the rest (mean 14.6 tests), the
tested set would be ~2,940 executions ≈ 103 s serial ≈ 17 s wall, and the mutation phase would collapse to
per-mutant session overhead — roughly 1–2 minutes. The leg would land near **8 minutes: a saving of ~13
minutes**, which agrees with the plan's "potential −14 min nightly" to within the model's precision.

### 2.3 The hosted runner makes it worse, and that is worth knowing

The nightly matrix runs on `ubuntu-latest` (4 cores → Stryker concurrency 2). The fixed 6:23 barely moves,
but the mutation phase roughly triples: ~893 s at 6 becomes ~2,680 s at 2, so the leg plausibly runs
**45–60 minutes hosted** where it runs 21 locally. The job's `timeout: 200` covers that with room. This is
context for "leave it": leaving it is a choice about roughly an hour of hosted nightly wall-clock on a tier
that ruling (c) declared unbounded — not about 21 minutes.

---

## 3. Why Stryker does this — the mechanism

### 3.1 What the documentation says

From <https://stryker-mutator.io/docs/stryker-net/configuration/>, `coverage-analysis`:

> **perTest**: "capture the list of mutants covered by each test. For every mutant that has tests, only the
> tests that cover the mutant are used to test a mutant." (**Default: `perTest`**)
> **all**: "capture the list of mutants covered by a test. Test only the mutants covered by unit tests."
> **off**: "coverage data is not captured. All unit tests are run against all mutants."

and, on static code:

> "mutants that are executed as part of some static constructor/initializer are run against all tests as
> Stryker cannot reliably capture coverage for those."

From <https://stryker-mutator.io/docs/mutation-testing-elements/static-mutants/>:

> "A static mutant is a mutant that is executed once on startup instead of when the tests are running."
> … "test filtering is limited since per test coverage cannot be determined."

Two adjacent flags matter to the cost and are quoted here so nobody proposes them as fixes later.
`disable-bail`: "Stryker aborts a unit testrun for a mutant as soon as one test fails because this is enough
to confirm the mutant is killed" — this is the default and it is why a *surviving* static costs ~7× a killed
one. `disable-mix-mutants`: "Stryker combines multiple mutants in the same testrun when the mutants are not
covered by the same unit tests. This reduces the total runtime" — an optimisation static mutants are
structurally excluded from (§3.2, last step).

### 3.2 The chain, from Stryker.NET's source (`master`, tool version in CI is 4.16.0)

1. **Runtime registration** — `src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs`, injected
   into the mutated assembly:

   ```csharp
   private static void RegisterCoverage(int id)
   {
       lock (_coverageLock)
       {
           if (!_coveredMutants.Contains(id)) { _coveredMutants.Add(id); }
           if (MutantContext.InStatic() && !_coveredStaticMutants.Contains(id)) { _coveredStaticMutants.Add(id); }
       }
   }
   ```

   `MutantContext` (`InjectedHelpers/Coverage/MutantContext.cs`) is a `[ThreadStatic] private static int
   depth` incremented by a `using` scope; `InStatic()` is `depth > 0`. The orchestrator places that scope
   via `PlaceStaticContextMarker`, and per Stryker's own technical reference the two orchestrators that do
   so are `StaticFieldDeclarationOrchestrator` and `StaticConstructorOrchestrator` — **static initialisers,
   not static methods**.

2. **Collection** — `src/Stryker.DataCollector/CoverageCollector.cs` is an in-proc VSTest data collector.
   At each `TestCaseStart` it calls `CaptureCoverageOutsideTests()` ("see if any mutation was executed
   outside a test"), which sweeps up everything registered since the previous `TestCaseEnd`; at
   `TestCaseEnd` it publishes `"coveredMutations;staticMutations"` plus, separately, the leaked set.

3. **Classification** — `src/Stryker.TestRunner/Results/CoverageRunResult.cs` turns those three lists into
   flags from `Stryker.Abstractions/Testing/MutationTestingRequirements.cs`:

   ```csharp
   Static = 1,               // mutation is static or executed inside à static context
   CoveredOutsideTest = 2,   // mutation is covered outside test (before or after)
   ```

4. **Both become "static"** — `src/Stryker.Core/Stryker.Core/CoverageAnalysis/CoverageAnalyser.cs`,
   `ParseResultForThisMutant`:

   ```csharp
   if (!resultingRequirements.HasFlag(MutationTestingRequirements.Static)
       && (mutationTestingRequirement.HasFlag(MutationTestingRequirements.Static)
           || mutationTestingRequirement.HasFlag(MutationTestingRequirements.CoveredOutsideTest)))
   {
       resultingRequirements |= MutationTestingRequirements.Static;
   }
   ```

5. **Assessed by every test** — same file, `CoverageForThisMutant`:

   ```csharp
   else if (resultTingRequirements.HasFlag(MutationTestingRequirements.Static) || mutant.IsStaticValue)
   {
       // static mutations will be tested against every tests, except the one that are trusted not to cover it
       mutant.CoveringTests  = allTestsGuidsExceptTrusted.Merge(testGuids);
       mutant.AssessingTests = allTestsGuidsExceptTrusted.Merge(assessingTests).Excluding(failedTest);
       mutant.IsStaticValue  = true;
   }
   ```

   `allTestsExceptTrusted` degenerates to `TestIdentifierList.EveryTest()` when no test reported
   `CoverageConfidence.Exact` — which is what the reports show, since every static mutant serialises with an
   empty `coveredBy`.

6. **And cannot be batched** — `src/Stryker.Core/Stryker.Core/MutationTest/MutationTestProcess.cs` pulls
   `m.AssessingTests.IsEveryTest` mutants out of the grouping pass before mutants are mixed into shared test
   runs. A static mutant therefore gets a whole test session to itself *and* runs the whole suite in it.

### 3.3 What the flag is **not**, here

**It is not the `static` keyword.** All four mutated files are static classes or static members, so a
syntactic rule would flag 201 of 201. The report flags 85. Two observations settle it beyond doubt:

- The same *line* carries both: `BlittableProof.cs` L30 (`return a.SpecialType == b.SpecialType && ...`)
  has 2 static mutants and 1 non-static; L37 has 2 and 1; `ConstructorSelector.cs` L281, L286, L288 and L294
  each carry one of each.
- The split inside one method follows *reachability*, not syntax. In `ConstructorSelector.Select`,
  L41–L88 — the prologue every invocation executes — is entirely static; L92–L156, the ambiguity and
  diagnostic branches, is entirely non-static. In `BlittableProof`, the `CanReinterpret` entry (L16), the
  `LayoutIdentical` guard clauses (L26–L30) and the whole of `IsPrimitive` (L57–L58, 13 mutants) are static,
  while the field-sorting comparator (L78–L86) and `IsSourceSequential` (L97–L110) are not.

The rule that fits the data is: **a mutant is flagged when Stryker observed at least one of its executions
outside a per-test capture window** — which the hottest code, executed by hundreds of tests, is
overwhelmingly likely to hit at least once, and cold branches are not.

### 3.4 Which of the two channels fires here — undetermined from disk, and here is the experiment

The report carries one boolean; `Static` (a `MutantContext` scope) and `CoveredOutsideTest` (a leak between
test windows) are indistinguishable in it. What the worktree *does* let me rule out:

- **Not xUnit parallelism.** Stryker's generated runsettings
  (`src/Stryker.TestRunner.VsTest/VsTestContextInformation.cs`, `GenerateRunSettings`) emit
  `<DisableParallelization>true</DisableParallelization>` for xUnit and MsTest and `MaxCpuCount` 1 for the
  run. Tests are serial during coverage capture.
- **Not a static harness invocation.** `GeneratorTestHarness`'s only static state is a
  `Lazy<MetadataReference[]>` of metadata references; no test type runs the generator from a static
  constructor or field initialiser (`grep` over `tests/` finds exactly one static constructor in the whole
  tree, in `RegistryPropertyTests`, and no `IClassFixture`/`ICollectionFixture` anywhere).
- **Not theory-data enumeration.** The 55 `[MemberData]` providers yield seeds, ids and source-string case
  records; none invokes a generator driver (`FeatureInteractionCompileMatrixTests.BuildCases`, the largest,
  yields raw C# strings).
- **Not a static initialiser inside the mutated assembly.** `DwarfMapper.Generator`'s static field
  initialisers are `DiagnosticDescriptor` constructions and name constants; none calls into the pipeline.

That leaves the leak channel with an unidentified trigger — most plausibly work that outlives a
`TestCaseEnd` or precedes the first `TestCaseStart` in the test host. **The deciding experiment**, for
whoever wants it: run the generator leg once with Stryker's trace verbosity and read the coverage log —
`CoverageAnalyser` logs `"Mutant {MutantId} will be tested against ({TestCases}) tests"` per mutant, and the
collector emits its own `CoverageLog` sink messages per test; whether a flagged mutant arrives via
`detectedStaticMutations` or via the leaked list is visible there. That is one ~21-minute run on a quiet
machine and it is the only thing that would turn this section's "plausibly" into a fact. It was **not** run
here: this task is read-only by instruction.

**The honest verdict on K3's exit criterion:** the mechanism is named with evidence (§3.2, source-cited);
the specific trigger in this repo is **not identifiable from data on disk**, and recovery is therefore
**not demonstrable within this task's constraints**. §5 explains why it would probably not be worth acting
on even if it were.

---

## 4. The coverage-analysis setting in this repo

| Config | `coverage-analysis` | In effect |
|---|---|---|
| `stryker-config.json` (generator) | absent | `perTest` (Stryker's documented default) |
| `stryker-config.runtime.json` | `"perTest"`, explicit | `perTest` |
| `stryker-config.doctooling.json` | absent | `perTest` (default) |

The default is documented as `perTest`, and the generator report corroborates that it is actually in
force — a run without coverage capture could produce neither the non-empty `coveredBy` sets nor the 7
`NoCoverage` classifications, and T3's phase table records a distinct 1:29 "coverage capture" phase.

**No other mode helps; both make it worse.** By the quoted definitions, `all` captures coverage only to skip
uncovered mutants, so every *covered* mutant would run the whole suite — the leg's 116 well-attributed
mutants would join the 85 in paying full price. `off` runs all tests against all mutants and additionally
**changes the scoreable population**: `NoCoverage` ceases to exist as a classification, the denominator
moves from 201, and every `break` derived from it becomes incomparable. That is a ratchet consequence, not
just a wall-clock one. The generator leg is already on the fastest of the three modes.

---

## 5. Options

| # | Option | Wall-clock effect | Cost | Risk | Verdict |
|---|---|---|---|---|---|
| 1 | **Leave it; document the cost** | none | one paragraph | none | **Recommended** |
| 2 | Change `coverage-analysis` | worse (`all`), much worse (`off`) | trivial | `off` moves the denominator and invalidates `break` | Rejected — strictly worse |
| 3 | `// Stryker disable` the 15 adjudicated-equivalent statics | −8 to −9 min (est.) | small edit | — | **Ruled out by ruling (b)** |
| 4 | De-static the hot generator members | **none** | product churn | product change for tooling | Rejected — ineffective *and* appeasement |
| 5 | Split the leg into per-file parallel CI jobs | −~6 min on the critical path (est.) | 4 configs, 4 ratchets, 4× fixed compute | config drift; needs a maintainer ruling | The only ruling-clean lever |
| 6 | Kill the static survivors | −8 to −9 min (est.) | — | — | Closed: 15 of 16 are proven-equivalent |
| 7 | Raise `concurrency` | small | trivial | R21-3 measured core contention | Last resort, as T3 already said |
| 8 | Recover attribution at the cause | −~13 min (est.) | unknown; trigger unidentified | moves assessed-test sets *down* | Not demonstrable; see the warning below |

### 5.1 Where the cost actually sits, and why options 3 and 6 collapse into each other

Of the 16 static survivors — the mutants that cannot bail and so dominate everything — **14 are in
`BlittableProof`** (L28 ×1, L29 ×1, L58 ×12) and 2 are in `ConstructorSelector` (L58, L88). Cross-checked
against `Issues/ledgers/equivalent-mutants.md` on the r22 branch:

- the 14 `BlittableProof` rows and `ConstructorSelector` L58 (`c.Parameters.Length > 0` → `>= 0`) are
  **`proven-equivalent`** entries with written case analyses — **15 of the 16**;
- `ConstructorSelector` L88 (`useObjectInitializerOnly = true` → `false`, a Boolean mutation) is the "L88
  flag question" the same ledger files among the **open maintainer dead-code questions** (research Q2), not
  an adjudicated equivalent.

So the leg's dominant nightly cost is 15 mutants that are unkillable *by proof* and one that is awaiting a
ruling. "Kill the survivors" — normally the house answer to everything on a mutation leg — cannot reach
them; that is what makes this a wall-time question rather than a coverage one. And the only mechanism that
would take them out of the run is an in-source `// Stryker disable`, which **ruling (b) rejects as score
mechanics**. The single existing `// Stryker disable all` (the `DocSnippetInjector` progress guard, 7
Ignored mutants) is grandfathered *and* exactly pinned by P1's scan precisely so no new one can ride in
under the exception. Option 3 is listed for completeness and is closed.

### 5.2 Option 4 fails twice over

Restructuring `ConstructorSelector`/`BlittableProof` into instance members is the intuitive fix and it is
wrong on the evidence. §3.3 shows the flag tracks *when Stryker observed the code executing*, not member
staticness — the same line yields both a static and a non-static mutant. Removing the `static` keyword
changes the syntactic `InStaticValue` path, which §3.3 already established is **not** the path these 85
mutants take. So the change would very likely buy nothing at all.

It is also the shape the house rule names: a product change whose only motive is a testing-tool artefact.
Both grounds are sufficient; the first is the stronger one, because it means even a maintainer willing to
suspend the rule would be paying product churn for an unmeasured, probably-zero return. Reject on the
evidence first.

### 5.3 Option 5 — the split, quantified

The nightly already runs the three legs as a parallel matrix (`ci.yml`, `strategy.matrix` over
`leg: runtime | generator | doctooling`, per-leg `timeout-minutes`), so the leg *is* the critical path and
splitting it further is the only structural lever left. Per-file jobs, using §2.2's model:

| Job | Static survivors | Mutation-phase work | Phase wall @6 | + fixed 6:23 |
|---|---:|---:|---:|---:|
| `BlittableProof` | 14 | ~3,291 s | ~9:09 | **~15:30** |
| `ConstructorSelector` | 2 | ~1,824 s | ~5:04 | ~11:30 |
| `EquatableArray` | 0 | ~50 s | ~0:10 | ~6:35 |
| `LocationInfo` | 0 | ~30 s | ~0:05 | ~6:30 |

Critical path **21:19 → ~15:30, a ~6-minute saving** (estimate), bought with 4× the ~6:23 whole-project
mutate-and-compile (~19 extra runner-minutes per night), four `break` values and four `low` values to move
in lockstep with every re-measure, four non-vacuity assertions, and four sets of config NOTEs to keep
honest. The union of the four `mutate` lists is identical to today's, so nothing is *narrowed* — but the
mutation-leg-integrity ruling says "never narrow `mutate`", and whether a union-preserving split honours it
in spirit is a maintainer call, not an agent's. Note also that the split does not attack the cause: 87 % of
the remaining critical path is still those 14 equivalent mutants, now alone in their own job.

### 5.4 Option 8 — the warning that outranks the saving

Even if the trigger were found and per-test attribution recovered, the change would move every affected
mutant's **assessing set from "every test" to a computed subset**. That is the direction this repository has
rejected, in writing, twice: `stryker-config.runtime.json` records that scoping the runtime leg to its
23 known killer classes cut it to ~3 minutes and was **deliberately rejected**, because "when a test becomes
the sole killer of a mutant, an exclusive filter drops that kill, the SCORE GOES UP, and coverage goes
down" — and it records the measurement that proved the derivation blind to 3 of 26 killers. An attribution
fix is not a hand-maintained list, so it is not the same object; but it has the same failure direction, and
the current over-assessment is the conservative side of it. Any such change would need its score re-measured
against the 81.59 % baseline mutant-for-mutant, and a score that *rose* would be evidence of a problem, not
of success.

---

## 6. Recommendation

**Leave it, and write the cost down where the leg is read.** Concretely: one sentence in
`stryker-config.json`'s NOTE 2 recording that ~99 % of the mutation-testing phase and ~70 % of wall-clock is
the static set, that 15 of the 16 static survivors are proven-equivalent and therefore permanent, and that
ruling (c) makes this affordable — plus a pointer to this document for the mechanism. Nothing else changes.
If wall-time ever *does* start to matter, option 5 (per-file split) is the only ruling-clean lever and its
price is written down above.

**Cost if wrong.** If this verdict is wrong, the loss is ~13 minutes per night on a tier the maintainer has
declared unbounded, on jobs that already run in parallel and whose timeout has 10× headroom — recoverable at
any time by running the one trace-verbosity experiment §3.4 names, since nothing here forecloses it. If the
*opposite* verdict were taken and acted on wrongly, the loss is a product refactor for zero measured gain
(option 4), a relitigated in-source-disable ruling (option 3), or four ratchets that can drift out of step
in the one direction the ratchet exists to prevent (option 5). The asymmetry is the recommendation.

---

## 7. Numbers in the ledgers that this analysis does not reproduce

Flagged per the engagement rule — evidence given, nothing silently substituted.

1. **`stryker-config.json`, P5 paragraph: "66 in-scope compile errors excluded" is mislabelled.** The
   P5 report has **66 `CompileError` mutants project-wide** and **42 inside the four `mutate`d files**
   (`EquatableArray` 2 + `LocationInfo` 0 + `BlittableProof` 24 + `ConstructorSelector` 16). The same
   config's earlier 2026-08-19 paragraph says "42 in-scope compile errors excluded" and is correct as
   written. Both numbers are right; the word "in-scope" is attached to the wrong one. Suggested wording:
   "66 project-wide compile errors rolled back, 42 of them in the mutated files".

2. **Plan K3 mixes two runs: "14:39 of the leg's 19:41".** The 14:39 mutation-testing phase is from T3's
   **21:02** run (`T3-mutation-survivors.md`, "Where the 21 minutes goes"); the 19:41 is the H5
   re-validation, for which no phase breakdown was ever recorded. Quoting them as a ratio overstates the
   static share (14:39 of 21:02 = 70 %; of 19:41 it would be 74 %). Use 14:39 / 21:02, or the P5 estimate in
   §2.2.

3. **"~1,580 for every covered mutant put together" has drifted, and 5,798 with it.** Measured on the P5
   report the figure is **1,696** test executions over 116 non-static mutants, and the suite is **5,897**
   tests, not 5,798. Neither is an error — both are correct for the 2026-08-19 run the sentence describes —
   but `stryker-config.doctooling.json`'s NOTE 2 repeats 5,798 and "it costs 21 minutes" as present tense.
   The 21 minutes happens to still hold (21:19).

4. **`stryker-config.doctooling.json` NOTE 2's "1,603 test executions in total" is not reproducible from any
   report in the worktree.** The run that NOTE describes (`2026-08-21.23-32-50`, 193 Killed + 54 Survived +
   37 NoCoverage) sums to **1,717**, over 39 distinct tests; the newer P3 run (`23-56-18`) sums to 2,516 over
   70 distinct tests. 1,603 may well be exact for the superseded 2026-08-19 report, which is not on disk
   here — recorded as *not reproducible*, not as wrong.

5. **A coincidence of cardinality that must not be read as a match.** `equivalent-mutants.md`'s
   discrepancy-flag section pins **16** proven-equivalent generator mutants (`BlittableProof` L28 ×1 +
   L29 ×1 + L58 ×12, plus `ConstructorSelector` L58 and L243) against the "15 proven" the research and plan
   carried — and its row-level arithmetic is independently sound. The static-survivor set measured here is
   also 16, but it is **a different set**: `BlittableProof` L28 + L29 + L58 ×12, plus `ConstructorSelector`
   **L58 and L88**. The two sets overlap in 15 members. `ConstructorSelector` L243 (`src.IndexOf('.') >= 0`
   → `> 0`) is proven-equivalent but **not static** in the P5 report; `ConstructorSelector` L88
   (`useObjectInitializerOnly = true` → `false`) is static but **not adjudicated** — it is the "L88 flag
   question" the same ledger files among the open dead-code questions. Anyone quoting "16" must say which
   16. The defensible statement, and the one §5.1 uses, is **15 proven-equivalent static survivors plus one
   open question**.
