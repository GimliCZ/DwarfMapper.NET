<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Conversion notes — an AutoMapper CLEAN corpus, converted

What it actually cost to move this corpus from AutoMapper 14 to DwarfMapper, written down while doing it.
The corpus itself is in this directory; `PATTERN-PROVENANCE.md` says where its patterns came from and why no
code was copied. Both mappers run over the same payloads and the results are compared, so "we converted it"
is a claim the build checks — which is the only version of that claim worth anything, because a conversion
that *compiles* is exactly what the Round-18 consumer had while it was losing data.

## The scoreboard

| AutoMapper idiom | Cost to convert |
|---|---|
| convention-matched members | nothing |
| `ForMember(… MapFrom …)` rename | `[MapProperty]`, one for one |
| enum → `int` cast in a `MapFrom` | **less than nothing** — the cast disappears; DwarfMapper converts natively |
| `Ignore()` | `[MapIgnore]`, one for one |
| `NullSubstitute` | `[MapProperty(NullSubstitute = …)]`, one for one |
| `Condition` | `[MapProperty(When = …)]`, plus a named predicate instead of a lambda |
| a value resolver | `[MapProperty(Use = …)]`, plus a named method instead of a lambda |
| `ReverseMap()` | two declarations instead of one |
| nested source paths | `[MapProperty("A.B", …)]`, one for one |
| a collection member | nothing |
| `IncludeBase` | restate, and `[RestatesBase]` checks the restatement — see below |
| runtime polymorphic dispatch | `[MapDerivedType]` arms, explicit |
| **flattening** | **three lines where AutoMapper had zero, and the obvious translation is wrong** |
| **`ConstructUsing` + a private constructor** | **a domain change** |
| **a generic wrapper** | one closed declaration — same as AutoMapper needed |
| **assembly-scanned profile discovery** | not translatable, by design |

## The four that cost something

### 1. Flattening — `[Flatten]` is not AutoMapper's flattening

The obvious translation of AutoMapper's implicit flattening is `[Flatten(nameof(Branch.Address))]`, and it is
**wrong**. AutoMapper's convention prefixes the flattened member with the containing member's *name*, so
`Address.Town` becomes `AddressTown`. `[Flatten]` lifts the leaf under its own name and produces `Town`. The
pair then fails the completeness gate on two members that look like they should obviously have matched, which
is a confusing five minutes.

Stated per member instead — `[MapProperty("Address.Town", "AddressTown")]` — which is longer and says what it
does. Worth knowing before starting a migration, not during one.

### 2. `ConstructUsing` over a private constructor — the one domain change

`Member` had a private constructor and a public `Register` factory. AutoMapper reached the constructor
reflectively; DwarfMapper generates ordinary C# and cannot, so the pair was `DWARF026` and **no attribute
fixes it**.

`[MapConstructor]` naming `Register` is the literal translation of `ConstructUsing`, and it compiles. It also
loses `CardNumber` — a factory owns construction, so an `init`-only member cannot be assigned afterwards.
`DWARF080` reports that at build time instead of letting it ship, which is the whole reason that diagnostic
exists.

The conversion widened the constructor to `internal` and bound its *parameter*, which is what the diagnostic
recommends. That is a change to the domain to suit the mapper — the only one — and it is the compile-time,
compiler-checked version of a grant that reflection was taking anyway. A test asserts the card number
survives, because the version that loses it also passes every other test in this file.

### 3. `[GenerateWrapperMap]` wants a single-payload generic

`Page<T>` carries paging metadata alongside its items, so it is not the single-payload shape
`[GenerateWrapperMap]` expresses, and the attribute is refused with `DWARF067`. The closed instantiation is
declared instead — which is exactly what AutoMapper needed too, one `CreateMap` per closed type. No ground
lost, and the build said so rather than the mapper silently doing something else.

### 4. Assembly-scanned profile discovery — not translatable, and that is the feature

AutoMapper finds profiles by reflecting over an assembly at startup. DwarfMapper resolves at compile time, so
there is nothing to scan and every pair is a declaration. That is a real cost in typing and the entire source
of the guarantees: a pair that is not declared does not exist, rather than existing depending on which
assemblies happened to load.

## The one deliberate behavioural difference

`Availability` carries `[Description("on-shelf")]`. The AutoMapper profile wrote `.ToString()` — the
identifier — while DwarfMapper's default reads the annotation. Neither is wrong; a migration that does not
**notice** starts writing `"on-shelf"` into a store full of `"OnShelf"`. `DWARF083` reports it at build time
and `EnumStringSource = Identifier` is the one-line switch back. Asserted as a difference rather than papered
over, because finding these is the corpus's job.

## What the corpus found in the generator

One defect, on the first conversion attempt.

A `[MapDerivedType]` dispatcher is excluded from resolving its own arms — otherwise a switch arm calls its own
switch. That exclusion worked by **signature**, and a base arm whose pair *is* the dispatcher's own pair needs
a sibling method with the same parameter and return types: legal C#, different name, no `DWARF060`. The
signature exclusion removed the sibling along with the dispatcher, so the arm had nowhere to resolve,
synthesized a fresh mapper that could not see the sibling's configuration, and the pair failed its
completeness gate on members the sibling explicitly maps.

Narrowed to exclude the dispatching **method** rather than every method with its signature, and pinned by
`DerivedTypeArmCompositionTests.An_arm_may_resolve_to_a_SIBLING_that_shares_the_dispatchers_signature`.

A base and a derived source mapping to one DTO is the ordinary shape in a CLEAN application rather than an
edge case, which is why a corpus at this scale found it and 5,500 hand-written tests did not.
