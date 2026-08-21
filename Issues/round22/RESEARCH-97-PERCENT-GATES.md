<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Research: the 97% program — realistic gates and pressures, per dimension

Status: research, 2026-08-21. Read-only — no build, test, or Stryker run was launched (a mutation
measurement was running on this machine). Sources: the r21 worktree at tip (`fa98664`), the ledgers
(`T3-mutation-survivors.md`, `H7-timeout-dissection.md`, `E3-E1-report.md`), `Issues/round20/TASKS.md`,
`scripts/housekeeping.ps1`, `.github/workflows/ci.yml`, and the cited external evidence. Numbers quoted
are the ledgers' own; where a count is approximate it says so.

**The directive** (verbatim): *"Could we there ratchet down slowly to 97% coverage from all points of
testing views? Research realistic gates and pressures we can do on library to test it through."*

**The one-paragraph answer.** 97% is realistic for some dimensions of this repo and structurally
impossible for others, and the difference is decidable today from the ledgers. It is realistic for the
DocTooling and runtime mutation legs (every undetected mutant is already triaged and all but two are
killable), plausible for line coverage once the denominator stops charging compile-time-only code, and
**impossible as a raw number** for the generator mutation leg (≥7.5% of its current denominator is
proven-equivalent or dead) and for the surface matrix (22% of rows are excused for reasons the C#
grammar imposes). Industry evidence says no one gates at 97% mutation score — Google abandoned
project-score gating entirely, Meta's learned mutants survive >50% of a rigorous suite, and Stryker's
own "excellent" threshold is 80 — so where this repo *can* reach 97, it is because its legs are tiny
(118–284 mutants) and fully triaged, a boutique property, not an industry norm. The program below
therefore has two halves: (1) an **adjudicated-denominator mechanism** (category + obligation, house
rule compliant) that makes 97% *meaningful* where it is reachable, and (2) a **mandatory-raise ratchet
invariant** that supplies the "slowly", enforced by a SelfValidation scan rather than by discipline.

---

## 1. The dimension table

"Honest 97%-equivalent" = what the 97 becomes once the denominator is adjudicated; where the raw number
is unreachable, the named alternative. Steps are floors/targets to be **set only from a re-measurement
in the same commit** (house rule; the audit already rejected "measured − 4").

| Dimension | Current (measured) | Structural ceiling | Honest 97%-equivalent | Exclusion mechanism (category + obligation) | R22 step | R23 step | R24 step |
|---|---|---|---|---|---|---|---|
| Line, DwarfMapper (runtime) | floor **77.7** | unknown until the 0%-by-design classes are classified; likely ≥95 after exclusion | 97 of the *adjudicated* denominator | `[ExcludeFromCodeCoverage(Justification=…)]` per compile-time-only type; scan obligates non-empty justification naming a sanctioned category + symbol present in the surface catalog | classify every 0%-covered class; apply exclusions; re-measure; floor → new measured | kill-list tests (mutation holes #1–#13 raise lines too); floor → measured | floor → measured; 97 if reached, else record the residue with reasons |
| Line, Generator | floor **93.8** | unknown; 11 NoCoverage mutants mark real unreached branches | 97 raw is plausible — smallest gap of the five | same attribute mechanism if any by-design residue exists (none known yet) | corpus tests from the kill list (partial-file fixture, mixed ref-ctor) raise it; floor → measured | R22-01 differential corpus converts NoCoverage paths to covered; floor → measured | 97 target |
| Line, DocTooling | floor **90.7** | ~97+ reachable — all uncovered paths are triaged real holes (families B–D) | 97 raw | none needed on current evidence | land the 5 enumerated test families; floor → measured | floor → measured (expect ≥95) | 97 target |
| Line, CodeFixes | floor **92.4** | unknown | 97 raw plausibly | same attribute mechanism | measure the uncovered residue, classify | targeted tests; floor → measured | 97 target |
| Line, Testing (shipped pkg) | floor **83.2** | unknown — worth priority: this assembly ships | 97 of adjudicated | same attribute mechanism | classify + test the residue (shipped surface!) | floor → measured | 97 target |
| Branch (all, informational) | 76.1 / 87.4 / 84.2 / 68.4 / 82.2; **wobbles** (Testing 81.9–82.2 across runs) | n/a — measurement is nondeterministic | **do not gate** (H7 rule: no gate on a nondeterministic oracle); mutation legs already pressure branches deterministically | n/a | keep informational; optionally a 3-run variance probe to locate the wobble | gate at integer-truncated floor **only if** proven stable | — |
| Mutation, generator leg | **71.64** (144/201; break 71) | **~89.8 survivor-side** without adjudication (15 proven-equivalent survivors + 8 probably-equivalent + 11 dead/unreachable NoCoverage cap it); ~100 of adjudicated | 97 **only via the adjudication ledger** — raw 97 is impossible and should be said so | `// Stryker disable once <Mutator> : <reason citing ledger anchor>` → `Ignored`, out of the denominator (Stryker semantics, §2.2); scan pins count exactly, every reason non-empty + ledger-anchored case proof | adjudicate the 15 proven; kill top-3 families (~15 mutants); re-measure; break → measured (~80s) | kill remaining triaged holes; maintainer rules on the dead-code questions; break → measured (~high 80s of adjudicated) | 97-of-adjudicated if the dead-code deletions land |
| Mutation, DocTooling leg | **67.96** (193/284; break 67) | ~99.6 of adjudicated (1 proven equivalent) | **97 raw is realistic** — the honest showcase leg | 1 adjudication (`DocSnippetInjector.cs:83`); same comment+scan mechanism | triage the 43 exposed survivors; land the 5 enumerated families (~67 mutants); break → measured | remainder of the 43; break → measured | 97 target (≥275/283) |
| Mutation, runtime leg | **61.02** (72/118; break 61) | ~99 of adjudicated (1 proven equivalent; −5 mutants when the already-ruled `ResetForTests` deletion is re-measured) | **97 raw is realistic** — all 13 hole families are enumerated and cheap | 1 adjudication (`DwarfRefContext` L77 Equality); same mechanism | T7 (top-3) + null-guard family + message-pin convention; re-measure (denominator shrinks with `ResetForTests` gone); break → measured (~80s) | holes #5–#13; break → measured | 97 target (~109/112) |
| Surface matrix, excused rows | 866/866 green; **191 excused** (NoSuchSite 116 + Unaskable 44 + UnhonouredButLoud 14 + StructurallyExcused 13 + Divergent 4) | ≤26 excused (97%) is **grammatically impossible** — NoSuchSite is "wholly structural since A11" (no C# declaration site exists) | reframe: **100% of excuse categories carry a discharged obligation** — an excused row is a category with a live re-measured proof, never a hatch | already partly built (populations pinned exactly, ≤10-band closed); missing obligations are B3 (option `NotApplicable` never re-measured), B6 (per-cause drift), B7 (CrossAssembly placement-blind), B32 (site narrowings uncounted) | close B3 + B6 | close B7 + B32; every category obligation-complete | maintain at 100%; any new excuse category must ship with its obligation |
| NoCoverage mutants (all legs) | gen 11 / doc 37 / runtime 7 | doc + runtime → 0 (all triaged real); gen → the dead-code questions | 0, with dead-code residue adjudicated or deleted | folded into the mutation rows | as above | as above | 0 / 0 / adjudicated |
| Diagnostics: NegativeCases pins | unknown fraction; `PredatesThisProject` hatch open (B11) | 100% of post-project ids | 100% (raise-only fraction) | `PredatesThisProject` becomes a counted shrink-only population, not a hatch | count it; close B11 | shrink | shrink |
| Unannounced diagnostics | **76** (`PredatesTheChangelog`) | 0 | 0 before first tag | none — this is D-e, blocked on the maintainer | — (human) | — | 0 at release |
| XML-doc coverage (shipped API) | **100% by construction** — `GenerateDocumentationFile` + warnings-as-errors makes CS1591 a build break on `DwarfMapper` + `DwarfMapper.Testing` | at ceiling | already there | n/a | none — record it as a dimension at ceiling | — | — |

Sanity notes on the arithmetic (all from `T3-mutation-survivors.md` / `E3-E1-report.md`):

- **Generator**: 201 scoreable = 144 K + 46 S + 11 NC, Timeout 0 (authoritative run `12-10-18`).
  Survivors: 15 proven equivalent (13 BlittableProof `LayoutIdentical`/`IsPrimitive` + ConstructorSelector
  L58, L243 — all with case-analysis proofs, marked *do not attempt*), 8 probably-equivalent
  (`InstanceFields` L85/L86 position comparator), ~23 triaged real. NoCoverage: all 11 are
  unreachable-branch / defensive-never-taken / dead-code-question — none is a plain corpus hole.
  Adjudicating the 15 gives denominator 186: today 144/186 = 77.4%; killing every triaged real survivor
  ≈ 167/186 = **89.8%**. 97% of 186 needs 181 detected — unreachable without also adjudicating the
  8 + 11, i.e. **the mechanism is the target**, and the ledger must own it, not the score.
- **DocTooling**: 284 = 193 K + 54 S + 37 NC, Timeout 0 (post-H7 re-measure). Exactly **1** proven
  equivalent. The 43 guard-exposed survivors are corpus holes *explicitly not judged equivalent*; the
  37 NC are families B–D + ParseId, all real, with named one-test-per-family kills. 97% = 275/283.
- **Runtime**: 118 = 71 K + 1 T (clock noise, detected) + 39 S + 7 NC (2026-08-17). **1** proven
  equivalent (`DwarfRefContext` L77 Equality). 13 hole families, all enumerated with the missing case
  named. `ResetForTests` (5 × NC) was deleted by ruling D-b — the next re-measure loses those 5 from
  the denominator honestly. 97% ≈ 109/112 post-adjudication. *Caveat:* the prompt-era shorthand
  "~13 equivalent/noise-adjacent" conflates the 7 `Timeout → Survived` re-classifications (real
  survivors, already honest in the 61.02) with equivalence; the ledger proves exactly one equivalent.

---

## 2. Per-dimension findings and the industry evidence

### 2.1 Is 97% mutation score attained anywhere? No — and the biggest practitioners stopped chasing a score at all

- **Google** does not gate on (or even routinely compute) a project mutation score. Its deployed system
  is *per-diff*: mutants only in changed, covered, non-"arid" lines, surfaced as code-review findings —
  72,425 diffs analyzed, 150,854 findings, 11,049 with developer feedback; irrelevant-but-killable
  mutants are simply ignored on developer judgement ([State of Mutation Testing at
  Google](https://research.google/pubs/state-of-mutation-testing-at-google/), Petrović & Ivanković,
  ICSE-SEIP 2018; extended in [Practical Mutation Testing at
  Scale](https://www.semanticscholar.org/paper/Practical-Mutation-Testing-at-Scale:-A-view-from-Petrovi%C4%87-Ivankovic/c04b8dddca00070ae4abb1864974f4c3d05274f2)).
  The lesson for this repo is inverted but real: Google *cannot* adjudicate a whole-project denominator
  at 2 GLOC, so it abandoned the score; this repo's legs are 118–284 mutants and **fully triaged**, so
  an adjudicated score is actually meaningful here. That is rare and worth stating in the gate comments.
- **Meta**: semi-automatically learned mutants — >50% survived "Facebook's rigorous test suite of unit,
  integration, and system tests" ([Beller et al., ICSE-SEIP 2021](https://arxiv.org/abs/2010.13464)).
  Elite suites sit nowhere near 97 on realistic mutant classes.
- **Equivalent-mutant adjudication cost**: manual classification averages **~15 minutes per mutant with
  only ~80% accuracy** ([Schuler & Zeller, *Covering and Uncovering Equivalent Mutants*, STVR
  2013](https://onlinelibrary.wiley.com/doi/abs/10.1002/stvr.1473)). The house ledger discipline
  (written case-analysis proof per mutant, T3/H7 format) is *stronger* than the studied practice and
  therefore costlier per mutant — which is exactly why the adjudicated population must be counted,
  pinned, and shrink-preferred, never casual.
- **Stryker's own thresholds**: `high` (green) defaults to **80**; `break` defaults to 0
  ([configuration docs](https://stryker-mutator.io/docs/stryker-net/configuration/)). The tool's
  definition of excellent is 80, not 97.
- **The existence proof at the extreme**: SQLite maintains 100% MC/DC via the TH3 harness — at a
  **590:1 test-to-source ratio**, and its authors state maintaining it "is laborious and time-consuming
  … probably not cost effective for a typical application"
  ([sqlite.org/testing.html](https://www.sqlite.org/testing.html)). That is what the last percent costs
  at the limit.

**Adjudication workflow — the recommended shape.** Stryker.NET supports source-level
`// Stryker disable [once|all] [<mutator list>] : <reason>` comments; excluded mutants report as
**`Ignored`**, and the mutation-testing-elements score is `detected / valid` where valid excludes
Ignored ([ignore mutations](https://stryker-mutator.io/docs/stryker-net/ignore-mutations/); [mutant
states and metrics](https://stryker-mutator.io/docs/mutation-testing-elements/mutant-states-and-metrics/)).
So the mechanism that fits the house zero-allowlist rule is all three layers at once, and it already
has an in-repo precedent (H7 phase 2 landed `// Stryker disable all : …` on the `DocSnippetInjector`
progress guard, and its 7 mutants report `Ignored` with the reason string in the report):

1. **At the code**: `// Stryker disable once <Mutator> : equivalent — proof at Issues/ledgers/<anchor>`
   on the exact line. Visible where the code is edited; removed automatically if the code is rewritten.
2. **In the ledger**: the case-analysis proof (the T3 bar: "original and mutant agree on every
   reachable input"), one entry per adjudicated mutant.
3. **In SelfValidation**: a scan counting `Stryker disable` occurrences across `src/`, pinned
   **exactly** (the ≤10-band lesson: small populations get exact pins), each required to carry a
   non-empty reason containing a ledger anchor. Raising the count = a new proof in the same commit;
   this is a category ("adjudicated equivalent") with an obligation (the proof + the pin), not an
   allowlist.

Config-level `ignore-mutations` is the wrong tool here except for whole-mutator policy: it is central,
invisible at the code, and reason-free — an allowlist in the house sense.

### 2.2 Line coverage: the honest denominator, then 97

Google's measured distribution: **median project coverage 78%, 75th percentile 85%, 90th percentile
90%** ([Ivanković et al., *Code Coverage at Google*, ESEC/FSE
2019](https://research.google/pubs/code-coverage-at-google/)). 97% line coverage is beyond Google's
90th percentile — it is attainable only for small codebases with cleaned denominators, which this is.

The runtime assembly's 77.7 is depressed by attribute classes that are compile-time-only **by design**
(consumed by the generator, never executed). Two candidate mechanisms:

- **(a) `[ExcludeFromCodeCoverage(Justification = "…")]` + a SelfValidation scan** — the attribute has
  carried a `Justification` property since .NET 5, and coverlet honors the attribute. The scan
  obligates: justification non-empty, naming a category from a fixed sanctioned set (first category:
  *compile-time-only attribute, consumed by the generator*), and — the non-vacuity teeth — the excluded
  symbol must appear in the generator-test corpus / surface catalog, so an exclusion cannot outlive the
  code's reason for existing. Count pinned exactly.
- **(b) Leave the denominator dirty and freeze the floor at the depressed value.** Rejected: it makes
  the floor a lie in both directions — real regressions in executed code hide inside the slack, and
  the number can never say anything about 97.

(a) is the house-compliant choice: it is the coverage twin of the `Stryker disable` mechanism —
category + obligation + exact pin — where a raw floor-freeze is an unaccounted excuse. The R22 step is
a *measurement*, not a target: enumerate every 0%-covered class per assembly from the existing
ReportGenerator summary, classify each (by-design vs hole), apply exclusions, re-measure, and set the
floors to the new measured values in that commit. Only then does a 97 target mean anything.

One deliberate side effect to exploit: the mutation kill lists and the coverage floors co-move. Family
C of the DocTooling triage (`ExampleCatalogue.Build` refusals, 14 mutants) and the runtime null-guard
family are *both* NoCoverage — killing them adds covered lines. Schedule mutation kills first in each
round, then read the coverage floor off the same run.

### 2.3 Branch coverage: do not gate a wobbling instrument

Measured wobble: Testing branch 81.9–82.2 across runs with no code change. Coverlet's branch metric
counts compiler-generated branches it must heuristically filter — async/iterator state-machine
branches, singleton-iterator `MoveNext()` branches, try/catch inside state machines — and its own
changelog is a history of trimming these ([e.g. PR #549 "Fix and simplify async
coverage"](https://github.com/coverlet-coverage/coverlet/pull/549), [issue #810 (yield-return state
machine)](https://github.com/coverlet-coverage/coverlet/issues/810)). On top of the filter gaps,
once-per-process branches (static-init guards, cached-delegate null checks) land in different tests
under xUnit parallelism, so run order moves the count. A floor on this number is a wall-clock-shaped
oracle — the exact genre H7 just banned (`Timeout` kills). Ruling proposal: **branch stays
informational until a 3-run variance probe shows a stable integer-truncated value per assembly**; the
deterministic pressure on branches is already the mutation legs (an untested branch is a Survived or
NoCoverage mutant, deterministically) plus R22-02's metamorphic relations (§5).

### 2.4 The surface matrix: 97% non-excused is the wrong question

191 of 866 rows pass by category. 97% non-excused would demand ≤26 — but 116 are `NoSuchSite` (the C#
grammar offers no declaration site) and 44 are `Unaskable`: the language, not the tests, sets this
ceiling. The round-20 reviewer's sentence stands as the dimension's definition: *"866/866 honestly
judges what it judges, but roughly 200 rows pass by being excused rather than by being right."* The
honest 97-equivalent is: **every excuse category carries a discharged, re-measured obligation** — the
matrix's version of "no allowlists". Four obligations are still missing, all already filed: B3 (the
option matrix's `NotApplicable` is accepted on a non-blank reason alone, never re-measured — the only
place left where an excuse cannot go stale-red), B6 (the NoSuchSite ratchet gates the total, so
offsetting per-cause drift passes), B7 (CrossAssembly rows can satisfy the category without crossing an
assembly), B32 (site narrowings are validated for form and counted by nothing). Closing B3+B6 (r22) and
B7+B32 (r23) takes the dimension to its real ceiling: 100% of excused rows obligation-backed, exact
per-cause pins. A percentage target adds nothing after that.

---

## 3. The ratchet rule — "slowly", as a testable invariant

What "ratcheting CI gates" looks like in practice: coverage-delta/patch gates rather than absolute
bars; Stryker's `since` (mutate only changed code) and experimental `with-baseline` (store a report,
re-test only what changed) ([Stryker.NET
configuration](https://stryker-mutator.io/docs/stryker-net/configuration/)); benchmark baselines with
alert thresholds ([github-action-benchmark](https://github.com/benchmark-action/github-action-benchmark),
alert at e.g. 150% of previous). The common shape is *baseline + permitted delta*. This repo already has
the stricter half (floor = measured exactly, moved only with re-measurement in the same commit); what it
lacks is the **forcing half** — nothing today *requires* a floor to follow an improvement, so measured
value and floor can drift apart and the slack becomes an unaccounted allowance. The proposal:

**The ratchet invariant (one rule, four clauses), enforceable by a SelfValidation scan:**

- **R1 — floors are measurements.** Every gated floor/break equals the measured value truncated to the
  gate's stated precision, annotated `{value, run-id/date, commit}` in the gate's own comment. A floor
  may change only in a commit containing the re-measurement. *(Existing house rule, now written as an
  invariant.)*
- **R2 — the mandatory raise (the "slowly" engine).** The deep run **fails** when
  `measured ≥ floor + q` for its dimension, with quantum `q` = 1.0 pp for line coverage, one mutant's
  worth of score for a mutation leg, 1 for any counted population — and the failure message says *raise
  the floor to the measured value in this commit*. Improvement becomes irreversible by construction:
  nobody can bank slack, and every round's kill-list work mechanically drags the floor behind it. This
  is the inverse of `break: measured − 4` (already rejected by the audit) — the floor chases the
  measurement instead of trailing it by policy.
- **R3 — adjudications are counted categories.** Every `// Stryker disable` in `src/` and every
  `[ExcludeFromCodeCoverage]` carries a non-empty reason naming a sanctioned category and a ledger
  anchor; a scan pins each count exactly; raising a count requires the proof in the same commit.
  *(Extends the ARCH-06 registered-pattern idiom.)*
- **R4 — no gate on a nondeterministic oracle.** A dimension whose measurement moves without a code
  change (branch %, wall-clock) may be reported but not gated. *(The H7 Timeout ruling, generalized.)*

**Enforcement shape**: one SelfValidation test parsing `scripts/housekeeping.ps1`'s `$coverageFloors`,
the three `stryker-config*.json` `break` values, and a small machine-readable measurement ledger
(dimension → value/run/commit). The repo already parses its own configs in guards
(`Assert-MutantsWereTested`, and H4's pending `break ≤ low` sanity check — fold H4 into this scan).
R2's runtime half lives in the deep-tier script: after each leg, compare the report's measured score
against the config's break and fail on `≥ break + q`.

**Concrete round targets** (all "→ measured" per R1; the figures are the expected neighborhoods from
the §1 arithmetic, not values to type in):

| Dimension | R22 (expected) | R23 | R24 |
|---|---|---|---|
| Mutation runtime | adjudicate 1, kill T7 + guards + messages → break ~80s | holes #5–#13 → ~90s | **97** (~109/112) |
| Mutation DocTooling | 5 families + triage the 43 → break ~80s | remainder → ~90 | **97** (≥275/283) |
| Mutation generator | adjudicate 15, kill top-3 families → ~80 of adjudicated | remaining triaged holes + dead-code rulings → high 80s | 97-of-adjudicated **only if** the dead-code deletions land; otherwise record ~90 as the ceiling and say why |
| Line floors ×5 | exclusion pass + re-measure (floors jump for runtime by denominator honesty alone) | kill-list side effects → measured | 97 where §1 marks it reachable |
| Matrix obligations | B3 + B6 | B7 + B32 → 100% obligation-backed | hold |

---

## 4. The pressures survey — decision table

Already in place and *not* re-proposed (the RFC audit caught R22-04 doing exactly that): surface matrix,
3 mutation legs with measured breaks, coverage floors (`housekeeping -Coverage`), ILVerify
(`-ILVerify`), **AOT/trim gate in CI on ubuntu + windows with a behavioural gate over the published
native binary** plus the local AotBench (`-SkipAot` to skip), ApiCompat with one suppressed intentional
rc-phase break, PublicAPI.Shipped/Unshipped files (277 + 42 symbols), analyzers
(AnalysisMode=All + Meziantou + BannedApiAnalyzers, warnings-as-errors), conformance gate (fail-closed),
roslyn-forward-compat leg, CodeQL, SBOM (CycloneDX + SLSA attestation), SHA-pinned actions, pinned SDK,
deep fuzz tier (`DWARF_DEEP`, 21 registered populations), nightly runtime mutation, R22-03
incremental-caching contract (landed, `cedad48`).

| Pressure | Verdict | Reason (one line) | Tool / gate shape | Cost estimate |
|---|---|---|---|---|
| **Allocation regression gate** | **adopt-r22** (with T8) | BenchmarkDotNet `MemoryDiagnoser` allocated-bytes is deterministic — exact-pin fits the house rule; the repo's own 3.29× allocation lead is recorded platform-independent, unlike throughput | deep tier: ShortRun + assert allocated bytes == pinned per benchmark (raise/lower only with re-measure) | minutes (rides T8's planned smoke leg — do not build twice) |
| **Wall-time regression gate** | **adopt-later (r23), alert-only** | wall-clock is R4-nondeterministic on shared runners; industry practice is baseline + generous threshold, not exact | [github-action-benchmark](https://github.com/benchmark-action/github-action-benchmark) BenchmarkDotNet adapter, `alert-threshold` ~150%, comment-only (never `fail-on-alert` per-PR); Dict throughput is known platform-dependent — Linux CI numbers must never be quoted as the Windows figures | ~5–10 min nightly |
| **Generator compile-time cost gate** (time-to-first-emit; per-1000-mappers scaling) | **adopt-later (r23)** | real consumer-facing cost with no gate today; R22-00's type-graph renderer makes a 1000-mapper corpus generatable on demand | GeneratorDriver-based bench in the benchmark project; gate = ratio vs pinned baseline (e.g. fail > 1.5×), deep tier; also assert the incremental re-run is cached (R22-03 already pins per-step) | unmeasured — measure before landing (round-21 rule); est. low minutes |
| **Package/binary size ratchet** | **adopt-later (r23)** | pre-1.0 and ApiCompat/PublicAPI already guard surface growth; size is a cheap secondary signal | `dotnet pack` in deep tier; ceiling = measured KB (truncated), raise-only-with-re-measure | seconds |
| **AOT/trim verification** | **present — extend only** | `aot-trim-gate` already publishes ubuntu+windows, asserts NativeAOT-ness, and executes the behavioural gate; AotBench exists locally | possible r24 extension: `osx-arm64` rid leg | 0 now |
| **Cross-platform test legs** | **adopt-later (r23), nightly** | correctness is de-facto dual-platform today (local dev = Windows, CI = ubuntu, AOT gate = both); a macOS leg buys little for a Roslyn generator but is cheap insurance | nightly `windows-latest` + `macos-latest` full-suite legs (not per-push; runner cost + wall-clock) | ~10 min each, nightly |
| **.NET preview-SDK canary** | **adopt-later (r23)** | .NET 11 previews exist; a generator's worst consumer-facing failure is "new SDK refuses to load it" | clone of `roslyn-forward-compat`'s shape: non-blocking (`continue-on-error: true`) until first seen green — that leg's own comment states the discipline | ~5 min nightly |
| **Roslyn-version matrix extension** | **adopt-r22 (one flag)** | the forward-compat leg exists but is still `continue-on-error: true` — by its own comment "a gate that cannot fail is decoration"; flipping it once seen green is an *obligation already written down* | flip `continue-on-error` after first green run; floor leg (MSCA 5.0.0) is already what build-test pins | 0 |
| **Concurrency pressure on registry statics** | **present — reject Coyote** | torture tests (240 rounds, ×4 deep), E4's parallelism-with-audit, and 49 registry mutants already pressure this; Coyote's systematic exploration needs task-based code + binary rewriting — poor fit for lock-free `ConcurrentDictionary` statics, high adoption cost | keep: torture × `DWARF_DEEP`; the E2 measurement says the cost is trivial (~1 s/pass) | 0 |
| **Documentation coverage** | **present — at ceiling** | CS1591-as-error + `GenerateDocumentationFile` on both shipped assemblies = 100% public-symbol docs by build break | none | 0 |
| **Dependency/supply-chain** | **adopt-r22** | SBOM+attestation exist, but no vulnerability *gate* and no lock files; both are near-free | `NuGetAuditMode=all` (audit transitives; NU1901–NU1904 become errors under warnings-as-errors) + `RestorePackagesWithLockFile` + `--locked-mode` restore in CI ([lock-file docs](https://devblogs.microsoft.com/dotnet/enable-repeatable-package-restores-using-a-lock-file/)) | ~0; one-time lock-file churn |
| **Reproducible-build verification** | **adopt-later (r23)** | fits the CRA-defensive posture; SBOM without bit-reproducibility is a claim without a check | `ContinuousIntegrationBuild=true` + build-twice-compare-hashes leg (or `dotnet-validate`); nightly | ~2 min nightly |
| **API docs drift** | **present — fix B23 instead** | `docs/generated` is byte-compared already (`GeneratedDocsAreCurrentTests`); the known defect is the renderer's whitespace-eating (`LoadOptions.None`), which no new gate can see because the comparison is self-consistent | fix B23 (`LoadOptions.PreserveWhitespace`) with the full regenerated diff reviewed | small, needs review |
| **Test-hang backstop** | **adopt-r22** | H7 §4's own recommendation, still unlanded: bounds *every* future hang with the offender named; not a kill mechanism (R4-clean — it names, never scores) | `dotnet test --blame-hang --blame-hang-timeout 5m` on plain CI test steps only (never the Stryker legs — the per-mutant ceiling owns that role) | 0 |
| **Mutation-leg static-mutant attribution** | **adopt-r22 (research first)** | the generator leg's whole cost problem: 85/201 mutants are `static` → each re-runs 5,798 tests (~493k executions, 14:39 of 21:02); recovering per-test attribution is the *enabling* work for both the budget (§5) and any faster iteration on that leg's 97-program | investigate why Stryker sees the generator invocation as static state (T3 open item 3; likely the harness's static caches / module-init path); no gate — a wall-clock reduction | research session; potential −14 min nightly |

Rejected outright: per-PR mutation runs (Google's per-diff model needs infrastructure this repo doesn't
have and the nightly cadence already fits the 118–284-mutant scale); gating branch % (§2.3); per-PR
wall-time gates (R4); a 97% *matrix-row* target (§2.4).

---

## 5. Composition with the round-22 backbone (R22-00→02) and the budget

The compiler-testing arc and the 97-program are the same campaign viewed from two sides — the arc
*generates* the pressure, the gates *bank* it:

- **R22-01 (differential oracle) is generator-leg coverage and mutation pressure.** Generated type
  graphs reach extraction/emission branches no hand-built fixture visits — precisely where the 11
  generator NoCoverage mutants and the 93.8-line residue live. Every oracle disagreement shrinks (via
  CsCheck) to a pinned corpus row, i.e. a deterministic future kill. Its leg-1 must-compile invariant is
  the `EmittedInvalidCode` ratchet (now pinned at 0, T6) generalized from 866 cells to the generated
  space — note the audit's sequencing constraint is now satisfied: T6 closed B27/B33, so leg 1 no
  longer needs a divergence allowance on day one.
- **R22-02 (metamorphic) is the honest branch-coverage pressure.** MR-1/MR-2/MR-3 vary exactly the axes
  (member order, unmapped members, type kind) whose branches coverlet's wobbling metric cannot gate;
  a violated relation is a deterministic red, which is what §2.3 gives up by not gating branch %.
- **R22-00's kind/shape enums are ratchet-scanned against the matrix dimensions** (the RFC's own
  companion test) — the generator-bias blind spot becomes a declared, counted population, same family
  as the matrix obligations in §2.4.
- **Kill-lists and the corpus feed each other**: T3's partial-file `BlittableProof` fixture and
  mixed-`ref` constructor are exactly the shapes R22-00's validity rules must be able to express; write
  them once as corpus rows, and both the mutation kill and the differential coverage land together.

**The budget collision, with the T5-style data.** The deep tier holds 44 min; mutation alone is
**39:24** (runtime 12:25 + generator 21:02 [19:41 re-measured] + DocTooling 5:57 [5:04 re-measured]).
R22-01 at 1,000 iterations is generator-runs-plus-Roslyn-emits — minutes at minimum, and **unmeasured;
per the round-21 rule it cannot land without a measured wall-clock**. The arbitration the T3 ledger's
own numbers support:

1. **Split mutation across alternating nights** (runtime + DocTooling ≈ 18:22; generator ≈ 19:41 —
   both fit alone), and give the differential leg the lighter night. The legs are independent runs with
   independent breaks; nothing requires all three nightly. R2's mandatory-raise check runs whichever
   night measures the leg.
2. **Fund the static-mutant attribution research** (last row of §4): −14 min from the generator leg
   would fit all three legs plus the differential leg on one night and dissolve the rotation.
3. Iteration counts live behind **`DWARF_DEEP`** (T4's knob, already landed with a self-test): fast
   tier runs a smoke count (tens), deep tier the full 1,000, the 10,000-iteration variant is the knob's
   deep value — never a copied test (the audit's correction to the RFC).
4. What stays ruled out regardless: narrowing `mutate`, filtering test sets, excluding test projects
   (maintainer ruling, binding, restated in `ci.yml` itself).

---

## 6. Open questions for the maintainer

1. **The adjudication instrument.** Approve `// Stryker disable once … : <reason + ledger anchor>`
   comments in `src/` (scanned, exact-pinned, Ignored-out of the denominator) as the sanctioned
   equivalent-mutant mechanism? Precedent exists (H7's progress guard); the alternative
   (config `ignore-mutations`) is an allowlist by the house's own definition.
2. **The dead-code questions the triage raised** — each is a denominator decision, not a test task:
   `BlittableProof` L29–L30's apparently unreachable `true` return; `ConstructorSelector` L281/L285
   (provably dead per the ledger); the L88 `useObjectInitializerOnly` flag (possibly a redundant
   out-parameter). Deletion shrinks the denominator honestly; adjudication keeps the code and pins the
   proof. Generator-leg 97 depends materially on these rulings.
3. **The sanctioned exclusion categories for line coverage** — the first is *compile-time-only
   attribute, consumed by the generator*; is anything else (e.g. `Debug.Fail`-style unreachable
   defensive arms) admitted, or is that a hole by definition?
4. **Branch coverage**: accept "informational until proven stable" (R4), or fund the 3-run variance
   probe now?
5. **R2's quantum** — is one mutant / 1.0 pp the right forcing step, or should a round be allowed to
   bank more than one quantum before the raise is forced?
6. **Budget arbitration** — alternating nights now, or hold the rotation until the static-mutant
   attribution research reports?
7. **D-e** still gates the changelog dimension (76 unannounced ids) and only a human can discharge it.
8. **The reframe itself**: confirm that "97% from all points of testing views" is delivered as
   *per-dimension honest targets with adjudicated denominators* — with the two places it must be
   refused as a raw number (generator leg ≈ 90 ceiling; matrix ≤ 26 excused impossible) recorded as
   ceilings with proofs rather than aspirations.

---

## Sources

- Petrović & Ivanković, *State of Mutation Testing at Google*, ICSE-SEIP 2018 —
  <https://research.google/pubs/state-of-mutation-testing-at-google/> (PDF:
  <https://research.google.com/pubs/archive/46584.pdf>)
- Petrović et al., *Practical Mutation Testing at Scale: A view from Google* —
  <https://www.semanticscholar.org/paper/Practical-Mutation-Testing-at-Scale:-A-view-from-Petrovi%C4%87-Ivankovic/c04b8dddca00070ae4abb1864974f4c3d05274f2>
- Beller et al., *What It Would Take to Use Mutation Testing in Industry — A Study at Facebook*,
  ICSE-SEIP 2021 — <https://arxiv.org/abs/2010.13464>
- Schuler & Zeller, *Covering and Uncovering Equivalent Mutants*, STVR 2013 —
  <https://onlinelibrary.wiley.com/doi/abs/10.1002/stvr.1473>
- Ivanković, Petrović, Just, Fraser, *Code Coverage at Google*, ESEC/FSE 2019 —
  <https://research.google/pubs/code-coverage-at-google/>
- SQLite, *How SQLite Is Tested* — <https://www.sqlite.org/testing.html>
- Stryker.NET configuration (thresholds, `since`, `with-baseline`) —
  <https://stryker-mutator.io/docs/stryker-net/configuration/>; ignore mutations / `Stryker disable`
  comments — <https://stryker-mutator.io/docs/stryker-net/ignore-mutations/>; mutant states & score
  formula — <https://stryker-mutator.io/docs/mutation-testing-elements/mutant-states-and-metrics/>
- Coverlet compiler-generated-branch handling —
  <https://github.com/coverlet-coverage/coverlet/pull/549>,
  <https://github.com/coverlet-coverage/coverlet/issues/810>
- github-action-benchmark (BenchmarkDotNet adapter, `alert-threshold`) —
  <https://github.com/benchmark-action/github-action-benchmark>
- NuGet lock files / repeatable restore —
  <https://devblogs.microsoft.com/dotnet/enable-repeatable-package-restores-using-a-lock-file/>

---

## Maintainer rulings — 2026-08-21, session Q&A

1. **The reframe is ACCEPTED**: 97% delivered as per-dimension honest targets; the generator-mutation and
   surface-matrix dimensions ship adjudicated-denominator targets plus proven ceilings, never a gamed raw
   number.
2. **In-source adjudication markers are REJECTED** ("No in-source markers"). Equivalent mutants stay
   **ledger-only**: no `// Stryker disable once` adjudication comments in product code. Consequence, stated
   plainly: raw scores stay depressed by the proven-equivalent residue, and every gate/floor works on the RAW
   measured score, with the ceiling gap carried as a **documented offset** (the equivalents ledger with its
   per-mutant proofs and an exact-pinned count). The mandatory-raise invariant (R2) still applies to the raw
   score. The generator leg's honest asymptote is therefore its ~89.8% raw ceiling, and "97%" for that leg
   formally means: raw floor at ceiling-with-proof, offset ledger discharged. (H7's existing
   `Stryker disable all` around the DocSnippetInjector progress guard predates this ruling and guards
   test-infrastructure honesty, not score adjudication — it stays.)
3. **The 44-minute deep-tier ceiling is RAISED** ("Raise the ceiling"): the nightly budget may exceed 44 min
   so ALL legs run EVERY night — no alternating rotation. The static-mutant attribution research remains
   worthwhile for wall-time but is no longer the enabling constraint. T5 should assemble the nightly with
   everything included and simply record the measured total; the fast tier's cap is unchanged.
