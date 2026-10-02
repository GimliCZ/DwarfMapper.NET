<!-- SPDX-License-Identifier: GPL-2.0-only -->

# DwarfMapper.NET — Round 32 task list (executable)

**Source.** `FINDING-nightly-red-and-gate-gaps.md` (same folder; the item numbers and notes in brackets point
there). Every task below traces to one item of it. This file follows `Issues/ROUND-PROTOCOL.md`; its procedures
G1–G8 apply to every task and are cited, not copied.

**Base.** `feat/round32` from `5561ada5`: master `24a31de`, plus the protocol and the finding.

**Roles** (protocol §1.2):

- **[S]** executes as written;
- **[O]** owns the design forks below and every OPUS-REVIEW;
- **[H]** is the owner.

**Audit.** `round32-audit.sh` (same folder) has three modes:

- `static` checks each task's code-level completion without building;
- `tests` runs the round-32 acceptance tests;
- `baseline` prints the figures the T00 entry records.

## Part 1 — Context for [O]

### 1.1 Repository facts (measured on `5561ada5`, 2026-10-02)

- **Toolchain.** The SDK pin is 10.0.101 (`global.json`, `rollForward: disable`). CI installs `dotnet-ilverify`
  10.0.11 in five jobs. The `mutation` job is not one of them.
- **Who parses the Stryker configs:**
  - CI parses `stryker-config*.json` with Python's `json.load` in two `mutation` steps: the config check, and the
    dashboard publish.
  - `housekeeping.ps1` (`ConvertFrom-Json`) and `RatchetInvariantScanTests` (`JsonDocument`) accept a BOM.
  - Python does not.
- **The six legs.** `RatchetInvariantScanTests` R1 reads three of them (generator, doctooling, runtime).
  `codefixes` carries its latest measurement as "RE-VERIFIED 2026-09-26 … score 87.64 %". `testing` carries its
  latest as "RE-MEASURED 2026-09-21 … = 100.00 %". Neither matches R1's two patterns.
- **How the legs run.**
  - Housekeeping runs a leg with `--configuration Release` (`scripts/gate-checks.ps1`, `Invoke-StrykerLeg`).
  - CI runs `dotnet stryker --config-file …` with Stryker's default, Debug.
  - The comment that pins Release says the two are behaviourally identical, because `src/` has no
    `#if DEBUG`/`Debug.Assert`. That still holds: none was found on 2026-10-02.
- **The band check.** The proven-ceiling rule lives in `Assert-MutationScoreWithinBand`
  (`scripts/gate-checks.ps1`), which `Assert-LegScoreWithinBand` calls. Housekeeping runs it; CI never does.
- **The pinned Stryker.** `dotnet-stryker` 4.16.0, the version CI pins, knows the config key
  `break-on-initial-test-failure`, and prints "{FailingTestsCount} tests are failing. Stryker will continue but
  outcome will be impacted." [MEASURED: strings in the 4.16.0 package's `Stryker.CLI.dll` and `Stryker.Core.dll`]

### 1.2 Invariants

Protocol §3 applies in full. The two that bind this round hardest:

- **I-3.** No threshold, ceiling or allowance moves in this round except where a task below says so.
- **I-1.** No step may quiet a red job. A publish failure, for one, is fixed or decided, never muted.

### 1.3 Traps

- **The "full-ci" story.** The label does not run the dashboard publish step. That step is
  `schedule`/`workflow_dispatch` only. So a `full-ci` PR cannot prove T06; only a dispatch or a nightly can.
- **Workflow pushes.** Pushing a change to `.github/workflows/` may be refused without the `workflow` scope
  (protocol §8). Tasks that touch workflows run last.
- **A cascading red.** Removing the BOM (T01) makes the runtime leg RUN in CI for the first time since
  `ed69922`. Round 31 left that leg red at 90.45 % against `break` 97, with a re-measure owed (round 31 owner
  action 0). The nightly will then show that honest red instead of a parse error. That is expected, not a
  regression of T01.

### 1.4 Decisions required ([O] or owner) before the dependent task

| # | Decision | Needed by | Options | Recommendation |
|---|---|---|---|---|
| D1 | How the dashboard upload fits | T06 | (A) upload the score only; (B) upload the report filtered to scoreable mutants; (C) stop failing the job on publication | B, falling back to A. Reject C: it silences a red (I-1). See T06's research. |
| D2 | Run the band check in CI's `mutation` job | T04 | (A) yes, after every leg; (B) leave it housekeeping-only | A. The band check is the instrument that calls a score above the ceiling impossible, and CI is where the impossible score appeared. |
| D3 | The package-size overrun | T07 | (A) re-measure and raise both ceilings with an entry-by-entry account; (B) find and remove unintended growth first | Decided by T07's measurement. If every byte is attributed to an intended change, A; otherwise B for the unattributed part. |
| D4 | DCO sign-off | T13 | (A) enforce `git commit -s` (a hook or CI check); (B) drop the sentence from `CONTRIBUTING.md` | Owner. Practice has been B since 2026-07-26. |
| D5 | Break a leg when its initial test run fails | T05 | (A) `"break-on-initial-test-failure": true` in all six configs; (B) leave Stryker's warning-only default | A. It turns "outcome will be impacted" from a log line into a red leg (I-1). |

### 1.5 Escalation

Protocol §6. A STOP goes to `## Escalations` in `TASK-LOG.md`, the task is marked `[!]`, and work continues with
the next independent task.

## Part 2 — Procedures

Protocol §5, G1–G8, apply to every task:

- new test files go under `tests/<Project>/Round32/`, in namespace `<ProjectNamespace>.Round32`;
- commit subjects take the form `<type>(round32/<Tnn>): …`;
- the per-task gate is the three projects that compile generated code: Generator.Tests, CompilerTests and
  IntegrationTests.

## Part 3 — Tasks

**Execution order:** T00 · T01 · T08 · T09 · T10 · T11 · T12 · T02 · T07 · T06 · T14 · T03 · T04 · T05 · T13 ·
T15 · T16 · T99.

Tasks that touch `.github/workflows/` (T03, T04, T06) run after every other task (protocol §8).

### T00 [S] Baseline

1. Fresh state on `feat/round32`; toolchain per protocol §8.
2. Run, exactly as CI's `build-test` and `surface-matrix` jobs do: locked restore; Release build; the default
   lane; the surface matrix.
3. Run `./round32-audit.sh baseline` and record the printed figures in the T00 entry of `TASK-LOG.md`.
4. Run `./round32-audit.sh static`. Every task must report TODO, which proves the audit can see work.

**STOP if** the build or any test fails on the untouched tree.

### T01 [S] The runtime config's BOM, test first [note A]

- **Files:** a new `tests/DwarfMapper.Generator.Tests/Round32/StrykerConfigEncodingTests.cs`;
  `stryker-config.runtime.json`.
- **Steps:**
  1. Write the test. For every `stryker-config*.json` at the repository root, it asserts:
     - the file does not start with `EF BB BF`;
     - the file parses as JSON from UTF-8 bytes with a strict decoder. This is the property Python's
       `json.load` needs.

     Non-vacuity: it must find six configs.
  2. Run the test. It must FAIL, naming exactly `stryker-config.runtime.json`.
  3. Run CI's own config-check Python on the file. It must fail with `Unexpected UTF-8 BOM`.
  4. Remove the three BOM bytes, and nothing else (`cmp` against the old file minus three bytes).
  5. Re-run 2 and 3. Both must pass.
- **Audit:** `round32-audit.sh static` reports T01 DONE.
- **STOP if** any other config fails, or the file changes beyond its first three bytes.

### T02 [O] Research: why CI's generator leg scores above its proven ceiling [note C]

- **Output:** `FINDING-T02-ci-phantom-kills.md`.
- **Steps:**
  1. From run 213's job log, take the leg's initial-test-run lines and its summary (killed, survived, timeout,
     tested).
  2. Compare with the ledger's generator row (`Issues/ledgers/equivalent-mutants.md`).
  3. Find which tests fail in that job's initial run, and why.
  4. State the mechanism, a falsifiable prediction for the fixed job, and what would settle the remainder.

  Label every claim per §7.
- **STOP if** the initial run's failing tests cannot be identified from evidence. Report what is known.

### T03 [S] Every CI job that runs the suite installs ilverify [item 3, T02]

- **Files:**
  - `tests/DwarfMapper.Generator.Tests/SelfValidation/CiToolPrerequisiteScanTests.cs`;
  - `.github/workflows/ci.yml` (the `mutation` job);
  - `.github/workflows/release.yml`.
- **Steps:**
  1. Extend the scan in two ways:
     - read every workflow under `.github/workflows/`, not only `ci.yml`;
     - count `dotnet stryker` as running the suite, as `dotnet test` already is.

     Non-vacuity: at least two workflow files, and the existing job-count floors.
  2. Run the scan. It must FAIL, listing `ci.yml:mutation` and `release.yml:release`.
  3. Add `- run: dotnet tool install --global dotnet-ilverify --version <the pinned version>` to both jobs.
     Copy it from `build-test`, so the existing "same version everywhere" test still holds.
  4. Run the scan again. It must PASS. Run `zizmor` over the changed workflows, at the version
     `actions-security.yml` pins: no new finding.
- **Prediction (T02):** the next nightly's generator leg reports the ledger's ceiling, 96.62 %, not 98.80 %.
- **Owed:** a nightly or a dispatch, which is the owner's to trigger.
- **STOP if** the scan finds an offender other than these two.

### T04 [O → D2] The band check runs in CI [note C]

If D2 is A: after each leg in the `mutation` job, run `Assert-LegScoreWithinBand -Leg <leg>` from
`scripts/gate-checks.ps1` against the leg's report. Do this exactly as housekeeping does, and add a meta-test
that every leg in the matrix runs it.

- **RED:** the meta-test fails on the current workflow.
- **Owed:** a nightly or a dispatch.

### T05 [O → D5] A failing initial test run breaks the leg [T02]

If D5 is A:

1. Write a test asserting `"break-on-initial-test-failure": true` in all six configs. RED, then set the key.
2. Prove the behaviour. Plant a failing test in a scratch copy of a fast leg's test project, run
   `dotnet stryker --config-file stryker-config.doctooling.json`, and see it exit non-zero right after the
   initial run. Then remove the plant.

### T06 [O → D1] The dashboard upload [note C]

- **Research, executable now** (`RESEARCH-T06-dashboard-413.md`):
  - what the job uploads;
  - how large the reports are (run 213's artifact sizes from the job logs);
  - what the pinned Stryker's binaries say about score-only uploads;
  - each option's cost against I-1.
- **Fix:** follows D1. It is verifiable only by a dispatch, which is an owner action.

### T07 [O → D3] The package-size overrun [note B]

1. Pack the two shipped packages exactly as CI's `package-size` job does. Do it at `1972246a` (the last
   ceiling move), `b688bb9` (round 30's merge) and `24a31de` (round 31's merge), each in a scratch worktree
   outside the repository.
2. Give an entry-by-entry account of the growth per interval (`FINDING-T07-package-growth.md`).
3. The ceiling move itself waits for D3, because loosening a gate is the owner's (protocol §4).

**STOP if** a pack differs between two runs of the same commit, since then the measurement is not reproducible.

### T08 [S] R1 covers all six mutation configs [item 4]

- **Files:** `tests/DwarfMapper.Generator.Tests/SelfValidation/RatchetInvariantScanTests.cs`;
  `stryker-config.codefixes.json` and `stryker-config.testing.json`, comment wording only.
- **Steps:**
  1. Add the three missing `InlineData` rows.
  2. Run R1. It must FAIL for `codefixes` (no "MEASURED <date>") and `testing` (no "score NN.NN %"). It must PASS
     for `pipeline`.
  3. Make the two comments state their latest recorded measurement in R1's words. No figure, date, threshold or
     run reference changes:
     - "RE-VERIFIED 2026-09-26" becomes "RE-MEASURED 2026-09-26";
     - "= 100.00 %" becomes ", score 100.00 %".
  4. Run R1 again. It must PASS for all six.
- **STOP if** a recorded measurement for a config's current `break` cannot be found in its own comment or in the
  ledger.

### T09 [S] The probe rule is enforced [item 4]

- **Files:** a new `tests/DwarfMapper.Generator.Tests/Round32/ProbeFileScanTests.cs`.
- **Test:** no `ZZ*.cs` file exists under `src/`, `tests/`, `samples/` or `benchmarks/` (skip `bin/` and `obj/`).
  It also checks non-vacuity: the scan sees the repository's `.cs` files.
- **Proof:** it is green on the tree, so it is a guard (G3). Show it failing once: plant `tests/ZZProbe.cs`, see
  RED naming it, delete it, see GREEN. Record both runs.

### T10 [S] Agent worktrees are ignored [item 4]

- **File:** `.gitignore`.
- **RED:** `git check-ignore -q .claude/worktrees/x` fails.
- **GREEN:** after adding `.claude/worktrees/`, it succeeds, and `git status --porcelain` is unchanged.

### T11 [S] The NegativeCases README states the rule the ratchet enforces [item 4]

- **File:** `tests/DwarfMapper.NegativeCases/README.md`.
- **Change:** replace the paragraph that calls `PredatesThisProject` the opt-out. The list is pinned exactly and
  may only shrink, and a new id arrives with a case file. Quote the test's own words.
- **Proof:** the paragraph no longer contradicts `DiagnosticCoverageRatchetTests`, and every test still passes.

### T12 [S] The ledgers README names its capture commit [item 4]

- **File:** `Issues/ledgers/README.md`.
- **Change:** replace the unexpanded `$(git rev-parse --short HEAD)` with the commit the round 19 and 20 copies
  were captured at, `069987f7`, the parent of `656042c2` that added them. State that later ledgers name their
  own capture point in their headers.

### T13 [H → D4] DCO sign-off [item 4]

Owner decision. Record the options and leave it `[!]`.

### T14 [S] A cloud session can install the toolchain [item 4]

- **Files:** `scripts/cloud-toolchain.sh`; one line in `Issues/ROUND-PROTOCOL.md` §8, which is FLAGGED because it
  is the agent's own instructions and the owner asked for the finding to be resolved.
- **The script:**
  1. Reads the SDK version from `global.json` and the ilverify version from `ci.yml`.
  2. Installs the SDK with `dotnet-install.sh`. Where that host is blocked, it installs from the official
     `mcr.microsoft.com/dotnet/sdk:<version>-noble-amd64` image instead, verifying every layer digest against
     the manifest.
  3. Installs ilverify from nuget.org.
  4. Installs PowerShell 7 from packages.microsoft.com when `pwsh` is missing.
  5. Unshallows the clone.
  6. Prints the environment exports.

  It never substitutes another version, and it exits non-zero when the pinned SDK cannot be had.
- **Proof:** run it into a fresh `HOME`, then confirm `dotnet --version` equals the pin and `ilverify` and `pwsh`
  run.

### T15 [d] The `full-ci` label [item 2]

Decided: protocol §1.1 phase 8 makes the label part of closing a round. There is nothing to build.

### T16 [d] Shallow cloud clones [item 4]

Decided: protocol G7 and §8 unshallow first, and T14's script does it.

### T99 [S] Final gate

Protocol G8, then the round report. The checklist names the open owner actions and next-round candidates.
