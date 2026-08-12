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
> `[assembly: DwarfMapperValidationRoot]` so the build fails (`DWARF061`) if any `Map<T>(src)` call site has no
> provider (instead of a runtime `DwarfMapMissingException`). Call sites stay nearly verbatim. Full guide:
> [ambient cross-assembly maps](ambient-cross-assembly-maps.md).

---

## Step 0 — Decide these nine things before you convert anything

Every row below is a **behaviour change that compiles cleanly**. Each was found the expensive way during a
real ~300-map migration; several would have shipped wrong data. Read the list, decide each one, and write the
decision down — a migration ledger of "what we chose and why" pays for itself the first time someone asks.

| # | Difference | Default consequence if you do nothing |
|---|---|---|
| 1 | **`NullStrategy` defaults to `Throw`.** AutoMapper silently substituted `default`. | A nullable-value source into a non-nullable target **throws at runtime**. Builds clean, fails in production. Set `NullStrategy = SetDefault` for parity. |
| 2 | **`EnumStrategy` defaults to `ByName`.** AutoMapper matched by **value**. | Enums with different member orders map differently. Set `EnumStrategy = ByValue` for parity. |
| 3 | **enum→string prefers `[EnumMember]`, then `[Description]`, then the identifier.** AutoMapper used `.ToString()`, i.e. always the identifier. | **The sharpest one.** `[Description]` is usually a *display* annotation, but it becomes your *persistence* format. An enum member `Kofi` carrying `[Description("Ko-Fi")]` starts writing `"Ko-Fi"` into a store full of `"Kofi"` — breaking reads of every existing record. `DWARF083` reports it; **`EnumStringSource = Identifier` is the one-line parity switch**, on the mapper or on `[assembly: DwarfMapperDefaults]`, instead of a converter per enum. |
| 4 | **`SkipNullSourceMembers` guards nullable-*typed* members.** AutoMapper's `Condition(src != null)` was a runtime **value** check. | A non-nullable-typed member holding a runtime null is now copied where AutoMapper skipped it. |
| 5 | **enum↔string parsing is case-sensitive.** `Enum.Parse` ignored case. | Hand-edited or legacy data throws instead of parsing. Only reachable if something other than your own writer produced the string. |
| 6 | **`SkipNullSourceMembers` is class-scoped**, but `ForAllMembers` was per-map. | A profile mixing patch-merge maps with ordinary ones must be **split into two mapper classes**. Plan for it rather than discovering it mid-conversion. |
| 7 | **`required` destination members cannot be `[MapIgnore]`d.** AutoMapper built targets reflectively and bypassed the rule. | `DWARF079` tells you so now, with the `[MapValue]` remedy. Before that diagnostic existed, this was the single most-repeated stumble of the migration. |
| 8 | **A `private` "ctor for automapper" stops working.** It only ever worked because AutoMapper constructs by reflection. | `DWARF026`, no escape — `private` is never usable by design. Widen it to `internal` plus `[InternalsVisibleTo]`: the same grant, but one the compiler checks. |
| 9 | **`[MapConstructor]` factories cannot assign `init`-only members.** | The factory's value wins and the source value is dropped (`DWARF080`). Prefer binding the **constructor parameters** — direct construction fills an object initializer, where `init` members *are* assignable. |

Two more that are not behaviour changes but will cost you an afternoon each if you meet them cold:

- **A wall of `CS8795` has two completely different causes.** Either the generator ran and refused (there will
  be `DWARF078` plus real `DWARF…` errors above it — fix those), or the generator never ran in that project
  at all (no DwarfMapper diagnostics whatsoever — see Step 1). Read the `DWARF…` lines first; the `CS8795`s
  are one-per-mapping-method noise either way.
- **Ambient `Map<ICollection<T>>(x)` resolves on the *runtime* type.** See Step 6.

## Step 1 — Reference the packages

Follow [common-changes.md §1](common-changes.md#change-1--reference-the-packages-target-net10). Add
the single `DwarfMapper` package, target `net10.0`. Leave the AutoMapper package in place for now —
you'll remove it at the end (Step 8) so the codebase keeps compiling mid-migration.

### Every project that declares mappers needs the reference — it does not flow

DwarfMapper marks its analyzer `PrivateAssets="all"`, so it deliberately does **not** flow transitively. A
project that references a project that references DwarfMapper does **not** get the generator. Its mappers
simply produce no code, and you get the `CS8795` wall described above with no DwarfMapper diagnostics to
explain it.

**Prove the generator actually runs** in each project rather than assuming the reference resolved — the two
failure modes look identical from the outside. Drop a throwaway mapper in, build, then delete it:

<!-- fence-exempt: a deliberate throwaway smoke test, meant to be deleted; not a sample worth maintaining -->
```csharp
// TEMPORARY — proves the GENERATOR runs, not merely that the assembly is referenced.
// If the analyzer is not wired, this fails with CS8795 and nothing else.
[DwarfMapper]
public partial class __WiringSmokeTest
{
    public partial Target Map(Source s);
}
```

**Centralising the reference across many projects: use `Directory.Build.targets`, not `.props`.** This is not
a style preference. `.props` is imported **before** the project body, so a `<UseDwarfMapper>true</UseDwarfMapper>`
property set inside a `.csproj` does not exist yet when a condition in `.props` is evaluated — the condition
silently never matches. `.targets` is imported **after** the body and sees it. As a bonus, it means you never
have to touch an existing `Directory.Build.props`.

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

<!-- snippet: rename -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    [MapProperty(nameof(Customer.FullName), nameof(CustomerDto.Name))]
    public partial CustomerDto ToDto(Customer c);
}
```
<!-- endsnippet -->

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
| `.NullSubstitute(v)` | `[MapProperty(src, tgt, NullSubstitute = v)]` (emits `src ?? v`). **Direct-assignable members only — not combinable with `Use=`.** For substitute-**and**-convert, use a `Use=` method that handles null, or `[MapIgnore]` + `[AfterMap]`. |
| `.Condition(s => p)` / `.PreCondition(...)` | `[MapProperty(src, tgt, When = nameof(P))]` (`bool P(S)`; member keeps default when false). **`When` sees the source object only** — no destination, no resolved-member value, no pre-/post-resolution timing distinction. Conditions that inspect the destination or the mapped value must move to `[AfterMap]`. |
| `.ForCtorParam("p", o => o.MapFrom(s => s.Y))` | `[MapProperty(nameof(S.Y), "p")]` (targets the ctor param by name) |

The mechanical rule: **lambdas become named methods.** A `MapFrom(s => Compute(s.Y))` becomes a private
method `Compute(srcMemberType) -> destType` referenced by `Use=`. A `Condition(s => …)` becomes a
`bool`-returning method referenced by `When=`.

<!-- snippet: composite-mapper -->
```csharp
[DwarfMapper]
public partial class CustomerMapper
{
    [MapProperty(nameof(Customer.FullName), nameof(CustomerDto.Name))]                           // rename
    [MapProperty(nameof(Customer.Total), nameof(CustomerDto.Total), Use = nameof(FormatMoney))]  // conversion
    [Flatten(nameof(Customer.Address))]                                                          // Address.City -> City
    public partial CustomerDto ToDto(Customer src);

    private static string FormatMoney(decimal d) => d.ToString("C", CultureInfo.GetCultureInfo("en-US"));
}
```
<!-- endsnippet -->

### Resolvers and type converters

| AutoMapper | DwarfMapper |
|---|---|
| `IMemberValueResolver<S,D,TSrcMember,TDestMember>` (**one** source member) | `[MapProperty(src, tgt, Use = nameof(M))]` — `M` takes the **source member** and returns the destination member type. A whole-source `Use=` method is a build error (`DWARF014`). |
| `IValueResolver<S,D,TMember>` (**whole source** — e.g. `FullName = s.First + " " + s.Last`) | `[MapIgnore(nameof(D.Member))]` on that member **+** `[AfterMap] void Fill(S s, D d) => d.Member = …;`. The hook receives the whole source and target. |
| `.ConvertUsing(s => …)` / `ITypeConverter<S,D>` (type pair) | a non-partial `D Convert(S s)` method on the mapper — user methods win over synthesis |
| built-in scalar coercions (often need config) | **built-in, richer, and stricter** — see Step 5 |

There is **no `ResolutionContext`**. If a resolver needed extra *external* data (not a second source member),
pass it as an extra method parameter (`partial Dto Map(Entity e, string tenant)` matches `tenant` to a
`Tenant` dest member by name).

**Resolver / converter that needs DI.** A `[DwarfMapper]` mapper is a normal `partial class` — give it a
constructor and reference the dependency from an **instance** `Use=`/`Convert` method:

<!-- snippet: ctor-injection -->
```csharp
[DwarfMapper]
public partial class RatedOrderMapper(IRateService rates)   // primary constructor
{
    [MapProperty(nameof(Order.FullName), nameof(OrderDto.Name))]
    [MapProperty(nameof(Order.Total), nameof(OrderDto.Total), Use = nameof(ToLocal))]
    [MapValue(nameof(OrderDto.Source), "api-v2")]
    public partial OrderDto ToDto(Order o);

    private decimal ToLocal(decimal amount) => rates.Convert(amount);
}
```
<!-- endsnippet -->

Register the concrete type in DI (`services.AddScoped<RatedOrderMapper>()`). Note the trade-offs: such a mapper
**can't be `new`-ed argument-free**, and it does **not** get the generated `To<Target>()` convenience
extensions (those require a parameterless constructor) — call it via the instance or DI.

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
| enum→string as `.ToString()` (AM default) | `[DwarfMapper(EnumStringSource = EnumStringSource.Identifier)]` | DwarfMapper defaults to the `[EnumMember]`/`[Description]` text — set this for AM parity |
| `AllowNullCollections = true` | `[DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]` | DwarfMapper defaults to `AsEmpty`; this matches AM's default |
| `ForAllMembers(o => o.Condition((_,_,src) => src != null))` (skip null source members / patch-merge) | `[DwarfMapper(SkipNullSourceMembers = true)]` | a null source member keeps the destination's default (`if (src.X is not null) dst.X = …`) — the "don't clobber with nulls" guard. Distinct from a **per-member** `.Condition`, which is `[MapProperty(src, tgt, When = …)]` (above) |
| `.PreserveReferences()` | `[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]` | full topology reconstruction |
| `.MaxDepth(n)` | `[DwarfMapper(MaxDepth = n)]` | throws catchable `DwarfMappingDepthException`, never a silent StackOverflow |
| `query.ProjectTo<Dto>(cfg)` | `partial IQueryable<Dto> Project(IQueryable<S> q)` | direct members, renames, ignores, enum→int casts, nested objects, collections, and dotted-path flattening (`[MapProperty("A.B", …)]`) all translate; only non-translatable conversions (narrowing/parse/by-name/`Use=`/`HashSet`·dict) are `DWARF028` |

`.ReverseMap()` example:

<!-- snippet: reverse-map -->
```csharp
[DwarfMapper]
public partial class ReversibleOrderMapper
{
    [ReverseMap]
    [MapProperty(nameof(Order.FullName), nameof(OrderDto.Name))]
    [MapIgnore(nameof(OrderDto.Source))]
    public partial OrderDto ToDto(Order o);

    public partial Order FromDto(OrderDto d);   // inherits the inverted Name -> FullName rename
}
```
<!-- endsnippet -->

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
- **`IncludeBase<S,T>()`** — there is no inheritance primitive. Restate the shared `[MapProperty]`/`[MapIgnore]`
  on each derived pair. Restatement is explicit and keeps every pair readable at its own declaration; the cost
  is that it can drift, so bracket each restated block with a comment naming the base pair.
- **Object↔collection maps** (`CreateMap<ICollection<Rank>, RanksDocument>()`) — not a mapping shape. Map the
  document's inner collection instead, which is what an AutoMapper `ConstructUsing` that called
  `ctx.Mapper.Map<ICollection<T>>(x.Items)` was literally already doing.
*(`AddDwarfMappers()` + `IDwarfMapper` covers update-into too, as of the facade's
`Map(source, destination)` overload — `mapper.Map(src, existingDest)` is a near-verbatim replacement for
AutoMapper's two-argument `Map`. Declare a two-parameter partial method on a public mapper and it is
registered automatically.)*

### The one that is not a non-goal, but will surprise you: ambient collection maps

`IDwarfMapper.Map<TDest>(object)` resolves through a registry keyed on the **exact** `(source, target)` pair,
and AutoMapper derived collection maps implicitly from the element map. So this compiles and throws:

<!-- fence-exempt: illustrates a runtime failure; a compiling sample cannot show "throws at first use" -->
```csharp
List<DbStore> rows = ...;
var items = _mapper.Map<ICollection<StoreItem>>(rows);   // DwarfMapMissingException
```

Two things make it easy to get wrong, and both cost a false start during the reference migration:

1. **Two keys, often two different types.** The build-time check (`DWARF061`) validates the call site's
   **static** argument type; the runtime registry looks the delegate up by `source.GetType()` and walks base
   types **only — never interfaces**. A method declared `Task<ICollection<T>>` that returns a `List<T>` needs
   *both* pairs declared. Declaring only the static one compiles and still throws.
2. **Declare the collection pair beside its ELEMENT pair, not beside the call site.** If the calling assembly
   does not reference the one that owns the element map, the generator cannot see that element pair's
   configuration and synthesises a fresh convention-only map, which then fails completeness. The ambient
   registry is process-wide, so declaring it beside the element map resolves the far-away call site anyway.

A lazy LINQ source (`Where(...)`, `SelectMany(...)`) can never be declared at all — those are private
`System.Linq` iterator types that no attribute can name. Materialise with `.ToList()` before mapping; that
call is load-bearing, so say so in a comment.

Every one surfaces a diagnostic or has a typed alternative — none fail silently.
