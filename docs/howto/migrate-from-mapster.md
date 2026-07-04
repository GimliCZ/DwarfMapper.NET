<!-- SPDX-License-Identifier: GPL-2.0-only -->
# How-to: migrate from Mapster to DwarfMapper

A step-by-step walkthrough for moving off **Mapster 10.x**. Mapster is convention-first and zero-config at
the call site (`src.Adapt<Dst>()`), with `TypeAdapterConfig` for overrides. Because the happy path has *no
declarations to port*, this migration is mostly **adding explicit declarations** — one attribute line per
pair you actually use.

> Read [common-changes.md](common-changes.md) first. Exhaustive feature table:
> [`../MIGRATION.md` §2](../MIGRATION.md#2-from-mapster-10x).

---

## The one mental-model shift

**Mapster maps by convention on demand; DwarfMapper requires you to declare each pair, once, at compile
time.** Mapster's `src.Adapt<Dst>()` "just works" for any two shapes at runtime. DwarfMapper trades that
implicitness for a compile-time guarantee: you list the pairs you use, and in return every one is
completeness-checked and AOT-safe. (Mapster's runtime mode is **not** AOT-safe — this is the main reason to
switch.)

---

## Step 1 — Reference the packages

Follow [common-changes.md §1](common-changes.md#change-1--reference-the-packages-target-net10):
the single `DwarfMapper` package, target `net10.0`. Keep Mapster installed until Step 6.

## Step 2 — Inventory the pairs you actually `Adapt`

Because Mapster needs no per-pair declaration, the first job is to find every `.Adapt<T>()` /
`.Adapt(dest)` / `.ProjectToType<T>()` call and list the `(Source, Dest)` pairs. That list becomes your
`[GenerateMap]` declarations. A quick grep for `.Adapt<` and `.ProjectToType<` finds them.

## Step 3 — Declare the pairs

One attribute line per pair:

```diff
+ [DwarfMapper]
+ [GenerateMap<Order, OrderDto>]
+ [GenerateMap<Customer, CustomerDto>]
+ public partial class Mappers { }
```

And replace the call sites (drop the generic arg — the overload is resolved by source type):

```diff
- var dto = order.Adapt<OrderDto>();
+ var dto = mappers.Map(order);          // mappers = new Mappers() or injected
```

For update-into-existing:

```diff
- order.Adapt(existingDto);
+ mappers.Update(order, existingDto);    // declare: public partial OrderDto Update(Order s, OrderDto d);
```

## Step 4 — Port your `TypeAdapterConfig` overrides

Anything you configured via `config.NewConfig<S,D>()` / `ForType<S,D>()` becomes attributes on a named
partial method for that pair:

| Mapster | DwarfMapper |
|---|---|
| `.Map(d => d.X, s => s.Y)` | `[MapProperty(nameof(S.Y), nameof(D.X))]` |
| `.Map(d => d.X, s => Expr(s))` | `[MapProperty(nameof(S.Y), nameof(D.X), Use = nameof(Method))]` |
| `.Ignore(d => d.X)` | `[MapIgnore(nameof(D.X))]` — required (completeness gate) |
| `.IgnoreIf((s, d) => cond, d => d.X)` | `[MapProperty(src, tgt, When = nameof(P))]` |
| `.AfterMapping((s, d) => …)` / `.BeforeMapping(…)` | `[AfterMap]` / `[BeforeMap]` named methods |
| `.MapWith(s => convert(s))` (type pair) | a non-partial `D Convert(S s)` method on the mapper |
| `.ConstructUsing(s => new D(...))` | generator emits the ctor automatically; custom logic → `Use=` / `[AfterMap]` |
| `.AddDestinationTransform(...)` | post-process in `[AfterMap]` or a `Use=` method |

The rule (same as every guide): **lambdas become named methods.**

```csharp
[DwarfMapper]
public partial class Mappers
{
    [MapProperty(nameof(Order.Total), nameof(OrderDto.Total), Use = nameof(FormatMoney))]
    [MapIgnore(nameof(OrderDto.InternalNote))]
    public partial OrderDto ToDto(Order src);

    private static string FormatMoney(decimal d) => d.ToString("C");
}
```

## Step 5 — Match Mapster's global settings

Mapster's global `TypeAdapterConfig.GlobalSettings` knobs map to class-level `[DwarfMapper(...)]` options:

| Mapster | DwarfMapper |
|---|---|
| `NameMatchingStrategy.Flexible` | `[DwarfMapper(NameConvention = NameConvention.Flexible)]` (snake/camel/Pascal/UPPER interchangeable) |
| `NameMatchingStrategy.IgnoreCase` | `[DwarfMapper(CaseInsensitive = true)]` (ambiguity → `DWARF010`) |
| `.MaxDepth(n)` | `[DwarfMapper(MaxDepth = n)]` (catchable depth exception, never silent SO) |
| `.PreserveReference(true)` | `[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]` (full topology) |
| enum mapping (by **value** default) | `[DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]` for parity (DwarfMapper defaults to **by name**) |
| `ProjectToType<Dst>()` (EF) | `partial IQueryable<Dst> Project(IQueryable<S> q)` — translatable subset (nested/collection/enum-cast/dotted supported; non-translatable → `DWARF028`) |
| `Mapster.Tool` codegen step | **not needed** — DwarfMapper is always source-gen, no separate tool/CLI |

> **Watch the enum default.** Mapster maps enums by value; DwarfMapper maps by **name** by default. If you
> relied on by-value, set `EnumStrategy = EnumStrategy.ByValue`, or you'll get `DWARF015` where member names
> differ.

## Step 6 — Build, clear DWARF001, verify, remove Mapster

1. Build. Clear the `DWARF001` completeness errors — these are members Mapster was silently leaving at their
   default. Map / `[MapValue]` / `[MapIgnore]` each.
   ([common-changes.md §4](common-changes.md#change-4--clear-the-build-the-dwarf001-wall).)
2. Verify with `[RoundTrip]` and/or assert old≡new over real inputs
   ([common-changes.md §5](common-changes.md#change-5--prove-the-swap-was-lossless)).
3. Remove the Mapster `PackageReference`, any `TypeAdapterConfig` setup, and the `Mapster.Tool` build step
   if you used one.

---

## Known divergences & non-goals (Mapster-specific)

- **`.IgnoreNullValues(true)`** — DwarfMapper's `Update` **replaces**; it does not skip null-source members.
  Use `[MapIgnore]` or restructure if you relied on null-skipping merge semantics.
- **`.ConstructUsing`** — the generator selects and emits the constructor itself; custom construction goes
  through `Use=` or `[AfterMap]`, not a factory lambda.
- **Zero-config "map anything at runtime"** — by design you declare each pair. The payoff is the completeness
  gate, AOT-safety, and no first-call expression-compilation cost.
- **Nested deep-merge into existing objects** — `Update` preserves top-level identity but replaces nested
  members/collections.

Each non-goal surfaces a diagnostic or has a typed alternative — see
[`../MIGRATION.md` §4](../MIGRATION.md#4-deliberate-non-goals-all-libraries--and-the-idiomatic-replacement).
