<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Carry-forward: everything known and not yet done

Written at the close of the round-19 regression plan (branch `feat/surface-coverage-architecture`, 23 commits,
HEAD `9f75d8e`) and **before round 20 starts**, so that nothing found along the way is lost to a transcript.

Nothing here is a blocker for round 20. Every item is either a deliberate deferral with a stated reason, a
maintainer decision, or a known limitation already pinned by a ratchet. Items are grouped by what they *are*,
not by which task found them, because that is how they will be picked up.

**Status key:** `MUST` = fix before merging the branch · `LATER` = fold into a future task list ·
`DECIDE` = needs a maintainer ruling, not an implementer · `KNOWN` = accepted limitation, pinned, no action
planned.

---

## 1. Consumer-facing — the ones that reach a user

| # | Status | Item |
|---|---|---|
| 1.1 | `MUST` | **`CHANGELOG.md`: the new public member is not under `### Added`.** `IsUpdateAmbiguous` is genuine new public surface but appears only inside the `Fixed` prose. A consumer scanning `Added` for new API will miss it entirely. |
| 1.2 | `MUST` | **`CHANGELOG.md`: a consumer-facing `Fixed` entry for `ResetForTests`**, which is `internal`, `[InternalsVisibleTo]` a single test project, and unreachable by any consumer. It should not be in a consumer changelog at all. |

These two are the only items on this page that a user of the package could trip over. Everything else is
internal.

---

## 2. Maintainer decisions — not an implementer's call

| # | Status | Item |
|---|---|---|
| 2.1 | `DECIDE` | **`NullCollections` silent at `Projection`.** An explicit design decision, not an oversight. The entry records **three candidate resolutions** and a **reverted attempt**: refusing broke seven existing tests, one of which asserts the current ternary *on purpose*, because `Enumerable.Select(null!, …)` throws at query-evaluation time. Pick a resolution before anyone implements it. |
| 2.2 | `DECIDE` | **`ResetForTests` has zero callers repo-wide.** `internal`, IVT to one project; the only hits are comments saying it is unreachable. Round-19 Task 10 *extended* it (adding `UpdateMaps`/`UpdateAmbiguous`) as ruled — but **deleting it is arguably the honest fix**. Extending dead code preserves it. |
| 2.3 | `DECIDE` | **`CLAUDE.md`'s working note is stale *and* describes a superseded mechanism.** It says "5 hand-written fences in `docs/diagnostics.md`"; there were already 13 before round 19 touched it, and fence exemptions are now inline `fence-exempt` markers with required reasons, not an allowlist. **By that file's own stated rule, it should be deleted rather than corrected.** |
| 2.4 | `DECIDE` | **`internal` + `[InternalsVisibleTo]` was sized for one meta-attribute; there are now four** (`[DwarfSurface]`, `[DwarfSurfaceSite]`, `[DwarfSurfaceProbe]`, `[DwarfSurfaceOption]`). Still the right call, or does the test-only metadata now deserve its own home? |
| 2.5 | `KNOWN` | **`MaxDepth` silent at `SpanMap`/`AsyncStream`.** Lower severity than it sounds — the default bound of 64 still applies, so this is a *tighter* bound being ignored, not unguarded recursion. |

---

## 3. Test-infrastructure hardening

| # | Status | Item |
|---|---|---|
| 3.1 | `LATER` | **The `NoSuchSite` ratchet gates the total only.** Per-cause counts (68 no-mapping-method / 48 registry-has-no-mapper-class / 21 no-fixture-declares-one / 0 no-member-slot) live in the failure message, not in an assertion — so **offsetting drift** (+N in one cause, −N in another) passes silently. |
| 3.2 | `LATER` | **The fixture-baseline rule is a comment, not a gate.** *"A fixture that cannot compile without the element under test can never show that element doing nothing"* — learned the hard way when `[FlattenGraph]`'s fixture masked its own silence. Unenforced. **The trap for whoever hardens it:** a naive "baseline must compile" gate fires on four legitimate fixtures, because a `DWARF001`-by-design fixture produces `CS8795` in its baseline. |
| 3.3 | `LATER` | **`CorpusFor`'s throwing default arm is unreached by any test.** A correct fail-fast guard for a seventh `SurfaceCategory`, but the enum has exactly six members and all have explicit arms — so it is untested code, not an exercised guard. Cheap fix: `Assert.Throws` with an out-of-range enum cast. |
| 3.4 | `LATER` | **The `CrossAssembly` obligation is placement-blind.** `IsWritten(MultiAssemblyCorpus, …)` scans the whole tree, so a `[UsesMap]` row confined to a single project would satisfy it — for the one category whose entire claim is that it is only observable *across* an assembly boundary. Strictly better than the single-csproj version it replaced, but not yet tight. |
| 3.5 | `LATER` | **Nothing forbids a cell being both in `DeclaredDivergences.Reasons` and `StructurallyInapplicable`**, which would double-count it against two ratchets. Currently disjoint (`Registry` is not a `StructurallyInapplicable` endpoint) and creating overlap needs a deliberate ceiling raise. Theoretical. |
| 3.6 | `LATER` | **`DiagnosticCoverageRatchetTests` claims a property "holds by construction"**, but adding to `PredatesThisProject` is a visible-diff hatch that no test blocks. |
| 3.7 | `LATER` | **`IsGeneratorAuthored`'s remarks omit the `*.g.cs` collision case.** A consumer's own generator emitting `Foo.g.cs`, or a checked-in `.g.cs`, is silently exempted from `DWARF086`. Permissive-only (false negative), can never redden a consumer build — but undocumented at the method that decides it. |

---

## 4. Environment / tooling

| # | Status | Item |
|---|---|---|
| 4.1 | `LATER` | **`.git`-as-a-file breaks the doc tests in any worktree.** `RepoRoot` requires a `.git` **directory**; in a git worktree `.git` is a file. Three `GeneratedDocsAreCurrentTests` fail before reading any doc. Consequence beyond noise: **`The_option_support_matrix_matches_the_generators_actual_behaviour` never runs in a worktree**, so any generated-doc change authored from one is silently unverified locally. CI (a normal clone) still catches it. |
| 4.2 | `LATER` | **Both sibling Stryker configs are parse-fixed but have never been run to completion.** They had `comment` inside the `stryker-config` object, which Stryker 4.16 rejects outright — **the repo's mutation testing had never run at all.** Now fixed and given the `json` reporter, but their `break: 70` is inherited and unvalidated: nobody has measured what those projects actually score. |
| 4.3 | `KNOWN` | Excluding `DwarfMapper.Generator.Tests` from the runtime mutation leg keeps it at 44 minutes instead of hours. **Measured cost: zero** — `DwarfMapValidationException`'s constructors are bare `: base(…)` forwarders and produce no mutants at all. |

---

## 5. Known limitations already pinned (no action unless round 20 takes them)

| # | Status | Item |
|---|---|---|
| 5.1 | `KNOWN` | **96 cells (~11% of the matrix) carry the wrong verdict.** `CS8795` (unimplemented partial method, emitted *after* a blocking DWARF error) is read as `NotCompilable` where it means `Refused` — so those cells are skipped rather than judged. Pinned shrink-only at ceiling 107. **→ round-20 Task 10.** |
| 5.2 | `KNOWN` | **21 cells wholly unmeasured for want of two template slots.** `[MapTo]`@`Struct` (14) and `[DwarfMapperConstructor]`@`Constructor` (7) are legal, claimed, and unmeasurable because `EndpointSources` declares no struct and no annotatable constructor. Counted inside the `NoSuchSite` ratchet at 137. **→ round-20 Task 11.** |
| 5.3 | `KNOWN` | **Mutation score 66.95%, 39 survivors.** A starting ratchet, not a clean bill, for the one taxonomy where mutation is the only available proof. Kill-first: `DwarfMapperRegistry.cs:76` (the `return;` keeping a duplicate `Register` out of `InterfaceMaps` — stated invariant, **zero tests**), `DwarfMappingDepthException.cs:32` (`MaxDepth`/`ActualDepth` have **no reference of any kind**), `DwarfMapExceptions.cs:95`. |
| 5.4 | `KNOWN` | **`DwarfMapperRegistry.cs:291` (`Key.Equals`'s `&&`) is equivalent-in-practice — do not try to kill it.** `Key` is a `private readonly struct` whose only consumer is `ConcurrentDictionary`, which tests `hashcode == n._hashcode` *before* consulting `Equals`; `(A,B)` and `(A,C)` differ in hash so `Equals` is never reached. Recorded because **a test was once proposed to kill it that would have passed without killing it**, and been booked as mutation coverage. |
| 5.5 | `KNOWN` | Report §8 of the Task-9 record discloses two config deviations in its addendum rather than by amending the section itself. Cosmetic; self-acknowledged. |

---

## 6. The pattern worth remembering

Four separate times on this branch, a mechanism **reported success while measuring nothing**:

1. 122 `Registry` cells were fiction — the harness never drove `MapToGenerator`.
2. 95 cells "passed" as `NotCompilable` because the template emitted `CS0579` and nobody looked.
3. `[FlattenGraph]`'s fixture carried `DWARF001` in its **baseline**, so a non-acting element read
   `UnhonouredButLoud` and passed both claim branches.
4. The runtime Stryker config mutated **nothing** — 185 mutants "removed by filter", no score, **exit 0** —
   and `housekeeping.ps1` checked only `$LASTEXITCODE`.

All four were found by **running** something, never by reading it. Three were found only because a later,
unrelated task happened to execute the path. That is the argument for item 3.2 above, and the reason the
non-vacuity guards in this codebase are worth their weight.

A fifth instance, caught before it shipped: a test proposed to kill a mutation survivor **would have passed
without killing it** (item 5.4) — the false-credit failure appearing *inside* the tool built to detect it.
