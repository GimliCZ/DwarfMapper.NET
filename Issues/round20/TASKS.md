<!-- SPDX-License-Identifier: GPL-2.0-only -->

# The task list

**Standing rule, adopted 2026-08-16: every issue found goes in here, as a task, when it is found.**
Not into a ledger line, not into a report addendum, not into a category table. Here. One store.

This file supersedes the issue-tracking role of `CARRY-FORWARD.md` (which stays as the written-up *reasoning*
behind these items — the detail is worth keeping, it just should not be where work is tracked).

**Status:** `TODO` · `WIP` · `DONE` · `DECIDE` (blocked on a maintainer ruling, not on work) ·
`DROP` (considered and rejected — kept so nobody re-raises it).

**Where the detail lives:** `CF §n` = `Issues/round20/CARRY-FORWARD.md` section n · `Dn` = an entry in
`tests/DwarfMapper.Generator.Tests/Contracts/DeclaredDivergences.cs` · `R21` =
`Issues/round21/RESEARCH.md` · ledger = `.superpowers/sdd/2026-08-16-round20-generator-defects/progress.md`
(git-ignored; rulings and per-task history live there).

---

## NOW — state of the work

Everything below this line is a backlog. This section is what is *actually happening*, so it can be read
without asking.

**Branch:** `feat/surface-coverage-architecture`, worktree `C:/Users/Jouda/RiderProjects/DwarfMapper-surface`.
Nothing pushed. Surface matrix green at 865/865; whole solution builds 0 warnings / 0 errors.

| | |
|---|---|
| **In flight** | **A2** — the missing arity checks (D4/D5/D21). Dispatched, running. |
| **Next** | A3 → A4 → A7 in that order (they share the `MapProperty`/`MapIgnore` extractor region — ordering ruled in the ledger), then A5, A6, A8, A9, then **A10 last among the refusal tasks**, then A11. |
| **Blocked on a human** | **D-e only.** ~80 diagnostics predate the CHANGELOG and have never been announced; `Scan9` guards only *new* ids. Harmless until the first tag, then not. |
| **Round 19** | Complete — 10 tasks + 5a/5b/5c, final whole-branch review returned *merge after must-fixes*, and those must-fixes were **A0**. Merge itself is **F1** below and needs your say-so. |

### Why A10 runs late — the one ordering fact worth knowing

Every DWARF **Error** suppresses emission, so a newly-refused cell reports `CS8795` and stays
`NotCompilable` rather than becoming `Refused`. Task A1 proved this empirically: its fix moved a cell from
one `NotCompilable` sub-population to another and **no ceiling moved at all.** A10 reclassifies that whole
population once, so running it before the refusal tasks means measuring the same ceilings twice.

Standing ruling from the same finding: **no task may lower a ceiling it did not re-measure in the same
commit.** Predicted movement has proven unreliable.

---

## A. Generator defects — the round-20 plan

Fixing any of these turns the build **red** until its `DeclaredDivergences` entry is deleted and the three
ceilings (findings 23 / declared cells 162 / structural 12) are lowered to their newly measured values.

| # | Status | Task | Closes |
|---|---|---|---|
| A0 | `DONE` | CHANGELOG must-fixes + `Scan9` (every diagnostic id must be announced) | CF §1.1–1.3 |
| A1 | `DONE` | Duplicate `[FlattenGraph]` destination emitted uncompilable code → `DWARF087` | N4 |
| A2 | `WIP` | The missing arity checks | D4, D5, D21 |
| A3 | `TODO` | `[MapProperty]`'s named-argument payload discarded in silence | D3 |
| A4 | `TODO` | Co-located host reads no member-level directives | D20 |
| A5 | `TODO` | `MapToGenerator` ignores assembly-level config — **D19 is a trust boundary** | D17, D18, D19 |
| A6 | `TODO` | `[MapNullSkip]` class and method forms are exact inverses — one is wrong | D6, D7 |
| A7 | `TODO` | Element-wise endpoints do not inherit method-level directives | D1, D2, D16 |
| A8 | `TODO` | Projection does not honour member directives | D9, D10 |
| A9 | `TODO` | Directives acting at exactly one endpoint (**take one directive per commit**) | D8, D11–D15 |
| A10 | `TODO` | `CS8795` read as `NotCompilable` where it means `Refused` — 96 cells judged for the first time | G4/R4 |
| A11 | `TODO` | No struct / constructor slot in the endpoint templates — 21 cells unmeasurable | G5 |

## B. Test-infrastructure holes

| # | Status | Task | Source |
|---|---|---|---|
| B1 | `TODO` | **`Scan6a` passes by construction** — searches for an enum's members in a corpus containing the enum's own declaration. `Scan2` already excludes its own defining file; copy that. Check `Scan3`, `Scan6b`, `Scan5`-options for the same shape, and the `>=40` floors sitting against actual 59 and 82. | CF §5b |
| B2 | `TODO` | **`Property`/`Field` share one `BuildAt` arm that discards `site`** — every Field cell is byte-identical to its Property cell, so a field-only divergence is invisible while reading as measured. ~11 of 162 cells. Deserves a declared **G6** entry, not a silent fix. | CF §5b.3 |
| B3 | `TODO` | **The option matrix's excuse class cannot go stale-red** — `OptionContractTests` accepts `NotApplicable` on a non-blank *reason* alone, never re-measured, never counted (8 of 18 `ProjectionCells`). The surface matrix re-classifies live; this does not. Also `DeclaredDivergences.CoversOption` is endpoint-blind. | CF §5b.4 |
| B4 | `TODO` | **No test pins that same-source, different-destination `[FlattenGraph]` stays accepted** (`("Entry","NodesA")` + `("Entry","NodesB")`). The one shape a source-keyed refusal would wrongly reject. Guards a brand-new build-breaking Error. | Task 1 review |
| B5 | `TODO` | Fixture-baseline rule is a comment, not a gate. **Trap:** a naive "baseline must compile" check fires on four legitimate `DWARF001`-by-design fixtures. | CF §3.2 |
| B6 | `TODO` | `NoSuchSite` ratchet gates the total only — offsetting per-cause drift passes silently. | CF §3.1 |
| B7 | `TODO` | `CrossAssembly` obligation is placement-blind — a row confined to one project satisfies the one category whose claim is that it is only observable *across* assemblies. | CF §3.4 |
| B8 | `TODO` | "Shrink-only" is prose on both `PredatesTheChangelog` and `DiagnosticTestAllowlist`. One guard covers both. | CF §3.8 |
| B9 | `TODO` | `Scan9`'s control proves the *corpus* is real, not that the *assertion* is. Sweep the scan family for the same shape. | CF §3.9 |
| B10 | `TODO` | `CorpusFor`'s throwing default arm is unreached by any test. | CF §3.3 |
| B11 | `TODO` | `DiagnosticCoverageRatchetTests` claims "holds by construction"; `PredatesThisProject` is a hatch no test blocks. | CF §3.6 |
| B12 | `TODO` | Nothing forbids a cell being in both `Reasons` and `StructurallyInapplicable` (double-count). Theoretical today. | CF §3.5 |
| B13 | `TODO` | `SurfaceParityTests` checks the evidence link's *shape*, not that file and anchor resolve. Latent. | CF §5b.5 |
| B14 | `TODO` | `IsGeneratorAuthored`'s remarks omit the `*.g.cs` collision case (permissive-only, undocumented). | CF §3.7 |

## C. Tooling and environment

| # | Status | Task | Source |
|---|---|---|---|
| C1 | `TODO` | **Stryker runs in no CI job at all.** The 66.95% is a manual leg, free to regress silently. | CF §5b.1 |
| C2 | `TODO` | **`.git`-as-a-file breaks the doc tests in any worktree** — and forced Task 1 to hand-render a generated file. Fix `RepoRoot` to accept both. | CF §4.1 |
| C3 | `TODO` | Both sibling Stryker configs are parse-fixed but never run; `break: 70` is inherited and unvalidated. | CF §4.2 |
| C4 | `TODO` | `docs/research/testing-conformance-REPORT.md:22` still says "Mutation testing — none". | CF §5b.2 |
| C5 | `TODO` | Drift: `ci.yml` says "854 cells" (actual 861/865); a ratchet open-codes `AssertRatchet`; `AssemblyScanTests` keeps a private repo-root walk `RepoPaths` exists to replace. | CF §5b.6 |
| C6 | `TODO` | Kill the top mutation survivors: `DwarfMapperRegistry.cs:76` (duplicate `Register` → `InterfaceMaps`, stated invariant, **zero tests**), `DwarfMappingDepthException.cs:32`, `DwarfMapExceptions.cs:95`. **Do not** attempt `:291` — equivalent in practice, see CF §5.4. | CF §5.3–5.4 |

## D. Decisions — **ruled**, now ordinary work

These were parked awaiting a maintainer. Under the execution rule adopted 2026-08-16 they were ruled on
instead, each with what it costs if the ruling is wrong. **Every one is reversible in a single commit.**
Full reasoning is in the ledger under `Ruling:`.

| # | Status | Ruling | Cost if wrong |
|---|---|---|---|
| D-a | `TODO` | **`NullCollections`@`Projection`: keep today's behaviour, keep the divergence entry, and document it** in `docs/options.md` as *projection's collection null-semantics are `AsNull` by nature*. Option (a) risks failing inside a translated query at runtime — worse than a documented divergence. Option (b) was implemented and reverted after breaking seven tests. | Nothing changes at runtime; the entry stays recorded and (a)/(b) remain open to a later maintainer. |
| D-b | `TODO` | **Delete `ResetForTests`.** Zero callers; its IVT targets a project whose registry tests do not use it; the mutation leg excludes that project — so round-19's additions to it are unverifiable dead code *by construction*. | A future test wants a reset hook and re-adds ~8 lines. |
| D-c | `TODO` | **Delete the stale items from `CLAUDE.md`'s working note** — the file's own rule is to delete each once decided, and one describes a fence-allowlist mechanism that no longer exists. ⚠️ This edits the agent's own instructions, so it is called out rather than done quietly. | Two historical notes lost — both preserved here and in `CARRY-FORWARD.md`. |
| D-d | `DONE` | **Keep `internal` + `[InternalsVisibleTo]`.** The four meta-attributes need it; public would grow the shipped API for test-only metadata, and a separate assembly breaks the single-package delivery story. The CRA objection was about what the IVT *exposes* — **D-b resolves it**, leaving only inert metadata with zero runtime reads. | If the CRA posture later demands zero IVT, the meta-attributes move to their own assembly — a contained refactor. |
| D-e | `TODO` | **Keep deferring the ~80 unannounced diagnostics** to a first-release-notes task; `PredatesTheChangelog` is the worklist. The project has never shipped, so nothing is currently mis-announced. | First release notes ship incomplete. `Scan9` guards only *new* ids, so **this one needs a human before the first tag.** |

## E. Research — measure before changing anything

| # | Status | Task |
|---|---|---|
| E1 | `TODO` | **Mutation leg costs 44 min because 91 tests do all the killing and 5,592 pay for it** (~61× waste). Scope to the intentional tests. See R21-1. |
| E2 | `TODO` | **Time the torture collection × 49** — I wrongly recorded this hypothesis as refuted on kill-count, which is not time-cost. Genuinely unmeasured. See R21-3. |
| E3 | `TODO` | Check from the existing JSON whether any mutant was killed **only** by an accidental toucher — de-risks E1 with no run at all. |
| E4 | `TODO` | Isolate the culture-swapping tests into their own collection so the rest of `IntegrationTests` can parallelise. **Bigger than it looks:** a second shared-state hazard (the registry static) the existing comment never mentions. See R21-3. |

## F. Branch and process — the work that is not a code fix

Previously invisible: it lived only in the SDD ledger and in my head. It belongs here like everything else.

| # | Status | Task |
|---|---|---|
| F1 | `TODO` | **Merge `feat/surface-coverage-architecture`.** Round 19's final whole-branch review returned *merge after must-fixes*; those were A0 and are done. **Needs your say-so — a merge is a side effect outside this worktree, so I do not do it on a ruling.** 30 commits, `+6.4k/−0.5k`, of which `src/` is only +729. |
| F2 | `TODO` | Decide the fate of the worktree after merge. It is a real git worktree at a sibling path, plus a git-ignored `.superpowers/sdd/` workspace holding the ledger, briefs and reports. The ledger is the only record of ~15 rulings; **capture it before deleting anything.** |
| F3 | `TODO` | Round-19 plan and spec live in `docs/superpowers/`; round-20's plan is on the branch. If the branch merges, three plan documents land in `docs/` — decide whether they stay as history or move under `Issues/`. |
| F4 | `DONE` | Pre-flight conflict scan for the round-20 plan (table + four ordering rulings) — recorded in the ledger. Was skipped before A0 and run late. |

---

## The honest caveat about this list

There are **~40 open items** here. That is a real risk in itself: a list where everything is a task is a list
nobody finishes, and it can feel like progress while nothing closes.

**Suggested cut line, if one is wanted:** everything in **A** (they are measured product defects with a ratchet
already asserting they exist), plus **B1–B4** (each is a mechanism currently reporting success while measuring
nothing — the failure mode that has now appeared **six** times), plus **C1–C2** (both make other work
untrustworthy). That is ~18 items. Everything else is genuinely deferrable, and **D** is not work at all until
it is ruled on.
