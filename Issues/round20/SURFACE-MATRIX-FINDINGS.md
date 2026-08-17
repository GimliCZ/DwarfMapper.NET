<!-- SPDX-License-Identifier: GPL-2.0-only -->

# The surface matrix, first complete measurement

> **STATUS, 2026-08-16 — everything below the line is the FIRST measurement and several of its readings have
> since been superseded.** The case-space was enriched (task 5b), three instrument defects and one broken
> fixture baseline were fixed, and the matrix was re-measured. The current state, and the write-up every entry
> in `DeclaredDivergences.Reasons` links to, is **[the ratified findings](#ratified)** at the end of this
> document: **13 findings over 82 cells, plus 12 cells excused as structural — 94 red cells, all accounted
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
>
> **2026-08-17, third fix — 19 / 113 to 18 / 93.** `D20`: the co-located `[GenerateMap]` host now reads the
> member-placement `[MapProperty]` / `[MapIgnore]` its own `[DwarfSurfaceSite]` had always claimed it did,
> through the one parser the `[MapTo]` registry already used; the method forms written on a host member are
> refused as the new `DWARF089`. Measured, **2 cells went Honoured and 18 Refused** — but the twenty are
> three different things (**6** where the directive acts: the 2 Honoured plus **4** labelled Refused only by
> a pre-existing `DWARF038`; **6** refusals of a named argument the binding now reaches; **8** `DWARF089`),
> so this is the first finding on this branch that did **not** resolve to a single verdict. See
> [D20](#D20).
>
> **2026-08-17, fourth fix — 18 / 93 to 15 / 84.** Three findings, and — unlike every round before it — **two
> distinct root causes**, established by measurement rather than assumed from the shared symptom. `D1` and `D2`
> are one shape and are closed by generalizing the twice-written `DWARF077` check into a single element-wise
> gate reporting the new **`DWARF090`**: eight cells Silent → Refused, with the remedy the message names
> (`[MapIgnore<TTarget>]`, `[MapProperty<TSource, TTarget>]`) measured applying at those very endpoints —
> `Honoured` for the ignore, and for the rename `Refused` by the `DWARF038` conversion warning that only fires
> *because* the bind happened.
> `D16` is **not** propagation and needed its own fix, the new **`DWARF091`** — and chasing it down turned up
> the worst defect of the round: `[AfterMap]` on `void Update(Src, Dst)` was emitting `Update(s, d);` **inside
> `Update`**, shipped infinite recursion, which **the matrix had scored `Honoured`**. That grading failure is
> filed as [B19](TASKS.md); a green cell means the output changed, not that it is right. `D2`'s filed evidence
> was also wrong — `DWARF038` is `ImplicitConversionApplied`, not a refusal of `[MapProperty]` — and the
> correction is in its section. `NotCompilableCellCeiling` fell 107 → 99 as eight hook cells left the `CS8795`
> population for `Refused`. See [D1](#D1), [D2](#D2), [D16](#D16).
>
> **2026-08-17, fifth fix (A5) — 15 / 84 to 13 / 82.** `D17`, `D18` and `D19` shared one root cause —
> `MapToGenerator` read **no** assembly-level configuration at all — and one hoist closed **two** of the three.
> The lookup now lives once, in `Pipeline/AssemblyConfiguration`, and all three front doors call it: the class
> model for its defaults layer, the aggregate emitter for `PublicExtensions` (an inline copy until now), and
> the `[MapTo]` registry for both. `D19` is the trust boundary and is refused as the new **`DWARFR10`**; `D18`
> made the registry's extension class `internal` unless the assembly opts in, which is what the option's own
> documentation always said its default was — a **BREAKING** change, announced in `CHANGELOG.md`.
> `D17` did **not** close, and the reason its entry gave was measurably false: the `[MapTo]` front door emits
> an extension class and no ambient registry rows whatever, so `RegisterCollectionShapes = false` has nothing
> there to withhold. It stays recorded, with the measurement in place of the false justification and the three
> ways out set down for the maintainer. Re-measured across all 854 cells before and after: **exactly two rows
> differ**, both at `Registry` on the `Assembly` site. See [D17](#D17), [D18](#D18), [D19](#D19).

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

> **Amended 2026-08-17.** That last sentence was the *evidence for* [D20](#D20) and is no longer true of the
> co-located host: `MapperExtractor` now reads member symbols too, through the shared `MemberDirectives`
> parser — but **only the annotated host's own members, and only for pairs the host is the destination of.**
> The claim S1 makes is unchanged and was re-measured after that change: at the five mapper-declared
> endpoints nothing reads a DTO member, and all 100 cells are byte-identical.

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
D3/D4/D5/D21 below, and again after D20 was closed on 2026-08-17. **105 cells are red. Every one is
accounted for and the matrix is green:**

| Population | Cells | Findings | Where it is recorded |
| --- | ---: | ---: | --- |
| Recorded divergences | **93** | **18** | `DeclaredDivergences.Reasons`, re-measured every run |
| One option of an option bag, no surface at that endpoint | **12** | 2 options × 4 endpoints (+ the assembly-level twin) | `DeclaredDivergences.StructurallyInapplicable` |

**Nothing was narrowed, widened or excused to reach that.** The 93 are named cell by cell — element, generic
arity, axis, declaration site, endpoint — and `Every_declared_divergence_is_still_a_divergence` re-classifies
each one on every run. A row whose cell stops being silent turns the build **red** until the row is deleted.
That is the property that makes this a ratchet rather than an allowlist, and it is the reason recording a gap
is safe: a fix cannot leave a fossil behind.

Two further ratchets sit on the store itself. The **finding** count (18) fails if a new defect is written down
instead of fixed. The **cell** count (93) fails if an existing entry's cell list is widened — which is the
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

## The findings — 13 live, 10 fixed

Each has an anchor, because `DeclaredDivergences.Reasons` links to it. The ten marked **RESOLVED** keep their
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

### D1 — `[MapIgnore("Id")]` does not reach the element-wise endpoints — 4 cells — RESOLVED

> **RESOLVED 2026-08-17 as `DWARF090`, together with D2 — they were one shape.** Both are an **unscoped**
> member directive written on a mapping method or its class, at the two endpoints that resolve no members of
> their own: a span map and an async-stream map map the *element* pair through a mapper synthesized per
> `(source, target)` and **shared by every route that reaches that pair**, so it can only take configuration
> from directives that **name** the pair. An unscoped one belongs to the declaration it sits on and never
> arrives.
>
> **Closed by extending the existing mechanism, not by adding a second one.** The `DWARF077` explicit-only
> check was written **twice**, once in each element-wise branch — the shape in which the next directive gets
> added to one branch and forgotten in the other. It is now one gate, `ReportElementWiseDirectiveGaps`, that
> both branches call; `DWARF077` remains its blocking first clause and the two directives here are rows
> beneath it. The gate reads through `ReadIgnores` / `ReadExplicitMaps` — the same readers resolution uses —
> so it reports exactly what is dropped and nothing else, and inherits their malformed-argument hardening.
>
> **Refusal rather than propagation**, for the reason `DWARF077` already records: pushing one method's
> unscoped directive into a shared pair would silently re-configure a nested mapping **another** method owns.
> That is a worse defect than the silence and invisible from the declaration that caused it.
>
> What makes this a closure rather than a capability withdrawal is that the remedy the message names is
> **measured working at these very endpoints**. The pair-scoped twins are matched against every synthesized
> pair:
>
> | Case at SpanMap / AsyncStream | Before | After |
> |---|---|---|
> | `[MapIgnore<Dst>("Id")]` on the class | `Honoured` | `Honoured` (unchanged — this is the remedy) |
> | `[MapProperty<Src, Dst>("Id", "Name")]` on the class | applied | applied (unchanged) |
> | `[MapIgnore("Id")]`, method **and** class site | **Silent** | **Refused** |
> | `[MapProperty("Id", "Name")]` on a method, and ×2 | **Silent** | **Refused** |
>
> **Eight cells, Silent → Refused** (D1's four and D2's four). A **Warning**, and the severity is
> load-bearing rather than stylistic: a blocking error suppresses the class's emission, so the cell would land
> in the `NotCompilable` population behind `CS8795` — whose ceiling is shrink-only, so an Error would have
> *raised* a ratchet while appearing to close a finding. Case file:
> `DWARF090_DirectiveNotAppliedElementWise.cs`.

*Method and Class sites → SpanMap, AsyncStream.* Acts at CreateMap, UpdateInto, Projection (Honoured). One
mapper therefore drops the member on three of its overloads and copies it on two, from one declaration.

<a id="D2"></a>

### D2 — `[MapProperty("Id", "Name")]` on a method does not reach SpanMap/AsyncStream — 4 cells — RESOLVED

> **RESOLVED 2026-08-17 as `DWARF090`. See [D1](#D1) for the resolution — it is one fix for both.**
>
> **This finding's own evidence was wrong, and the correction matters more than the fix.** The sentence below
> says the directive is "refused with DWARF038" at CreateMap and UpdateInto, and that the finding is therefore
> *a missing diagnostic*. It is not. **`DWARF038` is `ImplicitConversionApplied`** — it has nothing to do with
> `[MapProperty]`'s placement. Measured message at CreateMap:
>
> > `DWARF038: Member 'Name': implicit parse/format (string↔T) conversion int → string? is applied …`
>
> The probe fixture's `Id` is an `int` and its `Name` is a `string?`, so binding one to the other *is* an
> implicit parse. The directive was **honoured** at CreateMap and UpdateInto all along; the warning is an
> artifact of the fixture's types. The cell *read* `Refused` only because `SurfaceProbe.Classify` tests for an
> added diagnostic **before** it compares output, so an honoured-and-warned cell is indistinguishable from a
> refused one.
>
> Two lessons, both cheap to state and both already paid for: **"Acts at" is not the same claim as "Refused
> at"**, and a finding whose evidence is a diagnostic id should name what that id *means*. Had this been read
> at filing time, D2 would have been written as D1 with a second attribute — which is what it turned out to
> be, and why one change closed both.

*Method site, `ctor(2)` and the ×2 case → SpanMap, AsyncStream.* Acts at CreateMap and UpdateInto, where it is
**refused with DWARF038**. The generator has an opinion about this directive and states it at three endpoints;
at the other two the identical text raises nothing and changes nothing.
*(The two sentences above are the original filing and the second is **false** — see the correction in the
resolution note. Kept, not edited, because the record of a misread is worth more than a tidy page.)*

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

### D6 — `[MapNullSkip(true)]` on a method — 3 cells → **1 cell, NARROWED**

> **PARTIALLY RESOLVED 2026-08-17, together with D7 — one root cause, three readers.** See
> [the joint resolution](#D6D7) immediately below D7. Two of this finding's three cells close as
> `Refused (DWARF090)`; the **Projection** cell stands, and stands for a reason that is a decision rather than
> plumbing.

*Method site → Projection.* Acts at CreateMap and UpdateInto (Honoured), refused element-wise (DWARF090).
Null-skipping decides whether a null source member overwrites the destination, and projection answers that
question differently from `.Map` on the same mapper without saying so.

<a id="D7"></a>

### D7 — `[MapNullSkip<Src, Dst>(true)]` on the class is D6 inverted — 6 cells → **2 cells, NARROWED**

*Class site, `ctor(1)` and ×2 → Projection.* Now acts at CreateMap, UpdateInto, SpanMap, AsyncStream and
CoLocatedHost. **"Pair-scoped attributes do not reach method-declared pairs" was never the explanation** —
`MapProperty<S,T>`, `MapValue<T>`, `MapIgnore<T>` and `MapConstructor<S,T>` all act at the method endpoints in
the same run — and the four cells resting on that claim are removed rather than re-argued.

<a id="D6D7"></a>

#### D6 + D7 — the joint resolution: three readers of one option

**Neither *form* was wrong.** Both `MapNullSkipAttribute` and `MapNullSkipAttribute<TSource, TTarget>` declare
`[DwarfSurface(…)]` with the default `AppliesTo = SurfaceEndpoints.All`, and `SurfaceEndpoints.All`'s own
documentation makes that a live claim in both directions rather than an omission ("over-claiming fails and
under-claiming fails too, so there is no value that passes vacuously"). Their XML docs describe one option
written at two scopes. So the intended endpoint set is the union — every endpoint that has a declaration site
for the form — and the declaration was telling the truth. The **implementation** had three partial readers of
one option:

| Reader | Where | What it saw |
|---|---|---|
| `ReadMapNullSkip(method) ?? classDefault` | `MapperExtractor.cs` update-into and create-map `ResolveMembers` calls | the method form and the class policy — **never** the pair-scoped form |
| `ResolvePairNullSkip(pairNullSkips, …)` | the `[GenerateMap]` pair loop and the auto-synthesized nested/element pair loop | the pair-scoped form and the class policy — and had no method to read |
| bare `skipNullSrc` | the projection endpoint's `ResolveProjectionMembers` call | **neither** scoped form |

That is why the two findings were the exact complement of each other, and why between them a caller reached
every endpoint while either alone reached about half — silently.

**Closed by making them one.** `ResolvePairNullSkip` is gone, folded into a single
`MapperExtractor.ResolveNullSkip(pairNullSkips, method, source, target, classDefault)` that every mapping-shape
front door now calls. Precedence is **most-specific-wins**: the method form, then the pair-scoped form, then the
mapper's `SkipNullSourceMembers`, then the assembly default. Contradictory values across the two forms became
reachable the moment both fed one resolution and nothing defined them before; this is the answer both
attributes' own documentation already implied, since the method form exists to *"carve one method out of a class
that enables it"* and carving out only works if it outranks what it is carving out of. Pinned in both
directions by `MapNullSkipScopeTests.A_method_form_outranks_a_contradicting_pair_form_in_both_directions`.

**The element-wise cells are a refusal, not propagation** — the D1/D2 precedent, for the reason `DWARF077`
records: the mapper synthesized for an element pair is shared by every route to that pair, so pushing one
method's directive into it would silently re-configure a mapping another method owns. `DWARF090` now covers
`[MapNullSkip]` alongside `[MapIgnore]` and `[MapProperty]`, and names `[MapNullSkip<Src, Dst>(…)]` as the
remedy — **with the value repeated**, because a bare remedy quoted back at a written `[MapNullSkip(false)]`
would invert the semantics the caller asked for. The remedy is measured `Honoured` at both endpoints, and
`DWARF090`'s "where it does act" sentence is now a parameter rather than a baked-in claim: the stock wording
says the unscoped form is honoured at projection, which is true of the two member directives and **false** of
this one.

| Case | Before | After |
|---|---|---|
| `[MapNullSkip<Src, Dst>(true)]` on the class @ CreateMap | **Silent** | **Honoured** |
| `[MapNullSkip<Src, Dst>(true)]` on the class @ UpdateInto | **Silent** | **Honoured** |
| ×2, same two endpoints | **Silent** | **Honoured** |
| `[MapNullSkip(true)]` on a method @ SpanMap | **Silent** | **Refused (DWARF090, Warning)** |
| `[MapNullSkip(true)]` on a method @ AsyncStream | **Silent** | **Refused (DWARF090, Warning)** |
| Both forms @ Projection (3 cells) | **Silent** | **Silent — still recorded** |

**Six cells close. `DivergentCellCeiling` 82 → 76**; `DivergenceFindingCeiling` stays at **13**, because both
findings survive with their Projection cell. Re-measured across every other population in the same run and all
five are byte-for-byte unchanged: `NotCompilable` 99, `UnhonouredButLoud` 14, `Unaskable` 44, `NoSuchSite` 137,
`StructurallyExcused` 12. Surface matrix 865/865 before and after.

**Why Projection did not close, measured rather than assumed.** It looks like it should fall out for free, and
the one-line change was made and then reverted. `ResolveProjectionMembers` *already* refuses an untranslatable
null-skip per affected member with `DWARF028` — the identical treatment
`[DwarfMapper(SkipNullSourceMembers = true)]` and its assembly twin already get at this endpoint (see the
generated option support matrix). Threading the resolved value in therefore produces the **right generator
behaviour** and makes the option uniform across all four of its scopes. But `DWARF028` is an **Error**, a
blocking error suppresses the class's emission, and the partial projection method is then unimplemented: all
three cells measured `NotCompilable (CS8795)`, not `Refused`. That is the instrument gap **G4/R4** recorded
earlier in this file, and it raises `NotCompilableCellCeiling` **99 → 102** — a ratchet raise, and a closure by relocating a cell into
the population the parity theory judges by nothing. Recorded instead.

What Projection waits on is a decision, not threading. *"Do not overwrite the destination's current value"* has
no referent inside an object initializer that **constructs** the destination: there is no prior value to keep.
Omitting the member unconditionally is not the same mapping either — a non-null source row must still be
assigned. The three candidate resolutions are (a) fix R4 so a blocking refusal reads `Refused`, then thread the
value; (b) give this one reason a non-blocking id, accepting that a projection then drops a member with only a
warning; (c) declare the option structurally inapplicable at Projection and say so in the docs. All three are
maintainer calls, and (a) retires most of an 99-cell population rather than three cells.

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

### D16 — `[AfterMap]` at SpanMap — 1 cell — RESOLVED

> **RESOLVED 2026-08-17 as `DWARF091`, and it was NOT the same fix as [D1](#D1)/[D2](#D2).** It was never
> directive propagation into the synthesized element mapper. `CollectHooks` scanned every method on the mapper
> class and accepted anything whose signature fitted, which includes the **partial mapping method declarations
> themselves** — methods whose bodies this generator writes, so the caller has no code there for a hook to run.
> The signature filter then decided the outcome by shape alone, and gave three different wrong answers to one
> mistake:
>
> | `[AfterMap]` written on | What happened |
> |---|---|
> | `Dst Map(Src s)` | `DWARF018` complained the hook was not `void` — a signature complaint about a signature that was never the problem |
> | `void MapSpan(ReadOnlySpan<Src>, Span<Dst>)` | fitted the two-parameter after-hook shape exactly, was registered, **never called** — the silent cell this finding filed |
> | `void Update(Src, Dst)` | fitted **and was called** |
>
> **The third row is the real finding, and this section did not contain it.** Measured generated body:
>
> ```csharp
> public partial void Update(global::Demo.Src s, global::Demo.Dst d)
> {
>     …
>     d.Tag = s.Tag;
>     Update(s, d);          // ← unconditional infinite recursion
> }
> ```
>
> It compiled, and nothing in the build said so. **The matrix scored that cell `Honoured`.** That is the first
> confidently *wrong positive* in this whole exercise, and it is filed as its own backlog item — see
> [B19](TASKS.md) — because the mechanism is general: `Honoured` asserts that the output **changed**, never that
> it is **right**.
>
> **The fix needs no special case for mapping methods.** C# erases a partial method with no implementing part
> along with every call to it, so as a hook it can only ever be a no-op; and where the generator supplies the
> missing part, the call re-enters the method being generated. Neither is what `[AfterMap]` means, at any
> endpoint — so the refusal sits **before** the signature check, and one diagnostic now covers all five
> endpoints instead of `DWARF018`'s signature complaint at three of them.
>
> **Cells moved:** SpanMap `Silent → Refused` (this finding's one cell). Two consequences beyond it, both
> deliberate and both re-measured: `[AfterMap]` at UpdateInto goes `Honoured → Refused` — correct, since the
> "honour" was the recursion above — and **eight cells go `NotCompilable` → `Refused`** (three `AfterMap`, five
> `BeforeMap`, previously stranded behind `CS8795` by `DWARF018`'s blocking error), which is why
> `NotCompilableCellCeiling` fell 107 → 99. A **Warning**, for the reason `DWARF090` is one. Case file:
> `DWARF091_HookOnMethodWithNoBody.cs`.

*Method site → SpanMap.* Honoured at UpdateInto; blocks the build at CreateMap, Projection and AsyncStream.
SpanMap alone compiles and never calls the hook, so a post-mapping fixup runs for every element of an async
stream and for none of a span.
*(The "honoured at UpdateInto" above is the original filing. It was recursion — see the resolution note.)*

<a id="D17"></a>

### D17 — `RegisterCollectionShapes = false` is dropped by the registry front door — 1 cell — STANDS, reason corrected

> **RE-MEASURED 2026-08-17 (A5). Not closed, and the stated reason below is wrong.** A5 closed D18 and D19 —
> which shared D17's root cause exactly, `MapToGenerator` reading no assembly-level configuration — and this
> cell did not come with them. The sentence "the `[MapTo]` registry front door, whose entire output **is**
> registry rows" is a pun on the word *registry*, and it is false. Measured: that front door emits an
> extension class and **nothing else** — no `[assembly: DwarfProvidesMap]`, no
> `DwarfMapperRegistry.Register` call, and so no collection-shape rows for this option to withhold. Only
> `DwarfGenerator`/`AggregateEmitter` emit ambient registration, and only for `[DwarfMapper]` classes and
> co-located hosts.
>
> So the cell is real (the option IS silent there) but it cannot be closed by plumbing. **Three ways out, and
> the choice is the maintainer's:**
>
> | Option | What it costs |
> | --- | --- |
> | **(a) Honour it** — give `[MapTo]` maps ambient registration, then gate the collection shapes on the option | A feature, not a fix: manifest emission, `DWARF061` (required-not-provided), `DWARF063` (ambiguous provider), and competition with a `[DwarfMapper]` class that maps the same pair. Arguably right — a front door named *registry* that is absent from the registry is its own oddity — but out of A5's scope. |
> | **(b) Refuse it** — a diagnostic at every `[MapTo]` type in an assembly that set the option | Noise. The assembly attribute is a house style set for the assembly's `[DwarfMapper]` classes; complaining about it at an unrelated type is the A7 mistake (a diagnostic about something the caller never mentioned). |
> | **(c) Reclassify structural** — a `StructurallyInapplicable` row saying there are no registry rows here | Honest as a statement, but it **raises `StructurallyExcusedCellCeiling` 12 → 13**, and no ratchet may be raised in this round without a deliberate decision. |
>
> A5 took none of the three. The row stays in `DeclaredDivergences.Reasons`, with the false justification
> replaced by the measurement, so nothing is closed by narrowing a claim or by moving a cell somewhere
> unjudged.

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

### D18 — `[assembly: DwarfMapperOptions(PublicExtensions = true)]` is ignored by the registry — 1 cell — RESOLVED

> **RESOLVED 2026-08-17 (A5).** The registry now reads the option through `AssemblyConfiguration`, the same
> reader the aggregate facade uses, so `__DwarfRegistry_<Source>` is **`internal` unless the assembly opts
> in** — which is what `PublicExtensions`' own XML documentation has always said its default was ("Defaults to
> false — all generated extensions are assembly-internal"). Cell re-measured `Silent` → **`Honoured`**.
>
> **This is a shipped-behaviour change, and the only closure available.** With the old public-by-default there
> is no value of the option that changes registry output, so the cell could not become `Honoured` without the
> flip. It is defensible on three counts: the emitted accessibility contradicted the option's documented
> contract; `[MapTo]` is a documented prototype/experimental tier; and no in-repo consumer relies on the
> public form (Conformance F18 and Gallery 16 are both in-assembly, and both still pass). The cost is real and
> announced in `CHANGELOG.md` under **Changed/BREAKING**: a library shipping `[MapTo]` types for another
> assembly to consume must now add `[assembly: DwarfMapperOptions(PublicExtensions = true)]`, since
> `source.MapTo<TTarget>()` is the only way to invoke a registry map — there is no mapper instance to fall
> back on. The opt-in remains a ceiling, not a decision: a pair involving a non-public type stays internal.
>
> Five `Snap_Golden_Registry*` snapshots and the golden manifest moved, each by exactly one line
> (`public static class` → `internal static class`), which is itself the evidence that the flip is scoped.

*Assembly site → Registry.* The registry emits an extension class (`__DwarfRegistry_Src`) with a
public/internal choice of its own and ignores this option, so an assembly default is honoured for
`[DwarfMapper]` classes and quietly overridden for `[MapTo]` types. **Registry is deliberately still claimed**
by the element after S2 narrowed the other four endpoints away as shapes — precisely because there IS an
extension here to decide about.

**Scope corrected:** the original entry paired this with `[DwarfMapperDefaults(SkipNullSourceMembers = true)]`
at Registry, which is no longer silent and is not part of the finding.

<a id="D19"></a>

### D19 — `[assembly: DwarfMapperDefaults(AutoMatchMembers = false)]` is dropped by the registry — 1 cell — RESOLVED

> **RESOLVED 2026-08-17 (A5).** The trust boundary now closes at the `[MapTo]` front door too. The registry
> asks the question with `MapperExtractor.ReadAutoMatchMembers` — the *same* reader the class model uses, not a
> second copy — against `AssemblyConfiguration.OptionsFor(compilation)`, and refuses the implicit by-name wire
> as the new **`DWARFR10`** (Error), the registry counterpart of `DWARF072`. Cell re-measured `Silent` →
> **`Refused (DWARFR10)`**.
>
> A separate id rather than a reuse of `DWARF072`, deliberately: that message names
> `[DwarfMapper(AutoMatchMembers = false)]` and there is no mapper class at this front door — the instruction
> arrives at the assembly and the fixes are written on the *source member*. Reusing the id would have handed
> the caller a message naming a construct their code does not contain.
>
> Three edges, each with its own test in `RegistryDiagnosticsGenTests` (the second of the three was **claimed
> here before it was written** — the claim was caught at review and the test now exists, verified to fail
> against a guard keyed on `Directives.Count == 0`):
>
> - **A destination the caller NAMED still maps.** `[MapProperty("Id")]` is a decision; a by-name match is a
>   coincidence. Without this the guard could be "refuse everything" and still look right.
> - **A `[MapProperty]` whose one argument names nothing** (`MemberDirectives.Name` is null for a non-constant
>   or empty argument) falls back to the member's own name — so it is an *implicit* match and is refused, not
>   credited to the attribute's mere presence.
> - **A member refused by `DWARFR10` does not also draw `DWARFR02`.** "Has no source member" would be false —
>   it has one, and declining to wire it is the point. Two diagnostics about one member, one of them untrue,
>   sends the reader to the wrong fix.
>
> Not propagated into `SynthNested`, mirroring `MapperExtractor`'s deliberate non-propagation into an
> auto-synthesized nested mapper: the boundary guards the pair the caller declared, and a synthesized helper
> has no member-level directives to satisfy it with.

*Assembly site → Registry.* Filed as **N3** by task 5b, and genuinely new: in neither the first measurement's
table nor the option store. `AutoMatchMembers = false` is a **trust boundary** — nothing is mapped unless the
caller said so. The mapper-level form acts at all five method endpoints and the assembly-level form is
honoured everywhere else; the `[MapTo]` registry drops it, so an assembly that has switched auto-matching off
still has every registry map auto-matching. Half a trust boundary is worse than none, because the developer
believes they have one. Same shape as the DWARF077 gap, one endpoint over.

<a id="D20"></a>

### D20 — the co-located host reads no member-level directive — 20 cells — RESOLVED

> **RESOLVED 2026-08-17.** The co-located path now reads the member forms off the host's own members, through
> the same `MemberDirectives` parser the `[MapTo]` registry uses; the method forms written there are refused
> as the new `DWARF089`. All twenty cells re-measured: by matrix verdict **2 Honoured and 18 Refused** —
> `Honoured` 148 → 150, `Refused` 175 → 193, `Silent` 248 → 228. `DivergenceFindingCeiling` 19 → **18**,
> `DivergentCellCeiling` 113 → **93**; no other ratchet moved, and none was raised. Matrix green at 865/865,
> whole solution 0 warnings / 0 errors.
>
> **This one did not resolve to a single verdict, unlike D3/D4/D5/D21.** Those four were one mistake with one
> answer (refuse). Here the two placements are two different things at the same site: the member form
> *should* act and now does, and the method form written on a member should not and now says so. The 18
> `Refused` are three unlike things, which is why the verdict alone is a poor summary of this finding:
>
> | What the cell now does | Cells | Verdict |
> | --- | ---: | --- |
> | the directive ACTS (a rename, a format, an exclusion) | **6** | 2 `Honoured`; 4 read `Refused` because a pre-existing `DWARF038` fires about the conversion the rename implies, and the probe checks diagnostics before output |
> | a named argument the binding now reaches is refused on its merits | **6** | `Refused` — `DWARF014` / `DWARF050` / `DWARF049` |
> | the method form written on a member | **8** | `Refused (DWARF089 (Warning))` |
>
> | Case, both sites | Before | After |
> | --- | --- | --- |
> | `[MapProperty("Id")]` | Silent | `Refused (DWARF038 (Warning))` — **honoured**; the warning is the pre-existing one about the implicit `int → string` conversion the rename now implies |
> | `[MapProperty("Id", StringFormat="probe")]` | Silent | `Refused (DWARF038 (Warning))` — **honoured**, emitting `ToString("probe", InvariantCulture)` |
> | `[MapProperty("Id", Use="probe")]` | Silent | `Refused (DWARF014)` — the named converter does not exist |
> | `[MapProperty("Id", When="probe")]` | Silent | `Refused (DWARF038 (Warning), DWARF050)` — the named predicate does not exist |
> | `[MapProperty("Id", NullSubstitute="probe")]` | Silent | `Refused (DWARF038 (Warning), DWARF049)` — a null substitute cannot ride a converter |
> | `[MapIgnore]` | Silent | `Honoured` — the member is no longer assigned |
> | `[MapProperty("Id","Name")]`, and ×2 | Silent | `Refused (DWARF089 (Warning))` — the method form on a member |
> | `[MapIgnore("Id")]` | Silent | `Refused (DWARF089 (Warning))` — the method/class form on a member |
> | `[MapIgnore]` ×2 | Silent | `Refused (DWARF089 (Warning))` — two directives, one declared pair |
>
> **Field and Property behave identically** — same verdict, same diagnostic ids, all ten shapes. This is the
> first finding measured after **B2** gave the `Field` site its own slot (gap G6), so it is the first place a
> field-only divergence *could* have shown up. There is none: one reader, one member-symbol loop, and
> `IPropertySymbol` / `IFieldSymbol` reach it on the same terms.
>
> **The boundary held, measured rather than assumed.** The whole 854-cell matrix was classified before and
> after: **exactly 20 rows differ, all at `CoLocatedHost`, 10 `Property` + 10 `Field`.** Every `Registry`
> cell and every cell at the five mapper endpoints is byte-identical. Populations: `Honoured` 148 → 150,
> `Refused` 175 → 193, `Silent` 248 → 228; `NoSuchSite` 137, `NotCompilable` 107, `Unasked` 25,
> `UnhonouredButLoud` 14 all unchanged. That boundary is the reason the fix is gated on
> `separateEmit && the pair's target IS the annotated class`: `[GenerateMap]` on a `[DwarfMapper]` class
> (mode 1) and the five mapper endpoints never reach the reader at all, because there the DTO pair is two
> ordinary types the consumer may not own.
>
> **`DWARF088` re-verified, not assumed.** A2's reviewer established that the paths did not collide because
> `ExtractGenerateMapHost` returns null for `[DwarfMapper]` classes; this change alters that path, so both
> directions were re-measured. `[MapIgnore]` at the **class** site of a co-located host still reports
> `DWARF088` alone, and the member sites report `DWARF089` alone — no double-report either way. The two
> checks cannot collide by construction: `ReportMemberFormDirectives` is called on the class symbol and on
> mapping-method symbols, never on a member symbol, and the new check reads member symbols only.
>
> **Known cosmetic wart, pre-existing and left alone:** `DWARF088`'s message calls a co-located host "this
> mapper class" when the bare `[MapIgnore]` is written on one. It came in with A2 and is not made worse here;
> fixing it silently inside this task would have moved a cell nobody was measuring.

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
