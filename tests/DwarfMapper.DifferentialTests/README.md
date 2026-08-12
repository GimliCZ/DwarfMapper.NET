<!-- SPDX-License-Identifier: GPL-2.0-only -->
# DwarfMapper.DifferentialTests

**One payload, three mappers, compared member by member.**

DwarfMapper, [Mapperly](https://github.com/riok/mapperly) and AutoMapper each map the same source object; the
results are walked reflectively and any disagreement is reported as a path — `Stops[1].City` — rather than as
"not equal".

## Why an outside oracle

Every real defect Round 18 found hid behind a **corpus hole**, not behind subtle generator code. This
repository's corpus is self-authored, so it shares its author's blind spots by construction. The fuzzer and
`[RoundTrip]` prove *self-consistency*, which is a genuinely weaker property: a mapper that is wrong the same
way in both directions round-trips perfectly.

Mapperly's semantics were designed by other people, and AutoMapper's by others again. Agreement with them is
evidence from outside — the one kind this repository could not previously produce.

## The accepted-divergence list is the deliverable

Three mappers designed independently will differ on documented axes. Each difference is either a defect or a
decision, and `AcceptedDivergences.cs` is where the decisions are written down — with the axis named and the
reason stated. That turns claims in `docs/COMPARISON.md` from prose into something executable: an assertion
about how DwarfMapper differs from Mapperly now fails if it stops being true.

Three guards keep the list honest:

- **Every entry states a reason.** A one-word justification is how an allowlist becomes a way of making red
  things green.
- **Every entry names an oracle this harness actually runs.**
- **Every entry is exercised.** A stale entry is worse than no entry: it reads as a documented difference
  while silently covering whatever else falls under its path prefix. If the difference is gone, delete the
  entry — that is the ratchet tightening.

A fourth guard covers the shapes rather than the list: **every pair declared on the oracle is actually
compared**. Types can be added to `Shapes.cs` and a method to `MapperlyShapes` without being wired into
`ShapeCatalog.All()`, which would leave the shape untested while the suite stayed green. It found a hole on
its first run — `Address -> AddressDto` was declared on all three mappers and reached only as somebody's
nested member, so a divergence in the pair itself would have been visible exclusively through whichever
container happened to hold it.

That guard matches the declared target type by **assignability**, not by name. The first polymorphic pair
broke it by satisfying it: a method declared to return `CommandDto` returns an `AliasCommandDto` at runtime —
which is the pair being exercised in its most interesting form — and name equality called that uncovered. A
ratchet that fires on correct coverage teaches people to edit the ratchet.

The current list has one axis on it, deliberately chosen because the three mappers really do disagree:
enum→string. DwarfMapper reads `[Description]`/`[EnumMember]` by default (which is why `DWARF083` exists);
Mapperly and AutoMapper use the identifier. The *same shape* is also compared under
`EnumStringSource.Identifier`, where all three agree — which is what makes the divergence a decision with a
one-line switch rather than an incompatibility.

## When a comparison fails

One of three things is true, and the harness cannot tell you which:

1. **DwarfMapper is wrong** — the case this project exists to find.
2. **The oracle is wrong.** It happens; say so in the accepted-divergence entry.
3. **They differ deliberately** — add an `AcceptedDivergence` naming the axis *and* the reason.

## The comparer

`MemberComparer` is load-bearing: every claim this project makes passes through it, and a comparer that misses
a difference makes the whole harness a very convincing way of proving nothing. `MemberComparerTests` pins its
two judgement calls:

- **Concrete collection type is not a difference.** A member declared `IReadOnlyList<T>` that one mapper
  materialises as `List<T>` and another as `T[]` holds the same data, and a consumer reading through the
  declared type cannot tell. A *differential* oracle compares what a consumer observes. Real DTOs use
  interface-typed collections constantly, so without this the harness would cry wolf on its first harvested
  shape — while element-wise differences are still reported, with an index.
- **Dictionary keys are compared as sets, not by lookup.** Asking `actual.Contains(key)` uses the *actual*
  dictionary's own comparer, so an `OrdinalIgnoreCase` target reports `"A"` as present when what it holds is
  `"a"` — and the two mappers would agree on a dictionary whose keys are not the same.

A polymorphic result that came back as the base type IS reported, and a self-referencing graph terminates.

It also walks **fields**, not only properties, and that is not tidiness: a `ValueTuple` has no public
properties at all — `Item1`/`Item2` are fields — so before this the comparer found nothing to compare in a
tuple and reported that any two tuples agreed. The value-tuple shape was added on top of a comparer that
could not have failed it.

## Where the mappers deliberately differ

`LoudRatherThanSilentTests` is the counterpart to the agreement suite: it asks what each mapper does with a
value nobody declared an answer for — an enum value matching no destination member, a runtime type matching
no dispatch arm. DwarfMapper and Mapperly both refuse; AutoMapper substitutes. Those tests are what make the
corresponding `docs/COMPARISON.md` rows executable rather than prose, and they live outside `ShapeCatalog`
because that enumerable is evaluated eagerly — one throwing shape would take every comparison down with it.

## Adding shapes

Add the types to `Shapes.cs`, declare the pair on all three mappers, and add the comparison to
`ShapeCatalog.All()`. Both the per-shape assertions and the ledger check read that one enumerable, so a new
shape is covered by both without touching either test.

**Qualify the oracle's attributes.** This project's namespace is `DwarfMapper.DifferentialTests`, so
DwarfMapper's own attributes are in scope through the enclosing namespace and BEAT a `using`-imported one of
the same name. `[MapDerivedType]` exists in both libraries: written unqualified on `MapperlyShapes` it binds
to DwarfMapper's, Mapperly never sees an attribute, and the harness reports a defect that is entirely the
harness's own. Write `[Riok.Mapperly.Abstractions.MapDerivedType<…>]`.

Shapes should be written the way real DTOs are written, not the way a generator author would choose to test
one — that is the entire point. See task **R18-29** for harvesting shapes from public projects: harvest the
*shape*, never the text.

## Licensing

`Directory.Packages.props` pins Mapperly 4.3.1 and Mapster 10.0.8 (MIT) and **AutoMapper 14.0.0 — the last MIT
release; v15+ is RPL-1.5 and GPL-incompatible**. This project is `IsPackable=false` and the shipped library
references none of them.
