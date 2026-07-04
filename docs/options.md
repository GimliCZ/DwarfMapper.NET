<!-- SPDX-License-Identifier: GPL-2.0-only -->
# DwarfMapper options cheat-sheet

Every knob in one place. Class-level options go on `[DwarfMapper(...)]`; per-member attributes go on the
mapping method. Defaults are chosen so the out-of-the-box behaviour is the safe, permissive one.

## Class-level options — `[DwarfMapper(...)]`

| Option | Type | Default | What it does |
|---|---|---|---|
| `CaseInsensitive` | `bool` | `false` | Match member names ordinal-ignore-case. Ambiguity → `DWARF010`. |
| `NameConvention` | `NameConvention` | `Exact` | `Flexible` matches across `PascalCase` ↔ `camelCase` ↔ `snake_case` ↔ `UPPER_CASE`. Collision → `DWARF048`. |
| `EnumStrategy` | `EnumStrategy` | `ByName` | Enum↔enum mapping by member name (`ByName`) or underlying value (`ByValue`). Missing by-name member → `DWARF015`. |
| `NullStrategy` | `NullStrategy` | `Throw` | Nullable-value source → non-nullable target when null: `Throw`, or `SetDefault` (use the destination default). |
| `NullCollections` | `NullCollectionStrategy` | `AsEmpty` | Null source collection → `AsEmpty` (never throws) or `AsNull` (target must be a nullable reference). |
| `AutoNest` | `bool` | `true` | Auto-synthesize a private mapper for a nested `(S,T)` pair with no declared method. `false` requires explicit declarations. |
| `SkipNullSourceMembers` | `bool` | `false` | A null source member never overwrites the destination's default: emits `if (src.X is not null) dst.X = …;` for nullable-source, post-construction-settable members. The equivalent of AutoMapper's `ForAllMembers(o => o.Condition((_,_,src) => src != null))`. Non-nullable value-type sources and `required`/`init`-only targets are unaffected. |
| `AllowNonPublic` | `bool` | `false` | Opt in to using non-public but reachable **constructors AND members** — an `internal`/`protected internal` ctor, getter, or setter in the same assembly or one exposed via `[InternalsVisibleTo]`. `private`/`protected` are never usable (the generated code could not compile). Off by default: an internal ctor/accessor is non-public on purpose, so reaching it from a mapper should be a deliberate, stated choice. |
| `ReferenceHandling` | `ReferenceHandlingStrategy` | `None` | `None` (depth-guarded, zero alloc) or `Preserve` (full topology reconstruction). |
| `OnCycle` | `OnCycleStrategy` | `Throw` | In `None` mode: `Throw` (catchable depth exception) or `SetNull` (break cycles ≡ `System.Text.Json` IgnoreCycles). Ignored under `Preserve` → `DWARF037`. |
| `MaxDepth` | `int` | `64` | Depth bound for recursion-capable pairs; throws `DwarfMappingDepthException` instead of a silent `StackOverflowException`. Hard cap `1000`. |
| `ImplicitConversions` | `bool` | `true` | `true`: non-lossless conversions are applied but surface `DWARF038` (Info). `false`: they become build errors (Mapperly-strict). |
| `RequiredMapping` | `RequiredMappingStrategy` | `Target` | `Target`: every destination member must be mapped. `Both`: also require every source member to be read (`DWARF039`). |
| `GenerateExtensions` | `bool` | `true` | Emit `source.ToTarget()` convenience extension methods (namespace `DwarfMapper.Extensions`). `false` suppresses them for this mapper. |

> **`CaseInsensitive` and `NameConvention` interact** — they both govern how member names are matched, so set
> one or the other. `NameConvention.Exact` honours `CaseInsensitive` (`Exact` + `CaseInsensitive=true` =
> ordinal-ignore-case). `NameConvention.Flexible` already normalizes case for auto-matching, so `CaseInsensitive`
> is largely redundant under it (it still applies to `[Flatten]` leaves). Prefer `Exact` (optionally with
> `CaseInsensitive`) **or** `Flexible`, not surprising combinations of both.

### Strategy enum values

| Enum | Values |
|---|---|
| `EnumStrategy` | `ByName` (default), `ByValue` |
| `NullStrategy` | `Throw` (default), `SetDefault` |
| `NullCollectionStrategy` | `AsEmpty` (default), `AsNull` |
| `ReferenceHandlingStrategy` | `None` (default), `Preserve` |
| `OnCycleStrategy` | `Throw` (default), `SetNull` |
| `RequiredMappingStrategy` | `Target` (default), `Both` |
| `NameConvention` | `Exact` (default), `Flexible` |

All of these live in the single `DwarfMapper` namespace — one `using DwarfMapper;` brings in every attribute,
enum, and `DwarfMappingDepthException`.

## Assembly-level options — `[assembly: DwarfMapperOptions(...)]`

| Option | Type | Default | What it does |
|---|---|---|---|
| `PublicExtensions` | `bool` | `false` | Emit the generated convenience extensions (`DwarfMapper.Extensions`) as **`public`** (cross-assembly) for pairs whose source and target types are both public — pairs involving a non-public type stay assembly-internal for safety. The opt-in for the layered "mappers in a library, consumed elsewhere" layout. |

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
| `[MapValue(tgt, "const")]` / `[MapValue(tgt, Use = nameof(M))]` | Constant or computed (parameterless `M`) value for a source-less member. |
| `[MapIgnore("Member")]` | Intentionally drop a destination member (suppresses `DWARF001`). Class- or method-level. |
| `[MapIgnoreSource("Member")]` | Source-side mirror (under `RequiredMapping = Both`). |
| `[MapProperty<TSource, TTarget>(src, tgt)]` | **Class-level, pair-scoped** rename/convert (`Use`/`NullSubstitute`/`When` too). Configures a `[GenerateMap]` pair — or an auto-synthesized nested/collection-element pair — with **no partial method**. Matches nothing → `DWARF056`. |
| `[MapIgnore<TTarget>("Member")]` | **Class-level, pair-scoped** ignore (suppresses `DWARF001`) for any pair targeting `TTarget`. Matches nothing → `DWARF056`. |
| `[MapValue<TTarget>(tgt, const)]` / `[MapValue<TTarget>(tgt) { Use = … }]` | **Class-level, pair-scoped** constant/computed value for a source-less member of `TTarget`. Lets a `[GenerateMap]` pair be completed with no method. Matches nothing → `DWARF056`. |
| `[Flatten("Root")]` | Pull a complex member's sub-members up to same-named destination members. |
| `[FlattenGraph(...)]` | Collapse an object graph to a flat collection. |
| `[MapDerivedType<TDerivedSrc, TDerivedDst>]` | Polymorphic dispatch arm on a base-type method. |
| `[ReverseMap]` | Inherit inverted simple renames onto a declared inverse method. |
| `[RoundTrip]` | Emit a fuzz-driven round-trip verifier (needs `DwarfMapper.Testing`). |
| `[BeforeMap]` / `[AfterMap]` | Lifecycle hooks (validate the source / fill computed or ignored members). |
| `[Reinterpret("Member")]` | Force the blittable/SIMD bulk-copy fast-path on an array member. |
| `[AutoNest(false)]` | Disable auto-nesting for a single method even when the class enables it. |
| `[DwarfMapperConstructor]` | Disambiguate which constructor to use on an immutable target. |

## See also

- [`../README.md`](../README.md) — prose reference with examples for most options (this table is the complete list).
- [`diagnostics.md`](diagnostics.md) — every `DWARF…` diagnostic, what triggers it, and how to fix it.
- [`MIGRATION.md`](MIGRATION.md) — how each AutoMapper / Mapster / Mapperly option maps to the above.
- [`howto/`](howto/) — task-oriented migration walkthroughs.
