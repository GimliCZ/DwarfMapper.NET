<!-- SPDX-License-Identifier: GPL-2.0-only -->

# The extracted pipeline gets a mutation leg — and the first attempt was the wrong size

Round 27 moved three giant methods into 13 new `Pipeline/` files and the brief asked for the moved functions
to be covered "close to 100 % **from mutation** and other standpoints". Line coverage (94.5 %) and reach
(32/32 methods) were delivered. Mutation coverage of those functions was **0 %**, because no leg's `mutate`
globs name any of the new files — measured in
[`AUDIT-mutation-scope.md`](AUDIT-mutation-scope.md), which also corrects that document's own overstatement
of the gap. This leg closes that.

## Why a fifth config rather than widening the generator leg

`stryker-config.json` guards four small, deliberately chosen files — `EquatableArray`, `BlittableProof`,
`ConstructorSelector`, `LocationInfo` — and has held **break 84** for several rounds. Folding 6,659 new lines
into it would collapse that number and destroy a gate that is doing its job, and the collapse would be
indistinguishable from a regression in the four files it currently protects.

A separate leg keeps the two measurements separate and follows the CodeFixes precedent exactly: a new leg is
pinned to its **first measured score**, and the floor rises later only by killing survivors.

## The first attempt, and why it was abandoned

The leg was first scoped at all 13 extracted files, 6,659 lines. That run is worth recording in full,
because its failure is the evidence for how the leg is scoped now.

Stryker generated **13,129 mutants**, discarded 3,240 as compile errors, filtered 8,049 as belonging to
files outside the leg, and settled on **1,353 to test**. After **158 minutes on 12 cores it had not
finished**. While running it leaked vstest host processes: 32 alive, of which **26 had accumulated less than
one second of CPU** and 13 were more than five minutes old. Six were doing work; the rest were holding a few
hundred megabytes each and never being reclaimed.

It was killed deliberately rather than left overnight, for two independent reasons:

- **It cannot run in the nightly.** The CI matrix sizes each leg at roughly ten times its measured
  wall-clock — `runtime` 11:36 → 120, `generator` 19:41 → 200. Ten times 158 minutes is far beyond the
  six-hour ceiling GitHub Actions puts on a job.
- **It did not reliably terminate locally either**, which disqualifies it as a gate no matter what CI does.

Recovery is worth a line, because a killed mutation run leaves debris in a place that matters. `src/` was
untouched — Stryker mutates in a sandbox — but nine test projects were left holding a planted
`DwarfMapper.Generator.dll` in `bin/`, with three `*.stryker-unchanged` backups beside them. Those markers
are what `RepoWriteGuard` refuses repo writes on, and its failure message states the remedy verbatim: delete
the backups and their sibling DLLs, then rebuild. `Remove-PlantedMutants` did the first part, and the
solution then rebuilt clean with 0 errors and 0 warnings.

**The leg is now one area**: the four member-resolution phases plus the two context records they are
parameterised by — 944 lines, roughly 190 tested mutants at the density the abandoned run measured, which is
the same order as the CodeFixes leg (177 in 6 min 20 s). The other ten extracted files are named in the audit
as the follow-on, one area per leg, each sized to complete.

## A near-miss worth recording: the score that described a different leg

The second attempt was killed by its own 60-minute fuse — a guess, and a wrong one, since the observed rate
is about four mutants a minute and there were 236 of them. It therefore wrote no report.

The script waiting to record the result then took "the newest `mutation-report.json` on disk", found the
**previous day's generator-leg report**, and wrote that leg's 338 scoreable and 84.32 % into this leg's config
and ledger row as its first measurement. Nothing in the output looked wrong. The giveaway was one line
further down: the undetected mutants were in `BlittableProof` and `ConstructorSelector`, which this leg does
not mutate. Both edits were reverted before anything was committed.

It is the same defect this whole round has been about, committed by the tool built to record the round's
results: **an instrument produced a confident number about something other than what it claimed to measure.**
A stale battery anchor scored a defect nobody had introduced; a scope audit credited a ten-file leg that
never existed; this attributed one leg's score to another. In every case the arithmetic was impeccable and
the subject was wrong.

The applier now refuses unless the report is newer than the run's start **and** the files it scored mutants
in are within the files this leg mutates. The second guard is the load-bearing one — a timestamp is satisfied
by any recent run — and it too was wrong on the first attempt: it compared the report's whole file list, but
Stryker reports on the entire project with most files carrying only `Ignored` or `CompileError` mutants, so
it would have refused the correct report as readily as the wrong one. That was found by negative-testing the
guard against a known-wrong report rather than by reading it, which is the only method that has reliably
worked here.

## What Stryker actually measures here

The created-versus-tested gap is large enough to be worth writing down, because it is the difference between
what the file list suggests and what the leg can say:

| | mutants |
|---|---:|
| created across the project (13-file scoping) | 13,129 |
| removed by the `mutate` filter (files outside this leg) | 8,049 |
| **compile errors** (whole project) | **3,240** |
| could not be injected | 4 |
| not covered by any test | 132 |
| removed by the "block already covered" filter | 351 |
| **tested** | **1,353** |

The 3,240 compile errors are not noise, and they are not this leg's fault: `Directory.Build.props` sets
`TreatWarningsAsErrors=true` with an empty `WarningsNotAsErrors`, so a mutant that produces unreachable code
(`CS0162` — which every constant-folded condition does), an unused private member (`IDE0051`), a member that
no longer touches instance state (`CA1822`) or a ternary with identical arms (`MA0140`) fails to build and is
rolled back. Stryker excludes those from the denominator rather than counting them, so **the score stays
honest — but the reach is smaller than the file list implies.** The same fact is why `if (false)` is not a
usable form in the hand-planted battery, where a non-compiling mutant was being counted as *killed*; see
[`FINDING-mutation-battery-catalogue-rot.md`](FINDING-mutation-battery-catalogue-rot.md).

<!-- RESULT -->

## What this leg is not

It is **not** a kill program. Pinning the floor at the first measurement is the whole of this change, exactly
as the CodeFixes leg was pinned at 52.54 % before any survivor was addressed and only then raised to 87.01 %.
Dispositioning survivors is separate work: it needs a case analysis per survivor, and the ledger's bar for
`proven-equivalent` is that original and mutant agree on **every reachable input** — which is not something to
assert in bulk at the end of a long run.

The survivor worklist below is recorded at file/method grain for that reason. Per-survivor prose at this
scale would be theatre, and the M24 experience says the useful signal is concentrated: of seven
projection-option mutants in the hand-planted battery, six were killed and exactly one named a real gap.
