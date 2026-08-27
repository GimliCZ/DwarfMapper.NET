<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Moving coverage to Codecov, and mutation to the Stryker Dashboard

Asked for as a swap: hosted reports in place of the local coverage gate. This is what is prepared, what the
cutover requires, and the one place the swap **cannot** go all the way.

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

## Where the swap stops: mutation cannot be gated by a dashboard

The Stryker Dashboard (`dashboard.stryker-mutator.io`) hosts mutation reports, keeps history per module, and
serves a badge. It publishes **no status check**, so nothing about it can fail a build. Mutation gating
happens one place only — the `break` threshold inside the run — and this repository's CI cannot run the
mutation legs at all.

So the honest end state is asymmetric, and worth stating plainly rather than discovering later:

- **coverage** — gated by Codecov's status checks, in CI, on every push.
- **mutation** — gated by `break` in each config, locally via `housekeeping.ps1`; the dashboard adds history,
  a badge, and a browsable report, and gates nothing.

`project-info` is already in all five configs (`name` = `github.com/GimliCZ/DwarfMapper.NET`, `module` =
`generator` / `runtime` / `doctooling` / `codefixes` / `pipeline`). Five modules under one project means each
leg keeps its own history instead of five runs overwriting one score. The `dashboard` reporter is
deliberately **not** in the configs: it fails when no API key is present, and a config that cannot run
without a secret is a config that cannot run.

Publishing, once the key exists:

```bash
dotnet stryker --config-file stryker-config.pipeline.json \
               --reporter dashboard --version "$(git rev-parse --abbrev-ref HEAD)"
# STRYKER_DASHBOARD_API_KEY in the environment
```

`--version` is supplied at the command line rather than written into the file precisely because it is the
branch, and a branch name committed to a config is wrong the moment anyone branches.

## The one feature that might reopen CI mutation

`--with-baseline` compares against a previously published dashboard version and **tests only the mutants
that changed**. That is aimed squarely at the constraint that killed the 13-file leg: 1,353 mutants and 158
minutes without finishing, against a nightly budget that cannot exceed six hours.

It is worth an experiment, not an assumption. The saving depends entirely on how many mutants a typical
change touches, and a refactor that moves a file invalidates its whole module. Measure it on one leg before
believing it, and record the number the way every other figure here is recorded.

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
