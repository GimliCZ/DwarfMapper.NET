<!-- SPDX-License-Identifier: GPL-2.0-only -->
# How-to: migrate from AutoMapper to DwarfMapper

A step-by-step walkthrough for moving a codebase off **AutoMapper 14.0.0** (the last MIT release).
AutoMapper is a runtime expression-tree/reflection engine with a fluent builder and DI registration;
DwarfMapper is a compile-time generator with attribute-only config. This is the heaviest of the four
migrations — but most of the weight is *deleting* AutoMapper's runtime machinery, not rewriting maps.

> Read [common-changes.md](common-changes.md) first. For the exhaustive feature table (every `ForMember`
> option, every divergence), keep [`../MIGRATION.md` §1](../MIGRATION.md#1-from-automapper-1400) open
> alongside this guide.

---

## The one mental-model shift

**AutoMapper has a runtime configuration object; DwarfMapper does not.** There is no `MapperConfiguration`,
no `IMapper`, no `Profile`, no `AddAutoMapper(assembly)` scan. The generated `partial class` you write *is*
the configuration, resolved at compile time. So the migration is: take everything you expressed in a
`Profile` and re-express it as attributes on one class — then delete the runtime plumbing.

> **Migrating a large codebase with many `_mapper.Map<Dto>(src)` call sites?** You don't have to rewrite them
> or write an aggregate mapper. DwarfMapper ships an ambient facade — **`IDwarfMapper`**, with
> `Map<TDest>(object source)` — that is a near-verbatim drop-in for AutoMapper's `IMapper`. Swap the injected
> `IMapper` for `IDwarfMapper` (registered for you by `AddDwarfMappers()`, or use `DwarfMapperFacade.Instance`),
> declare each pair once with `[GenerateMap<Src, Dto>]` on a **public** class — it self-registers into a
> process-wide registry at load time, **even across assemblies you don't reference** — and mark one assembly
> `[assembly: DwarfMapperValidationRoot]` so the build fails (`DWARF062`) if any `Map<T>(src)` call site has no
> provider (instead of a runtime `DwarfMapMissingException`). Call sites stay nearly verbatim. Full guide:
> [ambient cross-assembly maps](ambient-cross-assembly-maps.md).

---

## Step 1 — Reference the packages

Follow [common-changes.md §1](common-changes.md#change-1--reference-the-packages-target-net10). Add
the single `DwarfMapper` package, target `net10.0`. Leave the AutoMapper package in place for now —
you'll remove it at the end (Step 8) so the codebase keeps compiling mid-migration.

## Step 2 — Turn each Profile into a partial mapper class

This is the headline diff. A `Profile` full of `CreateMap` calls becomes a `partial class` full of
`[GenerateMap]` attributes:

```diff
- public class MappingProfile : Profile
- {
-     public MappingProfile()
-     {
-         CreateMap<Order, OrderDto>();
-         CreateMap<Customer, CustomerDto>();
-         CreateMap<Address, AddressDto>();
-     }
- }
+ [DwarfMapper]
+ [GenerateMap<Order, OrderDto>]
+ [GenerateMap<Customer, CustomerDto>]
+ [GenerateMap<Address, AddressDto>]
+ public partial class Mappers { }
```

It's a near find-and-replace: `CreateMap<A, B>();` → `[GenerateMap<A, B>]`. If a pair carries member
config (`.ForMember(...)`), declare it as a **named partial method** instead so you have somewhere to hang
the attributes (Step 4):

```csharp
[DwarfMapper]
public partial class Mappers
{
    [MapProperty(nameof(Customer.FullName), nameof(CustomerDto.Name))]
    public partial CustomerDto ToDto(Customer src);
}
```

## Step 3 — Delete the runtime plumbing

These have no equivalent and aren't needed — the generated class is already the config and the completeness check:

```diff
- services.AddAutoMapper(typeof(MappingProfile).Assembly);   // no assembly scan (AOT-safe by design)
- var cfg = new MapperConfiguration(c => c.AddProfile<MappingProfile>());
- cfg.AssertConfigurationIsValid();                          // redundant — completeness is now a build error
- cfg.CompileMappings();                                     // there is no runtime compile step
```

Register the concrete mapper instead, or just `new` it (it's stateless):

```diff
- services.AddAutoMapper(...); /* inject IMapper */
+ services.AddSingleton<Mappers>();   // inject Mappers directly; or `new Mappers()` at the call site
```

## Step 4 — Translate `ForMember` and friends

Every fluent member option maps to an attribute. The common ones:

| AutoMapper 14 | DwarfMapper |
|---|---|
| `.ForMember(d => d.X, o => o.MapFrom(s => s.Y))` (rename) | `[MapProperty(nameof(S.Y), nameof(D.X))]` |
| `.MapFrom(s => s.A.B)` (nested source) | `[MapProperty("A.B", nameof(D.X))]` (dotted path; nullable hop → `DWARF044`) |
| `.MapFrom(s => Compute(s.Y))` | `[MapProperty(nameof(S.Y), nameof(D.X), Use = nameof(Compute))]` |
| `.Ignore()` | `[MapIgnore(nameof(D.X))]` — **required**, unmapped = `DWARF001` |
| `.MapFrom(_ => "const")` | `[MapValue(nameof(D.X), "const")]` |
| `.MapFrom(_ => Compute())` (no source) | `[MapValue(nameof(D.X), Use = nameof(Compute))]` (`Compute` is **parameterless**) |
| `.NullSubstitute(v)` | `[MapProperty(src, tgt, NullSubstitute = v)]` (emits `src ?? v`) |
| `.Condition(s => p)` / `.PreCondition(...)` | `[MapProperty(src, tgt, When = nameof(P))]` (`bool P(S)`; member keeps default when false) |
| `.ForCtorParam("p", o => o.MapFrom(s => s.Y))` | `[MapProperty(nameof(S.Y), "p")]` (targets the ctor param by name) |

The mechanical rule: **lambdas become named methods.** A `MapFrom(s => Compute(s.Y))` becomes a private
method `Compute(srcMemberType) -> destType` referenced by `Use=`. A `Condition(s => …)` becomes a
`bool`-returning method referenced by `When=`.

```csharp
[DwarfMapper]
public partial class CustomerMapper
{
    [MapProperty(nameof(Customer.FullName), nameof(CustomerDto.Name))]
    [MapProperty(nameof(Customer.Total), nameof(CustomerDto.Total), Use = nameof(FormatMoney))]
    public partial CustomerDto ToDto(Customer src);

    [MapIgnore(nameof(Customer.PasswordHash))]
    public partial Customer FromDto(CustomerDto dto);

    private static string FormatMoney(decimal d) => d.ToString("C");
}
```

### Resolvers and type converters

| AutoMapper | DwarfMapper |
|---|---|
| `IValueResolver` / `IMemberValueResolver` (per member) | `[MapProperty(src, tgt, Use = nameof(M))]` — `M` sees the **source member**, not whole-source/dest/context |
| `.ConvertUsing(s => …)` / `ITypeConverter<S,D>` (type pair) | a non-partial `D Convert(S s)` method on the mapper — user methods win over synthesis |
| built-in scalar coercions (often need config) | **built-in, richer, and stricter** — see Step 5 |

There is **no `ResolutionContext`**. If a resolver needed extra data, pass it as an extra method parameter
(`partial Dto Map(Entity e, string tenant)` matches `tenant` to a `Tenant` dest member by name).

### Flattening — now explicit

AutoMapper flattens `Customer.Name → CustomerName` by naming convention automatically. DwarfMapper makes
flattening **explicit** (no silent name-splitting, which is a mislinking risk):

```diff
- // AutoMapper: automatic if names align
+ [Flatten(nameof(Customer.Address))]                          // Address.City -> City
+ // or per-leaf:
+ [MapProperty("Customer.Name", nameof(Dto.CustomerName))]
```

## Step 5 — You can probably delete custom converters

AutoMapper often needs explicit converters for scalar mismatches. DwarfMapper handles a large set
**built-in** and AOT-safe (no reflection): `int↔long` (checked), `string↔IParsable` (InvariantCulture),
`enum↔{enum,string,int}`, `DateTime↔string` (round-trip "o"), nullable lift `T→T?`, and more. So a chunk of
your `ConvertUsing`/`IValueConverter` code is now dead — delete it and let synthesis handle it.

Two things to know:

- A **non-lossless** auto-conversion (e.g. `long→int` narrowing, `string→int` parse) is applied but surfaces
  a **`DWARF038` suggestion** so it's visible, not silent. Want them to be hard errors instead?
  `[DwarfMapper(ImplicitConversions = false)]` flips them to build errors (Mapperly-strict).
- **float/double/decimal → int is never silent** — it still requires an explicit `Use=` converter (no
  silent fractional truncation, ever).

Full conversion table: the [repository README, "Built-in scalar conversions"](../../README.md).

## Step 6 — ReverseMap, enums, collections, null

| AutoMapper | DwarfMapper | Note |
|---|---|---|
| `.ReverseMap()` | `[ReverseMap]` on the forward method **+** an explicit inverse `partial A ToA(B)` | inverts simple renames automatically; non-invertible config (`Use=`, dotted, `When`) → `DWARF051`; missing inverse method → `DWARF052` |
| enum **by value** (AM default) | `[DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]` | DwarfMapper defaults to **by name** — set this for AM parity |
| `AllowNullCollections = true` | `[DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]` | DwarfMapper defaults to `AsEmpty`; this matches AM's default |
| `ForAllMembers(o => o.Condition((_,_,src) => src != null))` (skip null source members / patch-merge) | `[DwarfMapper(SkipNullSourceMembers = true)]` | a null source member keeps the destination's default (`if (src.X is not null) dst.X = …`) — the "don't clobber with nulls" guard. Distinct from a **per-member** `.Condition`, which is `[MapProperty(src, tgt, When = …)]` (above) |
| `.PreserveReferences()` | `[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]` | full topology reconstruction |
| `.MaxDepth(n)` | `[DwarfMapper(MaxDepth = n)]` | throws catchable `DwarfMappingDepthException`, never a silent StackOverflow |
| `query.ProjectTo<Dto>(cfg)` | `partial IQueryable<Dto> Project(IQueryable<S> q)` | direct members, renames, ignores, enum→int casts, nested objects, collections, and dotted-path flattening (`[MapProperty("A.B", …)]`) all translate; only non-translatable conversions (narrowing/parse/by-name/`Use=`/`HashSet`·dict) are `DWARF028` |

`.ReverseMap()` example:

```csharp
[DwarfMapper]
public partial class OrderMapper
{
    [ReverseMap]
    [MapProperty(nameof(Order.FullName), nameof(OrderDto.Name))]
    public partial OrderDto ToDto(Order o);

    public partial Order FromDto(OrderDto d);   // inherits the inverted Name → FullName rename
}
```

## Step 7 — Build, and clear DWARF001

Build. AutoMapper validated completeness only when you called `AssertConfigurationIsValid()` (and only in a
test); DwarfMapper validates it **every build**. Expect the first build to list destination members your
profiles silently dropped. For each: map it, `[MapValue]` it, or `[MapIgnore]` it. See
[common-changes.md §4](common-changes.md#change-4--clear-the-build-the-dwarf001-wall).

This is the moment the migration pays for itself — you're seeing, for the first time, exactly which fields
were never being mapped.

## Step 8 — Verify, then remove AutoMapper

1. Add `[RoundTrip]` to your forward/back pairs and call `VerifyRoundTrip_*` from a test
   ([common-changes.md §5](common-changes.md#change-5--prove-the-swap-was-lossless)).
2. For extra confidence, keep AutoMapper for one release and assert `oldMap(x)` ≡ `newMap(x)` over real inputs.
3. Remove the AutoMapper `PackageReference`, the `Profile` classes, and the `AddAutoMapper` registration.
4. Delete the now-redundant `AssertConfigurationIsValid()` tests.

---

## Known divergences & non-goals (AutoMapper-specific)

DwarfMapper deliberately does **not** do these (each has a static replacement; full list in
[`../MIGRATION.md` §1.9 + §4](../MIGRATION.md#19-reference-handling--depth--projection--validation--naming)):

- **Open generics** (`CreateMap(typeof(S<>), typeof(D<>))`) — declare one `[GenerateMap<S<Foo>, D<Foo>>]`
  per closed type you actually use.
- **Assembly scanning** (`AddAutoMapper(asm)`) — registration is explicit (this is what keeps it AOT-safe).
- **`ResolutionContext.Items`/`State`** — use an extra typed method parameter.
- **Per-call config** (`mapper.Map(src, o => o.AfterMap(...))`) — declared `[AfterMap]`, or act at the call site.
- **`IncludeAllDerived()` / runtime `Map(obj, srcType, destType)`** — list each `[MapDerivedType<DS, DD>]` arm explicitly (no reflection discovery).
- **Deep merge into existing nested objects** — `Update(src, dest)` preserves the *top-level* identity but **replaces** nested members/collections.
- **`SetMappingOrder` / `RecognizePrefixes` / `ShouldMapProperty` predicates** — order is deterministic; use `[MapProperty]`/`[MapIgnore]` explicitly.

Every one surfaces a diagnostic or has a typed alternative — none fail silently.
