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

**All three are one edit to `CHANGELOG.md`, and it is round-20 Task 0.**

| # | Status | Item |
|---|---|---|
| 1.1 | `MUST` | **`DWARF086` is absent from `CHANGELOG.md`.** It is in `AnalyzerReleases.Unshipped.md`, `docs/diagnostics.md` and `docs/generated/diagnostics-index.md` — but the CHANGELOG's own preamble **mandates an entry for any new diagnostic id**, and the release workflow publishes that section verbatim as the GitHub Release notes. **A new build-breaking `Error` would ship unannounced.** Nothing guards this: `AssemblyScanTests` syncs descriptors ↔ AnalyzerReleases only, and **no test in the repository reads `CHANGELOG.md` at all**. |
| 1.2 | `MUST` | **The new public member is not under `### Added`.** `IsUpdateAmbiguous` is genuine new public surface but appears only inside the `Fixed` prose. A consumer scanning `Added` for new API will miss it. |
| 1.3 | `MUST` | **A consumer-facing `Fixed` entry for `ResetForTests`**, which is `internal`, IVT to a single test project, and unreachable by any consumer. It does not belong in consumer release notes. |
| 1.4 | `LATER` | **~80 diagnostics predate `CHANGELOG.md` and have never been announced.** The project has not shipped (`AnalyzerReleases.Shipped.md` is empty), so the first release notes should enumerate them. `PredatesTheChangelog` (in `AssemblyScanTests.cs`) is the worklist and shrinks as they are written. |

These three are the only items on this page a user of the package could trip over. Everything else is internal.

**Strongly recommended alongside:** a test that fails when a new `DWARF` id has no `CHANGELOG` line. The rule
forbidding an unannounced diagnostic is **the one stated invariant in this repository with no test behind it** —
which is precisely the class of thing this whole branch was built to eliminate.

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
| 3.8 | `LATER` | **"Shrink-only" is prose on both `PredatesTheChangelog` and `DiagnosticTestAllowlist`.** Nothing stops someone silently *appending* an id instead of writing the CHANGELOG entry. Exact membership is asserted, so the set cannot drift unnoticed — but growth is a one-line edit with no gate. Not a regression (it mirrors the pre-existing allowlist's convention), and the right fix is one guard covering both. |
| 3.9 | `DONE` | **CLOSED by task B9.** `Scan9`'s and `Scan8`'s verdicts are now extracted as pure functions (`UnannouncedIds`, `StatesAFix`) and driven over known-bad input, so a gutted assertion reddens. Proved by reverting each to the gutted form: the two controls failed while `Scan6a`/`Scan9` **themselves passed** — the scans cannot see their own hollowness. Stated limit at the control: it pins the predicate, not the `[Fact]`'s wiring to it. Original: **`Scan9_is_not_vacuous` guards corpus-emptiness but not tautology.** It would catch a mistyped path (the six-time historical failure, which is what it was asked to catch) but not `Scan9` itself being gutted to `Assert.True(true)`. |

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

## 5b. Found by the final whole-branch review (new — not in the per-task records)

| # | Status | Item |
|---|---|---|
| 5b.1 | `LATER` | **Stryker runs in no CI job at all.** `.github/workflows/` has zero references. The 66.95% is a manual 44-minute `housekeeping.ps1 -Mutation` leg recorded in a JSON comment — **free to regress silently**, which is the exact property the rest of the branch spends 5,000 lines preventing. |
| 5b.2 | `LATER` | **`docs/research/testing-conformance-REPORT.md:22` still reads "Mutation testing (Stryker.NET) — none"**, which now actively contradicts three configs and a measured score. Pre-existing, newly wrong. |
| 5b.3 | `DONE` | **CLOSED by task B2 — declared as `G6` in `SURFACE-MATRIX-FINDINGS.md`.** Measured at **70 duplicate cells**, not ~11: 10 cases × 7 endpoints, of which 10 sit in `D20`'s ratcheted list. Fixed by extending the slot-marker mechanism per site (`PropertySlotMarker` + `FieldSlotMarker`, resolved by `SlotMarkerFor`), giving every endpoint template a real field member, and routing `Registry`/`CoLocatedHost` member sites through the same splice. Re-measured whole-matrix: 70 → 0 duplicate pairs, **0 verdicts changed, no ratchet moved**, `D20` still accurate. Guarded by `SurfaceProbeTests.Property_and_Field_sites_are_not_measured_as_the_same_source`. Original finding: **`Endpoints.cs:281-285`: `AttributeTargets.Property or AttributeTargets.Field` is one arm that discards `site`.** For the two elements legal on both (`MapIgnore`, `MapProperty`), **every Field cell is byte-identical to its Property cell at all seven endpoints** — so a field-only divergence is invisible while the matrix reads as measured. ~11 of the 162 ratcheted cells. Same family as the Task-4 Critical (Method/Property producing identical source) and the G5 template gaps. Per this branch's own convention this deserves a **declared G6 entry**, not a silent fix. |
| 5b.4 | `LATER` | **The allowlist replacement never reached the sibling option matrix.** `OptionContractTests.cs:193-196` accepts `CellStatus.NotApplicable` on a **non-blank reason alone** — never re-measured, never counted (8 of 18 `ProjectionCells`). It is the one excuse class on the branch that **cannot go stale-red**, in direct contrast to `SurfaceParityTests.cs:457`, which re-classifies live. Related: `DeclaredDivergences.CoversOption` (`:543`) is endpoint-blind, so the option matrix can excuse collateral cells outside the 162 ceiling. |
| 5b.5 | `LATER` | **`SurfaceParityTests.cs:511` checks the evidence link's *shape*, not that the file and anchor resolve.** All 23 `<a id="D…">` anchors exist today, so this is latent rather than broken. |
| 5b.6 | `LATER` | Drift: `ci.yml:42,124` say "854 cells" (actual 861/865); `The_cells_with_no_declaration_site_are_counted_by_cause` open-codes `AssertRatchet`'s two asserts instead of calling it; `AssemblyScanTests.cs:72` still carries a private repo-root walk that `RepoPaths` exists to delete. |
| 5b.7 | `DECIDE` | **The `internal` + IVT decision, sharpened.** `src/DwarfMapper/AssemblyInfo.cs` is new — **the shipped, unsigned package previously had zero `[InternalsVisibleTo]`**. This is compile-time accessibility, not a vulnerability, but it grants full internal access to any assembly *named* `DwarfMapper.Generator.Tests`, and the concrete thing it exposes is `ResetForTests()`, which mutates process-wide static state. That sits badly against this project's honor-accessibility and CRA-defensive stance. The four meta-attributes themselves are harmless — inert metadata, zero runtime reads. |
| 5b.8 | `DECIDE` | **`ResetForTests` deletion, sharpened further.** Its IVT goes to `Generator.Tests`, but the registry tests live in `IntegrationTests`, **and** the Stryker runtime leg excludes `Generator.Tests` — so Task 10's two new `Clear()` lines are **unverifiable dead code by construction**. Deleting it also removes the only concrete reason for 5b.7's IVT. |

### The scan family that is text-satisfiable (all pre-existing, none introduced here)

> **CLOSED by tasks B1 + B9 (2026-08-17).** Three of the six listed below were genuinely vacuous — `Scan6a`,
> `Scan6b`, and `T3b` (the same defect as `Scan6b`, one file over, found by the sweep and not on this list).
> `Scan6a`'s needle is now the qualified `TargetKind.<value>` form — **not** the file exclusion suggested
> below, which was tried and failed 14 of 17 values for no defect, because `CollectionConverter.cs` holds the
> switch arms as well as the declaration. `Scan6b`/`T3b` were deleted in favour of
> `CollectionCoverageSelfValidationTests`, which reads the same enum reflectively and proves each value is
> actually EMITTED. `Scan3` and both `Scan5`s had their own file (and, for `Scan3`, the sibling exemption
> list) removed from their corpora; re-measured, nothing changed — the flaw was in the mechanism, not yet in
> the result. Both floors tightened to their measured values: 40 → 60 and 40 → 84. Full write-up in
> `.superpowers/sdd/2026-08-16-round20-generator-defects/task-B1-B9-report.md`.

The review hunted for a third vacuous mechanism and **found one, plus a family around it**:

- **`AssemblyScanTests.cs:428` `Scan6a_TargetKind_values_are_referenced_in_generator_source`** — its corpus is
  all of `src/DwarfMapper.Generator/**`, which **includes the file declaring `enum TargetKind`**
  (`Pipeline/CollectionConverter.cs:1011`). Every needle matches its own declaration text; `missing` is empty
  **by construction**, and deleting the test changes nothing. `Scan2` (`:344`) excludes its own defining file
  for exactly this reason — the fix is one line, already demonstrated five scans above.
- `Scan3` (`:363`) is discharged by the literal id array at `DiagnosticCoverageRatchetTests.cs:36-61`.
- `Scan6b` (`:444`) does bare `Contains` on BCL type names (`Array`, `List`).
- `Scan5`-options (`:117`) is self-satisfied by a comment in its own file.
- The `>= 40` floors at `:551`/`:556` sit against actual values of 59 and 82 — slack from birth.

Ruled out by the same sweep, and worth recording as *verified sound*: all six obligation corpora are guarded by
`Every_corpus_is_non_empty` (an empty corpus makes obligations **fail**, not pass); the four cell ratchets are
two-sided at their measured values; `IsWritten`/`IsAssigned` carry explicit prose-mention negative controls;
`Assert-MutantsWereTested` is sound; the `SurfaceMatrix` trait filter degrades benignly.

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

A sixth, found by the final review and **pre-existing rather than introduced**: `Scan6a` searches for an enum's
members in a corpus that includes the enum's own declaration, so it has always passed by construction. It sits
five scans below the one this branch deleted for being text-satisfiable, in a file this branch edited.

**The through-line:** every one of the six is the same failure — *a mechanism reporting success while measuring
nothing* — and not one was found by reading. That is the case for the non-vacuity guards in this codebase, and
the reason item 3.2 (the unenforced fixture-baseline rule) is worth closing rather than leaving as a comment.

---

## 7. Merge verdict from the final whole-branch review

**Merge after must-fixes.** The shortest path to mergeable is **one edit to `CHANGELOG.md`** (§1 above).
Nothing else found blocks.

Production code was reviewed clean: zero reflection anywhere in `src/DwarfMapper/*.cs`, trim/AOT posture
intact with the AOT gate still asserting NativeAOT-ness and running the binary, `DWARF086` double-guarded
against firing on the generator's own emission (`CompilationProvider` is pre-generation **and**
`IsGeneratorAuthored` checks `.g.cs`; all nine hint names end `.g.cs`) and scoped to `compilation.Assembly` so
referenced metadata is never judged, and the registry fix pinned by a guard asserting the exact triple.

**On the headline claim:** TRUE, with qualifications that are honestly stated — *but only where a test-reader
looks*. "Case-complete" means the executed cross-product for `ConsumerDirective` + `EmissionShape`; the other
six shipped attributes get executed-but-not-case-complete category obligations. That is the design, declared at
each element, not a hole. The disclosure is dense and accurate — and lives entirely in test sources,
`Issues/round20/`, and a Stryker config comment. **There is zero mention in `README`, `CHANGELOG` or `docs/`.**
No unqualified claim ships anywhere, which is the important half; but a reader of the shipped documentation
learns nothing about what is and is not covered.
