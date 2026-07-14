<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Real-world mapping parity corpus

This project replicates mapping transformations that **real, popular OSS .NET projects** implement with the
reference mappers, and asserts DwarfMapper produces the **exact output** those libraries' documented
semantics describe. It is a *capability/parity* corpus: each scenario is a generic domain → DTO shape
(not copied library code), validated at **runtime** with `DwarfMapper.Testing`-style fixtures.

> **Licensing.** Patterns are studied from Riok.Mapperly (MIT) and Mapster (MIT) directly, and from
> AutoMapper **14.0.0** documented behaviour *conceptually only* (no AutoMapper source/test code is
> reproduced — the 14.0.x line is the last classic MIT-style release; v15+ is dual-licensed). Mapping
> *shapes* (Order → OrderDto, flatten an address, stamp audit fields) are ordinary domain modelling.

## Why these projects

Grounded in real usage of the reference mappers (stars / NuGet downloads at time of survey):

| Project / package              | Scale                                       | Mapper used                     | Scenarios drawn                                           |
|--------------------------------|---------------------------------------------|---------------------------------|-----------------------------------------------------------|
| dotnet/eShop, eShopOnWeb       | ~11k★ each                                  | manual + AutoMapper             | Order total, constant status, CatalogType/Item DTOs       |
| ABP Framework                  | ~13k★                                       | **switched default → Mapperly** | validates the compile-time-mapper stance                  |
| Jellyfin                       | ~53k★                                       | manual `DtoService`             | scalar→array, enrichment                                  |
| Jason Taylor CleanArchitecture | ~20k★                                       | AutoMapper                      | `(int)Priority`, audit create-flow, `Colour` value object |
| FastEndpoints                  | ~6k★, 14M dl                                | manual `Mapper<,,>`             | computed FullName, request→entity→response                |
| nopCommerce                    | ~10k★                                       | AutoMapper                      | ignore `PasswordHash` both directions                     |
| Mapperly                       | 24M dl (Volo.Abp.Mapperly is top dependent) | —                               | flattening, enum strategies, ctor mapping                 |
| Mapster                        | 69M dl                                      | —                               | TwoWays round-trip, computed/conditional, dictionaries    |
| AutoMapper 14.0.0              | 892M dl                                     | —                               | NullSubstitute, null collections, value converters        |

## Scenarios (20 runtime-validated)

`CorpusFlatteningRenameTests` — basic/PascalCase/deep flatten, single-level unflatten, bidirectional
round-trip, field rename, drop-nav-keep-FK + ignore sensitive field.
`CorpusEnumCollectionTests` — enum by-name across different numeric values, enum→int, enum→string,
`List<Child>` (new instance), element-drops-extra-member, null source collection → empty.
`CorpusComputedNullTests` — computed full name, aggregate total, constant field, decimal→currency string
(culture-pinned `en-US`), null substitution, AfterMap audit stamping (clock-pinned), record/init ctor +
value-object→string.

## Deliberate divergences (DwarfMapper is *stronger*, by design)

Where DwarfMapper does **not** match a reference library, it is the resilience-first / no-silent-surprises
design choice — usually *louder*, often at **compile time**:

- **Unmapped enum member (by-name)** → DwarfMapper is a **compile error** (`DWARF015`), vs Mapperly's
  *runtime* `ArgumentOutOfRangeException`. You cannot ship the mistake.
- **Narrowing/overflow** → loud `OverflowException` via `CreateChecked`, never a silent truncation/wrap.
- **Null interior on a deep source path** → DwarfMapper throws (and warns `DWARF044` at compile time),
  vs Mapster's silent null-propagation. Use `NullSubstitute`/`When` for the lenient shape explicitly.
- **PATCH/merge `IgnoreNullValues`** (Mapster) → DwarfMapper's update-into *replaces*; selective
  null-skipping is intentionally explicit rather than a global mode.

These are validated by the generator + integration suites; the corpus asserts the matching cases.
