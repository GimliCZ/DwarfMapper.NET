<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 32 — task log

There is one entry per executed task. Each entry records what was done and the evidence for it. It also records
every place where the task list's instructions were **not** followed literally, with the reason. Escalations go
under their own heading at the end (protocol §6).

## T00 — baseline · `5561ada5`, 2026-10-02

- **Session.** An attended cloud session on `feat/round32`, run start `5561ada5`.
- **Toolchain.** `builds.dotnet.microsoft.com` is blocked by this environment's network policy, and so are
  `dotnetcli.azureedge.net` and `ci.dot.net`. The pinned SDK was installed from another official Microsoft
  channel, which the policy allows:
  1. Fetched the `mcr.microsoft.com/dotnet/sdk:10.0.101-noble-amd64` image manifest.
  2. Downloaded the three layers that carry `/usr/share/dotnet`, each verified against its manifest SHA-256.
  3. Copied `/usr/share/dotnet` into `$HOME/dotnet`.

  The result is Microsoft's own 10.0.101 build, not a rebuild. Ubuntu's archive offered only 10.0.105 and
  10.0.112, which the pin forbids. `dotnet --version` prints `10.0.101` in the repository.

  The other tools:
  - `dotnet-ilverify` 10.0.11 from nuget.org, the version CI pins;
  - PowerShell 7.5.11 from packages.microsoft.com;
  - python3, and ICU 74 already present.

  `DOTNET_CLI_UI_LANGUAGE=en`; the clone was unshallowed earlier in the session.
- **Restore and build.** `dotnet restore DwarfMapper.NET.sln --locked-mode` exit 0, including the NuGet audit.
  `dotnet build DwarfMapper.NET.sln -c Release --no-restore` reports **0 Warning(s), 0 Error(s)**.
- **Default lane.** Run as CI's `build-test` job runs it:
  `--filter "Category!=SurfaceMatrix&Category!=Perf" --blame-hang --blame-hang-timeout 5m`. All nine assemblies
  passed with 0 failed:

  | Assembly | Passed |
  |---|---|
  | CorpusTests | 20 |
  | Testing.Tests | 139 |
  | ConsumerTests.Host | 23 |
  | DifferentialTests | 70 |
  | CleanCorpus | 53 |
  | NegativeCases | 192 |
  | IntegrationTests | 987 |
  | CompilerTests | 52 |
  | Generator.Tests | 7,831 |
- **Surface matrix.** Run as CI's `surface-matrix` job runs it: 901 passed, 0 failed.
- **Golden.** `GoldenCorpusTests` 2/2 passed with `DWARF_GOLDEN_UPDATE` unset; the manifest has 1,014 cases.
- **Audit `baseline`:**

  | Figure | Value |
  |---|---|
  | `head` | 5561ada5 |
  | `sdk` | 10.0.101 |
  | `bom_configs` | 1 |
  | `pragma_src` | 7 |
  | `probes` | 0 |
  | `r1_rows` | 3 |
- **Audit `static`:** every task reports TODO (16 TODO, exit 1), which proves the audit can see work.

## T01 — the runtime config's BOM, test first · `3543f6ae`

Done as specified.

- **RED, three ways:**
  - `StrykerConfigEncodingTests` failed naming exactly `stryker-config.runtime.json: starts with a UTF-8
    byte-order mark`.
  - CI's own config-check Python, lifted unchanged from `ci.yml`, raised `JSONDecodeError: Unexpected UTF-8 BOM`.
  - So did the publish step's `project-info` read.
- **Fix:** the first three bytes removed and nothing else. `cmp` shows old-minus-three-bytes against new:
  16,096 → 16,093 bytes.
- **GREEN, all three:**
  - the test passes;
  - the config check prints `break 97 <= low 97 - runnable`;
  - the publish read prints `module = runtime`.
- **Gate:** Generator.Tests 7,832, CompilerTests 52 and IntegrationTests 987, all green.
- **Owed:** a nightly or dispatch. The runtime leg will run in CI for the first time since `ed69922`, and its
  round-31 red (90.45 % against `break` 97, with a re-measure owed) may now show honestly. That is a known
  owner action, not a regression.

## T08 — R1 covers all six mutation configs · `17874059`

Done as specified, and the prediction made while planning held: two configs went RED, one passed.

- **RED:** with the three `InlineData` rows added and nothing else changed:
  - `codefixes`: "carries no 'MEASURED YYYY-MM-DD' / 'RE-MEASURED YYYY-MM-DD' provenance";
  - `testing`: "quotes no 'score NN.NN %' measurement at all";
  - `pipeline`: passed.
- **The measurements existed in both comments;** only the wording missed R1's patterns. These are one-word
  fixes, and each `stryker-config` section was verified identical before and after:
  - "RE-VERIFIED 2026-09-26" became "RE-MEASURED 2026-09-26";
  - "detected = 100.00 %" became "detected, score 100.00 %".

  Both figures agree with the ledger's rows: codefixes 87.64 % (2026-09-26), testing 100.00 % (2026-09-21).
- **GREEN:** `RatchetInvariantScanTests` 13/13. CI's config check still prints "runnable" for both files.
- **Gate:** Generator.Tests 7,835, CompilerTests 52 and IntegrationTests 987, all green.

`Ruling: change the comments' wording rather than widen R1's regex to accept "RE-VERIFIED" or "= NN.NN %" —
costs if wrong: one revert; widening the instrument was the option that loosens a gate (I-3).`

## T09 — the probe rule is a test · `3deb34b9`

Done as specified. `ProbeFileScanTests` is a guard (green on the tree), so it was shown failing once:

| State | Result |
|---|---|
| clean tree | 1/1 passed |
| `tests/DwarfMapper.Generator.Tests/Round32/ZZPlantedProbe.cs` planted | failed, naming the file |
| plant removed | 1/1 passed |

The plant was never staged. **Gate:** Generator.Tests 7,836, CompilerTests 52 and IntegrationTests 987, all
green.

## T10 — agent worktrees are ignored · `b6d99dce`

Done as specified.

- **RED:** `git check-ignore -q .claude/worktrees/agent/x` → exit 1.
- **GREEN:** exit 0.
- **Control:** `.claude/skills/round/SKILL.md` and `.claude/hooks/*.ps1` are still tracked (exit 1).

## T11 — the NegativeCases README states the rule the ratchet enforces · `48f46a15`

Done as specified. The paragraph quotes the test ("a counted, bounded population"). It names the guard
(`The_exemption_list_is_an_exactly_pinned_bounded_population`) and the constants (`PredatesThisProjectPin`,
`ExemptionIdHorizon`) rather than their values, so it cannot drift from them the way the old one did.
NegativeCases builds with 0 warnings and passes 192/192.

## T12 — the ledgers README names its capture commit · `d8f24aa7`

**Deviation.** The task list said to state that "later ledgers name their own capture point in their headers".
Checked before writing: rounds 21, 22, 23 and 25 do not. The README instead gives `656042c2` (parent `069987f7`)
for the rounds 19 and 20 copies, and the `git log --diff-filter=A` command for every later file. It says only
"some" later headers state their capture.

## T15 — the `full-ci` label · decided, nothing to build

Protocol §1.1 phase 8 makes the label part of closing a round, and the round report lists "open the round PR
with the full-ci label" as an owner action. Opening and labelling a PR is the owner's (protocol §4).

The finding's corrected item 2 also bounds what the label can prove. It reaches the BOM (T01) and the
package-size job (T07). It cannot reach the dashboard publish step, which runs only on `schedule` and
`workflow_dispatch`.

## T16 — shallow cloud clones · decided, nothing to build beyond T14

Protocol G7 and §8 make `git fetch --unshallow` the first step of any history search. T14's script does it on
setup.

## T02 — research: CI's generator leg above its proven ceiling · `6b29547a`

Output: `FINDING-T02-ci-phantom-kills.md`. Steps 1–4 are done, and the STOP condition did not fire: the failing
tests were identified by measurement. Three things go beyond the task's steps:

- **A reading of the pinned binary.** The mechanism turned on how Stryker 4.16.0 treats a test that failed its
  initial run, so the tool package was decompiled (`ilspycmd`, installed to the session scratchpad).
  - That reading overturned the working hypothesis in its direct form.
  - A first reading then went too far the other way, concluding "no verdict changed".
  - An independent read-only reviewer found the indirect path, which was verified line by line before the
    commit.
  - Both corrections are written into the finding.
- **A local two-arm Stryker run, not finished.** The 13.4 GB shell cgroup killed it twice (test host 9.1 GB,
  Stryker 4.1 GB). The Debug outputs were removed afterwards, and the sweep reported "none mutated".
- **A restated prediction.** The task list's T03 line, "Prediction (T02): … reports … 96.62 %", is restated in
  the finding's §6: generator ≤ 97.11 %, pipeline ≤ 94.71 %. The task list is not edited, because it was
  approved at `85215901`.

**Review**, independent and read-only, with a fresh context, of the decompiled-code claims:
- **CONFIRMED:** the score/tally arithmetic and the coverage-analysis claims.
- **PARTLY:** the session runner (the full list runs once per test assembly) and the update handler (a timed-out
  session is re-judged against unstripped failures).
- **REFUTED:** "tested in isolation". `MustBeTestedInIsolation` is never read.
- **MISSED:** the indirect path.

All four were applied before the commit.

`Ruling: mark T02 [~] rather than [!] — the research the task asks for is complete, and what remains is opening
run 213's artifact, which this session's network policy refuses (403) — costs if wrong: one checklist mark.`

## T07 — the package-size overrun, measured · `0a5f9158`

The measurement is done as specified. The ceiling move is escalated (D3, below).

- **Deviation.** The packs ran in a scratch **clone**, not a `git worktree`, so nothing was written under the
  repository's `.git`.
- **Added: calibration against run 213.** It surfaced the nuspec's `branch` attribute, worth 19 B. Once the clone
  carried it, the packs matched CI's to 2 B and 3 B.
- **STOP did not fire.** `scripts/repro-pack-check.py` passed every pair, byte-for-byte.

## T06 — the dashboard upload, researched · `fce08cb0`

The research is done as specified. The fix waits for D1 (below). The four items, in `RESEARCH-T06-dashboard-413.md`:

1. What the job uploads (§1).
2. Run 213's sizes (§2). They came from the artifact-list API as well as the job logs.
3. What the pinned binaries say about score-only uploads (§4). Nothing: the client publishes full reports or
   real-time batches only.
4. Each option against I-1 (§4).

Two more points:

- **Added: what the decompiled reporter writes** (§3.1). It overturned the draft's inference about ignored files,
  and it showed that option B as first drafted would not shrink the body. The recommendation became: measure,
  then B′, then A.
- **Not done: a measurement on a local report.** The T02 runs never wrote one.

## T14 — a cloud session can install the toolchain · `e1cd6f5f`

Done as specified. The commit is FLAGGED, because it adds one line to protocol §8, the agent's own instructions.

- **Proof,** on the final script:
  - a fresh `HOME` took the MCR route and installed SDK 10.0.101, which is the pin, plus ilverify 10.0.11;
  - the PowerShell branch reinstalled `pwsh`;
  - a shallow clone went from 1 commit to 1,579;
  - an unobtainable pin exited 1 with empty stdout.
- **The guard was shown failing once:** a layer corrupted after download was refused.
- **Added beyond the task: ShellCheck,** clean with no directives. The plant above exposed a stdout leak on
  failure, which was fixed.
- **A side effect, reverted.** The PowerShell branch installs the feed's current release, 7.6.6. This session was
  put back on 7.5.11, so the final gate runs on T00's toolchain.

## T03 — every CI job that runs the suite installs ilverify · `aa343936`

Done as specified.

- **RED:** the scan named exactly `[ci.yml:mutation, release.yml:release]`.
- **GREEN:** 3/3. zizmor 1.30.1 gives the same result before and after: no findings, 40 suppressed.
- **Gate:** Generator.Tests 7,836, CompilerTests 52 and IntegrationTests 987, all green.
- **The workflow push was accepted.**
- **Deviation.** The pin test and the exemption test also read every workflow now. Without that, release.yml's new
  install would escape the "same version everywhere" check.

## Escalations

### T04 — needs decision D2, before step 1

- Command: `grep -n "Assert-LegScoreWithinBand" .github/workflows/*.yml scripts/housekeeping.ps1`
- Output: no line in any workflow. `scripts/housekeeping.ps1` calls it after every leg it runs. CI's `mutation`
  job dot-sources `scripts/gate-checks.ps1` only for `Remove-PlantedMutants` and
  `Assert-NoMutatedProductBinaries`.
- Needs: owner ruling (D2). The task runs only "if D2 is A", and the decisions table leaves D2 to the owner.
- Options:
  - (A) run `Assert-LegScoreWithinBand` after every leg, with a meta-test that every matrix leg runs it;
  - (B) keep the band check housekeeping-only.

  Recommended: A. Run 213 went above two ceilings unnoticed (T02). The static-mutant hazard outlives T03 at its
  lower rate. Costs if wrong: one revert of a workflow step.
- Blocks: nothing else in this round.

### T05 — needs decision D5, before step 1

- Command: none run. Both of the task's steps run only "if D5 is A".
- Output: the pinned Stryker knows the key (task list §1.1, MEASURED). T02 found what its default costs: an
  initially failing test switches every static mutant onto an explicit test list, run once per test assembly,
  which bends the score upward without failing anything.
- Needs: owner ruling (D5).
- Options:
  - (A) `"break-on-initial-test-failure": true` in all six configs, with a test that pins it;
  - (B) keep the warning-only default.

  Recommended: A, now that T03 has removed the two standing failures. Costs if wrong: one revert of six one-line
  config changes.
- Blocks: nothing else in this round.

### T06 — the fix needs decision D1, after the research

- Command: the research, `RESEARCH-T06-dashboard-413.md`.
- Output: the generator and pipeline uploads fail with HTTP 413. The bulk of the body is per-mutant test-id lists
  [INFERRED, by elimination from the decompiled reporter].
- Needs:
  - owner ruling (D1);
  - owner action: download one artifact, and later run a dispatch to prove the fix.
- Options:
  - (A) score only;
  - (B′) the report without `coveredBy` and test sources;
  - (B) filter to scoreable mutants, which would not shrink the body;
  - (C) `continue-on-error`, which silences a red.

  Recommended: measure one artifact, then B′, falling back to A. Costs if wrong: one more dispatch.
- Blocks: nothing else.

### T07 — the ceiling move needs decision D3 and a Windows pack

- Command: CI's pack recipe at three commits, `FINDING-T07-package-growth.md`.
- Output: `DwarfMapper` is 361,965 B (353 KB) against 322; `DwarfMapper.Testing` is 54,616 B (53 KB) against 52.
  Every entry is attributed to intended work.
- Needs:
  - owner ruling (D3). Loosening a gate is the owner's.
  - owner action: a Windows pack of the same tree. The repository's rule takes the larger measurement, and the
    Windows figure decides `DwarfMapper` between 353 and 354.
- Options:
  - (A) raise both ceilings to the larger measurement in one commit, with the entry-by-entry account, in the
    manner of `1972246a`;
  - (B) remove unintended growth first. None was found.

  Recommended: A. Costs if wrong: one revert of two numbers.
- Related: `README.md` inside the packages turns a code-size gate red on prose. `DwarfMapper.Testing` crossed its
  ceiling in round 31 on `README.md` alone (round 30 `CI-NIGHTLY-REVIEW.md`).
- Blocks: nothing.

### T13 — an [H] task: DCO sign-off is the owner's (D4)

- Command: `git log origin/master --grep='Signed-off-by' --format='%ad' --date=short | sort | uniq -c`
- Output: `23 2026-07-26`, out of 1,563 commits on `master`. `CONTRIBUTING.md` line 7 reads "Sign your commits
  with `git commit -s`."
- Needs: owner action.
- Options:
  - (A) enforce the sign-off with a commit-msg hook or a CI check, and add `-s` to the protocol's commit step;
  - (B) drop the sentence from `CONTRIBUTING.md`.

  Recommended: none; this is governance. Practice has been B since 2026-07-26. Costs if wrong: A blocks every
  unsigned commit, agents' included; B removes a stated contribution requirement.
- Blocks: nothing.
