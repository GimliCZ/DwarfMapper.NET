# Surface-coverage architecture — design

**Status:** approved 2026-08-13. Supersedes the coverage half of `Issues/round19/` (ARCH-04, REG-01b, REG-05,
REG-06 fold into this; REG-02/03/04/07/08 and ARCH-01/02/03/05/06 are unaffected and remain as written).

## The property being enforced

> For every element of the shipped public surface, the case-space is **derived from its declaration**, every
> cell has an **executed** probe, and which cells are *claimed* is **declared next to the code**, verified in
> both directions.

## Why the current arrangement does not deliver it

`Contracts/OptionCatalog` → `OptionProbe` → `OptionEndpointParityTests` already implements exactly this
property — for **one** attribute (`[DwarfMapper]`, 18 writable properties × 7 endpoints). It reflects the
option set rather than listing it, computes non-default probes, and classifies each cell by *running the
generator twice and comparing output*. That is the target shape.

Every other public surface element is covered by **substring presence**:

| Gate | Asserts | Proves |
|---|---|---|
| `AssemblyScanTests.Scan4` | `"[MapProperty"` occurs in some test file | mention |
| `AssemblyScanTests.Scan5` | an enum member name occurs in some test file | mention |
| `OptionSurfaceCoverageTests` | the name occurs in some `samples/` file | mention |
| ARCH-04 (proposed) | the name occurs in some consumer assembly | mention |

None executes a line of behaviour, and the escape hatch is **six independent allowlist dictionaries**
(`NotDemonstrable`, `AttributeNotDemonstrable`, `DiagnosticTestAllowlist`, `MatrixExemptions`,
`OptionGaps.KnownSilent`, `OptionGaps.StructurallyInapplicable`) — each one "somebody typed a reason once",
which is the drift mode `OptionCatalog`'s own doc comment says it was built to eliminate.

Two things genuinely cannot be reflected, and both currently live test-side where they rot:

1. **What makes an element observable** (`OptionCatalog.TriggeringShapes` — "you need a nested class pair
   here"). Hand-kept dictionary, no gate forcing a new option to acquire an entry — an unprobed option reads
   as "not probed" and claims nothing.
2. **Which endpoints an element legitimately does not reach** (`OptionGaps.StructurallyInapplicable` — "an
   update has no `source.ToTarget()` form to suppress"). A semantic claim only the author can make.

## The architecture

### 1. `[DwarfSurface]` — the declaration-site contract

An `internal` attribute in `src/DwarfMapper`, read by the test projects through `[InternalsVisibleTo]` (the
idiom `src/DwarfMapper.Generator/AssemblyInfo.cs` already uses). **Zero public-API growth.**

```csharp
[DwarfSurface(SurfaceCategory.ConsumerDirective,
              AppliesTo = SurfaceEndpoints.CreateMap | SurfaceEndpoints.UpdateInto,
              ProbeKey  = "nested-pair")]
```

| Member | Replaces | Why it must live in `src` |
|---|---|---|
| `Category` — **required ctor arg** | `NotDemonstrable`, `AttributeNotDemonstrable`, ARCH-04 `Exclusions` | there is no parameterless ctor, so a surface cannot be declared unclassified |
| `AppliesTo` — flags, defaults to `All` | `OptionGaps.StructurallyInapplicable` | a semantic claim; the matrix then verifies it **in both directions** |
| `ProbeKey` — string | `OptionCatalog.TriggeringShapes` | names a fixture; the fixture text stays in tests, so no DTO blobs ship in the package |

`ProbeKey` is a **dynamic binding**: `src` states the demand, a `[SurfaceProbe("nested-pair")]`-marked fixture
class in the test project supplies it, and a bijection test fails on a missing fixture *or* an orphaned one.
Neither side can drift from the other.

`AppliesTo` defaulting to `All` is deliberate. Over-claiming fails (claimed ⇒ must be Honoured/Refused) and
under-claiming fails (unclaimed ⇒ must be Silent/NotCompilable), so a wrong default is *always* caught. There
is no value of `AppliesTo` that passes vacuously.

### 2. `SurfaceCategory` — obligations, not exemptions

The zero-allowlist lever. **There is no `Exempt` member.** Each category carries a *different* mandatory
proof obligation:

| Category | Mandatory proof |
|---|---|
| `ConsumerDirective` | executed cross-product (every cell Honoured / Refused / declared-inapplicable) **+** ≥1 consumer-assembly use **+** ≥1 runnable-sample use |
| `GeneratorEmitted` | *produced* by a generator run in a test **and** *consumed* by the reading side. Sample/consumer obligations do not apply — structurally, see §3 |
| `BuildFailureOnly` | a NegativeCases row pinning id **and** remedy wording, **plus** a positive row proving the non-failing path compiles |
| `EmissionShape` | a structural assertion on generated text (presence / absence / accessibility) at every claimed endpoint |
| `CrossAssembly` | exercised by a multi-assembly consumer fixture; single-assembly cells are inapplicable *by category* |
| `TestingOnly` | consumer-shaped use **+** a `DwarfMapper.Testing.Tests` contract row |

Today's `["ImplicitConversions"] = "false turns DWARF038 into a BUILD ERROR, so a sample could not compile"`
stops being a waiver and becomes `BuildFailureOnly` — which *demands a NegativeCases pin*. Every one of the
nine current exclusion rows redirects this way. Nothing gets cheaper; obligations get **redirected**.

### 3. `GeneratorEmitted` becomes a fact

New diagnostic **DWARF086**: hand-writing `[DwarfProvidesMap]` / `[DwarfRequiresMap]` in non-generated source
is refused. Their exemption from consumer/sample obligations is then *proved* rather than asserted.

The type names and namespace do **not** change — the diagnostic gives the same structural guarantee without a
rename.

### 4. The generalized matrix

`Contracts/` grows surface-level twins of the option-level machinery:

- **`SurfaceCatalog`** — reflects `[DwarfSurface]`-marked types and derives the case-space:
  `AttributeUsage.ValidOn` decomposed into individual targets × ctor overloads × property value domains ×
  `AllowMultiple`→a two-instance case × 7 endpoints.
- **`SurfaceProbe`** — `OptionProbe.Classify` generalized to a `SurfaceCase`, plus one new classification
  **`NotCompilable`**, asserted for sites `AttributeUsage` forbids. This pins the `AttributeUsage` declaration
  to reality; nothing does that today.
- **`SurfaceParityTests`** — the bidirectional property. A wrong `AppliesTo` fails from either side.

**Cost:** ≈940 cells, ≈1100 generator runs (≈7× the current option matrix). Two mitigations are load-bearing,
not optional:

- memoize the "off" baseline per `(endpoint, fixture)` — shared across every cell using it, roughly halves the
  runs;
- `[Trait("Category", "SurfaceMatrix")]` so it can be its own CI leg.

### 5. Enum domains: every member

`OptionCatalog.NonDefaultFor` picks `FirstOrDefault(v => !v.Equals(def))`. Complete for a 2-member enum,
silently skips members on 3+. Enumerate the full domain instead. This is where the option *enums* become
genuinely covered rather than textually referenced by Scan5.

### 6. Runtime API: mutation testing as the oracle

Stryker is configured for `DwarfMapper.Generator` and `DwarfMapper.DocTooling`. **`src/DwarfMapper` — the
shipped runtime assembly — is not mutated at all.**

Registry members, `MapConfig`, `IDwarfMapper` and the exception types have no derivable case-space the way an
attribute does. For those, a surviving mutant is the only non-textual proof that a case is untested. The
runtime assembly is small, so this is a fast leg. Break threshold ratchets upward only.

This is what makes round 19's H3 (`AmbiguousInterfaces` / `DestinationType`, zero references anywhere)
*impossible* rather than merely noticed.

## Category assignment

| Category | Types |
|---|---|
| `ConsumerDirective` | `DwarfMapper`, `DwarfMapperDefaults`, `AfterMap`, `BeforeMap`, `AutoNest`, `Flatten`, `FlattenGraph`, `GenerateMap<,>`, `GenerateWrapperMap`, `MapCollectionKey`, `MapDerivedType`, `MapDerivedType<,>`, `MapIgnore`, `MapIgnore<T>`, `MapIgnoreSource`, `MapNullSkip`, `MapNullSkip<,>`, `MapProperty`, `MapProperty<,>`, `MapTo`, `MapValue`, `MapValue<T>`, `MapConstructor<,>`, `DwarfMapperConstructor`, `Reinterpret`, `RestatesBase<,>`, `ReverseMap`, `ProvidesMap` |
| `EmissionShape` | `DwarfMapperOptions` |
| `GeneratorEmitted` | `DwarfProvidesMap`, `DwarfRequiresMap` |
| `CrossAssembly` | `UsesMap`, `UsesMap<,>` |
| `BuildFailureOnly` | `DwarfMapperValidationRoot` |
| `TestingOnly` | `RoundTrip` |

These are the starting assignments. **If an obligation fails when it runs, the correct response may be to
change the assignment** — that is the design working, not a defect in the table.

## What survives, stated plainly

`OptionGaps.KnownSilent` is renamed `DeclaredDivergences` and **stays**, at its current 2 entries. It is not
an exemption from testing: the cell *is* probed, and the test asserts the divergence still exists. It cannot
reach zero while `NullCollections@Projection` is an open design decision (three candidate resolutions are
recorded there and none has been chosen). Rules: one entry per *known defect*, each carrying an `Issues/`
link, count shrink-only.

## Scope boundary

This covers each surface element across all of **its own** cases. Attribute × attribute *interaction* is a
different axis, partly held today by `FeatureCombinationFuzzTests` and `FeatureInteractionCompileMatrixTests`.
It is deliberately **out of scope** — the cross-product does not cover it and should not be described as if
it does.

## Expected first-run cost

A matrix this size will find real divergences on its first green attempt; that is its purpose.
`DeclaredDivergences` will grow before it shrinks, and triaging those findings — not writing the code — is the
dominant cost of this work.
