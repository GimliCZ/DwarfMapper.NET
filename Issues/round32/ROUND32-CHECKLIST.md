<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 32 — checklist

Source: `ROUND32-TASKS.md`, derived from `FINDING-nightly-red-and-gate-gaps.md`. Base commit: `5561ada5` on
`feat/round32`. Marks: `[ ]` open · `[x]` landed · `[~]` landed with an open owner step · `[d]` decided, not built ·
`[!]` escalated, or blocked by an escalated task (protocol §6).

Status: approved by the owner on 2026-10-02 at 85215901

The approval was given in an attended session, in the owner's words: *"Take findings for round32, create a branch
and start resolving them. As per other rounds."* The owner gave it before this list was written. The list derives
every task from the finding's items. The decisions table (D1–D5) stays the owner's, and the tasks that depend on
it wait for it.

## Tier 1 — the red nightly

- [x] **T00** Baseline — on `5561ada5`, with the pinned SDK:
  - build: 0 warnings, 0 errors;
  - default lane: 9/9 assemblies, 0 failed;
  - surface matrix: 901/901;
  - golden: green;
  - audit: 16 TODO.

  Figures are in `TASK-LOG.md`.
- [x] **T01** The runtime config's BOM, test first — `3543f6ae`. RED from the test and from CI's own Python; three bytes removed; GREEN.
- [~] **T02** Research: CI's generator leg above its proven ceiling — `6b29547a`. The failing pair never kills directly; it puts every static mutant on an explicit list that runs once per test assembly. Open: run 213's report.
- [x] **T03** Every CI job that runs the suite installs ilverify (workflow) — `aa343936`. RED named exactly `ci.yml:mutation` and `release.yml:release`; GREEN; zizmor unchanged.
- [!] **T04** The band check runs in CI (D2; workflow) — waits for D2.
- [!] **T05** A failing initial test run breaks a leg (D5) — waits for D5.
- [!] **T06** The dashboard upload (research now; fix needs D1) — research `fce08cb0`; the fix waits for D1.
- [!] **T07** The package-size overrun (measurement now; ceiling move needs D3) — measured `0a5f9158`, every entry attributed; the move waits for D3 and a Windows pack.

## Tier 2 — gate gaps

- [x] **T08** R1 covers all six mutation configs — `17874059`. codefixes and testing went RED as predicted; two one-word comment fixes; 13/13.
- [x] **T09** The probe rule is a test — `3deb34b9`. Shown failing once with a planted probe.
- [x] **T10** Agent worktrees are ignored — `b6d99dce`.

## Tier 3 — documentation and rules against practice

- [x] **T11** NegativeCases README states the rule the ratchet enforces — `48f46a15`.
- [x] **T12** Ledgers README names its capture commit — `d8f24aa7`. One deviation; see the task log.
- [!] **T13** DCO sign-off (D4) — [H], the owner's.
- [x] **T14** A cloud session can install the toolchain — `e1cd6f5f`, FLAGGED (one protocol §8 line). Fresh-HOME, PowerShell, shallow-clone and refusal runs passed; the digest guard was shown failing once.
- [d] **T15** The `full-ci` label — decided: protocol phase 8 makes it part of closing a round; opening and labelling the PR is the owner's.
- [d] **T16** Shallow cloud clones — decided: protocol G7/§8 unshallow first; T14's script does it on setup.

## Final gate

- [x] **T99** Protocol G8 — at `aa250531`, with HEAD the same before and after:
  - build: 0 warnings, 0 errors (full, non-incremental);
  - default lane: 9/9 assemblies, 0 failed; Generator.Tests 7,836;
  - surface matrix: 901/901;
  - `GoldenCorpusTests`: 2/2, without update;
  - audit: 5 TODO, which are exactly the escalated T04, T05, T06, T07 and T13, none landed;
  - CHANGELOG: no landed task is user-visible, so no line is owed;
  - tree: clean.

## Owner actions still open

1. **Decide D1–D5.** See the escalations in `TASK-LOG.md`.
   - D1: the dashboard upload. Recommended: measure, then B′, falling back to A.
   - D2: run the band check in CI. Recommended: A.
   - D3: the package ceilings. Recommended: A, after a Windows pack.
   - D4: DCO sign-off, which is governance.
   - D5: `break-on-initial-test-failure`. Recommended: A.
2. **Open run 213's `mutation-report-generator` artifact** (expires 2026-12-31), which this session's network
   policy refuses. For the 14 ledger rows, read `status`, `static` and `killedBy`, then plant every reported kill
   (T02 §6).
3. **Pack the same tree on Windows** for D3. The repository's rule takes the larger measurement.
4. **Trigger a nightly or dispatch after the merge.** It proves three things:
   - T01: the runtime leg runs again, and round 31's 90.45 % may show red;
   - T03 and T02's prediction: no failing initial test, generator ≤ 97.11 %, pipeline ≤ 94.71 %;
   - once built, the D1 fix.
5. **Confirm the task list's approval.** The approval line above records your instruction in this attended
   session, not a review of the list itself. The list is at `85215901`.
6. **Open the round PR with the `full-ci` label.**

## Next-round candidates

- **Wall-clock tests inside mutation runs** [EVIDENCE]. No `stryker-config*.json` sets a test-case filter, so
  `Category=Perf` tests run in every mutation session. That is a source of spurious failures for static mutants
  (T02 §4). The candidate is `Category!=Perf` in the configs. It changes what the legs measure, so the owner
  decides.
- **Upstream Stryker.NET** [INFERRED]. With an explicit list, Stryker runs the whole list once per test
  assembly (T02 §7). A report upstream is the owner's call.
- **`README.md` in the packages** [MEASURED]. It moves a code-size gate on prose: `DwarfMapper.Testing` went red
  in round 31 on it alone (T07 §3). This repeats the round 30 `CI-NIGHTLY-REVIEW.md` item.
- **Cloud sessions cannot run the generator leg** [MEASURED]. Locally it needs more than the 13.4 GB this
  session's shell had: test host 9.1 GB plus Stryker 4.1 GB (T02 §5). This is a sizing fact for protocol §8.
