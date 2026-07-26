<!-- SPDX-License-Identifier: GPL-2.0-only -->

# DwarfMapper Gallery

A runnable progression of mapping examples, **simplest first**, each in its own self-contained, annotated
file. Run them all:

```bash
dotnet run --project samples/DwarfMapper.Gallery
```

<!-- table: gallery-index -->
| # | Example | Shows |
|---|---|---|
| | **Basics** | |
| 01 | [`01_FlatMap.cs`](01_FlatMap.cs) — Flat map | the simplest map — `[GenerateMap<A,B>]`, same names and types |
| 02 | [`02_Rename.cs`](02_Rename.cs) — Rename a member | `[MapProperty(nameof(...), nameof(...))]` |
| 03 | [`03_BuiltInConversions.cs`](03_BuiltInConversions.cs) — Built-in conversions | automatic widening and enum-by-name |
| 04 | [`04_Nested.cs`](04_Nested.cs) — Nested objects | auto-nesting a nested `(S,T)` pair |
| 05 | [`05_Collections.cs`](05_Collections.cs) — Collections | lists and arrays, element-by-element and bulk copy |
| | **Configuration** | |
| 06 | [`06_DeepPaths.cs`](06_DeepPaths.cs) — Deep dotted paths | a dotted source path — what others reach with a lambda |
| 07 | [`07_Flatten.cs`](07_Flatten.cs) — Flatten | `[Flatten]` lifts sub-members to the top level |
| 08 | [`08_CustomConversion.cs`](08_CustomConversion.cs) — Custom conversion | `Use = nameof(Method)` — the method body is the "lambda" |
| 09 | [`09_ConditionalAndValue.cs`](09_ConditionalAndValue.cs) — Conditional and constant values | `When=`, `NullSubstitute=`, and `[MapValue]` |
| 10 | [`10_RecordTarget.cs`](10_RecordTarget.cs) — Immutable record target | constructor binding into a record with no parameterless ctor |
| 11 | [`11_Projection.cs`](11_Projection.cs) — IQueryable projection | the one place a `Select` lambda is generated for you |
| 12 | [`12_Ergonomics.cs`](12_Ergonomics.cs) — Extension method and DI | the generated `x.ToGemDto()` and `AddDwarfMappers()` |
| 13 | [`13_NestedListConfig.cs`](13_NestedListConfig.cs) — Configure a collection-element map | renaming a member of the element type inside a `List<T>` |
| 14 | [`14_NestedListConfigErgonomic.cs`](14_NestedListConfigErgonomic.cs) — The same, with no partial methods | pair-scoped `[MapProperty<S,T>]` on the class carries the nested rename |
| | **Front doors** | |
| 15 | [`ex15/15_CoLocated.cs`](ex15/15_CoLocated.cs) — Co-located on the DTO | `[GenerateMap]` on a plain `sealed` DTO — no `partial`, no `[DwarfMapper]` |
| | **Guides** | |
| 30 | [`guides/30_CompositeMapper.cs`](guides/30_CompositeMapper.cs) — A composite mapper | rename, `Use=` conversion, and `[Flatten]` in one mapper |
| 31 | [`guides/31_GenerateMapPairs.cs`](guides/31_GenerateMapPairs.cs) — Several pairs on one class | `[GenerateMap<A,B>]` stacked — the AutoMapper `CreateMap` shape |
| 32 | [`guides/32_FourWaysToCall.cs`](guides/32_FourWaysToCall.cs) — Ways to call a mapper | instance, the generated extension method, and `AddDwarfMappers()` DI |
| 33 | [`guides/33_ExplicitDirectives.cs`](guides/33_ExplicitDirectives.cs) — Satisfying the completeness gate | `[MapProperty]` / `[MapValue]` / `[MapIgnore]` — the three answers to `DWARF001` |
| 34 | [`guides/34_ReverseMapAndHooks.cs`](guides/34_ReverseMapAndHooks.cs) — Inverse maps, injected dependencies, and hooks | `[ReverseMap]`, a primary-constructor dependency in a `Use=` converter, and `[AfterMap]` |
| 35 | [`guides/35_AmbientFacade.cs`](guides/35_AmbientFacade.cs) — The ambient IDwarfMapper facade | mapping when the caller cannot name the concrete mapper type |
<!-- endtable -->

## Which declaration style should I use?

There are four ways to declare a mapping. They share one engine; pick by what the pair needs:

| Style                                                                                          | Use it when                                                                                                                                             | Trade-off                                                                                                                                                                                                                                                                                                                                                             |
|------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Partial method** — `[DwarfMapper] partial class` + `partial TTarget ToX(TSource)` (ex 02–12) | you need a specific method name, **`[RoundTrip]`**, **projection**, **span**, **async streaming**, or **extra parameters** (these are signature-driven) | most ceremony; a signature-only method reads as a "data holder" when the pair needs no config                                                                                                                                                                                                                                                                         |
| **`[GenerateMap<A,B>]`** on a `[DwarfMapper]` class (ex 01)                                    | low-ceremony bulk declaration (AutoMapper `CreateMap` migration)                                                                                        | one `Map` overload per **source** type — you can't map one source to two targets on the same class                                                                                                                                                                                                                                                                    |
| **Pair-scoped** `[MapProperty<,>]` / `[MapIgnore<,>]` / `[MapValue<,>]` (ex 14)                | configure a `[GenerateMap]` pair (including a **nested/collection element**) with **no method**                                                         | config lives on the mapper class, not next to the method                                                                                                                                                                                                                                                                                                              |
| **Co-located** on a plain DTO — no `[DwarfMapper]`, no `partial` (ex 15)                       | you want the mapping to **live with the type** and disappear when you delete it                                                                         | the DTO takes a compile-time dependency on the source type; the generated mapper type is **assembly-internal** and so are its `x.ToDto()` extensions by default. For cross-assembly use, opt the extensions public with `[assembly: DwarfMapperOptions(PublicExtensions = true)]` (works when both types are public; the generated mapper type itself stays internal) |

**Recommended default for a new project:** `[GenerateMap<A,B>]` + the generated `a.ToBDto()` extension (ex 14/15) — the
lowest ceremony. Reach for a partial method only when you need one of the signature-driven features above. The call name
is `Map(source)` for a `[GenerateMap]` pair, your chosen method name for a declared partial, or `source.ToTarget()` for
the generated extension.

## The "lambda" note

DwarfMapper is attribute-based: you never write a lambda to reach a property. The deep/computed access that
AutoMapper and Mapster express with `s => s.A.B.C` is expressed here as **dotted string paths** (06), **named
`Use=` methods** (08), **`[Flatten]`** (07), and — the only place a lambda is actually emitted — **`Project`**
(11), where the generator writes the `Select` expression tree for you. Each advanced file shows the
before→after against a lambda mapper in its header comment.

See also: [`../../docs/options.md`](../../docs/options.md) (all options) ·
[`../../docs/howto/`](../../docs/howto/) (migration guides) ·
[`../../docs/diagnostics.md`](../../docs/diagnostics.md) (every `DWARF…` rule).
