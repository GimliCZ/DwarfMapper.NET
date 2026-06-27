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

## Performance & memory

See [`benchmarks/DwarfMapper.Benchmarks`](../benchmarks/DwarfMapper.Benchmarks/) — DwarfMapper vs.
hand-written vs. the three competitors across **flat / nested / collection / blittable-struct** scenarios
with `[MemoryDiagnoser]`.

Confirmed full run — BenchmarkDotNet DefaultJob, **.NET 10.0.1, X64 RyuJIT AVX2** (AMD Ryzen 5 5600;
absolute ns vary by hardware; relative ordering is the point — run locally for your own):

| Scenario | DwarfMapper | Mapperly 4.3.1 | Mapster 10.0.8 | AutoMapper 14.0.0 | hand-written |
|---|---:|---:|---:|---:|---:|
| Flat (1 object) | 6.4 ns* | 4.6 ns | 13.2 ns | 51.5 ns | 4.8 ns |
| Nested | 11.5 ns | 10.3 ns | 20.5 ns | 58.4 ns | — |
| Array (1000 objects) | 4.55 µs | 4.47 µs | 5.82 µs | 5.26 µs | — |
| **Blit (1000 structs)** | **0.40 µs** | 0.93 µs | 0.97 µs | 1.02 µs | — |
| **Widen (1000 int→long)** | **0.35 µs** | 0.43 µs | 0.69 µs | 0.72 µs | — |
| Allocations (all scenarios) | = hand-written | = | = | = | baseline |

`*` Flat_Dwarf measured 4.8–7.6 ns across runs (6.4 ns this run) — nanosecond-scale noise on a
non-dedicated machine; the generated code is the same direct-assignment shape as Mapperly. Codegen
mappers (DwarfMapper / Mapperly) cluster at hand-written speed; the runtime tier (Mapster ~2.4–3×,
AutoMapper ~9–11×) trails.

**Takeaways:**
- DwarfMapper **matches hand-written** (tied with Mapperly, the other source generator) with **zero
  allocation overhead** — the destination object is the only allocation. On the 1000-object array it and
  Mapperly co-lead (4.55 µs vs 4.47 µs — within run-to-run noise), both ahead of the runtime mappers.
- On the **blittable struct array it is ~2.3–2.5× faster than every competitor** — the `MemoryMarshal.Cast`
  SIMD reinterpret path that none of Mapperly / Mapster / AutoMapper have (they copy field-by-field).
  (Repeatable steady-state on this machine is ~0.40–0.50 µs vs ~0.9–1.0 µs for the others — decisive, with
  allocations identical across all four libraries.)
- On the **primitive widening array (`int[]→long[]`)** the `Vector.Widen` path is ~2× faster than the
  runtime mappers (Mapster/AutoMapper) and a hair ahead of Mapperly's scalar codegen loop — at this size
  the work is memory-bound (writing the 8 KB output), so SIMD mainly separates it from the reflection/
  expression tier; the gap widens for smaller element types or cache-resident data.
- It is **~9–11× faster than AutoMapper** and **~2.4–3× faster than Mapster** on flat maps, which pay
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

### NativeAOT benchmarking & stability

The BenchmarkDotNet suite above measures the JIT. `samples/DwarfMapper.AotBench` is a separate harness
that is **published with NativeAOT and run as a native binary** — it both times the hot paths under real
AOT codegen and stress-tests for instabilities the JIT can't reveal: SIMD widen/blit bit-exactness at
every size around the vector boundary, Preserve-topology determinism over 100 000 runs, catchable
depth-guard over 20 000 runs, and `OnCycle=SetNull` acyclicity over 50 000 runs.

Results (10.0.1 ILC, win-x64, this machine):

- **No instabilities.** Every correctness/determinism check passes (exit 0); the default `dotnet publish`
  emits **zero** IL2xxx/IL3xxx trim/AOT warnings. SIMD output is **bit-for-bit identical** to the scalar
  reference at all boundary sizes, including negatives (sign extension).
- **AOT timing is *steadier* than the JIT** — no tiered-compilation jitter, so per-op min/max spreads are
  tighter (a stability *positive*).
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

  Verified: with `IlcInstructionSet=native` the AOT binary reports `Vector<int>.Count == 8` and all checks
  still pass. (`x86-x64-v3` is rejected by ILC 10.0.1 — use `native` or an explicit ISA list.)

Run it yourself:

```bash
dotnet publish samples/DwarfMapper.AotBench -c Release -r win-x64        # default baseline SIMD
dotnet publish samples/DwarfMapper.AotBench -c Release -r win-x64 -p:IlcInstructionSet=native   # full-width
./samples/DwarfMapper.AotBench/bin/Release/net10.0/win-x64/publish/DwarfMapper.AotBench.exe
```
