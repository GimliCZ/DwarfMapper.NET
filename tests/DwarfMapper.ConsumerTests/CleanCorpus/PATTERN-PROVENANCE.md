<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Pattern provenance — where this corpus's shapes come from

A CLEAN-architecture-scale mapping surface, built to be **converted** from AutoMapper to DwarfMapper and to record what
each conversion costs. It is the R18-29 method applied to whole applications rather than to a test directory: harvest
the **patterns**, re-express them in an original domain, and let the conversion be the deliverable.

## The rule, restated because the input changed

Nothing is copied. The sources below are MIT, and MIT permits copying with attribution — but this tree is **GPLv2-only**
and CRA-defensive, and a licence that permits something is not a reason to do it. More practically: transliterating a
distinctive domain and renaming the types produces a recognisable copy with the serial numbers filed off, which is what
"anonymised" must never mean here.

What is recorded is the **pattern inventory** — "a DTO that declares its own mapping in a nested `Profile`
class", "an enum member cast to `int` in a `MapFrom`" — which is a fact about how people configure a mapper, not
expression. The domain below (a library lending service) is original and shares no entity, member or vocabulary with any
source.

## Sources

| Project                                                                                                     | Licence | Read at              | What it contributed                                                                                                                                                                                                               |
|-------------------------------------------------------------------------------------------------------------|---------|----------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| [`jasontaylordev/CleanArchitecture`](https://github.com/jasontaylordev/CleanArchitecture)                   | MIT     | `main`, 2026-08-12   | the DTO-declares-its-own-mapping pattern (a nested `Profile` inside the DTO), and the enum→`int` `MapFrom`                                                                                                                        |
| [`dotnet-architecture/eShopOnWeb`](https://github.com/dotnet-architecture/eShopOnWeb)                       | MIT     | `main`, 2026-08-12   | the central `MappingProfile` holding several `CreateMap`s, and `ForMember` used purely to reconcile a renamed member                                                                                                              |
| [`jbogard/ContosoUniversityDotNetCore-Pages`](https://github.com/jbogard/ContosoUniversityDotNetCore-Pages) | MIT     | `master`, 2026-08-12 | view models as **records nested inside the handler that owns them**, `CreateProjection` over `IQueryable`, and two levels of navigation projected in one go — read because it is AutoMapper's own author demonstrating AutoMapper |
| [`kgrzybek/modular-monolith-with-ddd`](https://github.com/kgrzybek/modular-monolith-with-ddd)               | MIT     | `master`, 2026-08-12 | the DOMAIN shape rather than a mapping one: **strongly-typed ids**, **private readonly backing lists exposed read-only**, a private parameterless constructor for ORM hydration plus an internal factory, and value objects       |
| ABP-style frameworks                                                                                        | —       | 2026-08-12           | an **audited base class** (`CreatedAt`/`CreatedBy`/`LastModifiedAt`/`IsDeleted`) on every entity, which no DTO carries                                                                                                            |
| The Round-18 consumer                                                                                       | private | this session         | `ConstructUsing` with a non-public constructor, `ReverseMap`, per-member `Ignore`, `Condition`, and the collection-of-a-polymorphic-base shape                                                                                    |

## The inventory

Every row is a configuration idiom a real profile uses. The corpus exercises each of them at least twice, in a domain
where they are the natural thing to write rather than a demonstration.

| #  | AutoMapper idiom                              | Why it is interesting to convert                                                                                                                                                                                                    |
|----|-----------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 1  | convention-matched members                    | the baseline; should need no configuration at all                                                                                                                                                                                   |
| 2  | `ForMember(… MapFrom …)` for a renamed member | `[MapProperty]` — a direct translation                                                                                                                                                                                              |
| 3  | enum → `int` in a `MapFrom` cast              | DwarfMapper converts enums natively; the cast becomes nothing                                                                                                                                                                       |
| 4  | enum ↔ `string`                               | the `[Description]` precedence hazard, and `EnumStringSource`                                                                                                                                                                       |
| 5  | flattening (`Member.Sub` → `MemberSub`)       | AutoMapper does it by convention, DwarfMapper by `[Flatten]` — a silent-vs-declared difference                                                                                                                                      |
| 6  | `ReverseMap()`                                | two independent pairs, or `[ReverseMap]`                                                                                                                                                                                            |
| 7  | `ConstructUsing`                              | `[MapConstructor]` — and the `init`-only trap `DWARF080` reports                                                                                                                                                                    |
| 8  | a non-public constructor                      | AutoMapper bypasses accessibility reflectively; DwarfMapper will not                                                                                                                                                                |
| 9  | `Ignore()`                                    | `[MapIgnore]`, and the completeness gate that makes it necessary                                                                                                                                                                    |
| 10 | `NullSubstitute`                              | direct                                                                                                                                                                                                                              |
| 11 | `Condition` / `PreCondition`                  | `When=`                                                                                                                                                                                                                             |
| 12 | a custom value resolver                       | a converter method, or `[MapValue(Use=)]`                                                                                                                                                                                           |
| 13 | collection members                            | element pairs, and whether the element map is reused                                                                                                                                                                                |
| 14 | a collection of a polymorphic base            | the compile-time-vs-runtime dispatch difference; `[MapDerivedType]`                                                                                                                                                                 |
| 15 | `ProjectTo` over `IQueryable`                 | `Project`, and what `DWARF028` refuses                                                                                                                                                                                              |
| 16 | a paginated list wrapper                      | a generic wrapper pair — `[GenerateWrapperMap]`                                                                                                                                                                                     |
| 17 | **assembly-scanned profile discovery**        | **cannot be translated.** AutoMapper finds profiles by reflecting over an assembly at startup; DwarfMapper resolves at compile time by design. The conversion is a declaration per pair, and that is the point rather than a defect |

Row 17 is the one worth stating loudly, because it is the only row where the answer is "you cannot, and here is why you
would not want to".

## The second inventory — the shapes that are not on any feature list

The table above is the AutoMapper feature list, and a corpus built only from it is generic: every tutorial covers
rename, flatten, ignore, condition, resolver. What actually makes mapping hard in production is the DOMAIN, not the
mapper's option surface. `DddDomain.cs` covers that.

| #  | Real-world shape                                            | Why it is interesting to convert                                                                                                       |
|----|-------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------|
| 18 | a strongly-typed id wrapping a `Guid`                       | every id member needs a conversion, and there are a lot of them; a missing one defaults silently under a reflective mapper             |
| 19 | a value object flattened into two DTO members               | the ordinary treatment, and where AutoMapper's implicit path handling shows                                                            |
| 20 | a value object rendered to a string                         | a real converter rather than a copy                                                                                                    |
| 21 | a positional record fed from a value object                 | `ForCtorParam` + a nested path — **found R18-31**, and looking for the same defect in the projection lane found **R18-32**; both fixed |
| 22 | a read-only child collection over a private backing list    | a reflective mapper writes through the aggregate's back; a compile-time one cannot                                                     |
| 23 | a private parameterless constructor for ORM hydration       | the shape a mapper must NOT be able to use by accident                                                                                 |
| 24 | an audited base class every entity carries and no DTO wants | the source-completeness question                                                                                                       |
| 25 | a `[Flags]` enum as text                                    | a combined value formats as a comma-joined list, which a naive switch gets wrong                                                       |
| 26 | a dictionary member keyed by culture                        | localisation, present in every real application and in no tutorial                                                                     |
| 27 | view models as records nested inside their handler          | the mapper has to cope with types that are not top-level                                                                               |

Rows 21 and 22 are the two that produced findings rather than typing.

## What the corpus asserts

Both mappers run over the same payloads and the results are compared member by member — the same device as
`tests/DwarfMapper.DifferentialTests`, at application scale. A difference is either a defect, or a documented divergence
with a reason. The conversion notes live in `CONVERSION-NOTES.md` next to this file.
