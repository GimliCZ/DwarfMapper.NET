<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 27 — the task list, executed in order

## What this file is

The executable form of `PLAN.md` (scope, grounding, decisions) and `DESIGN-surface-and-security.md` (the
Phase 0 architecture). **Neither is re-derived here.** This file is the running state: one row per step, with
its verification and its current status, updated in the commit that moves it.

**PHASE 0 IS COMPLETE** (2026-08-26). Phase 1 may begin. The original rule was that — maintainer direction, recorded in `PLAN.md` §5. Surface
pinned, Gallery gate green, governance written down, and only then does code move.

Status values: `TODO` · `WIP` · `DONE` · `BLOCKED` · `DROPPED` (with a reason).

### Standing rules for every task

1. **Whole solution** builds — `samples/` included — 0 errors / 0 warnings.
2. Full suite green (8,208 at the time of writing).
3. **Byte-identity:** the 973-case golden manifest is unchanged, *except* where a task declares an
   emitted-bytes change; those land as a reviewed re-bless, never under the lock.
4. No ceiling, floor or allowlist is written except at a value **measured in the same commit**.
5. No logic edit shares a commit with a move.
6. Every guard states its **red-when** and carries a control proving it can fail.

---

## Phase 0 — surface governance and security

| # | Task | Status | Verification |
|---|---|---|---|
| 0.1 | **SEC-1** — pin that the shipped runtime carries no memory-unsafe or reflective surface | **DONE** `92221e0` | `ShippedRuntimeSafetyTests`, 6 facts |
| 0.2 | **SEC-3** — single-source the recursion bound across the netstandard2.0 boundary | **DONE** `92221e0` | `RecursionBoundTests`, 4 facts; manifest unchanged |
| 0.3 | **SEC-2** — structural proof that registry tables are append-only | **DONE** | `RegistryAppendOnlyTests`, 4 facts |
| 0.4 | **SEC-2 doc** — write the trust model into `SECURITY.md` | **DONE** | `SecurityDocIsCurrentTests`, 4 facts — every cited guard must exist |
| 0.5 | **SEC-4** — structural: `allowNonPublic` may only widen via a sanctioned accessibility API | **DONE** | `AccessibilityBoundaryTests`, 4 facts; behaviour already covered by `ConstructorSelectorHardeningTests` |
| 0.6 | **Axis 2** — `Security` property on `DwarfSurfaceAttribute` + per-value obligations + detector | **DONE** | `SecuritySurfaceObligationTests`, 4 facts; detector derives flags from the element’s own shape |
| 0.7 | **A1** — mark infrastructure `[EditorBrowsable(Never)]` | **DONE** | `DwarfRefContext` marked; API baseline unchanged, as intended |
| 0.8 | **A2** — `DwarfRefContext` ctor: emitted call sites now pass the flags BY NAME | **DONE** | emitted-bytes change; 1 manifest line + 2 snapshots, each a one-line diff reviewed before blessing |
| 0.9 | **Audit** — member-by-member review of the 278 entries + `DwarfMapper.Testing` | **DONE** | 0 unreferenced types, 0 unreferenced members; A1/A2 were the whole debt |
| 0.10 | **R27-08** — the two object factories merged into one | **DONE** | V1's three fixes ported into V2, V1 deleted, 28 call sites repointed; suite 8,219 green |
| 0.11 | **Promote** `Unshipped` → `Shipped` — arm the stability ratchet | **DONE** | 315 entries frozen; proved with a probe member → RS0016 |
| 0.12 | **Axis 1** — `Discovery` property + Gallery-coverage gate | **DONE** | `DiscoverabilityObligationTests`, 5 facts; `Infrastructure` asserts the INVERSE |
| 0.13 | **Gallery examples** — 9 written (examples 40–48), all runnable | **DONE** | gate green; `DwarfMapperDefaults` narrowed to ConformanceOnly with a stated reason |
| 0.14 | **Strict orphan rule** + generated illustrated Gallery README quoting all regions | **DONE** | exemption removed; README quotes 45 regions across 42 examples |

---

## Phase 1 — restructuring *(begins only after Phase 0 completes)*

| # | Task | Status | Verification |
|---|---|---|---|
| 1.0 | **R27-00** — REG-02's optional-parameter ban | **DONE** `78bc275` | `ResolverParameterDisciplineTests`; 31-row shrink-only allowlist |
| 1.1 | **R27-01** — prove seam reach: every `ExtractCore` phase exercised by ≥1 golden case | **DONE** | **26 / 26 phases reached**, 1,225 covered lines; `scripts/seam-reach.ps1` re-runs it |
| 1.2 | **R27-02** — `MapperOptions`, methods only (model records excluded, C4) | **DONE** | `ResolveMembers` 36→26, `ResolveProjectionMembers` 24→16; allowlist 31→15; byte-identical |
| 1.3 | **R27-03** — decompose `ExtractCore` (3,556 lines, ~20 phases) | TODO | one phase per commit, byte-identical each time |
| 1.4 | **R27-03b** — derive seams for the other three giants, then decompose | TODO | seam proposal lands as a **comment-only commit** first |
| 1.5 | **R27-04** — `RegistryTable<TDelegate>`: four mirrored tables become one generic, twice | TODO | torture suite becomes table-generic |
| 1.6 | **R27-05** — split `DiagnosticDescriptors.cs` (1,746 lines / 96) by owned id range | TODO | ids and wordings unchanged, so sync pins stay green |
| 1.7 | **R27-07** — hygiene: delete both decided `CLAUDE.md` items, the stale round-20 plan | TODO | `CLAUDE.md`'s own header demands the deletion |
| 1.8 | **R27-06** — `ConversionPolicy` single table | **DEFERRED** | trigger (R25 conversion rows) has not fired |

---

## Open questions blocking a task

**0.10 — the two object factories.** Answered "replace V1, V2 should be better", attempted, and **reverted
on measurement**: V2 is not a superset. It is better on nulls, boundary values and graph fixtures — the axes
it was written for — but it lacks three fixes V1 carries, each with recorded provenance:

1. **abstract/interface members come back `null`**, under a comment claiming the opposite ("try to pick a
   concrete"). This is the regression V1 was fixed for, found in a real ~300-map AutoMapper migration;
2. **`[Flags]` enums never get a combined value**, so a by-name converter that throws on every combination
   looks healthy;
3. **constructor selection uses `ctors[0]`** — reflection order — where V1 orders by parameter count, so V2
   is not seed-deterministic for records.

Measured: 9 `PolymorphicMemberFuzzTests` failures plus `FlagsEnumCoverageSelfValidationTests` and scattered
`CrossConfigFuzzTests` cases. Everything reverted; the tree is unchanged.

**The intent stands and the work changed shape: it is a MERGE, not a rename.** Recommended — port V1's three
fixes into V2 (the better base, and the fixes are small and already written, rationale comments included),
then delete V1. **Still blocks freezing `DwarfMapper.Testing`'s surface**: promoting two overlapping
factories makes the duplication a declared API and turns deleting the loser into a break.

---

## Findings promoted out of this round

Recorded so they are not silently dropped when the round closes.

* **N5** — the two object factories above (→ 0.10).
* **N6** — the empty `PublicAPI.Shipped.txt` is **correct pre-release convention**, not a defect: no version
  tag exists and `CHANGELOG.md` has only `[Unreleased]`. Promotion is a decision about when to commit.
* **N7** — 15 of 29 attributes absent from the Gallery, of which **10 are Conformance-proven**. The existing
  obligation is about *proof* and is satisfied; the gap is *lookup* (→ 0.12–0.14).
* **Round 28 candidates** — file fragmentation as a testable metric (co-change coupling from git already
  measured: `MapEmitter` ↔ `MapperExtractor` at 43 commits / 86 %, `MapperExtractor.Attributes.cs` at 100 %,
  i.e. a partial that has never changed alone), and the `Try*` naming-idiom split (35 methods, 9 returning a
  nullable instead of `bool` + `out`).
