# Round 22 — compiler-grade testing and the 97% gates program

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.

**Goal (two campaigns, one round, per the maintainer's rulings of 2026-08-21):** land the compiler-testing
arc (R22-00 → 01 → 02, as corrected by the audit) and the round-22 slice of the 97% program (per-dimension
honest targets, RAW-score gates, equivalents ledger-only) — plus arming the nightly instruments that round 21
assembled but never ran, and sweeping the round-20 task-list remainders into explicit dispositions.

**Sources of authority, in precedence order:**
1. Maintainer rulings appended to `Issues/round22/RESEARCH-97-PERCENT-GATES.md`: (a) per-dimension reframe
   ACCEPTED; (b) in-source adjudication markers REJECTED — equivalents are **ledger-only**, every gate works
   on the **RAW measured score**, the ceiling gap is a documented offset; (c) the 44-min deep-tier ceiling is
   RAISED — all legs run every night, no rotation, record the measured total; (d) **Sonar REJECTED outright**
   (SSAL disqualifying; LGPL 9.x pin declined).
2. `Issues/round22/AUDIT-2026-08-21.md` — binding corrections to the RFC: R22-00→02 ADOPT with fixes;
   R22-03 already landed in round 21 (`cedad48`); **R22-04 REJECTED** (built on falsified evidence and a
   break-below-measured philosophy the house rejects); R22-05 DEFERRED behind an explicit saturation trigger.
3. `Issues/round22/ROUND22-COMPILER-TESTING-RFC.md` — the arc's design (all code is sketch; re-derive every
   `// SEAM:` against tip and compile).
4. The ledgers: `Issues/ledgers/round21-sdd-ledger.md`, `T3-mutation-survivors.md`, `H7-timeout-dissection.md`.

## State at round start — measured, master tip `96e62f9` (post-round-21 merge `d131c76`)

- **Fast tier:** whole-solution build 0 warnings / 0 errors (samples included); full suite **7,699 / 0**
  across 8 projects; matrix **866/866**; build median ~25.6 s, suite ~63–75 s.
- **Mutation (all Timeout 0, quiet 12-core machine):** runtime **87.61 %** / `break` **87** (11:36);
  generator **71.64 %** / `break` **71** (19:41); DocTooling **67.96 %** / `break` **67** (5:04).
- **Coverage line floors** (branch informational per R4): DwarfMapper **78.3** · Generator **93.7** ·
  DocTooling **90.7** · CodeFixes **92.4** · Testing **83.2**.
- **Deep tier:** `DWARF_DEEP=1` = **15,786 tests @ ~105 s** (+42 s over fast); coverage collection +18 s;
  ILVerify ~1 s (runtime clean; Gallery = 3 known hand-written stackalloc findings).
- **Benchmarks:** 41 executed, **13 `*_Dwarf` allocation pins byte-exact across 3 runs** (Windows-measured);
  smoke ~6:36, folded into `-Nightly`.
- **Nightly CI (`mutation` matrix + `deep-test`) is assembled but DORMANT** — it has never run on master;
  `schedule` fires only on the default branch and nothing is pushed. Pushing `ci.yml` needs the `workflow`
  OAuth scope — **the push itself is the maintainer's.**
- **Equivalents:** proven-equivalent mutants exist only as ledger prose (generator 15 proven + 8
  probably-equivalent; DocTooling 1; runtime 1 proven + the ruled-in-practice `Key.Equals`); no
  machine-readable ledger, no pinned count yet.

**Known-stale rows in the sources, used with correction (flagged, not resolved here):** the research's
dimension table predates T7 and T5 — its runtime row (61.02 / break 61) is superseded by **87.61 / 87**, and
its line floors (77.7 / 93.8) by **78.3 / 93.7**; its §4 rows "allocation regression gate" and "test-hang
backstop" already LANDED in round 21 (T8, T5); its §5 budget-collision arbitration is superseded by ruling
(c). The RFC's `break: 0` and no-mutation-CI claims were falsified by the audit. `Issues/round20/TASKS.md`'s
NOW section and F1 handoff still show pre-merge state (`EmittedInvalidCode` 10 — it is **0**; F1 unmerged —
it merged at `dc385d4`); W6 sweeps those statuses.

## Budget

- **Fast tier: cap unchanged** — ~90 s suite, no change may grow it by more than ~10 %; measure
  before/after on every task that touches test projects.
- **Nightly: unbounded per ruling (c)** — no rotation, no total-duration gate. But **every leg's wall-clock
  is still measured and recorded** (local and, once armed, hosted-runner figures), and nothing enters the
  nightly without a measured wall-clock first. Iteration counts for new legs live behind `DWARF_DEEP`
  (T4's knob: catalog entry + `DeepTierSelfTests` pin in the same commit), never as copied tests.

## Global rules (carried over from round 21, plus the round-22 additions)

- Never `git push` — commits stay local, merge/push are the maintainer's. **Explicit pathspec on every
  commit.** `src/DwarfMapper.Generator` stays netstandard2.0.
- Warnings-as-errors, `AnalysisMode=All`, nullable, SPDX headers, doc-comment density.
- **Measured floors, no cushions** (invariant R1): every gated floor/break equals the measured value at the
  gate's stated precision and moves only in a commit containing the re-measurement. The rejected
  "break = measured − 4" philosophy stays rejected. No gate on a nondeterministic oracle (R4): branch %
  and wall-clock stay informational.
- **Whole-solution build gate:** any generator diagnostic/emission change builds the WHOLE solution,
  samples included, before it is called done.
- **Five-file diagnostic sync** for every new id: descriptor + AnalyzerReleases.Unshipped +
  docs/diagnostics.md + NegativeCases + CHANGELOG (index regenerated by the doc self-heal).
- **H7 termination discipline:** no loop lands without a provable termination variant or a progress guard;
  no detection anywhere may ride a wall clock. New harness loops (graph assembly, population, compare in
  the K-tasks) are explicitly in scope.
- **RepoWriteGuard / ARCH-06:** every raw file-write API use in `tests/` is a registered pattern with a
  reason and a pinned count; new test projects inherit the scan on arrival — design for temp dirs only.
- **Mutation-leg integrity (binding maintainer ruling):** never narrow `mutate`, never filter the test set,
  never exclude a test project.
- **Wake-failure workaround (standing, 3rd occurrence confirmed):** background agents' completion is
  verified in the foreground by the controller (monitor + ping); no agent's result is assumed delivered.
- Every tool adopted must be **RUN with measured output** in the task report — never merely referenced.
- Matrix stays green at 866/866; populations of ten or fewer stay **exactly pinned** (`AssertRatchet`
  refuses a ≤10 ceiling).

**Ruling (b) boundary, stated so nobody relitigates it:** the rejection of in-source markers scopes to
**mutation adjudication** (`// Stryker disable` comments as score mechanics). The coverage-denominator work
in P4 uses `[ExcludeFromCodeCoverage(Justification = …)]` — a different instrument the research specifies
and the reframe-acceptance covers: it is an attribute on compile-time-only code with a scanned, sanctioned,
exact-pinned justification, not a score adjudication. H7's one existing `// Stryker disable all` (the
`DocSnippetInjector` progress guard, 7 Ignored mutants) predates ruling (b) and guards test-infrastructure
honesty, not score — it is **grandfathered and exactly pinned** by P1's scan so no new in-source disable
can ride in under the exception.

---

## Layer 0 — arm the dormant instruments (maintainer-gated on the push)

Everything here waits on master being pushed with the `workflow` OAuth scope — that act is the
maintainer's. The agent work is the preparation, the first-run verification, and the follow-up commits.

### Z1 — first real runs of the `mutation` matrix and `deep-test` jobs

**What:** after the push, trigger (or let the cron fire) the four nightly jobs; verify against the
first-run checklist: H4 pre-flight executed per leg, non-vacuity guard counted scoreable mutants, the
`git diff --exit-code` clean-tree invariant held, per-leg artifacts uploaded, coverage report present.
**Why:** T5's own ledger states plainly what is UNEXERCISED: the nightly jobs themselves, and the Linux
path of the ILVerify ref-pack chain. A gate that has never run is decoration.
**Verification (measured):** per-leg hosted-runner wall-clocks recorded next to the local figures
(expect ~2–3× on 4-core runners; Stryker concurrency drops 6 → 2); all four jobs green or each red
diagnosed to a named cause.
**Exit:** one complete nightly (or dispatched equivalent) green on master, wall-clocks in the ledger.

### Z2 — Linux firsts: ILVerify path and the allocation pins

**What:** the `deep-test` ubuntu run exercises the ILVerify ref-pack resolution chain (Windows-verified,
Linux by-construction only) and the 13 allocation pins (Windows-measured bytes). If any Linux byte
differs, follow the baseline's own documented update path — and make the per-OS decision **in that same
commit**: either the bytes are identical (record the measurement, single baseline stands) or they differ
(adopt per-RID pin columns in `allocation-baseline.json`, each column measured on its OS; never a
tolerance band). The Dict-throughput platform caveat (memory: 2.13× Windows / ~1.14× Linux) is about
throughput, which is not gated; allocation is expected identical but that is expectation, not measurement.
**Why:** T8's ledger names this the one unmeasured axis of an otherwise byte-exact gate.
**Verification:** deep-test green on ubuntu with the allocation gate passing; the identical-or-per-RID
decision recorded with numbers.
**Exit:** the gate is measured on both OSes it runs on.

### Z3 — flip `roslyn-forward-compat` off `continue-on-error`

**What:** `continue-on-error: true` → `false` on the forward-compat leg (a per-push leg, not nightly),
citing the first green run **on pushed master** by run id in the flip commit. The flip commit is agent
work; the push that enables both the observation and the landing is the maintainer's.
**Why:** the leg's own comment states the obligation — "a gate that cannot fail is decoration"; the
research's one remaining adopt-r22 CI row.
**Exit:** the flag flipped with the green-run citation; the leg can now fail a push.

---

## Layer 1 — the 97% program, round-22 slice

### P1 — the ratchet invariant (R1–R4) as a SelfValidation scan, and the equivalents ledger

**What:** two instruments, one task, because the second feeds the first's documentation-offset clause.

1. **The scan.** One SelfValidation test enforcing the four clauses: **R1** — every gated floor/break
   (`$coverageFloors` in `scripts/housekeeping.ps1`, the three `stryker-config*.json` `break` values)
   carries its `{value, run-id/date, commit}` annotation; **R2** — the deep-tier script fails when
   `measured ≥ floor + q` (q = 1.0 pp line coverage, one mutant's score-worth per mutation leg, 1 per
   counted population — the default pending the maintainer's answer to research Q5) with a message that
   says *raise the floor to the measured value in this commit*; **R3** — every `// Stryker disable` in
   `src/` is counted and **exactly pinned at 1** (the grandfathered H7 progress guard; any increase is a
   ruling-(b) violation, red by construction), and every `[ExcludeFromCodeCoverage]` carries a non-empty
   justification naming a sanctioned category (see P4) — count exactly pinned; **R4** — a comment-level
   assertion that no gate reads branch % or wall-clock (the gates' own config comments name their oracle).
2. **The equivalents ledger.** `Issues/ledgers/equivalent-mutants.md` (machine-readable table): one row
   per adjudicated mutant with leg, file:line, mutator, **proof-grade category** (proven-equivalent /
   ruled-in-practice / probably-equivalent) and the case-analysis anchor. Seed from the existing ledgers:
   generator 15 proven + 8 probably-equivalent (T3), DocTooling 1 (`DocSnippetInjector.cs:83`), runtime 1
   proven (`DwarfRefContext` L77) + `Key.Equals` ruled-in-practice. Per-category counts **exactly pinned**
   by the scan; per-leg **raw ceiling** stated as the documented offset (generator ≈ 89.8 raw). Gates keep
   working on RAW scores — the ledger explains the gap, it never enters the denominator.

**Why:** ruling (a) + (b); research §3 — the repo has the floor-only half of the ratchet, this adds the
forcing half so slack cannot be banked. Research H4's config-sanity half already landed in round 21; this
scan is its measurement-side complement, not a rewrite.
**Verification:** scan red on a doctored floor comment / an added `Stryker disable` / an unjustified
exclusion (sabotage demos, reverted); R2 demonstrated by a deliberate floor-lag on a scratch value.
**Exit:** scan green at tip; ledger counts pinned; the offset per leg stated with its proof anchors.

### P2 — runtime-leg kill list (holes 2–3, 11-partial, 13, NC 7, 10) → break to measured

**What:** kill the post-T7 remainder mapped in the C6 row: null-guard statement removals on `Map`
L133–134 and `Update` L258–261 (holes 3, 2); the three iterator-remedy string tails L98/L101/L104
(extend the lazy-iterator message assertions); the facade `TryGet` guard `&&`→`||` (hole 13);
`Equals(object)` NoCoverage (hole 7); `TryEnterNode` NoCoverage (hole 10). The `ambiguousInterfaces`
`Count > 1` boundary (L86) is a near-equivalent-via-public-path candidate — adjudicate it into P1's
ledger (probably-equivalent) rather than chasing it. Re-run the leg on a quiet machine; `break` (and
`low` with it — Stryker refuses `low < break` with exit 0) to the measured floor in the same commit.
**Why:** research dimension table, runtime row, corrected for T7: the 97-by-R24 path runs through these
enumerated holes; each is named with its missing case in `E3-E1-report.md`.
**Verification:** per-mutant `killedBy` confirmed in the JSON; post-run `git status` clean; measured
score + wall-clock reported.
**Exit:** the named mutants Killed or ledger-adjudicated; `break` = new measured floor.

### P3 — DocTooling kill list (families A–D + ParseId) + triage of the 43 → break to measured

**What:** land the five enumerated one-test-per-family kills from T3's ledger: **B** `DocTableInjector`'s
two refusals (5 mutants, no dedicated test file, guards a demonstrated document truncation — first);
**C** `ExampleCatalogue.Build`'s two refusals via the existing file-list seam, synthetic `[DocExample]`
type (14); **A** the message-pin convention — every `Assert.Throws<DocToolingException>` also asserts a
discriminating fragment AND the reported line number (~30 mutants incl. the `i+1 → i-1` class);
**D** `OptionTableRenderer` via one synthetic attribute type with `string`/`int?`/null-default properties
(11); **ParseId** malformed-marker branches in both files (7). Then triage the 43 write-back-exposed
survivors with the house judgement format (real hole vs equivalent — they are explicitly NOT pre-judged
equivalent); kill what falls out cheaply, adjudicate the rest into P1's ledger. Re-measure; break 67 →
measured.
**Why:** research names this leg the honest showcase — ~99.6 raw ceiling, 97 raw realistic by R24.
**Verification:** leg re-run under RepoWriteGuard, post-run status clean, Timeout stays 0, measured
score + wall-clock; suite still green with fast-tier cap held.
**Exit:** the five families dead; the 43 dispositioned row-by-row; `break` = new measured floor.

### P4 — coverage denominator honesty + floors to measured

**What:** enumerate every 0 %-covered class per assembly from the existing ReportGenerator summary;
classify each **by-design vs hole**. Apply `[ExcludeFromCodeCoverage(Justification = "…")]` only under a
sanctioned category — the first and only pre-approved category is *compile-time-only attribute, consumed
by the generator* (research Q3's further categories, e.g. defensive unreachable arms, need a maintainer
ruling before use — holes by definition until ruled). P1's scan obligates: justification non-empty,
category sanctioned, the excluded symbol present in the generator-test corpus / surface catalog (the
non-vacuity teeth), count exactly pinned. Re-measure all five assemblies and set floors to the new
measured values in that commit. **Schedule after P2/P3/P5-adjacent kill work** — mutation kills and
coverage floors co-move (the NoCoverage families add covered lines), so read the floor off the same run.
**Why:** research §2.2 — the runtime assembly's 78.3 is depressed by generator-metadata attribute classes
never executed at runtime; a dirty denominator makes the floor a lie in both directions and 97 meaningless.
**Verification:** the classification table (class → by-design/hole → action) in the task report;
re-measured floors; scan green with the pinned exclusion count.
**Exit:** zero unclassified 0 %-covered classes; floors = measured on the honest denominator.

### P5 — generator-leg kill list (top-3 families) → break to measured

**What:** with the 15 proven equivalents in P1's ledger (documented offset — NOT removed from the
denominator), kill the triaged-real top of T3's ranking: (1) `BlittableProof.IsSourceSequential` L97
(`IsInSource → true` — the auto-blit safety gate admits metadata structs; the missing case is a BCL
struct field-compatible with a user struct — an unsafe *accept*, worst failure mode in the repo);
(2) `BlittableProof.InstanceFields` L78 + file-path comparator (~12 mutants) via the partial-file fixture
— the same struct pair split across two files, compiled in both file orders, same `CanReinterpret`
verdict (**write it as a corpus row**: it is also exactly a shape K0's validity rules must express, so
the mutation kill and the differential coverage land together); (3) `ConstructorSelector` L288
(`Any → All` on ref/out — one mixed `Dst(int a, ref int b)` constructor; the failure it prevents is the
`EmittedInvalidCode` genre), plus L55 and L230. Leave the dead-code questions (BlittableProof L29–L30,
ConstructorSelector L281/L285, the L88 flag) to the maintainer — they are denominator decisions
(research Q2), listed below. Re-measure; break 71 → measured (~80 raw expected; the ~89.8 raw ceiling
stays a documented offset per ruling (b)).
**Why:** research generator row; T3's kill-first ranking with per-mutant evidence.
**Verification:** re-run (≈20 min — quiet machine, no concurrent builds), zero Timeout, measured score +
wall-clock, `break`/`low` moved same-commit.
**Exit:** the three families dead; `break` = new measured floor; do-not-attempt list untouched.

### P6 — matrix excuse obligations B3 + B6, and the B11 count

**What:** (1) **B3** — the option matrix's `NotApplicable` excuse becomes re-measured, not
reason-string-only: each `NotApplicable` cell is re-classified live the way the surface matrix's excuses
are, so it can go stale-red; also fix `DeclaredDivergences.CoversOption`'s endpoint-blindness. (2) **B6**
— the `NoSuchSite` ratchet gains per-cause pins so offsetting drift inside the 116 cannot pass silently.
(3) **B11** — `PredatesThisProject` becomes a counted, shrink-only population (exact pin), closing the
one hatch `DiagnosticCoverageRatchetTests` claims but does not hold.
**Why:** research §2.4 and the dimension table's R22 column: the matrix's honest 97-equivalent is *every
excuse category carries a discharged, re-measured obligation*; B3 is the only excuse left that cannot go
stale-red. B7 + B32 are the R23 half — deferred below, on the research's own staging.
**Verification:** each new guard sabotage-demoed red (a doctored reason, an offsetting per-cause swap, a
hatch row added) then green; matrix stays 866/866; populations re-measured in-commit.
**Exit:** B3, B6, B11 rows flip DONE with measured pins.

---

## Layer 2 — the compiler-testing arc (R22-00 → 01 → 02; 05 parked)

The arc and the 97-program are one campaign seen from two sides: the arc *generates* pressure (generated
graphs reach the generator's NoCoverage branches; metamorphic relations pressure the branches coverlet's
wobbling metric cannot gate), the gates *bank* it. All RFC code is sketch — re-derive every `// SEAM:`
against tip and compile first.

### K0 — type-graph descriptor + renderer (R22-00, with the audit's corrections)

**What:** new `tests/DwarfMapper.CompilerTests/` (own project — SharpFuzz can instrument it later without
touching the main test assemblies): `GraphSpec`/`NodeSpec`/`MemberSpec` descriptor model, CsCheck
generators (CsCheck 4.7.0 is already pinned), renderer to compilable C# + mapper declaration. The
validity rules ARE the grammar, and the audit's corrections are binding: acyclic `NestedRef` wiring;
**per-node** member-name dedupe; forbid `required`×CtorParam; forbid `BaseRef` on structs; **forbid
value-type cycles through `NestedRef`** (a struct containing itself is CS0523, uncompilable regardless of
the mapper — this also guards K2's MR-3 re-kinding); the RFC's `Assemble` tuple-type sketch is a syntax
error — rewrite, don't transcribe. Population/compare loops carry H7 termination discipline; the project
writes temp dirs only and inherits the ARCH-06 scan on arrival.
**Companion (the RFC's own demand):** a ratchet scan asserting the descriptor's `TypeKind`/`MemberShape`/
`CollShape` enums cover every kind the surface matrix enumerates — generator bias becomes a declared,
counted population, same family as P6's obligations.
**Verification:** N sampled graphs render and **compile clean** (Roslyn in-memory, measured count +
wall-clock reported); the enum-coverage scan green and sabotage-demoed (drop a kind → red).
**Exit:** the generator produces valid-or-refused C# only; the bias scan is live.

### K1 — differential oracle over generated graphs (R22-01)

**What:** `DifferentialOracleTests` — two legs per sampled graph. **Leg 1 (must-compile-or-refuse):** a
loud generator refusal is a valid outcome; generator silence + compilation errors in the output is a
seed-replayable red. The audit's sequencing blocker is GONE — round-21 T6 emptied `EmittedInvalidCode`
(B27 → DWARF094, B33 → correct emission), so leg 1 needs no divergence allowance on day one; this leg is
the pinned-at-0 ratchet generalized from 866 cells to the generated space. **Leg 2 (oracle):** DwarfMapper's
map vs a deliberately naive reflection copier — **reuse the `[RoundTrip]` differ shipped in
`DwarfMapper.Testing`** for the member-path diff (dogfoods the shipped testing surface; do not grow a
parallel comparer). The oracle is test-side, public-members-only — this does NOT violate the house
no-reflection stance, which governs the shipped product (stated here so nobody relitigates it). One
oracle switch per **documented** option; every disagreement either shrinks (CsCheck) to a pinned corpus
row (a generator bug) or documents an undocumented semantic and teaches the oracle — a spec-completeness
ratchet with `DeclaredDivergences`' shrink-only discipline.
**Iteration counts:** behind `DWARF_DEEP` (catalog entry + `DeepTierSelfTests` pin, same commit): fast
tier a smoke count (tens), deep tier the full count (target 1,000; the 10,000 variant is a knob value
only if measured). **Measured wall-clock BEFORE the nightly integration** — the round-21 rule; the raised
ceiling removes the arbitration, not the measurement.
**Verification:** measured fast-smoke and deep wall-clocks; fast-tier cap held; at least one sabotage demo
(hand-broken emission → leg 1 red with the seed printed); any real disagreements filed as corpus rows.
**Exit:** both legs in the deep tier with recorded wall-clocks; the corpus-row pipeline demonstrated.

### K2 — metamorphic relations (R22-02)

**What:** `MetamorphicTests` over K0's corpus: **MR-1** member declaration order is semantics-free
(name-keyed population — the audit's correction — or the relation fails spuriously); **MR-2** adding an
unmapped/ignored member is behaviour-neutral (pin the base config: no explicit-only/strict modes, or
additions legitimately refuse); **MR-3** class/record/record-struct representation invariance (the
A11-F1/F2 bug class generalized), with the value-type-cycle guard on re-kinding, not just struct+BaseRef.
Iteration counts behind `DWARF_DEEP`, same discipline as K1.
**Why:** these vary exactly the axes whose branches R4 forbids gating via branch % — a violated relation
is a deterministic red, which is what §2.3 gave up.
**Verification:** measured wall-clocks; sabotage demo per relation (e.g. an order-dependent hand-mutation
→ MR-1 red); fast cap held.
**Exit:** three relations live in the deep tier, wall-clocks recorded.

### K3 — static-mutant attribution research (wall-time, no longer blocking)

**What:** research-first (T3 open item 3): why does Stryker classify 85 of the generator leg's 201
scoreable mutants as `static` (each re-running all 5,798 tests — ~493k executions, 14:39 of the leg's
19:41)? Likely the harness's static caches / module-init path. Deliverable is a diagnosis + a measured
prototype if the fix is cheap; potential −14 min nightly. Per ruling (c) this is an optimization, not an
enabling constraint — it must not delay K0–K2.
**Verification:** the mechanism named with evidence; any change re-measured against the 19:41 baseline.
**Exit:** a written verdict — recoverable (with the measured delta) or not (with why).

### Parked — R22-05 coverage-guided fuzzing, with its saturation trigger

**DO NOT START** until the trigger fires: **five consecutive nightly deep-count K1 runs (full
`DWARF_DEEP` iterations) producing zero new oracle disagreements and zero new pinned corpus rows.**
(N = 5 is the plan's default; the maintainer may adjust it — but the trigger stays a counted condition,
never a judgement call.) When adopted: separate project (SharpFuzz instruments the generator DLL in
isolation), total-function byte-decoder into `GraphSpec`, and **seed the corpus from the surface matrix's
enumerated cases** — the hand-built matrix becomes the fuzzer's starting population (the one R22-05 idea
the audit endorses unreservedly).

---

## Layer 3 — pressures adopt-r22 (what remains after round 21 landed the rest)

### S1 — supply-chain gate: NuGetAudit `all` + lock files + `--locked-mode`

**What:** `NuGetAuditMode=all` (transitives audited; NU1901–NU1904 become errors under
warnings-as-errors), `RestorePackagesWithLockFile` across the solution, CI restores with `--locked-mode`.
One-time lock-file churn committed with explicit pathspecs.
**Why:** the research's decision table — SBOM + attestation exist but no vulnerability *gate* and no
repeatable-restore proof; near-free. (The table's other adopt-r22 rows already landed in round 21:
allocation gate = T8, blame-hang backstop = T5, roslyn flag = Z3, attribution research = K3. Sonar is
rejected by ruling — nothing here may reintroduce it. Reproducible-build, wall-time alerting,
compile-time-cost gate, package-size ratchet, cross-platform legs, preview-SDK canary: all r23 rows,
untouched this round.)
**Verification (measured):** build 0 warnings with audit on (any NU19xx triaged, fixed or suppressed with
reason); lock files present per project; a sabotage demo — tamper one lock hash → locked-mode restore
fails; restore-time delta measured.
**Exit:** CI restore is locked-mode; audit gate live at error severity.

---

## Layer 4 — the swept TASKS.md remainders

Every non-`DONE` row of `Issues/round20/TASKS.md`, explicitly dispositioned. FOLD = a round-22 task
below; DEFER = round 23+ with the reason stated; MAINT = maintainer-only, listed in the final section.

| Row | Disposition | Where / why |
|---|---|---|
| B3 | FOLD | P6 — the one excuse that cannot go stale-red |
| B4 | FOLD | W5 — pin the legal same-source `[FlattenGraph]` shape |
| B5 | FOLD | W5 — baseline gate, with the four DWARF001-by-design fixtures excepted by name |
| B6 | FOLD | P6 — per-cause `NoSuchSite` pins |
| B7 | DEFER | research stages it R23 with B32 (obligation completeness) |
| B8 | FOLD | W5 — one shrink-only guard over both prose rules |
| B10 | FOLD | W5 — reach the throwing default arm |
| B11 | FOLD | P6 — counted shrink-only population |
| B12 | FOLD | W5 — forbid the double-count |
| B13 | FOLD | W5 — resolve file + anchor, not shape |
| B14 | FOLD | W5 — document the `*.g.cs` collision case |
| B15 | FOLD | W1 — the silent `[MapIgnore(...)]` argument discard (product half) |
| B16 | DEFER | wording-only; rides on B22's DWARFR-wording decision (r23) |
| B17 | DEFER | cosmetic dead helper in generated output; no behaviour effect, no ratchet touches it; r23 tidy-up |
| B18 | DEFER | needs its own diagnostic decision + five-file sync; round-22 product budget is the arc; stays recorded |
| B19 | FOLD | W6 (the documentation half — write the limitation where the ceilings are read); the systemic remedy IS K1/K2 |
| B20 | FOLD | W1 — same silent-discard shape as B15; decide one way for both; check reliance first |
| B21 | FOLD | W1 — the comparer has one home now; decide, then pin (CaseInsensitive × ignore set) |
| B22 | DEFER | the fold-DWARFR-into-DWARF0xx vs own-wording-gate decision is an r23 arc of its own; B16/B35/B36 wordings ride on it |
| B23 | FOLD | W4 — the research names it: fix the renderer, not a new gate |
| B24 | DEFER | new diagnostic + five-file sync; identical-scope contradiction needs its own design; recorded, unreachable by the matrix (×2 axis renders identical applications) |
| B25 | FOLD | W2 — probe-args fix; 28 cells currently measure a type error, not the directive |
| B26 | FOLD | W2 — warn only when a leaf was consumed; the D10 false-evidence enabler |
| B29 | DEFER | changes WHICH constructor existing projections call — needs its own before/after measurement round |
| B30 | FOLD | W3 — `SynthNested` CS1729 is the `EmittedInvalidCode` genre with no diagnostic; the population must stay 0 in spirit, not just in the matrix |
| B31 | DEFER | new id + the two-messages design question; five-file sync; r23 with B24 |
| B32 | DEFER | R23 with B7, per the research staging |
| B35 | DEFER | no viable remedy proposed yet; rides on B22's family decision |
| B36 | DEFER | prose-only; rides on B22 |
| C5 | FOLD | W6 — the open-coded `AssertRatchet` + the private repo-root walk `RepoPaths` exists to replace |
| D-a | MAINT | ruled (keep + document); the docs/options.md write awaits the maintainer's word per the round-20 handoff |
| D-c | MAINT | edits the agent's own instructions (CLAUDE.md) — called out, never done quietly |
| D-e | MAINT | 76 unannounced diagnostics; drafting offered, awaiting the word |
| D-f | MAINT | a design ruling: does `[MapTo]` participate in ambient registration at all |
| F1 | FOLD | W6 — reality closed it (merged `dc385d4`/`d131c76`); flip the row with evidence |
| F2 | FOLD | W6 — the ledger was captured at `96e62f9` "before the worktree is removed"; flip with evidence |
| F3 | MAINT | where the plan documents live is a repository-layout preference |
| H3 | FOLD | W7 — Meziantou phase 2 |
| H8 | MAINT | the durable-form decision (document the pwsh prerequisite and/or add a BOM); once ruled, the edit is one small task |

### W1 — the silent-discard pair: B15 + B20 (+ B21 decided and pinned)

**What:** decide ONE policy for a `[MapIgnore]` whose argument matches nothing (registry `[MapIgnore("x")]`
discard = B15; unscoped `[MapIgnore("Typo")]` inert everywhere = B20): the `DWARF056` "matched nothing"
family is the natural shape. **Check first whether any test or sample relies on a dead `[MapIgnore]`**
(B20's own warning). Settle B21 in the same task — under `CaseInsensitive = true`, does `[MapIgnore("id")]`
exclude `Id`? Decide, implement (one comparer home: `MapperExtractor.IgnoreNameComparer`), pin both
directions. New/extended diagnostics get the five-file sync; whole-solution build; matrix re-measured.
**Exit:** no `[MapIgnore]` form is silently inert; B15/B20/B21 rows DONE with measurements.

### W2 — matrix measurement integrity: B25 + B26

**What:** fix `MapValueAttribute<TTarget>`'s probe arguments (`{Name}` + a string, per the arity-0
precedent) so its 28 cells measure the directive, not a type error; re-measure the cells and any ceiling
movement in the same commit — and examine the DWARF056 readings at UpdateInto/Projection the row flags as
an unexamined claim. Gate `DWARF044` on `flatMatches` actually consuming a leaf (B26) — the change to an
existing warning's trigger is measured across the matrix, not argued.
**Exit:** both rows DONE; every moved population re-measured in its commit.

### W3 — `SynthNested` parameterless-ctor guard: B30

**What:** hoist the `HasParameterlessCtor` test (the `ReportUnreadConstructorDirective` hoist pattern) so
a nested member or collection ELEMENT whose type is ctor-only is refused instead of reaching the compiler
as CS1729 out of generated code. In-task ruling with recorded reasoning: reuse `DWARFR09` naming the
nested type vs a new id (the remedy sentence reads differently when the type at fault is not the annotated
one) — if a new id, full five-file sync. Pinned both ways; whole-solution build.
**Exit:** no ctor-only nested/element target reaches the compiler unrefused; B30 DONE.

### W4 — the renderer whitespace fix: B23

**What:** `ApiReferenceRenderer` gets `LoadOptions.PreserveWhitespace`; regenerate; **review the full
regenerated diff** in the commit (the comparison is self-consistent, so no gate can check this — a human
reads the diff, per the row's own filing reason).
**Exit:** `docs/generated/` no longer eats inter-tag spaces; diff reviewed and stated in the report.

### W5 — the small-guards batch: B4, B5, B8, B10, B12, B13, B14

**What:** seven bounded pins/guards, one task, per-item commits where populations move: the legal
`[FlattenGraph]` shape pinned (B4); the fixture-baseline gate with the four by-design exceptions named
(B5); one shrink-only guard over `PredatesTheChangelog` + `DiagnosticTestAllowlist` (B8); the `CorpusFor`
default arm reached (B10); the Reasons/StructurallyInapplicable double-count forbidden (B12); evidence
links resolved, not shape-checked (B13); the `IsGeneratorAuthored` remarks completed (B14).
**Exit:** all seven rows DONE; every new guard sabotage-demoed red once.

### W6 — records hygiene: C5, B19-doc, and the stale-status sweep

**What:** (1) C5's remainder — replace the open-coded `AssertRatchet` (the `NoSuchSite` one) and
`AssemblyScanTests`' private repo-root walk with the shared helpers. (2) B19's ask — write the
limitation ("`Honoured` proves difference, not correctness; `Refused` proves a diagnostic, not the right
one") **where the ceilings are read**, cross-referencing K1/K2 as the systemic pressure on that class.
(3) The stale-status sweep of `Issues/round20/TASKS.md`: NOW section and F1-handoff ceilings corrected to
post-merge reality (`EmittedInvalidCode` 0, matrix 866/866 on master), F1/F2 flipped DONE with commit
evidence, C1/C6/B27/B33/H-row cross-references verified current. Statuses only — no history rewritten.
**Exit:** the task list tells the truth at a glance; C5 and B19 rows DONE.

### W7 — Meziantou phase 2: H3

**What:** extend Meziantou.Analyzer from the five `src/` projects to tests/samples/benchmarks. Budget one
session of noise triage across 11 test projects; every disable reasoned in `.globalconfig`, zero
unexplained suppressions, build stays 0 warnings; fast-tier build delta measured against the cap (MA0002
is the known hottest rule — first knob if the cap binds).
**Exit:** analyzers-everywhere posture consistent; measured build delta reported; H3 DONE.

---

## Maintainer-only (not agent tasks — listed so nothing hides)

- **The master/ci.yml push** — nothing is pushed; `ci.yml` needs the `workflow` OAuth scope. Layer 0 is
  gated on this single act.
- **D-e** — the 76 unannounced diagnostics need release notes before the first tag; drafting was offered
  and awaits the word.
- **H8 durable form** — document the pwsh/reportgenerator/ilverify prerequisites and/or give
  `housekeeping.ps1` a BOM; the decision is the maintainer's, the edit afterwards is trivial.
- **D-a, D-c, D-f, F3** — per the disposition table above.
- **The generator dead-code rulings (research Q2)** — `BlittableProof` L29–L30's apparently unreachable
  `true` return; `ConstructorSelector` L281/L285 (provably dead per the ledger); the L88
  `useObjectInitializerOnly` flag. Deletion shrinks the denominator honestly; adjudication keeps the code
  and pins the proof. The generator leg's 97-story depends materially on these.
- **Open research questions not yet ruled:** Q3 (further sanctioned coverage-exclusion categories — P4
  proceeds with the one pre-approved category only), Q4 (fund the branch 3-run variance probe, or leave
  branch informational — the plan defaults to informational per R4), Q5 (R2's quantum — the plan defaults
  to one quantum), and the R22-05 trigger's N (the plan defaults to 5).

## Ordering

Layer 0 is maintainer-gated and does not block anything else; Z3 additionally waits for a green run.
Inside the agent's queue: **P1 first** (the ratchet scan + equivalents ledger must exist before any floor
moves, so every subsequent raise is forced, and the pinned `Stryker disable` count guards ruling (b) from
day one) and **S1 early** (independent, near-free). Then the kill lists **P2, P3, P5** — test-side work,
parallelizable across worktrees, but their leg re-measures serialize on the quiet machine (T3/H5
precedent: no concurrent builds during a Stryker run). **P4 after the kill lists** (floors read off the
same corpus growth). **P6** independent. The arc runs **K0 → K1 → K2** sequentially (each consumes its
predecessor); K1's corpus-row seam should be open when P5 writes the partial-file fixture (shared shape —
coordinate, don't duplicate). **K3** any time, low priority. The **W batch** folds in where its files are
already open: W2/W6 pair naturally with P6 (matrix/records files), W1/W3 with the generator work (they
are five-file-sync product changes — whole-solution build gate applies), W4/W5/W7 free-standing. Nothing
lands in the nightly without its measured wall-clock; every floor that moves, moves in the commit that
re-measured it.
