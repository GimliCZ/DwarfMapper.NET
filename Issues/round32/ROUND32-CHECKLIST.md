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
- [ ] **T02** Research: CI's generator leg above its proven ceiling
- [ ] **T03** Every CI job that runs the suite installs ilverify (workflow)
- [ ] **T04** The band check runs in CI (D2; workflow)
- [ ] **T05** A failing initial test run breaks a leg (D5)
- [ ] **T06** The dashboard upload (research now; fix needs D1)
- [ ] **T07** The package-size overrun (measurement now; ceiling move needs D3)

## Tier 2 — gate gaps

- [x] **T08** R1 covers all six mutation configs — `17874059`. codefixes and testing went RED as predicted; two one-word comment fixes; 13/13.
- [x] **T09** The probe rule is a test — `3deb34b9`. Shown failing once with a planted probe.
- [x] **T10** Agent worktrees are ignored — `b6d99dce`.

## Tier 3 — documentation and rules against practice

- [x] **T11** NegativeCases README states the rule the ratchet enforces — `48f46a15`.
- [x] **T12** Ledgers README names its capture commit — `d8f24aa7`. One deviation; see the task log.
- [ ] **T13** DCO sign-off (D4)
- [ ] **T14** A cloud session can install the toolchain
- [d] **T15** The `full-ci` label — decided: protocol phase 8 makes it part of closing a round; opening and labelling the PR is the owner's.
- [d] **T16** Shallow cloud clones — decided: protocol G7/§8 unshallow first; T14's script does it on setup.

## Final gate

- [ ] **T99** Protocol G8

## Owner actions still open

*(Filled in as the round runs.)*

## Next-round candidates

*(Filled in as the round runs.)*
