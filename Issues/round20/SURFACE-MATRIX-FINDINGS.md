<!-- SPDX-License-Identifier: GPL-2.0-only -->

# The surface matrix, first complete measurement

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
endpoints, as the `GenerateExtensions` rows already in `OptionGaps.StructurallyInapplicable`.

`AppliesTo` narrowed to `CreateMap | Registry | CoLocatedHost`. `Registry` is deliberately still claimed — the
registry *does* emit an extension class with a public/internal choice of its own, and ignores this option.
That is divergence D3, not a shape to declare away.

---

## Bucket 2 — Real divergences (165 cells, 18 findings) — NOT ratified

Nothing below has been added to `OptionGaps.KnownSilent` and nothing below had its `AppliesTo` narrowed. Each
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
