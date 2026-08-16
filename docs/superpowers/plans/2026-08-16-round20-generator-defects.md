# Round-20 generator defects — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this
> plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the generator defects the surface matrix measured, so that each `DeclaredDivergences` entry can
be deleted — and the build turns green again with the ceilings lowered.

**Architecture:** Twenty-one divergences (D1–D21) plus two carried-over entries were recorded rather than fixed,
under the maintainer's "record now, fix separately" ruling. They are **not twenty-three independent bugs**:
they collapse into **seven root causes** plus one codegen bug and two instrument gaps. This plan is organised by
root cause, because fixing one closes several findings at once — and because fixing them finding-by-finding
would mean touching the same resolver five times.

**Tech Stack:** C# / .NET 10 runtime package; netstandard2.0 Roslyn source generator (**never retarget**); xUnit.

**Source of truth:** `tests/DwarfMapper.Generator.Tests/Contracts/DeclaredDivergences.cs` (23 entries, each with
measured evidence and the counter-evidence ruling out "structural") and `Issues/round20/SURFACE-MATRIX-FINDINGS.md`.
`Issues/round20/GeneratorIssue.txt` is a **stale mid-work snapshot** — it says 18 divergences and recommends
withdrawing D11, both superseded. Do not work from it.

## Global Constraints

- **Never `git push`.** Commit locally only.
- **`src/DwarfMapper.Generator` stays `netstandard2.0`**; `src/DwarfMapper` stays `net10.0`, `IsTrimmable`,
  `IsAotCompatible`, **zero reflection**.
- Nullable on; **warnings are errors** with `AnalysisMode=All`; no unused members; SPDX header on new files.
- **Build the WHOLE solution** (`dotnet build DwarfMapper.NET.sln`) for every task here. All of these change
  generator emission or diagnostics, and `samples/` is not covered by `dotnet test`.
- Any **new diagnostic** must sync `AnalyzerReleases.Unshipped.md`, `docs/diagnostics.md` (whose C# fences are
  documented `fence-exempt` non-compiling illustrations), and gain a `NegativeCases` row pinning **id and
  remedy wording** — `Every_covered_diagnostic_pins_its_remedy_wording` enforces the second half.
- Match surrounding doc-comment density. This repo's comments state *why* a gate exists and *what defect it
  caught*.

## The mechanic that governs every task here

Each `DeclaredDivergences` entry **asserts the divergence still exists**.
`Every_declared_divergence_is_still_a_divergence` re-runs `SurfaceProbe.Classify` live for every declared cell
and fails when a cell stops being `Silent`.

**So fixing a generator defect turns the build RED.** That is the ratchet working. The task is not complete
until you have also:
1. deleted the entry from `DeclaredDivergences.Reasons`,
2. lowered the three shrink-only ceilings (currently **findings 23**, **declared cells 162**, **structural 12**)
   to their new measured values,
3. updated the corresponding section in `Issues/round20/SURFACE-MATRIX-FINDINGS.md`.

**Never lower a ceiling before the fix lands, and never widen an entry to absorb a cell you did not fix.**

## Root-cause map

| Root cause | Findings | Cells |
|---|---|---|
| **A** — element-wise endpoints do not inherit method-level directives | D1, D2, D16 | ~9 |
| **B** — Projection does not honour member directives | D6, D9, D10 | ~13 |
| **C** — missing arity checks (class model has none; registry's never fires) | D4, D5, D21 | ~13 |
| **D** — the named-argument payload is discarded silently | D3 | 20 |
| **E** — `MapToGenerator` ignores assembly-level configuration | D17, D18, D19 | 3 |
| **F** — `MapperExtractor` reads directives off class/method symbols only | D20 | 20 |
| **G** — `[MapNullSkip]` class and method forms are exact inverses | D7 | ~6 |
| **H** — directives that act at exactly one endpoint | D8, D11, D12, D13, D14, D15 | ~60 |
| **N4** — generator emits uncompilable code | — | 1 |
| **G4/R4** — instrument: `CS8795` read as `NotCompilable` | — | 96 |
| **G5** — instrument: no struct / constructor slot in the templates | — | 21 |

---

### Task 1: N4 — duplicate `[FlattenGraph]` emits uncompilable code

**Highest severity in round 20: the generator produces source that does not compile.**

**Files:** the `FlattenGraph` emitter under `src/DwarfMapper.Generator/`; `tests/DwarfMapper.NegativeCases/`.

- [ ] **Step 1: Reproduce and capture.** Two identical `[FlattenGraph("Root","Flat")]` on one mapping method
      emit `CS1912: Duplicate initialization of member 'Flat'` **in `Demo.M.g.cs`** — the generated file. Write
      a generator test that asserts the emitted source *compiles*, and see it fail. Confirm the diagnostic's
      location really is the generated file, not the fixture.

- [ ] **Step 2: Decide the semantics, then implement.** Two identical directives are either (a) a duplicate the
      generator should collapse, or (b) a caller error it should refuse. Prefer **(b) refuse** — a duplicate
      directive is a copy-paste mistake and silently collapsing it hides the mistake. If you refuse, mint the
      next free `DWARF0xx` (verify with `Scan1f_Descriptor_Id_has_no_gaps_except_reserved`; `DWARF086` is now
      taken) and sync all three ancillary files per the Global Constraints.

- [ ] **Step 3: Test both directions.** The positive test (duplicate refused / collapsed) and a negative
      control (a single directive still emits and compiles). Make sure the control would catch an
      unconditional refusal.

- [ ] **Step 4: Whole-solution build**, then commit.

**Note:** N4 is deliberately **not** in `DeclaredDivergences` — it is an output defect, not a silence. It is
currently pinned only indirectly by the `NotCompilable` ratchet (which prints CS ids). Deleting nothing from
the store; lower no ceiling.

---

### Task 2: Root cause C — the missing arity checks (D4, D5, D21)

Three findings, one shape: **a caller uses the wrong overload of a directive and the build says nothing.**

- **D4** — `[MapProperty("Id","Name")]` (the *method* form) on a registry source member. The descriptor
  `RegistryDiagnostics.MapPropertyArity` **exists for exactly this** and measured, never fires.
- **D5** — `[MapIgnore]` with no target (the *registry* form) on a mapping method or mapper class names nothing;
  the class model performs **no arity check at all**.
- **D21** — `[MapProperty("Id")]` (the *member* form, one argument) on a mapping method resolves to
  `Source == Target`, the identity binding auto-matching already produces.

**Why D21 is a defect either way:** if the directive is discarded, an explicit binding evaporated; if honoured,
it was honoured as a no-op the caller cannot have wanted. **Refusal is the right answer in both readings**, and
closure is observable only as a refusal, since honouring is byte-identical by construction.

- [ ] **Step 1:** Write three failing tests — one per finding — asserting a diagnostic is raised.
- [ ] **Step 2:** Fix D4 by finding why the existing descriptor never fires. This is a wiring bug, not a missing
      feature; the descriptor is already declared.
- [ ] **Step 3:** Add the class-model arity check that D5 and D21 both need. One check, two findings.
- [ ] **Step 4:** Sync ancillary files for any new id; whole-solution build; delete D4, D5, D21; lower ceilings
      by their cell counts; update the findings doc. Commit.

**Rejected route, recorded so nobody retries it:** giving D21's case
`MapperOptions = "AutoMatchMembers = false"` to make honouring visible **breaches two shrink-only ceilings** —
member-site cells at `CoLocatedHost` go `Unasked` (that template carries no mapper class), and the
Create/Update baselines stop compiling and land in `UnhonouredButLoud`, the verdict-swallowing trap D11 was
rescued from.

---

### Task 3: Root cause D — the discarded named-argument payload (D3)

**The sharpest finding on the surface.** `[MapProperty("Id", Use = "…")]` on a mapping method names a converter
**that does not exist**, and compiles to byte-identical output with no diagnostic at all five mapper endpoints
— as do `When`, `NullSubstitute` and `StringFormat`. A caller has named a converter, a predicate, a null
substitute and a format string, and **the whole named-argument payload is discarded in silence.**

**It is settled as a divergence by the generator's own code:** the pair-scoped `MapProperty<S,T>` form raises
`DWARF014` / `DWARF049` / `DWARF050` for these exact named arguments. The refusals exist; this path never
reaches them.

- [ ] **Step 1:** Write failing tests for all four named arguments at one endpoint, asserting the same
      diagnostics the pair-scoped form raises.
- [ ] **Step 2:** Find where the pair-scoped form validates these and why the method form bypasses it. Route
      the method form through the same validation rather than duplicating it — a second copy of a validation
      rule is how the two forms diverged in the first place.
- [ ] **Step 3:** Extend to all five endpoints; whole-solution build; delete D3; lower ceilings by 20; update
      the findings doc. Commit.

---

### Task 4: Root cause F — `MapperExtractor` reads directives off class/method symbols only (D20)

20 cells, **one root cause, therefore one fix.** At the co-located host the mapping is declared **by** the
annotated type — `[GenerateMap<Src, Dst>]` sits on `Dst` — so a member of that type is part of the declaration.
That is exactly why `MapProperty`'s own `[DwarfSurfaceSite]` keeps `CoLocatedHost` claimed for the member sites
while dropping the five mapper endpoints, where the DTOs are ordinary types the consumer may not own.

Measured: every member-level `[MapProperty]` and `[MapIgnore]` case, on both the `Property` and `Field` site,
is byte-identical and silent there. `MapperExtractor` reads these attributes off the class or method symbol
only; `MapToGenerator`'s registry path is the sole reader of the member-level forms.

- [ ] **Step 1:** Write a failing test: `[MapProperty("Src")]` on a member of a `[GenerateMap<Src,Dst>]` type
      must affect the emitted map.
- [ ] **Step 2:** Teach the co-located-host extraction path to read member-level directives off the annotated
      type. **Reuse `MapToGenerator`'s existing member-level reader** rather than writing a second one.
- [ ] **Step 3:** Confirm the five mapper endpoints are **unaffected** — this must not start reading directives
      off DTOs the consumer does not own. That boundary is the whole reason the sites were split.
- [ ] **Step 4:** Whole-solution build; delete D20; lower ceilings by 20; update findings doc. Commit.

---

### Task 5: Root cause E — `MapToGenerator` ignores assembly-level configuration (D17, D18, D19)

Three findings, one gap: **the `[MapTo]` registry front door does not read assembly-level configuration.**

- **D19 is the serious one — it is a trust boundary.**
  `[assembly: DwarfMapperDefaults(AutoMatchMembers = false)]` says nothing is mapped unless the caller said so.
  The mapper-level form acts at all five method endpoints; the assembly-level form is honoured everywhere else
  and **dropped by the registry** — so an assembly that has switched auto-matching off still has every registry
  map auto-matching, silently. *Half a trust boundary is worse than none, because the developer believes they
  have one.* Same shape as the `DWARF077` gap, different endpoint.
- **D17** — `RegisterCollectionShapes = false` is silent at `Registry`, the one endpoint whose entire output
  *is* registry rows.
- **D18** — `PublicExtensions = true` is ignored by the registry, which emits its own extension class with its
  own accessibility choice.

- [ ] **Step 1:** Write three failing tests, D19 first.
- [ ] **Step 2:** Thread assembly-level configuration into `MapToGenerator`. One plumbing change should close
      all three; verify rather than assume.
- [ ] **Step 3:** Whole-solution build; delete D17, D18, D19; lower ceilings by 3; update findings doc. Commit.

---

### Task 6: Root cause G — `[MapNullSkip]`'s two forms are exact inverses (D6, D7)

`[MapNullSkip(true)]` on a **method** is honoured at CreateMap and UpdateInto, silent at Projection, SpanMap and
AsyncStream. `[MapNullSkip<Src,Dst>(true)]` on the **class** is honoured at SpanMap, AsyncStream and
CoLocatedHost, silent at CreateMap, UpdateInto and Projection. **The exact complement.**

The two are documented as the same option written at two scopes, so between them a caller reaches every
endpoint and with either alone reaches roughly half — silently. **One of the two is wrong.**

"Pair-scoped attributes do not reach method-declared pairs" is **not** the explanation: `MapProperty<S,T>`,
`MapValue<T>`, `MapIgnore<T>` and `MapConstructor<S,T>` all act at the method endpoints in the same run.

- [ ] **Step 1:** Determine which form's endpoint set is intended. This is a design question — if it is not
      obvious from the docs, **stop and ask the maintainer** rather than picking.
- [ ] **Step 2:** Write failing tests for the endpoints the chosen form does not reach.
- [ ] **Step 3:** Fix; whole-solution build; delete D6 and D7; lower ceilings; update findings doc. Commit.

---

### Task 7: Root cause A — element-wise endpoints do not inherit method-level directives (D1, D2, D16)

SpanMap and AsyncStream map the element pair through an **auto-synthesized** mapper that does not inherit the
declaring method's directives. This is the same shape as the `DWARF077` gap already fixed once.

- **D1** — `[MapIgnore("Id")]` honoured at CreateMap/UpdateInto/Projection, silent at both element-wise
  endpoints, from **both** the method and the class site. One mapper drops the member on three overloads and
  copies it on the other two.
- **D2** — `[MapProperty("Id","Name")]` is *refused* with `DWARF038` at CreateMap/UpdateInto and raises nothing
  at the element-wise endpoints. **The diagnostic that protects three overloads is simply absent from the other
  two.**
- **D16** — `[AfterMap]` is honoured at UpdateInto and blocks the build at CreateMap/Projection/AsyncStream —
  four endpoints where the caller learns something. **At SpanMap alone** it compiles and the hook is never
  called.

- [ ] **Step 1:** Write failing tests for all three at SpanMap and AsyncStream.
- [ ] **Step 2:** Propagate method-level directives into the synthesized element mapper. Check whether the
      existing `DWARF077` propagation fix is the hook to extend — if so, extend it rather than adding a parallel
      path.
- [ ] **Step 3:** Whole-solution build; delete D1, D2, D16; lower ceilings; update findings doc. Commit.

---

### Task 8: Root cause B — Projection does not honour member directives (D9, D10; D6 lands with Task 6)

Projection emits an expression tree via a separate translator, which does not consult several member-level
directives.

- **D9** — `[MapValue("Name", …)]` honoured at CreateMap/UpdateInto, silent at Projection, SpanMap and
  AsyncStream: the same mapper produces the constant on two overloads and the auto-matched source value on
  three.
- **D10** — `[Flatten("Child")]` honoured at CreateMap/UpdateInto, silent at the other three, where the
  flattened destination members are left at their defaults.

**Both entries carry a re-measurement note worth reading before you start:** the evidence originally filed for
each was a *refusal of a nonsense argument* (D9 assigned a string constant to an `int`; D10 named a scalar
member). Against fixtures with real members the directives are genuinely honoured at Create/Update and the
silences stand. **Do not re-derive the finding from the stale evidence.**

- [ ] **Step 1:** Write failing tests at Projection for both.
- [ ] **Step 2:** Fix, or **refuse with a diagnostic** where projection genuinely cannot express the directive
      — an expression tree has real limits, and a stated refusal is an acceptable outcome. Silence is not.
- [ ] **Step 3:** Whole-solution build; delete D9, D10; lower ceilings; update findings doc. Commit.

---

### Task 9: Root cause H — directives that act at exactly one endpoint (D8, D11, D12, D13, D14, D15)

The largest group (~60 cells). Each acts where it was first implemented and is **silently swallowed everywhere
else**. This is the ISSUE-044 shape the repo's own docs call the most frequent defect class of the whole
engagement.

| Finding | Directive | Acts at | Silent at |
|---|---|---|---|
| D8 | `[MapDerivedType]` (both forms) | CreateMap | Update, Projection, Span, Async |
| D11 | `[FlattenGraph]` | CreateMap | Update, Projection, Span, Async |
| D12 | `[Reinterpret]` | Create, Update, Projection | **Span, Async** |
| D13 | `[ReverseMap]` | CreateMap | Update, Projection, Span, Async |
| D14 | `[MapCollectionKey]` | UpdateInto | Create, Projection, Span, Async |
| D15 | `[GenerateWrapperMap]` | CoLocatedHost (`DWARF067`) | all five mapper endpoints |

Two worth singling out:

- **D12** is silent at exactly the two endpoints whose whole purpose is bulk element throughput — and therefore
  the two where a caller reaching for a forced blit most expects it to apply.
- **D15** is *refused* at CoLocatedHost with `DWARF067`, so the generator **does** read it and **does** have an
  opinion about where it is valid. On a `[DwarfMapper]` class it produces neither the wrapper map nor the
  refusal. Whichever answer is right, silence is not it.

**D11 carries a warning:** it was proposed for withdrawal and **the withdrawal was wrong** — the fixture's own
baseline did not compile, so its cells read `UnhonouredButLoud` and decided nothing. A fixture that cannot
compile without the element under test can never show that element doing nothing. Also: **the `×2` case at
CreateMap is not part of D11** — that is N4 (Task 1).

- [ ] **Step 1:** Take these **one directive at a time**, each with its own failing test, fix and commit. Six
      findings in one commit would be unreviewable.
- [ ] **Step 2:** For each: honour it at the claimed endpoints, or refuse with a diagnostic where it cannot
      apply. Where you refuse, the `[DwarfSurface(AppliesTo)]` claim may legitimately narrow — but **only** if
      you can state the reason in terms of the endpoint's shape, not the generator's current behaviour.
- [ ] **Step 3:** After each, whole-solution build; delete that entry; lower ceilings; update findings doc.

---

### Task 10: G4/R4 — 96 cells carry the wrong verdict

~11% of the matrix reads `NotCompilable` because of `CS8795` (unimplemented partial method), which the
generator emits **after** a blocking DWARF error. Conceptually those cells are **`Refused`**, not
`NotCompilable` — so they are skipped rather than judged.

**Fixing this makes ~96 cells judged for the first time and will surface new divergences.** Collect them; do not
ratify. Same rule as every other task: if a cell does not fit an entry you can articulate as a defect a
maintainer would act on, stop and report it.

- [ ] **Step 1:** In `SurfaceProbe.Classify`, distinguish "the compiler rejected the placement" from "the
      generator refused and therefore emitted nothing". A `CS8795` accompanied by a new blocking DWARF
      diagnostic is a **refusal**.
- [ ] **Step 2:** Re-run the matrix; triage every newly-judged cell into under-reach / structural / divergence
      exactly as Task 5 of the surface plan did.
- [ ] **Step 3:** Lower the `NotCompilable` ceiling from 107 to its new measured value. Commit.

---

### Task 11: G5 — 21 cells unmeasured for want of two template slots

`[MapTo]`@`Struct` (14 cells) and `[DwarfMapperConstructor]`@`Constructor` (7 cells) are legal per
`AttributeUsage`, are **claimed** by their elements, and are wholly unmeasured because `EndpointSources`
declares no struct and no annotatable constructor. This is *our* gap, not the product's — currently counted
inside the `NoSuchSite` ratchet at 137.

- [ ] **Step 1:** Add a struct slot and a constructor slot to the endpoint templates. Adding a site must stay a
      single edit in `Endpoints.cs` — that is the property that makes this maintainable.
- [ ] **Step 2:** Re-run the matrix. Expect new divergences as 21 cells become answerable; collect, do not
      ratify.
- [ ] **Step 3:** Lower the `NoSuchSite` ceiling from 137; update the per-cause breakdown. Commit.

---

## Not in this plan — maintainer decisions

- **`NullCollections` silent at Projection** is an explicit **design decision**, not an oversight. Its entry
  records three candidate resolutions and a reverted attempt: refusing broke seven existing tests, including
  one that asserts the current ternary *on purpose* because `Enumerable.Select(null!, …)` throws at
  query-evaluation time. **Pick a resolution before anyone implements it.**
- **`MaxDepth` silent at SpanMap/AsyncStream** — lower severity than it sounds: the default bound of 64 still
  applies, so this is a *tighter* bound being ignored, not unguarded recursion.

## Suggested order

**Task 1 first** (the generator emits uncompilable code). Then **Tasks 2 and 3** — they close 33 cells and both
are wiring bugs where the correct behaviour already exists elsewhere in the generator. Then **5** (D19 is a
trust boundary). Then **4, 7, 8**, which are propagation fixes of increasing blast radius. **Task 9** last among
the product fixes, taken one directive at a time. **Tasks 10 and 11** are instrument work and can be done at
any point — both will surface new findings, so doing them earlier means triaging more, but measuring truer.
