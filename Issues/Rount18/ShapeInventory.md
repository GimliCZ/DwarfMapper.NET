<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Shape inventory — what other people's mappers are tested against

**Task R18-29.** A catalogue of the mapping *shapes* a competing generator's authors thought worth testing,
checked against DwarfMapper's own coverage. It exists because every real defect Round 18 found hid behind a
**corpus hole**, and this repository's corpus is self-authored — so it shares its author's blind spots by
construction. An outside inventory has different blind spots, which is the entire value.

## The rule: shapes, never text

Nothing is copied from another repository into this one. This tree is **GPLv2-only** and CRA-defensive;
pasting fixtures from an Apache-2.0 or MIT project imports their licence, their dependencies, and a
provenance problem that a future reviewer will check first.

What *is* recorded is the **shape fact** — "a mapper with a value-tuple target", "a target whose member is
reserved-keyword-named" — which is a fact about what people write, not expression. Where a shape is worth
covering, it gets re-expressed in DwarfMapper's own words in
`tests/DwarfMapper.DifferentialTests/Shapes.cs`.

## Source

`riok/mapperly`, `test/Riok.Mapperly.Tests/Mapping/`, at commit
[`3a1f57f`](https://github.com/riok/mapperly/tree/3a1f57fe89a91028535c658358ddfbb42dec6912/test/Riok.Mapperly.Tests/Mapping)
(2026-08-05). 81 test files, each named for the axis it exercises. Mapperly is MIT; this document cites it,
and copies nothing from it.

Two things make it the best available source. It is a **compile-time source generator solving the same
problem**, so its axes are directly comparable rather than analogous — and it was written by different people,
so where its list and ours differ, the difference is informative in both directions.

## The inventory

Legend: **✅** covered · **➕ gap** worth closing · **🐞 defect** found by this pass · **➖ n/a** — not a
shape DwarfMapper has, with the reason.

### Object mapping

| Shape axis | DwarfMapper | Where |
|---|---|---|
| Plain property mapping | ✅ | everywhere; `DifferentialTests` `FlatScalars` |
| Fields, not just properties | ✅ | `ObjectFieldTest` axis — `MemberKindTests` |
| Nested object members | ✅ | `DifferentialTests` `NestedObject` |
| Nested *property paths* (`a.b.c`) | ✅ | `DeepSourcePathGeneratorTests` |
| Flattening | ✅ | `[Flatten]`, Conformance F09 |
| Unflattening (target path) | ✅ | `[MapProperty]` unflatten target, `DWARF045`/`DWARF046` |
| Member visibility (non-public) | ✅ | `AllowNonPublic`, `AllowNonPublicMemberTests` |
| Init-only properties | ✅ | `DWARF080`, `ObjectPropertyInitPropertyTest` axis |
| `required` members | ✅ | `DWARF079` |
| Constructor resolution | ✅ | `ConstructorSelectorHardeningTests`, `DWARF024`–`DWARF026` |
| Ignore attribute | ✅ | `[MapIgnore]`, `[MapIgnoreSource]` |
| Ignore obsolete members | ✅ | `IgnoreObsoleteMembers`, Conformance F34 |
| Constant / computed values | ✅ | `[MapValue]`, Conformance F13 |
| No mappable members at all | ✅ | `DWARF001` completeness gate |
| **Reserved-keyword member names** (`@class`) | **🐞 defect** | emitted unescaped — see below, task **R18-30** |
| **Value tuples as a target** | **➕ gap** | named in the fuzz schema, never mapped end-to-end |

### Collections and dictionaries

| Shape axis | DwarfMapper | Where |
|---|---|---|
| `IEnumerable`/`List`/array | ✅ | `CollectionTaxonomyTests`, `DifferentialTests` `Collections` |
| Sets | ✅ | `CollectionTaxonomyTests` |
| Immutable collections | ✅ | `EnumerableImmutableTest`/`DictionaryImmutableTest` axes — `CollectionTaxonomyTests` |
| Dictionaries, custom dictionaries | ✅ | `DictionaryRuntimeTests`, `DictionaryKeyCollisionRuntimeTests` |
| Existing-target collections (merge) | ✅ | `[MapCollectionKey]`, `CollectionKeyUpsertRuntimeTests` |
| `Span`/`Memory` | ✅ | span endpoint; `SpanTest` axis |
| `Stack`/`Queue` element ORDER | ✅ | **closed by this pass** — `DifferentialTests` `StackAndQueueOrder`; all three agree |
| **Deep cloning** (`T` → `T`) | ➖ n/a | `DWARF076` refuses a same-type map by design — DwarfMapper is not a cloner |

### Enums

| Shape axis | DwarfMapper | Where |
|---|---|---|
| Enum ↔ enum, by name and by value | ✅ | `EnumStrategy`, Conformance F04 |
| Enum ↔ string | ✅ | `EnumStringSource`, `DWARF083`, Conformance F45 |
| Enum ↔ numeric | ✅ | `EnumUnderlyingRuntimeTests` |
| Explicit per-member enum mapping | ✅ | `[MapProperty(Use=)]` per member |
| Incomplete enum mapping | ✅ | `DWARF015` |
| `[Flags]` in both directions | ✅ | `EnumConverter`, `Snap_Golden_FlagsEnumFromString` |
| **Fallback value for an unmatched name** | **➕ gap** | DwarfMapper throws; Mapperly can default. A deliberate difference — but it is not *written down*, and unwritten defaults are how Round 18 started |
| **Naming strategy** (case-insensitive parse) | ➖ n/a | deliberate: `docs/howto/migrate-from-automapper.md` row 5 states the case-sensitivity difference |

### Polymorphism and generics

| Shape axis | DwarfMapper | Where |
|---|---|---|
| Derived-type dispatch | ✅ | `[MapDerivedType]`, `DerivedTypeArmCompositionTests`, Conformance F43/F44 |
| Derived types into an existing target | ✅ | update-into + `[MapDerivedType]` |
| Inheritance of configuration | ✅ | `[RestatesBase]`, `DWARF084`/`DWARF085` |
| Runtime target type (`Map(src, typeof(T))`) | ✅ | ambient registry, `ConsumerTests` |
| Generic *mapper class* | ➖ n/a | `DWARF054` — a generic partial cannot be completed |
| Generic *mapping method* | ➖ n/a | `DWARF053`, same reason |
| **Generic derived-type dispatch** | **➕ gap** | `GenericDerivedTypeTest` axis; untested here |

### User methods and conversions

| Shape axis | DwarfMapper | Where |
|---|---|---|
| User-implemented mapping method | ✅ | the primary form |
| User method with extra parameters | ✅ | `AdditionalParameterGeneratorTests`, `DWARF047` |
| Named mappings / `Use=` | ✅ | `ConverterPrecedenceTests`, `ConverterAdoptionPolicyTests` |
| Static-method conversion | ✅ | `ConverterTests` |
| Delegates as converters | ➖ n/a | deliberate: a delegate is not resolvable at compile time |
| Extension-method mappers | ✅ | `GenerateExtensions` facade |
| Object factories | ✅ | `[MapConstructor]`, `DWARF080` |
| Parse / format (`IParsable`, `ToString`) | ✅ | `ParsableConverter`, `[MapProperty(StringFormat=)]` |
| `ToString` with a format | ✅ | `StringFormat`, `DWARF073` |
| **Mapper composition** (`UseMapper`) | ➖ n/a | the ambient registry is the answer; see `docs/ambient-registry.md` |

### Everything else

| Shape axis | DwarfMapper | Where |
|---|---|---|
| Reference handling / cycles | ✅ | `ReferenceHandling`, `OnCycle`, `MaxDepth` |
| Nullability, both directions | ✅ | `DifferentialTests` `NullableMembers*`, `DWARF070` |
| Queryable projection | ✅ | `Project`, `DWARF028`, the endpoint contract matrix |
| Same-type mapping | ✅ | `DWARF076` |
| `UnsafeAccessor` for private members | ➖ n/a | **deliberate and load-bearing**: accessibility is honoured, never bypassed. See `docs/COMPARISON.md` |

## What this found

### A defect, on the first run — in two generators at once

The reserved-keyword shape did not reveal a coverage gap. It revealed a **bug**. A DTO with a member called
`@class` or `@event` — ordinary in code generated from a JSON or OpenAPI schema — makes DwarfMapper emit

```csharp
class = src.class,
event = src.event,
```

which the C# compiler parses as a malformed event declaration: `CS0065`/`CS0101`/`CS0102`, with an **empty
member name**, reported against generated code the consumer never wrote. No DWARF diagnostic; the build simply
collapses.

**Mapperly emits the same unescaped form and fails identically.** So this is not a case of being behind a
competitor — it is a shape *neither* corpus ever asked about, which is the entire argument for harvesting from
outside in the first place. Filed as **R18-30**, with the design note that member names reach emitted code at
~41 sites and that patching 40 of them reproduces a defect class this project has already been bitten by twice.

The shape itself is written and commented out in `tests/DwarfMapper.DifferentialTests/Shapes.cs`, with its
catalogue entry, so closing R18-30 is a matter of uncommenting two blocks and watching the comparison go green.

### The rest

**Four gaps**, all of them shapes nobody here would have thought of, which is the point:

1. **Reserved-keyword member names** — a defect in both generators, above. **R18-30.**
2. **`Stack`/`Queue` element order.** Closed: added as a differential shape, and all three mappers agree —
   enumeration order survives. Worth having asserted rather than assumed, because enumerating a `Stack` yields
   last-in-first-out and a mapper that rebuilds one by pushing in enumeration order reverses it, silently, and
   only for that one collection kind.
3. **Value-tuple targets.** Present in the fuzz schema as a *type*, never mapped end-to-end. Open.
4. **Generic derived-type dispatch.** Open.

And **one difference worth writing down rather than closing**: Mapperly offers an enum fallback value for an
unmatched name; DwarfMapper throws. That is a defensible choice — it is the same "loud rather than silent"
stance as `NullStrategy.Throw` — but it is not currently stated anywhere, and an unwritten default is exactly
how the `[Description]` near-miss happened.

## Next

Gaps 3 and 4 become shapes in `tests/DwarfMapper.DifferentialTests/Shapes.cs`, where all three mappers get the
same payload and the answer is settled by comparison rather than by opinion. Gap 1 is **R18-30**. The
enum-fallback difference becomes a row in `docs/COMPARISON.md`.

The harvest is worth repeating. This pass read one directory of one project and produced a defect, a closed
gap, two open ones and a difference worth documenting — from 81 file names.
