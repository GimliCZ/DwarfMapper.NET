<!-- SPDX-License-Identifier: GPL-2.0-only -->
# DwarfMapper vs. Mapperly / Mapster / AutoMapper

A capability, testing, performance, and **migration-ease** comparison against the three most common
.NET mappers. Benchmarks live in [`benchmarks/DwarfMapper.Benchmarks`](../benchmarks/DwarfMapper.Benchmarks/).

> **The real differentiator is correctness, not speed** (the source-generator peers tie us on throughput).
> See [`CORRECTNESS.md`](CORRECTNESS.md): completeness is a build error (a **DTO-drift contract gate**), the
> resolved mapping is documented on every method, round-trips are verified, codegen is provably
> deterministic + incrementally cached, and the output is provably reflection-free (NativeAOT/trim/regulated
> targets) — each clause backed by a named test or diagnostic the runtime mappers can't structurally match.

> **Licensing note.** AutoMapper is referenced as **14.0.0 only** — the last **MIT** release. v15+ is
> RPL-1.5 + commercial (GPL-incompatible); we deliberately use nothing from it. Mapperly (4.3.1) and
> Mapster (10.0.14) are MIT. Both are benchmark-only and never referenced by the shipped library.
>
> **Current releases, measured 2026-11-15** — Mapperly 4.3.1 and Mapster 10.0.14, the latest stable:
> [what each says about a mistake](#validation-what-each-library-says-about-a-mistake),
> [what each does with a hostile shape](#security-what-each-library-does-with-a-hostile-shape), and
> [performance on Windows and Linux](#current-releases-measured-2026-11-15). Full write-up:
> [the 2026-11-15 comparison](../benchmarks/results/2026-11-15-round35-t25-comparison.md). AutoMapper 14.0.0 is not
> re-measured: it is the last MIT release, unchanged, with three open advisories. The AutoMapper figures this page
> links to are from August–September 2026.
>
> **Other libraries evaluated.** TinyMapper, ExpressMapper, and Nelibur were dropped (net10-incompatible
> TFMs and/or abandoned since ~2017–2019). **AgileMapper 1.8.1** (MIT) restores on net10 but its NuGet
> package ships a **Debug-built (unoptimized) assembly**, which BenchmarkDotNet's optimization validator
> correctly rejects — benchmarking it would publish numbers that misrepresent its real performance, so it
> is excluded (that it ships unoptimized and is unmaintained is itself a characteristic of the legacy
> tier). That leaves DwarfMapper, Mapperly and Mapster, plus AutoMapper 14 in the capability tables.

## Approach & licensing at a glance

| | DwarfMapper | Mapperly 4.3.1 | Mapster 10.0.14 | AutoMapper 14.0.0 |
|---|---|---|---|---|
| Mechanism | Roslyn source gen | Roslyn source gen | runtime expr-trees (or CLI codegen) | runtime expr-trees + reflection |
| **NativeAOT / trim-safe** | **✅ yes** | ✅ yes | ❌ runtime mode no — measured: publishes with IL3053/IL2104, then the first map throws | ❌ no |
| Reflection-free | ✅ | ✅ | ❌ (runtime) | ❌ |
| License | GPL-2.0-only | MIT | MIT | MIT (v14); RPL-1.5 (v15+) |

## Capability matrix

| Capability | DwarfMapper | Mapperly | Mapster | AutoMapper 14 |
|---|:---:|:---:|:---:|:---:|
| Flat / nested auto-mapping | ✅ | ✅ | ✅ | ✅ |
| Collections / dictionaries | ✅ | ✅ | ✅ | ✅ |
| Enums (by name / value) | ✅ | ✅ | ✅ | ✅ |
| Null handling (configurable) | ✅ | ✅ | ✅ | ✅ |
| Flattening | ✅ `[Flatten]` (explicit) | convention (auto) | convention | convention (pioneered) |
| Custom per-member converter | ✅ `Use=` | ✅ | ✅ | ✅ |
| Constructor / record targets | ✅ | ✅ | ✅ | ✅ |
| Polymorphic / derived dispatch | ✅ `[MapDerivedType]` | ✅ | config | ✅ |
| **Reference-cycle / object-graph reconstruction** | ✅ `Preserve` (full topology) | ✅ `UseReferenceHandling` (opt-in) | ✅ `PreserveReference` | ✅ `PreserveReferences` |
| **Cycle → null (IgnoreCycles)** | ✅ `OnCycle=SetNull` | ❌ | ~ | ~ |
| **No-silent-StackOverflow guarantee** | ✅ depth-cap everywhere | ❌ | ~ | opt-in `MaxDepth`; no default limit |
| Graph degradation `[FlattenGraph]` | ✅ (homo + hetero) | ❌ | ❌ | ❌ |
| IQueryable projection | ✅ provably-translatable | ✅ | ✅ (EFCore) | ✅ `ProjectTo` |
| Update-into-existing | ✅ `void/T Map(s, dest)` | ✅ | ✅ `Adapt(s,dest)` | ✅ `Map(s,dest)` |
| **Zero-alloc `Span<T>` mapping** | ✅ | ❌ | ❌ | ❌ |
| **Async streaming `IAsyncEnumerable`** | ✅ | ❌ | ✅ | ❌ |
| **Blittable bulk-copy (reinterpret) fast-path** | ✅ `MemoryMarshal.Cast` memmove — arrays, `List<T>`, `ImmutableArray<T>`, enum arrays | ❌ | ❌ | ❌ |
| **SIMD primitive-widening (`int[]`→`long[]`)** | ✅ `Vector.Widen` | ❌ | ❌ | ❌ |
| **Completeness = build error** | ✅ `DWARF001` (always) | diagnostics | ❌ | `AssertConfigurationIsValid()` (test-time) |
| **Source-member coverage (unused-source check)** | ✅ `RequiredMapping=Both` → `DWARF039` (opt-in); `[MapIgnoreSource]` | ✅ `RMG020` | ❌ | ✅ (validates) |
| **Constant / computed value (`[MapValue]`)** | ✅ `[MapValue]` const + `Use=` (type-checked, `DWARF040–042`) | ✅ | ✅ | ✅ |
| **Deep source paths (`[MapProperty("A.B.C", …)]`)** | ✅ dotted path (`DWARF043` unknown, `DWARF044` nullable-hop) | ✅ | ✅ | ✅ (flatten) |
| **Unflattening (dotted target `→ Address.City`)** | ✅ single-level (`DWARF045/046`) | ~ | ✅ | ✅ (`ReverseMap`) |
| **Additional mapping parameters** | ✅ `Map(S s, …extra)` by name (`DWARF047` unused) | ✅ | ~ | ✅ (context) |
| **Naming conventions (snake/camel/UPPER)** | ✅ `NameConvention.Flexible` (`DWARF048` collision) | ~ | ✅ | ✅ |
| **Per-member null substitution** | ✅ `NullSubstitute=` (type-checked, `DWARF049`) | ~ | ✅ | ✅ |
| **Conditional member (`When=`)** | ✅ predicate `When=` (`DWARF050`) | ❌ | ✅ | ✅ |
| **Reverse mapping (`[ReverseMap]`)** | ✅ inverts simple renames (`DWARF051/052`) | ~ | ✅ | ✅ |
| **Conversion policy** | ✅ widening silent; non-lossless = `DWARF038` (Warning for lossy conversions, Info otherwise), or build error via `ImplicitConversions=false` | widening auto; lossy → diagnostic | most permissive | permissive |
| **Unmatched enum value at runtime** | throws `ArgumentOutOfRangeException` — **no fallback option** | default (a cast): passes the undefined value through; by name (`[MapEnum(ByName)]`): throws, `FallbackValue=` opts out | ~ (untested here) | maps the raw value through |
| **Runtime type with no dispatch arm** | throws `ArgumentException` naming the type; an instantiable base with no arm of its own is a **build error** (`DWARF119`) | throws | ~ (untested here) | maps the base as itself |
| **C# 15 unions** (.NET 11 SDK) ¶ | ✅ case by case, strict pairing; `DWARF113`/`DWARF114` where a pair cannot be mapped | ❌ builds with warning `RMG066`, then maps **every** union to an **empty** one | ❌ throws `CompileException` at the first map | ❌ `AssertConfigurationIsValid()` passes; maps the **first** case, throws on every other |
| `[RoundTrip]` anti-mislinking | ✅ | ❌ | ❌ | ❌ |

**Differentiators only DwarfMapper has:** the blittable SIMD fast-path, zero-alloc `Span<T>` mapping,
heterogeneous `[FlattenGraph]` degradation, a *non-optional* completeness build-error gate, `[RoundTrip]`
verification, uniform "never a silent StackOverflow" across direct/collection/dictionary cycles in
every reference mode, and two directives that remove an allocation rather than speeding one up:

* **`[MapShare]`** — assign the source reference instead of copying, for a collection the automatic
  immutability proof cannot see through. Measured **24 B/op against the copying twin's 8,080 B**, because
  the collection is not built at all. Refused outright where the graph is provably mutable (`DWARF104`):
  no assertion makes a settable member unsettable.
* **`[MapDenseEnumKeys]`** — fill an `[InlineArray]` member from an enum-keyed dictionary by index, one
  slot per declared member. **40 B/op against the dictionary twin's 384 B**; the slots live inside the
  destination object, so the dictionary and its bucket and entry arrays are never allocated. A key that
  would index outside the array is a build error (`DWARF105`), not a silent write.

Both are gated by exact allocation pins rather than described — see
[`allocation-baseline.json`](../benchmarks/DwarfMapper.Benchmarks/allocation-baseline.json) — and both are
demonstrated in the gallery (`49_MapShare.cs`, `50_MapDenseEnumKeys.cs`).

**Where DwarfMapper is the stricter one, and where that costs you.** The last two rows are the only ones on
which DwarfMapper is deliberately *less* capable than an oracle. A value the author did not declare an answer
for — an enum value from a cast or a database column, a runtime subtype with no registered arm — is refused
at runtime rather than substituted. That is the same stance as `NullStrategy.Throw` and the completeness
gate: a mapping nobody wrote is not a mapping anyone should rely on. One such case is refused earlier still:
an instance of a dispatch's own base type, when that type can be instantiated and has no arm, is visible in the
declaration and is a build error (`DWARF119`).

Mapperly reaches the same conclusion on the dispatch row by default, and on the enum row once it maps by name
(`[MapEnum(EnumMappingStrategy.ByName)]`); its default enum mapping is a cast, which passes an undefined value
through. Two independently designed generators agreeing is a better argument for the stance than either makes
alone. What Mapperly has and DwarfMapper does not is the **opt-out**: `FallbackValue=` names what an unmatched
enum value should become. If you want a display path that degrades to `Unknown` rather than throwing, DwarfMapper's
answer today is a `Use=` converter that handles the case explicitly, which is more typing and says what it
does at the call site.

Both rows are executable rather than prose, for the three columns this repository has an oracle for:
`tests/DwarfMapper.DifferentialTests/LoudRatherThanSilentTests.cs` runs DwarfMapper, Mapperly and AutoMapper
over the undeclared case and fails the day any of their cells stops being true — in either direction. Its Mapperly
enum arm is the by-name one; that Mapperly's default cast yields a value the target enum does not define is shown by
the validation probe below. The
**Mapster cells are `~` because they were not measured**: Mapster is in the differential harness, but only for
enum-order semantics (`EnumOrderSensitivityTests`), not for these two rows, and a cell filled in from a plausible
reading of somebody's defaults is exactly the kind of claim the rest of this table exists to avoid.

`¶` **The union row was measured, not read off documentation** — 2026-11-14, SDK 11.0.100, the versions in this
table, `union Pet(Cat, Dog)` into `union PetDto(CatDto, DogDto)`, with each case's map declared (Mapperly), created
(`CreateMap`, AutoMapper) or left to convention (Mapster); the same answers came from the latest stable Mapster,
10.0.14, and from the prereleases Mapperly 5.0.0-next.13 and Mapster 10.1.0-pre04. It
is not in the differential harness, which builds on the pinned .NET 10 SDK and cannot declare a union; the probe's
source and its output are in
[the 2026-11-14 union results](../benchmarks/results/2026-11-14-round35-t24-union-mapping.md).
None of the three knows what a union is. **Mapperly** sees a struct with no settable members, warns `RMG066`
("No members are mapped"), and returns `default` — an empty union, for a cat and a dog alike: the warning is
the only trace of the loss. **Mapster** refuses at the first map ("Cannot convert immutable type, please consider
using 'MapWith'"). **AutoMapper** binds the target's `PetDto(CatDto)` constructor to the source's `Value`:
a cat maps, a dog throws `AutoMapperMappingException` ("Dog -> CatDto"), and the configuration validation that
exists to catch this passes. The last is the most dangerous of the three, because a test that maps one value of
the first case passes. DwarfMapper reads the case types the compiler reports and refuses what it cannot pair;
on an older compiler it refuses every union with `DWARF113`.

## Validation: what each library says about a mistake

Measured 2026-11-15, every option at its default, one build per cell
([`benchmarks/comparison-probes/validation`](../benchmarks/comparison-probes/validation)). Mapster has no build-time
step; its opt-in `RequireDestinationMemberSource` acts when its configuration compiles, at run time.

| Mistake | DwarfMapper | Mapperly 4.3.1 | Mapster 10.0.14 |
|---|---|---|---|
| A destination member with no source | **build error** `DWARF001` | warning `RMG012`; the member keeps its default | silent (strict: throws on configuration compile) |
| A source member nothing reads | silent; `DWARF039` with `RequiredMapping = Both` | warning `RMG020` | silent |
| `string` into `int` | warning `DWARF038`; a bad value throws `FormatException` | silent; throws `FormatException` | silent; throws `FormatException` |
| A nullable into a non-nullable member | warning `DWARF070`; null is stored | silent; the assignment is skipped | silent; null is stored |
| An enum member with no counterpart (by name) | **build error** `DWARF015` | warning `RMG038`; an **undefined** enum value at run time | silent; an undefined enum value |
| A read-only destination member | **build error** `DWARF007` | warning `RMG066`; the value is lost | silent; lost |
| A constructor parameter nothing supplies | **build error** `DWARF024` | **build error** `RMG013` | silent; `null` passed in (strict: throws) |
| A flattening nobody declared (`CustomerName` ← `Customer.Name`) | **build error** `DWARF001` — declared, never guessed | flattened by convention | flattened by convention |

Five of the eight are build errors in DwarfMapper and two are warnings. Mapperly's warnings name the same mistakes, but a
build that keeps them as warnings still ships a mapper whose output is wrong in the ways the table says. Mapster says
nothing before run time.

## Security: what each library does with a hostile shape

Measured 2026-11-15, one process per cell — a stack overflow cannot be caught, it ends the process — every option at its
default, Linux, an 8 MB main-thread stack ([`benchmarks/comparison-probes/security`](../benchmarks/comparison-probes/security)).

| Shape | DwarfMapper | Mapperly 4.3.1 | Mapster 10.0.14 |
|---|---|---|---|
| A linked chain 100,000 deep | refused: catchable `DwarfMappingDepthException` (default depth 64, at most 1,000) | maps it | maps it |
| A node that references itself | catchable `DwarfMappingDepthException` | **stack overflow — the process dies** | **stack overflow — the process dies** |
| Re-entry through a user converter, 100,000 deep | catchable `InsufficientExecutionStackException` | **stack overflow — the process dies** | did not finish in 300 s |
| NativeAOT, a flat map | publishes clean, runs | publishes clean, runs | trim/AOT warnings, then **throws** on the first map |

No input ends a DwarfMapper process; the price is the bound itself — a legitimately deep chain beyond `MaxDepth` is
refused. Mapperly and Mapster have reference handling and depth limits as opt-ins; the table is their defaults, and with
those defaults one cyclic payload takes a server down.

## Testing approach comparison

| | DwarfMapper | Mapperly | Mapster | AutoMapper |
|---|---|---|---|---|
| Generator snapshot tests | ✅ Verify | ✅ Verify | n/a | n/a |
| Integration / behavioural | ✅ | ✅ | ✅ xUnit | ✅ |
| **Fuzz / property-based** | ✅ seeded combinatorial + topology oracles | ❌ | ❌ | ❌ |
| **Adversarial / exhaustion** | ✅ | ❌ | ❌ | ❌ |
| **Determinism tests** (byte-identical re-emit) | ✅ dedicated | ~ (Verify snapshots pin output) | n/a | n/a |
| **Assembly-scan self-validation / meta-tests** | ✅ (descriptor↔release sync, hollow-test detector, matrix completeness) | ❌ | ❌ | ❌ |
| AOT / trim CI gate | ✅ sample + gate | ✅ | n/a | n/a |
| Coverage gate | ✅ CI threshold | ~ | ~ | ~ |
| User-side config validation | build-time `DWARF001` | build-time | none | `AssertConfigurationIsValid()` (runtime/test) |

Beyond snapshots and integration tests, DwarfMapper's suite adds seeded fuzzing, adversarial/exhaustion
matrices, determinism checks, and **self-validating meta-tests** (the test suite scans the assembly to prove
every diagnostic/attribute/enum is covered). The competitor cells describe each project's own test suite as
published, not something this repository's harness verifies. The completeness gate is a *compile error* — you
cannot ship an incomplete mapping. AutoMapper validates at test or run time via `AssertConfigurationIsValid()`,
Mapperly reports a missing source as a warning (`RMG012`) that the build lets through unless warnings are
errors, and Mapster has no build-time check.

## Migration ease (the critical concern)

> **Full feature-by-feature conversion guide:** [`MIGRATION.md`](MIGRATION.md) maps *every* AutoMapper 14 /
> Mapster / Mapperly feature and mechanic to its DwarfMapper equivalent (with before→after and honest
> divergence/non-goal notes). Each "YES" row is proven at runtime by the parity suite
> [`LibraryParityRuntimeTests.cs`](../tests/DwarfMapper.IntegrationTests/LibraryParityRuntimeTests.cs).

The goal: moving an existing codebase to DwarfMapper should be **near single-line / mechanical**, never
"add `partial` everywhere" or "attribute every DTO class". DwarfMapper already requires **zero attributes
on the source/target POCOs** — only the mapper class is annotated. With **`[GenerateMap<S,T>]`** (added
for this purpose) a whole mapping is a single attribute line, so the diff from each competitor is small:

### From AutoMapper — mechanical 1:1 replace

```diff
- public class MappingProfile : Profile {
-     public MappingProfile() {
-         CreateMap<Order, OrderDto>();
-         CreateMap<Customer, CustomerDto>();
-     }
- }
+ [DwarfMapper]
+ [GenerateMap<Order, OrderDto>]
+ [GenerateMap<Customer, CustomerDto>]
+ public partial class Mappers { }
```
`CreateMap<A,B>();` → `[GenerateMap<A,B>]` (a find-and-replace). Call sites: `mapper.Map<OrderDto>(o)` →
`mappers.Map(o)` (drop the type arg — the overload is resolved by source type). **POCOs are untouched.**
Bonus: AutoMapper's `AssertConfigurationIsValid()` test becomes unnecessary — completeness is now a
compile error.

### From Mapster — keep the POCOs, add declarations

Mapster's zero-config `src.Adapt<Dst>()` has no declarations to port, so migration is: add one
`[GenerateMap<Src,Dst>]` per pair you use, and replace `src.Adapt<Dst>()` with `mappers.Map(src)`. You
gain compile-time safety + AOT (Mapster's runtime mode is not AOT-safe). The POCOs stay plain.

### From Mapperly — nearly identical, or even less ceremony

Mapperly already uses `[Mapper] partial class` + a `partial Dst Map(Src)` per pair. Port: `[Mapper]` →
`[DwarfMapper]`, and either keep the `partial` method declarations (same shape) **or** collapse each to a
`[GenerateMap<Src,Dst>]` attribute line and delete the partial method bodies-of-signatures.

### Ceremony scorecard (per mapping pair)

| | Per-pair ceremony | Attributes on DTO classes |
|---|---|---|
| AutoMapper | `CreateMap<A,B>();` (1 line, in a Profile) + DI | none |
| Mapster (runtime) | none (convention) | none |
| Mapperly | `partial B Map(A);` (1 method decl) | none |
| **DwarfMapper `[GenerateMap]`** | `[GenerateMap<A,B>]` (1 attribute line) | **none** |
| DwarfMapper (partial method) | `partial B Map(A);` (1 method decl) | none |

DwarfMapper now matches the *lowest* per-pair ceremony of the group while keeping POCOs attribute-free,
and uniquely makes completeness a build error.

### What one real migration actually found

A ~300-map AutoMapper 14 codebase was converted in full, with every pair replayed against a golden master
captured **before** the conversion. Three findings are worth reporting honestly, in both directions.

**1. The migration surfaced a live AutoMapper data-loss bug.** A `HashSet<BotPlatform> → HashSet<int>` member
could not be converted element-wise by AutoMapper, which collapsed the whole set to a single `0`:

```text
source:      [ 4, 5 ]
AutoMapper:  [ 0 ]        ← both values lost; 0 is not even in the source
DwarfMapper: [ 4, 5 ]
```

The field recorded *which platforms the user had chosen to display*, so that choice had been persisted as
`{0}` regardless of what anyone picked. This is **one anecdote from one codebase, not a benchmark** — but the
triggering shape is easy to check for in your own code: a collection member whose element type differs on the
two sides.

**2. Most divergences were the migration's own decisions, not defects.** 233 of 244 replayable pairs matched
byte-for-byte on the first full run. Of the rest, all were explained: deliberate non-conversions, hand-written
converters the harness could not see, and two documented behaviour choices.

**3. It found four DwarfMapper defects, three of them silent-data-loss behind a green build.** They are fixed,
each with a regression test, and the experience produced the diagnostics `DWARF078`–`DWARF080` plus the
pre-flight checklist in [`howto/migrate-from-automapper.md`](howto/migrate-from-automapper.md). The honest
lesson is not "the library was fine" — it is that **a 5,000-test generator suite could not reach any of them**,
because every one needed a multi-assembly, runtime-registry, real-consumer shape.

**Technique worth stealing:** capture a golden master *while still on the old mapper* — serialise both the
fabricated source and the mapped output, commit both, then replay against the new mapper. Committing the
**source** rather than a seed is the load-bearing detail: it makes the comparison independent of the fixture
generator, which will otherwise change under you mid-migration and silently reshuffle every input while still
appearing to pass. `DwarfMapper.Testing`'s `ObjectFactoryV2` and `GraphOracleComparer` are built for this (the
package is on nuget.org from 1.1.0-rc.1).

## Performance & memory

### Current releases, measured 2026-11-15

`MapperBenchmarks` in [`benchmarks/DwarfMapper.Benchmarks`](../benchmarks/DwarfMapper.Benchmarks/), BenchmarkDotNet
0.15.8 DefaultJob, one run per platform on the same AMD Ryzen 5 5600: Windows 10 with .NET 10.0.11, and Linux in the
`mcr.microsoft.com/dotnet/sdk:10.0.101` container (.NET 10.0.1, four logical CPUs). Single-object rows map a ring of
distinct fixture-drawn payloads; collection rows have 1,000 elements. Mean, **Windows / Linux**:

| Scenario | hand-written | DwarfMapper | Mapperly 4.3.1 | Mapster 10.0.14 |
|---|---:|---:|---:|---:|
| Flat (1 object) | 4.6 / 6.6 ns | 4.7 / 6.8 ns | 4.9 / 6.9 ns | 13.5 / 16.4 ns |
| Flatten (`Order.Customer.Name`) | — | 5.0 / 7.1 ns | 5.0 / 7.1 ns | 13.4 / 16.6 ns |
| Nested object | — | 10.8 / 17.2 ns | 10.7 / 17.1 ns | 19.6 / 26.6 ns |
| Enum, by name | — | 12.7 / 13.2 ns | 12.4 / 13.0 ns | — |
| Enum, by value ¹ | — | 3.4 / 10.8 ns | 3.1 / 5.2 ns | 11.9 / 14.5 ns |
| Array (1,000 reference elements) | — | 5.88 / 7.81 µs | 4.86 / 7.77 µs | 6.03 / 7.65 µs |
| List (1,000 reference elements) | — | 5.46 / 7.83 µs | 5.57 / 8.16 µs | 5.32 / 8.57 µs |
| Dictionary (1,000 entries) ² | — | 8.18 / 9.40 µs | 18.92 / 10.91 µs | 25.57 / 20.82 µs |
| Blit (1,000 blittable structs) | — | 0.41 / 0.51 µs | 0.95 / 1.08 µs | 0.98 / 1.13 µs |
| Value-element list (`int[]` → `List<long>`) | — | 0.32 / 0.43 µs | 0.88 / 1.06 µs | 0.91 / 1.08 µs |
| Widen (`int[]` → `long[]`) | — | 0.32 / 0.45 µs | 0.42 / 0.53 µs | 0.65 / 0.80 µs |
| Nested graph, mixed fills | — | 0.83 / 1.23 µs | 1.70 / 2.03 µs | 1.02 / 1.46 µs |

¹ The Linux by-value enum figure is a measurement artefact, not a cost of the generated code. On Windows the two
generators are level. The two methods compile to the same instructions apart from DwarfMapper's argument null check,
and the same method measured 5.5 and 19.6 ns in two Linux processes. The probe is in
[the 2026-11-15 results](../benchmarks/results/2026-11-15-round35-t25-comparison.md#the-linux-by-value-enum-gap).

² The dictionary row depends on the platform. DwarfMapper is 2.3× ahead of Mapperly on Windows and 1.16× on Linux.
The difference is Mapperly's dictionary path, which is slow on Windows; DwarfMapper's is close to level on both. Quote
both figures with that reason, or neither.

Allocations are identical in every row except two: the dictionary (DwarfMapper 31,120 B, Mapperly 31,176 B, Mapster
102,376 B) and the nested graph (DwarfMapper and Mapster 8,112 B, Mapperly 9,456 B). Each benchmark's allocated bytes
are pinned exactly in [`allocation-baseline.json`](../benchmarks/DwarfMapper.Benchmarks/allocation-baseline.json) and
checked every night (`scripts/housekeeping.ps1 -BenchSmoke`); timings are not gated, so reproduce them on your own
hardware.

Where DwarfMapper is ahead on both platforms, it takes a path the others do not have: the blit (2.1–2.3×), the
value-element list (2.4–2.8×), the SIMD widen (1.2–1.3× over Mapperly) and the nested graph's fills (1.6–2× over
Mapperly). Flat, flatten, nested, the by-name enum and the reference list are level within a few per cent. Mapster
takes 1.5–3.5× as long as the generators on every single-object row. Three rows need more than their numbers:

- **The blit lead depends on the collection size.** The table's 2.1–2.3× is a thousand-element copy, near the top of
  the curve. A sweep from 1 to 10⁶ elements on Windows
  ([2026-09-09](../benchmarks/results/2026-09-09-collection-decade-sweep.md)) measured 2.05–2.20× over Mapperly at
  100–1,000 elements, 1.06× at 10,000, where the destination array crosses the 85 KB Large Object Heap threshold and
  garbage collection dominates every library, and 1.45× at 10⁶. With one element the block copy is slightly slower
  than the element loop it replaces (0.84×), because its setup is not amortised. Quote the ratio with its size.
- **Reference-element arrays carry a null guard.** On the array row Mapperly is 17 % ahead on Windows and level on
  Linux. DwarfMapper tests each element and maps a null element to null; Mapperly's element method has no guard and
  dereferences it. The same sweep found Mapperly ahead by 8–29 % at every size on Windows, and removing the
  per-element test recovered about 9 %, which accounts for the gap. It is the price of a defined behaviour, and no way
  of closing it keeps that behaviour. The `List` row, with the same elements, is level.
- **Enums have two strategies, and each row measures one.** DwarfMapper maps enum members by name by default;
  Mapperly and Mapster cast the underlying value. The benchmark's enums declare their members in different orders, so
  a cast maps `Pending` to `Closed`: the two strategies give different answers. By name, DwarfMapper and Mapperly
  (configured with `[MapEnum(EnumMappingStrategy.ByName)]`) are level. By value, DwarfMapper with
  `EnumStrategy.ByValue` is level with Mapperly's default on Windows, and the Linux row is note ¹. By name is the
  safe default; `EnumStrategy.ByValue` is a one-line opt-out, and it is also what lets an enum array take the blit.

AutoMapper 14.0.0 is not in the 2026-11-15 run. Its figures from August 2026, on Windows with Mapster 10.0.8, are in
[the 2026-08-24 sweep](../benchmarks/results/2026-08-24-premerge-full-sweep.md) and, for the enum strategies,
[the 2026-08-26 enum run](../benchmarks/results/2026-08-26-enum-strategy-like-for-like.md).

Run locally for numbers on your hardware:

```bash
dotnet run -c Release --project benchmarks/DwarfMapper.Benchmarks
```

### C# 15 unions (.NET 11)

Nobody else maps a union (the capability matrix above, `¶`), so DwarfMapper's union map is measured against code
written by hand, and against the class hierarchy that held the same data before unions
([`benchmarks/DwarfMapper.UnionBenchmarks`](../benchmarks/DwarfMapper.UnionBenchmarks); full write-up with both
platforms in [the 2026-11-14 union results](../benchmarks/results/2026-11-14-round35-t24-union-mapping.md)).
Measured 2026-11-14 on Windows 10 (AMD Ryzen 5 5600), .NET 11.0.0, BenchmarkDotNet 0.14.0 DefaultJob:

| Payments (a three-case union) | One value | Batch of 1,000 |
|---|---:|---:|
| Union, hand-written switch | 5.86 ns | 8.83 µs |
| Union, hand-written in DwarfMapper's own structure | 6.27 ns | 9.17 µs |
| Union, DwarfMapper | 6.29 ns | 9.85 µs |
| Class hierarchy, hand-written | 7.25 ns | 9.77 µs |
| Class hierarchy, DwarfMapper | 8.24 ns | 8.78 µs |
| Class hierarchy, Mapperly 4.3.1 | 8.34 ns | 10.75 µs |
| Class hierarchy, Mapster 10.0.8 | 20.16 ns | 27.98 µs |

The generated union map costs what its own shape costs by hand — a switch calling the declared map for each case —
and trails a switch that builds every DTO inline by that call, under a nanosecond. Allocations are identical in every
row. One loss is recorded rather than smoothed over: for a list of unions, DwarfMapper's span fill measured 4–7 %
slower than plain `Add` (the row the hand-written structure beats). A re-run against Mapster 10.0.14 for the 2026-11-15
comparison gave the same picture: the generated union map level with its hand-written structure, and Mapster about 3× on the
hierarchy.

### Higher instructions / SIMD

DwarfMapper has **two** SIMD fast-paths that no competitor offers:

1. **Blittable bulk copy** — a layout-identical element pair is reinterpreted as a single
   `MemoryMarshal.Cast` block copy, with the same-bytes verdict settled at generation time rather than
   re-checked at runtime; the runtime lowers that memmove to the widest available vector instructions
   automatically (struct-array case at memcpy speed). It is not limited
   to `TSrc[]`→`TDst[]`: **`List<T>` on either side** (and therefore `IList<T>`, `IReadOnlyList<T>`,
   `ICollection<T>` and `IReadOnlyCollection<T>`, which all materialise to `List<T>`), **`ImmutableArray<T>`
   in both directions**, and **enum arrays** all take it when the proof holds. Enum arrays qualify only where
   the conversion is genuinely a reinterpret — `EnumStrategy.ByValue` over the same underlying type, or an
   enum against its own underlying primitive — because the default by-name mapping *throws* on a value
   matching no member and a block copy would pass such a value through instead.
   `Dictionary<K,V>` and `HashSet<T>` cannot join them: their entries live in a private nested struct with no
   public span over it, so no layout can be proven without reflection.
2. **SIMD widening** (shipped) — a lossless primitive widen array (`int[]`→`long[]`, `short[]`→`int[]`,
   `byte[]`→`ushort[]`, `float[]`→`double[]`, and the unsigned/sbyte variants — the seven
   `System.Numerics.Vector.Widen` pairs) is vectorized with `Vector.Widen` behind a
   `Vector.IsHardwareAccelerated` guard and a scalar tail. The result is **bit-for-bit identical to the
   scalar implicit widen** (verified by adversary/fuzz tests over every length around the vector boundary
   and the full value range, plus an AOT gate) — it is purely a throughput win and stays reflection-free /
   NativeAOT-safe. Like the bulk copy it is not limited to arrays: **`List<T>` on either side**
   (with the list-family interfaces that materialise to it) and **`ImmutableArray<T>` in both directions** take
   the same vector loop. Competitors copy these element-by-element.

Both are emitted only when provably safe, and never under `ReferenceHandling = Preserve`: there one source
collection reached twice must be one target collection, which takes the helper that registers it, and a static
block copy has nowhere to register. Everything else falls back to the element loop (with `CreateChecked` for
narrowing). See the `Blit` and `Widen` benchmark categories.

### NativeAOT benchmarking & stability

The BenchmarkDotNet suite above measures the JIT. `samples/DwarfMapper.AotBench` is a separate harness
that is **published with NativeAOT and run as a native binary** — it both times the hot paths under real
AOT codegen and stress-tests for instabilities the JIT can't reveal. It is run locally on demand, not in CI;
CI runs the separate `AotSample` gate, which publishes with trim and AOT warnings as errors and runs its checks.
The harness checks SIMD widen/blit bit-exactness at every size around the vector boundary, Preserve-topology
determinism over 100,000 runs, the catchable depth guard over 20,000 runs, and `OnCycle=SetNull` acyclicity over
50,000 runs.

Last recorded run: 2026-07-24, win-x64, .NET 10.0.1
([full output](../benchmarks/results/2026-07-24-full-sweep-windows.md#nativeaot-sample)):

- **No instabilities.** Every correctness and determinism check passed across the 170,000 stress iterations, and
  the SIMD output was bit-for-bit identical to the scalar reference at every boundary size, including negative
  values (sign extension).
- **Timing under NativeAOT**, median ns/op: Flat 8.6 ns, Array (1,000) 9.31 µs, Blit (1,000) 0.47 µs, Widen
  (1,000) 0.52 µs. These are one local run on an older build; read them as an order of magnitude, not against the
  JIT table above.
- **Vector width.** That AOT binary reported `Vector<int>.Count = 4` (128-bit vectors) where the JIT on the same
  machine used 256-bit vectors, so the `Vector.Widen` path ran at half the JIT's width. The result is the same at
  any width — the scalar tail covers what the vector body does not — so this is a speed setting, not a
  correctness one. NativeAOT's `IlcInstructionSet` property selects the instruction set the native code targets:

  ```xml
  <PropertyGroup>
    <IlcInstructionSet>native</IlcInstructionSet>  <!-- the build machine's instruction set -->
  </PropertyGroup>
  ```

Run it yourself:

```bash
dotnet publish samples/DwarfMapper.AotBench -c Release -r win-x64        # default instruction set
dotnet publish samples/DwarfMapper.AotBench -c Release -r win-x64 -p:IlcInstructionSet=native   # the build machine's
./samples/DwarfMapper.AotBench/bin/Release/net10.0/win-x64/publish/DwarfMapper.AotBench.exe
```
