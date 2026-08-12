<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Pattern provenance — where this corpus's shapes come from

A CLEAN-architecture-scale mapping surface, built to be **converted** from AutoMapper to DwarfMapper and to
record what each conversion costs. It is the R18-29 method applied to whole applications rather than to a test
directory: harvest the **patterns**, re-express them in an original domain, and let the conversion be the
deliverable.

## The rule, restated because the input changed

Nothing is copied. The sources below are MIT, and MIT permits copying with attribution — but this tree is
**GPLv2-only** and CRA-defensive, and a licence that permits something is not a reason to do it. More
practically: transliterating a distinctive domain and renaming the types produces a recognisable copy with the
serial numbers filed off, which is what "anonymised" must never mean here.

What is recorded is the **pattern inventory** — "a DTO that declares its own mapping in a nested `Profile`
class", "an enum member cast to `int` in a `MapFrom`" — which is a fact about how people configure a mapper,
not expression. The domain below (a library lending service) is original and shares no entity, member or
vocabulary with any source.

## Sources

| Project | Licence | Read at | What it contributed |
|---|---|---|---|
| [`jasontaylordev/CleanArchitecture`](https://github.com/jasontaylordev/CleanArchitecture) | MIT | `main`, 2026-08-12 | the DTO-declares-its-own-mapping pattern (a nested `Profile` inside the DTO), and the enum→`int` `MapFrom` |
| [`dotnet-architecture/eShopOnWeb`](https://github.com/dotnet-architecture/eShopOnWeb) | MIT | `main`, 2026-08-12 | the central `MappingProfile` holding several `CreateMap`s, and `ForMember` used purely to reconcile a renamed member |
| The Round-18 consumer | private | this session | `ConstructUsing` with a non-public constructor, `ReverseMap`, per-member `Ignore`, `Condition`, and the collection-of-a-polymorphic-base shape |

## The inventory

Every row is a configuration idiom a real profile uses. The corpus exercises each of them at least twice, in a
domain where they are the natural thing to write rather than a demonstration.

| # | AutoMapper idiom | Why it is interesting to convert |
|---|---|---|
| 1 | convention-matched members | the baseline; should need no configuration at all |
| 2 | `ForMember(… MapFrom …)` for a renamed member | `[MapProperty]` — a direct translation |
| 3 | enum → `int` in a `MapFrom` cast | DwarfMapper converts enums natively; the cast becomes nothing |
| 4 | enum ↔ `string` | the `[Description]` precedence hazard, and `EnumStringSource` |
| 5 | flattening (`Member.Sub` → `MemberSub`) | AutoMapper does it by convention, DwarfMapper by `[Flatten]` — a silent-vs-declared difference |
| 6 | `ReverseMap()` | two independent pairs, or `[ReverseMap]` |
| 7 | `ConstructUsing` | `[MapConstructor]` — and the `init`-only trap `DWARF080` reports |
| 8 | a non-public constructor | AutoMapper bypasses accessibility reflectively; DwarfMapper will not |
| 9 | `Ignore()` | `[MapIgnore]`, and the completeness gate that makes it necessary |
| 10 | `NullSubstitute` | direct |
| 11 | `Condition` / `PreCondition` | `When=` |
| 12 | a custom value resolver | a converter method, or `[MapValue(Use=)]` |
| 13 | collection members | element pairs, and whether the element map is reused |
| 14 | a collection of a polymorphic base | the compile-time-vs-runtime dispatch difference; `[MapDerivedType]` |
| 15 | `ProjectTo` over `IQueryable` | `Project`, and what `DWARF028` refuses |
| 16 | a paginated list wrapper | a generic wrapper pair — `[GenerateWrapperMap]` |
| 17 | **assembly-scanned profile discovery** | **cannot be translated.** AutoMapper finds profiles by reflecting over an assembly at startup; DwarfMapper resolves at compile time by design. The conversion is a declaration per pair, and that is the point rather than a defect |

Row 17 is the one worth stating loudly, because it is the only row where the answer is "you cannot, and here
is why you would not want to".

## What the corpus asserts

Both mappers run over the same payloads and the results are compared member by member — the same device as
`tests/DwarfMapper.DifferentialTests`, at application scale. A difference is either a defect, or a documented
divergence with a reason. The conversion notes live in `CONVERSION-NOTES.md` next to this file.
