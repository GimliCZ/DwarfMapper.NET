<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Every exclusion and bypass in the repository, enumerated and judged

Asked for after a review found round 27 "incomplete... especially for skipped testing anchors, unmaintained
relationship structures", with an instruction to scrutinise "exclusions and potentially unwanted bypasses of
testing methodology or architecture".

This is the enumeration. **Every count is measured**, by expanding the relevant globs or grepping the tree at
`bd135b2`, not by reading what a document claims.

The short answer: **the suppression surface is in good order, and the scope is not.** Nothing here is being
silently excused — the mechanisms that could hide a defect are individually pinned, justified and, where it
matters, re-measured rather than trusted. What is genuinely missing is reach: mutation testing runs over
9.4 % of `src/`, and the code round 27 created is entirely outside it.

## The suppression surface — nine mechanisms, all governed

| mechanism | count | what governs it | verdict |
|---|---:|---|---|
| xunit `Skip=` | **0** | — | no test is skipped anywhere |
| `[ExcludeFromCodeCoverage]` in `src/` | **9** | `RatchetInvariantScanTests` R3 | pinned exactly, each justified, category checked |
| `[SuppressMessage]` applied in `src/` | **0** | — | the six matches are the generator *reading* user suppressions |
| `#pragma warning disable` in `src/` | **3** | R4 (no new in-source disable) | `RS0030` + two `CA1508`, each with an inline reason |
| `// Stryker disable` | **1** | R4, pinned at exactly one | the grandfathered H7 progress guard, not adjudication |
| Stryker `ignore-mutations` | **0** | — | no mutator is disabled in any of the five legs |
| Stryker `ignore-methods` | **0** | — | " |
| `DeclaredDivergences` findings | **1** | re-classified live each run | down from 19; only `MaxDepth` remains |
| option-matrix `NotApplicable` cells | **8 of 24** | `OptionProbe.Classify` re-measures each | the B3 fix landed: a non-blank reason no longer passes on its own |

Two of these deserve their good verdict spelled out, because "pinned" can mean "frozen and forgotten".

**The coverage exclusions were re-verified, not assumed.** R3 pins nine `[ExcludeFromCodeCoverage]` uses and
names each one, but the *classification* behind them — round 22 examining every 0 %-covered class in the five
gated assemblies — is five rounds old, and nothing re-runs it. So it was re-run here, by mining nine existing
cobertura reports best-of-across-runs (a class covered by any run is not dead). **The only 0 %-covered class
in the five gated assemblies is `MapToAttribute`** — exactly the one the invariant names and deliberately
leaves unruled, pending a maintainer decision on its defensive `targets ?? Array.Empty<Type>()` arm. No new
by-design-dead class has appeared. The classification is still complete.

**The `NotApplicable` excuse class cannot go stale-red.** Each such cell claims its option is *unobservable*
at projection, and the contract now re-measures that claim with `OptionProbe.Classify` rather than accepting
a non-blank reason. An option that later starts acting turns the cell red with an instruction to re-declare
it.

## The build-level layer — where a bypass would be widest, and is not

An inline `#pragma` excuses one site; a project property excuses an entire assembly, so this layer matters
more than the one above and is easier to miss.

There is exactly one `Directory.Build.props`, and it is as strict as the toolchain allows:

```xml
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<WarningsNotAsErrors></WarningsNotAsErrors>   <!-- deliberately empty -->
<AnalysisLevel>latest-all</AnalysisLevel>
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
```

`latest-all` enables every analyzer rule the SDK ships rather than the default subset, `EnforceCodeStyleInBuild`
promotes the IDE style rules into the build alongside them, and both land as **errors** with nothing globally
exempted — there is no `NoWarn` at the root at all. Against that baseline:

| where | `NoWarn` | judgement |
|---|---|---|
| `src/DwarfMapper.CodeFixes` | `CA1303` | localisation of analyzer messages; one rule |
| `src/DwarfMapper.Testing` | `CA5394`, `CA1510`, `CA1032` | a fuzzing package: `CA5394` is "do not use insecure randomness", which is the package's entire job |
| `samples/*`, `benchmarks/*` | long lists | not shipping code; sample projects are written for readability |
| `tests/*` | naming/API rules | not shipping code |

Four rules across two shipping projects, each defensible. **No `src/` project relaxes a correctness rule.**

The `.editorconfig` carries exactly four `severity = none` entries — `CS0414`, `CA1802`, `CA1823`, `IDE0051` —
and all four are scoped to a **single test file**, `SurfaceFixtures.cs`, whose 19 `[SurfaceProbe]` fields are
read only through `GetFields()`/`GetValue()`, so no analyzer can see the use. The section's own comment
states the widening it causes rather than hiding it: the pragma covers the fixture block, the section covers
the file, so dead private code added elsewhere *in that one file* would go unflagged. That is the right way
to write an exemption — it tells you what it costs.

The strictness has one consequence worth naming, because it works against the tooling: **analyzer errors
silently shrink the mutation denominator, and `latest-all` plus `EnforceCodeStyleInBuild` makes the effect
large.** Stryker mutates, compiles, and discards whatever fails to build, so every mutant that trips `CS0162`
(unreachable code — which `if (false)` always produces here), `IDE0051`, `CA1822` or `MA0140` is rolled back
and never scored.

The round-27 pipeline leg measured it directly: of **13,129** mutants generated across the project,
**3,240 were discarded as compile errors** — with 8,049 filtered out as belonging to files outside the leg,
that is roughly **two thirds of the in-scope mutants dropped before a single test ran**. The generator leg's
ledger records the same effect from the other side: 4,142 rollbacks became 3,240 with no change to the
mutated files at all.

The score stays honest, because discarded mutants leave the denominator rather than counting as killed. But
the *reach* is much smaller than the file list suggests, and no document said so before this one. It is a
real trade rather than a defect: the same rules that reject those mutants also reject the corresponding
defects from ever being written by hand.

The same fact bit the hand-planted battery, where it was not benign: eight of its 33 entries did not compile,
and there the build failure marked the mutant **killed**, because a failed build fails every test. That is
fixed — the lane now builds each mutant and reports a non-compiling one as a catalogue defect — and it is
recorded in `FINDING-mutation-battery-catalogue-rot.md`.

## The gap that is real: scope

**9.4 % of `src/` is inside any mutation leg** — 3,334 of 35,538 lines. Per assembly:

| project | lines | in a leg | share |
|---|---:|---:|---:|
| `DwarfMapper.Generator` | 28,181 | 1,025 | **3.6 %** |
| `DwarfMapper` (runtime) | 3,431 | 941 | 27.4 % |
| `DwarfMapper.DocTooling` | 1,110 | 657 | 59.2 % |
| `DwarfMapper.CodeFixes` | 711 | 711 | 100 % |
| `DwarfMapper.Testing` | 2,054 | **0** | **0 %** |
| `Shared` | 51 | 0 | 0 % |

`AUDIT-mutation-scope.md` claimed 15.3 % overall and 13.6 % for the generator. Both were wrong — that
document's generator row credited a ten-file leg that **no revision of `stryker-config.json` has ever
had** — and it is corrected in the same change as this file.

This is not a suppression. Nobody excluded the generator; the legs were built file-by-file from the places a
silent wrong answer is worst, which was a defensible way to start. But it means the leg scores (84 / 95 / 97 /
87) are statements about a tenth of the product, and round 27 made the tenth smaller: the seam stage added
6,659 lines across 13 new `Pipeline/` files, none of them inside a glob, so the denominator grew while the
numerator did not. `stryker-config.pipeline.json` is the answer to that and is measured separately.

## The bypass that was real, and is now closed

Not an exclusion anyone declared — an assertion weaker than it looked.

The option matrix verifies a `Honoured` cell by requiring the generated output to **differ** from the same
source without the option:

```csharp
Assert.True(!string.Equals(generated, baseline, StringComparison.Ordinal), ...)
```

That defeats a *silently dropped* option, which is what it was written for. It cannot see **direction**: a
guard inverted so the option does the opposite of what it promises also changes the output, and passes. Five
cells are declared `Honoured` at projection, so five cells were only ever checked this weakly.

The repaired mutation battery is the instrument that settles which of them are actually asserted, because
inverting each guard is exactly what it does. Of the projection-option mutants — `NameConvention`,
`NullSubstitute`, `When=`, `Use=`, `AutoNest`, `ExplicitOnly`, `IgnoreObsoleteMembers` — **six were killed and
one survived**: `IgnoreObsoleteMembers`, which only two files in the repository mention and neither paired
with projection. It now has a test asserting the direction, and a control asserting the opposite direction,
verified to fail under the mutant and pass on clean source.

The general lesson is worth keeping: a contract that asserts *an effect exists* is not the same as one that
asserts *which effect*, and the difference is invisible until something inverts the guard.

## What remains open

1. **`DwarfMapper.Testing` has 0 % mutation coverage and ships as its own NuGet package** — defects there
   land in other people's test suites. It also sits at 87.1 % line coverage, so the prior audit's judgement
   holds: raise line coverage first, then mutate.
2. **The generator's remaining 96 %.** Tractable only as per-area legs added one at a time, each with its own
   measured floor — `CollectionConverter` and `DictionaryConverter` first, since they synthesize the code
   that moves consumer data.
3. **`MapToAttribute`'s 0/4** stays a declared hole until a maintainer rules on the defensive arm. Recorded
   rather than quietly excluded, which is the correct state for an unruled category.
