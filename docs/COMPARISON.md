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
> Mapster (10.0.8) are MIT. Both are benchmark-only and never referenced by the shipped library.
>
> **Other libraries evaluated.** TinyMapper, ExpressMapper, and Nelibur were dropped (net10-incompatible
> TFMs and/or abandoned since ~2017–2019). **AgileMapper 1.8.1** (MIT) restores on net10 but its NuGet
> package ships a **Debug-built (unoptimized) assembly**, which BenchmarkDotNet's optimization validator
> correctly rejects — benchmarking it would publish numbers that misrepresent its real performance, so it
> is excluded (that it ships unoptimized and is unmaintained is itself a characteristic of the legacy
> tier). The result is a clean four-way comparison (DwarfMapper / Mapperly / Mapster / AutoMapper 14).

## Approach & licensing at a glance

| | DwarfMapper | Mapperly 4.3.1 | Mapster 10.0.8 | AutoMapper 14.0.0 |
|---|---|---|---|---|
| Mechanism | Roslyn source gen | Roslyn source gen | runtime expr-trees (or CLI codegen) | runtime expr-trees + reflection |
| **NativeAOT / trim-safe** | **✅ yes** | ✅ yes | ❌ runtime mode no | ❌ no |
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
| **No-silent-StackOverflow guarantee** | ✅ depth-cap everywhere | ❌ | ~ | depth cap |
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
| **Conversion policy** | ✅ widening silent; non-lossless = `DWARF038` suggestion, or build error via `ImplicitConversions=false` | widening auto; lossy → diagnostic | most permissive | permissive |
| **Unmatched enum value at runtime** | throws `ArgumentOutOfRangeException` — **no fallback option** | throws; `FallbackValue=` opts out | ~ (untested here) | maps the raw value through |
| **Runtime type with no dispatch arm** | throws `ArgumentException` naming the type | throws | ~ (untested here) | maps the base as itself |
| `[RoundTrip]` anti-mislinking | ✅ | ❌ | ❌ | ❌ |

**Differentiators only DwarfMapper has:** the blittable SIMD fast-path, zero-alloc `Span<T>` mapping,
heterogeneous `[FlattenGraph]` degradation, a *non-optional* completeness build-error gate, `[RoundTrip]`
verification, and uniform "never a silent StackOverflow" across direct/collection/dictionary cycles in
every reference mode.

**Where DwarfMapper is the stricter one, and where that costs you.** The last two rows are the only ones on
which DwarfMapper is deliberately *less* capable than an oracle. A value the author did not declare an answer
for — an enum value from a cast or a database column, a runtime subtype with no registered arm — is refused
at runtime rather than substituted. That is the same stance as `NullStrategy.Throw` and the completeness
gate: a mapping nobody wrote is not a mapping anyone should rely on.

Mapperly reaches the same conclusion by default on both rows, which is worth saying plainly — two
independently designed generators agreeing is a better argument for the stance than either makes alone. What
Mapperly has and DwarfMapper does not is the **opt-out**: `FallbackValue=` names what an unmatched enum value
should become. If you want a display path that degrades to `Unknown` rather than throwing, DwarfMapper's
answer today is a `Use=` converter that handles the case explicitly, which is more typing and says what it
does at the call site.

Both rows are executable rather than prose, for the three columns this repository has an oracle for:
`tests/DwarfMapper.DifferentialTests/LoudRatherThanSilentTests.cs` runs DwarfMapper, Mapperly and AutoMapper
over the undeclared case and fails the day any of their cells stops being true — in either direction. The
**Mapster cells are `~` because they were not measured**: the differential harness references Mapperly and
AutoMapper only, and a cell filled in from a plausible reading of somebody's defaults is exactly the kind of
claim the rest of this table exists to avoid.

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

DwarfMapper's test methodology is the most defensive of the four: beyond snapshots + integration it adds
seeded fuzzing, adversarial/exhaustion matrices, determinism checks, and **self-validating meta-tests**
(the test suite scans the assembly to prove every diagnostic/attribute/enum is covered). The completeness
gate is a *compile error* — you cannot ship an incomplete mapping — whereas AutoMapper validates at
test/runtime via `AssertConfigurationIsValid()` and Mapster/Mapperly offer no equivalent.

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
appearing to pass. `DwarfMapper.Testing`'s `ObjectFactory` and `GraphOracleComparer` are built for this.

## Performance & memory

See [`benchmarks/DwarfMapper.Benchmarks`](../benchmarks/DwarfMapper.Benchmarks/) — DwarfMapper vs.
hand-written vs. the three competitors across **flat / nested / collection / blittable-struct** scenarios
with `[MemoryDiagnoser]`.

A single local run — BenchmarkDotNet DefaultJob, **.NET 10.0.1, X64 RyuJIT AVX2** (AMD Ryzen 5 5600,
non-dedicated machine; the Flat/Blit numbers are committed under [`benchmarks/results/`](../benchmarks/results/), the full 9-category sweep is not, and none run in CI). Absolute ns
vary by hardware; **relative ordering is the point — reproduce locally with the command below**:

| Scenario | DwarfMapper | Mapperly 4.3.1 | Mapster 10.0.8 | AutoMapper 14.0.0 | hand-written |
|---|---:|---:|---:|---:|---:|
| Flat (1 object) † | 6.7 ns | 6.9 ns | 15.8 ns | 56.4 ns | 6.7 ns |
| Nested | 11.5 ns | 10.3 ns | 20.5 ns | 58.4 ns | — |
| Array (1000 objects) | 4.55 µs | 4.47 µs | 5.82 µs | 5.26 µs | — |
| **Blit (1000 structs)** † | **0.59 µs** | 1.08 µs | 1.11 µs | 1.18 µs | — |
| **Value-element list (`int[]`→`List<long>`, 1000)** ‡ | **0.66 µs** | 1.00 µs | 0.92 µs | 2.83 µs | — |
| **Nested graph, mixed fills (50 lines + value collections)** ‡ | **0.88 µs** | 1.73 µs | 1.02 µs | 2.00 µs | — |
| **SIMD widen (`int[]`→`long[]`, 1000)** ‡ | **0.36 µs** | 0.44 µs | 0.70 µs | 0.74 µs | — |
| **Flatten (`Order.Customer.Name`)** ‡ | **4.9 ns** | 5.5 ns | 14.4 ns | 53.6 ns | — |
| **Nested object** ‡ | **10.8 ns** | 12.3 ns | 21.1 ns | 59.4 ns | — |
| **List (1000 REFERENCE elements)** ‡ | 6.11 µs | 6.13 µs | **5.42 µs** | 8.73 µs | — |
| **Array (1000 REFERENCE elements)** ‡ | 4.69 µs | **4.65 µs** | 6.09 µs | 5.23 µs | — |
| **Enum, BY NAME** ‡ § | 12.5 ns | **12.3 ns** | — | 73.6 ns | — |
| **Enum, BY VALUE** ‡ § | **2.9 ns** | 3.0 ns | 12.3 ns | — | — |
| **Widen (1000 int→long)** | **0.35 µs** | 0.43 µs | 0.69 µs | 0.72 µs | — |
| Allocations (all scenarios) | = hand-written | = | = | = | baseline |

`‡` **Value-element list, measured 2026-08-23** (Windows, AMD Ryzen 5 5600, .NET 10.0.1, DefaultJob, one run,
standard error +/- 11 ns). DwarfMapper fills the destination through `CollectionsMarshal.SetCount` + a span;
the others `Add` element-by-element, paying `_version++`, a capacity check that cannot fail and `_size++` per
element. **Allocations are identical to Mapperly and Mapster (8,112 B)** — the gap is fill efficiency, not
memory. Note the contrast with the reference-element `List` row above, where DwarfMapper is *behind* Mapster:
there the cost of allocating a thousand destination objects dominates and the fill strategy cannot show
through, which is why the optimisation is deliberately restricted to value elements.

`§` **The enum row used to say DwarfMapper was 3.98x slower than Mapperly. That was an artifact of the
benchmark, and correcting it removed the finding entirely.** Enum mapping has two legitimate strategies and
the four libraries do not agree on a default: **DwarfMapper and AutoMapper match member NAMES; Mapperly and
Mapster cast the underlying VALUE.** The benchmark left every library at its default, so this row was not
comparing four implementations of one operation — it was comparing a name switch against a raw cast, and
publishing the difference as a deficiency in our emission.

They do not even produce the same answer. The benchmark's enums are deliberately reordered
(`{Pending, Active, Closed}` against `{Closed, Pending, Active}`), so a value cast maps `Pending` to
`Closed`. Roughly a 4x "win" was the price of answering a different question.

Measured like-for-like (2026-08-26, same machine and job as the rows above), **the gap is gone in one
direction and reversed in the other**:

* **By name** — DwarfMapper **12.5 ns**, Mapperly told to match at `EnumMappingStrategy.ByName` **12.3 ns**.
  A 1.6 % difference, near enough the run-to-run spread to carry no meaning. AutoMapper, whose default is
  also by name, takes **73.6 ns**. So our switch was never the problem; the strategy was the whole gap.
* **By value** — DwarfMapper opted in with `EnumStrategy.ByValue` is **2.9 ns**, ahead of Mapperly's default
  **3.0 ns** and Mapster's default **12.3 ns**. Mapster performs a cast and still measures like a switch,
  because its per-call dispatch dominates whatever the cast costs.

Two things follow. **By-name safety is a default, not a tax** — `EnumStrategy.ByValue` is a documented
one-line opt-out (the same switch that lets an enum array take the blit fast path), and taking it puts
DwarfMapper first in its class. And **nothing here is filed as a defect any more**; the earlier "improving
this is filed, not fixed" note is withdrawn, because the thing it proposed to improve did not exist.

The semantics behind both rows are executable rather than asserted: `EnumOrderSensitivityTests` maps
divergently ordered enums through all four libraries and pins what each returns. Writing it corrected two
confident guesses of mine — that AutoMapper mapped by value (its ledger entry is about *undefined* values
passing through, not defined members) and that Mapster mapped by name (12.3 ns merely *looks* switch-shaped).
Both were wrong, which is why the table now rests on the tests instead of on inference.

**Every single-object row on this page now maps a RING of 512 distinct fixture-drawn payloads**, cycled per
iteration, rather than one cached object. That change moved four rows and *flipped one ranking* (Nested went
from marginally behind Mapperly to 1.14x ahead), which is why it is enforced by an architectural rule —
`BenchmarkPayloadRuleTests` fails the build if any benchmark maps a static payload.

**The four rows where a rival is ahead are there on purpose.** A comparison that lists only its wins is an
advertisement. The pattern across the whole sweep is consistent and worth stating plainly: **DwarfMapper
leads wherever a fast path is eligible and trails slightly where none is.** `List` and `Array` carry
reference elements, so neither the blit nor the value-element span fill applies, and what remains is
allocating a thousand destination objects — where there is nothing to win and Mapster's loop is marginally
tighter. The `Array` gap (1.01x) is smaller than the combined standard error and should be read as parity.
Full per-row numbers with standard errors:
[`benchmarks/results/2026-08-24-premerge-full-sweep.md`](../benchmarks/results/2026-08-24-premerge-full-sweep.md).

**Read the nested row carefully — it is not a fill-strategy number.** That graph mixes both strategies in one
map (reference-element `Lines` on the `Add` path, value-element `Totals` and per-line `Quantities` on the
span fill) and measures the whole mapping, including allocating fifty nested destination objects. Its gap
against Mapperly (900 ns) is far larger than the isolated value-element gap (257 ns) despite containing
FEWER value elements, so most of that lead comes from elsewhere in the graph mapping, not from the fill —
Mapperly also allocates more there (9,456 B against 8,112 B). The `int[]→List<long>` row is the isolation;
the nested row is what a realistic order-shaped map looks like.

`†` **Flat and Blit re-measured this session** (Linux, AMD Ryzen 5 5600, .NET 10.0.1 DefaultJob, tight
error bars): on Flat, hand-written (6.7 ns), DwarfMapper (6.7 ns) and Mapperly (6.9 ns) are statistically
tied — DwarfMapper marginally ahead of Mapperly here. Nested / Array / Widen rows are from an earlier run —
reproduce locally. Codegen mappers (DwarfMapper / Mapperly) cluster at hand-written speed; the runtime tier
(Mapster ~2.4×, AutoMapper ~8×) trails.

**Takeaways:**
- DwarfMapper **sits with hand-written and Mapperly at the codegen floor** — on the flat map its median
  is statistically tied (~6.7 ns each; DwarfMapper marginally ahead of Mapperly this run), since the generated
  code is the same direct-assignment shape — with **zero allocation overhead** (the destination object is the
  only allocation). On the 1000-object array it and
  Mapperly co-lead (4.55 µs vs 4.47 µs — within run-to-run noise), both ahead of the runtime mappers.
- On the **blittable struct array it is ~1.8–2.0× faster than every competitor** — the `MemoryMarshal.Cast`
  block-copy (reinterpret) path that none of Mapperly / Mapster / AutoMapper have (they copy field-by-field).
  (This session's measurement: **0.59 µs vs 1.08–1.18 µs** for the others — decisive, with allocations
  identical across all four libraries.)
- On the **primitive widening array (`int[]→long[]`)** the `Vector.Widen` path is ~2× faster than the
  runtime mappers (Mapster/AutoMapper) and a hair ahead of Mapperly's scalar codegen loop — at this size
  the work is memory-bound (writing the 8 KB output), so SIMD mainly separates it from the reflection/
  expression tier; the gap widens for smaller element types or cache-resident data.
- It is **~8× faster than AutoMapper** and **~2.4× faster than Mapster** on flat maps (measured: 56.4/6.7 ≈ 8.4×, 15.8/6.7 ≈ 2.4×), which pay
  runtime expression-tree / reflection overhead (and are not NativeAOT-safe). Mapster's first-call
  expression compilation is amortized here (steady state), yet still trails the codegen mappers.

Run locally for numbers on your hardware:

```bash
dotnet run -c Release --project benchmarks/DwarfMapper.Benchmarks
```

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
   NativeAOT-safe. Competitors copy these element-by-element.

Both are emitted only when provably safe; everything else falls back to the element loop (with
`CreateChecked` for narrowing). See the `Blit` and `Widen` benchmark categories.

### NativeAOT benchmarking & stability

The BenchmarkDotNet suite above measures the JIT. `samples/DwarfMapper.AotBench` is a separate harness
that is **published with NativeAOT and run as a native binary** — it both times the hot paths under real
AOT codegen and stress-tests for instabilities the JIT can't reveal. (It is run **locally on demand**; CI
runs the separate `AotSample` safety/trim gate, not this stress harness — the numbers below are a single
local machine's.)  The checks: SIMD widen/blit bit-exactness at
every size around the vector boundary, Preserve-topology determinism over 100 000 runs, catchable
depth-guard over 20 000 runs, and `OnCycle=SetNull` acyclicity over 50 000 runs.

Results (ILC 10.0.1, win-x64, AMD Ryzen 5 5600; **1.16 MB** self-contained native exe):

- **No instabilities.** Every correctness/determinism check passes (exit 0) across 170 000 stress
  iterations (100 000 Preserve-cycle + 20 000 depth-guard + 50 000 SetNull); the default `dotnet publish`
  emits **zero** IL2xxx/IL3xxx trim/AOT warnings. SIMD output is **bit-for-bit identical** to the scalar
  reference at all boundary sizes, including negatives (sign extension).
- **AOT timing is *steadier* than the JIT** — no tiered-compilation jitter, so per-op min/max spreads are
  tighter (a stability *positive*). Indicative ns/op at baseline SSE2 width: Flat ≈ 8 ns, Array (1000)
  ≈ 8.8 µs, **Blit (1000) ≈ 0.39 µs** (from the earlier AOT run — the JIT was later re-measured at ~0.59 µs, so treat these AOT figures as indicative of that run; the reinterpret is width-independent),
  Widen (1000) ≈ 0.47 µs (half-width; see the caveat below).
- **One AOT usage caveat worth knowing (not an instability).** NativeAOT defaults to a **baseline
  instruction set** (x86-64-v1 / SSE2) for portability, so `Vector<int>.Count == 4` under default AOT vs
  `8` under the AVX2-detecting JIT — the `Vector.Widen` path runs half-width. It stays **correct** (the
  scalar tail + narrower body produce identical results), just not maximally fast. To get full-width SIMD
  under AOT, opt into a higher ISA, e.g.:

  ```xml
  <PropertyGroup>
    <IlcInstructionSet>native</IlcInstructionSet>  <!-- build-machine ISA; or a specific list, e.g. avx2 -->
  </PropertyGroup>
  ```

  Re-verified this run: with `IlcInstructionSet=native` the AOT binary reports `Vector<int>.Count == 8`
  (vs `4` at baseline) and all correctness/determinism checks still pass (exit 0). (`x86-64-v3` is rejected
  by ILC 10.0.1 — use `native` or an explicit ISA list.)

Run it yourself:

```bash
dotnet publish samples/DwarfMapper.AotBench -c Release -r win-x64        # default baseline SIMD
dotnet publish samples/DwarfMapper.AotBench -c Release -r win-x64 -p:IlcInstructionSet=native   # full-width
./samples/DwarfMapper.AotBench/bin/Release/net10.0/win-x64/publish/DwarfMapper.AotBench.exe
```
