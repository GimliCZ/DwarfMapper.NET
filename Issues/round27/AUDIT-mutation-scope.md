<!-- SPDX-License-Identifier: GPL-2.0-only -->

# What mutation testing does NOT cover, and whether widening it would pay

Three legs report 84 %, 95 % and 97 %. Those are honest numbers about the files they name. This is what
they do not name, measured rather than estimated, and a judgement on each.

## The headline

**12.0 % of `src/` is inside any leg's `mutate` globs** — 4,278 of 35,538 lines.

> **Corrected 2026-08-27.** This file previously said 15.3 %, on a table crediting the generator with 10
> files and 3,849 mutated lines. **No revision of `stryker-config.json` has ever listed more than the four
> globs it carries today** (`git log -p` on that file: `EquatableArray`, `BlittableProof`,
> `ConstructorSelector`, `LocationInfo` — unchanged since the file was created). The generator's real share
> is **3.6 %**, not 13.6 %, and the headline was inflated with it. The document written to stop anyone
> overstating mutation coverage was itself overstating it by a third.

The scores are therefore not statements about the product. They are statements about a seventh of it, and
the seventh was chosen well: `EquatableArray`, `ConstructorSelector`, the registry, the exception types,
the doc pipeline — the places where a silent wrong answer is worst. But "DwarfMapper is mutation-tested at
95 %" is not a sentence the evidence supports, and this file exists so nobody writes it.

| project | files | in a leg | lines | mutated lines | share | line coverage |
|---|---:|---:|---:|---:|---:|---:|
| `DwarfMapper.Generator` | 68 | 7 | 28,181 | 1,969 | **7.0 %** | 94.5 % |
| `DwarfMapper` (runtime) | 42 | 6 | 3,431 | 941 | **27.4 %** | 73.9 % |
| `DwarfMapper.DocTooling` | 11 | 5 | 1,110 | 657 | **59.2 %** | 96.3 % |
| `DwarfMapper.CodeFixes` | 4 | 4 | 711 | 711 | **100 %** | 96.2 % |
| `DwarfMapper.Testing` | 7 | **0** | 2,054 | 0 | **0 %** | 87.1 % |
| `Shared` | 1 | **0** | 51 | 0 | **0 %** | — |
| **all** | **133** | **22** | **35,538** | **4,278** | **12.0 %** | |

Re-measured 2026-08-27 at `0485ff7` by expanding each config's `mutate` globs against the files actually on
disk. `DwarfMapper.CodeFixes` moved from 0 % to 100 % because the leg this audit recommended was built.

**Round 27 made the generator's share worse, not better.** The seam stage added 6,659 lines across 13 new
`Pipeline/` files — `MapperExtractor.Phases.cs` (3,398), `.Conversions.Arms.cs` (795),
`.Members.Phases.cs` (775), `.Flatten.Directive.cs` (609) and nine smaller ones — and **not one of them is
inside a `mutate` glob**. The refactor moved the most intricate code in the product into files no mutation
leg names, so the denominator grew while the numerator did not.

This is the gap that matters most here: the round-27 brief asked for the moved functions to be covered
"close to 100 % from mutation and other standpoints". Line coverage and reach were delivered; **mutation
coverage of those functions was 0 %**.

`stryker-config.pipeline.json` closes the first slice of it — the four member-resolution phases and the two
context records that parameterise them, 944 lines — and the sizing of that slice was decided by measurement
rather than taste. A leg scoped at all 13 extracted files generated 13,129 mutants, of which 1,353 needed
testing, and **after 158 minutes on 12 cores it had not finished**, leaking idle vstest hosts as it went (26
of 32 alive with no CPU). It was abandoned deliberately. Two things follow, and both argue the same way:

- **It cannot run nightly.** The CI matrix sizes each leg at roughly ten times its measured wall-clock; ten
  times 158 minutes exceeds the six-hour ceiling GitHub Actions imposes on a job. The four existing legs run
  in 5–20 minutes and fit comfortably.
- **It did not reliably terminate locally either**, which makes it useless as a gate regardless of CI.

So the remaining ten files are not "not worth covering" — they are covered **one area at a time**, each leg
sized to complete, each with its own measured floor, exactly as section 3 below already recommended. The
abandoned run's cost curve is what makes that recommendation concrete: ~0.20 tested mutants per line, so an
area of about a thousand lines is the largest unit that behaves.

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

28,181 lines. The CodeFixes leg gives the only honest cost datum this repo has: **177 mutants from 711
lines**, roughly one mutant per four lines. Extrapolated naively the generator is ~7,000 mutants, which is
not a gate anyone runs in one leg. (The "718 mutants from ~2,800 lines" this section used to cite belongs
to no leg that exists; it went with the erroneous table above.)

The tractable form is per-area legs added one at a time, each with its own measured floor, prioritised by
where a wrong answer is worst — `CollectionConverter` and `DictionaryConverter` first, since they synthesize
the code that actually moves consumer data.

### 4. `Shared` — no

52 lines, and it is `DwarfLimits`: constants with no branches. There is nothing for a mutator to say that
the compiler and `ShippedRuntimeSafetyTests` do not already say.

## The recommendation in one line

The CodeFixes leg is done (87.01 %, and it found what it was predicted to find). **Next is a leg over the
round-27 extracted `Pipeline/` files**, because that is where the brief actually pointed and where the share
was 0 %. Record **12.0 %** wherever the leg scores are quoted, so the numbers keep meaning what they say.

That figure is no longer maintained by hand: `MutationScopeScanTests` recomputes it from the configs on
disk and fails when this table drifts from them. The 15.3 % this document used to publish was a one-off
measurement nothing could re-derive, which is exactly how it survived being wrong for so long.
