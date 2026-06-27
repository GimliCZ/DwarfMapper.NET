<!-- SPDX-License-Identifier: GPL-2.0-only -->
# DwarfMapper Gallery

A runnable progression of mapping examples, **simplest first**, each in its own self-contained, annotated
file. Run them all:

```bash
dotnet run --project samples/DwarfMapper.Gallery
```

| # | File | Shows |
|---|------|-------|
| 01 | [`01_FlatMap.cs`](01_FlatMap.cs) | The simplest map — `[GenerateMap<A,B>]`, same names/types. |
| 02 | [`02_Rename.cs`](02_Rename.cs) | Renaming a member with `[MapProperty(nameof(...), nameof(...))]`. |
| 03 | [`03_BuiltInConversions.cs`](03_BuiltInConversions.cs) | Automatic type conversions (widening, enum-by-name). |
| 04 | [`04_Nested.cs`](04_Nested.cs) | Nested objects via auto-nesting. |
| 05 | [`05_Collections.cs`](05_Collections.cs) | Lists & arrays, element-by-element + bulk copy. |
| 06 | [`06_DeepPaths.cs`](06_DeepPaths.cs) | **Deep property access** — `"Customer.Address.City"` (what others do with a lambda). |
| 07 | [`07_Flatten.cs`](07_Flatten.cs) | `[Flatten("Address")]` lifts sub-members to the top level. |
| 08 | [`08_CustomConversion.cs`](08_CustomConversion.cs) | **Custom logic** via `Use = nameof(Method)` (the method body is the "lambda"). |
| 09 | [`09_ConditionalAndValue.cs`](09_ConditionalAndValue.cs) | `When=`, `NullSubstitute=`, and `[MapValue]`. |
| 10 | [`10_RecordTarget.cs`](10_RecordTarget.cs) | Mapping into an immutable `record` (constructor binding). |
| 11 | [`11_Projection.cs`](11_Projection.cs) | `IQueryable` projection — the one place a `Select(s => …)` lambda is **generated**. |
| 12 | [`12_Ergonomics.cs`](12_Ergonomics.cs) | The generated `x.ToGemDto()` extension and `AddDwarfMappers()` DI. |
| 13 | [`13_NestedListConfig.cs`](13_NestedListConfig.cs) | **Configuring a nested/collection-element map** on the class — rename `Person.Name → PersonDto.FullName` inside a `List<Person>`. |
| 14 | [`14_NestedListConfigErgonomic.cs`](14_NestedListConfigErgonomic.cs) | Same scenario with **zero partial methods**: pair-scoped `[MapProperty<Person, PersonDto>]` on the class carries the nested rename; `[GenerateMap]` + `moria.ToPlaceDto()` do the rest. |
| 15 | [`ex15/`](ex15/) | **Co-located mapping, no `partial`/`[DwarfMapper]`** — `[GenerateMap]` + `[MapProperty<,>]` live on a plain `sealed` DTO (`dtos/PersonDto.cs`); the generator emits a separate `PersonDtoMapper`. Delete the DTO and its mapping goes too. |

## Which declaration style should I use?

There are four ways to declare a mapping. They share one engine; pick by what the pair needs:

| Style | Use it when | Trade-off |
|---|---|---|
| **Partial method** — `[DwarfMapper] partial class` + `partial TTarget ToX(TSource)` (ex 02–12) | you need a specific method name, **`[RoundTrip]`**, **projection**, **span**, **async streaming**, or **extra parameters** (these are signature-driven) | most ceremony; a signature-only method reads as a "data holder" when the pair needs no config |
| **`[GenerateMap<A,B>]`** on a `[DwarfMapper]` class (ex 01) | low-ceremony bulk declaration (AutoMapper `CreateMap` migration) | one `Map` overload per **source** type — you can't map one source to two targets on the same class |
| **Pair-scoped** `[MapProperty<,>]` / `[MapIgnore<,>]` / `[MapValue<,>]` (ex 14) | configure a `[GenerateMap]` pair (including a **nested/collection element**) with **no method** | config lives on the mapper class, not next to the method |
| **Co-located** on a plain DTO — no `[DwarfMapper]`, no `partial` (ex 15) | you want the mapping to **live with the type** and disappear when you delete it | the DTO takes a compile-time dependency on the source type; the generated mapper type is **assembly-internal** and so are its `x.ToDto()` extensions by default. For cross-assembly use, opt the extensions public with `[assembly: DwarfMapperOptions(PublicExtensions = true)]` (works when both types are public; the generated mapper type itself stays internal) |

**Recommended default for a new project:** `[GenerateMap<A,B>]` + the generated `a.ToBDto()` extension (ex 14/15) — the lowest ceremony. Reach for a partial method only when you need one of the signature-driven features above. The call name is `Map(source)` for a `[GenerateMap]` pair, your chosen method name for a declared partial, or `source.ToTarget()` for the generated extension.

## The "lambda" note

DwarfMapper is attribute-based: you never write a lambda to reach a property. The deep/computed access that
AutoMapper and Mapster express with `s => s.A.B.C` is expressed here as **dotted string paths** (06), **named
`Use=` methods** (08), **`[Flatten]`** (07), and — the only place a lambda is actually emitted — **`Project`**
(11), where the generator writes the `Select` expression tree for you. Each advanced file shows the
before→after against a lambda mapper in its header comment.

See also: [`../../docs/options.md`](../../docs/options.md) (all options) ·
[`../../docs/howto/`](../../docs/howto/) (migration guides) ·
[`../../docs/diagnostics.md`](../../docs/diagnostics.md) (every `DWARF…` rule).
