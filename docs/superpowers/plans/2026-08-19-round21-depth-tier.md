# Round 21 — the depth tier and analysis tooling

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.

**Goal (maintainer's words):** retain the same coverage, but shift from ~4-minute surface testing to in-depth
full testing in a tier that runs **below 44 minutes**. Find and RUN additional static and dynamic analysis
tools. Improve tests and test benchmarks.

**Measured baselines at `dc385d4` (merged master, 2026-08-19):**
- Fast tier: build 26s + full suite **64s** (7,657 tests, 8 projects), surface matrix 31s of that.
- Runtime mutation leg (full suite, Stryker 4.16): **12m25s**, score 61.02%, break 61. Nightly CI.
- Generator + DocTooling mutation legs: **never run to completion** (C3) — break 70 inherited, unvalidated.

**Architecture: two tiers, one contract.**
- **Fast tier** (per push): exactly what exists today. Nothing moves out of it — "retain same coverage".
- **Deep tier** (nightly + workflow_dispatch, one `deep-test` job): everything below, total **< 44 min**,
  each component individually budgeted and measured, with a printed per-component timing table so the budget
  is enforceable rather than aspirational.

## Global constraints
- Never `git push`. Explicit pathspec on every commit. `src/DwarfMapper.Generator` stays netstandard2.0.
- Warnings-as-errors, `AnalysisMode=All`, nullable, SPDX, doc-comment density.
- **No ratchet raised**; new gates start at measured values. Surface matrix stays green at 866/866.
- Fast-tier wall-clock must NOT grow by more than ~10% from any change here; measure before/after.
- Every tool adopted must be RUN with measured output in the task report — not merely referenced.

## Tasks

### T1 — Static-analyzer evaluation, adoption, and first run
Candidates to evaluate against this repo (trial each, count findings, measure build-time cost):
Meziantou.Analyzer, Roslynator.Analyzers, SonarAnalyzer.CSharp, ErrorProne.NET.Structs+CoreAnalyzers,
Microsoft.CodeAnalysis.PublicApiAnalyzers (public-API freeze — directly serves the surface-tracking story),
Microsoft.CodeAnalysis.BannedApiAnalyzers, IDisposableAnalyzers, Microsoft.VisualStudio.Threading.Analyzers.
Adopt the subset whose signal is real for THIS repo; triage every finding: fix, suppress-with-reason, or
disable-rule-with-reason in `.editorconfig`. Zero unexplained suppressions. Build must stay 0 warnings.
PublicApiAnalyzers gets `PublicAPI.Shipped.txt`/`Unshipped.txt` seeded from the real surface — this is the
compile-time twin of the matrix's surface tracking.

### T2 — Dynamic tooling: coverage floor, API-compat, IL verification
- coverlet + ReportGenerator: measure line/branch coverage per assembly, commit a baseline, add a shrink-only
  floor gate (deep tier). The point is a NUMBER for "retain same coverage" — today coverage is asserted by
  the matrix, never measured as a percentage.
- Microsoft.DotNet.ApiCompat (or PublicApiAnalyzers files if T1 adopted them): baseline the shipped API.
- ILVerify over the emitted runtime + a generated-output sample: the EmittedInvalidCode verdict's runtime twin.

### T3 — C3: run both sibling Stryker legs, set honest breaks
Both configs are parse-fixed but have NEVER completed a run. Run each, record wall-clock + score, set `break`
to the measured floor (never the inherited 70), list survivors with kill-first judgements as C6 did for the
runtime leg. These two runs are deep-tier components — their wall-clock feeds T5's budget.

### T4 — Depth knobs in the existing suites
- `DWARF_DEEP=1` (env) multiplies: FeatureCombination fuzz iterations, torture rounds, property-test cases.
  Fast tier keeps today's counts exactly; deep tier turns the knob. Document each knob's fast/deep values.
- E4 (from round 20): isolate the culture-swapping tests into their own serial collection so the rest of
  IntegrationTests parallelises — with the registry-static audit that item warns about.
- CsCheck (or FsCheck) property tests for the runtime registry + TypeFacts/comparers: the members mutation
  testing flagged as weakly pinned (C6 survivors) become property-tested.

### T5 — The `deep-test` CI job and budget enforcement
One nightly job: T3's two mutation legs + the runtime leg (exists) + T2's coverage gate + T4's deep knobs +
ILVerify. Print a per-component timing table; fail the job if total exceeds 44 min (a budget gate, so growth
is a deliberate act). Local entry point in `scripts/housekeeping.ps1 -Deep`.

### T6 — Close the EmittedInvalidCode population: B27 + B33
The 10 cells where the generator emits code that does not compile: B27 (8 cells — `[GenerateMap<A,B>]` beside
a same-pair partial is bare CS0111) and B33 (2 cells — `Preserve` at Span/AsyncStream emits CS7036). Fix both;
`EmittedInvalidCodeCellCeiling` 10 → 0 re-measured. This is "retain coverage" made literal: the population the
file says must not exist, actually emptied.

### T7 — C6: kill the top mutation survivors
`DwarfMapperRegistry.cs:76` (duplicate `Register` → `InterfaceMaps`, stated invariant, zero tests),
`DwarfMappingDepthException.cs:32` (`MaxDepth`/`ActualDepth` unreferenced), `DwarfMapExceptions.cs:95`.
Do NOT attempt `Key.Equals`'s `&&` — equivalent in practice, recorded in the ledger. Re-run the runtime
mutation leg after; if the score rises, raise `break` to the new floor (tightening is the allowed direction).

### T8 — Benchmarks as tests
`benchmarks/` exists (BenchmarkDotNet). Add a deep-tier smoke leg: run the benchmark suite in a fast
validation mode (ShortRun/dry) so a benchmark that CRASHES or a regression > a stated factor fails the deep
job. Draw payloads from the fuzzer/fixtures per the house rule, not hand-built literals.

## Ordering
T1 and T3 are disjoint (props/editorconfig vs stryker configs) — run in parallel worktrees.
Then T2, T4 (needs T1's analyzers settled to avoid churn), then T6, T7 (product), then T5 (composes all
measured components), T8 last. B-item batch and D-c fold in where their files are already open.
