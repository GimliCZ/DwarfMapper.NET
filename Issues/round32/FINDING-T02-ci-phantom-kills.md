<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Finding (round 32 T02): why CI's generator leg scores above its proven ceiling

**Question** (round 32 finding, note C). In nightly run 213, the generator leg reported 98.80 %. That is above
its proven ceiling of 96.62 %, which `scripts/gate-checks.ps1` calls "arithmetically impossible". Why, and does
it affect other legs? Labels follow protocol §7.

## The answer, short

- **Every leg's initial test run had two failing tests:** `EmittedIlIsVerifiableTests`, because CI's `mutation`
  job never installs `ilverify`. This is a real CI defect, and T03 fixes it. [MEASURED and EVIDENCE, §2]
- **The two failures never kill a mutant themselves.** The pinned Stryker, 4.16.0, strips them from every verdict.
  [MEASURED: read from the decompiled binary, §3]
- **They change how every static mutant is tested** [MEASURED, §3]:
  - With no initial failure, a static mutant is tested against "every test", and each test assembly runs once.
  - With any initial failure, it gets an explicit list instead, and the pinned runner runs that whole list once
    per test assembly holding any of it. Every pass is judged, so one spurious failure in any pass kills the
    mutant, even one already reported Survived.
- **This multiplies the static-mutant hazard** that `Issues/ledgers/equivalent-mutants.md` recorded on
  2026-09-23. It fits run 213: only the two legs that mutate the generator exceed their ceilings, and only those
  two show verdicts changing after their first report [INFERRED, §4]. The run's own report would settle it (§6).

**Corrections, 2026-10-02, both made before this file was committed.**
1. The working hypothesis was that the two failing tests "killed" every mutant they cover. In that direct form it
   is wrong (§3, point 1).
2. The first reading of the binary concluded that the two failures therefore changed no verdict. An independent
   read-only review of the same decompiled code found the indirect path (§3, points 2–4), and it was verified line
   by line before this file was written.

The task list's T03 prediction ("reports … 96.62 %") is restated in §6 with the residual hazard allowed for. The
task list itself stays as approved.

## 1. What CI measured [EVIDENCE: run 213's job logs]

Stryker's final score is (Killed + Timeout) / (Killed + Timeout + Survived + NoCoverage), over every mutant
(`ProjectComponent.GetMutationScore` in the decompiled 4.16.0). Its progress tally counts each mutant once, at
its first report (`ProgressBarReporter.ReportRunTest`, behind `MutationTestProcess.OnMutantTested`'s
`reportedMutants` set). So a mutant whose status changes after its first report is tallied under its old status.
The tally also omits mutants decided before testing, such as NoCoverage.

| Leg | Initial-run failures | Tested | Tally K / S / T | Final score | Detected, from the score | Ledger: scoreable, proven-equivalent | Most it can detect | Excess |
|---|---:|---:|---|---:|---:|---|---:|---:|
| generator | 2 | 415 | 409 / 6 / 0 | 98.80 % | 410 (= 82/83) | 415, 14 | 401 | **≥ 9** |
| pipeline | 2 | 284 | 273 / 11 / 0 | 96.83 % | 275 | 284, 15 | 269 | **≥ 6** |
| doctooling | 2 | 290 | 280 / 10 / 0 | 96.55 % | 280 | 290, 10 | 280 | 0 |
| codefixes | 2 | 174, plus 4 NoCoverage | 156 / 18 / 0 | 87.64 % | 156 of 178 | 178, 22 | 156 | 0 |
| testing | 2 | 110 | 106 / 0 / 4 | 100.00 % | 110 | 110, 0 | 110 | 0 |

The runtime leg died earlier, in its config check (T01).

Two patterns, and both involve only the legs that mutate `DwarfMapper.Generator`:

- **They score above what can be proved.** The other three legs land exactly on their ledger values.
- **Their scores disagree with their own tallies,** by one mutant and by two. Mutants changed status after their
  first report. In the other three legs, score and tally agree.

All five legs ran with the same two failing tests. No leg logged an "unexpected test case" warning of either kind
(initial run or coverage run), a "Retrying the test session", or a "VsTest failed". That rules out the
test-id-mismatch and crash-retry paths.

## 2. The two failing tests [MEASURED, EVIDENCE where marked]

- **CI's mutation job lacks the tool** [EVIDENCE: `.github/workflows/ci.yml`]. `dotnet-ilverify` is installed in
  five jobs: `build-test`, `deep-test`, `roslyn-forward-compat`, `cross-platform` and `preview-sdk-canary`. The
  `mutation` job is not one of them.
- **Without the tool, exactly these two tests fail** [MEASURED, on this tree, with the Release build]:

  | `ilverify` on PATH | `dotnet test … --filter "FullyQualifiedName~EmittedIlIsVerifiableTests"` |
  |---|---|
  | no | `Failed: 2, Passed: 0`, both with "ilverify is not on PATH" |
  | yes | `Failed: 0, Passed: 2` |

- **They execute the generator before they fail** [EVIDENCE: read]. Each test calls `Emit`, which runs
  `DwarfGenerator` and `MapToGenerator` over its corpus. Only then does `RunIlVerify` reach
  `Assert.Fail("ilverify is not on PATH …")`.
- **Why the guard missed it** [EVIDENCE: `CiToolPrerequisiteScanTests.cs`]. The guard treats a job as running the
  suite only if its YAML says `dotnet test`, and it reads only `ci.yml`. `dotnet stryker` runs the suite too, and
  `release.yml` runs `dotnet test` without the tool (round 32 finding, item 3). This is the "fourth job" the
  test's own remarks predicted.
- **The other tool is present** [EVIDENCE: the step shells in the same log]. `pwsh` exists on the runner.

## 3. What the two failures do in the pinned Stryker [MEASURED: decompiled `dotnet-stryker` 4.16.0, CI's pin]

The tool package was decompiled with `ilspycmd`. Type and member names below are the decompiled ones.

1. **They never kill directly.**
   - `CoverageAnalyser.CoverageForThisMutant` excludes them from every mutant's assessing tests, ordinary and
     static (`.Excluding(failedTest)`).
   - `MutationTestProcess.TestUpdateHandler` strips them from a session's failures before any mutant is judged.

   One path skips the stripping: a session that times out is re-judged against the raw failures
   (`MutationTestExecutor`). Only a mutant assessed against every test could be killed that way, and here none is.
2. **They switch every static mutant to an explicit list.** `CoverageAnalyser.ParseCoverage` assesses static
   mutants against `EveryTest` only when no test is `Exact` *and* no test failed the initial run. Otherwise it
   uses an explicit list: every test of the coverage run, minus the `Exact` ones, minus the failures.
   - Under "Capture mutant coverage using 'CoverageBasedTest' mode", coverage is captured at `Normal`
     confidence, so no test is `Exact`.
   - So the two failures alone decide between the two forms.
3. **An explicit list runs once per test assembly.** `VsTestRunner.RunTestSession` loops over the leg's test
   assemblies. For each one holding any listed test, it passes the *full* list to `RunVsTest`, which runs every
   listed case. An `EveryTest` session runs each assembly once, by source.
4. **Every pass counts.**
   - A session's results accumulate: `RunEventHandler.StartSession` does not clear them.
   - Every results update re-judges every mutant of the batch, and `Mutant.AnalyzeTestRun` has no check that a
     verdict is already final.
   - Once the first complete pass is in, the session is no longer cancelled early.

   So a spurious failure in a later pass kills a mutant that was already reported Survived. That is the
   score/tally gap in §1.

## 4. What this most likely did in run 213 [INFERRED]

The ledger's 2026-09-23 section already saw a generator score above the ceiling, with no failing initial test:
the machine that measured it had `ilverify`.
- That run produced 97.11 % against 96.62 %, through two phantom kills of `IsPrimitive` mutants flagged
  `"static": true`.
- Stryker named `SameBytesIgnoringNames_accepts_two_different_primitives_of_the_same_width` as their killer.
- With each mutant planted permanently in `src/`, 10,146 tests stayed green.

So static mutants pick up spurious kills even when each assembly runs once.

In run 213, the two failures put every static mutant on an explicit list:
- Stryker found nine test projects for these legs [EVIDENCE: the log names them]. So a static mutant's suite
  could run up to nine times in one session, every pass judged.
- More passes mean more chances for a test that fails for a reason unrelated to the mutant: an order dependence,
  a host problem, or a timing test. One example of the last is
  `InterfaceBucketTests.Collection_dispatch_cost_does_not_grow_with_unrelated_registrations`. It is a wall-clock
  ratio test that CI's default lane excludes (`Category=Perf`), while Stryker's runs carry no category filter.
  Whether it failed in run 213 is not known.
- The legs with static generator mutants, generator and pipeline, are the two above their ceilings and the two
  where verdicts flipped late.

## 5. Local reproduction attempt [MEASURED, incomplete]

The plan was two arms. Each scopes the generator leg to 15 character spans of `Pipeline/BlittableProof.cs`: the
lines of 10 of the 14 proven-equivalent mutants (7 ledger rows), and two control lines. It runs CI's way:
`dotnet-stryker` 4.16.0, Debug, no flags. One arm runs without `ilverify` on PATH, the other with it.

Neither arm finished. The shell's memory cgroup is 13.4 GB, and the kernel's OOM report shows the test host at
9.1 GB anon RSS beside Stryker at 4.1 GB. That happened twice: once at default concurrency, once at
`--concurrency 1`. Measured before the kill, without `ilverify`:

- 15,642 mutants created, the same as CI;
- 22 to be tested;
- "3 tests are failing" in the initial run, where CI reported 2.

Two of the three are the ilverify pair, as §2 measured. The third was not identified, because Stryker logs only
the count.

After the run, every `bin/Debug` and `obj/Debug` directory was removed. `Assert-NoMutatedProductBinaries`
reported "22 product assemblies under tests/**/bin, none mutated".

A local generator leg needs more than 13.4 GB, which the owner's machine has.

## 6. What would settle it, and a prediction that can fail

**Settles it:** run 213's generator report, `mutation-report.json`, kept as the job's artifact
`mutation-report-generator` (109,415,879 B; it expires on 2026-12-31).
1. For each of the 14 ledger rows, read `status`, `static` and `killedBy`.
2. For every reported kill, plant the mutant and run the suite, as the ledger requires ("plant, not read").
3. If the extra kills are static mutants whose killers pass with the mutant planted, this finding holds. Their
   `killedBy` also names the spurious tests.

This session cannot download the artifact. The download URL the API returns is refused by its egress proxy
[EVIDENCE: "CONNECT tunnel failed, response 403"]. The owner can download it.

**Prediction.** After T03:
- the initial run prints no "tests are failing" line;
- static mutants go back to `EveryTest` sessions;
- each leg's excess falls back to about what the ledger pins for it. The generator leg reports at most 97.11 %,
  which is the ceiling plus its two pinned phantom kills. The pipeline leg, which pins none, reports at most
  94.71 %.

One run under the line proves little, because the hazard varies from run to run. An excess like run 213's
(generator ≥ 9, pipeline ≥ 6) after T03 refutes this mechanism.

## 7. What it means for the round

- **T03 is the fix for the score as well as the defect,** by this finding. It removes the failures that turn
  static mutants into repeated whole-suite runs.
- **T04 (D2) is still needed.** The static hazard remains at its lower rate, as 2026-09-23 showed. The band check
  is the only instrument that refuses a score above the ceiling, and CI does not run it.
- **T05 (D5) has a concrete reason.** A failing initial run changes how Stryker tests every static mutant, and
  bends the score upward without failing anything. `break-on-initial-test-failure` makes that a red.
- **The ledger is not changed here.** Run 213 has five survivors where the band check expects 12 (14 proven minus
  2 pinned phantom kills), so housekeeping's check would refuse it. Re-pinning needs the planting the ledger
  demands, and that needs the report.
- **Possibly upstream** [INFERRED]. Passing the full list to each assembly's run looks like a defect in
  Stryker.NET: it runs every listed test once per test assembly. Whether VsTest de-duplicates the cases was not
  verified here. Reporting it upstream is outside this repository, so it is the owner's call.
