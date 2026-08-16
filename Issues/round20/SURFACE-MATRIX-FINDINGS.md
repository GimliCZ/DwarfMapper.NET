<!-- SPDX-License-Identifier: GPL-2.0-only -->

# The surface matrix, first complete measurement

> **STATUS, 2026-08-16 — everything below the line is the FIRST measurement and several of its readings have
> since been superseded.** The case-space was enriched (task 5b), three instrument defects and one broken
> fixture baseline were fixed, and the matrix was re-measured. The current state, and the write-up every entry
> in `DeclaredDivergences.Reasons` links to, is **[the ratified findings](#ratified)** at the end of this
> document: **19 findings over 113 cells, plus 12 cells excused as structural — 125 red cells, all accounted
> for, matrix green.** Read the first measurement for how the buckets were arrived at; read the amendment for
> what is true now. The single most severe item found in the whole exercise is **[N4](#N4)** and it is not a
> silent divergence at all — **fixed on 2026-08-16 as `DWARF087`**; see the resolution note in that section,
> including why the `NotCompilable` count did not move.
>
> **2026-08-16, second fix — the ratified count fell from 23 / 162 to 19 / 113.** `D3`, `D4`, `D5` and `D21`
> turned out to be one shape: a caller reaching for the wrong **overload** of a directive, and the build
> saying nothing. One arity check on each side of the library retired all four findings and forty-nine cells
> at once — `DWARFR04` at the `[MapTo]` registry (a descriptor that already existed and checked a *different*
> arity), `DWARF088` in the class model. Each section carries its resolution note.

`SurfaceParityTests` runs the executed cross-product: every `[DwarfSurface]`-declared element of category
`ConsumerDirective` or `EmissionShape`, at every declaration site its `AttributeUsage` permits, in every case
its constructors and writable properties admit, at all seven endpoints. **29 elements × 122 cases × 7
endpoints = 854 cells.** Each is classified by compiling the same source with and without the attribute and
comparing generated text and diagnostics, then checked in both directions against the element's own
`AppliesTo` claim.

Wall clock for the whole matrix: **~15 s** (856 tests). The baseline compile is memoized per
`(endpoint, fixture)`, which is what keeps it there.

## What the 854 cells measured

| Effect | Cells | Meaning |
| --- | ---: | --- |
| `Honoured` | 48 | changed the emitted output |
| `Refused` | 107 | produced a diagnostic the caller can see |
| `NotCompilable` | 99 | the C# compiler rejected the placement (or a blocking DWARF error left the partial unimplemented — see gap G4) |
| `NoSuchSite` | 137 | the endpoint has no such declaration site; no cell to judge |
| `UnhonouredButLoud` | 2 | changed nothing, but the build fails there anyway |
| `Silent` | 461 | **accepted, changed nothing, said nothing, compiled** |

Only the 461 silent cells can fail. They triage as:

| Bucket | Cells | Distinct findings | Action |
| --- | ---: | ---: | --- |
| **Under-reach** — unclaimed endpoint that is live | **0** | 0 | none needed (see note below) |
| **Structural** — the element cannot apply there | **104** | 2 | applied in this commit |
| **A real divergence** — should work, does not | **165** | 18 | **reported, not ratified** |
| **Instrument gap** — the probe never asked a question | **188** | 2 classes | reported; the cells stay red |

**353 of the 854 cells are red after the fixes in this commit** (165 divergence + 188 instrument gap), and
that is the intended end state of this task: 165 of them are findings awaiting a maintainer decision and 188
are cells the current case-space cannot ask a meaningful question about. Nothing was narrowed or allow-listed
to lower that number.

There are **zero under-reach findings** and that is not a surprise here: every element still carries the
default `AppliesTo = All`, so no endpoint was unclaimed for the first run to discover. The under-reach
direction becomes live the moment anything is narrowed, and it is what polices the narrowing itself — the one
narrow this commit makes (`DwarfMapperOptions`) is now guarded by it.

---

## Bucket 1 — Structural (applied)

### S1. Member-site `[MapProperty]` / `[MapIgnore]` at the five mapper-declared endpoints — 100 cells

`MapPropertyAttribute` and `MapIgnoreAttribute` are legal on `Property`/`Field`, and their own summaries say
that placement is *"(the `[MapTo]` registry)"* form. At `CreateMap`, `UpdateInto`, `Projection`, `SpanMap` and
`AsyncStream` the mapping is declared by a partial method on a separate `[DwarfMapper]` class; the DTOs are
ordinary types the consumer may not own, and their members are not part of that mapper's declaration. At
`Registry` the directive lives on the source type and at `CoLocatedHost` on the target type — there the
annotated DTO *is* the declaration, which is why those two are excluded from this rule.

Confirmed at source level as well: `MapperExtractor` reads these attributes off the class symbol or the method
symbol; only `Registry/MapToGenerator.cs` reads them off member symbols.

**Where it is declared, and why not in `AppliesTo`:** as
`SurfaceParityTests.MemberSiteIsNotADeclarationSite`, a per-`(element, site, endpoint)` predicate. `AppliesTo`
is per-**endpoint** and cannot express this: at `CreateMap`, `[MapProperty("Id", "Name")]` on the mapping
*method* is refused with DWARF038 while the identical text on a DTO member is silent. Dropping `CreateMap`
from the claim to satisfy the member cell breaks the method cell in the other direction. **No value of the
flags satisfies both**, so this is a genuine expressiveness gap in `[DwarfSurface]` — see recommendation R1.

### S2. `[DwarfMapperOptions(PublicExtensions = …)]` at update / projection / span / async — 4 cells

Its whole effect is the accessibility of the generated convenience extension, and that extension is
create-shaped (`source.ToTarget()`). An update mutates an instance it is handed, a projection emits an
expression tree, and the span and stream overloads are generated per *mapper* rather than per overload — none
of the four produces an extension whose accessibility there is to decide. Same reason, and the same four
endpoints, as the `GenerateExtensions` rows already in `DeclaredDivergences.StructurallyInapplicable`.

`AppliesTo` narrowed to `CreateMap | Registry | CoLocatedHost`. `Registry` is deliberately still claimed — the
registry *does* emit an extension class with a public/internal choice of its own, and ignores this option.
That is divergence [D18](#D18) — the first measurement mis-numbered it D3 — not a shape to declare away.

---

## Bucket 2 — Real divergences (165 cells, 18 findings) — NOT ratified

> **SUPERSEDED by [the ratified findings](#ratified).** The table below is the first measurement. Five of its
> rows (D9, D10, D11, D12, D14) rested on constructor arguments that named nothing real, D17's scope
> contradicted this document's own S2, three findings were not visible yet (D19, D20, D21), and the cell
> counts have all moved. Kept as the record of how the buckets were arrived at; do not cite its numbers.

Nothing below had been added to the divergence store and nothing below had its `AppliesTo` narrowed. Each
is a maintainer decision: fix the generator, refuse with a diagnostic, or record the gap.

The dominant shape is **the element-wise endpoints**. `SpanMap` and `AsyncStream` map the element pair through
an auto-synthesized mapper, and directives attached to the mapping method do not reach it — the same root
cause as the DWARF077 explicit-only finding, now visible across nine more attributes.

| # | Element / case | Acts at | Silent at | Cells |
| --- | --- | --- | --- | ---: |
| D1 | `[MapIgnore("Id")]` on a method **and** on the class | Create, Update, Projection (Honoured) | **SpanMap, AsyncStream** | 4 |
| D2 | `[MapProperty("Id","Name")]` on a method, and ×2 | Create, Update (DWARF038) | **SpanMap, AsyncStream** | 4 |
| D3 | `[MapProperty("Id", Use=/When=/NullSubstitute=/StringFormat=)]` on a method | the class-scoped `MapProperty<S,T>` form raises DWARF014 / DWARF049 / DWARF050 for the identical named arguments | **all five mapper endpoints** | 20 |
| D4 | `[MapProperty("Id","Name")]` on a DTO **member** at `Registry` | `RegistryDiagnostics.MapPropertyArity` exists for exactly this misuse | **Registry** (and CoLocatedHost) | 4 |
| D5 | `[MapIgnore]` / `[MapIgnore]×2` (no-target form) on a method or class | — the no-target form is the registry form; the class model has no arity check at all | all five, + CoLocatedHost | 22 |
| D6 | `[MapNullSkip(true)]` on a method | Create, Update (Honoured) | **Projection, SpanMap, AsyncStream** | 3 |
| D7 | `[MapNullSkip<Src,Dst>(true)]` on the class | SpanMap, AsyncStream, CoLocatedHost (Honoured) | **Create, Update, Projection** | 6 |
| D8 | `[MapDerivedType]`, both the open and the generic form | CreateMap | Update, Projection, SpanMap, AsyncStream | 16 |
| D9 | `[MapValue("Id", …)]`, all four cases | Create, Update (blocking) | Projection, SpanMap, AsyncStream | 12 |
| D10 | `[Flatten("Id")]`, and ×2 | Create, Update (blocking) | Projection, SpanMap, AsyncStream | 6 |
| D11 | `[FlattenGraph("Id","Name")]`, and ×2 | CreateMap (blocking) | Update, Projection, SpanMap, AsyncStream | 8 |
| D12 | `[Reinterpret("Id")]`, and ×2 | Create, Update (blocking), Projection (loud) | SpanMap, AsyncStream | 4 |
| D13 | `[ReverseMap]` | CreateMap (blocking) | Update, Projection, SpanMap, AsyncStream | 4 |
| D14 | `[MapCollectionKey("Id","Name")]`, and ×2 | UpdateInto (blocking) | Create, Projection, SpanMap, AsyncStream | 8 |
| D15 | `[GenerateWrapperMap(typeof(Dst))]` on the mapper class, and ×2 | CoLocatedHost (DWARF067) | all five mapper endpoints | 10 |
| D16 | `[AfterMap]` on the mapping method | UpdateInto (Honoured), Create/Projection/Async (blocking) | **SpanMap** | 1 |
| D17 | `[DwarfMapper(GenerateExtensions=false)]` and `(RegisterCollectionShapes=false)`; same two on `[DwarfMapperDefaults]` | CreateMap, CoLocatedHost (Honoured) | Update, Projection, SpanMap, AsyncStream, **Registry** | 13 |
| D18 | `[DwarfMapperDefaults(SkipNullSourceMembers=true)]` and `[DwarfMapperOptions(PublicExtensions=true)]` | Honoured at four and two endpoints respectively | **Registry** | 2 |

Notes on the two most consequential:

- **D3** is the sharpest of the set. `[MapProperty("Id", Use = "probe")]` on a mapping method compiles, names
  a converter that does not exist, and produces byte-identical output and no diagnostic at every one of the
  five mapper endpoints. Verified by hand as well as by the matrix. The single-argument constructor is the
  registry form, so at a method site the whole named-argument payload — a converter, a predicate, a null
  substitute, a format string — is discarded in silence. `MapProperty<S,T>` with the same named arguments
  raises DWARF014 / DWARF049 / DWARF050, so the diagnostics exist; this path never reaches them.
- **D7** inverts **D6**: the pair-scoped `MapNullSkip<S,T>` works at exactly the endpoints where the
  method-scoped `MapNullSkip` does not, and vice versa. The pair-scoped family's other members
  (`MapProperty<S,T>`, `MapValue<T>`, `MapIgnore<T>`, `MapConstructor<S,T>`) all act at the method endpoints
  per the same run, so "pair-scoped attributes do not reach method-declared pairs" is not the explanation.

---

## Bucket 3 — Instrument gaps (188 cells): the probe never asked a question

These cells are red and should stay red until the instrument improves. They are **not** divergences and must
not be recorded as such: the generator was never asked anything it could answer.

### G1. One `ProbeKey` per element, but option bags carry 15–19 independent options — 156 cells

`[DwarfMapper]` (19 cases), `[DwarfMapperDefaults]` (15) and `[DwarfMapperOptions]` (2) are option *bags*.
`[DwarfSurface]` binds one fixture per **element**, so all nineteen `[DwarfMapper]` options are probed against
the same flat `Src{int Id, string? Name} → Dst{int Id, string? Name}` pair, which cannot express an enum
(`EnumStrategy`, `EnumStringSource`), a case mismatch (`CaseInsensitive`, `NameConvention`), a non-public
member (`AllowNonPublic`), an `[Obsolete]` member (`IgnoreObsoleteMembers`), a narrowing conversion
(`ImplicitConversions`), a cycle (`OnCycle`, `ReferenceHandling`, `MaxDepth`), a collection
(`NullCollections`) or an unmapped member (`RequiredMapping`).

This surface **is** measured, with a per-option fixture each, by `OptionCatalog` + `OptionEndpointParityTests`.
The two matrices overlap here and only one of them can see. Also included: the `ctor(0)` case of each bag —
a bare `[DwarfMapper]` configures nothing, so it is silent by construction.

### G2. Constructor arguments are sampled, not chosen — 32 cells

`SurfaceCatalog.SampleArgument` renders `"Name"` / `"Id"` for strings and `true` for `bool`, with no knowledge
of the fixture or of the option's semantics:

- `[AutoNest(true)]` — `true` is the constructor's own default, so the case is a no-op by construction.
  Avoiding the constructor default is **not** a general fix: `AutoNestAttribute(bool enabled = true)` wants
  `false` to be interesting, while `MapNullSkipAttribute(bool enabled = true)` wants `true`. The principled
  fix is a full `bool` domain (both values), the same treatment `ValueDomain` already gives properties.
- `[MapIgnoreSource("Id")]` — the `unconsumed-source-member` fixture's unconsumed member is `Extra`; the probe
  names `Id`, which *is* consumed, so there is no warning to silence.
- `[MapCollectionKey("Id","Name")]` — the `nullable-collection-rebuild` fixture's element type is `int`, which
  has neither member.
- `[MapProperty("Id")]` at a method site — resolves to `Source == Target == "Id"`, i.e. the identity binding
  auto-matching already produces. Silence here is genuinely ambiguous between "honoured invisibly" and
  "discarded"; the case cannot distinguish them. (The *named-argument* variants built on the same constructor
  are not ambiguous — those are D3.)

### G3. Two instrument defects found and FIXED in this commit

Both were producing confident, wrong readings before the first triage pass:

- **The `[MapTo]` registry generator was never run.** `MapToGenerator` is a second `IIncrementalGenerator`, and
  `GeneratorTestHarness.RunAll` / `RunAndGetCompilationErrors` drove only `DwarfGenerator`. Every `[MapTo]`
  source therefore emitted *nothing*, and all 122 cells of the `Registry` column read `NoSuchSite` or `Silent`
  — a whole endpoint reported as measured and inert while the generator responsible for it sat idle. With both
  generators wired in, that column now shows 18 genuine `Refused` cells (DWARFR02/R03/R04/R08). This is the
  same instrument-not-generator confusion `RunAll`'s own doc comment warns about, one endpoint over.
- **`[DwarfMapper]` was applied twice.** `EndpointSources.Build` always emits `[DwarfMapper]` on the mapper
  class and then *appended* the class-site case beside it; `AllowMultiple` is false, so all 19 of that
  element's cases were CS0579 at all five mapper endpoints — 95 cells passing as `NotCompilable` without one
  of them ever being measured. The class-site case now substitutes for the template's own attribute.

### G4. `NotCompilable` swallows `Refused` — 80 cells, not fixed

`SurfaceProbe.Classify` tests for new C# errors *before* it reads generator diagnostics. A blocking DWARF error
leaves the partial mapping method unimplemented, which is CS8795 in the final compilation, so a refusal is
labelled `NotCompilable` — documented as "the site is illegal per `AttributeUsage` and the compiler rejects
it", which is not what happened. It changes no verdict today (both outcomes pass a claimed endpoint) but it
mislabels the matrix, and it has a real edge: on the **unclaimed** side, `NotCompilable` returns early, so
after any narrowing an under-reaching claim whose cell refuses loudly would pass unnoticed. Reordering the two
checks, or splitting CS8795 out from genuine placement errors, would fix it.

### G5. Sites with no shape in the endpoint set

`Struct` and `Constructor` return `NoSuchSite` for every endpoint, because no fixture declares either — so
`[MapTo]` on a struct (28 cells) and `[DwarfMapperConstructor]` (7 cells) are wholly unmeasured. Already
declared in `EndpointSources.BuildAt`. Separately, the 14-fixture member-slot gap counted by
`SurfaceProbeTests.Fixtures_without_a_member_slot_are_counted_not_silently_absent` still stands unchanged.

### G6. The `Field` site was measured against the `Property` slot — 70 cells, CLOSED

`EndpointSources.BuildAt` handled `AttributeTargets.Property or AttributeTargets.Field` in **one switch arm
that discarded `site`**, and every endpoint template declared only properties. For the two elements legal on
both — `MapProperty` and `MapIgnore` — **every Field cell produced byte-identical source to its Property
cell, at all seven endpoints**: 10 cases × 7 endpoints = **70 cells** that read as measured while measuring
the property code path under a field label. A field-only divergence was invisible by construction, and 10 of
those cells sit inside `D20`'s declared list, i.e. inside a ratcheted population.

**This is the third instance of one shape on this branch.** Task 4 found `Method` and `Property`/`Field`
producing identical source; G5 found sites claimed but unmeasurable for want of a template slot; this is the
same family one level down. The governing rule is unchanged: **an honest refusal to judge beats a confident
wrong answer** — `BuildAt` must never fall through to a slot other than the one named.

**Resolution.** The existing slot-marker mechanism was extended per SITE rather than replaced:
`MemberSlotMarker` became `PropertySlotMarker`, a `FieldSlotMarker` twin was added, and `SlotMarkerFor(site)`
is the single fact both `SiteAbsenceReason` and `BuildAt` consult. Every endpoint's DTO pair gained a real
field (`Tag`, declared on both sides so the baseline still maps completely), and the two endpoints that
declare their own pair — `Registry`, `CoLocatedHost` — now route their member sites through the same splice
as the other five instead of a special-cased `memberAttribute`. Where a fixture carries no marker for the site
under test, the answer stays `NoSuchSite` with a per-site cause, never a fall-through.

**Re-measured, whole matrix, before and after:**

| | Before | After |
| --- | ---: | ---: |
| Field-site cells whose source was byte-identical to their Property twin | **70** | **0** |
| Cells whose source text changed at all (the `Tag` member is in every template) | — | 502 |
| Cells whose VERDICT changed | — | **0** |
| `NoSuchSite` / `NotCompilable` / `Unasked` / `UnhonouredButLoud` | 137 / 107 / 25 / 14 | 137 / 107 / 25 / 14 |
| `Honoured` / `Refused` / `Silent` | 148 / 175 / 248 | 148 / 175 / 248 |

Not one verdict moved, so **no ratchet moved and none was raised**. That is the finding, not an absence of
one: the 70 cells were reading the right answer for the wrong reason. The seven `Registry` Field cells refuse
with the same `DWARFR02`/`R03`/`R04` ids now that the directive sits on `Src.Tag` — `MemberFacts.Readable`
enumerates fields alongside properties, so `MapToGenerator` genuinely sees them. The ten `CoLocatedHost` Field
cells are still `Silent`, because `MapperExtractor` reads these attributes off the class or method symbol
only. **`D20`'s entry therefore remains accurate** — and for the first time its Field rows rest on a Field
measurement rather than on a Property one wearing a Field label.

The guard that makes the class unrepeatable is
`SurfaceProbeTests.Property_and_Field_sites_are_not_measured_as_the_same_source`, the twin of the Method/Property
one task 4 added. It derives the "legal on both" element set from `AttributeUsage.ValidOn`, so a third element
becoming legal on both sites acquires the guard with no edit, and it accepts "both sites honestly decline"
because a cell that does not exist is not a cell measured under the wrong label.

---

## Recommendations

- **R1. `[DwarfSurface]` needs to be site-aware.** S1 proves `AppliesTo` cannot express a claim that differs by
  declaration site, and it is not a corner case — it covers the two most-used attributes on the surface. A
  second flags property (member-site endpoints) at the declaration would move the 100-cell predicate out of
  the test and back next to the code it describes, which is the architecture's whole premise.
- **R2. Give `ValueDomain` treatment to constructor parameters** (G2): both `bool` values, and a way for a
  fixture to supply the member names its shape actually contains, rather than `"Id"` / `"Name"`.
- **R3. Let an element bind a fixture per CASE, not per element** (G1) — or accept the overlap and record that
  the option bags are `OptionCatalog`'s to measure, not this matrix's.
- **R4. Fix G4's ordering** before the first `AppliesTo` narrowing wave, or the under-reach direction has a
  hole in it exactly where narrowing puts pressure on it.

## Reproducing

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj \
  -c Release --filter "Category=SurfaceMatrix"
```

---

<a id="ratified"></a>

# Amendment, 2026-08-16 — the ratified findings

The matrix was re-measured after task 5b enriched the case-space, fixed three instrument defects and repaired
one fixture whose broken baseline was swallowing its own verdict — and again after the arity fix recorded in
D3/D4/D5/D21 below. **125 cells are red. Every one is accounted for and the matrix is green:**

| Population | Cells | Findings | Where it is recorded |
| --- | ---: | ---: | --- |
| Recorded divergences | **113** | **19** | `DeclaredDivergences.Reasons`, re-measured every run |
| One option of an option bag, no surface at that endpoint | **12** | 2 options × 4 endpoints (+ the assembly-level twin) | `DeclaredDivergences.StructurallyInapplicable` |

**Nothing was narrowed, widened or excused to reach that.** The 113 are named cell by cell — element, generic
arity, axis, declaration site, endpoint — and `Every_declared_divergence_is_still_a_divergence` re-classifies
each one on every run. A row whose cell stops being silent turns the build **red** until the row is deleted.
That is the property that makes this a ratchet rather than an allowlist, and it is the reason recording a gap
is safe: a fix cannot leave a fossil behind.

Two further ratchets sit on the store itself. The **finding** count (19) fails if a new defect is written down
instead of fixed. The **cell** count (113) fails if an existing entry's cell list is widened — which is the
likelier mistake, because a fresh regression absorbed into an existing row would otherwise pass both the
parity theory and the still-a-divergence gate with nothing registering that the surface got worse.

## The severity order, before the list

<a id="N4"></a>

### N4 — the generator emits code that does not compile (CS1912) — **highest severity found**

Two identical `[FlattenGraph("Root", "Flat")]` directives on one mapping method make the generator emit

```csharp
new Dst { Flat = …, Flat = … }
```

read directly out of the failing compilation:

```
CS1912: Duplicate initialization of member 'Flat'
  @SourceFile(DwarfMapper.Generator\DwarfMapper.Generator.DwarfGenerator\Demo.M.g.cs[842..846))
```

**This is not a silent divergence and it is deliberately NOT in `DeclaredDivergences`.** That store holds
cells that compile, do nothing and say nothing; this one is the opposite failure — the generator accepted a
duplicate directive, resolved both to the same destination member, and produced invalid C#. It is louder than
every finding below and strictly worse: a caller who writes it cannot build at all, and the diagnostic they
get names generated code they did not write.

It is not unpinned. `SurfaceParityTests.The_cells_the_compiler_rejects_are_counted` holds the `NotCompilable`
population at 107 and **prints the CS id of every cell**, precisely so this one stays separable from the 96
`CS8795` cells that are the G4/R4 mislabel and the 8 `CS0111` / 2 `CS7036` cells that are honest placement
errors. The fix is a duplicate check in the `[FlattenGraph]` resolver, refusing with a DWARF diagnostic that
names the repeated destination — after which the cell becomes `Refused`, the `NotCompilable` ceiling drops to
106, and nothing here needs to change.

**Where it should live**, as a recommendation rather than a decision: an issue against the generator, plus a
pinning test in `NegativeCases` asserting the refusal once it exists. It does not want a store of its own —
one store per failure mode is how the six allowlists this architecture is replacing came about.

> ### RESOLVED, 2026-08-16 — round 20, task 1 — as `DWARF087`
>
> **Fixed.** `ResolveFlattenGraphDirectives` now refuses a second directive naming an already-claimed
> destination collection, with **`DWARF087` — "Duplicate [FlattenGraph] destination collection"** (Error).
> Refused rather than collapsed, matching `DWARF011` on `[MapProperty]`, which is the exact structural sibling
> (method site, `AllowMultiple`, two-string constructor, duplicate *destination*): a repeated directive is a
> copy-paste mistake, and quietly keeping one of the two hides it from the only person able to fix it.
>
> **The defect was wider than this section describes.** It is keyed on the destination collection, not on the
> two directives being character-identical — measured directly, `[FlattenGraph("Entry", "Nodes")]` beside
> `[FlattenGraph("Other", "Nodes")]` emits the very same `CS1912` from two directives that are not duplicates
> of each other at all. A check that only caught exact duplicates would have left half the defect class in
> place. Both shapes are refused; several directives naming *different* collections are untouched, which is
> what `[FlattenGraph]`'s `AllowMultiple = true` is for.
>
> **The prediction above about the matrix was wrong, and the correction is the interesting part.** This
> section expected the cell to become `Refused` and the `NotCompilable` ceiling to drop 107 → 106. It did not.
> Every DWARF **Error** in this generator suppresses the emission, so a refused mapper's partial method has no
> implementing part and the cell reports `CS8795` — it *joined* the G4/R4 population rather than leaving
> `NotCompilable`. Measured before and after:
>
> ```
> before:  96 CS8795 +  8 CS0111 + 2 CS7036 + 1 CS1912  = 107
> after:   97 CS8795 +  8 CS0111 + 2 CS7036 + 0 CS1912  = 107
> ```
>
> **No ceiling moved**, and none should have: the population is unchanged and only its composition changed.
> That is the correct end state rather than a shortfall — the cell is now indistinguishable from every other
> refusal in the library, which is precisely what "no longer a generated-code defect" means here. It becomes
> `Refused` when **R4** is fixed, along with the other 96; it is no longer a case R4 is hiding something worse
> behind.
>
> Pinned by `FlattenGraphGeneratorTests.FlattenGraph_duplicate_directive_is_refused_with_DWARF087` (and the
> non-identical variant), whose `AssertNoDuplicateInitialization` asserts `CS1912` specifically is gone rather
> than that the emission compiles — the latter is unachievable for any Error in this generator and would have
> been a test that could never pass. Message text and remedy wording are pinned by
> `tests/DwarfMapper.NegativeCases/Cases/DWARF087_DuplicateFlattenGraphTarget.cs`
> (`EXPECT: DWARF087, DWARF078` / `EXPECT-CS: CS8795` — the ordinary refusal cascade).
>
> No `DeclaredDivergences` entry was added or wanted: this was never a silence, and it is now a claimed
> endpoint behaving correctly.

## The findings — 19 live, 4 fixed

Each has an anchor, because `DeclaredDivergences.Reasons` links to it. The four marked **RESOLVED** keep their
sections: the store entry is gone (a fixed gap left on the list is a fossil), but the write-up is what the
next reader needs to know the gap existed and how it was closed. **Acts at** is the evidence the cell is
a divergence rather than a shape: the same directive, at the same site, doing something observable somewhere
else.

<a id="MaxDepth"></a>

### MaxDepth — `[DwarfMapper(MaxDepth = 1)]` at the element-wise endpoints — 2 cells

*Class site → SpanMap, AsyncStream.* Acts at CreateMap and UpdateInto. The element pair's depth guard comes
from the auto-synthesized mapper rather than the method model. The oldest entry in the store, and the first
time the SURFACE matrix sees it — it was found by the option matrix once a recursive fixture existed. Full
prose, including why its severity is lower than it sounds, is in the store.

<a id="NullCollections"></a>

### NullCollections — `NullCollections = AsNull` at Projection — 2 cells

*`[DwarfMapper]` class site and `[assembly: DwarfMapperDefaults]` → Projection.* The one entry here that is a
**design decision rather than an oversight**: a refusal was implemented and reverted because it amounted to
"you cannot project a nullable collection under default options". Three candidate resolutions, and the
argument against each, are recorded verbatim in the store — that text is the most valuable in the file and is
carried unchanged.

<a id="D1"></a>

### D1 — `[MapIgnore("Id")]` does not reach the element-wise endpoints — 4 cells

*Method and Class sites → SpanMap, AsyncStream.* Acts at CreateMap, UpdateInto, Projection (Honoured). One
mapper therefore drops the member on three of its overloads and copies it on two, from one declaration.

<a id="D2"></a>

### D2 — `[MapProperty("Id", "Name")]` on a method: the refusal does not reach SpanMap/AsyncStream — 4 cells

*Method site, `ctor(2)` and the ×2 case → SpanMap, AsyncStream.* Acts at CreateMap and UpdateInto, where it is
**refused with DWARF038**. The generator has an opinion about this directive and states it at three endpoints;
at the other two the identical text raises nothing and changes nothing.

<a id="D3"></a>

### D3 — `[MapProperty]`'s named arguments are discarded at every method endpoint — 20 cells — RESOLVED

> **RESOLVED 2026-08-16 as `DWARF088`, and it was the same defect as D21.** The reading that closed it: the
> named arguments ride on the **one-argument constructor** — `[MapProperty("Id", Use = "probe")]` is `ctor(1)`
> plus a property initializer, not a second overload. So this was never "the payload is dropped"; it was
> "the whole directive is dropped, payload included", because `ReadExplicitMaps` accepts only the two-argument
> application. Writing the method form (`[MapProperty("Id", "Id", Use = "probe")]`) reaches DWARF014 exactly as
> this section predicted it should — that path was always live and the wrong overload never entered it.
>
> Refused rather than honoured, which was a real fork: `[MapProperty("Id", Use = "F")]` at a method has a
> *sensible* reading (bind Id to itself, convert with F), unlike D21's bare form. It is refused anyway, so the
> one rule is "the member-placement overload is not the method-placement overload" rather than a rule that
> looks away when named arguments are present. The caller is told what to write and their converter then runs;
> a maintainer who later prefers to honour it deletes a check. Twenty cells, Silent → Refused.
>
> **This finding was not in task 2's brief.** It fell out of the check written for D21 and was found by
> `Every_declared_divergence_is_still_a_divergence` turning red, which is that gate working as designed.

*Method site, `Use` / `When` / `NullSubstitute` / `StringFormat` → all five mapper endpoints.* The sharpest
finding on the surface. `[MapProperty("Id", Use = "probe")]` names a converter that does not exist and
produces byte-identical output and no diagnostic at all five. Verified by hand as well as by the matrix.

**Why it is a divergence and not a shape:** the pair-scoped `MapProperty<S,T>` form raises **DWARF014 /
DWARF049 / DWARF050** for these exact named arguments. The refusals exist; this path never reaches them. The
single-argument constructor is the registry form, so at a method site the whole named-argument payload — a
converter, a predicate, a null substitute, a format string — is dropped in silence.

<a id="D4"></a>

### D4 — `RegistryDiagnostics.MapPropertyArity` does not fire — 2 cells — RESOLVED

> **RESOLVED 2026-08-16. No new id was needed, and the diagnosis in the paragraph below was wrong in a way
> worth recording.** The descriptor is not unreachable and not dead: `MapPropertyArity` (`DWARFR04`) is
> reported from `MapToGenerator.cs` and has had a triggering test since it was written
> (`RegistryDiagnosticsGenTests.MapProperty_arity_mismatch_reports_DWARFR04`). It checks a **different
> arity** — how many `[MapProperty]` attributes are *stacked* on a member versus how many `[MapTo]` targets
> the type declares. The arity this cell gets wrong is how many values **one** attribute carries, and that one
> was checked nowhere.
>
> Where it actually went: `ParseDirectives` reads a destination name only off a one-argument application
> (`a.ConstructorArguments.Length == 1 ? … : null`), so the two-name method form arrived as a directive naming
> nothing, and the member fell back to binding its **own** name a few lines later. In the matrix's fixture
> that fallback happens to satisfy the destination, which is exactly why the cell read Silent rather than
> `DWARFR02`.
>
> The fix is a second branch reporting the **same** `DWARFR04` — its message ("must have either one value (all
> targets) or exactly one value per `[MapTo]` target") is already the right sentence for one attribute
> carrying two values. Two cells, Silent → Refused, and `DWARFR04` stays an **Error** because the registry
> emits free-standing extension methods and has no partial declaration to strand in `CS8795`.

*Property and Field sites → Registry.* The two-argument `[MapProperty("Id", "Name")]` is the METHOD form; on a
source member at the `[MapTo]` registry the form takes one argument. **The descriptor for exactly this misuse
exists** (`Registry/RegistryDiagnostics.cs`, raised from `MapToGenerator.cs:97`) and measured, it does not
fire. A caller who used the wrong overload gets a binding that does nothing and a build that says nothing.

<a id="D5"></a>

### D5 — the no-target `[MapIgnore]` is accepted at a method or class site — 22 cells — RESOLVED

> **RESOLVED 2026-08-16 as `DWARF088`, by the same single check that closed D3 and D21.** One check, three
> findings: `ReportMemberFormDirectives` runs once over the mapper class symbol and once per partial mapping
> method, and reports the member-placement overload of either attribute — `[MapProperty]` with one argument,
> `[MapIgnore]` with none. Two checks were not needed and would have been wrong: the mistake is the same one,
> and a per-attribute split would have produced two ids saying one sentence.
>
> The two SITES did need two call sites, and that is the part the finding names correctly — `classIgnores` and
> the per-method read are separate paths, and a check on either alone would have left the other silent. That
> is the shape the matrix found it in. Twenty-two cells, Silent → Refused, including both `CoLocatedHost`
> cells: the co-located host is extracted by the same `MapperExtractor.Extract`, so the class-site check
> reaches it for free.
>
> A **Warning**, unlike its registry mirror `DWARFR04`. Measured, not stylistic: every blocking DwarfMapper
> error suppresses the whole class's emission, so an Error here would have moved these cells into the
> `CS8795` / `NotCompilable` population — the G4/R4 ordering defect — instead of out of the divergence store.
> The refusal would have been correct and invisible.

*Method site → all five mapper endpoints; Class site → those five plus CoLocatedHost; single and ×2 forms.*
The no-target form is the REGISTRY form: the annotated member is the thing ignored. On a method or a class it
names nothing at all. **The class model performs no arity check**, so a caller who believes they have excluded
a member has excluded nothing. The mirror misuse at the registry has a descriptor (D4), which is what makes
the absence here a gap rather than a shape.

<a id="D6"></a>

### D6 — `[MapNullSkip(true)]` on a method — 3 cells

*Method site → Projection, SpanMap, AsyncStream.* Acts at CreateMap and UpdateInto (Honoured). Null-skipping
decides whether a null source member overwrites the destination, so the caller gets one behaviour on two
overloads and its opposite on three. Its pair-scoped twin proves the endpoints are reachable — see D7.

<a id="D7"></a>

### D7 — `[MapNullSkip<Src, Dst>(true)]` on the class is D6 inverted — 6 cells

*Class site, `ctor(1)` and ×2 → CreateMap, UpdateInto, Projection.* Acts at SpanMap, AsyncStream and
CoLocatedHost. Exactly the complement of D6: between the two forms a caller can reach every endpoint, and with
either alone reaches roughly half, silently. **"Pair-scoped attributes do not reach method-declared pairs" is
not the explanation** — `MapProperty<S,T>`, `MapValue<T>`, `MapIgnore<T>` and `MapConstructor<S,T>` all act at
the method endpoints in the same run.

<a id="D8"></a>

### D8 — `[MapDerivedType]`, both forms, act only at CreateMap — 16 cells

*Method site, open form `ctor(2)`/×2 and generic form `ctor(0)`/×2 → UpdateInto, Projection, SpanMap,
AsyncStream.* Acts at CreateMap. A derived-type declaration is made for the mapper, not for one overload of
it; on the other four the derived instance is mapped as its base and the extra members are dropped.

<a id="D9"></a>

### D9 — `[MapValue]` does not reach projection or the element-wise endpoints — 12 cells

*Method site, `ctor(1)` / `ctor(2)` / `Use` / ×2 → Projection, SpanMap, AsyncStream.* Acts at CreateMap and
UpdateInto (Honoured).

**Evidence re-derived.** The first measurement filed this as "Create, Update (blocking)". That reading was a
refusal of nonsense: the sampled argument assigned a string constant to an `int`. With an argument naming a
real member the directive is genuinely honoured at Create/Update, so the three silences are a divergence
rather than an artefact of a broken case.

<a id="D10"></a>

### D10 — `[Flatten]` does not reach projection or the element-wise endpoints — 6 cells

*Method site, `ctor(1)` and ×2 → Projection, SpanMap, AsyncStream.* Acts at CreateMap and UpdateInto
(Honoured). The flattened destination members are left at their defaults at the other three.

**Evidence re-derived** for the same reason as D9: the original argument named a scalar member, so
"Create, Update (blocking)" was a refusal, not an honouring. `[Flatten("Child")]` against a nested fixture is
honoured.

<a id="D11"></a>

### D11 — `[FlattenGraph]` acts only at CreateMap — 8 cells

*Method site, `ctor(2)` and ×2 → UpdateInto, Projection, SpanMap, AsyncStream.* Acts at CreateMap (Honoured).

**This finding was proposed for WITHDRAWAL and the withdrawal was wrong.** The
`graph-navigation-to-flat-collection` fixture gave `Dst` a collection with no source counterpart, so the
fixture's own **baseline** was `DWARF001` and emitted nothing at all. With an empty baseline, an element that
does nothing produces byte-identical (empty) output beside an error and reads `UnhonouredButLoud` — which
passes on both claim branches and decides nothing. The general rule, now written into the fixture's own
comment: **a fixture that cannot compile without the element under test can never show that element doing
nothing.**

With `Src` given the matching member so the baseline compiles, all eight cells were re-measured directly:

```
ctor(2)  @CreateMap     Honoured   (output differs)
ctor(2)  @UpdateInto    Silent     (identical, compiles)
ctor(2)  @Projection    Silent
ctor(2)  @SpanMap       Silent
ctor(2)  @AsyncStream   Silent
x2       @CreateMap     NotCompilable (CS1912)  <- N4, NOT part of this finding
x2       @UpdateInto/Projection/SpanMap/AsyncStream  Silent
```

The `×2` case at CreateMap is **[N4](#N4)**, a defect in the generated output, and is excluded from this
finding's cells on purpose.

> **2026-08-16:** N4 is [fixed](#N4) as `DWARF087`. That cell still reads `NotCompilable`, now with `CS8795`
> rather than `CS1912` — the ordinary refusal cascade, i.e. G4/R4 and no longer a defect of its own. Its
> exclusion from this finding's cells is unchanged.

<a id="D12"></a>

### D12 — `[Reinterpret]` does not reach the element-wise endpoints — 4 cells

*Method site, `ctor(1)` and ×2 → SpanMap, AsyncStream.* Acts at CreateMap, UpdateInto and Projection. These
are the two endpoints whose entire purpose is bulk element throughput, and therefore the two where a caller
reaching for a forced blit most expects it to apply.

**Evidence re-derived:** the original argument pointed the directive at a scalar. Against an unmanaged
array pair (`int[]` → `uint[]` — same width, both unmanaged, different types, exactly the pair the automatic
layout proof declines) it acts at three endpoints and the two silences stand.

<a id="D13"></a>

### D13 — `[ReverseMap]` acts only at CreateMap — 4 cells

*Method site → UpdateInto, Projection, SpanMap, AsyncStream.* A caller who wrote it on an update or a
projection gets no inverse and no explanation, and discovers the absence at the call site of a method that was
never generated.

<a id="D14"></a>

### D14 — `[MapCollectionKey]` acts only at UpdateInto — 8 cells

*Method site, `ctor(2)` and ×2 → CreateMap, Projection, SpanMap, AsyncStream.* Acts at UpdateInto, the
endpoint the directive is chiefly for.

**Evidence re-derived:** the original argument named members of an `int` element type, which has none, so the
directive could only ever apply to nothing. Against the `keyed-collection-elements` fixture
(`List<Item>` → `List<ItemDto>` with `Id`/`Label` on the element) it acts at UpdateInto and the four silences
stand.

<a id="D15"></a>

### D15 — `[GenerateWrapperMap]` on a `[DwarfMapper]` class does nothing and says nothing — 10 cells

*Class site, `ctor(1)` and ×2 → all five mapper endpoints.* **Refused at CoLocatedHost with DWARF067**, so the
generator reads the attribute and has an opinion about where it is valid. On a mapper class it produces
neither the wrapper map the caller asked for nor the refusal the co-located host would have given them.
Whichever of the two answers is right, silence is not it.

<a id="D16"></a>

### D16 — `[AfterMap]` at SpanMap — 1 cell

*Method site → SpanMap.* Honoured at UpdateInto; blocks the build at CreateMap, Projection and AsyncStream.
SpanMap alone compiles and never calls the hook, so a post-mapping fixup runs for every element of an async
stream and for none of a span.

<a id="D17"></a>

### D17 — `RegisterCollectionShapes = false` is dropped by the registry front door — 1 cell

*`[assembly: DwarfMapperDefaults]` → Registry.*

**Scope corrected.** The first measurement filed D17 at 13 cells across five endpoints and two attributes,
which **contradicted this document's own S2**: the option's silence at UpdateInto, Projection, SpanMap and
AsyncStream is not a divergence at all — none of those four produces a registerable delegate, and
`StructurallyInapplicable` has said so, with a per-endpoint reason, since before this matrix existed. Those
cells are excused as structural (see below). The `GenerateExtensions` half of the original D17 is the same
story.

What remains is the sharp cell and the only one the original entry got right: the `[MapTo]` registry front
door, whose entire output **is** registry rows, ignores the assembly-level instruction to withhold them.

<a id="D18"></a>

### D18 — `[assembly: DwarfMapperOptions(PublicExtensions = true)]` is ignored by the registry — 1 cell

*Assembly site → Registry.* The registry emits an extension class (`__DwarfRegistry_Src`) with a
public/internal choice of its own and ignores this option, so an assembly default is honoured for
`[DwarfMapper]` classes and quietly overridden for `[MapTo]` types. **Registry is deliberately still claimed**
by the element after S2 narrowed the other four endpoints away as shapes — precisely because there IS an
extension here to decide about.

**Scope corrected:** the original entry paired this with `[DwarfMapperDefaults(SkipNullSourceMembers = true)]`
at Registry, which is no longer silent and is not part of the finding.

<a id="D19"></a>

### D19 — `[assembly: DwarfMapperDefaults(AutoMatchMembers = false)]` is dropped by the registry — 1 cell

*Assembly site → Registry.* Filed as **N3** by task 5b, and genuinely new: in neither the first measurement's
table nor the option store. `AutoMatchMembers = false` is a **trust boundary** — nothing is mapped unless the
caller said so. The mapper-level form acts at all five method endpoints and the assembly-level form is
honoured everywhere else; the `[MapTo]` registry drops it, so an assembly that has switched auto-matching off
still has every registry map auto-matching. Half a trust boundary is worse than none, because the developer
believes they have one. Same shape as the DWARF077 gap, one endpoint over.

<a id="D20"></a>

### D20 — the co-located host reads no member-level directive — 20 cells

*Property and Field sites → CoLocatedHost. Every `[MapProperty]` case (7) and every `[MapIgnore]` case (3), on
both sites.*

At the co-located host **the mapping is declared BY the annotated type** — `[GenerateMap<Src, Dst>]` sits on
`Dst` — so a member of that type is part of the declaration. That is exactly why `MapPropertyAttribute`'s own
`[DwarfSurfaceSite]` keeps `CoLocatedHost` claimed for the member sites while dropping the five mapper
endpoints, where the DTOs are ordinary types the consumer may not own. The claim is right and the generator
does not honour it.

**One root cause, therefore one fix:** `[GenerateMap<S,T>]` is extracted by `MapperExtractor`, which reads
these attributes off the class or the method symbol only; `MapToGenerator`'s registry path is the sole reader
of the member-level forms.

<a id="D21"></a>

### D21 — the registry-form `[MapProperty("Id")]` is accepted at a method site — 5 cells — RESOLVED

> **RESOLVED 2026-08-16 as `DWARF088`.** Refused, as this section argued it had to be, and the prediction
> underneath it held exactly: closure is observable **only** as a refusal, and the five cells went
> Silent → Refused with the generated text unchanged. The rejected `AutoMatchMembers = false` route was not
> retried and did not need to be.
>
> The measured verdict is `Refused (DWARF088 (Warning))` rather than `Refused (DWARF088)` because the id is a
> Warning — see D5's note for why an Error would have hidden the refusal under `CS8795` instead of closing the
> cells.

*Method site, `ctor(1)` → all five mapper endpoints.*

The one-argument overload is the MEMBER-placement form, as its own summary states; the documented method form
takes two arguments. Written on a mapping method it resolves to `Source == Target`, which is the identity
binding auto-matching already produces.

**Why this is a defect whichever way the generator reads it** — and this matters, because task 5b named the
cell as genuinely ambiguous between "honoured invisibly" and "discarded": if the directive is discarded, a
caller's explicit binding evaporated; if it is honoured, it was honoured as a no-op the caller cannot have
wanted. **Refusal is the right answer either way**, and the class model has no arity check to give it — the
same missing check as D5, and the mirror of the one the registry has and does not fire (D4). Closure is
observable only as a refusal, since honouring it is byte-identical by construction.

**Rejected route, recorded so it is not retried:** giving this case
`[DwarfSurfaceProbe(MapperOptions = "AutoMatchMembers = false")]` would make honouring visible, but it
breaches two shrink-only ceilings — the member-site cells at CoLocatedHost go `Unasked`, because that template
carries no mapper class to hold the options, and the Create/Update baselines stop compiling and land in
`UnhonouredButLoud`, which is the verdict-swallowing trap D11 had to be rescued from.

## The 12 cells excused as structural, and why they are not in the store

| Option | Written at | Endpoints | Cells |
| --- | --- | --- | ---: |
| `GenerateExtensions` | `[DwarfMapper]` class | UpdateInto, Projection, SpanMap, AsyncStream | 4 |
| `RegisterCollectionShapes` | `[DwarfMapper]` class | the same four | 4 |
| `RegisterCollectionShapes` | `[assembly: DwarfMapperDefaults]` | the same four | 4 |

Each has a per-endpoint reason already written and already consumed by the option matrix — "an update has no
`source.ToTarget()` form to suppress", "`Span<T>` is a ref struct and cannot be boxed through the registry's
`Func<object, object>`", and so on. Recording them as divergences would be a false defect report.

**They cannot be expressed as an `AppliesTo` narrowing, and that is finding R3 in concrete form.** `AppliesTo`
is per ELEMENT; `[DwarfMapper]` carries nineteen independent options. No value of the flags can say
"`GenerateExtensions` has no surface at UpdateInto" without saying it about `EnumStrategy` too. So the surface
matrix reads the claim from `StructurallyInapplicable`, which is keyed by option and endpoint with no element
— the reason is about the endpoint's shape and holds wherever the option was written, which is also why the
assembly-level twin resolves through the same rows.

**This excuse is the only one in the matrix that is not self-retiring**, and it is counted for exactly that
reason. A `Reasons` row fails the moment its cell starts working; a structural row cannot, because "there is
nothing here to configure" and "it is configured correctly" are both non-failures. If UpdateInto ever grew a
convenience extension the cell would flip Silent → Honoured and the entry would sit there unnoticed.
`The_cells_excused_as_structural_are_counted` holds the population at 12, shrink-only, printing every cell.

## What is NOT here, and where it is instead

| | Cells | Counted by |
| --- | ---: | --- |
| ~~**N4** — generated code that does not compile~~ **FIXED** (`DWARF087`) | 0 | `The_cells_the_compiler_rejects_are_counted` (prints CS ids) — the `CS1912` line is gone |
| **G4/R4** — `NotCompilable` swallowing `Refused` (CS8795) | 97 | the same fact; the ordering defect is still open. **96 → 97**: N4's cell joined this population rather than leaving `NotCompilable`, because a refused mapper emits nothing and its partial method is unimplemented. Total still 107, so no ceiling moved |
| **G5** — `[MapTo]`@`Struct`, `[DwarfMapperConstructor]`@`Constructor`, no fixture declares either | 21 | `The_cells_with_no_declaration_site_are_counted_by_cause` |
| ~~**G6** — the `Field` site measured against the `Property` slot~~ **CLOSED** | 0 (was 70) | `Property_and_Field_sites_are_not_measured_as_the_same_source`. No verdict changed and no ratchet moved: the 70 cells were reading the right answer for the wrong reason |
| Cells the instrument poses no question about | 44 | `The_cells_that_pose_no_question_are_declared_and_counted` |
| Cells passing both claim branches | 14 | `The_cells_that_pass_both_claim_branches_are_counted` |

None of these is a divergence and none belongs in the store. G5 and G4/R4 are **our** gaps, not the product's;
the unaskable and both-branch populations are questions never asked. Recording any of them as a divergence
would ratify a bug the generator never committed.

## Reproducing the current state

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj \
  -c Release --filter "Category=SurfaceMatrix"
# 865 passed, 0 failed  (854 cells + 11 facts)
```
