# DwarfMapper.NET

> A compile-time object mapper for .NET that makes **mislinking structurally impossible**, verifies your maps with **zero-maintenance round-trip tests**, and falls back to **SIMD/blittable bulk copy** where the hardware allows it.

DwarfMapper mines the mapping at compile time and forges direct field copies — no runtime engine, no reflection, no surprises. Its first job is not to be fast (though it is). Its first job is to make sure you **never silently fail to map a property again**.

---

## Why another mapper?

Every C# mapper today optimizes for ergonomics or raw speed. None of them treat **the most common real-world failure — forgetting to wire up a property — as a compile error.** That single class of bug is the reason teams hand-write fixtures and round-trip assertions, and pay the maintenance cost forever.

DwarfMapper is built around that pain:

1. **You cannot ship an incomplete map.** Every destination member must be mapped, or *explicitly* ignored. Anything else is a build error.
2. **Round-trips are verified for you.** Tag a forward/back pair with `[RoundTrip]` and the generator emits a fuzz-driven harness that proves `Back(Forward(x)) ≡ x`. No hand-written fixtures.
3. **Failures explain themselves.** When a check fails you get a *mapping-aware* diff: which member diverged, source vs. destination value, and the exact resolved path — not a blind object dump.

Speed is the supporting act: we match the fastest compile-time mappers on ordinary DTOs and beat them on blittable collections.

---

## The landscape (and where we sit)

| Library | Mechanism | Speed | Alloc (mapper) | AOT / trim | Completeness as a build error? | Built-in round-trip testing? |
|---|---|---|---|---|---|---|
| **AutoMapper** | Runtime, compiled expression trees | Moderate | Allocates | Poor | No | No |
| **Mapster** | Runtime codegen **or** source gen | Fast | Low (codegen) | OK | No | No |
| **Mapperly** | Pure Roslyn source generator | Fastest tier | Zero | Excellent | Opt-in warning | No |
| **DwarfMapper** | Pure Roslyn source generator + blit/SIMD fast-path | Fastest tier; **faster on blittable collections** | Zero | Excellent (CI-verified) | **Yes, error by default** | **Yes** |

**Honest performance claim:** Mapperly already emits `dest.A = src.A`, which is the JIT floor for name-based mapping. On typical DTOs (strings, references, transforms) DwarfMapper produces the *same* direct assignments and is therefore *equally* fast. DwarfMapper pulls ahead only where the data is **blittable and layout-compatible** — there it copies whole spans with `MemoryMarshal`/`Unsafe`/SIMD instead of per-element field writes. We never claim to be "faster than everything"; we claim to be *as fast as the floor, faster where physics allows, and far harder to get wrong.*

---

## Design principles

- **0 reflection.** No `System.Reflection`, no `Reflection.Emit`, no runtime configuration. Everything is resolved by the source generator. NativeAOT- and trimming-safe, and verified so in CI.
- **0 hidden allocation.** The mapper itself allocates nothing. Mapping into a new reference type allocates only that target (unavoidable).
- **0 silent data loss.** Completeness is enforced at compile time. Over-posting / mass-assignment-style bugs are designed out: only members the generator explicitly resolved are ever written.
- **Provably-safe unsafe.** The blittable fast-path is gated by analyzer proofs (unmanaged types, matching size, no managed references, compatible layout). If a reinterpret cast cannot be proven safe, the generator falls back to plain assignments. It never emits an unprovable cast.
- **Declarative only.** Configuration lives in attributes and partial methods — never a runtime fluent builder. (Fluent runtime config is the reflection trap that breaks AOT; we don't go there.)

---

## How it works: sort → pair → prove → emit

The "radical" core is that DwarfMapper **sorts members into a canonical order before pairing them**, instead of matching each member dynamically. This does two things at once:

### 1. Sort
Members of the source and destination are sorted into a canonical order keyed by their *resolved* name (after conventions, renames, and flattening paths are applied). Sorting makes pairing **declaration-order-independent**: reordering fields in either type can never silently re-link them to the wrong target. It also groups blittable members into **contiguous runs**, which is what unlocks the bulk fast-path.

### 2. Pair
Pairing walks the two sorted member lists ordinally. The result is a deterministic, stable mapping plan.

### 3. Prove (the completeness gate + blit proof)
- **Completeness:** every destination member must be paired or carry `[MapIgnore]`. Otherwise → **build error** (configurable to warning). Every source member must be consumed or explicitly marked source-ignored. *"I forgot to map it" stops compiling.*
- **Blit proof:** for each contiguous run, the generator asks: are these segments unmanaged, identically sized, layout-compatible, and transform-free? If yes, it plans a bulk copy (`MemoryMarshal.Cast` / `Unsafe.CopyBlock` / SIMD; whole-span for collections). If no, it plans direct assignments.

### 4. Emit
The generator writes direct assignments, inline converters, and null guards for the ordinary members, and bulk/SIMD copies for the proven runs. The emitted code reads like something you'd have hand-written.

---

## Quick start

```csharp
using DwarfMapper;

[DwarfMapper]
public partial class OrderMapper
{
    // Filled in by the generator.
    public partial OrderDto ToDto(Order src);

    // A forward/back pair — round-trip verified automatically.
    [RoundTrip]
    public partial Order FromDto(OrderDto dto);
}
```

Customize with attributes (all compile-time, all AOT-safe):

```csharp
[DwarfMapper]
public partial class CustomerMapper
{
    [MapProperty(nameof(Customer.FullName), nameof(CustomerDto.Name))]
    [MapProperty(nameof(Customer.Total), nameof(CustomerDto.Total), Use = nameof(FormatMoney))]  // custom conversion
    [Flatten(nameof(Customer.Address))]          // Address.City -> City
    public partial CustomerDto ToDto(Customer src);

    [MapIgnore(nameof(Customer.PasswordHash))]   // explicit, intentional, audited
    public partial Customer FromDto(CustomerDto dto);

    private static string FormatMoney(decimal d) => d.ToString("C");
}
```

If `CustomerDto` gains a `Region` property and you don't map or ignore it, **the build fails** with a diagnostic pointing at the unmapped member.

### Configuring mapping

```csharp
[DwarfMapper(CaseInsensitive = true)]      // opt-in: match 'name' to 'Name'
public partial class CustomerMapper
{
    [MapProperty(nameof(Customer.FullName), nameof(CustomerDto.Name))]  // explicit rename
    public partial CustomerDto ToDto(Customer src);
}
```

- **Fields and properties** are both mapped (public, instance; writable on the destination, readable on the source).
- **Matching is exact/case-sensitive by default**; set `CaseInsensitive = true` to relax it (an ambiguous match then becomes `DWARF010`).
- **`[MapProperty(source, target)]`** maps differently-named members and suppresses the completeness error for that destination; an unknown source/target is `DWARF009`/`DWARF008`.
- A **read-only destination** member that has a matching source is flagged (`DWARF007`) rather than silently dropped.
- **Source-member coverage (opt-in).** By default the completeness gate is destination-side only (every target must be mapped). Set `[DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]` to also require every **source** member to be read by some destination — a source consumed by nothing (incl. via a constructor argument or a `[Flatten]` root) surfaces the **`DWARF039`** suggestion (Info), so a forgotten/mis-wired field is never silent. Suppress one with `[MapIgnoreSource("Member")]` (class- or method-level, the source-side mirror of `[MapIgnore]`); make it strict with `dotnet_diagnostic.DWARF039.severity = error` in `.editorconfig`.

**Converting between types.** When a source and destination member have different types, DwarfMapper bridges them in one of two ways:

```csharp
[DwarfMapper]
public partial class OrderMapper
{
    public partial OrderDto ToDto(Order o);
    public partial AddressDto ToDto(Address a);   // nested: auto-wired by type signature

    // custom scalar conversion: name a method with Use
    [MapProperty(nameof(Order.Total), nameof(OrderDto.Total), Use = nameof(FormatMoney))]
    public partial OrderDto ToDto2(Order o);

    private static string FormatMoney(decimal d) => d.ToString("C");
}
```

- **Nested objects** map automatically when you declare a mapping method for the nested type pair; it is found by signature and called (`Home = ToDto(o.Home)`).
- **Custom conversions** use `[MapProperty(src, tgt, Use = nameof(Method))]`, where `Method` takes the source member type and returns the destination type.
- An ambiguous auto-conversion is `DWARF013`; an invalid `Use` method is `DWARF014`. If no conversion is possible at all, you still get the completeness error `DWARF005`.

**Constant & computed values (`[MapValue]`).** Assign a destination member that has no source — and count it as mapped (suppressing `DWARF001`):

```csharp
[MapValue(nameof(OrderDto.Source), "api-v2")]            // constant literal
[MapValue(nameof(OrderDto.ImportedAt), Use = nameof(Now))] // computed: a parameterless method
private static System.DateTime Now() => System.DateTime.UtcNow;
```

A constant must be an attribute-legal value (string, bool, char, numeric, enum, or null) **assignable to the destination type** — a mismatch (e.g. `"x"` to an `int`, or `null` to a non-nullable value type) is `DWARF040`. A `Use` provider must be a **parameterless** method whose return type is assignable to the destination — otherwise `DWARF041`. Conflicting with `[MapProperty]`/`[MapIgnore]` on the same target, an unknown target, or targeting a constructor parameter is `DWARF042`.

**Built-in scalar conversions.** DwarfMapper handles common scalar type mismatches automatically without requiring `[MapProperty(Use=...)]`:

| Conversion kind | Example | Emitted code | Notes |
|---|---|---|---|
| **Implicit / identity** | `byte → int`, `int → int` | direct assignment | Zero-cost; compiler-proven lossless |
| **Integral narrowing** | `long → int`, `int → uint`, `uint → int` | `global::System.Int32.CreateChecked(v)` | Throws `OverflowException` if value doesn't fit; never silent truncation |
| **string → T** (`T : IParsable<T>`) | `string → int`, `string → Guid`, `string → bool`, `string → double` | `global::System.Int32.Parse(v, InvariantCulture)` | InvariantCulture; throws `FormatException`/`OverflowException` on bad input |
| **string → DateTime / DateTimeOffset** | `string → DateTime` | `DateTime.Parse(v, InvariantCulture, DateTimeStyles.RoundtripKind)` | Preserves `DateTimeKind` (UTC, Local, Unspecified) and sub-second precision |
| **T → string** (`T : IFormattable`) | `int → string`, `Guid → string`, `decimal → string` | `v.ToString(null, InvariantCulture)` | InvariantCulture (decimal separator is always `.`) |
| **bool / char → string** | `bool → string`, `char → string` | `v.ToString()` | No-arg overload (culture-invariant by nature); `bool` → "True"/"False"; `char` → single-char string |
| **DateTime / DateTimeOffset → string** | `DateTime → string` | `v.ToString("o", InvariantCulture)` | ISO-8601 round-trip format ("o"); lossless including sub-second precision and `Kind`/offset |
| **enum ↔ enum** (by name, default) | `Color.Red → Status.Red` | `switch` | Missing member → `DWARF015` |
| **enum ↔ enum** (by value) | value cast | `CreateChecked` on underlying | Throws on overflow |
| **enum ↔ string** | `Color.Red ↔ "Red"` | `switch` | No reflection |
| **enum ↔ integral** | `Color → int` | `CreateChecked` on underlying | Throws on overflow |
| **T → T?** (target-nullable, non-nullable source) | `long → int?`, `string → int?`, `int → Color?` | inner conversion result implicitly lifted to `T?` | Overflow/format errors still propagate |
| **T? → U?** (both nullable) | `long? → int?`, `E1? → E2?` | `src.HasValue ? Conv(src.Value) : null` | Null-preserving: null source → null target; non-null out-of-range still throws (e.g. `OverflowException`) |
| **T? → U** (nullable source, non-nullable target) | `int? → short`, `E1? → E2` | `src ?? throw` (default strategy) or `src.GetValueOrDefault()` (`SetDefault`) | Follows mapper's `NullStrategy` setting |

**User methods take precedence over built-in synthesized conversions.** Any non-partial single-parameter method on the mapper class that matches a `(srcType → tgtType)` pair is used automatically — at higher priority than `CreateChecked`/`Parse`/`ToString` synthesis. This lets you intentionally shadow the built-in with custom logic (e.g. a rounding `long→int`). Explicit `Use=` still wins first.

**Not automatically handled (require `Use=`):** float/double/decimal → integer (silent fractional truncation is never emitted automatically). Declare a custom method and reference it with `[MapProperty(Use=nameof(...))]`.

All emitted calls (`CreateChecked`, `Parse`, `ToString`) are concrete static/instance invocations — no reflection, no `Activator`, trim/NativeAOT-safe.

**Conversion policy (`ImplicitConversions`, no silent surprises).** Lossless same-category widening (`int→long`, `float→double`) and identity always map silently. Every **non-lossless** auto-conversion — narrowing (`long→int`), parse/format (`string↔int`), and cross-category numeric (`int→double`, `int→float`) — instead surfaces a **`DWARF038` suggestion (Info)** so it is visible in the IDE, never silent, while still being applied. Set `[DwarfMapper(ImplicitConversions = false)]` to flip those suggestions into **build errors** (Mapperly-style strict) — then each such conversion must be made explicit with `[MapProperty(Use = nameof(...))]`. Default is `true` (permissive), so this is opt-in and non-breaking.

**Enums.** Enum members map automatically:

- **enum ↔ enum** by name (default) — a source member with no same-named destination member is `DWARF015`. Opt into value-based casting with `[DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]`.
- **enum ↔ string** and **enum ↔ integral** are handled via generated `switch`/cast helpers (no reflection, AOT-safe).

**Nullable values.** A nullable value-type source mapped to a non-nullable destination (`int? → int`) is unwrapped per `NullStrategy`: **throw on null** (default) or `[DwarfMapper(NullStrategy = NullStrategy.SetDefault)]` to use the destination default. A non-nullable source mapped to a nullable-value-type destination (`long → int?`, `string → int?`) is handled automatically — the inner conversion (e.g. `CreateChecked`, `Parse`) runs, and the result is implicitly lifted to `T?`; overflow and format errors still propagate. Nullable-source + nullable-target (`long?→int?`) is not yet auto-handled (DWARF005; a documented follow-up).

**Collections.** `T[]`, `List<T>`, and `HashSet<T>` members map element-by-element, applying the same conversion rules per element (nested objects, converters, enums, nullable unwrap, even nested collections). When the element type is unchanged, the whole collection is bulk-copied (`T[]` → `T[]` clone, `→ List<T>`/`HashSet<T>` bulk constructor). Read-only source shapes (`IEnumerable<T>`, `IReadOnlyList<T>`, `IReadOnlySet<T>`, …) are accepted as sources.

**Flattening.** `[Flatten("Address")]` pulls a complex source member's sub-members up to top-level destination members of the same name (`Address.City → City`). The flattened leaf goes through the same conversion rules (converters, enums, nullable). One level deep; a null flatten root throws at runtime. Unknown root → `DWARF016`; a name flattened from two roots → `DWARF017`.

**Hooks.** `[BeforeMap]` (`void Hook(TSource)`) and `[AfterMap]` (`void Hook(TTarget)` or `void Hook(TSource, TTarget)`) methods on the mapper run around the generated body — validate the source, or fill computed/`[MapIgnore]`d destination members. Hooks apply to every mapping whose types are assignable to their parameters. An invalid signature is `DWARF018`.

**Dictionaries.** `Dictionary<K,V>` (and `IDictionary<K,V>`/`IReadOnlyDictionary<K,V>` sources) map to `Dictionary<K2,V2>`, converting **both keys and values** through the same rules as any other member (converters, nested mappers, enums, nullable). Entries are filled via the indexer, so post-conversion key collisions overwrite rather than throw.

**Constructor and record mapping.** DwarfMapper can map into targets that have **no parameterless constructor** — positional `record`s, `record struct`s, `readonly record struct`s, and constructor-based immutable classes/structs. The generator binds source members to constructor parameters by name, resolves any necessary conversion (same rules as property mapping), and emits a named-argument constructor call. `init`-only and mutable properties beyond the constructor parameters are object-initialized in the same statement.

```csharp
public record PersonDto(int Id, string Name);     // no parameterless ctor

[DwarfMapper]
public partial class PersonMapper
{
    public partial PersonDto ToDto(Person src);   // emits: new PersonDto(Id: src.Id, Name: src.Name)
}
```

Every constructor parameter is **mandatory** — if DwarfMapper cannot find a matching source member, it emits `DWARF024 ConstructorParameterUnmapped` (a build error). Positional record members that are satisfied by a constructor parameter are not double-assigned in the object initializer.

**Constructor selection policy** (deterministic, in order):
1. A constructor annotated `[DwarfMapperConstructor]` is selected unconditionally. More than one annotated constructor → `DWARF025 AmbiguousConstructor`.
2. An accessible parameterless constructor → object-initializer path (existing behavior, no ctor args emitted).
3. Exactly one non-parameterless constructor → use it.
4. Multiple non-parameterless constructors → pick the one with the most parameters; if there is a tie → `DWARF025`.
5. No candidates → `DWARF026 NoMappableConstructor`.

Obsolete constructors and the implicit record copy constructor (`R(R original)`) are always excluded from selection.

**`[MapProperty]` works with constructor parameters.** Use `[MapProperty("SourceMember", "ctorParamName")]` to redirect a source member onto a differently-named constructor parameter.

**All conversions apply to constructor parameters.** `CreateChecked` narrowing, `IParsable<T>` string parsing, enum conversions, nullable unwrap, and `T?→U?` null-preserving mapping all work identically on constructor parameters.

**Emitted code is AOT-safe.** Named-argument constructor calls are concrete, non-reflective invocations — no `Activator.CreateInstance`, no expression trees.

**Projection (IQueryable).** A partial method `IQueryable<TDto> Project(IQueryable<T> src)` generates `src.Select(s => new TDto { … })` — an expression tree your ORM translates to SQL. **Projection is deliberately simple:** only directly-assignable members (plus `[MapProperty]` renames and `[MapIgnore]`) are projected — no method calls, no inline nested-projection "magic" (a known antipattern). A member needing a real conversion is `DWARF019`; do that mapping with a runtime mapper instead.

**Blittable fast-path (SIMD).** When `TSrc[]` and `TDst[]` have **provably identical memory layout** — both unmanaged, `Sequential`, same packing, and the same ordered field names/types (including nested structs, recursively) — DwarfMapper skips the element loop and reinterprets the whole block in one vectorized `MemoryMarshal.Cast` memmove, behind a JIT-folded runtime size guard. This is the one place DwarfMapper beats a hand-written name-based copy, and it is emitted **only when proven safe** — otherwise it falls back to the element loop. For layout-compatible types the proof can't confirm (differing field names you know are positionally correct, types from referenced assemblies), opt in with `[Reinterpret("Member")]` — still memory-safe (unmanaged + size guard), with the field correspondence as your assertion. A bad `[Reinterpret]` target is `DWARF022`.

**Update-into-existing.** Declare a two-parameter partial method to map onto an **existing** target instead of constructing a new one — the target's identity is preserved (handy for updating tracked entities, pooled objects, or pre-allocated buffers):

```csharp
[DwarfMapper]
public partial class CustomerMapper
{
    public partial void Update(CustomerDto src, Customer dest);   // void form, mutates dest
    public partial Customer Update(CustomerDto src, Customer dest); // fluent form, returns dest
}
```

Settable members are assigned from the source; the same completeness gate (`DWARF001`), conversions, `[MapProperty]`/`[MapIgnore]`, and hooks apply. Both parameters are null-guarded (loud `ArgumentNullException`). Nested members and collections are **replaced** (mapped fresh / assigned), not merged. The target must be a reference type (a struct passed by value couldn't observe the mutation). Recursion-capable nested members are depth-guarded as usual.

**Zero-alloc span mapping.** Declare `void Map(ReadOnlySpan<S> src, Span<D> dst)` to map element-wise into a caller-provided buffer with no heap allocation — ideal for hot paths over `stackalloc`/pooled memory:

```csharp
[DwarfMapper]
public partial class M { public partial void Map(ReadOnlySpan<int> src, Span<long> dst); }
```

Each element runs through the full conversion pipeline (`dst[i] = convert(src[i])`). The destination must be a writable `Span<D>` (source may be `Span<S>` or `ReadOnlySpan<S>`), and a destination smaller than the source throws `ArgumentException` — never a silent truncation.

**Async streaming.** Declare `IAsyncEnumerable<D> Map(IAsyncEnumerable<S> src)` to lazily transform an async sequence — the generator emits an `async` iterator (`await foreach … yield return convert(item)`) that streams element-by-element without buffering, preserving back-pressure:

```csharp
[DwarfMapper]
public partial class M { public partial IAsyncEnumerable<Dto> Map(IAsyncEnumerable<Entity> src); }
```

Ideal for mapping a streamed data source (DB cursor, network stream) to DTOs without materializing the whole sequence.

**Reference handling & cycles.** Recursive/self-referential types (`Node { Node? Next }`, a tree, mutually-recursive types) are detected at generator time. Only the `(src,tgt)` pairs that can actually re-enter get the extra machinery — acyclic pairs stay zero-overhead. The behaviour for shared references and cycles is controlled per mapper:

```csharp
[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]   // full topology
[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]                        // break cycles with null
[DwarfMapper(MaxDepth = 128)]                                          // depth bound (default 64)
```

- **`ReferenceHandling = None`** (default): no identity tracking, zero allocation. Recursion-capable pairs carry a depth counter — a cycle (or an over-deep chain), whether through a **direct reference member or a `List<T>`/`Dictionary<K,V>` edge**, throws a loud, catchable `DwarfMappingDepthException` at `MaxDepth` (default 64, hard cap 1000) instead of a silent `StackOverflowException`. Non-recursive collections stay zero-overhead (no context threaded).
- **`ReferenceHandling = Preserve`**: a reference-identity map (`ReferenceEqualityComparer`, register-before-populate) is threaded through the recursive pairs, reconstructing the **complete topology** — shared nodes, diamonds, and all cycles are relinked isomorphically; two distinct-but-value-equal records are never merged. One small dictionary per top-level `Map` call.
- **`OnCycle = SetNull`** (None mode only): breaks a reference cycle by **nulling the re-entrant back-edge** — equivalent to `System.Text.Json`'s `ReferenceHandler.IgnoreCycles`. A node already on the active mapping stack is not mapped again; the member pointing back to it becomes `null`, yielding a finite acyclic projection (handy for display/DTO shapes). Shared-but-acyclic nodes (diamonds) are still mapped on each path (duplicated) — only true ancestor cycles are nulled. The one shared `DwarfRefContext` is threaded through nested object, collection, and dictionary mappers alike, so a cycle routed through a **`List<T>` element or `Dictionary<K,V>` value** breaks the same way (the re-entrant element/value becomes `null`). Setting `OnCycle = SetNull` together with `Preserve` reports `DWARF037` (Preserve reconstructs cycles, so `OnCycle` has no effect).

---

## Resilience: the headline feature

### Completeness diagnostics
Unmapped destination members are a `DWARF` build error — always. There is no global or per-mapper severity override; completeness is enforced at compile time by design. This is the compile-time replacement for the manual "did I wire everything up?" review.

### `[RoundTrip]` verification
Tag the forward method with `[RoundTrip]`; the generator finds the inverse mapping method and emits a `VerifyRoundTrip_<method>(seed, iterations)` that fuzzes inputs (seeded, reproducible), runs `Back(Forward(x))`, asserts structural equality, and on mismatch throws an **informed dump**. One attribute replaces the fixtures you used to maintain by hand — call it from a single test:

```csharp
[DwarfMapper]
public partial class OrderMapper
{
    [RoundTrip] public partial OrderDto ToDto(Order o);
    public partial Order FromDto(OrderDto d);
}

[Fact]
public void Order_roundtrips() => new OrderMapper().VerifyRoundTrip_ToDto();
```

Requires a reference to `DwarfMapper.Testing`; without it, `[RoundTrip]` is a no-op (no verifier is generated). No inverse → `DWARF020`; ambiguous inverse → `DWARF021`.

### Informed dumps
A mapping-aware diff renderer. Instead of "two objects differ somewhere," you get:

```
✗ Order.FromDto(ToDto(x)) mismatch  [seed: 0x8F3A21C7]
  Discount         expected 0.15   actual 0.00    via  Order.Discount → OrderDto.Discount → (unmapped on return)
  ShippedAtUtc     expected …Z     actual null    via  Order.ShippedAtUtc → OrderDto.ShippedAt  (Kind lost in converter)
```

The diff knows the mapping graph, so it tells you *where the wiring broke*, not just *that values differ*.

### Verifying maps today

`DwarfMapper.Testing` ships the round-trip verifier you can call now:

```csharp
var m = new OrderMapper();
RoundTrip.Verify<Order, OrderDto>(m.ToDto, m.FromDto);   // fuzzes inputs, asserts Back(Forward(x)) ≡ x
```

On a mismatch it throws with a mapping-aware dump (member path, expected vs. actual, and the replay seed). `ObjectFactory.Create<T>(seed)` and `Fuzzer.Generate<T>(count, seed)` build seeded fixtures for your own tests. The package is reflection-based and test-only — it is never AOT-published and does not affect the core library's reflection-free guarantees. (Prefer `[RoundTrip]` above for the zero-boilerplate path; this direct call is for ad-hoc verification.)

---

## Security model

DwarfMapper treats mapping as an attack surface and a correctness surface at once:

- **Zero reflection / AOT- & trim-safe.** No reflection or runtime emit anywhere; CI runs a NativeAOT + trimming build and fails on any reflection fallback.
- **Over-posting protection.** Only members the generator explicitly resolved are written. Ambiguous matches are a build error, never a silent guess — so untrusted input can't drive an unintended field assignment.
- **Provably-safe blit.** The unsafe fast-path is emitted only behind analyzer-verified proofs; otherwise it degrades to safe assignments.
- **Completeness diagnostics.** No silent data loss in either direction.

---

## Performance

- **Direct-assignment path:** identical codegen to the fastest source-gen mappers — the JIT floor.
- **Blittable fast-path:** contiguous unmanaged, layout-matched runs are bulk-copied; collections of such types are copied whole-span via `MemoryMarshal.Cast<TSrc, TDest>` and SIMD where it helps.

---

## Packages

DwarfMapper is **.NET 10 only**. The one exception is the generator assembly itself (see note below).

| Package | Purpose | TFM |
|---|---|---|
| `DwarfMapper` | Attributes + tiny abstractions. Zero dependencies. | `net10.0` |
| `DwarfMapper.Generator` | Roslyn incremental source generator + analyzers/diagnostics. | `netstandard2.0` * |
| `DwarfMapper.Testing` | Fixture builders, seeded property-based fuzzer, round-trip verifier, informed dumps. xUnit/NUnit theory sources. | `net10.0` |

\* The generator assembly targets `netstandard2.0` because Roslyn loads analyzers/source generators *into the compiler host*, which only accepts `netstandard2.0` components. This is a hard requirement of the Roslyn toolchain, not a runtime-support choice — the code it **generates** targets .NET 10 like everything else.

---

## Repository layout

```
DwarfMapper.NET.sln
src/
  DwarfMapper/               # attributes + abstractions
  DwarfMapper.Generator/     # incremental source generator + analyzers
  DwarfMapper.Testing/       # fixtures, fuzzers, round-trip, informed dumps
tests/
  DwarfMapper.Generator.Tests/   # generator snapshot + behavior tests
  DwarfMapper.IntegrationTests/  # end-to-end mapping integration tests
  DwarfMapper.Testing.Tests/     # DwarfMapper.Testing library unit tests
samples/
  DwarfMapper.AotSample/     # NativeAOT + trimming gate sample
README.md
```

---

## Roadmap

### v1 — shipped (feature-complete, pre-release)
- Flat, nested, and collection (`T[]` / `List<T>` / `HashSet<T>` / `Dictionary<K,V>`) mapping
- Enums and null handling
- Custom per-member conversion: `[MapProperty(src, tgt, Use = nameof(Method))]`
- Canonical sort → ordinal pairing
- Completeness diagnostics (build error by design — no configurable severity)
- Blittable bulk-copy + SIMD fast-path, analyzer-gated; `[Reinterpret]` escape hatch
- Flattening: `[Flatten("Root")]` pulls sub-members to top-level destination
- Before/after mapping hooks: `[BeforeMap]` / `[AfterMap]`
- `IQueryable` projection: `IQueryable<TDto> Project(IQueryable<T> src)` → SQL-translatable expression tree
- `DwarfMapper.Testing`: fixtures, seeded fuzzer, `[RoundTrip]`, informed dumps
- NativeAOT + trimming CI gate (verified; AOT sample in `samples/`)
- **Constructor / record / immutable target mapping**: positional records, `record struct`, `readonly record struct`, readonly structs, constructor-only classes; all conversions apply to ctor params; `[DwarfMapperConstructor]` disambiguation; DWARF024/025/026; named-argument AOT-safe emit
- **Object-graph composition**: auto-synthesized nested mappers; full collection/dictionary taxonomy + nested compositions; deep auto-nesting
- **Reference handling**: `ReferenceHandling = Preserve` (full topology reconstruction), `OnCycle = SetNull` (System.Text.Json-style cycle breaking), and `None` depth-guarding — all three handle cycles through **direct, collection, and dictionary edges** with no silent `StackOverflowException` (catchable `DwarfMappingDepthException` at `MaxDepth`)
- **Polymorphic dispatch** (`[MapDerivedType]`) and graph degradation (`[FlattenGraph]`, homogeneous + heterogeneous)
- **Update-into-existing**: `void Map(S src, T dest)` / `T Map(S src, T dest)` maps onto an existing instance (identity preserved); same completeness gate, conversions, hooks, `[MapProperty]`/`[MapIgnore]`
- **Zero-alloc span mapping**: `void Map(ReadOnlySpan<S> src, Span<D> dst)` maps element-wise into a caller buffer (no allocation), with a defensive length guard (too-small destination throws, never silent truncation)
- **In-repo BenchmarkDotNet suite** (`benchmarks/DwarfMapper.Benchmarks`): DwarfMapper vs. hand-written **and vs. Mapperly / Mapster / AutoMapper 14** across flat / nested / collection / blit scenarios (see [`docs/COMPARISON.md`](docs/COMPARISON.md) for the full capability/testing/migration comparison); builds in CI, run locally for numbers
- **Async streaming**: `IAsyncEnumerable<D> Map(IAsyncEnumerable<S> src)` lazily transforms an async sequence element-by-element (emitted as an `async` iterator; streaming/back-pressure preserved)

### Planned
- Competitor benchmark comparisons (Mapperly / Mapster / AutoMapper)
- NuGet publish

---

## License

**GNU General Public License v2.0 only** (`SPDX: GPL-2.0-only`).

DwarfMapper is free and open source, and it is **strong copyleft on purpose**. You may use, study, modify, and redistribute it freely. But note what that means *for this kind of library specifically*:

> DwarfMapper is a **source generator** — it emits code directly into your assembly. Under GPLv2, software you build and distribute on top of it is a derivative work and must itself be released under GPLv2, with corresponding source made available to whoever you distribute it to.

In plain terms: **if you build on DwarfMapper and ship it, your project is GPLv2 too, and your users get the source.** That is the point. The library stays free, and anyone who profits from it does so in the open, where its origin is visible.

Caveats, stated honestly:
- **SaaS is not distribution.** A company running DwarfMapper inside a hosted service never ships binaries, so GPLv2 does not compel them to publish source. (Closing that loophole would require AGPL — a deliberate choice we did *not* make.)
- **GPLv2-only**, not "v2 or later" — the version is fixed.
- GPLv2-only is incompatible with Apache-2.0 and GPLv3, which constrains what third-party code can be pulled in later. DwarfMapper has zero dependencies, so this is a non-issue today.

The full license text lives in [`LICENSE`](LICENSE). Every source file carries an SPDX header (`// SPDX-License-Identifier: GPL-2.0-only`).

## Status

**Feature-complete v1 — pre-release / not yet published to NuGet.** The generator, all documented attributes, and the testing toolkit are built and covered by tests. APIs are stabilising. Not yet on NuGet; use via source or direct project reference. Feedback on rough edges welcome.

## Name

Dwarves mine with precision and forge what they find. DwarfMapper mines your types at compile time and forges the copies — small, exact, and unwilling to leave a seam unmapped.
