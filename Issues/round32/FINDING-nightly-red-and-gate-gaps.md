<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Finding: master's nightly is red in four jobs, and no round PR ran the tier that would have caught it

Filed on 2026-10-02, while distilling `Issues/ROUND-PROTOCOL.md`, as recorded input for round 32's plan. Nothing
here is fixed. Each item names its evidence and the fix that evidence points to, and leaves the decision to the
round that takes the item up. Labels follow the protocol's §7. The tree measured is `master` at `24a31de` (the
round-31 merge).

## 1. Four nightly jobs are red [MEASURED: CI]

Scheduled runs 206, 208 and 210–213 (2026-09-27 to 2026-10-02) all concluded `failure`. The push and PR runs
in the same period (207, 209) are green. Only the jobs of run 213 (`36991553091`) were read, and every red job
there belongs to the nightly / `full-ci` tier.

| Job | Failing step | Cause (from the job log) | Fix the evidence points to |
|---|---|---|---|
| `mutation (runtime)` | Assert the config is runnable (break <= low) | See note A. | Remove the BOM, and add a check that refuses one (note A). |
| `mutation (generator)` | Publish the verified report to the Stryker dashboard | The leg itself passed, with a final score of 98.80 %. The upload returned `HTTP 413 {"message":"request entity too large"}`. | **Owner decision:** shrink the report, split the upload, or stop failing the job on publication. |
| `mutation (pipeline)` | the same publish step | Log not read. [INFERRED: the same 413] | as above |
| `package-size` | Check the package-size ceilings | See note B. | See note B. |

**Note A — the runtime leg never runs.**

- `stryker-config.runtime.json` has started with a UTF-8 BOM since `ed69922` (round 31 T12).
- The step's `json.load(open(path))` refuses the BOM with `JSONDecodeError: Unexpected UTF-8 BOM`, so the leg
  never runs.
- The next step then fails too ("no product assembly found under src/**/bin"), because nothing was built.
- Reproduced in this session with the step's own Python code.
- **Why nothing local caught it:** housekeeping's `ConvertFrom-Json` and `RatchetInvariantScanTests`' .NET parser
  both accept a BOM.
- **Fix:** remove the BOM, and add a check over `stryker-config*.json` that refuses one.

**Note B — the package outgrew its ceilings.**

- `DwarfMapper` is 353 KB (361,965 B) against a 322 KB ceiling.
- `DwarfMapper.Testing` is 53 KB (54,616 B) against a 52 KB ceiling.
- Round 31 grew the package (`Dwarf.Map`, the interceptors, `build/DwarfMapper.props`) without the re-measurement
  "in this commit" that the gate's exact-ceiling protocol asks for.
- **Fix, one of:**
  - re-measure and raise the ceiling, with an entry-by-entry account, following the `7e112c7` precedent. This is
    the protocol's one sanctioned raise (I-3);
  - find the growth that was not intended.

## 2. No round PR carried the `full-ci` label [MEASURED: GitHub API]

`CONTRIBUTING.md` says a round-closing PR is labelled `full-ci`, so that the expensive tier runs on the merge
result before the merge. None of PRs #1–#6 carries a label.

Item 1 shows the cost. Every job that is red there belongs to that tier, so round 31 closed green and master
turned red the following night. The protocol now makes the label part of closing a round (§1.1, phase 8).

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
- **Probe files.** The `ZZ*.cs` probe rule (round 31 G7, now protocol G5) lives only in `round31-audit.sh`, and
  that script never exits non-zero. No test, script or CI step enforces the rule.
- **Stale README.** `tests/DwarfMapper.NegativeCases/README.md` still describes adding an id to
  `PredatesThisProject` as the way to opt out. `DiagnosticCoverageRatchetTests` pins that list exactly and refuses
  any addition. The README is stale prose of the kind the repository removes. [EVIDENCE: read]
- **Unexpanded command.** `Issues/ledgers/README.md` says "Copied at commit $(git rev-parse --short HEAD)"; the
  command was never expanded.
- **Shallow cloud clones.** A cloud session's clone is shallow by default: this session saw 366 of 1,563 commits
  until `git fetch --unshallow`. History searches (protocol G7) silently miss older decisions there, which is why
  the protocol makes unshallowing the first step.
- **Blocked SDK download.** This session's network policy blocks `builds.dotnet.microsoft.com`, so the pinned SDK
  cannot be installed and neither goal in the protocol can run here. The fix is an environment setting, so it is an
  owner action: allow that host for the environment that runs the goals.
