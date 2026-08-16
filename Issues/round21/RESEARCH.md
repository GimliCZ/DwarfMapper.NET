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

## R21-2 — Should the runtime mutation leg cover the generator's runtime dependencies?

Deferred from round 19. `DwarfMapper.Generator.Tests` was excluded from the runtime mutation leg to keep the
run to 44 minutes; the measured cost was **zero** (`DwarfMapValidationException`'s constructors are bare
`: base(…)` forwarders and produce no mutants).

That measurement holds for today's mutant set. If R21-1 makes the leg substantially faster, the exclusion may
stop being necessary at all, and the question becomes moot rather than answered.
