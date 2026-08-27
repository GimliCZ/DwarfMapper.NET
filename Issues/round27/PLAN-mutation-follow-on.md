<!-- SPDX-License-Identifier: GPL-2.0-only -->

# The mutation follow-on: 55 survivors, and five more areas

Two separate jobs, and they are worth keeping separate because one raises a floor and the other lowers a
denominator.

1. **Kill the pipeline leg's survivors** — 76.99 % against a leg that is already gated.
2. **Cover the remaining extracted files** — 5,715 lines that no leg names, one area at a time.

Everything below is measured. The density in particular: the first leg ran **236 tested mutants from 944
lines, 0.250 per line**. The estimate used to size it was 0.20, taken from the abandoned 13-file run, so the
heuristic under-predicted by a quarter — recorded here so the next area is sized against the real figure and
the error does not compound.

## 1. The 55 undetected mutants — and they are not 55 problems

`MapperExtractor.Members.Phases.cs`, 52 survived plus 3 uncovered. By mutator:

| mutator | count |
|---|---:|
| Statement mutation | 17 |
| String mutation | 17 |
| Logical mutation | 7 |
| Equality mutation | 6 |
| `!x` → `x` | 3 |
| Conditional (false) | 2 |
| Conditional (true) | 2 |
| Block removal | 1 |

They cluster on a handful of lines, and **a cluster is normally one untested behaviour rather than N of
them** — six mutants on one guard means nothing exercises that guard, not that six things are wrong:

| line | × | the code |
|---|---:|---|
| 59 | **6** | `if (string.IsNullOrEmpty(m.SourceName) \|\| …` |
| 51 | 3 | `else if (tm is IFieldSymbol f && !f.IsReadOnly && !f.IsConst…` |
| 583 | 3 | `var valueExpr = epConv is null ? ep.Name : epConv + "(" + ep…` |
| 668 | 3 | `$"target '{target.Name}' matches multiple source members und…` |

So the honest worklist is closer to **a dozen behaviours**, and the four above account for 15 of the 55.

**Start with line 59.** Six undetected mutants on one guard is the strongest signal in the report, and the
shape matches what the projection gap looked like before it was found: a condition every path happens to
satisfy, so no test distinguishes it.

**Treat the 17 String mutations as their own decision, not as work.** They are overwhelmingly diagnostic
message text, and a suite that asserts diagnostic **ids** rather than prose will never kill them. Three ways
out, and the choice is a maintainer's:

- assert the message text where the wording is itself a contract (the repo already does this for remedies in
  `NegativeCases`);
- adjudicate them equivalent — but the bar is "agree on every reachable input", and message text is reachable
  output, so that argument has to be made per-mutant and will often fail;
- accept them as a permanent, *documented* ceiling on this leg, the way `rawCeiling` already works elsewhere.

What must not happen is killing them by asserting prose nobody intended to freeze. That converts a mutation
score into a wording lock, and the next person to improve an error message pays for it.

## 2. The remaining extracted files

5,715 lines, ~1,424 tested mutants at the measured density — which is why this is five legs and not one.

| file | lines | ~tested | fits a leg? |
|---|---:|---:|---|
| `MapperExtractor.Phases.cs` | 3,398 | ~849 | **no — must be split** |
| `MapperExtractor.Conversions.Arms.cs` | 795 | ~198 | yes |
| `MapperExtractor.Flatten.Directive.cs` | 609 | ~152 | yes |
| `MapperExtractor.Flatten.Hetero.cs` | 278 | ~69 | yes, pairs with Hetero.Arms |
| `MapperExtractor.Hetero.Arms.cs` | 244 | ~61 | yes, pairs with Flatten.Hetero |
| `MapperExtractor.Flatten.Members.cs` | 120 | ~30 | yes, pairs with Flatten.Directive |
| `MethodExtractionContext.cs` | 90 | ~22 | context record |
| `FlattenGraphContext.cs` | 75 | ~18 | context record |
| `ConversionRequest.cs` | 55 | ~13 | context record |
| `DwarfLimits.cs` | 51 | ~12 | `src/Shared` — different project, needs its own config |

**Proposed legs, each sized against the one that actually completed** (944 lines / 236 mutants / 37 minutes):

| leg | files | lines | ~tested |
|---|---|---:|---:|
| `conversions` | `Conversions.Arms.cs` + `ConversionRequest.cs` | 850 | ~213 |
| `flatten` | `Flatten.Directive.cs` + `Flatten.Members.cs` + `FlattenGraphContext.cs` | 804 | ~201 |
| `hetero` | `Flatten.Hetero.cs` + `Hetero.Arms.cs` + `MethodExtractionContext.cs` | 612 | ~153 |
| `phases-*` | `Phases.cs`, split — see below | 3,398 | ~849 |

`Phases.cs` is the problem. At ~849 tested mutants it is three to four times the size that completes, and
Stryker's `mutate` globs select whole files, so it cannot be split without splitting the file. That is a
real refactor, not a config change, and it should be judged on its own merits — a 3,398-line partial is
worth splitting for reasons that have nothing to do with mutation testing, and if it is split for those
reasons the legs follow for free.

Until then `Phases.cs` stays uncovered, and that is a stated gap rather than an oversight.

## What "prepared" means here, and what it does not

Ready now:

- the leg pattern is proven end to end — config, ledger row, badge, `Assert-LegScoreWithinBand`, CI matrix
  entry, and the five sites `M5` keeps consistent;
- the sizing rule has a measured constant (0.250 tested mutants per line) instead of a guess;
- the failure modes are known and gated: a red baseline invalidates a run, a fuse sized by guess cuts one
  off, and a recorder that takes "the newest report" will attribute another leg's score to this one. All
  three happened; all three are now guarded.

Not ready, and not to be pretended otherwise: **none of these legs can run in CI.** Each takes tens of
minutes locally and the nightly budget cannot absorb them, so every one is a local gate whose floor is
recorded in the ledger. `--with-baseline` would have changed that arithmetic and it needed the Stryker
Dashboard, which cannot be logged into — recorded as declined in
[`PLAN-hosted-reports.md`](PLAN-hosted-reports.md).

Also open, and older than this work: `mutation (generator, stryker-config.json)` has failed on master's
nightly every night since at least 2026-08-24. That leg gates at `break 84` against a ledger-recorded 84.32,
which is under a point of headroom — so it is a plausible candidate for a genuine floor breach rather than
infrastructure, and it should be diagnosed before any new leg is added beside it.
