<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Design: test-hardening programme — from "tests exist" to "claims are mechanised"

- **Date:** 2026-07-26
- **Status:** **PROPOSED** — no code written against it yet.
- **Component:** `DwarfMapper.Generator.Tests`, `DwarfMapper.IntegrationTests`, `samples/DwarfMapper.Conformance`,
  `.github/workflows/ci.yml`. Non-behavioural additions only to `src/`.

## 1. Problem

The suite is large and disciplined — ~5,640 tests, a 973-case golden corpus, thirteen self-validation scans,
six fuzz suites, zero `Skip`/`Retry`/`Sleep` markers, and a hollow-test detector that polices every `[Fact]` in
the repo. An audit of that layer in this session refuted five separate hypotheses about how it might be weak.

And yet a single afternoon of adversarial probing found **six real defects**, three of them silent
wrong-data:

| Defect | Class | How it evaded ~5,600 tests |
|---|---|---|
| `NullSubstitute` dropped by projection | silent wrong data | rename bound → completeness satisfied → nothing to report |
| `When=` dropped by projection | silent wrong data | same |
| `SkipNullSourceMembers` dropped by projection | silent wrong data | same |
| `NullStrategy` ignored by projection | runtime throw vs configured default | no test declared both paths on one mapper |
| `ImmutableArray<T>` as a **source** | emitted uncompilable code (CS0411/CS1061) | every collection test used `List<T>` (a *reference* type) as source |
| `[GenerateMap<T,T>]` | no diagnostic; silent shallow clone | a type trivially satisfies itself, so the completeness gate cannot object |

The pattern is one thing, not six. **Coverage was measured by "does a test exist for this feature?" — never by
"does this feature behave the same through every endpoint that exposes it?"** Every defect above lives in the
gap between an option and an endpoint: the option was tested, the endpoint was tested, the *cell* was not.

Three further observations frame the work:

**Claim-vs-mechanism drift is already present.** This session found three documentation claims that the
mechanism did not implement: `AutoValidate` was a dead flag the generator never read, the "informed dumps"
output was fabricated, and `CORRECTNESS.md` asserted CI "runs a behavioural gate over the published native
binary" when CI only ever *publishes* it. Docs are not tested, so they drift silently — and a security or
correctness claim that drifted is worse than one never made.

**The detectors were not themselves proven to detect.** `GeneratorAssertSelfTests` proves the assert *helpers*
fail on bad input, but nothing proved the hollow detector still discriminates. Adding that negative control
immediately exposed a hole: detection substring-matched over `ToFullString()`, so a comment mentioning
`Assert.` counted as the assertion.

**The gate is not closed.** `samples/DwarfMapper.Conformance` returns a real exit code from 47 assertions and
is never executed by CI. `aot-trim-gate` publishes the AOT sample and never runs it. Both are proofs that only
ever run when a human remembers.

## 2. Goals / Non-goals

**Goals**

- G1. Make endpoint divergence **structurally impossible to ship silently** — an option that reaches one
  endpoint and not another must fail a test, not a user's build.
- G2. Bind every published correctness/security **claim** to a named, executing test, and fail the build when a
  claim has no mechanism.
- G3. Prove the **detectors** detect, not merely that they run.
- G4. Turn conformance and AOT behaviour from manual rituals into **fail-closed** CI gates producing dated
  artifacts.
- G5. Keep the suite fast enough to stay in the inner loop (current full run ≈ 30 s generator + 0.5 s
  integration; budget +50 % worst case, and only in the non-default lane).

**Non-goals**

- Not a rewrite. The existing framework (`Framework/`, `Golden/`, `Fuzzing/`, `SelfValidation/`) is the
  substrate; this programme extends it.
- Not chasing flakiness. There is none to chase — see §4.1; Phase 1 is about *proving* determinism.
- Not raising a coverage-percentage number. Line coverage is not the failure mode; endpoint × option cells are.
- Not decomposing `MapperExtractor.ExtractCore` (cyclomatic 329). Separate programme, separate risk budget.

## 3. Phase 1 — Stabilise the substrate

**Finding: there are no flaky tests.** A scan for `Skip=`, `Retry`, `Thread.Sleep`, `DateTime.Now`, and
`new Random()` across all four test projects returns exactly one hit, and it is a false positive (a fixture
method named `Now()` returning `default`). The substrate is stable; what is *unproven* is that it is
**deterministic for the right reasons**.

Determinism here is load-bearing beyond flakiness: generated helper names are hashes, the golden manifest
compares emitted text byte-for-byte, and reproducible builds depend on identical output across machines. Two
real nondeterminism bugs were found and fixed this session, which is the argument that more exist:

- Four `SortedSet<(string,string)>` used the default tuple comparer, which routes to culture-sensitive
  `string.CompareTo`. Emitted check order, message order, and diagnostic order therefore varied by machine
  culture (verified diverging under `tr-TR`). Now ordinal.
- `StableHash` — which produces the suffix in every generated helper name from ~10 call sites, and whose own
  doc demands stability "across processes and machines" — had **no behavioural test**. Now pinned to the
  published FNV-1a reference vectors.

**Deliverables**

1. **Nondeterminism ratchet** (`SelfValidation/DeterminismSourceScanTests`): a source scan over
   `src/DwarfMapper.Generator/` failing on banned primitives in any code path that reaches emission —
   `string.GetHashCode`, `StringComparer.CurrentCulture*`, `ToString()`/`Parse` without an `IFormatProvider`,
   `DateTime.Now`, `Guid.NewGuid`, and unordered `Dictionary`/`HashSet` iteration whose result is written to a
   `CodeWriter`. Allowlist with justification, capped and shrink-only, mirroring `HollowAllowlist`.
2. **Symbol-order independence property**: `GetMembers()` order is not guaranteed for a partial type split
   across files — a hazard already noted in `MapperExtractor.Projection.cs`'s own H1 comment. Add a fuzz
   property that emits a type across N file-orderings and asserts byte-identical output.
3. **Regression guards for this session's fixes**, so the substrate work is not re-derived later:
   already landed as `ValueTypeSourceCollectionTests` (13 target families), `SelfMapDiagnosticTests`,
   `StableHashTests`, `ProjectionRuntimeParityTests`, and `TestTheTestsScanTests.T5`.

**Exit criteria:** the ratchet is green with an allowlist of ≤ 3 justified entries; the file-order property
passes over ≥ 50 seeds; `DeterminismFuzzTests` extended to run under a non-invariant culture
(`tr-TR`, `de-DE`) in CI.

## 4. Phase 2 — Per-endpoint contract tests

This is the phase that closes the class of defect in §1.

**The matrix.** DwarfMapper declares **31 public attribute types**, which deduplicate to **26 usage names**
(generic and non-generic forms share one, e.g. `MapDerivedTypeAttribute` and `MapDerivedTypeAttribute<,>` →
`MapDerivedType`) — the same derivation `TestTheTestsScanTests` already performs for `MatrixExemptAttributes`,
and the matrix must reuse it rather than invent a second notion of "an attribute".

Not all 26 are member-level. Three groups are structurally outside a per-method matrix and become N/A rows
carrying that reason: assembly-scoped policy (`DwarfMapperOptions`, `DwarfMapperDefaults`,
`DwarfMapperValidationRoot`, `UsesMap`), generator-emitted manifests (`DwarfProvidesMap`, `DwarfRequiresMap` —
never hand-written), and the type-level front doors (`DwarfMapper`, `MapTo`, `GenerateMap`,
`GenerateWrapperMap`) which *select* an endpoint rather than modify one.

They are still enumerated, because "this attribute is not applicable to any endpoint" is exactly the kind of
claim that rots silently — `DwarfMapperDefaults` is documented as applying to "every `[DwarfMapper]` class",
which means it does **not** reach the `[MapTo]` registry, and nothing currently states that anywhere a reader
of the registry docs would look.

The remaining member-level attributes are crossed against **seven endpoints**, each a distinct code path with
its own resolver or emitter:

| # | Endpoint | Shape |
|---|---|---|
| E1 | create map | `partial TTarget Map(TSource)` |
| E2 | update-into | `partial void/TTarget Update(TSource, TTarget)` |
| E3 | projection | `partial IQueryable<T> Project(IQueryable<S>)` |
| E4 | span map | `partial void Map(ReadOnlySpan<S>, Span<T>)` |
| E5 | async stream | `partial IAsyncEnumerable<T> Map(IAsyncEnumerable<S>)` |
| E6 | registry front door | `[MapTo]` → `x.MapTo<T>()` (`MapToGenerator`) |
| E7 | co-located host | `[GenerateMap<S,T>]` on a plain class |

Every (attribute, endpoint) cell has exactly one correct status:

- **HONOURED** — the attribute applies; pinned by a behavioural assertion.
- **REFUSED** — inapplicable, and says so with a **named diagnostic** (the "error shape").
- **N/A** — structurally meaningless (e.g. `[RoundTrip]` on a span map), recorded with a reason.

**SILENT is not a permitted status.** Every §1 defect was a cell that was silently ignored; the matrix makes
that state unrepresentable, because a cell with no declared expectation fails the ratchet.

**Deliverables**

1. `Contracts/EndpointContractMatrix.cs` — the declared expectation per cell, one row per attribute, with the
   expected diagnostic id for every REFUSED cell.
2. `Contracts/EndpointContractTests.cs` — a `[Theory]` over the matrix: HONOURED cells compile clean and assert
   the behaviour; REFUSED cells assert the exact diagnostic id; N/A cells are skipped with their reason
   surfaced.
3. **Growth ratchet** — a self-validation scan reflecting over public attributes and over the endpoint list,
   failing when either grows without a matrix row. This is what makes the guarantee durable rather than a
   snapshot of today.
4. **Error-shape uniformity check** — every REFUSED cell's diagnostic must (a) exist in
   `AnalyzerReleases.Unshipped.md`, (b) be documented in `docs/diagnostics.md`, and (c) carry a message naming
   the attribute *and* a remedy. The `AllowNonPublic` case is the cautionary example: it refused correctly but
   reported `DWARF001 "no matching source member"` for a member that plainly existed.

**Prior art to reuse:** `FeatureInteractionCompileMatrixTests` + `MatrixExemptAttributes` already implement
this pattern along the *feature-combination* axis; Phase 2 adds the *endpoint* axis and reuses the ratchet
machinery verbatim.

**Exit criteria:** every (usage name × endpoint) cell declared — 26 × 7 = 182 rows, the majority of them N/A
with a stated reason, which is itself the deliverable; zero cells in SILENT; ratchet green; the four
projection fixes from this session expressed as matrix rows rather than bespoke tests.

## 5. Phase 3 — Security invariants as permanent guards

`docs/SECURITY.md` makes **7 structured claims** (uncontrolled recursion, generator-side SO, silent numeric
narrowing, culture-sensitive parsing, reflection/type-confusion, vulnerable dependencies, trim/AOT unsafety).
`docs/CORRECTNESS.md` makes **6 numbered guarantees**. Most already have tests somewhere — but the binding is
by convention and prose, so a claim can outlive its mechanism without anything failing. That is exactly what
happened to `AutoValidate`, the informed-dumps description, and the AOT "behavioural gate" sentence.

**Deliverables**

1. **Claim identifiers.** Each claim row gets a stable id (`SEC-01`…`SEC-07`, `COR-01`…`COR-06`) in the
   markdown, plus the name of the test that mechanises it.
2. **Invariant tests**, one per claim, named for its id — e.g. `Sec01_CyclicInput_ThrowsCatchableDepthException`,
   `Sec04_GeneratedParseAndToString_AlwaysPassInvariantCulture`,
   `Sec05_ProductionPath_ContainsNoReflectionTokens` (this one exists as `ReflectionFreeMetaTests`; it is
   *renamed and bound*, not rewritten).
3. **Claim↔test binding scan** (`SelfValidation/ClaimMechanismScanTests`): parse the claim ids out of the two
   markdown files, reflect the test assembly, and fail when a claim has no test of that name — or a bound test
   no longer exists. This is the mechanism that closes drift *for good*, because prose alone can no longer
   assert a guarantee.
4. **Negative controls.** Each security invariant gets a paired proof that it can fail — mirroring
   `IncrementalCachingTests`' existing negative control and `T5`. A depth-guard test that passes because the
   fixture never recursed is worth nothing.

**Exit criteria:** 13 ids bound; scan green; each invariant demonstrated red under a deliberate mutation
(recorded in the commit, not left as a claim).

## 6. Phase 4 — Adversarial / fuzz / matrix harness on generic fixtures

Existing: `CombinatorialSchema` (basic-type × shape × nullability), `SyntheticSchema` (feature-driven), and six
fuzz suites (`DeterminismFuzz`, `TopologyOracleFuzz`, `MetamorphicPropertyFuzz`, `CrossConfigFuzz`,
`IndependenceOracleFuzz`, `AllEmitPathsAgreeFuzz`). Two gaps, both previously identified in
`docs/research/` and both confirmed this session:

**No shrinking.** A failing seed reports the whole generated schema. Diagnosis is manual bisection, which
raises the cost of *acting* on a fuzz failure and therefore lowers the odds anyone does.

**No mutation testing.** Nothing proves a guard fails when the code breaks. This session mutation-tested one
guard by hand — reverting the projection fix turned 4 of 6 parity tests red — and that single sample is the
only evidence the suite catches regressions at all.

**Deliverables**

1. **Generic fixture core** (`Fuzzing/Fixtures/`): schema generation expressed over a shared shape vocabulary
   (member kind × type × nullability × collection shape × endpoint), so a new endpoint is a parameter rather
   than a new schema file. Subsumes the duplication between the two schemas.
2. **Shrinker**: on failure, minimise the schema (drop members, then simplify types, then relax options) while
   the failure reproduces; report the minimal repro and its seed.
3. **Bounded mutation battery** (`Fuzzing/MutationBatteryTests`, non-default lane): apply a fixed catalogue of
   semantic mutations to the generator — invert a conditional, drop a null guard, swap a comparer to
   culture-sensitive, remove a diagnostic emission — rebuild in-memory, and assert **at least one test fails**
   for each. A surviving mutant is a coverage hole with a name and address.
4. **Endpoint axis**: extend fuzzing to emit each schema through all seven endpoints and cross-check, which is
   Phase 2's guarantee stated as a property rather than a table.

**Exit criteria:** shrinker reduces a seeded failure to ≤ 5 members; mutation battery runs ≥ 20 mutants with
zero survivors, or each survivor filed as an issue with the missing test named.

## 7. Phase 5 — Conformance and proof as a fail-closed gate

Today CI runs: `build-test` (with coverage thresholds), `aot-trim-gate`, `codeql`, `sbom`. Two proofs exist but
are **not gates**:

- `samples/DwarfMapper.Conformance` — 47 runtime assertions, `return 1` on failure at 36 sites, never executed
  by CI.
- `aot-trim-gate` — `dotnet publish … -warnaserror` proves the build is trim/AOT-clean, then never runs the
  binary. `CORRECTNESS.md` claimed otherwise until this session corrected it.

**Deliverables**

1. **Execute the proofs.** CI runs the conformance binary and the published AOT sample and asserts exit code 0.
2. **Dated artifacts**, following the convention already established by `benchmarks/results/` (e.g.
   `2026-07-05-flat-blit-linux.md`): each gate writes `conformance/results/<date>-<rid>.md` recording the
   commit, RID, assertion count, and result. An artifact is evidence with a date on it; a green check is not.
3. **Fail-closed semantics.** The gate fails when the run fails **and** when it did not happen: no artifact,
   an artifact whose commit ≠ `GITHUB_SHA`, or an assertion count that *decreased* versus the previous
   artifact. Silence must be failure — a skipped proof currently looks identical to a passing one, which is the
   same defect class as a vacuous test.
4. **Assertion-count ratchet.** The conformance sample's count may only grow; the README figure is derived from
   the artifact rather than hand-maintained. (It was wrong by one — "48" against 47 actual — until this session.)

**Exit criteria:** a deliberately broken conformance assertion turns CI red; deleting the artifact step turns
CI red; the README figure is generated.

## 8. Sequencing, cost, risk

Phases are ordered by dependency, and each is independently shippable:

1. **Phase 1** first — determinism underpins the golden manifest that every later phase leans on.
2. **Phase 2** next — highest defect yield per unit effort; it is the phase that would have caught six of six
   defects found this session.
3. **Phase 3** — cheap once Phase 2's ratchet machinery exists; mostly binding and naming.
4. **Phase 5** before **Phase 4** if budget is tight: a fail-closed gate protects everything already built,
   whereas the harness mainly finds *new* defects. Phase 4 is the largest and the most deferrable.

**Runtime budget.** Phases 1–3 and 5 are cheap (scans, table-driven theories, one CI execution). Phase 4's
mutation battery is expensive by nature and belongs in a nightly/manual lane, never the inner loop.

**Risks**

- *Ratchet fatigue.* Three ratchets already exist; four more could turn "add an attribute" into a chore. Every
  ratchet must name the exact file and row to add, in its failure message.
- *Matrix rot.* 161 cells is a maintenance surface. Mitigated by generating the skeleton from reflection and
  requiring only the *expectation* to be hand-written.
- *Mutation-battery flakiness.* In-memory rebuilds are slow and can time out. Bound the catalogue; keep it out
  of the default lane.
- *Over-testing trivia.* The naming-convention coverage map reports 119/120 types "uncovered" including
  `CollectionConverter`; acting on it would produce ~119 ceremonial unit tests. Coverage is judged by cells and
  claims, never by that metric.

## 9. Acceptance

The programme is done when:

- an option added to one endpoint and not another **fails a test** (Phase 2 ratchet);
- a claim in `SECURITY.md`/`CORRECTNESS.md` without a live test **fails the build** (Phase 3 scan);
- a proof that did not run **fails CI** (Phase 5 fail-closed);
- and each of the above has been demonstrated red by deliberate mutation, recorded in its commit.

Until a guard has been seen to fail, it is a claim — which is the failure mode this whole programme exists to
end.
