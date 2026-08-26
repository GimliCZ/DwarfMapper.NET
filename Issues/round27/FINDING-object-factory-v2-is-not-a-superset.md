<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Finding: `ObjectFactoryV2` is not a superset of `ObjectFactory`

**2026-08-26.** Task 0.10 was to settle the two overlapping object factories in `src/DwarfMapper.Testing`,
and the instruction was *"replace it — as far as I know ObjectFactoryV2 should be better."*

**It is better in one direction and worse in three others.** The replacement was attempted, measured, and
reverted. This file is why.

---

## What was done

`ObjectFactory` was deleted, its 28 call sites across 16 files repointed to `ObjectFactoryV2`, its entries
removed from `PublicAPI.Unshipped.txt`, and its test file ported. The solution built clean with 0 warnings.

Then the suite ran, and **three test classes failed** — `ObjectFactorySubstitutionTests` (the ported file),
`PolymorphicMemberFuzzTests` (9 failures), and `FlagsEnumCoverageSelfValidationTests`, plus scattered
`CrossConfigFuzzTests` value-preservation cases.

Everything has been reverted. The tree is exactly as it was, and the finding is here instead of in the code.

---

## Why V2 is better — the part of the premise that holds

V2's added coverage is real, and its own documentation makes the case well:

* **Nulls.** V1 unwrapped `Nullable<T>` and always returned a value, and never returned a null reference
  either — so the fuzz suites drove the mapper exclusively with fully-populated graphs, and *none* of the
  null machinery (`NullStrategy`, `NullSubstitute`/DWARF049, nullable→non-nullable/DWARF070,
  `SkipNullSourceMembers`, `nullAsNull`, null-propagation in synthesized nested helpers) was ever exercised
  by a fuzzer. V2 calls that "the single largest blind spot in the suite", and it is right.
* **Boundary values.** Every integral draw in V1 was `rng.Next(1, MaxValue)` — never zero, never negative,
  never at a limit — so the narrowing and sign-conversion machinery was barely probed. V2 draws edges with
  `EdgeProbability`.
* **Graph fixtures.** V2 adds `MakeSelfLoop`, `MakeTwoNodeCycle`, `MakeDiamond`, `MakeOwnerGraph`.

None of that is in question. It is why V2 exists and why it should be the survivor.

---

## Why it cannot simply replace V1 — three regressions

### 1. Abstract and interface members come back `null`

```csharp
// ── Interface or abstract → try to pick a concrete ──────────────────
if (type.IsInterface || type.IsAbstract || depth >= DefaultMaxDepth)
{
    return type.IsValueType ? Activator.CreateInstance(type) : null;   // ← returns null
}
```

**The comment describes a behaviour the code does not have.** It says "try to pick a concrete" and then
returns `null` for every interface and abstract type.

That is precisely the bug V1 was fixed for, and V1's fix carries its provenance:

> Found migrating a ~300-map codebase off AutoMapper. The factory used to return null for any abstract or
> interface type, which made polymorphic graphs untestable — exactly the shape `[MapDerivedType]` exists to
> map. A `Dictionary<K, AbstractValue>` came out with null VALUES, so every fixture built from it exercised
> the null path rather than the dispatch path, and then looked like a real behavioural difference when
> replayed against a mapper that correctly refuses nulls.

V1 answers with `PickConcrete`, which scans loaded assemblies for a concrete, parameterless-constructible
subtype and — importantly — **orders candidates by full name before drawing**, so the choice is a pure
function of the seed rather than of assembly enumeration order.

Measured consequence of the swap: nine `PolymorphicMemberFuzzTests` failures, including
`The_fixture_factory_populates_an_abstract_member_rather_than_nulling_it`, which is the regression test for
this exact bug.

### 2. `[Flags]` enums never receive a combined value

V2 contains no `FlagsAttribute` handling at all. V1 does, with the reasoning recorded:

> A `[Flags]` enum's whole point is that COMBINED values (`Read | Write`) are legal — and picking a single
> declared member can never produce one. So the fuzzers only ever fed enums values that happened to have a
> name, and a by-name converter that threw on every combination looked perfectly healthy.

V1 also accumulates in the enum's own underlying type, because an unsigned enum can hold values above
`long.MaxValue` that `Convert.ToInt64` would throw on.

Measured consequence: `FlagsEnumCoverageSelfValidationTests.ObjectFactory_produces_COMBINED_flag_values_not_just_declared_ones`
fails.

### 3. Constructor selection is non-deterministic for records

V2 takes `ctors[0]` — whichever constructor reflection happens to return first. V1 orders by parameter count
and takes the fewest, with its own recorded reason: records and ctor-only DTOs were silently null in every
fuzz run, so the constructor-mapping path was never exercised by the value oracles.

Reflection member order is not contractually stable. A factory whose output depends on it is not
seed-deterministic, which is the property the whole fuzz corpus rests on.

---

## What this means

The two factories are not old-and-new. **Each carries fixes the other lacks**, which is why both are live:
V1's callers need substitution and flags, V2's callers need nulls and edges. Neither can be deleted as-is.

The intent — one factory — is right. The work is a **merge**, not a rename:

* **Recommended: port V1's three fixes into V2, then delete V1.** V2 is the better base (its additions are
  larger and harder to re-derive), and the three fixes are small, self-contained and already written. Their
  rationale comments must come across intact; each is a recorded regression.
* The reverse — porting V2's null/edge/graph work into V1 — is strictly more work for the same end state.

**Effect on the freeze (task 0.11):** unchanged in direction. `DwarfMapper.Testing`'s surface must not be
promoted to `Shipped` while it carries two overlapping factories, because promotion makes the duplication a
declared API and deleting the loser becomes a break. This finding does not remove that blocker; it changes
the work needed to clear it.

---

## Why this was reverted rather than pushed through

The instruction rested on "V2 should be better", and V2 *is* better along the axis it was written for. The
three regressions are not visible from its documentation — the abstract-substitution one is actively
contradicted by its own comment — so the premise was reasonable and simply turned out to be wrong.

Deleting V1 would have silently reintroduced a regression found in a real 300-map migration, in the
component whose entire job is generating the fixtures that other tests trust. That is the one place where a
silent coverage loss is least likely to be noticed: the fuzzers would have gone on passing, with less
behind them.
