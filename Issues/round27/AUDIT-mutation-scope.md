<!-- SPDX-License-Identifier: GPL-2.0-only -->

# What mutation testing does NOT cover, and whether widening it would pay

Three legs report 84 %, 95 % and 97 %. Those are honest numbers about the files they name. This is what
they do not name, measured rather than estimated, and a judgement on each.

## The headline

**15.3 % of `src/` is inside any leg's `mutate` globs** — 5,458 of 35,664 lines.

The scores are therefore not statements about the product. They are statements about a seventh of it, and
the seventh was chosen well: `EquatableArray`, `ConstructorSelector`, the registry, the exception types,
the doc pipeline — the places where a silent wrong answer is worst. But "DwarfMapper is mutation-tested at
95 %" is not a sentence the evidence supports, and this file exists so nobody writes it.

| project | files | in a leg | lines | mutated lines | share | line coverage |
|---|---:|---:|---:|---:|---:|---:|
| `DwarfMapper.Generator` | 68 | 10 | 28,242 | 3,849 | **13.6 %** | 93.6 % |
| `DwarfMapper` (runtime) | 42 | 6 | 3,473 | 947 | **27.3 %** | 73.9 % |
| `DwarfMapper.DocTooling` | 11 | 5 | 1,121 | 662 | **59.1 %** | 96.3 % |
| `DwarfMapper.CodeFixes` | 4 | **0** | 715 | 0 | **0 %** | **88.8 %** |
| `DwarfMapper.Testing` | 7 | **0** | 2,061 | 0 | **0 %** | **66.3 %** |
| `Shared` | 1 | **0** | 52 | 0 | **0 %** | — |

The generator's 13.6 % is itself flattered by round 27: of its 3,849 mutated lines, ~2,800 are the six
files this round added to a new leg. Before that it was nearer 3.7 %.

## What is NOT excluded — worth stating, because it is the good news

- **No mutator is disabled anywhere.** No config carries `ignore-mutations` or an excluded-mutator list, and
  none sets `mutation-level` below the default. Every mutation kind Stryker offers is in play on the files
  that are in play.
- **No test is skipped.** Zero `Skip =` on any `[Fact]`/`[Theory]`; every run reports `Přeskočeno: 0`.
- **No test project is excluded from a leg**, and the runtime config records the measurement proving why
  that matters: excluding `DwarfMapper.Generator.Tests` would drop that leg from 97 % to **60.17 %**, because
  three fuzz/oracle classes there contribute 37 kills, 8 of them exclusive. It also records that an earlier
  "costs zero kills" claim was false *and* had been measured on a run where the exclusion never took effect.
- **Exactly one in-source `Stryker disable`**, the H7 progress guard, paired with its restore and pinned at
  exactly 1 by `RatchetInvariantScanTests`.

So the exclusion story is purely FILE SCOPE. Nothing is being hidden by a filter, a skip, or a suppression.

## Would exposing more pay? Ranked, with the reasoning

### 1. `DwarfMapper.CodeFixes` — DONE, and the answer was emphatic

**88.8 % line coverage and 0 % mutation coverage.** That combination is the exact shape mutation testing
exists to interrogate: code that is thoroughly *executed* by tests which may assert nothing about it. 268
coverable lines is a small mutant population, so the leg would be fast.

It is also user-facing in the most literal way — these are the IDE lightbulbs a consumer clicks. A code fix
that produces subtly wrong source is worse than one that fails loudly, and nothing proved the tests would
notice.

**Measured 2026-08-26, first run: 52.54 %** — 93 detected of 177 scoreable, 55 survived, 29 uncovered, in
7 min 04 s. 88.8 % of these lines are executed by tests and barely half the mutants are detected, which is
the gap this leg was predicted to expose and did. Per provider: `AddReverseMapInverse` 40.0 %,
`AddMapIgnore` 50.0 %, `ResolveExplicitOnlyMember` 54.8 %, `RestateBaseConfiguration` 55.7 %.

The survivor profile names the remedy. Sixteen **String** mutations survive, and in a code fix the string
literals ARE the source written into the consumer's file plus the action title in their lightbulb — so the
tests assert very little about what the fix produces. Then 11 Logical, 9 Equality and 8 Boolean survivors:
condition logic nothing checks. The 29 uncovered mutants are a different problem needing a different
remedy — 12 Statement and 11 Block-removal mutants sit in code no test reaches, so tests must be written
before assertions can be strengthened.

The leg is live at `break: 52`, its honest first floor. Both sibling legs started this way — the generator
at 71.64 and doc-tooling at 67.96 — and were raised by killing survivors, never by re-measuring.

### 2. `DwarfMapper.Testing` — yes, but coverage first

**66.3 % line coverage, 0 % mutation.** This ships as its own NuGet package, so its defects land in other
people's test suites. But mutants in the uncovered third would come back `NoCoverage`, which scores as
undetected and would produce a low floor that says "we have not written the tests" rather than "the tests
are weak". Raise line coverage first, then mutate; otherwise the number measures the wrong thing.

### 3. The generator's other 86 % — worth it, but not as one leg

28,242 lines and the round-27 leg already shows the cost curve: 718 mutants from ~2,800 lines. Extrapolated
naively that is tens of thousands of mutants, which is not a gate anyone runs.

The tractable form is per-area legs added one at a time, each with its own measured floor, prioritised by
where a wrong answer is worst — `CollectionConverter` and `DictionaryConverter` first, since they synthesize
the code that actually moves consumer data.

### 4. `Shared` — no

52 lines, and it is `DwarfLimits`: constants with no branches. There is nothing for a mutator to say that
the compiler and `ShippedRuntimeSafetyTests` do not already say.

## The recommendation in one line

Add a CodeFixes leg next — small, fast, well-covered, user-facing, and currently unexamined — and record
the 15.3 % figure wherever the leg scores are quoted, so the numbers keep meaning what they say.
