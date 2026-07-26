<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Plan: test-hardening programme

- **Date:** 2026-07-26
- **Spec:** [`specs/2026-07-26-test-hardening-programme.md`](../specs/2026-07-26-test-hardening-programme.md)
- **Status:** NOT STARTED

Execution order is the spec's §8: **P1 → P2 → P3 → P5 → P4**. Phase 5 deliberately precedes Phase 4 — a
fail-closed gate protects everything already built, whereas the harness mainly finds *new* defects, so if
budget runs out Phase 4 is the right thing to be missing.

Every step below lands as **one commit**, leaves the suite green, and states its own verification. A step that
cannot be verified red-then-green is not done.

---

## P1 — Stabilise the substrate

| # | Step | Verify |
|---|---|---|
| 1.1 | `SelfValidation/DeterminismSourceScanTests` — ban `string.GetHashCode`, `StringComparer.CurrentCulture*`, provider-less `ToString`/`Parse`, `DateTime.Now`, `Guid.NewGuid`, and unordered `Dictionary`/`HashSet` iteration feeding a `CodeWriter`. Allowlist ≤ 3, shrink-only, failure message names file + line + the fix. | Re-introduce the culture-sensitive `SortedSet` comparer fixed in `10b8932` → scan goes red. |
| 1.2 | Run `DeterminismFuzzTests` under `tr-TR` and `de-DE` (xunit fixture setting `CurrentCulture`), not just invariant. | Revert the ordinal comparer → red under `tr-TR`, green under invariant. |
| 1.3 | File-order independence property: emit one type declared across N partial-file orderings, assert byte-identical output. Targets the `GetMembers()` hazard already flagged in `MapperExtractor.Projection.cs`'s H1 comment. | Seeded ≥ 50 orderings; document whether it finds anything (a clean result is a result). |

**Done when:** ratchet green with ≤ 3 justified entries, both cultures pass, property runs in the default lane.

---

## P2 — Per-endpoint contract matrix *(highest defect yield — do not defer)*

| # | Step | Verify |
|---|---|---|
| 2.1 | `Contracts/Endpoints.cs` — the seven endpoint shapes (E1–E7) as reusable source templates, so a cell is `(attribute, endpoint) → expectation` and nothing else. | — |
| 2.2 | `Contracts/EndpointContractMatrix.cs` — 26 usage names × 7 endpoints. Reuse `TestTheTestsScanTests`' existing usage-name derivation; do **not** invent a second one. Each cell: `Honoured`, `Refused(diagnosticId)`, or `NotApplicable(reason)`. | Skeleton generated from reflection; expectations hand-written. |
| 2.3 | `Contracts/EndpointContractTests.cs` — theory over the matrix: Honoured ⇒ compiles clean + behaviour asserted; Refused ⇒ that exact id reported; N/A ⇒ skipped with reason. | Flip one Refused cell to Honoured → red. |
| 2.4 | Growth ratchet: fail when a public attribute or an endpoint appears without a row. | Add a scratch attribute → red naming the missing row. |
| 2.5 | Error-shape uniformity: every Refused id must exist in `AnalyzerReleases.Unshipped.md`, be documented in `docs/diagnostics.md`, and name both the attribute and a remedy. | `AllowNonPublic` is the cautionary case — it refused correctly while reporting `DWARF001 "no matching source member"` for a member that existed. |
| 2.6 | Fold this session's four projection fixes into matrix rows; delete the bespoke tests they replace *only* once the rows are proven red-then-green. | Suite count must not silently drop. |

**Done when:** 182 rows declared, zero SILENT, ratchet green.

---

## P3 — Security invariants

| # | Step | Verify |
|---|---|---|
| 3.1 | Add stable ids to the claim tables: `SEC-01…SEC-07` in `docs/SECURITY.md`, `COR-01…COR-06` in `docs/CORRECTNESS.md`, each naming its test. | Docs-only commit. |
| 3.2 | One invariant test per id, named for the id. Bind existing tests where they exist (`ReflectionFreeMetaTests` → `SEC-05`) by renaming, not rewriting. | — |
| 3.3 | `SelfValidation/ClaimMechanismScanTests` — parse ids from both markdown files, reflect the test assembly, fail on a claim with no test **or** a binding pointing at a test that no longer exists. | Delete a bound test → red. Add an unbound claim row → red. |
| 3.4 | Negative control per invariant: demonstrate each red under a deliberate mutation and record it in the commit message. | A depth-guard test whose fixture never recursed is worth nothing — this step is what proves otherwise. |

**Done when:** 13 ids bound, scan green, every invariant demonstrated red at least once.

---

## P5 — Fail-closed conformance gate *(before P4)*

| # | Step | Verify |
|---|---|---|
| 5.1 | CI executes `samples/DwarfMapper.Conformance` and asserts exit 0. It already returns `1` from 36 sites and has never been run by CI. | Break one assertion → CI red. |
| 5.2 | CI runs the published AOT binary per RID and asserts exit 0, making `CORRECTNESS.md`'s "behavioural gate" sentence true rather than corrected-away. | Break an AOT assertion → red. |
| 5.3 | Emit `conformance/results/<date>-<rid>.md` (commit sha, RID, assertion count, result), following the `benchmarks/results/` convention. | Artifact present and dated. |
| 5.4 | Fail-closed: red when the run fails, when no artifact exists, when artifact commit ≠ `GITHUB_SHA`, or when the assertion count **decreased**. | Delete the artifact step → CI red. This is the step that makes silence ≠ success. |
| 5.5 | Derive the README assertion figure from the artifact. | It read "48" against 47 actual until this session. |

**Done when:** every failure mode in 5.4 demonstrated red.

---

## P4 — Adversarial harness *(largest; most deferrable)*

| # | Step | Verify |
|---|---|---|
| 4.1 | `Fuzzing/Fixtures/` — one shape vocabulary (member kind × type × nullability × collection shape × endpoint); migrate `CombinatorialSchema` and `SyntheticSchema` onto it. | Existing fuzz suites stay green through the migration. |
| 4.2 | Shrinker: on failure, minimise (drop members → simplify types → relax options) while the failure reproduces; report minimal repro + seed. | Seed a known failure → repro shrinks to ≤ 5 members. |
| 4.3 | `Fuzzing/MutationBatteryTests` (**non-default lane**): catalogue of semantic mutations — invert a conditional, drop a null guard, swap a comparer to culture-sensitive, remove a diagnostic emission — rebuilt in-memory; assert ≥ 1 test fails per mutant. | ≥ 20 mutants, zero survivors — or each survivor filed with the missing test named. |
| 4.4 | Endpoint axis: emit each schema through all seven endpoints and cross-check. P2's table restated as a property. | Should independently rediscover the projection divergences. |

**Done when:** shrinker meets its bound; mutation battery has no unexplained survivors.

---

## Tracking

Each phase gets its own follow-up commit trail; this plan is updated in place with status per step.
A step is **not** complete on "tests pass" — it is complete when the guard has been *seen to fail* for the
right reason. Until then it is a claim, which is the thing this programme exists to end.
