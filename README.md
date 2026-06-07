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
- **0 hidden allocation.** The mapper itself allocates nothing. Mapping into a new reference type allocates only that target (unavoidable); `MapTo(src, ref dest)` and span overloads give you true zero-allocation into caller-owned memory.
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
    [MapWith(typeof(MoneyToDecimalConverter))]   // custom conversion
    [Flatten(nameof(Customer.Address))]          // Address.City -> City
    public partial CustomerDto ToDto(Customer src);

    [MapIgnore(nameof(Customer.PasswordHash))]   // explicit, intentional, audited
    public partial Customer FromDto(CustomerDto dto);
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

**Enums.** Enum members map automatically:

- **enum ↔ enum** by name (default) — a source member with no same-named destination member is `DWARF015`. Opt into value-based casting with `[DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]`.
- **enum ↔ string** and **enum ↔ integral** are handled via generated `switch`/cast helpers (no reflection, AOT-safe).

---

## Resilience: the headline feature

### Completeness diagnostics
Unmapped destination members are a `DWARF` build error by default. Configure severity globally or per-mapper. This is the compile-time replacement for the manual "did I wire everything up?" review.

### `[RoundTrip]` verification
Tag a forward/back method pair. The generator emits a harness (consumed by `DwarfMapper.Testing`) that:
- generates fuzzed instances of the source type (seeded, reproducible),
- runs `Back(Forward(x))`,
- asserts structural equality against the original,
- and on mismatch produces an **informed dump**.

One attribute replaces the fixtures you used to maintain by hand.

### Informed dumps
A mapping-aware diff renderer. Instead of "two objects differ somewhere," you get:

```
✗ Order.FromDto(ToDto(x)) mismatch  [seed: 0x8F3A21C7]
  Discount         expected 0.15   actual 0.00    via  Order.Discount → OrderDto.Discount → (unmapped on return)
  ShippedAtUtc     expected …Z     actual null    via  Order.ShippedAtUtc → OrderDto.ShippedAt  (Kind lost in converter)
```

The diff knows the mapping graph, so it tells you *where the wiring broke*, not just *that values differ*.

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
- **Zero-alloc overloads:** `MapTo(src, ref dest)` and `Span<T>`/`ReadOnlySpan<T>` overloads for mapping into caller-owned memory.
- **Benchmarks in-repo:** a BenchmarkDotNet suite compares DwarfMapper against Mapperly, Mapster, and AutoMapper across flat, nested, and blittable-collection scenarios. Numbers, not adjectives.

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
  DwarfMapper/             # attributes + abstractions
  DwarfMapper.Generator/   # incremental source generator + analyzers
  DwarfMapper.Testing/     # fixtures, fuzzers, round-trip, informed dumps
tests/
  DwarfMapper.Tests/       # generator snapshot + behavior tests
  DwarfMapper.Benchmarks/  # BenchmarkDotNet vs. Mapperly/Mapster/AutoMapper
samples/                   # runnable examples
README.md
```

---

## Roadmap

### v1 — provable core (build first)
- Flat, nested, and collection/array/span mapping
- Enums and null handling
- Custom converters (`[MapWith]`)
- Canonical sort → ordinal pairing
- Completeness diagnostics (error by default)
- Blittable bulk-copy + SIMD fast-path, analyzer-gated
- `DwarfMapper.Testing`: fixtures, seeded fuzzer, `[RoundTrip]`, informed dumps
- NativeAOT + trimming CI gate; BenchmarkDotNet suite

### v2 — richer surface (documented now, built after the core is proven)
- Flattening / unflattening (`[Flatten]`)
- Before/after mapping hooks
- `IQueryable` projection (`ProjectTo`)
- `async` mapping pipelines

These v2 features are the heaviest to prove correct, so they ride behind the v1 core rather than blocking it.

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

🏗️ **Pre-alpha — design phase.** This README is the design contract. APIs shown are the intended shape and may change as the generator is built.

## Name

Dwarves mine with precision and forge what they find. DwarfMapper mines your types at compile time and forges the copies — small, exact, and unwilling to leave a seam unmapped.
