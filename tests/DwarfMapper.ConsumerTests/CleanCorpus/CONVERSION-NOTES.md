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

## The second corpus: what a DDD codebase actually looks like

The scoreboard above is the AutoMapper FEATURE list, and on its own it is generic — every mapper tutorial
covers rename, flatten, ignore, condition, resolver. `DddDomain.cs` covers what makes mapping hard in
applications that are not tutorials, harvested from `kgrzybek/modular-monolith-with-ddd` (domain shape),
`jbogard/ContosoUniversityDotNetCore-Pages` (view-model shape) and ABP-style framework conventions.

| Real-world shape | Cost to convert |
|---|---|
| strongly-typed ids (`SessionId` wrapping a `Guid`) | one `[MapProperty]` per id — **identical to AutoMapper's `ForMember` per id** |
| a value object flattened to two members | one `[MapProperty]` each, same as AutoMapper |
| a value object rendered to a string | a converter, same as AutoMapper's resolver |
| a `[Flags]` enum as text | nothing — both produce the comma-joined list |
| a dictionary member | nothing |
| view models as records NESTED inside their handler | nothing |
| an audited base class no view carries | nothing — and see below |
| a positional record's parameters fed from a value object | one `[MapProperty]` per parameter — **after the defect this corpus found was fixed** |
| **a read-only child collection over a private list** | **not mapped inward at all, on purpose** |

### Strongly-typed ids cost the same and fail differently

AutoMapper needs a `ForMember` per id; DwarfMapper needs a `[MapProperty]` per id. Identical typing. What
differs is what happens when one is **missing**: a default `Guid` that reaches the wire, versus a build that
stops. In a codebase with a hundred ids that difference is the entire argument, and a test states it rather
than leaving it to be believed.

### The read-only child collection is the real behavioural difference

`Session.Slots` has no setter and is backed by a private list, because the aggregate wants every addition to
go through `Schedule`. AutoMapper writes into the backing field reflectively and never mentions it.
DwarfMapper cannot and will not pretend to — so only the entity→view direction is declared, and the reverse
would have to call the aggregate's own method.

That is the largest single difference in this corpus, and it is not a limitation. It is the domain's decision
being honoured instead of bypassed. A migration should expect to find every place a reflective mapper was
quietly writing through an aggregate's back.

### The audited base class

Four members on every entity that no view carries. AutoMapper says nothing about an unmapped **source**
member — convenient right up to the day one of them mattered. DwarfMapper is silent by default too, and
`RequiredMapping = Both` is the switch that makes source coverage a build-time question. Either way it is
now an asserted property of the corpus rather than an assumption.

### Conversion note 7 — a defect, not a cost, and now neither

`ForCtorParam("Start", o => o.MapFrom(s => s.Window.Start))` did not translate. A dotted source path into a
**constructor parameter** was refused under `DWARF009` — "source member 'Window.Start' does not exist or is
not readable" — about a member that existed, was readable, and mapped correctly into a *property* three lines
up. Filed as **R18-31** and fixed; the conversion is now one `[MapProperty("Window.Start", "Start")]` per
parameter, which is what it should always have been. See below for what the defect actually was.

The workaround it forced in the meantime was a converter per parameter taking the value object whole — decent
enough that it was easy not to notice the cost, which is the argument for converting real corpora rather than
feature lists.

## The one deliberate behavioural difference

`Availability` carries `[Description("on-shelf")]`. The AutoMapper profile wrote `.ToString()` — the
identifier — while DwarfMapper's default reads the annotation. Neither is wrong; a migration that does not
**notice** starts writing `"on-shelf"` into a store full of `"OnShelf"`. `DWARF083` reports it at build time
and `EnumStringSource = Identifier` is the one-line switch back. Asserted as a difference rather than papered
over, because finding these is the corpus's job.

## What the corpus found in the generator

Two defects, one per half, each on the first conversion attempt.

### R18-31 — a dotted source path bound to a constructor parameter

Three places have to agree about what `"Window.Start"` means: member resolution, constructor-argument
resolution, and the constructor *selector* that scores which parameters have a source. Only the first walked
the path; the other two compared it against a flat set of member names, where a string containing a dot
matches nothing.

That produced two failures with one cause. Resolution reported `DWARF009` — "does not exist or is not
readable" — which is the most misleading thing a diagnostic can say about a member that does exist, because
the reader goes looking for a typo. And selection scored the constructor unsatisfiable, so a type with more
than one constructor would quietly get the *narrower* one and then fail completeness on the members the wider
one would have filled — the same shape as `ISSUE-044`, and invisible in this corpus because `SlotView` has a
single constructor.

The walk now lives once, in `MemberFacts.TryResolvePath`, and all three call it. Pinned by four tests in
`DeepSourcePathGeneratorTests` and by
`ConstructorSelectorHardeningTests.Dotted_source_path_into_a_ctor_param_keeps_the_wide_ctor`, which is the
two-constructor case this corpus could not reach.

### A `[MapDerivedType]` arm that resolved to its own dispatcher

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
