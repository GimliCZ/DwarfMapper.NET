<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 32 — checklist

Source: `ROUND32-TASKS.md`, derived from `FINDING-nightly-red-and-gate-gaps.md`. Base commit: `5561ada5` on
`feat/round32`. Marks: `[ ]` open · `[x]` landed · `[~]` landed with an open owner step · `[d]` decided, not built ·
`[!]` escalated, or blocked by an escalated task (protocol §6).

Status: proposed — awaiting owner approval

## Tier 1 — the red nightly

- [ ] **T00** Baseline
- [ ] **T01** The runtime config's BOM, test first
- [ ] **T02** Research: CI's generator leg above its proven ceiling
- [ ] **T03** Every CI job that runs the suite installs ilverify (workflow)
- [ ] **T04** The band check runs in CI (D2; workflow)
- [ ] **T05** A failing initial test run breaks a leg (D5)
- [ ] **T06** The dashboard upload (research now; fix needs D1)
- [ ] **T07** The package-size overrun (measurement now; ceiling move needs D3)

## Tier 2 — gate gaps

- [ ] **T08** R1 covers all six mutation configs
- [ ] **T09** The probe rule is a test
- [ ] **T10** Agent worktrees are ignored

## Tier 3 — documentation and rules against practice

- [ ] **T11** NegativeCases README states the rule the ratchet enforces
- [ ] **T12** Ledgers README names its capture commit
- [ ] **T13** DCO sign-off (D4)
- [ ] **T14** A cloud session can install the toolchain
- [ ] **T15** The `full-ci` label
- [ ] **T16** Shallow cloud clones

## Final gate

- [ ] **T99** Protocol G8

## Owner actions still open

*(Filled in as the round runs.)*

## Next-round candidates

*(Filled in as the round runs.)*
