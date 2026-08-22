<!-- SPDX-License-Identifier: GPL-2.0-only -->
# DwarfMapper options cheat-sheet

Every knob in one place. Class-level options go on `[DwarfMapper(...)]`; per-member attributes go on the
mapping method. Defaults are chosen so the out-of-the-box behaviour is the safe, strict one (completeness enforced, null/cycle throw by default).

Set a house style once for the whole assembly with `[assembly: DwarfMapperDefaults(...)]` — every mapper
inherits those values unless it sets its own. **Precedence: mapper > assembly defaults > built-in default.**
The policy options layer (`CaseInsensitive`, `NameConvention`, `EnumStrategy`, `EnumStringSource`, `NullStrategy`, `NullCollections`,
`ImplicitConversions`, `RequiredMapping`, `AllowNonPublic`, `AutoNest`, `AutoMatchMembers`, `IgnoreObsoleteMembers`,
`SkipNullSourceMembers`, `RegisterCollectionShapes`); per-graph knobs (`MaxDepth`, `ReferenceHandling`, `OnCycle`) stay per-mapper.

## Class-level options — `[DwarfMapper(...)]`

<!-- table: class-options -->
| Option | Type | Default | What it does |
|---|---|---|---|
| `CaseInsensitive` | `bool` | `false` | Match member names ordinal-ignore-case. Ambiguity → `DWARF010`. |
| `NameConvention` | `NameConvention` | `Exact` | `Flexible` matches across `PascalCase` ↔ `camelCase` ↔ `snake_case` ↔ `UPPER_CASE`. Collision → `DWARF048`. |
| `EnumStrategy` | `EnumStrategy` | `ByName` | Enum↔enum mapping by member name (`ByName`) or underlying value (`ByValue`). Missing by-name member → `DWARF015`. For enum↔**string**, a member's `[EnumMember(Value="…")]` (else `[Description("…")]`, else its identifier) is used as the string form — so `InProgress` can serialize as `"in_progress"` with no custom converter. Non-`[Flags]` enums only. Switch that off with [`EnumStringSource`](#class-level-options--dwarfmapper) when the annotations are for display. |
| `NullStrategy` | `NullStrategy` | `Throw` | Nullable-value source → **non-nullable** target when null: `Throw`, or `SetDefault` (use the destination default). It governs that case only: when the target **can hold the null** — a `Nullable<T>` or a nullable-annotated reference — the null is *lifted* (null in, null out) regardless of this setting, including across a nested pair whose two sides are declared with different kinds, and per element of a mapped collection or dictionary. The same lift holds through `Project`: an `IQueryable` projection cannot express `NullStrategy` at all, so a nullable source into a target that cannot hold the null is refused at build time with `DWARF028` rather than answered differently from `Map`. |
| `NullCollections` | `NullCollectionStrategy` | `AsEmpty` | Null source collection → `AsEmpty` (never throws) or `AsNull` (propagates null **only** when the target member is nullable — a nullable reference or a nullable value-type collection like `ImmutableArray<T>?`; a non-nullable target silently degrades to `AsEmpty`). |
| `AutoNest` | `bool` | `true` | Auto-synthesize a private mapper for a nested `(S,T)` pair with no declared method. `false` requires explicit declarations. |
| `SkipNullSourceMembers` | `bool` | `false` | A null source member never overwrites the destination's default: emits `if (src.X is not null) dst.X = …;` for nullable-source, post-construction-settable members. The equivalent of AutoMapper's `ForAllMembers(o => o.Condition((_,_,src) => src != null))`. Non-nullable value-type sources and `required`/`init`-only targets are unaffected. **Narrow it to one pair or one method with [`[MapNullSkip]`](#mapnullskip--patch-merge-for-one-pair-or-one-method).** |
| `AllowNonPublic` | `bool` | `false` | Opt in to using non-public but reachable **constructors AND members** — an `internal`/`protected internal` ctor, getter, or setter in the same assembly or one exposed via `[InternalsVisibleTo]`. `private`/`protected` are never usable (the generated code could not compile). Off by default: an internal ctor/accessor is non-public on purpose, so reaching it from a mapper should be a deliberate, stated choice. |
| `ReferenceHandling` | `ReferenceHandlingStrategy` | `None` | `None` (depth-guarded, zero alloc) or `Preserve` (full topology reconstruction). |
| `OnCycle` | `OnCycleStrategy` | `Throw` | In `None` mode: `Throw` (catchable depth exception) or `SetNull` (break cycles ≡ `System.Text.Json` IgnoreCycles). Ignored under `Preserve` → `DWARF037`. |
| `MaxDepth` | `int` | `64` | Depth bound for recursion-capable pairs; throws `DwarfMappingDepthException` instead of a silent `StackOverflowException`. Hard cap `1000`. |
| `ImplicitConversions` | `bool` | `true` | `true`: non-lossless conversions are applied but surface `DWARF038` (Info). `false`: they become build errors (Mapperly-strict). |
| `RequiredMapping` | `RequiredMappingStrategy` | `Target` | `Target`: every destination member must be mapped. `Both`: also require every source member to be read (`DWARF039`). |
| `GenerateExtensions` | `bool` | `true` | Emit `source.ToTarget()` convenience extension methods (namespace `DwarfMapper.Extensions`). `false` suppresses them for this mapper. |
| `AutoMatchMembers` | `bool` | `true` | `false` = explicit-only (trust-boundary guard): nothing is wired by name, every member needs `[MapProperty]`/`[MapValue]` or `[MapIgnore]`, and a would-be auto-match raises `DWARF072`. Stops an untrusted same-named field (e.g. `IsAdmin`) over-posting onto a protected member. |
| `IgnoreObsoleteMembers` | `bool` | `false` | Drop `[Obsolete]` members from mapping: an obsolete destination is neither required nor auto-populated, an obsolete source needn't be consumed (no `DWARF039`). An explicit `[MapProperty]`/`[MapValue]` still opts a specific one back in. |
| `RegisterCollectionShapes` | `bool` | `true` | Also register each declared object map into the ambient registry under the common **collection shapes**, so `IDwarfMapper.Map<ICollection<TTarget>>(listOfSources)` resolves without declaring a separate collection pair. Emitted at compile time — no reflection, no runtime synthesis. Six rows per pair, all keyed on `IEnumerable<TSource>`, so a `List`, an array, a `HashSet` and a lazy LINQ iterator are served by one entry. `false` keeps the table minimal for a mapper never reached through the facade over a collection. |
| `EnumStringSource` | `EnumStringSource` | `Attribute` | Which text an enum member maps to and from when the other side is a `string`. `Attribute` (default): `[EnumMember(Value="…")]`, else `[Description("…")]`, else the identifier — so `InProgress` ↔ `"in_progress"` with no converter. `Identifier`: always the C# member name, exactly as `Enum.ToString()`/`Enum.Parse` use it. **The one-line answer to `DWARF083`**: `[Description]` is overwhelmingly a *display* annotation, and under the default it silently becomes the *persistence* format — a store full of `"NextDay"` starts receiving `"Next-Day"`. Applies to both directions and to `[assembly: DwarfMapperDefaults]`. Non-`[Flags]` enums only (a `[Flags]` string form is the comma-joined list `Enum.ToString` builds from identifiers, so the two settings agree). |
<!-- endtable -->

> **`CaseInsensitive` and `NameConvention` interact** — they both govern how member names are matched, so set
> one or the other. `NameConvention.Exact` honours `CaseInsensitive` (`Exact` + `CaseInsensitive=true` =
> ordinal-ignore-case). `NameConvention.Flexible` already normalizes case for auto-matching, so `CaseInsensitive`
> is largely redundant under it (it still applies to `[Flatten]` leaves). Prefer `Exact` (optionally with
> `CaseInsensitive`) **or** `Flexible`, not surprising combinations of both.

### Strategy enum values

| Enum | Values |
|---|---|
| `EnumStrategy` | `ByName` (default), `ByValue` |
| `EnumStringSource` | `Attribute` (default), `Identifier` |
| `NullStrategy` | `Throw` (default), `SetDefault` |
| `NullCollectionStrategy` | `AsEmpty` (default), `AsNull` |
| `ReferenceHandlingStrategy` | `None` (default), `Preserve` |
| `OnCycleStrategy` | `Throw` (default), `SetNull` |
| `RequiredMappingStrategy` | `Target` (default), `Both` |
| `NameConvention` | `Exact` (default), `Flexible` |

All of these live in the single `DwarfMapper` namespace — one `using DwarfMapper;` brings in every attribute,
enum, and `DwarfMappingDepthException`.

## Assembly-level options — `[assembly: DwarfMapperOptions(...)]`

<!-- table: assembly-options -->
| Option | Type | Default | What it does |
|---|---|---|---|
| `PublicExtensions` | `bool` | `false` | Emit generated convenience extensions as **`public`** (cross-assembly) for pairs whose source and target types are both public — pairs involving a non-public type stay assembly-internal for safety. **Governs BOTH extension emitters:** the aggregate facade in `DwarfMapper.Extensions` *and* the `[MapTo]` registry's per-source `__DwarfRegistry_<Source>` class. A library that ships `[MapTo]` types for another assembly to consume **must** set this, because `source.MapTo<TTarget>()` is the only way to invoke a registry map — there is no mapper instance to fall back on. The opt-in for the layered "mappers in a library, consumed elsewhere" layout. |
<!-- endtable -->

## Per-member / per-method attributes

Put these on the mapping method (or the class, where noted).

> **`nameof` vs string literals:** use `nameof(Type.Member)` for plain member names (refactor-safe). Dotted
> paths (`"Customer.Name"`, `"Address.City"`) and constructor-parameter names must be **string literals** —
> `nameof` can't express a path or a parameter name — so mixing the two styles in one attribute is normal.

| Attribute | Use |
|---|---|
| `[MapProperty(src, tgt)]` | Rename. `src`/`tgt` may be dotted paths (deep read / single-level unflatten). |
| `[MapProperty(src, tgt, Use = nameof(M))]` | Custom conversion via a named method (`M(srcType) → tgtType`). |
| `[MapProperty(src, tgt, NullSubstitute = v)]` | Emit `src ?? v` for a nullable source. |
| `[MapProperty(src, tgt, When = nameof(P))]` | Guard the assignment with `bool P(S)`. |
| `[MapProperty(src, tgt, StringFormat = "F2")]` | Format an `IFormattable` source into a `string` member: `src.ToString("F2", InvariantCulture)`. Non-string target / non-formattable source / with `Use=` → `DWARF073`. |
| `[MapValue(tgt, "const")]` / `[MapValue(tgt, Use = nameof(M))]` | Constant or computed (parameterless `M`) value for a source-less member. |
| `[MapCollectionKey("Items", "Id")]` | **Update-into only.** Merge a `List<T>` member by key instead of replacing it: matched keys update the slot, new keys are added, unmatched existing elements are kept. v1: same element type both sides. Out of scope → `DWARF074`. |
| `[MapIgnore("Member")]` | Intentionally drop a destination member (suppresses `DWARF001`). Class- or method-level. |
| `[MapIgnoreSource("Member")]` | Source-side mirror (under `RequiredMapping = Both`). |
| `[MapProperty<TSource, TTarget>(src, tgt)]` | **Class-level, pair-scoped** rename/convert (`Use`/`NullSubstitute`/`When` too). Configures a `[GenerateMap]` pair — or an auto-synthesized nested/collection-element pair — with **no partial method**. Matches nothing → `DWARF056`. |
| `[MapIgnore<TTarget>("Member")]` | **Class-level, pair-scoped** ignore (suppresses `DWARF001`) for any pair targeting `TTarget`. Matches nothing → `DWARF056`. |
| `[MapValue<TTarget>(tgt, const)]` / `[MapValue<TTarget>(tgt) { Use = … }]` | **Class-level, pair-scoped** constant/computed value for a source-less member of `TTarget`. Lets a `[GenerateMap]` pair be completed with no method. Matches nothing → `DWARF056`. |
| `[MapConstructor<TSource, TTarget>(nameof(Factory))]` | **Class-level, pair-scoped** `ConstructUsing`: names a factory `TTarget Factory(TSource)` on the mapper; settable members are then filled from source. Invalid factory → `DWARF059`; matches nothing → `DWARF056`. |
| `[MapNullSkip<TSource, TTarget>]` / `[MapNullSkip<TSource, TTarget>(false)]` | **Class-level, pair-scoped** `SkipNullSourceMembers` for that one pair, overriding the mapper and assembly setting. |
| `[MapNullSkip]` / `[MapNullSkip(false)]` | **Method-level** `SkipNullSourceMembers` for that one mapping method. |
| `[Flatten("Root")]` | Pull a complex member's sub-members up to same-named destination members. |
| `[FlattenGraph(...)]` | Collapse an object graph to a flat collection. |
| `[MapDerivedType<TDerivedSrc, TDerivedDst>]` | Polymorphic dispatch arm on a base-type method. |
| `[ReverseMap]` | Inherit inverted simple renames onto a declared inverse method. |
| `[RoundTrip]` | Emit a fuzz-driven round-trip verifier (needs `DwarfMapper.Testing`). |
| `[BeforeMap]` / `[AfterMap]` | Lifecycle hooks (validate the source / fill computed or ignored members). |
| `[Reinterpret("Member")]` | Force the blittable/SIMD bulk-copy fast-path on an array member. |
| `[AutoNest(false)]` | Disable auto-nesting for a single method even when the class enables it. |
| `[DwarfMapperConstructor]` | Disambiguate which constructor to use on an immutable target. |

## How a method becomes a converter

Three routes, in precedence order. Only the first two are explicit, and the third is worth understanding
because it acts at a distance.

1. **Named** — `[MapProperty(src, tgt, Use = nameof(M))]`. `M` converts that one member and nothing else.
2. **Pair factory** — `[MapConstructor<S,T>(nameof(F))]`. `F` constructs that pair's target and nothing else.
3. **Adopted by signature** — any method on the mapper whose signature converts `S` to `T` is used
   automatically wherever that conversion is needed.

Route 3 is a deliberate feature: it is the replacement for AutoMapper's `ConvertUsing` /
`ITypeConverter<S,D>`, and it is why a plain `Dst Convert(Src s)` on the mapper simply works with no
attribute. Two consequences follow from it, and both are enforced:

- **A method dedicated by route 1 or 2 is withheld from route 3.** Naming a converter for a member states
  that it belongs to that member; it is not an offer to convert every pair of those types. The reservation is
  **mapper-wide**, not per-method — a sibling method that declares no `Use=` of its own does not get to
  borrow one.
- **Two matching methods are a build error** (`DWARF013`), never an arbitrary choice. Picking one would make
  the mapping depend on declaration order.

> **Why this is spelled out.** Both rules exist because their absence produced silent data loss in a real
> migration: a `string BuildDocumentId(Guid)` written for one member was also serving a plain `Guid`→`string`
> member on the same type, and a `[MapConstructor]` factory was adopted as a collection's element converter,
> mapping the whole collection to blanks. Both behind green builds. The behaviour is now pinned by
> `ConverterAdoptionPolicyTests`.

Adding an unrelated helper to a mapper class can therefore change existing maps, if its signature happens to
fit a conversion nothing else provides. That is the cost of the convenience; the reservation rules bound it
to methods you have not already dedicated elsewhere.

## `[MapNullSkip]` — patch-merge for one pair or one method

`SkipNullSourceMembers` is a **policy** option: it applies to every map on the mapper (or, via
`[DwarfMapperDefaults]`, every mapper in the assembly). That is the right default — one statement of intent
reads better than the same attribute repeated per pair.

But a mapper often has genuinely both kinds of map: a **replace** map where a `null` means *"clear this"*, and
a **patch** map where a `null` means *"leave this alone"*. `[MapNullSkip]` narrows the option to a single pair
or method so those can live together:

<!-- fence-exempt: contrasts two methods on ONE class to make the scoping point; no single sample file holds both -->
```csharp
[DwarfMapper]
public partial class SettingsMappers
{
    // Full replace: a null in the DTO clears the destination member.
    public partial void Replace(SettingsDto src, Settings dst);

    // Patch-merge: a null in the DTO leaves the destination member alone.
    [MapNullSkip]
    public partial void Patch(SettingsDto src, Settings dst);
}
```

`[MapNullSkip(false)]` carves one map out of a class that enables the option, and
`[MapNullSkip<TSource, TTarget>]` does the same for attribute-declared `[GenerateMap]` pairs.

> **Why it exists.** AutoMapper's `ForAllMembers(o => o.Condition((_,_,src) => src != null))` was configured
> **per map**. Translating a profile that mixed both kinds therefore meant splitting it across two mapper
> classes purely to carry one boolean — and that split had a real consequence: a nested pair reached from both
> classes was synthesized twice, once guarded and once not, silently.

## See also

- [`../README.md`](../README.md) — prose reference with examples for most options (this table is the complete list).
- [`diagnostics.md`](diagnostics.md) — every `DWARF…` diagnostic, what triggers it, and how to fix it.
- [`MIGRATION.md`](MIGRATION.md) — how each AutoMapper / Mapster / Mapperly option maps to the above.
- [`howto/`](howto/) — task-oriented migration walkthroughs.
