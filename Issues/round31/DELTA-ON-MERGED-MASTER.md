# Round 31 — delta between the plan's base and merged master

`ROUND31-TASKS.md` and `POST-ROUND30-IMPROVEMENT-RESEARCH.md` in this folder are **verbatim copies** of the
delivered artefacts and are not edited. Both were written against `feat/round30 @ 3fa3fcb` with round 30
**unmerged**; `ROUND31-TASKS.md` §1.1 instructs re-running T00 on merged master and re-locating any moved anchor
by quoted code text. This file is that re-location, measured on **`master` @ `b688bb9`** (round 30 merged, PR #5)
on 2026-09-26. Nothing here overrides a task; where a task and this file disagree, the task is the instruction and
this file is the evidence Opus needs before assigning it.

## Anchors that still hold

| anchor | plan says | measured on b688bb9 |
|---|---|---|
| golden manifest | ≥ 1,000 cases | **1,014** (`Golden/output-manifest.txt` line 1), 1,017 lines |
| Verify snapshots | 85 `*.verified.txt` | **85** |
| highest diagnostic id | DWARF111, new ids from DWARF112 | **DWARF111** |
| T01 offenders | 3 projects with `NoWarn NU1903` | **exactly those 3** (DifferentialTests, CleanCorpus, Benchmarks) |
| T09 anchor | `collectionRegs` loop in `AggregateEmitter.cs` | present |
| T10 anchor | `if (method.IsProjection)` in `MapEmitter.cs` | present |
| SDK pin | `global.json` 10.0.101, `rollForward: disable` | unchanged; `dotnet restore --locked-mode` clean |
| baseline test run | — | solution build 0 warnings / 0 errors, **10,127 tests green across 9 assemblies** |

## Anchors that moved

**T02's site count: the task is right at four.** An early reading of this file claimed five, from
`grep -c '== null ?'`; the fifth hit is a COMMENT at `Projection.cs:1204` describing the CS0173 two-arm case, not an
emission site. Corrected when T02 landed: four emission sites (908, 925, 1235, 1742), all with `srcType` in scope,
so T02's STOP condition ("no symbol for the compared expression is in scope") did not fire at any of them.

**`python3` does not exist on this machine — only `python`.** `round31-audit.sh` calls `python3` in three places
(`wide_count`, the T05 duplicate scan, the T20 harden-runner scan). They fail silently: `baseline` wrote `wide=`
(empty), which makes T08's gate `[ "$w" -lt "${wide:-0}" ]` compare against 0 and never report progress. The script
is kept verbatim; before using it, make `python3` resolve (a shim on PATH, or run the audit under WSL). Do not
"fix" it by deleting the check.

## Two tasks that collide with round 30, and must not be executed as written

### T07 is already built, and the round-30 version is stronger

Round 30 landed the parameter-ceiling ratchet in `9a22a61`:
`tests/DwarfMapper.Generator.Tests/SelfValidation/ResolverParameterDisciplineTests.cs`, alongside REG-02's
optional-option-parameter ban. It has the same design T07 specifies — hard ceiling 6, a shrink-only legacy
allowance pinned at **exact** counts, and a companion test that fails on stale rows — and it is stronger in three
ways: it walks with **Roslyn** rather than a regex, covers the **whole** `GeneratorSrcDir` rather than `Pipeline/`
only, and includes **local functions** so the ceiling cannot be evaded by extracting one. It was proven RED both
directions by hand (a method that grows past its row, and a row that no longer matches).

It currently carries **57 rows**, down from 64 at landing — `7184fb9` paid off CollectionConverter's seven
collection emitters via `CollectionEmit`, and the shrink-only test forced those seven rows to be deleted rather
than edited.

Consequences for round 31:
- Creating `Round31/ResolverParameterCeilingTests.cs` would add a **weaker duplicate** of a live gate. T07 should
  be re-scoped to "verify the existing ratchet and record its row count", or closed as done.
- `round31-audit.sh`'s T07 check tests for the Round31 file path, so it reports TODO for work that exists. Read it
  as "the Round31 file is absent", not "there is no ratchet".

**The count reconciliation, because the numbers look contradictory and are not.** The audit's regex definition
(`private|internal static` in `Pipeline/**`, parameters counted by commas inside `[^)]*`) yields **40** here, with
the worst at `ResolveProjectionCtorExpr` 16 and `ResolveUnflattenTarget`/`EmitBody` at 14 — which is exactly the
research's "30 → 47, worst 16". So the research used that definition. It **undercounts**, because `[^)]*` stops at
the first `)` inside the parameter list: `ResolveMembers` has **29** parameters and a `= new()` default, so it is
truncated and never counted. The Roslyn measure sees it and names it the worst method in the assembly. T07 step 3's
STOP condition ("row count differs from 47 by more than a few") would therefore fire on a correct tree — 40 by the
regex, 57 rows by Roslyn, neither of them 47, and none of it a defect.

### T08 contradicts a recorded ruling — owner decision required first

T08 says: create `src/DwarfMapper.Generator/Pipeline/ExtractionContext.cs`, an
`internal sealed record ExtractionContext(...)`, constructed in one place. Commit **`ae9c7ea`** (2026-08-26), which
extracted `ExtractCore`'s per-method loop, records the opposite decision verbatim:

> **THE CONTEXT IS THREE BUNDLES, NOT ONE.** The body closes over 35 locals, and a single "context" holding all of
> them would be a god-object that moved the mess rather than resolving it. They split by LIFETIME AND DIRECTION
> […] `MapperDeclarations` is read-only facts about the class computed once, `MapperPolicy` is the configuration,
> `MapperAccumulators` is what the loop fills.

Eight such types exist today: `MapperDeclarations`, `MapperPolicy`, `MapperAccumulators`,
`MethodExtractionContext`, `MemberResolutionContext`, `MemberLookups`, `MemberAccumulators`,
`FlattenGraphContext` — all landed byte-identical against the golden manifest. `MemberResolutionContext`'s own
remarks state the split was **measured**, by checking every `ResolveMembers` parameter for write sites, and that
`diagnostics` was deliberately kept OUT of the request bundle because it is written, not read.

The research reached its recommendation from the round-30 external audit, which reported that "`ExtractionContext`
(R26-02) did not land". That was a false negative from searching for a **type name**: the work landed under
different names, in a form the maintainer chose over the RFC's. Round 30 verified this the expensive way — an
`ExtractionContext.cs` was written and six methods migrated before the compiler (CS1628, an `in` parameter cannot
be captured by a lambda) forced a look at the call sites, where `ctx`, `acc`, `policy` and `decls` were already in
scope. It was reverted.

**So T08 needs an owner decision before Sonnet touches it**, and the choice is not "context or no context":
1. **Extend the existing bundles** to the families that still take loose parameters — the measured next candidate
   is the Projection family (`ResolveProjectionMembers` 21, `ResolveProjectionCtorExpr` 15/16,
   `ResolveProjectionExpr` 13, `ResolveProjectionNestedObjectExpr` 13), which shares
   `compilation, location, diagnostics, enumPolicy` across all four **and** `autoNest, nullAsNull,
   implicitConversions` across three — and those last three are already `MapperOptions` members, so part of that
   paydown is finishing R27-02's migration rather than designing anything. `MapperExtractor.Projection.cs` is in
   **no leg's mutate globs**, so it needs no mutation re-measure.
2. Add a ninth bundle only where no existing one fits, following the lifetime-and-direction split rather than the
   RFC's single object.
3. Overrule `ae9c7ea` deliberately, in a commit that says so.

## Smaller notes

- **T19** changes `ci.yml`'s default filter to `Category!=SurfaceMatrix&Category!=Perf`. Round 30 added a
  dashboard-publish step to the `mutation` job; T20's "harden-runner as the first step of **every** job in every
  workflow" now includes that job.
- **T03** is a guard test expected green: round 30's ledger already records `MaxDepth` behaviour, and the
  `DwarfMappingDepthException` path is live. If a row goes red, it is a finding, per the task.
- **Round-30 carry-forward not in these documents**, in case round 31 wants it: no PowerShell test harness exists
  for `scripts/gate-checks.ps1` (two defects were found there by reading alone, and those functions decide whether
  a mutation leg passes); and ~625 MB of uncited `StrykerOutput/` runs can be deleted without touching any pin.
