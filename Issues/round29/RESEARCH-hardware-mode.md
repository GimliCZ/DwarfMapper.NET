# Research: extending the hardware fast path beyond struct arrays

**Date:** 2026-09-02 · **Status:** research, no code changed · **Constraints set by the user:** no
uninitialized memory (`GC.AllocateUninitializedArray` is out), no `unsafe` code (pointers, `Unsafe.As`,
non-temporal stores are out). Everything proposed below is safe-API only and keeps the locked design
language: prove the property at generation time, emit the fast path behind a hardware gate, keep the scalar
path as the always-correct oracle.

## 1. The question

The blit is DwarfMapper's one genuine speed differentiator, and today it fires only for `TSrc[]`/`List<TSrc>`
whose ELEMENTS are layout-identical unmanaged structs. Real DTOs are overwhelmingly classes, so the
differentiator rarely fires in a consumer's build. The user's proposals: (a) blit classes; (b) deliberately
declare transfer-model classes as structs. This document measures what each buys and lays out the strategies
that survive the constraints, ranked.

## 2. Where the time actually goes (measured)

Probe: `artifacts/hwprobe` (BenchmarkDotNet 0.14, ShortRun, .NET 10.0.101, 12-core Windows, noisy machine —
ratios are the signal, not the absolute nanoseconds). Shapes mirror the repo benchmarks: a 12-byte `Vec3`
struct pair, and a five-field unmanaged DTO (`int, long, bool, float, double`; 32-byte object body / 32-byte
struct).

### 2a. Five fields, four storages (N elements)

| Emission | N = 1,000 | N = 100,000 | N = 1,000,000 | Memory (1k) |
|---|---:|---:|---:|---:|
| class[] → class[] field copy (what every mapper emits) | 5.25 µs (1.00) | 4.32 ms (1.00) | 52.1 ms (1.00) | 54.7 KB |
| class[] → class[] **per-object body memcpy** (unsafe, excluded) | 5.15 µs (0.98) | 4.40 ms (1.02) | 56.4 ms (1.08) | 54.7 KB |
| class[] → **struct[] gather** (one allocation, plain field copy) | 2.34 µs (**0.45**) | 0.47 ms (**0.11**) | 4.4 ms (**0.08**) | 31.3 KB (0.57) |
| struct[] → struct[] blit (`MemoryMarshal.Cast` + `CopyTo`) | 0.96 µs (**0.18**) | 0.35 ms (**0.08**) | 2.1 ms (**0.04**) | 31.3 KB (0.57) |
| struct[] → class[] unpack | 6.01 µs (1.15) | 4.38 ms (1.01) | 55.0 ms (1.06) | 54.7 KB |

Reading: **the copy is not the cost; the destination storage is.** Replacing five field assignments by one
32-byte memcpy per object changes nothing (within noise), because a class destination costs one heap object
per element — allocation, zeroing, header, GC promotion (note Gen1/Gen2 collections appear at 100k and
1M only for the class rows). The moment the destination is a struct array, the whole output is ONE
allocation and the field copy runs at 2 ns/element; from 100k up the class rows fall 9–12× behind the gather
and 12–25× behind the blit. The repo's own benchmark says the same thing in different clothes:
`Array_Dwarf` (1000 classes) 4.9 µs vs `Blit_Dwarf` (1000 structs) 0.42 µs.

### 2b. The struct blit itself, and what the excluded techniques would have bought

| Variant (Vec3, 12 B) | N = 1,000 | N = 100,000 | N = 1,000,000 |
|---|---:|---:|---:|
| `new T[n]` + `MemoryMarshal.Cast(...).CopyTo` (today) | 583 ns (1.00) | 242 µs (1.00) | 1.41 ms (1.00) |
| uninitialized array (**excluded by policy**) | 321 ns (0.56) | 199 µs (0.82) | 0.84 ms (0.59) |
| uninitialized + AVX non-temporal stores (**excluded**: unsafe) | 836 ns (1.46) | 133 µs (0.55) | 0.75 ms (0.53) |
| uninitialized + `Parallel.For` chunks (safe half of it) | 567 ns (0.99) | 192 µs (0.79) | 1.04 ms (0.73) |

The zeroing of the destination is ~40 % of a blit's cost at every size; the policy pays that, knowingly, for
a `new T[]` that can never hand a consumer a byte it did not write. Non-temporal stores only pay above the
cache and need pointers. Parallel chunking is safe and worth 20–27 % from ~100k elements; at 1k it is
neutral, which is exactly why it must sit behind a size threshold.

## 3. Why "blittable classes" is not the lever (the physics)

A class instance is `[object header][MethodTable*][fields]` on the heap, one per element. A collection of
class elements cannot be moved as one block: the destination elements do not exist until allocated, each
allocation is a runtime call plus zeroing, and the references in the destination array are written with GC
write barriers. What CAN be blitted is the field block inside one object, and only when the layout is
guaranteed — the CLR honours `[StructLayout(Sequential)]` on a class only when it has no reference fields,
auto-layout classes carry no layout contract at all, and the copy needs `Unsafe.As` + `CopyBlock`. Measured
gain: nil (row 2 above), because the copy was never the cost. Classes with string members are out for a
second reason: a reference field copied by memcpy skips the write barrier, which is a GC correctness bug,
not a performance choice. So: **class blit is possible, unsafe, and buys ~2 %.** Not an option, as the user
suspected — and now for a measured reason rather than an assumed one.

## 4. Strategies that survive the constraints, ranked

### S1 — Transfer models as structs (the user's idea; the big lever)
The only change that moves a consumer's numbers by an order of magnitude is the DESTINATION TYPE of collection
elements: a struct DTO turns `List<Order> → OrderDto[]` into one allocation (2.2× at 1k, 9–12× at ≥100k,
−43 % memory), and a struct pair on both sides into the blit (5.5–25×). The generator already supports every
struct shape as a map target (readonly struct, positional `record struct`, `readonly record struct`,
constructor-bound; `NonTrivialShapeRuntimeTests`, `ConstructorMappingRuntimeTests`). What is missing is the
NUDGE at the site where it matters and the safety net around the semantic change. Proposed:

- **A mapping-site diagnostic (Info), not a type-level one.** Fire only where a collection of class elements
  maps to a collection of class elements AND the target element is *transfer-model shaped*: sealed, no
  base class, no explicit constructor logic / behaviour, every member auto-property or field, size of the
  would-be struct ≤ ~64 B, no member of a type that must be a class (no `IDisposable`, no events), not an EF
  entity (no `DbSet` usage / no key attribute), and the pair is field-compatible. Message names the pair, the
  measured class of gain ("one allocation instead of N; if both sides are structs, a block copy"), and the
  fix. A type-level analyzer would flood every DTO in a solution; the mapping site is where the cost is paid.
- **A code fix: "Convert to `readonly record struct`".** Rewrites the declaration, keeps `init`/`get`
  properties and initializers, drops `sealed`, and deliberately does NOT touch usages: every `null` check,
  `== null` assignment and reference-identity use turns into a compile error, which is the loud signal this
  project prefers to a silent semantic change. The hazards a struct brings, and why loud is right:
  `list[i].X = v` is CS1612 (value copy), `default` instead of `null`, `Dto?` becomes `Nullable<Dto>` and
  boxes through `object`, interfaces box, two variables no longer alias, large structs copy on every pass
  (hence the ≤64 B rule and `in` parameters), equality becomes value equality (a *feature* for a DTO).
- **Generator side, already in place:** gather path for class→struct elements, blit for struct→struct,
  constructor binding for positional records. No emission change needed for S1 to pay.

Expected effect on the repo benchmark if `FlatDst`/`NestedDst` were structs: `Array`/`List`/`Seq` drop
from ~5 µs to ~1–2 µs at N = 1000; the string member keeps them off the blit (a struct with a reference field
is not unmanaged), which is fine — the gather already carries most of the gain.

### S2 — Span mapping should blit (cheap, safe, unshipped today)
`Map(ReadOnlySpan<S> src, Span<D> dst)` is documented as the zero-alloc hot path, and the emitter ALWAYS
writes the element loop, even for a pair the proof accepts. For a layout-identical unmanaged pair it should
emit `MemoryMarshal.Cast<S, D>(src).CopyTo(dst)` (safe, no allocation at all, the fastest path the runtime
offers) with the same proof and the same `DWARF100` near-miss explanation. This is the one place where the
user's "hardware mode" needs nothing new: zero allocation + block copy.

### S3 — Homogeneous-struct SIMD: widen and permute without unsafe
Today `Vector.Widen` fires for `int[]→long[]` and the six sibling primitive pairs only. Two extensions keep
every property of the current path (proof at generation time, `IsHardwareAccelerated` gate, scalar tail as
the oracle) and stay in safe APIs:
- **Widen inside structs.** `Vec3<float>[] → Vec3<double>[]` where both structs are unmanaged, sequential,
  and every field is the SAME primitive in the same order: reinterpret each array as its primitive span
  (`MemoryMarshal.Cast<Vec3f, float>`, safe), widen the flat span, and it is the destination's bytes. Same
  for `short→int`, `int→long`, `float→double`, `enum→underlying`. Gain: the existing widen ratio (~2× vs
  scalar at 1000).
- **Field-permuted blit.** Same size, same primitive, different field order (`XYZ → ZYX`, or a `Color` with
  `RGBA` vs `BGRA`): `Vector128.Shuffle` over the flat span with a proof-derived control vector, scalar tail.
  Gain: likely 2–3× vs the scalar loop (unmeasured; measure before promising). This also turns a class of
  `[Reinterpret]` refusals into an automatic path.

### S4 — Chunked parallel blit and gather above a threshold (safe, opt-in)
`Parallel.For` over `MemoryMarshal.Cast(...).CopyTo` chunks: −21 % at 100k, −27 % at 1M, neutral at 1k
(table 2b). For the gather (class→struct) the loop is compute-bound and should scale better than the
bandwidth-bound blit. Threading inside a mapper is a semantic decision (thread-pool use, exceptions,
cancellation), so: opt-in per mapper (`[DwarfMapper(ParallelThreshold = 100_000)]`), default off, never in
Preserve/SetNull mode (the identity context is not thread-safe), only for the blit and the pure gather.

### S5 — Memory: keep the output off the LOH and reuse it
Arrays ≥ 85,000 B (≥ 7,083 `Vec3`, ≥ 2,656 32-byte DTOs) land on the LOH; the probe's Gen2 columns at 100k
are that. Two safe answers already half-exist: the span map (S2) into a caller-owned or `ArrayPool` buffer,
and update-into-existing for object graphs. What is missing is documentation that says so in the performance
section, and a `Map(source, Span<TDto>)` overload guidance snippet with `ArrayPool<TDto>.Shared`.

### S6 — Struct layout hygiene (memory, and more pairs on the fast path)
A DTO declared `bool, long, int, double` is 32 B with 11 B of padding; declared `long, double, int, bool` it
is 24 B. An Info diagnostic on transfer-model structs whose field order pads more than a threshold ("reorder
to save N bytes per element") shrinks arrays and cache traffic, and pairs that differ only by order become
S3's permuted blit instead of a refusal.

### S7 — What NOT to do (measured or reasoned, kept for the record)
- Uninitialized arrays: −41 to −44 % on the blit; excluded by policy (a consumer must never see unwritten bytes).
- Non-temporal stores: −45 % above the cache, +46 % at 1k; excluded (unsafe), and only large arrays benefit.
- Class body memcpy / `Unsafe.As` reinterpretation of instances: ~0 % and unsafe; the latter also violates GC
  type identity.
- Object pooling of destination classes: reuses memory but not the copy or the header writes, and gives the
  mapper ownership semantics it must not have.

## 5. Recommendation and order

1. **S2 span-map blit** — small emitter change, existing proof, existing near-miss diagnostic, exact-pin
   benchmark row to add. Zero-allocation AND block copy in one path.
2. **S1 mapping-site diagnostic + "convert to readonly record struct" code fix** — the real lever; ships with
   the hazard list in `docs/diagnostics.md` and a benchmark pair (`Array` with struct destination) so the claim
   is a measured number, not a promise.
3. **S3 homogeneous-struct widen** (then permuted blit, after measuring).
4. **S4 parallel threshold**, opt-in, last.
5. Documentation: the performance section should say plainly that the fast path is a property of the
   STORAGE — "if your transfer model is a struct, DwarfMapper moves it as bytes; if it is a class, every
   mapper including this one pays one allocation per element" — because that is the truthful version of the
   differentiator and the one consumers can act on.

Every item above keeps the rule: prove at generation time, gate on hardware, scalar path stays the oracle,
`allocation-baseline.json` pins move only with a re-measure in the same commit.

## 6. Open questions for the user
- Threshold for "transfer model" size: ≤64 B by value, or allow larger with `in` parameters?
- Should the code fix produce `readonly record struct` (value equality, `with`) or a plain `readonly struct`
  (no equality surface, smaller metadata)?
- Is S4's parallelism wanted at all in a library that otherwise never touches the thread pool?

## 7. External knowledge (web pass, 2026-09-02) and how it changes the plan

Sources are listed at the end; each bullet says what it changes.

- **`TensorPrimitives.ConvertChecked/ConvertSaturating/ConvertTruncating<TFrom,TTo>(ReadOnlySpan, Span)`
  (System.Numerics.Tensors, .NET 9+) is a safe, vectorized, generic span conversion.** Its `TryConvertUniversal`
  vectorizes exactly these pairs with `Vector128/256/512.Widen`/`Narrow`/`ConvertTo*`: byte→ushort/short,
  byte→(u)int, byte→float, sbyte→short, ushort→(u)int, short→int, uint→(u)long, int→long, Half→float,
  float→double (widen); (u)int→float, (u)long→double (same-size); double→float, float→Half (narrow);
  identity → `CopyTo`; every other pair falls to a scalar loop. **Changes S3:** the generator does not need
  its own `Vector.Widen` loops for new pairs — for a homogeneous struct pair it can `MemoryMarshal.Cast` both
  arrays to their primitive spans and call one `TensorPrimitives` conversion (safe API, AVX-512 aware, scalar
  tail handled by the library). The existing hand-rolled widen stays for the pairs it already serves (it is
  pinned and measured); the new pairs above (byte/ushort/short/uint/Half, the same-size int→float and
  long→double, and the narrowing float←double) come for free behind the same proof. Package cost: a
  reference to `System.Numerics.Tensors` in the consumer — a declared dependency, or an opt-in emit mode.
- **Safe vector loads/stores are the runtime team's own guidance:** "`Vector128.Create(span)` and `CopyTo`
  are the simplest way to move data between a span and a vector, the JIT keeps them efficient, and they need
  no pinning or reference arithmetic"; reach for `LoadUnsafe` "only when you genuinely must walk a buffer by
  managed reference on a measured hot path"; remainder handling is "the most common source of bugs in
  vectorized code" and needs tests for lengths that are not a multiple of the vector width. **Changes S3's
  permuted blit:** `Vector128.Create(ReadOnlySpan)` + `Vector128.Shuffle` + `CopyTo(Span)` is fully within
  the no-unsafe policy; and the mutation/fuzz corpus must include non-multiple lengths for every new path
  (the existing widen path's scalar tail is the template).
- **.NET 10 escape analysis** stack-allocates small arrays, delegates, and foreach enumerators that provably
  do not escape, and spans over them. **Does NOT change the picture for mappers:** a mapped destination
  escapes by definition (it is returned), so class DTO allocations stay on the heap. It does mean a
  `Span`-shaped intermediate the generator might allocate internally (a `stackalloc`-sized temp) is free now.
- **`GC.AllocateUninitializedArray` semantics** (for the record, excluded by policy): it silently falls back
  to a zeroed `new[]` for arrays under 2,048 bytes and for reference-element arrays, and its benefit is
  "only material for large arrays". So even if the policy changed, the sub-2 KB cases would gain nothing;
  the probe's 44 % at 1k came from a 12 KB array, above that threshold.
- **LOH threshold is 85,000 bytes.** Confirms S5: 7,083 `Vec3` or 2,656 32-byte DTOs per array is the line
  above which every mapped output is a Gen2 object; the span-map-into-pooled-buffer path is the answer.
- **Framework Design Guidelines** (2008 text, reprinted with an out-of-date warning): "AVOID defining a
  struct unless it logically represents a single value, has an instance size under 16 bytes, is immutable,
  and will not have to be boxed frequently" — and, on the same page, "value type arrays are allocated
  inline … allocations and deallocations of value type arrays are much cheaper … much better locality of
  reference." Measured counter-evidence: a 32-byte struct at 14.6 ns vs class at 21.2 ns (144 B), a 64-byte
  struct 26.4 vs 29.9 ns (240 B), with degradation setting in around 40 B for by-value passing.
  **Changes S1's threshold:** the transfer-model rule should not be "≤16 B" (that guideline is about
  single-value semantics and by-value passing, not array storage); for collection elements the win holds to
  ~32–64 B and the cost above that is copy-on-pass, mitigated by `in` parameters and `ref readonly` access.
  Proposal: warn (not refuse) above 64 B; suggest `in` in the diagnostic text.
- **Struct DTOs and the frameworks around them — the hazards to name in the diagnostic help page:**
  System.Text.Json deserializes readonly (record) structs with a parameterized constructor only with
  `[JsonConstructor]` (issues #82929, #94443: properties silently default otherwise); EF Core supports value
  types as *complex types* (EF 8+) but not as entities, "collections of value types (struct) are not
  currently supported" for complex collections, and `SqlQuery<T>` requires a reference type. So the code fix
  must never touch a type that is an EF entity, and the help page must say: transfer models on the wire and
  in memory, yes; persistence models, no.
- **Data-oriented layout (AoS vs SoA):** SoA gains ~30 % on field-wise scans and feeds SIMD directly, but a
  mapper's output is consumed as whole records, so AoS (a struct array) is the right target; SoA is a
  consumer-side choice. **No change** to the plan, noted so nobody proposes an SoA emit mode.
- **Refactoring tooling:** Roslyn has an open request for "convert type to record" (#62623) and a
  properties-to-positional-parameters refactoring (#47598); nothing ships that converts a class to a
  `record struct` with the null/aliasing audit. **Confirms S1's code fix is not duplicating an IDE feature.**
- **Mapperly** maps records and init-only types through constructors and allocates "only the destination
  object"; it has no bulk copy and no struct-array path. **Confirms the differentiator is real** — and that
  the honest claim is about storage, which any consumer can change today.
- **Memory bandwidth:** a single core does not saturate DRAM on desktop parts (~20 GB/s practical, threads
  raise copy throughput); confirms S4's 20–27 % for large arrays and its ceiling — parallelism helps the
  bandwidth-bound blit less than the compute-bound gather.

### Plan adjustments after the web pass
1. S3 becomes "**TensorPrimitives-backed conversions**": homogeneous struct widen/narrow/same-size pairs via
   `MemoryMarshal.Cast` + `TensorPrimitives.ConvertChecked` (or `ConvertTruncating` where the pair is a
   documented reinterpret), opt-in dependency. Keep the hand-rolled `Vector.Widen` for the shipped pairs.
2. S1's size rule: ≤32 B silent, 32–64 B Info, >64 B suggest `in`; never on EF entity types; the help page
   lists the System.Text.Json `[JsonConstructor]` requirement.
3. S3's permuted blit: safe `Vector128.Create(span)/Shuffle/CopyTo`, with the non-multiple-length corpus rule.
4. Nothing new for classes: escape analysis does not reach a returned object graph.

### Sources
- TensorPrimitives conversion helpers (vectorized pairs): https://source.dot.net/System.Numerics.Tensors/System/Numerics/Tensors/netcore/TensorPrimitives.ConvertHelpers.cs.html · ConvertSaturating docs: https://learn.microsoft.com/dotnet/api/system.numerics.tensors.tensorprimitives.convertsaturating
- Runtime vectorization guidelines: https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/vectorization-guidelines.md
- Performance Improvements in .NET 10 (escape analysis, stack allocation): https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/
- GC.AllocateUninitializedArray: https://learn.microsoft.com/dotnet/api/system.gc.allocateuninitializedarray · https://github.com/dotnet/runtime/discussions/47198 · https://blog.ladeak.net/posts/gc-allocate-uninitialized
- Large object heap: https://learn.microsoft.com/dotnet/standard/garbage-collection/large-object-heap
- Framework Design Guidelines, class vs struct: https://learn.microsoft.com/dotnet/standard/design-guidelines/choosing-between-class-and-struct
- Struct-size measurements: https://github.com/angryflaren/csharp-struct-vs-class-performance · https://perfaddict.net/struct-vs-class/
- System.Text.Json readonly struct issues: https://github.com/dotnet/runtime/issues/82929 · https://github.com/dotnet/runtime/issues/94443
- EF Core complex types / value types: https://learn.microsoft.com/ef/core/modeling/complex-types · https://github.com/dotnet/efcore/issues/35877
- AoS/SoA: https://en.wikipedia.org/wiki/AoS_and_SoA · https://hwisnu.bearblog.dev/array-of-structs-and-struct-of-arrays/
- Roslyn refactoring requests: https://github.com/dotnet/roslyn/issues/62623 · https://github.com/dotnet/roslyn/issues/47598
- Mapperly: https://mapperly.riok.app/docs/intro/ · https://the-runtime.dev/articles/mapperly-source-generated-object-mapping/
- Vector128.Shuffle / Vector256.Create(span): https://learn.microsoft.com/dotnet/api/system.runtime.intrinsics.vector128.shuffle · https://learn.microsoft.com/dotnet/api/system.runtime.intrinsics.vector256.create

## 8. User feedback (2026-09-02) and the resulting design direction

Three rulings from the user after reading sections 1–7: (a) DTOs usually carry nested complex class members,
so decomposing a complex DTO class INTO structs (the whole graph, not just the root) is where the advantage
is; (b) the size thresholds (≤32 B silent, 32–64 B Info, >64 B suggest `in`) are reasonable; (c) parallel
copying depends too much on the machine to be a default — and "transposing in memory, then SIMD" is
generally the better option than element loops. Consequences:

### 8a. Decomposing a DTO graph into value layout

A transfer model is a TREE of transfer models: `OrderDto { Header, Address Ship, Address Bill, Money Total }`.
As classes that is four allocations per order; as nested structs it is ONE inline block, and if every leaf is
unmanaged the whole `OrderDto[]` blits. The proof already walks nested structs recursively ("including nested
structs and fixed-buffer lengths, recursively"), so the generator side needs no new proof for the 1:1 case.
What decomposition means member by member:

| Member kind in the class graph | Value-layout form | Blit status of the root | Notes |
|---|---|---|---|
| 1:1 nested transfer-model class (`Address Ship`) | inline nested struct | keeps it | the common case; the code fix recurses through it |
| optional nested (`Address? Ship`) | `Address?` = `Nullable<AddressStruct>` (hasValue + value, inline) | keeps it IF the proof learns `Nullable<TSrc>` vs `Nullable<TDst>` over layout-identical `T` | today the proof stops at the outer type; a small, provable extension (same layout: 1-byte flag, same padding rule on both sides) |
| primitives, enums, `Guid`, `DateTime`, `decimal`, fixed buffers, `[InlineArray(n)]` | as-is | keeps it | all unmanaged |
| `string` and other reference leaves | stays a reference field | loses the ROOT blit; the root still takes the one-allocation **gather** | the honest limit: a string is a heap object by definition. Opt-in later: an index into a per-map string table (columnar), which is a different feature |
| 1:N nested collection (`List<LineDto> Lines`) | stays a reference to `LineStruct[]` (a struct cannot inline a variable-length collection; `[InlineArray(n)]` only for fixed n) | loses the ROOT blit; the collection member blits ON ITS OWN (already the case today), the root gathers | two allocations per order instead of 2 + N |
| polymorphic member (`Shape` base with derived) | cannot be a struct | refused at the type | the code fix stops and names the member |
| identity-bearing / mutable-shared / entity | must stay a class | refused | EF entities, anything with events, `IDisposable`, reference identity |

So "decompose" is a transitive code fix over the transfer-model subgraph reachable from a mapped collection
element, with a per-member verdict table like the one above in the diagnostic's message, and the same loud
policy for usages (null checks, aliasing, `list[i].X = v`). The measured expectation from section 2 scales
with the number of nested classes per element: K allocations per element collapse to 1 (gather) or 0 per
element (blit), so a four-class DTO gains MORE than the flat probe showed, not less.

Two concrete generator extensions fall out of the table, both provable at generation time:
1. **`Nullable<T>` pairs in the proof** — `Nullable<TSrc>` ≡ `Nullable<TDst>` when `TSrc` ≡ `TDst`
   (layout-identical), so an optional nested struct does not break the root blit. Small; near-miss text
   for `DWARF100` names it.
2. **Per-member blit inside a gather** — already the behaviour for collection members; the decomposed root
   simply makes more members qualify. Nothing to build; a test to pin.

### 8b. "Transpose in memory, then SIMD" as the standing rule for non-identical layouts

The user's ruling: when two layouts are NOT identical, prefer rearranging bytes with vector instructions
over a scalar per-element loop. Within the no-unsafe policy this is exactly the `MemoryMarshal.Cast` +
`Vector128.Create(span)` / `Vector128.Shuffle` / `CopyTo(span)` family, and it covers more than section 4's
S3 assumed:

- **Any field permutation or padding change of a struct ≤ 16 B is one byte-granular `Vector128.Shuffle`**
  (`pshufb`): the control vector is derived from the two layouts at generation time (source byte offset of
  every destination byte, `0xFF` for padding). Mixed field types do not matter at byte granularity. A 12-byte
  struct does not divide 16, so the pattern repeats every `lcm(size, 16)` bytes — three control vectors for
  12-byte structs, cycled over the flat byte span; scalar tail for the remainder. Proof obligations: both
  unmanaged, sequential, every destination byte sourced from exactly one source byte of the same primitive
  width (no cross-width reinterpretation), same-bytes for every field — the existing near-miss machinery
  already has all of it except the offset map.
- **Width changes (int→long, float→double, byte→ushort) are a transpose + `TensorPrimitives` conversion**:
  reinterpret both arrays as primitive spans and convert (section 7). When a struct mixes widths, split by
  field: gather each field's column into a rented primitive buffer (strided copy), convert the column with
  `TensorPrimitives`, scatter back — the classic AoS→SoA→AoS transpose. Whether the two extra passes beat
  the scalar loop depends on struct size and N; it must be measured per shape class before it is emitted,
  and the generator should only take it when the proof shows every field is convertible by a vectorized
  pair (table in section 7). Where a field is not (e.g. a `decimal`), the scalar loop stays.
- **32-byte structs**: `Vector256.Shuffle` shuffles within 128-bit lanes on AVX2 (`Avx2.Shuffle` semantics
  differ from the cross-platform `Vector256.Shuffle`, which is lane-agnostic but may lower to slower code);
  measure before choosing the width. `Vector512` is not worth a separate path for a mapper.
- **Parallelism is demoted** to "not planned": machine-dependent, and the user does not want the mapper's
  behaviour to hinge on core count. The one place it might return is behind an explicit consumer call
  (`MapParallel`), never implicitly.

### 8c. Revised order
1. S2 span-map blit (unchanged).
2. S1 + 8a: the mapping-site diagnostic and the **transitive** "decompose to readonly record structs" code fix,
   with the verdict table, thresholds ≤32 / ≤64 / `in`, EF/serializer hazards in the help page, and the
   `Nullable<T>` proof extension so optional nested members keep the blit.
3. 8b permuted blit for ≤16-byte structs (byte shuffle), then the `TensorPrimitives` width-change path,
   each measured against the scalar loop it replaces and gated by the same proof + hardware check.
4. AoS→SoA→AoS column conversion for mixed-width structs: prototype and measure first; emit only if it wins
   for the common sizes.
5. Parallelism: dropped from the plan.

## 9. Simulation of the plan (2026-09-02, `artifacts/hwprobe/PlanProbe.cs`, results in `plan-results.md`)

Every strategy from sections 4–8 written with the safe APIs it would ship with, each fast variant checked
against its scalar twin at startup (all 30 checks passed), BenchmarkDotNet ShortRun, N = 1,000 and 100,000.
Ratios are against the scalar loop the strategy would replace.

| Strategy | Shape | N = 1,000 | N = 100,000 | Verdict |
|---|---|---:|---:|---|
| **8a decomposition**: 4-class DTO tree → nested structs, gather | `OrderC{Header,Ship,Bill,Total}` → `OrderS` | **0.30×** (22.1 → 6.5 µs), memory 184 → 64 KB | **0.09×** (20.0 → 1.8 ms), 18.4 → 6.4 MB | **confirmed, the lever** |
| same, with a `string` leaf in the root (gather only) | `OrderStrS` | 0.21× | 0.07× | confirmed: a reference leaf costs the root blit, not the win |
| nested structs both sides, blit | `OrderS` → `OrderSDto` | **0.14×** (3.1 µs) | **0.05×** (0.92 ms) | confirmed |
| nested structs both sides, scalar copy | | 0.36× | 0.08× | the blit is 2.5× / 1.7× over the scalar struct copy |
| **S2 span-map blit** | `Map(ReadOnlySpan<V3S>, Span<V3S>)` | **0.21×** (691 → 145 ns) | 0.98× | confirmed while the buffer is cache-resident; bandwidth-bound above |
| **S3 width change via `TensorPrimitives`** | `V3<float>[]` → `V3<double>[]` | **0.60×** (1.43 → 0.86 µs) | 0.97× | confirmed for cache-resident sizes |
| **8b permuted blit, 16-byte struct** | `XYZW` → `WZYX`, one byte shuffle | 0.77× | 0.95× | marginal: keep only if a ratio gate holds |
| **8b permuted blit, 12-byte struct** | `XYZ` → `ZYX`, three-window shuffle | **2.21× slower** | 1.35× slower | **rejected** |
| **8b column transpose for mixed widths** | `{int,float,int,float}` → `{long,double,long,double}` | **2.93× slower** | 1.82× slower | **rejected** |

### What the simulation says

1. **Decomposition is confirmed and stronger than the flat probe predicted.** A four-class DTO tree is 3.3×
   faster and 2.9× smaller as nested structs at 1k, 11× faster at 100k; as a struct pair on both sides the
   blit is 7× / 22×. The string-leaf variant shows the honest limit: the root stops blitting but keeps the
   one-allocation gather, and the gain is still 5× / 14×. This is the feature to build.
2. **"Transpose in memory, then SIMD" does not beat the JIT's scalar loop when the scalar loop is already a
   straight field copy.** The 12-byte shuffle (three loads, three shuffles, two ORs per 16 output bytes) is
   2.2× slower than three float moves; the column transpose pays two extra passes over memory and loses 2.9×.
   The rule that survives is narrower: SIMD wins when it replaces per-element WORK (a widening conversion:
   1.7×) or when the whole block is a pure move (the blit: 5× at cache-resident sizes); it loses when it
   replaces a copy the JIT already emits as two or three register moves. The 16-byte single-shuffle case is
   the only permutation that pays, and only 23 % at 1k — not enough to justify a new emission path with its
   own proof, near-miss text and mutation surface. **8b is dropped except as a measured, gated future item
   for ≥16-byte layouts.**
3. **Everything converges to memory bandwidth above ~100k elements** (both scalar and vector variants write
   at ~17–20 GB/s), so the SIMD paths are a small-to-medium-N story (the common DTO case: tens to thousands
   of elements). Decomposition is the exception because it removes allocations, not instructions: 11× holds
   at 100k and the earlier flat probe showed 12× at 1M.
4. **The span-map blit is a 5× win for the exact scenario it is documented for** (hot path, small
   caller-owned buffer) and neutral for bulk; ship it.

### Revised plan (final)
1. S2 span-map blit (proof + `MemoryMarshal.Cast` + `CopyTo`, near-miss text, allocation pin row).
2. S1/8a: mapping-site Info diagnostic + transitive "decompose to readonly record structs" code fix, with the
   verdict table, 32/64 B thresholds and `in`, the `Nullable<T>` proof extension, EF/JSON hazards in the help
   page, and a benchmark row that pins the DTO-tree gain so the claim is a number.
3. S3: `TensorPrimitives`-backed conversions for homogeneous structs (opt-in dependency), for the vectorized
   pairs in section 7.
4. Not planned: 12-byte and mixed-width shuffles/transposes (measured losers), parallel chunking
   (machine-dependent), any unsafe or uninitialized-memory path (policy).

### 9b. Small-N sweep (1, 10, 100, 1,000; `plan-results-small.md`, all self-checks passed)

Ratios against the scalar loop of the same shape, absolute time of the scalar baseline in brackets.

| Strategy | N = 1 | N = 10 | N = 100 | N = 1,000 |
|---|---:|---:|---:|---:|
| DTO tree → nested structs, gather | **0.32×** (33 ns) | **0.37×** (191 ns) | **0.35×** (1.79 µs) | **0.40×** (20.4 µs) |
| … with a `string` leaf | 0.27× | 0.24× | 0.27× | 0.29× |
| nested structs both sides, blit | **0.21×** | **0.19×** | **0.15×** | **0.20×** |
| nested structs both sides, scalar copy | 0.32× | 0.55× | 0.47× | 0.44× |
| span-map blit | 1.66× (1.1 ns) | **0.30×** | **0.17×** | **0.20×** |
| `TensorPrimitives` widen in struct | 1.20× (4.5 ns) | 0.80× | **0.58×** | **0.66×** |
| permuted blit, 16-byte, one shuffle | 0.74× | 0.86× | 1.12× | 0.79× |
| permuted blit, 12-byte, three windows | 1.74× slower | 1.37× | 2.15× | 2.24× |
| mixed-width column transpose | 10.5× slower | 4.4× | 2.9× | 2.7× |

Reading:
- **Decomposition wins at every size, including a single element**: one `OrderDto` as four classes costs
  33 ns and 176 B; as nested structs 10.6 ns and 48 B (the allocation count, not the copy, again). The
  memory ratio is 0.42× at N = 1 (one array header amortised over one element) and 0.35× from 10 up.
- **The blit's advantage over the scalar struct copy is stable at 1.5–2.5× from N = 1**, so a proof-gated
  block copy has no size threshold to tune.
- **Span-map blit and `TensorPrimitives` cross over between N = 1 and N = 10**: at a single element the
  call overhead (a `MemoryMarshal.Cast` + `CopyTo`/library dispatch) costs 0.7–0.9 ns more than the inline
  copy; from 10 elements they win (3.3× and 1.25×), from 100 they are at their full ratio. Emission rule:
  these two paths carry no size guard in the generated code (a single-element hot path loses under a
  nanosecond, a guard would cost about the same), but the documentation quotes the N ≥ 10 figure.
- **The rejected paths are worse at small N, not better** (a 12-byte shuffle 1.7–2.2× slower, the transpose
  10× slower at N = 1 because of the pool rentals). Confirms dropping them.
- Noise note: ShortRun on a loaded machine; the 16-byte shuffle flips between 0.74× and 1.12× across sizes,
  which is exactly the "marginal, not worth a path" verdict from 9.

### 9c. Absolute speeds (per-element cost, three regimes; ShortRun, loaded machine, +-15 %)

| Emission (DTO tree, 48 B payload/elem) | N = 1 | N = 1,000 | N = 100,000 |
|---|---:|---:|---:|
| classes -> classes | 33 ns | 20 ns/elem (22 us) | 200 ns/elem (20 ms) - GC promotion dominates |
| classes -> nested structs (gather) | 10.6 ns | 6.5 ns/elem | 18 ns/elem (1.8 ms) |
| nested structs -> nested structs (blit) | 6.8 ns | 3.1 ns/elem | 9.2 ns/elem (0.92 ms) |

Cost floors on this machine: a 3-4 field class allocation ~5 ns (2 ns of it the copy); scalar copy of a
12-16 B struct 0.7-1.5 ns; cache-resident block copy without allocation (span-map blit, 12 B) 0.14 ns
(~86 GB/s); block copy including `new T[]` + zeroing (Vec3 blit at 1k) 0.58 ns (~20 GB/s);
TensorPrimitives float->double in a struct 0.87 ns vs 1.32 scalar; anything writing more than a few MB
runs at ~17-20 GB/s whatever the code. Consequences: the span-map blit is the only path at the copy floor
(no allocation); every allocating path spends most of its time in `new T[]` + zeroing (the policy cost);
above a few MB all emissions tie at bandwidth and only allocation-count wins survive.

## 10. Research batch 2 — the open pathways after decomposition (`PlanProbe2.cs`, `plan2-results.md`)

All self-checks passed. Ratios against the class-shaped baseline of each category; N = 1,000 / 100,000.

| Pathway | 1,000 | 100,000 | Memory | Verdict |
|---|---:|---:|---:|---|
| **A. optional nested member** `Address? Ship` — classes → structs gather | 0.30× | 0.11× | 0.46× | confirmed |
| A. `Nullable<AddressS>` inside the root, struct → struct **blit** | **0.15×** | **0.06×** | 0.46× | confirmed: `Nullable<T>` is `{bool hasValue; T value}`, sequential, unmanaged when `T` is — same `T` on both sides blits bit-for-bit (self-checked). The proof extension is sound. |
| **B. 1:N member** `List<Line>` per order — per-order `LineS[]` | 0.30× | 0.31× | 0.41× | confirmed |
| B. **arena**: one `LineS[]` for all orders + `(offset, count)` per order | 0.29× | **0.10×** | **0.32×** | at scale 3× better than per-order arrays: two allocations instead of N+1, no per-order fragments, sequential locality |
| **C. reference leaf in a struct array** (`string Name`) vs an id | gather 1.55× slower | 1.33× slower | 1.5× more | a reference field costs a GC write barrier per element store (~0.7 ns) and keeps the array GC-scannable; the GC.Collect twin measurement was inconclusive (both fixtures alive in both arms — see the file comment) |
| **D. layout hygiene** 40 B padded vs 24 B packed, blit | **0.57×** | 0.62× | 0.60× | reordering fields is a free 40 % on the blit and on memory |
| D. padded source → packed destination, scalar | 1.04× | 0.67× | 0.60× | one packed side already saves memory bandwidth at scale; both packed unlocks the blit |

What this adds to the plan:
1. **`Nullable<T>` proof extension is justified**: optional nested members keep the root blit at 0.06–0.15×.
2. **1:N decomposition has a better shape than "struct + array per parent"**: the arena. A transfer model
   with a collection member can be emitted as `(offset, count)` ranges into a single element array owned by
   the mapping result — the FlatBuffers / Cap'n Proto / rkyv relative-offset idea applied to an in-memory
   DTO (see section 11). Costs: the DTO no longer carries its collection by reference (a consumer reads
   `result.Lines.AsSpan(order.LineOffset, order.LineCount)`), so it is an opt-in *result shape*, not a drop-in
   for `List<LineDto>`. Gain: 10× at 100k and 3.1× less memory than the class shape.
3. **Reference leaves are the next-most-expensive thing after allocations**: a string in a struct array is a
   write barrier per store plus GC scanning of the whole array. The columnar answer (Arrow, section 11) is
   a string table + index; for a mapper that is an opt-in "dictionary-encode this member" directive, and
   the diagnostic should at least say why a `string` member keeps the pair off the blit.
4. **Layout hygiene is worth a diagnostic (S6 confirmed)**: 40 % time and memory from field order alone.

## 11. What mappers are, in general — and what other fields already learned that applies here

The question widened from "how do we blit more" to "what should we know about mappers as such". This section
is the cross-domain view: every field that moves structured data from one shape to another has converged on
the same handful of ideas, and each of them is a candidate for the hardware-mode work.

### 11a. A mapper is one of five things (and DwarfMapper is four of them)

| Kind | What it does | Where it lives elsewhere | DwarfMapper today |
|---|---|---|---|
| **Structural** | pair members by name / position / tag | MapStruct, AutoMapper, protobuf field tags, schema matching in ETL | the sort + ordinal pairing, completeness diagnostics |
| **Representational** | change the physical encoding of the same value | P/Invoke marshalling, serializers, endianness, Arrow, GPU vertex formats | the blit, the widen, `[Reinterpret]` |
| **Semantic** | change meaning: units, enums, nulls, defaults | enum strategies, `NullSubstitute`, converters | present |
| **Navigational** | walk a graph: cycles, identity, sharing, depth | ORM identity maps, serializer reference handling, rkyv shared pointers | Preserve / SetNull / depth guard |
| **Temporal** | apply a change, not a whole value | database view-update, lenses, dirty tracking, incremental computation | update-into, `[RoundTrip]` (the round-trip laws below) |

The insight from other fields: **the representational kind is where hardware lives**, and it only pays when
the mapper *owns the layout* of at least one side. Serializers own their wire layout; Arrow owns its column
layout; ECS engines own their chunk layout; a mapper between two user-declared classes owns nothing and is
therefore stuck at "copy field by field into whatever the user declared" — which is exactly what sections 2–9
measured. Every pathway below is a way of taking (partial) ownership of a layout.

### 11b. Lessons per field, and the pathway each suggests

1. **Rust `zerocopy` / `bytemuck`: layout proofs as *traits*, checked by derive macros.** `FromBytes`,
   `IntoBytes`, `KnownLayout`, `Pod` are compile-time evidence that a type may be reinterpreted; conversions
   are safe functions over that evidence, and the proof composes structurally (a struct is `Pod` iff every
   field is). DwarfMapper's `BlittableProof` is the same idea as a *procedure* rather than a *type*.
   **Pathway: make the proof a declared, composable property.** A `[Blittable]`-style assertion the generator
   VERIFIES (not trusts) on a transfer model, emitted into the generated code as a static assertion
   (`static_assert`-like: a `Debug.Assert(Unsafe.SizeOf<T>() == N)` is unsafe-adjacent; a `Marshal.SizeOf`
   check at module init is the safe form), so a later edit to the DTO that breaks the layout fails the
   consumer's build or first run, not silently drops to the scalar path. Also: nested proofs cache per type,
   the way trait resolution does — the extractor recomputes them per pair today.
2. **Zero-copy formats (FlatBuffers, Cap'n Proto, rkyv): don't copy — *view*.** Their answer to "mapping is
   expensive" is to make the target a *view over the source bytes* with relative offsets instead of a copy:
   rkyv "performs exactly the same as native types" because access is a pointer add. **Pathway: DTO views.**
   For a read-only consumer (serialize-and-forget, a projection into a response), the generator can emit a
   `readonly ref struct OrderView` whose properties read the source object's members on access — zero
   allocation, zero copy, lifetime bounded by the source (`ref struct` enforces it at compile time). This is
   *the* zero-cost mapper and it is entirely safe C#; it does not replace `Map` (a view cannot outlive its
   source or be stored), it complements it for the "map then immediately consume" case that dominates web
   handlers. Measured expectation: the map costs nothing; the consumer pays exactly what it reads.
3. **Arrows's validity bitmap: nulls as a separate bit-plane, values dense.** Arrow keeps a `1 bit per element`
   validity buffer instead of inline sentinels, so the value buffer stays dense and SIMD-able and an all-valid
   column omits the bitmap entirely. Our `Nullable<T>` inline flag costs 1 byte + padding per element (an
   `AddressS?` is 20 → 24 B) and breaks the dense stream. **Pathway: bitmap nullability in generated
   transfer models** — an optional member becomes `T` plus one bit in a per-array `ulong[]`/`BitArray`
   validity plane owned by the result. Only for the arena/result-shape models of section 10 (a user-declared
   struct keeps `Nullable<T>`); gain: the element stays blittable and 4–8 B smaller, and "is null" for a
   whole batch is a popcount.
4. **Arrow / columnar engines: dictionary encoding for strings.** Repeated strings are stored once, referenced
   by index; the column is an `int[]`. Section 10 measured a string leaf at +55 % per element (write barrier)
   plus GC scanning. **Pathway: `[MapDictionaryEncode]`** on a string member of a transfer model: the result
   carries a `string[] Table` and the element an `int NameId`; the generator emits the interning (a
   `Dictionary<string,int>` during the map). Opt-in, because it changes the DTO's shape for the consumer.
5. **ECS (Unity DOTS): archetype chunks — SoA per component, 16 KB chunks, blittable-only components, jobs
   over chunks.** The lesson is not SoA per se (section 9 showed AoS is right for whole-record consumers) but
   *chunking*: bounded contiguous blocks that stay cache-resident and are the unit of parallelism and of
   allocation. **Pathway: chunked results for large N** — a mapping over 100k elements returns a
   `ChunkedArray<TDto>` (an array of 16 KB struct arrays) instead of one 5 MB LOH array: every chunk stays
   below the LOH line, allocation is never a Gen2 object, and each chunk is a cache-resident blit (the regime
   where section 9c showed 5× rather than 1×). Consumers get `IReadOnlyList<T>`-shaped access. This attacks
   the exact place where our numbers collapsed to bandwidth.
6. **Serialization schema evolution: pair by tag, not by name.** protobuf field numbers survive renames;
   MapStruct and AutoMapper pair by name and break on rename; DwarfMapper pairs by *sorted name* and blits by
   *position*. **Pathway: stable member ids in transfer models** (`[MapId(3)]` or the order of declaration
   frozen by the generator) so that a renamed member is a diagnostic, not a silently unmapped one — this is
   the mislinking pain the user named as the #1 problem, seen through the serializer lens.
7. **Bidirectional transformations (lenses): GetPut / PutGet / PutPut laws.** `[RoundTrip]` already checks
   `Back(Forward(x)) ≡ x`, i.e. PutGet. The lens literature says a *well-behaved* mapper also needs GetPut
   (an unchanged view written back changes nothing) and, for update-into, PutPut (writing twice equals
   writing the last). **Pathway: state the laws the update-into endpoint satisfies and fuzz them** — GetPut
   for `Update(src, dest)` is exactly "mapping the same source twice is idempotent", which a fuzz oracle can
   check today; it is cheap and it closes the loop the theory says is open.
8. **Compilers: fusion (deforestation) and copy elision.** A pipeline `A → B → C` through two mappers
   materialises `B`; a compiler fuses the two maps into one traversal. **Pathway: map fusion across nested
   mappers** — when the generator can see both maps (same compilation, `[GenerateMap]` chains), emit the
   composed map and never allocate the intermediate. Also the update-into form of the same idea: mapping
   into an existing struct array (`Span<TDto>`) is copy elision of the result buffer; section 9 measured it at
   0.14 ns/elem.
   **ANSWERED 2026-09-09 - `Issues/round29/SPIKE-map-fusion.md`. The JIT does NOT elide the intermediate**, at N=1 or N=1000, and the same bytes come back in-process and out-of-process: the chained path allocates exactly one `B` per element more than the fused path (112 vs 72 B at N=1; 88,024 vs 48,024 B at N=1000), and an escaping control confirms the instrument can tell the two cases apart. 0.55x allocation at N=1000. So fusion is an UNCLAIMED WIN rather than something .NET 10's widened escape analysis already gives away - and the work is the REFUSAL LIST (hooks, `[RoundTrip]`, side-effecting converters, identity-preserving maps, and any publicly reachable `A->B`), not the emitter.
9. **Databases: late materialization and projection pushdown.** Do not build the row until you know which
   columns the consumer needs. **Pathway: consumer-driven projection** — the `.Project` endpoint already does
   this for `IQueryable`; the in-memory analogue is a generated *partial* transfer model per call site (only
   the members the consumer reads), which the view of item 2 gives for free.
10. **Marshalling (P/Invoke, COM): the mapper as a layout contract with a foreign side.** `[StructLayout]`,
    `MarshalAs`, blittable rules — the CLR's own marshaller is a mapper that refuses non-blittable shapes and
    documents every rule. DwarfMapper's `DWARF100` near-miss text is that documentation in diagnostic form;
    the pathway is to keep it complete for every new proof rule (Nullable, arena ranges, bitmaps).

### 11c. "Token-variant" pathways — alternative framings we would otherwise miss

Deliberately rephrasing "make mapping faster" five ways, and what each yields:

- *"Make mapping unnecessary"* → the view (11b.2) and consumer-driven projection (11b.9): the fastest map
  is the one that never runs.
- *"Make the target cheaper to own"* → decomposition (sections 8–10), arenas, bitmaps, dictionary encoding,
  chunking: the mapper owns the result layout, and a mapper that owns a layout can be as fast as a serializer.
- *"Make the source cheaper to read"* → nothing; the source is the user's, and reading a class field is
  already one load. This is why "blittable classes" was a dead end: the source side is not ours to change.
- *"Do less work per element"* → SIMD only where it removes work (conversion) or where the copy is a block;
  measured, and mostly exhausted at cache-resident sizes.
- *"Do the work at a better time"* → generation time (proofs, control vectors, layout decisions: already the
  design) and *compile time in the consumer* (static layout assertions, fused maps); never at run time.

### 11d. What this means for the plan

Nothing in sections 5, 9 and 10 is contradicted; two items are added above the fold and one reordered:
1. **DTO views** (`readonly ref struct` over the source) — zero-copy, safe, small emitter change, new
   endpoint shape; measure against `Map` on a serialize-immediately workload.
2. **Result-owned layouts** for the decomposed models: arena ranges for 1:N (measured 10× / 0.32× memory at
   100k), validity bitmaps for optionals, dictionary-encoded strings, 16 KB chunking above the LOH line —
   one opt-in "transfer result" shape rather than four attributes.
3. Then the already-planned: span-map blit, the decompose code fix with the `Nullable<T>` proof extension,
   layout-hygiene diagnostic, `TensorPrimitives` conversions, GetPut/PutPut fuzz laws for update-into,
   map fusion when both maps are visible.

### 11e. Sources for this section
- zerocopy traits: https://google.github.io/zerocopy/zerocopy/trait.FromBytes.html · https://google.github.io/zerocopy/zerocopy/trait.IntoBytes.html · Rust patterns on zero-copy: https://microsoft.github.io/RustTraining/rust-patterns-book/ch11-serialization-zero-copy-and-binary-data.html
- rkyv relative pointers and feature comparison: https://rkyv.org/architecture/relative-pointers.html · https://rkyv.org/feature-comparison.html · zero-copy formats compared: https://mohashari.github.io/zero-copy-serialization-protobuf-flatbuffers-capnproto/
- Arrow columnar format (validity bitmaps, struct arrays, dictionary encoding): https://arrow.apache.org/docs/format/Columnar.html
- MapStruct design (compile-time, no reflection, inspectable code): https://github.com/mapstruct/mapstruct · https://mapstruct.org/faq/
- Unity DOTS archetype chunks and blittable components: https://unity.com/blog/engine-platform/on-dots-entity-component-system · https://blog.innogames.com/unitys-performance-by-default-under-the-hood/ · https://docs.unity3d.com/Packages/com.unity.entities@0.4/manual/component_data.html
- Bidirectional transformations and lens laws: https://www.cs.ox.ac.uk/people/jeremy.gibbons/publications/ssbx-intro.pdf · https://www.cs.cornell.edu/~jnfoster/papers/ssgip-bidirectional.pdf · https://link.springer.com/chapter/10.1007/978-981-97-6429-7_3
- `Nullable<T>` layout and unmanaged-ness: https://learn.microsoft.com/dotnet/api/system.nullable-1 · https://github.com/dotnet/csharplang/discussions/6918 · https://github.com/dotnet/runtime/issues/97230
- Write barriers and reference-rich arrays: https://learn.microsoft.com/dotnet/standard/garbage-collection/large-object-heap · https://developer.arm.com/community/arm-community-blogs/b/architectures-and-processors-blog/posts/smarter-write-barriers-for-arm64-in-net-coreclr
- Schema/semantic mapping surveys: https://dl.acm.org/doi/10.1145/3567444 · https://www.ncbi.nlm.nih.gov/pmc/articles/PMC5937227/

## 12. Research batch 3 — DTO views, arena consumption, and the FusedChat shapes (`PlanProbe3.cs`, `plan3-results.md`)

All self-checks passed (every variant fills the same sink). Ratios against the class shape; N = 1,000 / 100,000.

| Case | 1,000 | 100,000 | Memory | Verdict |
|---|---:|---:|---:|---|
| **A. DTO tree, map to classes then consume 6 fields** (baseline) | 29.5 µs | 34.5 ms | 184 KB | — |
| A. map to nested structs, then consume | 0.45× | 0.10× | 0.35× | as before |
| A. **view** (`readonly ref struct` over the source), consume directly | **0.20×** (5.8 µs) | **0.04×** (1.4 ms) | **0 B** | the fastest map is the one that never runs |
| **B. 1:N, build the result AND read every line** — classes | 59 µs | 51.6 ms | 296 KB | — |
| B. per-order struct arrays | 0.34× | 0.33× | 0.41× | fine |
| B. **arena** (one line array + ranges) | 0.33× | **0.17×** | **0.32×** | 2× better than per-order arrays at scale, consumption included |
| **C. FusedChat-shaped message** (5 strings, `DateTime`, `bool`, enum) — map to class, consume | 12.1 µs | 10.4 ms | 80 KB | — |
| C. map to struct, consume | 0.73× | 0.20× | 0.70× | string references cost write barriers; the win is the object allocation only |
| C. **view**, consume | **0.18×** | **0.05×** | **0 B** | string-heavy DTOs gain most from not copying at all |
| **D. FusedChat `ProfileButtonData?[8]`** — class slots (per 100 profiles) | 7.1 µs | 1.39 ms | 47 KB | — |
| D. inline 8-slot struct + validity mask (`[InlineArray(8)]`) | 0.84× | 0.80× | 0.71× | modest: each button is still four strings |
| **E. FusedChat `Dictionary<BotPlatform,int>` per element** — dictionary copy | 42.6 µs | 47.0 ms | 312 KB | — |
| E. enum-indexed inline counts (`[InlineArray(6)]`) | **0.20×** | **0.07×** | **0.09×** | the single largest per-element cost in those DTOs |

### What the numbers say
1. **Views are the strongest result of the whole research**: 5× at 1k, 25× at 100k, zero allocation, and
   string-heavy DTOs (the realistic ones) gain the most because nothing is copied and no write barrier
   runs. The constraint is real and enforced by the compiler: a `ref struct` cannot be stored, captured, or
   returned across an `await` — it fits "map and immediately serialize / render / compare", which is what a
   web controller does with a DTO. Emission is small: one `readonly ref struct` per pair with a property per
   mapped member (converters and nested views compose: a nested member is another view, a collection is a
   span/enumerator view), the same name-pairing and completeness gate, no allocation anywhere.
2. **The arena stays ahead when the result is consumed**, 2× over per-parent arrays at 100k, because the
   consumer walks one contiguous array instead of 100k small ones.
3. **On FusedChat's own shapes** the order of wins is: per-element dictionaries (E: 5× / 14×, 11× less
   memory) ≫ views for the message/profile DTOs (C: 5× / 20×) ≫ struct decomposition of string-heavy
   models (C: 1.4× / 5×) ≫ inline fixed slots (D: 1.2×).

### What jumps out of the FusedChat structures (read 2026-09-02, branch backup/dwarfmapper-migration)
- **`Dictionary<BotPlatform, …>` everywhere** (`MessageCounts`, `CommandsCounts`, `PeakOnlineChatters`,
  `LivestreamUrls`, `ClientAlerts`, `MostUsedCommands`, `MostActiveChatters`, `LivestreamStatuses`), and
  `Dictionary<string, int> MessagesPerPlatform` keyed by the platform NAME. `BotPlatform` has five values.
  Every one of these is a hash table allocated and copied per element for a five-slot fixed domain. A mapper
  directive "this enum-keyed dictionary is dense over a small enum" (`[MapDenseEnumKeys]`) can emit an
  enum-indexed inline array in a generated transfer result (E: 0.20× / 0.07×, 11× less memory); on the
  consumer's own model side the same change is a one-line type swap they can make today.
- **`HashSet<BotPlatform> Platforms` per `StreamDaySchedule`**: a 5-bit mask. Same directive family.
- **`ChatMessage`: `Dictionary<string,string>? Emotes` and `ICollection<BadgeData>` per message**, both
  copied entry by entry by the generated helpers (`__DwarfMapDict_dc751720`, `__DwarfMapColl_3311efdd` +
  a `BadgeData` object per badge). `BadgeData` is the SAME immutable get-only type on both sides — the copy
  buys nothing; a "share identical immutable member" rule (`[MapShare]`, or automatic for get-only
  reference types identical on both sides) removes N allocations per message. `Emotes` as a
  `Dictionary<string,string>` is inherently a hash table per message; the view path (item 1) makes the
  copy disappear for the read-only API responses that dominate `ChatHistoryController`.
- **`MongoDbChatHistoryRepository`: `documents.OrderBy(x => x.Id).ToList()` then `ToChatMessages(...)`** —
  two materialisations of the page; a mapper that accepts `IEnumerable<T>` with a known count, or a
  `Map(ReadOnlySpan<TDoc>, Span<T>)` over `CollectionsMarshal.AsSpan(list)`, removes one.
- **`ProfileButtonData?[8]`**: a fixed 8-slot design that is already a struct-with-mask in disguise
  (D: 1.2×, and no per-profile array); modest because each button carries four strings.
- **Obsolete enum members (`Trovo`, `DLive`)** kept for compatibility: the CS0618 fix from this morning is
  the right shape; nothing further.

### Plan after batch 3 (final ordering of the research)
1. **DTO views** — new endpoint shape (`readonly ref struct`), measured 5–25×, zero allocation.
2. **Decompose-to-structs code fix + mapping-site diagnostic** (sections 8–10), with the `Nullable<T>`
   proof extension and the arena result shape for 1:N.
3. **Dense enum-keyed collections** (`[MapDenseEnumKeys]`: enum-indexed inline arrays and bit masks) —
   the largest per-element cost in the consumer's real models.
4. **Share identical immutable members** instead of copying (`BadgeData`-shaped types).
5. Span-map blit, layout-hygiene diagnostic, `TensorPrimitives` conversions, lens-law oracles, map fusion.

## 13. "Spannable classes" — a span over a class's field block instead of converting the declaration (`PlanProbe4.cs`, `plan4-results.md`)

The user's idea: if a class is convertible to a struct, its field block should be spannable — view it with
`MemoryMarshal.CreateSpan(ref obj.FirstField, n)` (no `unsafe` keyword) and blit it into a struct array
element, skipping the class-to-struct conversion of the declaration. Two questions, both measured.

**Correctness (the layout check, run once per benchmark process, same verdict every time):**
- `[StructLayout(Sequential)]` class, no reference fields → the span copy reproduces every field.
- Ordinary class (auto layout) → **garbage**: `B` reads back 0 instead of 21, `E` a denormal instead of 3.5,
  because the CLR reordered the fields by size (`long` first, then `int`/`float`, then `bool`). The span is
  the caller's *claim* about the layout; nothing checks it. So the technique is defined only for
  sequential, reference-free classes — which is a declaration change of the same weight as adding `struct`,
  and one the CLR silently ignores the moment a `string` member is added.

**Speed (five unmanaged fields, 32-byte body, ShortRun, wide error bars at 1k):**

| Variant | N = 1,000 | N = 100,000 |
|---|---:|---:|
| field gather into a struct array (safe, layout-independent) | 3.11 µs (1.00) | 547 µs (1.00) |
| span copy from the sequential class | 2.12 µs (0.68) | 548 µs (1.00) |
| span copy from the auto-layout class (wrong data) | 2.39 µs | 651 µs |

At 1k the body copy saves about a nanosecond per element over five field moves; at 100k the two are
identical, bandwidth-bound. Both are already at the one-allocation struct-array destination, which is
where the 3–11× came from (section 9): the span step adds at most 30 % on top of that and nothing at scale.

**Verdict.** Spanning a class body is real but buys ~1 ns per element, requires a layout attribute the
runtime does not enforce for reference-carrying classes, silently corrupts data on an ordinary class, and
uses an API that is unsafe in substance (no bounds, no layout contract) even though it needs no `unsafe`
keyword. The gain that matters comes from the destination being a struct array, which the safe field gather
already delivers. Not adopted; the `class(span(struct(span(class))))` composition inherits the same
verdict plus an extra copy, and `class(span(class))` is the body memcpy measured at ~1.0× in section 2.
