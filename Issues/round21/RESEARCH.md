<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 21 — research items

Deferred deliberately. Nothing here is a defect with a known fix; each is a question worth measuring before
anyone changes code.

---

## R21-1 — Why does a 111-mutant run take 44 minutes?

**The observation.** The runtime mutation leg (`stryker-config.runtime.json`) tested 111 mutants in **44:02**.
That is ~24 seconds per mutant, against a library whose actual work — mapping one object — takes nanoseconds.

**The maintainer's reading:** something is four orders of magnitude off, and it points at the test code rather
than at the scale of the run.

**Where that reading is right, and where the arithmetic does not carry.** The conclusion is very likely
correct; the stated mechanism probably is not, so recording both matters or the research starts in the wrong
place.

A mutation run's cost is not *N mutants × mapping time*. Stryker compiles every mutant into one assembly behind
an `ActiveMutation` switch, then per mutant **starts a test host, discovers tests, and runs the covering
set**. Host startup and discovery alone are seconds, and they are paid 111 times regardless of how fast the
library is. So a slow run does not by itself imply badly written code — it implies **the covering set per
mutant is large, or expensive, or both**.

---

## MEASURED, 2026-08-16 — hypothesis 1 confirmed, 2 and 3 refuted

Done by re-parsing the existing `StrykerOutput/2026-08-16.18-50-35/reports/mutation-report.json`. **No re-run,
no rebuild.** The hypotheses below are kept for the record; the numbers supersede them.

### The tests are not slow

| | |
|---|---|
| Total test executions across 111 scoreable mutants | **124,593** |
| Wall-clock | 2,642 s |
| **Per test execution** | **21.2 ms** |

21 ms per execution is *fast* for a suite that compiles Roslyn in places. **There is no four-orders-of-magnitude
coding problem.** The run is long because of how many executions there are, not how slow each one is.

### Two files account for 99.8% of the cost

| File | Mutants | Test executions | Share |
|---|---:|---:|---:|
| `DwarfMapperRegistry.cs` | 49 | 67,836 | **54.4%** |
| `DwarfRefContext.cs` | 19 | 56,541 | **45.4%** |
| `DwarfMappingDepthException.cs` | 5 | 100 | 0.1% |
| `DwarfMapExceptions.cs` | 33 | 93 | 0.1% |
| `IDwarfMapper.cs` | 5 | 23 | 0.0% |

The other three files — **43 mutants** — cost **216 executions between them**. Median covering-test count
across all mutants is **13**; the maximum is **5,589 of 5,592**. Coverage analysis works fine almost everywhere
and collapses completely on two types.

### The decisive number

Of the **5,592 tests that "cover"** those two files, exactly **91 distinct tests ever killed one of their
mutants.**

That is a **~61× waste factor**, and it is not incidental to the design — it *is* the design:
`coverage-analysis: perTest` measures **touch, not intent**. `DwarfMapperRegistry` is populated by module
initializers in every test assembly, and `DwarfRefContext` is threaded through every reference-preserving map.
Essentially every test in the suite touches both **without asserting anything about either**. Such a test can
only kill a mutant by accident, and it costs the same as one that was written to.

Where the real killers live (the whole list is short): `ConsumerSurfaceTests` (70 + 14 + 11 + 10 + 10 kills),
`CollectionShapeRegistrationTests` (18), `CrossConfigFuzzTests` (14), `TopologyOracleFuzzTests` (11 + 6),
`RegistryInterfaceLookupTests` (6), `SetNullAdversarialRuntimeTests` (5), `RegistryConcurrencyTortureTests` (4).

### What this means for the three hypotheses

- **(1) `perTest` coverage does not narrow for the registry — CONFIRMED**, and it is the whole story.
- **(2) The torture suite is not the cause — REFUTED.** `RegistryConcurrencyTortureTests` accounts for 4 kills
  and no meaningful share of the cost. The 60→240 round change was not the problem.
- **(3) Ordinary test-harness inefficiency — REFUTED** at 21.2 ms per execution.

### The actionable shape

Do **not** "make the tests faster" — they are fast. Scope the mutation leg's test set to the classes that
exercise the registry *intentionally*, via a Stryker test filter or a dedicated project. On these numbers that
should take the leg from **44 minutes to low single-digit minutes**, at little or no cost in killed mutants,
since 91 tests already do all the killing.

That matters because of `CARRY-FORWARD.md` §5b.1: **the leg runs in no CI job at all.** A 44-minute leg nobody
runs is worth less than a 3-minute leg on every push. Making it fast is what makes the score real.

**Open question for round 21, and it is a genuine one:** cutting to the intentional tests would have missed any
mutant killed *only* by an accidental toucher. Whether that ever happens here is answerable from the same JSON —
check whether each killed mutant's `killedBy` set contains at least one test from the intentional list.

---

### Original hypotheses (superseded, kept for the record)

**Three hypotheses, in the order worth testing:**

1. **`coverage-analysis: perTest` is probably not narrowing anything for the registry.**
   `DwarfMapperRegistry` is a **process-wide static populated by module initializers**. Every test in the
   chosen projects that touches a generated mapper causes registration — so essentially *every* test "covers"
   `DwarfMapperRegistry.cs`. Per-test coverage analysis can only narrow when tests touch disjoint code; here
   the coverage matrix may be close to dense, and each of the ~60 registry mutants then re-runs nearly the
   whole project. **Check the JSON report's per-mutant covering-test counts first** — this is one query and it
   either confirms or kills the hypothesis immediately.

2. **The torture suite may be inside the covering set, and it was made 4× longer in the same round.**
   Round-19 Task 10 raised `RegistryConcurrencyTortureTests` from 60 to 240 rounds to get detection power up
   (2/60 → 23–58/240 per round), in a collection marked `DisableParallelization`. Those tests live in
   `DwarfMapper.IntegrationTests` — **which is the project selected as the mutation `test-projects`.** If the
   torture suite covers registry mutants, every registry mutant re-runs 240 serial rounds of thread contention.
   That is not bad code; it is **two locally-correct decisions from different tasks interacting badly**, and it
   would dominate the wall-clock on its own.

3. **Only then, ordinary test-harness inefficiency.** Per-test fixture construction, `Thread.Sleep`-style
   waits, unbounded retries. Worth checking, but it is the third place to look, not the first.

**Why this is research and not a fix.** If (2) is the cause, the remedy is a trade-off, not a cleanup: exclude
the torture collection from the mutation leg and you lose the concurrency mutants it is uniquely able to kill.
That is a real decision about what mutation testing is *for* here, and it should be made from measurements
rather than from a stopwatch reading.

**What would settle it, cheaply:**
- Per-mutant covering-test counts from `StrykerOutput/**/reports/mutation-report.json` — no re-run needed.
- Wall-clock of `dotnet test` on `DwarfMapper.IntegrationTests` alone, with and without the
  `registry-torture` collection. Two runs, minutes each.
- Multiply: if `torture-suite-time × registry-mutant-count` lands near 44 minutes, hypothesis (2) is confirmed
  without ever re-running Stryker.

**Related, and probably the real prize:** whatever the answer, **the mutation leg runs in no CI job at all**
(`CARRY-FORWARD.md` §5b.1). A 44-minute leg nobody runs is worth less than a 5-minute leg that runs on every
push. Making it fast enough to automate is the outcome that matters — the score itself is secondary.

---

## R21-3 — Would parallelisation help? (measured 2026-08-16)

**Short answer: a little, and it is the wrong lever. Scoping beats it outright, and one of the two available
parallelism knobs is load-bearing for correctness.**

### What is already parallel, and what is not

| Layer | State | Detail |
|---|---|---|
| **Stryker across mutants** | **already on** | `concurrency` is not set in any of the three configs, so it takes Stryker's default of `ProcessorCount / 2` — **6 of this machine's 12 logical CPUs**. |
| **xUnit within each test host** | **fully OFF** | `tests/DwarfMapper.IntegrationTests/AssemblyInfo.cs:8` — `[assembly: CollectionBehavior(DisableTestParallelization = true)]`. Every test in the assembly runs serially, inside every one of the ~111 test-host runs. |

### The reason it is off is real — and its cost justification has silently expired

The attribute carries its own rationale, and it is a good one:

> *The security regression tests temporarily swap `CultureInfo.CurrentCulture` (to prove our generated
> `Parse`/`ToString` stay invariant). That mutation is process-thread-wide, so running tests in parallel could
> let the de-DE window bleed into a concurrently-running test. Disabling parallelization keeps the suite
> deterministic;* **it costs little (the whole integration suite runs in well under a second).**

That last clause was true when written and is **no longer true in the context it now also governs.** Under
`dotnet test` the suite runs **once**. Under the mutation leg it runs **once per mutant**, and for the two hot
files against essentially the whole 5,592-test set. A cost that was negligible × 1 is not negligible × 111.

This is the same shape as most of what round 19 found: **a narrow, correct constraint applied at too broad a
scope, with a cost justification that expired without anything noticing.** The constraint is needed by a
handful of culture-swapping tests; it is imposed assembly-wide.

### The three levers, ranked

1. **Scope the leg to the intentional tests — do this first.** 91 tests do all the killing; 5,592 pay for it
   (§ *The decisive number*). That is a **~61× waste factor**, and no amount of parallelism removes waste — it
   only buys more machines to do it on. Zero correctness risk.
2. **Isolate the culture-swapping tests into their own `[Collection]`** and let the rest of the assembly run in
   parallel. This is the standard xUnit remedy and it preserves the exact property the comment protects: the
   de-DE window stays inside a serial collection, everything else parallelises. This is the *right* fix for
   parallelism, and it is worth doing on its own merits — it also speeds up ordinary `dotnet test`.
3. **Raise Stryker `concurrency`** from the default 6 toward 10–12. Cheapest to try, smallest ceiling, and it
   **competes with lever 2 for the same 12 cores** — six test hosts each running multi-threaded xUnit will
   oversubscribe. Pick one axis or tune both together; do not assume they compose.

### Why parallelism cannot be the answer

Even *perfect* 12× scaling takes 44 minutes to **~3.7 minutes**. Scoping alone should reach **~1 minute**, at
no correctness risk and no extra hardware. And the goal (`CARRY-FORWARD.md` §5b.1) is a leg fast enough to run
in CI on every push — where a shared runner has far fewer cores than this machine, so a parallelism-dependent
speedup largely evaporates precisely where it is needed.

**Order of work, if round 21 takes this on:** scope first, measure again, then decide whether either
parallelism lever is still worth its complexity. It may simply stop mattering.

---

## R21-2 — Should the runtime mutation leg cover the generator's runtime dependencies?

Deferred from round 19. `DwarfMapper.Generator.Tests` was excluded from the runtime mutation leg to keep the
run to 44 minutes; the measured cost was **zero** (`DwarfMapValidationException`'s constructors are bare
`: base(…)` forwarders and produce no mutants).

That measurement holds for today's mutant set. If R21-1 makes the leg substantially faster, the exclusion may
stop being necessary at all, and the question becomes moot rather than answered.
