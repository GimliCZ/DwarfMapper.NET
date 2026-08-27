<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Moving coverage to Codecov, and mutation to the Stryker Dashboard

Asked for as a swap: hosted reports in place of the local coverage gate. Coverage moved; **mutation did
not**, and the section below records why that was decided rather than deferred.

Everything here is measured or quoted from the tools' own documentation. Both services are free for public
repositories, and this one is public — verified by an anonymous `git ls-remote` against the origin — so cost
does not decide between them.

## What the swap actually changes, which is not what it sounds like

Moving a gate to a third party normally means loosening it. Here it is the reverse, and the numbers say so:

| | assemblies gated | generator line floor | new/changed lines |
|---|---:|---:|---|
| the inline gate in `ci.yml` (deleted, `f361402`) | **1** | **80 %** | not checked |
| `scripts/housekeeping.ps1` (local only) | 5 | 94.5 % | not checked |
| `codecov.yml` as prepared | **5** | **94.5 %** | **90 % patch target** |

CI has been gating fourteen points below the coverage the code actually has, on one assembly out of five,
and nothing has ever checked that *new* lines are covered. That last column is the part no floor can do: a
floor is satisfied by a large well-covered assembly absorbing a small uncovered addition, which is exactly
how coverage erodes without any floor going red.

Every component target in `codecov.yml` is the measured floor from `housekeeping.ps1`. They are two
statements of one fact, and `RatchetInvariantScanTests.R5` now fails the build when they disagree — so they
move together in one commit because nothing else is possible, not because someone remembers to.

## Mutation: NOT going to the dashboard — decided, not pending

The Stryker Dashboard (`dashboard.stryker-mutator.io`) hosts mutation reports, keeps history per module and
serves a badge. It was evaluated and **declined**. Two reasons, and the second is the one that would have
applied even if the first were fixed tomorrow.

**It cannot be logged into.** The GitHub OAuth round-trip completes — `authorize` returns, `callback` fires
with a code — and the dashboard's own session exchange then answers
`{"message":"invalid response encountered","error":"Unauthorized","statusCode":401}`. That is after GitHub
has done its part, so it is a fault in the service, not in this repository or its permissions. Nothing here
can fix it and nothing here waits on it.

**It would have been the weakest thing in the room anyway.** Its three features already exist here, in forms
that are gated rather than merely displayed:

| dashboard feature | what this repository already has |
|---|---|
| browsable per-mutant report | `mutation-report.html`, written by every local run of every leg |
| score history per leg | `equivalent-mutants.md` — score, scoreable population and measurement date |
| badge | six README badges rendered *from* that ledger and byte-compared by the doc suite |

A dashboard badge asserts whatever was last uploaded. A badge here is generated from the ledger, `R2`
cross-checks each `measuredRawScore` against its config's `break`, and `R3` pins the row sums — so it is a
claim the build fails over rather than a picture. And since CI cannot run the mutation legs, uploads could
only ever come from manual local runs: the resulting trend line would mean "whenever somebody remembered",
which is the class of half-measured signal this round exists to remove.

**What stays, deliberately.** `project-info` remains in all five configs (`name` =
`github.com/GimliCZ/DwarfMapper.NET`, `module` = `generator` / `runtime` / `doctooling` / `codefixes` /
`pipeline`). It is inert without a key, costs nothing, and means that if the service is ever repaired the
integration is one `--reporter dashboard` flag away rather than a change to five files. The reporter itself
is **not** configured, so no run depends on a service that cannot authenticate.

If someone wants the mutation data, it is already there: the HTML report from any local run, and the ledger
for the numbers.

### The consequence for CI mutation

`--with-baseline` — testing only the mutants changed since a published version — required the dashboard as
the place baselines are published, so declining the dashboard closes that route too. It was the one idea
that might have made an expensive leg affordable in the nightly, and it is recorded here as closed rather
than left as a lead someone re-discovers and re-investigates.

That leaves the sizing rule as the only lever, which is where it already was: one area per leg, each sized
to complete, as `AUDIT-mutation-scope.md` sets out.

## Cutover — what is done, and what is left

**Done already, so the list is shorter than it was:**

- `CODECOV_TOKEN` exists as an Actions secret on the GitHub repository. Nothing about it lives here; the
  workflow carries only the `${{ secrets.CODECOV_TOKEN }}` reference GitHub resolves at run time.
- Both uploads are wired and SHA-pinned — `codecov-action@0fb71748` (v5.5.5) for coverage and
  `test-results-action@0fa95f0e` (v1.2.1) for test analytics, the latter fed by `JunitXml.TestLogger`
  because `dotnet test` emits no JUnit and the step would otherwise upload nothing and report success.
- The inline coverage gate in `ci.yml` is **deleted** (`f361402`), because it was strictly dominated: one
  assembly at 80 % against five at their measured floors.

**That deletion did not leave coverage ungated**, which is the only reason it was safe to do before Codecov
had ever produced a verdict. `scripts/housekeeping.ps1` enforces the identical floors locally and in the
nightly `deep-test` job, and `RatchetInvariantScanTests.R5` fails the build if those floors and
`codecov.yml` ever disagree. What went away was the third, weakest copy — not the enforcement.

**Left to do, and step 2 is the one worth not skipping:**

1. Push the branch. Confirm five component statuses and a patch status appear, and that their percentages
   match what `housekeeping.ps1 -Coverage` prints locally. **If the two disagree, stop** — one of them is
   measuring something other than what it claims, and this round has five examples of what that costs.
2. Deliberately drop a covered line in a scratch branch and confirm the component status goes **red**. A gate
   that has never failed is a gate nobody has tested — the same reasoning as invariant M4, and the same
   check that was run against R5 before trusting it.
3. Make the Codecov statuses required in branch protection. Until then they are advisory: a status that
   nothing requires is a report, whatever its name.
4. Decide what `housekeeping.ps1 -Coverage` becomes once CI's verdict is authoritative. It stays useful as
   the local pre-push check, and R5 keeps it honest, so the case for deleting it is weak — but it should be
   a decision rather than an oversight.

## What is being accepted, stated once

Enforcement moves off-repo. A Codecov outage becomes a merge blocker, and the correctness of the gate now
depends on a third party's parser rather than seventy lines of Python anyone here can read. That is a real
trade for real gains — five assemblies instead of one, patch coverage, history, PR annotations — and it is
the maintainer's call, already made.

Two things reduce the blast radius, and both are already done: both actions are pinned to commit SHAs
(`codecov-action@0fb71748`, v5.5.5; `test-results-action@0fa95f0e`, v1.2.1) like the other 45 in these
workflows, and `fail_ci_if_error`
is `false`, so an unreachable Codecov fails to produce a verdict rather than manufacturing a red one. An
absent status is visible in branch protection; a red build blamed on the network teaches people to re-run
until it passes, which is worse than no gate at all.
