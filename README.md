# DwarfMapper.NET

> A compile-time object mapper for .NET where **an unmapped member is a build error**, maps are **round-trip-verifiable with one attribute**, and blittable data falls back to **SIMD/blittable bulk copy** where the hardware allows it.

> [!IMPORTANT]
> **Is DwarfMapper for you?** Three things to decide *before* you invest time:
> - **License — GPL-2.0-only (strong copyleft).** Because DwarfMapper is a source generator, if you build on it and **distribute** the binary, your project must also be GPLv2 with source available. Running it inside a hosted service (SaaS) does **not** trigger that. **If you ship closed-source binaries, this is not the mapper for you.** ([why →](#license))
> - **.NET 10 only.** The runtime package targets `net10.0` with no multi-targeting — net8/net9 projects can't reference it yet.
> - **Pre-release.** `1.0.0-rc.1` is a release candidate — APIs are stabilising; install it with the `--prerelease` flag. ([status →](#status))
>
> If GPLv2, .NET 10, and pre-release all work for you, read on: completeness becomes a *build error*, maps are *round-trip-**verifiable*** (opt in per pair with `[RoundTrip]`), and blittable data goes through *SIMD*.

**Quick links:** [Quick start](#quick-start) · [Why another mapper?](#why-another-mapper) · [The landscape](#the-landscape-and-where-we-sit) · [Migration guides](docs/howto/) · [Diagnostics reference](docs/diagnostics.md) · [Options cheat-sheet](docs/options.md) · [Packages](#packages)

DwarfMapper mines the mapping at compile time and forges direct field copies — no runtime engine, no reflection, no surprises. Its first job is not to be fast (though it is). Its first job is to make sure you **never silently fail to map a property again**.

---

## Why another mapper?

Every C# mapper today optimizes for ergonomics or raw speed. None of them treat **the most common real-world failure — forgetting to wire up a property — as a compile error.** That single class of bug is the reason teams hand-write fixtures and round-trip assertions, and pay the maintenance cost forever.

DwarfMapper is built around that pain:

1. **You cannot ship an incomplete map.** Every destination member must be mapped, or *explicitly* ignored. Anything else is a build error.
2. **Round-trips are verifiable in one line.** Tag a forward/back pair with `[RoundTrip]` (with a `DwarfMapper.Testing` reference) and the generator emits a fuzz harness you call from a test to prove `Back(Forward(x)) ≡ x`. No hand-written fixtures.
3. **Failures explain themselves.** When a check fails you get a *mapping-aware* diff: which member diverged, source vs. destination value, and the exact resolved path — not a blind object dump.

Speed is the supporting act: we match the fastest compile-time mappers on ordinary DTOs and beat them on blittable collections.

---

## The landscape (and where we sit)

| Library | Mechanism | Speed | Alloc (mapper) | AOT / trim | Completeness as a build error? | Built-in round-trip testing? |
|---|---|---|---|---|---|---|
| **AutoMapper** | Runtime, compiled expression trees | Moderate | Allocates | Poor | No | No |
| **Mapster** | Runtime codegen **or** source gen | Fast | Low (codegen) | Codegen mode only | No | No |
| **Mapperly** | Pure Roslyn source generator | Fastest tier | Zero | Excellent | Opt-in warning | No |
| **DwarfMapper** | Pure Roslyn source generator + blit/SIMD fast-path | Fastest tier; **faster on blittable collections** | Zero | Excellent (CI-verified) | **Yes, error by default** | **Yes** |

**Honest performance claim:** Mapperly already emits `dest.A = src.A`, which is the JIT floor for name-based mapping. On typical DTOs (strings, references, transforms) DwarfMapper produces the *same* direct-assignment shape and measures within run-to-run noise of Mapperly / hand-written (see [COMPARISON](docs/COMPARISON.md)). DwarfMapper pulls ahead only where the data is **blittable and layout-compatible** — there it copies whole spans with `MemoryMarshal`/`Unsafe`/SIMD instead of per-element field writes. We never claim to be "faster than everything"; we claim to be *as fast as the floor on ordinary DTOs, faster only on blittable/layout-identical collections, and far harder to get wrong.*

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
- **Completeness:** every destination member must be paired or carry `[MapIgnore]`. Otherwise → **build error** (always — suppressing the diagnostic in `.editorconfig` doesn't ship an incomplete map: the method body simply isn't generated, so the build still fails, just with a rawer compiler error; see *Completeness diagnostics* below). Every source member must be consumed or explicitly marked source-ignored. *"I forgot to map it" stops compiling.*
- **Blit proof:** for each contiguous run, the generator asks: are these segments unmanaged, identically sized, layout-compatible, and transform-free? If yes, it plans a bulk copy (`MemoryMarshal.Cast` / `Unsafe.CopyBlock` / SIMD; whole-span for collections). If no, it plans direct assignments.

### 4. Emit
The generator writes direct assignments, inline converters, and null guards for the ordinary members, and bulk/SIMD copies for the proven runs. The emitted code reads like something you'd have hand-written.

---

## Quick start

Start with two plain classes — your domain type and the shape you want to map it to. Mark the **source** with `[MapTo]` and the generator gives you an extension method — no mapper class, nothing to register, no reflection. (One `net10.0` package bundles the attributes, generator, and IDE code fixes — reference `DwarfMapper`; it's a release candidate, so install with `--prerelease`. See [Status](#status).)

```csharp
// Program.cs — a complete program (top-level statements + the two classes)
using DwarfMapper;

var person = new Person { Name = "Ada", Age = 42 };

PersonDto dto = person.MapTo<PersonDto>();   // or person.ToPersonDto()
Console.WriteLine($"{dto.Name}, {dto.Age}"); // Ada, 42

[MapTo(typeof(PersonDto))]
public class Person
{
    public required string Name { get; set; }   // `required` keeps the default `Nullable` context warning-free
    public int Age { get; set; }
}

public class PersonDto
{
    public required string Name { get; set; }
    public int Age { get; set; }
}
```

That's the whole loop: **two plain classes, one attribute, one call.** The generated body is exactly what you'd write by hand — `new PersonDto { Name = person.Name, Age = person.Age }`.

**The payoff:** add an `Email` property to `PersonDto` and forget to map it, and **the build fails** — `DWARFR02` names the unmapped member (`Email`). (The squiggle sits on the `[MapTo]` source type. You'll also see `CS1061: 'Person' does not contain 'MapTo'` — that's expected: the generator withholds the extension method until the map is complete, so the `DWARFR02` is the real cause, not a missing `using`.) You cannot silently ship an incomplete map. Don't want a member mapped? Put `[MapIgnore]` on it.

`[MapTo]` carries the full conversion engine — number/string/enum/date conversions, nested objects, and `T[]`/`List<T>` collections — and one source can target several DTOs with per-member control (see [No mapper class — `[MapTo]`](#no-mapper-class--mapto) below).

### When you need a mapper class

For custom converters (`Use=`), before/after hooks, reference-cycle handling, projections, or `[RoundTrip]` verification, declare a `partial` class marked `[DwarfMapper]` with empty `partial` methods — the generator fills in the bodies, and your source/target types stay plain POCOs:

```csharp
[DwarfMapper]
public partial class CustomerMapper
{
    [MapProperty(nameof(Customer.FullName), nameof(CustomerDto.Name))]                            // rename
    [MapProperty(nameof(Customer.Total), nameof(CustomerDto.Total), Use = nameof(FormatMoney))]  // custom conversion
    [Flatten(nameof(Customer.Address))]                                                          // Address.City -> City
    public partial CustomerDto ToDto(Customer src);

    [MapIgnore(nameof(Customer.PasswordHash))]   // explicit, intentional, audited
    public partial Customer FromDto(CustomerDto dto);

    private static string FormatMoney(decimal d) => d.ToString("C");
}
```

If `CustomerDto` gains a `Region` property and you don't map or ignore it, **the build fails** — deliberate, not nagging: forgetting to wire up a new property is the single most common real-world mapping bug, and here it can't ship. The fix is one attribute: map it, or `[MapIgnore(nameof(CustomerDto.Region))]` to drop it on purpose (audited, not silent). Both forms run the same **sort → pair → prove → emit** engine and completeness gate — reach for `[MapTo]` on ordinary DTOs, the mapper class when you need converters, hooks, cycles, or projections. The rest of this page is the reference.

### Declaring maps without a method per pair (`[GenerateMap]`)

For low-ceremony migration — e.g. replacing AutoMapper's `CreateMap<A, B>()` — annotate the mapper **class** with `[GenerateMap<TSource, TTarget>]` instead of writing a `partial` method for every pair. The generator emits a `public TTarget Map(TSource src)` overload per declared pair, with the **same** completeness gate, conversions, nested/collection handling, and hooks as a declared method. The source/target types stay plain POCOs (no attributes on them):

```csharp
[DwarfMapper]
[GenerateMap<Order, OrderDto>]
[GenerateMap<Customer, CustomerDto>]
public partial class Mappers { }

// usage — overload resolved by the source type:
OrderDto dto = new Mappers().Map(order);
```

Overloads are distinguished by source type; declaring two pairs with the same source type but different targets is an ambiguous-overload compile error — declare those as named `partial` methods instead.

### No mapper class — `[MapTo]`

The default, lowest-ceremony form (introduced in the Quick start): put `[MapTo]` on the **source type** and call the generated extension — no `partial` class, nothing to instantiate. One source can target **several DTOs**, and per-member directives are **stacked and read in source order, each aligned to the matching `[MapTo]` target** — so a member can be renamed differently per target, or mapped in some and ignored in others:

```csharp
using DwarfMapper;

[MapTo(typeof(OrderDto), typeof(OrderSummary))]
public class Order
{
    [MapProperty("Name"), MapProperty("FullName")]  // OrderDto.Name ; OrderSummary.FullName
    public string FullName { get; set; }

    [MapProperty("Total"), MapIgnore]               // mapped into OrderDto ; ignored for OrderSummary
    public decimal Total { get; set; }
}

OrderDto    a = order.MapTo<OrderDto>();
OrderSummary b = order.ToOrderSummary();
```

The same completeness gate applies (every destination member must be satisfied, or it's a build error), and the **full conversion engine** is reused — numeric widening/narrowing, `string`↔`T` parse/format, enums, nested objects, and `T[]`/`List<T>` collections (including collections of nested objects) all work, generating the same helpers as the class model. `[MapProperty]` and `[MapIgnore]` are the same attributes the mapper class uses — on a source member they take the destination-name (member) form.

What stays on the `[DwarfMapper]` class model (`[MapTo]` emits a diagnostic pointing you there): `Use=` custom converters, hooks, projections, `[MapDerivedType]`/`[FlattenGraph]`, reference cycles, `Dictionary<,>`, and nullable-unwrap. Full design: [`docs/superpowers/specs/2026-06-24-dwarfmapper-v23-type-registry-mapping.md`](docs/superpowers/specs/2026-06-24-dwarfmapper-v23-type-registry-mapping.md).

### Four ways to call a mapper

The mapper is a stateless, allocation-free class, so use whichever style fits:

```csharp
// 1. Instance — new it (free; it holds no state) or inject it.
var dto = new OrderMapper().ToDto(order);

// 2. Convenience extension method (generated by default, in DwarfMapper.Extensions).
using DwarfMapper.Extensions;
var dto = order.ToOrderDto();          // named after the target type; forwards to a cached singleton

// 3. Dependency injection (when Microsoft.Extensions.DependencyInjection is referenced).
services.AddDwarfMappers();             // registers every [DwarfMapper] in the assembly as a singleton
// ...then inject OrderMapper directly.

// 4. Ambient facade — the AutoMapper `IMapper.Map<T>` drop-in (DI or the static Instance).
IDwarfMapper mapper = DwarfMapperFacade.Instance;   // AddDwarfMappers() also registers IDwarfMapper for DI
var dto = mapper.Map<OrderDto>(order);              // resolves from the process-wide registry
```

- **Ambient `IDwarfMapper.Map<TDest>(src)`** is a near-verbatim drop-in for AutoMapper's `IMapper.Map<TDest>(src)` — ideal for migrating many call sites without writing an aggregate mapper. It resolves from a process-wide registry that **public** `[GenerateMap<S,T>]` / `[DwarfMapper]` mappers self-register into at load time (via a `[ModuleInitializer]`), so it works **across assemblies you don't reference**. Mark one assembly `[assembly: DwarfMapperValidationRoot]` and the generator verifies at **build time** that every `Map<TDest>(src)` call site has a provider in the reference graph (`DWARF061`). Registration runs in each provider assembly's `[ModuleInitializer]`, so `DWARF061` proves the *graph*, not runtime *load order* — call the generated `DwarfMap.Validate()` at startup to also fail-fast when a provider hasn't loaded yet (trimming / lazy-load); otherwise `Map<TDest>` can still throw `DwarfMapMissingException`. Collections project at the call site: `list.Select(mapper.Map<Dst>)`. Full guide: [ambient cross-assembly maps](docs/howto/ambient-cross-assembly-maps.md).

- **Extension methods** are generated for every simple `TTarget Map(TSource)` method, named `To<TargetType>()`, backed by a cached stateless instance. They live in the `DwarfMapper.Extensions` namespace (one `using` to surface them). Opt a mapper out with `[DwarfMapper(GenerateExtensions = false)]`. Update-into, span, async-streaming, projection, and derived-dispatch methods, and any pair whose generated name would collide, are skipped.
  > **Cross-assembly note:** these `[DwarfMapper]`-class `To<Target>()` extensions are **assembly-internal by default**, so `order.ToOrderDto()` resolves only inside the assembly that declares the mapper. To call them from another project, set `[assembly: DwarfMapperOptions(PublicExtensions = true)]` — extensions then become `public` for pairs whose source **and** target types are both public (pairs involving a non-public type stay internal, for accessibility safety). Or call the mapper **instance** / use **DI** (`AddDwarfMappers()`), which always work across assemblies. *(The `[MapTo]`-registry extensions — `x.MapTo<Dto>()` — are a separate front door and are already `public` for public types, so they work cross-assembly with no opt-in.)*
- **`AddDwarfMappers()`** is generated only when your project references `Microsoft.Extensions.DependencyInjection.Abstractions`. It registers each mapper as a singleton (no reflection, no assembly scan) — AOT-safe like everything else.
- **Thread-safe.** Generated mappers hold no mutable state; `DwarfMapperFacade.Instance` / `IDwarfMapper` and the `DwarfMapperRegistry` are safe for concurrent use (the registry is a `ConcurrentDictionary`); and reference-handling (`Preserve`/`SetNull`) allocates a fresh per-call context. So a single mapper instance — or the DI singleton, or the shared facade — can map from any number of threads at once.

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
- **Naming conventions**: `[DwarfMapper(NameConvention = NameConvention.Flexible)]` makes `PascalCase`, `camelCase`, `snake_case` and `UPPER_CASE` interchangeable for auto-matching (names are normalized by stripping `_` and lowercasing) — e.g. `user_name → UserName`. A post-normalization collision (two source members reducing to one target) is the build error `DWARF048`; explicit `[MapProperty]` stays exact.
- **`[MapProperty(source, target)]`** maps differently-named members and suppresses the completeness error for that destination; an unknown source/target is `DWARF009`/`DWARF008`.
- **Convention — `nameof` vs string literals.** Use `nameof(Type.Member)` for plain member names (refactor-safe). **Dotted paths** — a deep source path (`"Customer.Name"`) or an unflatten target (`"Address.City"`) — and **constructor-parameter names** must be **string literals**, because `nameof` can't express a path or a parameter name. So a mapper commonly mixes the two styles, by design: `[MapProperty(nameof(Src.Total), "Order.Total")]`.
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

- **Nested objects** map automatically when you declare a mapping method for the nested type pair; it is found by signature and called (`Home = ToDto(o.Home)`). When no such method is declared, **auto-nesting** synthesizes one for the nested pair (on by default; class-level `[DwarfMapper(AutoNest = false)]` turns it off, and the per-method `[AutoNest(false)]` attribute disables it for a single mapping method even when the class enables it). An abstract/interface source type under auto-nesting is `DWARF033` — declare an explicit method or `[MapDerivedType]` instead.
- **Custom conversions** use `[MapProperty(src, tgt, Use = nameof(Method))]`, where `Method` takes the source member type and returns the destination type.
- **Null substitution & conditions**: `[MapProperty(src, tgt, NullSubstitute = "(none)")]` emits `src ?? "(none)"` for a nullable source (direct members; the value is type-checked → `DWARF049`). `[MapProperty(src, tgt, When = nameof(Predicate))]` guards the assignment — `if (Predicate(s)) dst.X = …;` — where `Predicate` is a `bool` method taking the source (the member keeps its default when false; an invalid predicate is `DWARF050`).
- **Deep source paths**: the source may be a **dotted path** that reads through the object graph — `[MapProperty("Customer.Name", nameof(Dto.CustomerName))]` emits `s.Customer.Name` (member names never contain dots, so the path is unambiguous). The leaf type goes through the same conversion rules (`Use=` works too). An unresolvable segment is `DWARF043`; a **nullable interior hop** (which can throw `NullReferenceException` at runtime) is the `DWARF044` suggestion.
- **Unflattening (dotted target)**: the *target* may also be a single-level dotted path — `[MapProperty(nameof(Src.City), "Address.City")]` assigns `City` through an intermediate `Address`, which is instantiated once if null (`if (t.Address is null) t.Address = new …();`). Multiple leaves into the same intermediate share one instance. The intermediate must be a **class with a public parameterless constructor** (else `DWARF045`); a direct mapping of the same root conflicts (`DWARF046`). Deeper-than-one-level paths are a `DWARF045` (documented follow-up).
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
| **enum ↔ enum** (by value) | `E1:short → E2:int` | `(E2)Int32.CreateChecked((short)v)` — via **both** enums' actual underlying types | Throws `OverflowException` on overflow |
| **enum ↔ string** | `Color.Red ↔ "Red"` | `switch` | No reflection |
| **enum ↔ integral** | `Color:byte → int`, `enum:uint → long`, `int → Status:short` | `Int32.CreateChecked((byte)v)` — cast through the enum's **actual declared underlying** type (`byte`/`short`/`uint`/`long`/…, never a fixed `int`), then checked | Runtime throws `OverflowException` on overflow. In a **projection**, a genuine widening (`short→int`) inlines a plain cast, but any narrowing — including a same-width unsigned→signed like `uint→int` — is `DWARF028` (SQL can't range-check) |
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

**Nullable values.** A nullable value-type source mapped to a non-nullable destination (`int? → int`) is unwrapped per `NullStrategy`: **throw on null** (default) or `[DwarfMapper(NullStrategy = NullStrategy.SetDefault)]` to use the destination default. A non-nullable source mapped to a nullable-value-type destination (`long → int?`, `string → int?`) is handled automatically — the inner conversion (e.g. `CreateChecked`, `Parse`) runs, and the result is implicitly lifted to `T?`; overflow and format errors still propagate. Nullable-source + nullable-target (`long?→int?`, `E1?→E2?`) is also handled automatically and is null-preserving: a null source maps to a null target, and a non-null value runs the inner conversion (out-of-range still throws, e.g. `OverflowException`) — see the `T? → U?` row in the conversion table above.

**Collections.** `T[]`, `List<T>`, and `HashSet<T>` members map element-by-element, applying the same conversion rules per element (nested objects, converters, enums, nullable unwrap, even nested collections). When the element type is unchanged, the whole collection is bulk-copied (`T[]` → `T[]` clone, `→ List<T>`/`HashSet<T>` bulk constructor). Read-only source shapes (`IEnumerable<T>`, `IReadOnlyList<T>`, `IReadOnlySet<T>`, …) are accepted as sources. A **null source collection** never throws: by default (`[DwarfMapper(NullCollections = NullCollectionStrategy.AsEmpty)]`) it produces an empty target collection; set `NullCollections = NullCollectionStrategy.AsNull` to propagate null instead (the target member must then be a nullable reference type). An unsupported collection/dictionary target type is `DWARF027`.

**Flattening.** `[Flatten("Address")]` pulls a complex source member's sub-members up to top-level destination members of the same name (`Address.City → City`). The flattened leaf goes through the same conversion rules (converters, enums, nullable). One level deep; a null flatten root throws at runtime. Unknown root → `DWARF016`; a name flattened from two roots → `DWARF017`.

**Reverse mapping.** Annotate a forward method with `[ReverseMap]` and declare a separate inverse partial method (`Entity FromDto(Dto d)` for a forward `Dto ToDto(Entity e)`) — the inverse **inherits the inverted simple `[MapProperty]` renames** (`A → B` becomes `B → A`), so you don't restate them. Only single-member renames invert automatically; non-invertible forward config (`Use=` converters, dotted paths, `NullSubstitute`/`When`) is `DWARF051` (declare those reverse renames explicitly), and a `[ReverseMap]` with no inverse method is `DWARF052`. An inverse method's own `[MapProperty]` takes precedence over an inherited one.

**Additional mapping parameters.** A map method may declare **extra parameters after the source** — `public partial Dto Map(Entity e, string tenant)` — each matched to a destination member **by name** (case-insensitively) and emitted directly: `Tenant = tenant`. Precedence is `[MapProperty]`/`[MapValue]` > extra parameter > by-name source member. An extra parameter that matches no destination is the `DWARF047` suggestion. Extra parameters are **not** propagated into nested mappings (the nested mapper keeps its single-source signature) — pass values explicitly where needed.

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

**Projection (IQueryable).** A partial method `IQueryable<TDto> Project(IQueryable<T> src)` generates `src.Select(s => new TDto { … })` — an expression tree your ORM translates to SQL. **Projection is provably translatable:** directly-assignable members, `[MapProperty]` renames/`[MapIgnore]`, enum→int casts, nested objects (`new TInnerDto { … }`, recursively), collections (`.Select(…).ToList()/.ToArray()`), and dotted-path source flattening (`[MapProperty("A.B", …)]`) are all inlined into the expression tree. Only members needing a runtime conversion — narrowing, string-parse, enum-by-name, a custom `Use=`, a non-translatable collection/dictionary target (`HashSet`/`ISet`/immutable/`Dictionary`), or reference handling — are rejected as `DWARF028` (with a reason); do those with a runtime mapper instead.

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
Unmapped destination members are a `DWARF` build error — always. Completeness is enforced **by construction**, not merely by the diagnostic's severity: suppressing the diagnostic in `.editorconfig` doesn't ship an incomplete map — the method body isn't generated, so the build still fails (with a rawer compiler error instead of the helpful `DWARF001`). This is the compile-time replacement for the manual "did I wire everything up?" review.

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

**Conformance sample.** On top of the generator/integration test suite, [`samples/DwarfMapper.Conformance`](samples/DwarfMapper.Conformance) is a single runnable app that exercises *every* feature — flat/rename/conversions, enum strategies, nested/collections, projection, all three cycle strategies, `[MapTo]`, `[Flatten]`/`[FlattenGraph]`, `[Reinterpret]`, hooks, `[ReverseMap]`, ambient registry, `[RoundTrip]` — plus adversarial "dirty paths" (null source → `ArgumentNullException`, narrowing overflow → `OverflowException` with no silent truncation, bad parse → `FormatException`, unguarded cycle → throw), with **47 runtime assertions**. It doubles as living documentation.

---

## Security model

DwarfMapper treats mapping as an attack surface and a correctness surface at once:

- **Zero reflection / AOT- & trim-safe.** The shipped runtime library uses no reflection or runtime emit; the optional `DwarfMapper.Testing` package (test projects only) is reflection-based. CI runs a NativeAOT + trimming build and fails on any reflection fallback.
- **Over-posting protection.** Only members the generator explicitly resolved are written. Ambiguous matches are a build error, never a silent guess — so untrusted input can't drive an unintended field assignment.
- **Provably-safe blit.** The unsafe fast-path is emitted only behind analyzer-verified proofs; otherwise it degrades to safe assignments.
- **Completeness diagnostics.** No silent data loss in either direction.

### Supply chain (CRA-aligned)

Releases are built and published to be independently verifiable:

- **Deterministic, reproducible builds** (`Deterministic` + `ContinuousIntegrationBuild`) with SourceLink and embedded untracked sources.
- **Machine-readable SBOM** (CycloneDX) generated in CI and attached to every release.
- **Keyless signing — no private key stored anywhere.** Releases are signed via GitHub's OIDC identity through Sigstore (`actions/attest-build-provenance`) and recorded in a public transparency log; authenticity = each artifact's **SHA-256 fingerprint** (`SHA256SUMS`) + the **git identity** that built it (verify with `gh attestation verify`).
- **Hardened CI**: all GitHub Actions pinned to commit SHAs; CodeQL, NativeAOT/trim gate, and coverage gate.

See [`docs/RELEASING.md`](docs/RELEASING.md) for step-by-step verification (fingerprint, provenance/identity, SBOM).

---

## Performance

- **Direct-assignment path:** identical codegen to the fastest source-gen mappers — the JIT floor.
- **Blittable fast-path:** contiguous unmanaged, layout-matched runs are bulk-copied; collections of such types are copied whole-span via `MemoryMarshal.Cast<TSrc, TDest>` and SIMD where it helps.

---

## Packages

DwarfMapper is **.NET 10 only**. The one exception is the generator/code-fix assemblies (see note below).

| Reference | Purpose | TFM |
|---|---|---|
| **`DwarfMapper`** | **Everything you need in one package** — the attributes + tiny abstractions (`lib/net10.0`) **and** the Roslyn source generator + IDE code fixes bundled in the analyzer slot. Zero runtime dependencies. | `net10.0` |
| `DwarfMapper.Testing` *(optional — **test projects only**)* | Fixture builders, seeded property-based fuzzer, round-trip verifier, informed dumps. Add to **test** projects to enable `[RoundTrip]`. **It is reflection-based — do not reference it from a shipped/AOT-published assembly** (there is no build-time guard; doing so reintroduces reflection and voids the AOT-safe guarantee). | `net10.0` |

```xml
<!-- one reference enables compile-time mapping + the IDE quick-fixes; no analyzer wiring -->
<PackageReference Include="DwarfMapper" Version="1.0.0-rc.1" />
```

\* The bundled generator/code-fix assemblies target `netstandard2.0` because Roslyn loads analyzers/source generators *into the compiler host*, which only accepts `netstandard2.0` components. This is a hard requirement of the Roslyn toolchain, not a runtime-support choice — the code it **generates** targets .NET 10 like everything else. They are a `DevelopmentDependency` (analyzer-only) and never become a runtime dependency of your app.

Released packages ship with a CycloneDX SBOM, `SHA256SUMS`, and a keyless GitHub-identity provenance signature (no stored key) — see [`docs/RELEASING.md`](docs/RELEASING.md) to verify.

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
  DwarfMapper.CorpusTests/       # real-world mapping parity corpus
benchmarks/
  DwarfMapper.Benchmarks/    # BenchmarkDotNet suite (vs hand-written + Mapperly/Mapster/AutoMapper)
samples/
  DwarfMapper.AotSample/     # NativeAOT + trimming gate sample
  DwarfMapper.AotBench/      # NativeAOT benchmarking & stability harness
  DwarfMapper.Gallery/       # runnable simple→advanced mapping examples
  DwarfMapper.Conformance/   # one runnable app exercising every feature + adversarial paths (47 asserts)
docs/                        # COMPARISON.md, SECURITY.md, design specs + plans
README.md
```

---

## Roadmap

### v1 — shipped (feature-complete, pre-release)
- **`[MapTo]` registry mapping** — the default low-ceremony form: no `partial` class, intent on the source type, called as an extension (`x.MapTo<Dto>()`); full conversion engine (scalars / nested / `T[]`·`List<T>`), multi-target with stacked `[MapProperty]`/`[MapIgnore]`
- Flat, nested, and collection (`T[]` / `List<T>` / `HashSet<T>` / `Dictionary<K,V>`) mapping
- Enums and null handling
- Custom per-member conversion: `[MapProperty(src, tgt, Use = nameof(Method))]`
- Canonical sort → ordinal pairing
- Completeness diagnostics (build error by construction — configuring its severity can’t ship an incomplete map)
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
- **In-repo BenchmarkDotNet suite** (`benchmarks/DwarfMapper.Benchmarks`): DwarfMapper vs. hand-written **and vs. Mapperly / Mapster / AutoMapper 14** across flat / nested / collection / blit scenarios (see [`docs/COMPARISON.md`](docs/COMPARISON.md) for the full capability/testing/migration comparison, [`docs/MIGRATION.md`](docs/MIGRATION.md) for the feature-by-feature conversion guide from AutoMapper 14 / Mapster / Mapperly, and [`docs/CORRECTNESS.md`](docs/CORRECTNESS.md) for the proves-your-mappings-are-right guarantees); builds in CI, run locally for numbers
- **Async streaming**: `IAsyncEnumerable<D> Map(IAsyncEnumerable<S> src)` lazily transforms an async sequence element-by-element (emitted as an `async` iterator; streaming/back-pressure preserved)
- **Pair-scoped member configuration (no `partial` method)**: `[MapProperty<TSource, TTarget>(...)]`, `[MapIgnore<TTarget>(...)]`, and `[MapValue<TTarget>(...)]` declared on the class configure a `[GenerateMap]` pair — or an auto-synthesized nested / collection-element pair — so a fully-configured mapper (including nested renames) can be declared with **zero partial methods**. The linkage applies wherever that pair is mapped; an attribute matching no mapped pair is `DWARF056`. Consume via the generated extension methods or `AddDwarfMappers()` DI:
  ```csharp
  [DwarfMapper]
  [GenerateMap<Place, PlaceDto>]
  [MapProperty<Person, PersonDto>(nameof(Person.Name), nameof(PersonDto.FullName))]  // nested-element rename
  public partial class Mappers { }   // no methods; place.ToPlaceDto() / services.AddDwarfMappers()
  ```
- **Co-located mapping (no `partial`, no `[DwarfMapper]` on the type)**: a class that carries `[GenerateMap<S,T>]` (plus pair-scoped `[MapProperty<,>]`/`[MapIgnore<,>]`) but is **not** a `[DwarfMapper]` mapper gets its mapping emitted into a *separate* generated `<Host>Mapper` type. So a DTO can declare its own mapping inline, stay a plain (even `sealed`, non-`partial`) data class, and be consumed via the generated extension / `AddDwarfMappers()` DI. Delete the type and its mapping goes with it. (Trade-off: the host takes a compile-time dependency on the other type.)
  ```csharp
  [GenerateMap<Person, PersonDto>]
  [MapProperty<Person, PersonDto>(nameof(Person.Name), nameof(PersonDto.FullName))]
  public sealed class PersonDto { public string FullName { get; set; } public int Age { get; set; } }
  // no [DwarfMapper], no partial — person.ToPersonDto() works; the generated PersonDtoMapper is the DI singleton
  ```

### Releasing
- Tagging `v*` builds, tests, signs (keyless GitHub OIDC), SBOMs, and cuts a **GitHub Release** with the signed packages; **nuget.org publishing is done manually by the maintainer** from those artifacts. `v1.0.0-rc.1` is the first published release.

---

## License

**GNU General Public License v2.0 only** (`SPDX: GPL-2.0-only`).

DwarfMapper is free and open source, and it is **strong copyleft on purpose**. You may use, study, modify, and redistribute it freely. But note what that means *for this kind of library specifically*:

> DwarfMapper is a **source generator** — it emits code directly into your assembly. Under GPLv2, software you build and distribute on top of it is a derivative work and must itself be released under GPLv2, with corresponding source made available to whoever you distribute it to.

In plain terms: **if you build on DwarfMapper and ship it, your project is GPLv2 too, and your users get the source.** That is the point. The library stays free, and anyone who profits from it does so in the open, where its origin is visible.

Caveats, stated honestly:
- **The generated-output derivative-work question is legally unsettled.** The reading above — that code the generator *emits into your assembly* (which contains no DwarfMapper library code) makes your project a derivative work — is the **conservative** interpretation. Whether it actually holds for source-generator *output* is genuinely contested and untested in court. This is guidance, not legal advice; if your use is commercially sensitive, get your own counsel rather than relying on this summary.
- **SaaS is not distribution.** A company running DwarfMapper inside a hosted service never ships binaries, so GPLv2 does not compel them to publish source. (Closing that loophole would require AGPL — a deliberate choice we did *not* make.)
- **GPLv2-only**, not "v2 or later" — the version is fixed.
- GPLv2-only is incompatible with Apache-2.0 and GPLv3, which constrains what third-party code can be pulled in later. DwarfMapper has zero dependencies, so this is a non-issue today.

The full license text lives in [`LICENSE`](https://github.com/GimliCZ/DwarfMapper.NET/blob/master/LICENSE). Every source file carries an SPDX header (`// SPDX-License-Identifier: GPL-2.0-only`).

## Status

**Feature-complete v1 — pre-release (`1.0.0-rc.1`).** The generator, all documented attributes, and the testing toolkit are built and covered by tests; APIs are stabilising. Packages build, keyless-sign (GitHub identity), and ship via a tag-triggered pipeline that cuts a signed GitHub Release ([`docs/RELEASING.md`](docs/RELEASING.md)); nuget.org publishing is manual. Being a release candidate, install it with the `--prerelease` flag. Feedback on rough edges welcome.

## Name

Dwarves mine with precision and forge what they find. DwarfMapper mines your types at compile time and forges the copies — small, exact, and unwilling to leave a seam unmapped.
