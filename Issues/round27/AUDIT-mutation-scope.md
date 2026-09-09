<!-- SPDX-License-Identifier: GPL-2.0-only -->

# What mutation testing does NOT cover, and whether widening it would pay

Three legs report 84 %, 95 % and 97 %. Those are honest numbers about the files they name. This is what
they do not name, measured rather than estimated, and a judgement on each.

## The headline

**11.1 % of `src/` is inside any leg's `mutate` globs** — 4,785 of 42,976 lines.

> **Corrected 2026-09-07.** This line said 12.0 % while the table below said 11.5 %, and the two are
> the same measurement — the headline simply stopped being updated when the table was re-measured
> under it. It is the table's `all` row now, and it stays so: `MutationScopeScanTests` re-derives that
> row from the configs on every build, so the headline can no longer drift away from it alone.

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
| `DwarfMapper.Generator` | 75 | 7 | 34,810 | 2,473 | **7.1 %** | 95.7 % |
| `DwarfMapper` (runtime) | 44 | 6 | 3,614 | 944 | **26.1 %** | 73.9 % |
| `DwarfMapper.DocTooling` | 11 | 5 | 1,111 | 657 | **59.1 %** | 96.3 % |
| `DwarfMapper.CodeFixes` | 5 | 4 | 1,336 | 711 | **53.2 %** | 96.8 % |
| `DwarfMapper.Testing` | 9 | **0** | 2,243 | 0 | **0 %** | 96.4 % |
| `Shared` | 1 | **0** | 51 | 0 | **0 %** | — |
| **all** | **145** | **22** | **43,165** | **4,785** | **11.1 %** | |

Re-measured 2026-08-27 at `0485ff7` by expanding each config's `mutate` globs against the files actually on
disk. `DwarfMapper.CodeFixes` moved from 0 % to 100 % because the leg this audit recommended was built.
`DwarfMapper.Generator`'s file/line counts were bumped again 2026-09-03 (round 29, T0.2): the span-map blit
added one file, `Pipeline/MapEmitter.SpanMap.cs` (82 lines, outside every `mutate` glob), and a top-level
`Nullable<T>` refusal in `BlittableProof.cs` (39 lines, INSIDE the glob — a CS0453 regression T0.1's Nullable
branch introduced, found and fixed in the same task) — mutated lines move with it, both shares within the
gate's 1 pp tolerance of where they were. Bumped once more the same day (round 29, T0.3): `DWARF101`'s
struct-layout measurement added `Pipeline/LayoutHygiene.cs` (273 lines, outside every `mutate` glob) and 25
lines to `Pipeline/MapperExtractor.Conversions.Arms.cs`, so the denominator grew again and the generator's
share reads 7.0 % where it read 7.1 %. Re-measured in full 2026-09-06 (round 29, T2.1), which added
`Pipeline/TransferModelShape.cs` (757 lines, outside every `mutate` glob) and 59 lines to
`Pipeline/LayoutHygiene.cs`: every column above is the measurement as of that commit rather than an
increment on the last one, because the line columns had drifted further than the file counts â€” the gate
pins files EXACTLY and shares to a percentage point, so 2,250 lines of round-29 work had accumulated in the
generator's denominator (and 253 in `BlittableProof.cs`, inside the glob) without tripping it. The
generator's share reads 7.3 % where it read 7.0 %, and the headline is unchanged at 12.0 %. Re-measured
in full again 2026-09-06 (round 29, T2.3), which added the FIFTH code-fix provider,
`ConvertToRecordStructCodeFixProvider.cs` (559 lines — the `DWARF103` transitive rewrite). The generator grew
1,103 lines across the same task (the classifier's inlined-model collector and the `DWARF103` report site's
diagnostic properties, 28 of them inside `BlittableProof.cs`'s glob); its share reads 7.2 % where it read
7.3 %. **Re-measured in full again 2026-09-07**, after the `[GenerateView]` endpoint was withdrawn
(`Issues/round29/WITHDRAWN-generated-views.md`): the four files that endpoint added —
`Model/ViewModel.cs`, `Pipeline/MapperExtractor.Views.cs`, `Pipeline/ViewEmitter.cs` and the runtime's
`GenerateViewAttribute.cs` — are gone, so the file counts return to 71 and 42 (137 overall). The LINE
columns do not return with them, and that is the point of re-measuring rather than restoring: unrelated
round-29 work landed either side of the endpoint, so the generator reads 32,784 lines against the 31,965
recorded before it, and 2,322 mutated lines against 2,289 — the numerator moved in
`Pipeline/MapperExtractor.Members.Phases.cs`, which the pipeline leg already covers, and not one of the
withdrawn files was ever inside a `mutate` glob. Shares: the generator 7.1 %, the whole of `src/` 11.4 %.

**Re-measured in full again 2026-09-07 (round 29, T3.1)**, which added `[MapShare]`: two generator files —
`Pipeline/ImmutabilityProof.cs` (the deep-immutability proof, 451 lines) and
`Pipeline/MapperExtractor.Share.cs` (the per-member decision, 161 lines), both **outside** every `mutate`
glob — and the runtime's `MapShareAttribute.cs` (86 lines, likewise outside). File counts 71 → 73 and
42 → 43, 137 → 140 overall. The numerator moved by 48 lines in
`Pipeline/MapperExtractor.Members.Phases.cs` and `Pipeline/MemberResolutionContext.cs`, which the pipeline
leg already covers. The generator's share reads 7.0 % where it read 7.1 %, the runtime's 26.8 % where it
read 27.5 %, and the headline 11.2 % where it read 11.4 %. **The measurement is the whole table again, not
an increment**: the line columns had drifted between the last re-measure and this one, which is the drift the
file-count pin cannot see and the reason this document is re-measured rather than adjusted.

**Re-measured in full again 2026-09-07 (round 29, T3.2)**, which added `[MapDenseEnumKeys]`: two generator
files — `Pipeline/DenseEnumProof.cs` (the range proof and the helper it authorises, 418 lines) and
`Pipeline/MapperExtractor.DenseEnum.cs` (the per-member decision and the directive's own validation, 235
lines), both **outside** every `mutate` glob — and the runtime's `MapDenseEnumKeysAttribute.cs` (91 lines,
likewise outside). File counts 73 → 75 and 43 → 44, 140 → 143 overall. The numerator moved by 103 lines,
in `Pipeline/BlittableProof.cs`'s neighbours inside the pipeline leg —
`Pipeline/MapperExtractor.Members.Phases.cs`, `Pipeline/MapperExtractor.Members.cs`,
`Pipeline/MapperExtractor.cs` (the `DWARF092` arm) and `Pipeline/MemberResolutionContext.cs` — all of which
that leg already covers. The generator's share reads 7.1 % where it read 7.0 %, the runtime's 26.1 % where
it read 26.8 %, and the headline 11.1 % where it read 11.2 %. **The measurement is the whole table again,
not an increment**, for the reason the block above gives: the line columns drift between re-measures and the
file-count pin cannot see it.

**Re-measured in full again 2026-09-09 (round 29, Phase 4)**, which added the lens-law oracle: two files in
`DwarfMapper.Testing` — `LensLaws.cs` (the two verifiers, 140 lines) and `LensLawException.cs` (the informed
dump, 49 lines), both **outside** every `mutate` glob, as the whole of that package always has been. File
counts 7 → 9 for `DwarfMapper.Testing` and 143 → 145 overall; its line column 2,054 → 2,243, and the
headline stays at 11.1 % because the denominator moved by 189 lines against 43,165. Its `line coverage`
column is corrected from a stale 87.1 % to the enforced floor of 96.4 % in the same pass — the column had
not been touched since the package was at 87.1 %, and nothing checks it, which is its own small instance of
the disease this document exists to name.

**`DwarfMapper.Testing` remains at 0 % mutation coverage**, and the new code does not change the argument
for that: the package is test-only, never AOT-published, and its own suite is what verifies it. What DID
change is the consequence of a wrong answer from it. `RoundTrip.Verify` and now `LensLaws` are the
instruments consumers grade THEIR mappers with, so a verifier that silently passes is a verifier that
certifies a broken map. That moves the package up the list in §2 below rather than settling it.

**Neither the range proof nor the decision beside it is in a mutation leg**, which is the same sentence the
block below writes about `ImmutabilityProof` and it is worse here: a wrong `true` from `DenseEnumProof`
authorises an index. What guards it instead is a per-refusal test set (`MapDenseEnumKeysTests`, 32 cases,
most of them refusals), a runtime suite that executes the fill and the guard, a `NegativeCases` row pinning
the out-of-range message, and the `feat:MapDenseEnumKeys` golden case — whose pinned bytes include the
emitted subtraction, so an offset silently dropped on the way through moves a hash. Adding the file to a leg
is a leg-runtime decision outside this task; recorded here so it is a known hole rather than an assumed
covering.

**The proof itself is not in a mutation leg, and that is worth stating rather than leaving to the table.**
`ImmutabilityProof` is the component whose wrong answer is worst in this feature — a false `Proven` shares a
mutable object and nothing afterwards reports it — and it sits in the 93 % of the generator no leg covers.
What guards it instead is a per-verdict test set (`MapShareTests`, `MapShareRuntimeTests`) and the golden
manifest's `feat:MapShare` case, which carries a proven member, an asserted one and a copied one in one
method precisely so that a verdict flipping in either direction moves a pinned byte.

**`DwarfMapper.CodeFixes` drops from 100 % to 53.2 %, and that is the honest number rather than a regression
nobody noticed.** T2.3 did add the new provider to `stryker-config.codefixes.json` and ran the leg
(2026-09-06): **79.24 %** — 449 mutants created, 230 tested, 187 killed, 43 survived, 6 uncovered — which
crashes that config's `break: 87`. Two facts from that run decided it was not a threshold to lower:

- **26 of the 43 survivors are in the new file, and roughly half are provably equivalent** — three
  `ConfigureAwait(false)` flips, the two `converted.Add` lines that add the same symbol for a non-generic
  type, `Short`'s strip-the-`T:` branch which is a no-op for any namespaced id. Even a complete kill program
  over *the mutants that ran* lands near **85 %**, *below* the current break — and that figure is not itself
  a ceiling, because the run's mutant set was truncated by the safe-mode removal in the next bullet. The raw
  ceiling has moved either way, and re-pinning it needs the per-mutant case analysis
  `Issues/ledgers/codefixes-mutation-survivors.md` holds for the existing 23, not a number chosen to fit.
- **Stryker's safe mode removed every mutation in `Convert` and `ObliviousNestedMembers`** — the two methods
  that perform the rewrite — because two mutants there hit CS0165 and CS1503. That run scored the new file
  on its periphery and would have said almost nothing about the code that edits a consumer's source.

The 43 survivors split 3 + 5 + 3 + 12 = 23 across the four existing providers and 26 in the new file; those
23 are exactly the set that ledger already dispositions, so nothing in T2.3 moved them and the four-file
leg's standing 87.01 % continues to describe it unchanged.

So the file stays out of the leg until a task does the kill program properly, and the debt is recorded HERE,
where this scan pins it exactly and it cannot rot. The headline falls to **11.5 %** from 12.0 % — the
denominator grew by 559 lines of the most literally user-facing code in the repository, and the numerator did
not move, which is precisely the shape of gap this audit was written to make visible rather than the kind it
should be allowed to hide.

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
