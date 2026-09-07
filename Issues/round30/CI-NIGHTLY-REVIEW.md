# End-of-round item: review the CI tiers before and after round 29 merges

Raised by the owner, 2026-09-07: *"some of the CLI pipelines are failing nightly tests, likely from changes
we already did… we will need review of 8 successful and 7 skipped checks."*

## First, two facts that reframe it

**1. The seven "skipped" checks are skipped by design, not broken.** Every one of them carries
`if: github.event_name == 'schedule' || github.event_name == 'workflow_dispatch'` in `.github/workflows/ci.yml`
— `mutation` (:176), `deep-test` (:317), `reproducible-build` (:576), `package-size` (:646),
`cross-platform` (:709), `preview-sdk-canary` (:767), `bench-wall-time-alert` (:866). On a push they skip
deliberately, because they are the expensive tier. A push run showing 8 green and 7 skipped is the intended
shape, not a partial run.

**2. Round 29 cannot be the cause of a current nightly failure, because it has never left this machine.**
Verified: the branch `feat/round29-hardware-mode` has **no upstream**, `master` is at `0c30156` (the round-28
merge), and **93 commits** sit locally and unpushed. `ci.yml:23` also records that GitHub runs `schedule`
**only on the default branch**, so the nightly tier has only ever seen master. Whatever is failing tonight
comes from round 28, from an earlier round, or from environment drift — a new SDK on the runner image, a
Roslyn bump, an ubuntu image change. It is not us.

That does not make the concern wrong. It relocates it: **the risk is what happens the first night after
round 29 merges**, when seven gates that have never seen this work all see it at once.

## What I cannot do here

Fetching the failing run's logs would mean using stored credentials against CI, which is out of bounds by
standing instruction. Diagnosing the *current* failures needs the run output pasted in, or a
`workflow_dispatch` the owner triggers.

## What round 29 will do to each nightly gate — predicted, with the reasoning

| gate | risk | why |
|---|---|---|
| **mutation** | **HIGH** | see below — both ratchets sit within one mutant of failing |
| **cross-platform** | **MEDIUM** | the BCL layout table is x64-verified only |
| **bench-wall-time-alert** | **MEDIUM** | the suite grew 57 → 59 benchmarks, and views add a category |
| **package-size** | **LOW** | the ceiling rule already protects this — see below |
| **deep-test** | LOW | the 766-case exhaustion ran green locally on the tip |
| **reproducible-build** | LOW | new files and doc comments, no build-input nondeterminism introduced |
| **preview-sdk-canary** | LOW | tracks a preview SDK; largely independent of this round |

### mutation — the real exposure, and it is structural

Both ratchets this round raised are pinned to **the measurement itself**, because invariant R2 requires the
floor to equal the measurement. That leaves margins thinner than one mutant:

| leg | break | measured | margin | one mutant is worth |
|---|---|---|---|---|
| generator | 87 | 87.04 % | **0.04 pp** | 1 / 409 = 0.24 pp |
| pipeline | 78 | 78.28 % | **0.28 pp** | 1 / 242 = 0.41 pp |

**A single mutant changing verdict fails either gate.** And the gate's own error text names the mechanism by
which that happens with no test having changed:

> check the Timeout bucket first: a Timeout counts as detected, so a re-classification moves the score with
> no test having changed

A CI runner has a different core count and different timing from this machine, so a mutant that timed out
here (counted as *killed*) may survive there, or the reverse. **The floor was measured on one machine and is
enforced on another.** That is the architectural question this item should settle — not "why did nightly go
red", but whether a floor pinned to a single-machine measurement is the right instrument when the gate runs
elsewhere. Options worth weighing: measure the floor in the CI environment and pin it there; keep R2 but
allow a stated tolerance below break for cross-machine variance; or accept the fragility and treat a
one-mutant red as a re-measure trigger rather than a regression.

### cross-platform — the concrete shape to check first

`BclLayoutFactsTests` asserts sizes **and alignments** for `Guid`, `DateTime`, `DateTimeOffset`, `TimeSpan`,
`TimeOnly`, `DateOnly` and `decimal`, and task 0.3c recorded plainly that **the new entries are x64-only
measurements**. If the cross-platform job includes an arm64 runner, those assertions are the first thing to
look at. They should hold — none of these types is expected to differ — but "should" is exactly what that
task refused to assert without measuring, and the same standard applies here.

### package-size — why it is low risk despite 563 B of headroom

The repo's own rule sets the ceiling to **the larger of the Windows and container measurements**, and Windows
packs measured 200–330 B larger at both prior comparisons (CRLF in the XML doc and nuspec; the DLLs are
byte-identical). The current 318 KB ceiling was set from the Windows number, so the ubuntu measurement should
land under it. Worth confirming, not worth worrying about.

Note also that the ceiling is an **observability ratchet, not a budget** (`gate-checks.ps1:606`). If it goes
red the answer is to account for the growth entry-by-entry and re-measure in the same commit, not to shrink
anything.

## The action

1. **Before merging:** run the nightly tier manually against the round-29 branch via `workflow_dispatch`
   (which the `if` conditions permit) so all seven gates see this work once, on the runner, before it becomes
   master's problem. That converts an overnight surprise into a controlled measurement.
2. **Settle the mutation-floor question above** — it is the only one of the seven whose failure mode is
   designed-in rather than incidental.
3. **Diagnose the current master failures separately**, from pasted output. They are not round 29's, and
   conflating them with round 29's merge risk would waste the diagnosis.
