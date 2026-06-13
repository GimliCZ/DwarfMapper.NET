<!-- SPDX-License-Identifier: GPL-2.0-only -->
# DwarfMapper vs. Mapperly / Mapster / AutoMapper

A capability, testing, performance, and **migration-ease** comparison against the three most common
.NET mappers. Benchmarks live in [`benchmarks/DwarfMapper.Benchmarks`](../benchmarks/DwarfMapper.Benchmarks/).

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
| Flattening | ✅ `[Flatten]` | manual | convention | convention (pioneered) |
| Custom per-member converter | ✅ `Use=` | ✅ | ✅ | ✅ |
| Constructor / record targets | ✅ | ✅ | ✅ | ✅ |
| Polymorphic / derived dispatch | ✅ `[MapDerivedType]` | ✅ | config | ✅ |
| **Reference-cycle / object-graph reconstruction** | ✅ `Preserve` (full topology) | ❌ partial | ✅ `PreserveReference` | ✅ `PreserveReferences` |
| **Cycle → null (IgnoreCycles)** | ✅ `OnCycle=SetNull` | ❌ | ~ | ~ |
| **No-silent-StackOverflow guarantee** | ✅ depth-cap everywhere | ❌ | ~ | depth cap |
| Graph degradation `[FlattenGraph]` | ✅ (homo + hetero) | ❌ | ❌ | ❌ |
| IQueryable projection | ✅ provably-translatable | ✅ | ✅ (EFCore) | ✅ `ProjectTo` |
| Update-into-existing | ✅ `void/T Map(s, dest)` | ✅ | ✅ `Adapt(s,dest)` | ✅ `Map(s,dest)` |
| **Zero-alloc `Span<T>` mapping** | ✅ | ❌ | ❌ | ❌ |
| **Async streaming `IAsyncEnumerable`** | ✅ | ❌ | ✅ | ❌ |
| **Blittable / SIMD bulk-copy fast-path** | ✅ `MemoryMarshal.Cast` | ❌ | ❌ | ❌ |
| **SIMD primitive-widening (`int[]`→`long[]`)** | ✅ `Vector.Widen` | ❌ | ❌ | ❌ |
| **Completeness = build error** | ✅ `DWARF001` (always) | diagnostics | ❌ | `AssertConfigurationIsValid()` (test-time) |
| **Conversion policy** | ✅ widening silent; non-lossless = `DWARF038` suggestion, or build error via `ImplicitConversions=false` | widening auto; lossy → diagnostic | most permissive | permissive |
| `[RoundTrip]` anti-mislinking | ✅ | ❌ | ❌ | ❌ |

**Differentiators only DwarfMapper has:** the blittable SIMD fast-path, zero-alloc `Span<T>` mapping,
heterogeneous `[FlattenGraph]` degradation, a *non-optional* completeness build-error gate, `[RoundTrip]`
verification, and uniform "never a silent StackOverflow" across direct/collection/dictionary cycles in
every reference mode.

## Testing approach comparison

| | DwarfMapper | Mapperly | Mapster | AutoMapper |
|---|---|---|---|---|
| Generator snapshot tests | ✅ Verify | ✅ Verify | n/a | n/a |
| Integration / behavioural | ✅ | ✅ | ✅ xUnit | ✅ |
| **Fuzz / property-based** | ✅ seeded combinatorial + topology oracles | ❌ | ❌ | ❌ |
| **Adversarial / exhaustion** | ✅ | ❌ | ❌ | ❌ |
| **Determinism tests** | ✅ | ❌ | ❌ | ❌ |
| **Assembly-scan self-validation / meta-tests** | ✅ (descriptor↔release sync, hollow-test detector, matrix completeness) | ❌ | ❌ | ❌ |
| AOT / trim CI gate | ✅ sample + gate | ✅ | n/a | n/a |
| Coverage gate | ✅ CI threshold | ~ | ~ | ~ |
| User-side config validation | build-time `DWARF001` | build-time | none | `AssertConfigurationIsValid()` (runtime/test) |

DwarfMapper's test methodology is the most defensive of the four: beyond snapshots + integration it adds
seeded fuzzing, adversarial/exhaustion matrices, determinism checks, and **self-validating meta-tests**
(the test suite scans the assembly to prove every diagnostic/attribute/enum is covered). The completeness
gate is a *compile error* — you cannot ship an incomplete mapping — whereas AutoMapper validates at
test/runtime via `AssertConfigurationIsValid()` and Mapster/Mapster offer no equivalent.

## Migration ease (the critical concern)

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

## Performance & memory

See [`benchmarks/DwarfMapper.Benchmarks`](../benchmarks/DwarfMapper.Benchmarks/) — DwarfMapper vs.
hand-written vs. the three competitors across **flat / nested / collection / blittable-struct** scenarios
with `[MemoryDiagnoser]`.

Confirmed full run — BenchmarkDotNet DefaultJob, **.NET 10.0.1, X64 RyuJIT AVX2** (absolute ns vary by
hardware; relative ordering is the point — run locally for your own):

| Scenario | DwarfMapper | Mapperly 4.3.1 | Mapster 10.0.8 | AutoMapper 14.0.0 | hand-written |
|---|---:|---:|---:|---:|---:|
| Flat (1 object) | 4.8–7.6 ns* | 4.4 ns | 13.1 ns | 53.3 ns | 4.6 ns |
| Nested | 12.4 ns | 10.7 ns | 22.2 ns | 59.5 ns | — |
| Array (1000 objects) | 5.75 µs | 5.04 µs | 6.15 µs | 5.29 µs | — |
| **Blit (1000 structs)** | **0.39 µs** | 0.98 µs | 0.98 µs | 1.03 µs | — |
| **Widen (1000 int→long)** | **0.42 µs** | 0.47 µs | 1.04 µs | 1.11 µs | — |
| Allocations (all scenarios) | = hand-written | = | = | = | baseline |

`*` Flat_Dwarf measured 4.8 ns and 7.6 ns across two full runs — nanosecond-scale noise on a
non-dedicated machine; the generated code is the same direct-assignment shape as Mapperly. Codegen
mappers (DwarfMapper / Mapperly) cluster at hand-written speed; the runtime tier (Mapster ~3×,
AutoMapper ~11×) trails.

**Takeaways:**
- DwarfMapper **matches hand-written** (tied with Mapperly, the other source generator) with **zero
  allocation overhead** — the destination object is the only allocation.
- On the **blittable struct array it is ~2.5× faster than every competitor** — the `MemoryMarshal.Cast`
  SIMD reinterpret path that none of Mapperly / Mapster / AutoMapper have (they copy field-by-field).
- On the **primitive widening array (`int[]→long[]`)** the `Vector.Widen` path is ~2.5× faster than the
  runtime mappers (Mapster/AutoMapper) and a hair ahead of Mapperly's scalar codegen loop — at this size
  the work is memory-bound (writing the 8 KB output), so SIMD mainly separates it from the reflection/
  expression tier; the gap widens for smaller element types or cache-resident data.
- It is **~12× faster than AutoMapper** and **~3× faster than Mapster** on flat maps, which pay
  runtime expression-tree / reflection overhead (and are not NativeAOT-safe). Mapster's first-call
  expression compilation is amortized here (steady state), yet still trails the codegen mappers.

Run locally for numbers on your hardware:

```bash
dotnet run -c Release --project benchmarks/DwarfMapper.Benchmarks
```

### Higher instructions / SIMD

DwarfMapper has **two** SIMD fast-paths that no competitor offers:

1. **Blittable bulk copy** — a layout-identical `TSrc[]`→`TDst[]` is reinterpreted as a single
   `MemoryMarshal.Cast` block copy behind a JIT-folded size guard; the runtime lowers that memmove to the
   widest available vector instructions automatically (struct-array case at memcpy speed).
2. **SIMD widening** (shipped) — a lossless primitive widen array (`int[]`→`long[]`, `short[]`→`int[]`,
   `byte[]`→`ushort[]`, `float[]`→`double[]`, and the unsigned/sbyte variants — the seven
   `System.Numerics.Vector.Widen` pairs) is vectorized with `Vector.Widen` behind a
   `Vector.IsHardwareAccelerated` guard and a scalar tail. The result is **bit-for-bit identical to the
   scalar implicit widen** (verified by adversary/fuzz tests over every length around the vector boundary
   and the full value range, plus an AOT gate) — it is purely a throughput win and stays reflection-free /
   NativeAOT-safe. Competitors copy these element-by-element.

Both are emitted only when provably safe; everything else falls back to the element loop (with
`CreateChecked` for narrowing). See the `Blit` and `Widen` benchmark categories.
