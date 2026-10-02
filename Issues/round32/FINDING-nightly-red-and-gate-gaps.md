<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Finding: master's nightly is red in four jobs; a `full-ci` round PR would have shown two of the four causes

Filed on 2026-10-02, while distilling `Issues/ROUND-PROTOCOL.md`, as recorded input for round 32's plan.
Corrected the same day after an independent review (see the end). Nothing here is fixed. Each item names its
evidence and the fix that evidence points to, and leaves the decision to the round that takes the item up. Labels
follow the protocol's §7. The tree measured is `master` at `24a31de` (the round-31 merge).

## 1. Four nightly jobs are red [EVIDENCE: CI run logs]

Scheduled runs 206, 208 and 210–213 (2026-09-27 to 2026-10-02) all concluded `failure`. The push and PR runs in the
same period (207, 209) are green. Runs 206 and 208 ran on round 30's merge (`b688bb9`), so master was already red
before round 31 merged. Only the jobs of run 213 (`36991553091`) were read, so what failed in the earlier runs is
not known.

| Job (run 213) | Failing step | Cause (from the job log) | Fix the evidence points to |
|---|---|---|---|
| `mutation (runtime)` | Assert the config is runnable (break <= low) | See note A. | Remove the BOM, and add a check that refuses one (note A). |
| `mutation (generator)` | Publish the verified report to the Stryker dashboard | See note C. | **Owner decision:** shrink the report, split the upload, or stop failing the job on publication. |
| `mutation (pipeline)` | the same publish step | Log not read. [INFERRED: the same 413] | as above |
| `package-size` | Check the package-size ceilings | See note B. | See note B. |

**Note A — the runtime leg never runs.**

- `stryker-config.runtime.json` has started with a UTF-8 BOM since `ed69922` (round 31 T12).
- The step's `json.load(open(path))` refuses the BOM with `JSONDecodeError: Unexpected UTF-8 BOM`, so the leg
  never runs.
- The next step then fails too ("no product assembly found under src/**/bin"), because nothing was built.
- **Why nothing local caught it:** housekeeping's `ConvertFrom-Json` and `RatchetInvariantScanTests`' .NET parser
  both accept a BOM.
- [MEASURED: reproduced in this session with the step's own Python code]
- **Fix:** remove the BOM, and add a check over `stryker-config*.json` that refuses one.

**Note B — the package outgrew its ceilings.**

- `DwarfMapper` is 353 KB (361,965 B) against a 322 KB ceiling.
- `DwarfMapper.Testing` is 53 KB (54,616 B) against a 52 KB ceiling.
- The ceiling was last re-measured in `1972246a` (2026-09-09), and rounds 29 to 31 have changed the shipped sources
  since. [INFERRED: round 31's additions are one candidate — `Dwarf.Map`, the interceptors and
  `build/DwarfMapper.props` — but which change grew the package was not measured.]
- **Fix, one of:**
  - re-measure and raise the ceiling, with an entry-by-entry account, in the manner of `7e112c7`. This is the
    protocol's sanctioned raise (I-3);
  - find the growth that was not intended.

**Note C — the generator leg's score is suspect.**

- The upload returned `HTTP 413 {"message":"request entity too large"}`.
- The leg reported a final score of 98.80 %. That is above the generator leg's proven ceiling in
  `Issues/ledgers/equivalent-mutants.md`, and a score above the ceiling is, in `scripts/gate-checks.ps1`'s own
  words, "arithmetically impossible". It means a mutant the ledger proved unkillable was reported killed.
- The 2026-09-23 phantom kills recorded in that ledger are the precedent.
- So the score is not established. The failed publish is the only established fact.
- **Why CI let it through:** CI's mutation job runs the break threshold and the non-vacuity, tree and binary
  checks, but not the band check (`Assert-LegScoreWithinBand`) where the proven-ceiling rule lives. Only
  `scripts/housekeeping.ps1` runs that check, so in CI a score above the ceiling passes the leg. [EVIDENCE: read]

## 2. No round PR carried the `full-ci` label [EVIDENCE: GitHub API]

`CONTRIBUTING.md` says a round-closing PR is labelled `full-ci`, so that the expensive tier runs on the merge
result before the merge. None of PRs #1–#6 carries a label.

The label would have shown two of item 1's four causes:

- **The BOM:** the runtime leg's config check has no `if:` of its own, so it runs in a `full-ci` mutation job.
- **The size overrun:** the `package-size` job runs under `full-ci`.

It would not have shown the 413s. The publish step runs only on `schedule` and `workflow_dispatch`
(`.github/workflows/ci.yml`), so no PR runs it.

The protocol makes the label part of closing a round (§1.1, phase 8).

## 3. A release tag would fail its own test step [EVIDENCE: read, not run]

- `.github/workflows/release.yml` runs `dotnet test … --filter "Category!=Perf"` but never installs
  `dotnet-ilverify`.
- `EmittedIlIsVerifiableTests` has been in that lane since `0f1f5d2` (2026-09-09). It calls `Assert.Fail` when
  `ilverify` is not on PATH.
- No tag exists in the repository, so this has not fired yet.
- `CiToolPrerequisiteScanTests` reads only `ci.yml`, which is why nothing caught it.
- **Fix:** install the tool in the release job, and extend the scan to every workflow that runs tests.

## 4. Gaps between written rules and practice [MEASURED unless marked]

- **DCO sign-off.** `CONTRIBUTING.md` asks for `git commit -s`. Only 23 of the 1,563 commits on `master` carry
  `Signed-off-by`, all of them dated 2026-07-26. **Owner decision:** enforce the sign-off, or drop the sentence.
- **Mutation config checks.** `RatchetInvariantScanTests` R1 checks three of the six `stryker-config*.json` files
  (generator, doctooling, runtime). It skips codefixes, pipeline and testing. [EVIDENCE: read]
- **Probe files.** The `ZZ*.cs` probe rule (round 31 G7, now protocol G5) lives only in `round31-audit.sh`. That
  script reports a TODO but never exits non-zero for one; it exits 2 only on a usage or directory error. No test,
  script or CI step enforces the rule.
- **Stale README.** `tests/DwarfMapper.NegativeCases/README.md` still describes adding an id to
  `PredatesThisProject` as the way to opt out. `DiagnosticCoverageRatchetTests` pins that list exactly and refuses
  any addition. [EVIDENCE: read]
- **Unexpanded command.** `Issues/ledgers/README.md` says "Copied at commit $(git rev-parse --short HEAD)"; the
  command was never expanded.
- **Agent worktrees.** `.claude/worktrees/` is not git-ignored, so an agent worktree shows as untracked and can trip
  a custody check. Protocol G1 now exempts it; ignoring it in `.gitignore` would be the gate. [EVIDENCE: read]
- **Shallow cloud clones.** A cloud session's clone is shallow by default: this session saw 366 of 1,563 commits
  until `git fetch --unshallow`. History searches (protocol G7) silently miss older decisions there, which is why
  the protocol makes unshallowing the first step.
- **Blocked SDK download.** This session's network policy blocks `builds.dotnet.microsoft.com`, so the pinned SDK
  cannot be installed and neither goal in the protocol can run here. The fix is an environment setting, so it is an
  owner action: allow that host for the environment that runs the goals.

## Corrections, 2026-10-02 (independent review of the first version, `8c9c3dd`)

1. **The `full-ci` story.** The first version said the label would have caught all four red jobs and that master
   "went red the night after" round 31 merged. Both claims were wrong.
   - The publish step never runs on a PR (item 2).
   - Runs 206 and 208 were already red on round 30's merge (item 1).
   - The title is corrected to match.
2. **The generator leg.** The first version called it "passed (98.80 %)". That score is above the leg's proven
   ceiling, so it is not evidence of a pass (note C).
3. **Package growth.** The first version attributed the growth to round 31. The ceiling last moved on 2026-09-09,
   so that attribution is inference (note B).
4. **Labels.** CI logs and API answers are now labelled EVIDENCE rather than MEASURED (protocol §7). The audit
   script's exit behaviour is stated precisely (item 4).
