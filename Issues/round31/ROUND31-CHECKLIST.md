# Round 31 — checklist and completion audit

Source: `ROUND31-TASKS.md` (the full steps live there) and `POST-ROUND30-IMPROVEMENT-RESEARCH.md` (the research
ids in brackets). Audited 2026-09-27 on `feat/round31` @ `82f3203` against the commits, `TASK-LOG.md` and
`round31-audit.sh static` (run with `python3` shimmed to `python` and `PYTHONUTF8=1`). Round-31 tests were **not**
re-run for this audit.

Legend: `[x]` done · `[~]` partly done / done with a recorded deviation · `[ ]` not started · **BLOCKED** needs a
decision or access first.

## Tier 1 — hardening

- [x] **T00** Baseline — baseline recorded at `b688bb9` (merged round 30, PR #5).
- [x] **T01** Advisory-scoped audit suppression [A2] — `e9e09d4`. Audit DONE: 0 NU190x NoWarn.
- [x] **T02** Projection null guards cast to `object` [A1] — `0938e21`. Found the CS0034 defect as well. 4 sites, not 5.
  - [ ] EF/SQLite translatability row: waits on T17's rig.
- [x] **T03** CVE-2026-32933 PoC pinned [A3] — `ca831d8`. Guard green 6/6; SECURITY.md row and MIGRATION.md paragraph present.
- [x] **T04** Strengthen two `LocationInfo` tests [D1] — `031c39a`, with the sabotage check.
- [x] **T05** Duplicated coverage sources [D2] — `031c39a`. 1 deleted, 2 marked `shared-fixture`. Audit: 0 unmarked.
- [x] **T06** Byte-identity lock confirmed [B1] — 1,014 golden cases (≥ 1,000).
- [~] **T07** Parameter-ceiling ratchet [B1] — `031c39a`. **Deviation:** the table comes from the Roslyn walk (54 rows), not the task's regex (40). The regex would leave the ratchet red on an untouched tree.

## Tier 2 — structural and performance

- [ ] **T08** ExtractionContext [B1] — **BLOCKED:** conflicts with the three-bundle ruling in `ae9c7ea`, so the owner has to decide. Audit: 40 methods with more than 6 parameters.
- [~] **T09** Registry collection pre-size [P1] — `0af2f2e` + `4a6821f`. **Deviation:** one runtime helper `DwarfCollectionMap` instead of inline emission, because inlining grew the golden corpus by 70 %. The `CollectionsMarshal.AsSpan` fast path was removed again because it read stale data when a hook mutated the list. *The audit's TODO here is stale: it greps for the inline shape that was rejected.*
- [ ] **T10** Hoist projection trees [P5a] — **STOP fired:** the lambda is written across many `sb` sites. Handed to Opus.
- [ ] **T11** Batch registration `RegisterMany` [P6] — not started.
- [x] **T12** Exact-pair slot for the facade [P2a, P4 update] — `ed69922` + `c65c4fe`. **Deviation (owner ruling):** only a FOUND delegate is cached and the version machinery is deleted. `ExactUpdateSlot` is present. *The audit's TODO is stale: it looks for `_version`.*
- [ ] **T13** In-memory projection route + parity [P5 Case 2] — depends on T10.
- [ ] **T14** Static per-destination dispatch at the validation root [P2b] — needs an Opus spec first. Must now also satisfy the "runtime does not re-decide" ruling and T31's tests.
- [ ] **T15** Case 2 nested selects → general mapping [P5] — depends on T13.
- [ ] **T16** Expose `{Method}Expression` [P5d] — needs Opus to pick the name and allocate a DWARF id. Depends on T10.
- [ ] **T17** EF precompiled-query experiment [P5c] — O/H.
- [ ] **T18** NativeAOT size experiment [P7] — O/H.
- [~] **T19** Perf lane and benchmarks [P2/P4/P5/P6/P8] — `d177479`, `924f237`; results in `benchmarks/results/2026-09-26-round31-full-matrix.md`.
  - [x] Facade `Map<TS,TD>` A/B (`AmbientFacadeBenchmarks`) and T09 A/B (`RegistryCollectionBenchmarks`, `CollectionReadProbe`)
  - [ ] `ci.yml` default filter `Category!=Perf` plus a nightly Perf job. **BLOCKED:** the token cannot push `.github/workflows`.
  - [ ] Facade collection source at 10/100/500 pairs; `Project()` inline vs hoisted; routed vs tree; startup `__Register()` at 100/500/1,000; P8 A/B pairs
  - [ ] P3: the A1 benchmark row (user-operator guard cost in in-memory projections)

## Tier 3 — CI, release, platform

- [ ] **T20** zizmor + Harden-Runner [A5] — **BLOCKED** on the workflow-scope token. Audit: 15 jobs without harden-runner as step 1.
- [ ] **T21** NuGet Trusted Publishing [A4] — the human part on nuget.org and GitHub comes first.
- [ ] **T22** .NET 11 SDK leg [C1] — workflow change, so same wall as T20.
- [ ] **T23** Nullability-attribute probe [A6] — not started. Cheap, and it decides whether A6 is real.
- [ ] **T24** C# 15 unions refused loudly [C3] — **deadline 2026-11-10**. Needs the Opus metadata check first.
- [ ] **T25** C# 15 closed hierarchies [C2] — O; needs the Roslyn packaging decision.
- [~] **T26** Interceptors [C4] — a proposal only (`PROPOSAL-T26-compile-time-binding.md`). It finds interceptors unsound on an interface receiver, and `InterceptsLocation` on net10 is unverified. The task gates any prototype on T14 + T19.
- [ ] **T27** CS8795 stubs vs suppression [B2] — O decision.

## Tier 4 — test-suite tidy-up and docs

- [ ] **T28** Normalize `__` locals in assertions [D3, optional] — audit: 60 raw-local assertions left.
- [ ] **T29** `// Covers:` provenance headers [D4] — audit: 118 files without a header.
- [ ] **T30** "Coming from AutoMapper" guide [C5] — not started. T03 did add a short MIGRATION.md paragraph.

## Added this round (not in the task list)

- [x] **T31** Architecture tests: "the runtime does not re-decide what the compiler decided" — `0729198` (`RuntimeSurfaceArchitectureTests`, 6 tests, sabotage-checked).

## Final gate

- [ ] **T99** — not reached. Open items:
  - [ ] `round31-audit.sh static` all DONE. T09/T12 need the audit script updated to the accepted shapes.
  - [ ] Full build + default-filter test run; `round31-audit.sh tests`
  - [ ] Golden green without `DWARF_GOLDEN_UPDATE`
  - [ ] CHANGELOG `[Unreleased]` lines. None added for round 31 yet.

## Audit-tool defects found while auditing

- `round31-audit.sh` calls `python3`, which does not exist on this Windows machine. Its heredoc scripts also fail
  under cp1250 unless `PYTHONUTF8=1` is set. Without both, T05 and T08 report a false TODO.
- The T09 and T12 checks encode the pre-deviation designs, so they report TODO for work that landed.
