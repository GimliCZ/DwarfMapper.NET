<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Design — Correctness-first generator hardening & DX

**Status:** Phases A, B, E **landed** (2026-06-18) · C & D remaining (see status note below) · **Date:** 2026-06-18

## Status (2026-06-18)

- **Phase A — DONE.** `ModelCacheSafetyTests` (structural cache-safety) + `DWARF055` build-budget Info.
- **Phase B — DONE.** XML-doc mapping plan on every public generated method (`MapEmitter.EmitMappingPlanDoc`);
  55 snapshots updated (verified additive-only).
- **Phase E — DONE.** `ReflectionFreeMetaTests` (E3), `docs/CORRECTNESS.md` (E1), COMPARISON/README framing (E2).
- **Phase C — NOT STARTED (deliberate).** The `DwarfMapper.CodeFixes` package is a net-new netstandard2.0
  project + `Microsoft.CodeAnalysis.*.Workspaces` dependency + analyzer-nupkg packaging wiring + a new
  code-fix-testing harness. That is a restore-fragile, packaging-heavy lift whose failure mode is a broken
  solution build. It deserves its own focused session rather than a rushed tail-end attempt that risks the
  green build maintained through A/B/E. Recommended first slice when picked up: the `DWARF052` "insert the
  missing [ReverseMap] inverse method" fix (highest delight, smallest surface), then `DWARF001`.
- **Phase D — DEFERRED (optional perf).** Per-method extraction granularity; revisit only if a large-mapper
  user reports IDE latency or the `DWARF055` signal starts firing in real projects.

All landed work: full suite 2970 green, 0 warnings, nothing pushed.

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
