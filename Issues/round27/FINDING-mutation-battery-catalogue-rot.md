<!-- SPDX-License-Identifier: GPL-2.0-only -->

# The hand-planted mutation battery graded 24/33 — the real number is 17/17

`scripts/mutation-battery.sh` is the lane that checks the **suite**, not the product: each entry is a defect
this project shipped or narrowly avoided, re-planted in the source, and a survivor names a behaviour that
regressed once and that nothing would now notice. It is explicitly a non-default lane — "run it before a
release, or when changing a guard" — and round 27 is the first time it has been run since the seam stage
moved 2,565 lines out of `MapperExtractor.cs`.

It reported **24 killed of 33, 0 survivors**. That headline is wrong in both directions, and the two errors
point opposite ways.

**Every figure here is measured.** Staleness is `sed`'s verdict on the real source at two revisions;
compile-validity is the compiler's verdict on the mutated tree. Nothing is inferred from reading.

---

## What the run actually measured

| class | count | what the verdict is worth |
|---|---:|---|
| genuinely behavioural, killed by the named guard | **17** | a real kill: the code compiled, and the guard the catalogue names is what failed |
| **compile-break** | **7** | worthless: the mutated source does not compile, so *every* test fails and the named guard's failure proves nothing |
| **stale** | **9** | worthless: the `sed` matched nothing, so no defect was planted at all |
| total | 33 | |

So the lane's honest score is **17 of 17 behavioural mutants killed, 0 survivors** — and **16 of its 33
catalogue entries currently measure nothing**. The suite is in better shape than 24/33 suggests; the
catalogue is in considerably worse shape.

The script already detects staleness and reports it loudly — that part of the instrument works, and it is
why this was findable at all. It has no notion of compile-validity, which is the gap this document is about.

---

## The 7 compile-breaks

A mutant whose replacement text does not compile is caught by the **compiler**, not by a test. The run prints
`killed by <guard>`, but the guard never ran. `M27` is the clearest case: it substitutes a call to
`NoOpSourceCoverage(`, an identifier that **has never existed in `src/` in the entire git history**
(`git log -S` returns nothing). It could never have been anything but a build break.

Measured at the round-27 base `1c300f3` (which builds clean unmutated, so the comparison is valid):

| id | compiles at `cb14993` | compiles at `1c300f3` | verdict |
|---|---|---|---|
| M02 | no | no | always broken |
| M07 | no | no | always broken |
| M09 | no | no | always broken |
| M10 | no | no | always broken |
| M13 | no | no | always broken |
| M23 | no | no | always broken |
| M25 | no | no | always broken |

**All seven predate round 27.** Not one was broken by the seam stage — these entries have never been capable
of measuring anything, at least as far back as the merge of rounds 25 and 26. Whatever kill counts this lane
has reported historically were overstated by seven.

## The 9 stale anchors

Split by cause, tested by applying each expression to the file at both revisions:

**Broken by round 27's seam stage (matched at `1c300f3`, no longer at `cb14993`) — 6:**
`M05`, `M18`, `M22`, `M24`, `M26`, `M27`.

**Already stale before round 27 (matched at neither) — 3:**
`M06`, `M11`, `M12`.

The seam stage moved the guarded code out of `MapperExtractor.cs`, `MapperExtractor.Members.cs` and
`MapperExtractor.Projection.cs` into the new phase files. In `MapperExtractor.Projection.cs`,
`skipNullSourceMembers`, `explicitOnly` and `ignoreObsolete` now survive **only as `<param>` doc comments** —
the code that consumed them lives elsewhere, so an anchor written against the old body matches nothing.

Three are near-mechanical re-anchors (the text is intact, the file changed):

| id | was | now |
|---|---|---|
| M18 | `MapperExtractor.Members.cs` | `MapperExtractor.Members.Phases.cs:656` |
| M27 | `MapperExtractor.cs` | `MapperExtractor.Phases.cs:2168` — **but see the compile-break above; re-anchoring alone would not fix it** |
| M06 | `MapperExtractor.cs` | `MapperExtractor.Phases.cs:1166`, **and the line has since been split in two**, so the one-line anchor needs rewriting either way |

The remaining six need the behaviour located and the expression re-derived. That is deliberately **not** done
here: an anchor written quickly against the wrong line plants a different defect than the catalogue claims,
which is strictly worse than a stale entry, because it reads as coverage.

---

## Why this went unnoticed

The same shape as every other instrument defect this round. The lane is non-default, so nothing ran it
between the refactor that broke the anchors and now; and for the three that were already stale before round
27, nothing ran it for considerably longer. A stale entry is silent by construction — the run still prints a
score, and the score still looks like a measurement.

This is the third instrument in this round to be found measuring less than it claimed, after
`scripts/extracted-reach.py` (14 of 32 methods) and the conformance gate's recorded pass count. In every case
the gate existed and simply had not been run since the change that broke it.

**A fourth, for the record: the checker written to produce this document had the bug too.** Its first version
re-parsed the catalogue with `awk`, which does not apply shell escaping, so the seven entries containing `\`
were fed to `sed` with a literal double backslash and one silently mis-reported. The second version `eval`s
the array through the shell, so escaping is identical to the battery's own parsing — and it then reproduced
the battery's 9-entry stale set exactly, which is what makes the numbers above independent rather than
self-confirming.

---

## Resolved — 33/33, and the rules that keep it there

Repaired and re-run 2026-08-27 at `d7e4744`, in a clean worktree, with the lane's own new green-baseline
gate passing first: **33 killed, 0 survived, 0 stale, 0 compile-break**, exit 0. Every catalogued defect is
caught by the guard the catalogue names for it.

The repair is in `0485ff7`; the rules that stop it rotting again are in `78a5776` as
`MutationMethodologyScanTests`, which runs in milliseconds against the ordinary build rather than the hour
this lane takes:

| rule | what it refuses |
|---|---|
| M1 | an entry matching nothing — the failure that hid nine times |
| M1b | breadth nobody chose: >1 site needs a `g` flag or a sed address |
| M2 | a constant condition, which cannot compile here and so credits a guard that never ran |
| M3 | a row without unique id, named guard and description |
| M4 | the lane losing its baseline check, per-mutant build, or its failure on either |
| M5 | the five leg-naming sites disagreeing |
| M6 | a ledger adjudication naming source that no longer exists |

The original worklist below is kept because it is the record of what was wrong.

## What to do

1. **Add a compile check to the lane.** Build once after applying each mutant; a mutant that does not compile
   is a catalogue defect and must be reported as such, not counted as killed. This is the structural fix —
   without it, a future entry can rot into a compile-break and still read green.
2. **Repair or retire the 7 compile-breaks.** `M27` names an identifier that never existed; the others need
   checking individually against the same standard.
3. **Re-anchor the 9 stale entries**, or drop any whose behaviour is genuinely gone — the script's own message
   already offers both options.
4. Re-run, and expect the score to *fall* before it rises. 17/17 is the current honest denominator; repairing
   the catalogue raises the denominator, which is the point.
