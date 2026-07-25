<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Design — Correctness-first generator hardening & DX

**Status:** Phases A, B, C, E **landed** (2026-06-18) · D investigated & deferred with a technical blocker · **Date:** 2026-06-18

## Status (2026-06-18)

- **Phase A — DONE.** `ModelCacheSafetyTests` (structural cache-safety) + `DWARF055` build-budget Info.
- **Phase B — DONE.** XML-doc mapping plan on every public generated method (`MapEmitter.EmitMappingPlanDoc`);
  55 snapshots updated (verified additive-only).
- **Phase E — DONE.** `ReflectionFreeMetaTests` (E3), `docs/CORRECTNESS.md` (E1), COMPARISON/README framing (E2).
- **Phase C — DONE** (status corrected 2026-07-23; this entry previously read "NOT STARTED (deliberate)" long
  after the work had landed). The `DwarfMapper.CodeFixes` netstandard2.0 project exists and is in the solution,
  with both recommended slices delivered **and** one more:
  - `AddReverseMapInverseCodeFixProvider` — `DWARF052` (the recommended first slice),
  - `AddMapIgnoreCodeFixProvider` — `DWARF001` (the recommended second),
  - `ResolveExplicitOnlyMemberCodeFixProvider` — `DWARF072`.

  Tests live in `tests/DwarfMapper.Generator.Tests/CodeFixes/`. The packaging risk anticipated here did not
  materialise. Later hardening (audit ISSUE-028) moved the member-name recovery off diagnostic **message
  parsing** onto the diagnostic **property bag** (`DiagnosticInfo.MemberName` → the `"Member"` property), so a
  reworded or localised message no longer silently breaks a fix; `CodeFixes/DiagnosticPropertyContractTests.cs`
  pins that contract.
- **Phase D — INVESTIGATED, DEFERRED (with technical blocker).** Per-method extraction granularity was
  investigated against the real code. The blocker: `synthesized` (the shared synthesized-helper dictionary)
  and `nestedRegistry` are created **once per class** (`MapperExtractor.cs:186` and `:198` as of 2026-07-23;
  cited as lines 87/95 when written — the file has grown, the structure has not) and threaded through
  every method; synthesized nested mappers are **deduplicated across methods** (the dedup prevents `CS0111`
  duplicate-member errors), plus there's a cross-method synthesized post-pass, Preserve-mode re-synthesis,
  and the registry cap (`DWARF031`). So methods are **not independent units**. A per-method cache split would
  need either per-method helper duplication (→ duplicate-member compile errors) or a class-level combine
  stage for the shared helpers that re-runs on any method change — which redoes most of the heavy work,
  **capping the incremental benefit**. Given (a) the small achievable win, (b) the high risk of refactoring
  the generator's most intricate path, and (c) no evidence the cost matters (`DWARF055` does not fire on
  normal mappers; no large-mapper latency reported), Phase D stays deferred. If pursued, it should be a
  dedicated isolated effort (own worktree/branch) where intermediate build-breakage is contained, and would
  likely require first refactoring synthesized-helper ownership out of the per-class extraction.

All landed work: full suite 2970 green, 0 warnings, nothing pushed. *(As of 2026-07-23: 5,561 green, 0 warnings,
merged and pushed to `origin/master`. The 2970 figure is the count on the day this design was written.)*

## Reconciliation note (2026-07-23)

This design predates the four-part **generator maintainability programme**
(`2026-07-22-generator-testing-framework-design.md`, `2026-07-22-shared-engine-core-design.md`, then the emission
layer and the `MapperExtractor.cs` split). Two connections matter, and both were discovered *after* duplicating
effort — read this section before starting either of the remaining sub-projects:

- **Phase A ↔ sub-project 1.** `ModelCacheSafetyTests` already enforces "no `ISymbol`/`SyntaxNode`/`Compilation`/
  `Location` in a pipeline model", **structurally** — by reflecting over declared types in
  `DwarfMapper.Generator.Model`, recursing into element types, with an allow-list. Sub-project 1 later added
  `GeneratorCacheAssert.NoSymbolsInPipeline`, which checks the same rule **dynamically** over runtime step
  outputs. They are complementary, not redundant: the structural one fires the moment a bad field is *declared*
  and has no depth bound; the dynamic one reaches the `.Combine()` tuple shapes (`.Item1.Item1[…]`) that live
  outside the `Model` namespace, which is where a silent depth-truncation defect was actually found. Anyone
  touching either should know the other exists.
- **Phase D ↔ sub-project 4.** The per-method-extraction blocker documented above is the same obstacle
  sub-project 4 (splitting `MapperExtractor.cs`) will meet: `synthesized` and `nestedRegistry` are class-scoped
  with cross-method deduplication that prevents `CS0111`. Phase D's conclusion — that methods are **not**
  independent units, and that helper ownership must be refactored out of per-class extraction first — is prior
  art for that sub-project, not a separate concern.

## Goal & framing

Turn the scattered strengths DwarfMapper already has — the completeness gate, `[RoundTrip]`, proven
determinism + incremental caching, AOT cleanliness — into one coherent, defensible identity:
**"the mapper that proves your mappings are right and reproducible."** Mapperly ties on speed; none of the
peers can structurally copy a *correctness-first* story. This design implements the pieces that make that
story real and self-guarding, sequenced cheap-and-safe first.

Each phase is independently shippable, independently tested, and leaves the tree green. Phases A–B are the
high value-to-effort core; C–D are larger and gated on demand; E is docs/positioning.

---

## Phase A — Internals guards (S, low risk)

Make the incrementality/caching guarantees *un-regressable* and the IDE-perf risk *visible*.

### A1. Caching-invariant structural meta-test
- **What:** a reflection meta-test that walks every type in the `DwarfMapper.Generator.Model` namespace and
  asserts each is a value-equatable record whose every field/property is a "cache-safe" type: primitives,
  `string`, enums, other Model records, or `EquatableArray<T>`. Forbidden: raw `ImmutableArray<T>`/arrays/
  `List<T>`, and anything from `Microsoft.CodeAnalysis` (`ISymbol`, `Compilation`, `SyntaxNode`,
  `Location`).
- **Why:** the behavioural caching test (`IncrementalCachingTests`, shipped) proves caching works *today*;
  this proves it *structurally* the instant someone adds a bad field, and encodes the rule in code.
- **Where:** new `tests/DwarfMapper.Generator.Tests/SelfValidation/ModelCacheSafetyTests.cs`. Pure
  reflection over the generator assembly; allow-list of cache-safe types; recurse into Model records.
- **Risk:** low. May surface an existing borderline field (good — that's the point).

### A2. Build-budget diagnostic (opt-in)
- **What:** a new Info diagnostic raised when a single mapper's resolved member+method count exceeds a
  threshold (e.g. > 150 mapped members), warning that a god-mapper may add IDE/compile latency and
  suggesting a split. Off by default via threshold; suppressible.
- **Diagnostic id:** claims the next free slot at implementation time. Because this ships before the
  `IMPROVEMENT-PLAN.md` *planned* diagnostics (nullable-unwrap, MapValue-shadow, etc.), it takes
  **DWARF055** and those tentative reservations shift up by one (the plan's ids were never binding —
  whatever is built first claims the next sequential id). Reconcile `IMPROVEMENT-PLAN.md` when implemented.
- **Why:** the only real cost of "all heavy work in the transform" is large-mapper IDE latency; this turns
  a latent mystery into a proactive, suppressible signal. (Counts only — no timing, which isn't available
  deterministically in-generator.)
- **Where:** descriptor + `AnalyzerReleases.Unshipped.md` line + emission in `MapperExtractor`; a
  triggering test (meta-test obligations honored).
- **Risk:** low; Info severity, threshold-gated, suppressible.

---

## Phase B — Surfacing the mapping plan (S–M)

The "explain this mapping" idea, realized two ways that both *compile* (a generator can only add C#, not
arbitrary files, so this is comment + XML-doc, not a `.md` written to disk).

### B1. Mapping-plan comment header in each `.g.cs`
- **What:** emit a structured comment block at the top of each generated mapper method body (or file)
  listing, per destination member: `← source` (or converter `via Method`, constant, ignored-and-why,
  flatten/path resolution, conversion applied). The plan data already exists in `MemberMap`/`MapMethodModel`.
- **Why:** makes "go to definition" on a generated `Map` a human-readable audit — the anti-mislinking story
  you can *see*. Free for review; zero runtime cost.
- **Where:** `MapEmitter` — a `EmitMappingPlanComment(MapMethodModel)` helper invoked per method. Snapshot
  (Verify) tests will move — update them (the comment is the intended change).
- **Risk:** low. Snapshot churn (expected). Keep the comment deterministic (ordered) so it doesn't break
  the determinism test.

### B2. XML-doc `<summary>` on generated public map methods
- **What:** emit `/// <summary>Maps Src→Dst. FullName→Name; ignores PasswordHash; City via Flatten(Address).</summary>`
  on each generated public method so the plan shows in IDE hover with no extra tooling.
- **Why:** discoverability at the call site, not just in the generated file.
- **Where:** `MapEmitter` method-signature emission. Share the plan-rendering helper with B1.
- **Risk:** low; must XML-escape member names. Snapshot churn.

---

## Phase C — DX tooling: code-fixes (M–L, gated)

A new `DwarfMapper.CodeFixes` analyzer package (netstandard2.0, `Microsoft.CodeAnalysis.CSharp.Workspaces`).
Start with the highest-delight, lowest-surface fixes:

- **C1. `DWARF052` → insert the missing `[ReverseMap]` inverse method declaration** (one-keystroke).
- **C2. `DWARF001` → insert `[MapIgnore(nameof(...))]` or a `[MapProperty(...)]` stub** (message text
  already names the fix).
- **C3 (later). `DWARF010` disambiguation, `DWARF015` enum completion.**
- **Where:** new project `src/DwarfMapper.CodeFixes/`, wired into the analyzer nupkg; new test project.
- **Risk:** new project + packaging; must stay netstandard2.0. Code-fix false-positives are low-impact
  (user previews). This is the largest net-new surface — ships after A/B.

---

## Phase D — Per-method extraction granularity (L, optional perf)

- **What:** refactor the pipeline so each mapper *method* is an independently-cached node combined into the
  class output, instead of one per-class model. A one-method edit then recomputes only that method.
- **Why:** the principled fix for large-mapper IDE latency (the Phase A2 signal's root cause).
- **Risk:** real pipeline complexity; class-level config (depth, naming, ref-handling) is shared and must be
  threaded as its own small cached input combined with each method. Deferred until A2 shows it matters or a
  large-mapper user reports latency. **Not implemented in the first pass** — captured here so the plan is
  complete.

---

## Phase E — Positioning & docs (no code)

- **E1.** A `docs/CORRECTNESS.md` that unifies the narrative: completeness gate + `[RoundTrip]` + mapping-plan
  audit (B) + proven determinism/caching + AOT/reflection-free. The signature identity.
- **E2.** Frame the completeness gate as a **DTO-drift contract gate** and the reflection-free property
  (plan item 16's token-scan meta-test) as a **compliance/AOT/CRA** signal in `COMPARISON.md`/README.
- **E3.** Fold the reflection-free static-analysis meta-test (IMPROVEMENT-PLAN item 16) in here as the
  evidence behind E2.

---

## Build sequence & testing

1. **A1, A2** — guards first; each with its own tests; full suite green.
2. **B1, B2** — share a plan-renderer; update snapshots; determinism test must stay green (ordered output).
3. **E1–E3** — docs, once B exists to point at.
4. **C1, C2** — code-fix package + tests.
5. **C3, D** — only on demand.

Every phase: `dotnet test -c Release` green (currently 2900), 0 warnings, `AssemblyScanTests` honored for any
new diagnostic, snapshots updated only for intended codegen changes. Nothing pushed without explicit
approval (per standing instruction).

## Out of scope
- Open generics / reflection (permanent non-goal).
- New *mapping* features (the un-selected brainstorm bucket).
- Writing arbitrary non-`.cs` files from the generator (not supported; B uses comments + XML-doc instead).
