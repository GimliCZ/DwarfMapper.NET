<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 23 — the product defects the compiler arc found, and the r23 gate slice

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.

**Goal, stated as the round's own thesis: round 23 is a PRODUCT round.** Round 22 built a compiler-grade
instrument (K0 type-graph descriptor → K1 differential oracle → K2 metamorphic relations) and pointed it at
the generator. It found **two real generator defects on its first two runs** — `I5` on K0's first 25-sample
smoke, `I7` on K1's first 1,000-sample deep run — and both are pinned **red-on-fix**: the corpus rows and the
runtime message pins assert *today's broken behaviour*, so they go RED the moment the defect is fixed. That is
the signal, not a failure. Fixing them, and removing their pins and their keyed sampled-space exclusions in
the same commit, is the spine of this round. Everything else — the r23 slice of the 97% program, the
adopt-later pressures, the swept task-list remainders — hangs off that spine.

The two defects are one family: **`Nullable<T>` lift gaps around synthesized pair maps**, both of the
ISSUE-044 endpoint/kind-inconsistency genre this repository has now closed eleven times. `I7`'s own filing
says it in as many words: *"Same root family as I5 … decide once for both."* This plan honours that — one
maintainer ruling, two implementations.

**Sources of authority, in precedence order:**

1. **The round-22 maintainer rulings** appended to `Issues/round22/RESEARCH-97-PERCENT-GATES.md`, all still in
   force and none re-litigated here: (a) the **per-dimension reframe** — 97 % is delivered as per-dimension
   honest targets, never a gamed raw number; (b) **ledger-only adjudication** — no in-source score-adjudication
   markers, every gate works on the **RAW measured score**, the ceiling gap is a documented offset in
   `Issues/ledgers/equivalent-mutants.md`; (c) the **deep-tier ceiling is raised** — all legs run every night,
   no rotation, record the measured total; (d) **Sonar REJECTED outright** (SSAL disqualifying; the LGPL 9.x
   pin declined) — nothing in this plan may reintroduce it.
2. `docs/superpowers/plans/2026-08-21-round22-compiler-testing-and-97-gates.md` — the format this plan matches,
   and the authority for what round 22 explicitly handed forward (its Layer 4 disposition table's DEFER rows
   and its maintainer-only section).
3. `Issues/round20/TASKS.md` **as it stands in the r22 worktree** (it is ahead of master) — the live task
   store, including the I-section filed this round (I1–I9) and the B-rows round 22 deferred.
4. The ledgers: `Issues/ledgers/equivalent-mutants.md` (the offset of record),
   `Issues/ledgers/T3-mutation-survivors.md`, `Issues/ledgers/E3-E1-report.md`,
   `Issues/ledgers/H7-timeout-dissection.md`, `Issues/ledgers/round21-sdd-ledger.md`, and the round-22 SDD
   progress log `.superpowers/sdd/2026-08-21-round22/progress.md` (git-ignored — capture it before the
   worktree is removed, the F2 lesson).

**Task-id scheme for this round** (so nothing collides with rounds 20–22): **Z** = Layer 0, carried forward
unchanged from round 22; **N** = Layer 1 product defects; **M** = Layer 2, the 97 % program's r23 slice;
**S** = Layer 3 pressures (continuing round 22's `S1`); **V** = Layer 4, the swept task-list remainders.

---

## State at round start — measured, with its provenance stated

**Read this before quoting any number below.** Master tip is **`b6cbc0f`** at the time of writing — and it
does **NOT** contain round 22. (Master is moving: it took two README-badge commits, `6244f3a` and `b6cbc0f`,
while this plan was being written. Treat the tip hash as informational and the *claim* as load-bearing: no
round-22 commit is on master yet.) Every round-22 figure in this section was measured in the worktree
`C:/Users/Jouda/RiderProjects/DwarfMapper-r22` at branch tip **`adbe9a3`** (W3 landed; **W4–W7 had not
landed when this plan was written**). This plan therefore *assumes the round-22 merge*, which is the
maintainer's act, and every figure here is provisional until that merge and the W4–W7 reconciliation
described at the end of Layer 4.

- **Fast tier** (at `adbe9a3`): whole-solution build `--no-incremental` **0 warnings / 0 errors**, samples
  included, ~33.0 s; full suite **7,806 / 0** foreground, ~69–75 s; surface-matrix census **866/866**.
- **Mutation, all three legs, all `Timeout` 0** (quiet 12-core reference machine):

  | Leg | Raw measured | `break` / `low` | Wall | Scoreable |
  |---|---:|---:|---:|---:|
  | runtime (`stryker-config.runtime.json`) | **96.46 %** (109 K / 3 S / 1 NC) | 96 / 96 | 8:08 | 113 |
  | generator (`stryker-config.json`) | **81.59 %** (164 / 201; 30 S, 7 NC) | 81 / 81 | 21:19 | 201 |
  | DocTooling (`stryker-config.doctooling.json`) | **95.42 %** | 95 / 95 | 4:36 | 284 |

- **Coverage line floors** (branch informational per R4), read from `scripts/housekeeping.ps1`
  `$coverageFloors` at `adbe9a3`: DwarfMapper **91.2** · Generator **93.7** · DocTooling **95.7** ·
  CodeFixes **92.4** · Testing **83.2**. (The runtime and DocTooling jumps are P4's denominator-honesty pass —
  nine compile-time-only attributes excluded with sanctioned justifications, count exactly pinned.)
- **Equivalents ledger** (`Issues/ledgers/equivalent-mutants.md`, created by P1, ruling-(b) compliant):

  | Leg | proven | ruled-in-practice | probably | **rawCeiling** | Offset from measured |
  |---|---:|---:|---:|---:|---:|
  | generator | 24 | 0 | 6 | **88.05 %** | 6.46 pp |
  | DocTooling | 10 | 0 | 0 | **96.47 %** | 1.05 pp |
  | runtime | 2 | 1 | 1 | **98.23 %** | 1.77 pp |

- **Deep tier:** **27** registered `DeepPopulation` entries, counted at `adbe9a3`; the research's "21
  registered populations" is stale (the delta was not traced — count it, do not narrate it). The compiler-tests
  project's
  populations, each measured before entering the catalog per the round-21 rule: `CompilerGraphSmokeSeeds`
  25 / 250, `CompilerOracleSeeds` 20 / **1,000**, `CompilerMrMemberOrderSeeds` 10 / 250,
  `CompilerMrUnmappedMemberSeeds` 10 / 250, `CompilerMrRekindSeeds` 8 / 150. Project-alone deep wall
  **30.4 / 31.7 / 30.7 s** across 3 runs with all three relations live.
- **Nightly CI is still DORMANT.** The `mutation` matrix and `deep-test` jobs are assembled and have **never
  run on master**; `schedule` fires only on the default branch and nothing is pushed. Pushing `ci.yml` needs
  the `workflow` OAuth scope — **the push is the maintainer's.** Layer 0 is unchanged from round 22 because
  none of it could be done.
- **Diagnostics:** round 22 minted **`DWARF095`** (W1, unscoped `[MapIgnore]` naming nothing) and
  **`DWARFR12`** (W1, the registry's discarded `[MapIgnore]` argument), and W3 **retitled `DWARFR09`** rather
  than minting a thirteenth registry id. `Scan9` now guards **all** live ids uniformly — D-e drained
  `PredatesTheChangelog` to zero and the baseline was deleted (`73c58c3`). **Next free ids: `DWARF096` and
  `DWARFR13`.**
- **Supply chain:** `NuGetAudit` level `low` / mode `all` with lock files across all 22 projects, CI restoring
  `--locked-mode`, housekeeping stage 0 (S1). One standing suppression, recorded as **I8**.

---

## Budget

- **Fast tier: cap unchanged** — the ~90 s discipline stands, no change may grow the suite by more than
  ~10 %; measure before and after on every task that touches a test project. Round 22 held it across ten
  tasks and ~150 new tests; there is no reason to spend the headroom now.
- **Nightly: unbounded per ruling (c)**, but **every leg's wall-clock is still measured and recorded**, and
  nothing enters the nightly without a measured wall-clock first. New iteration counts live behind
  `DWARF_DEEP` (catalog entry + `DeepTierSelfTests` pin in the same commit), never as copied tests.
- **A specific new cost to watch:** the N-tasks re-run K1 and K2 at deep counts to prove the sampled space is
  clean after each fix. That is ~30 s of CompilerTests wall per re-run today — cheap — but the *keyed
  exclusions being deleted widen the sampled space*, so the post-fix wall-clock is a new measurement, not the
  old one. Record it.

---

## Global rules (carried forward — re-stated because a rule nobody restates is a rule that erodes)

- **Never `git push`.** Commits stay local; merges and pushes are the maintainer's, per instance.
  **Explicit pathspec on every commit** — never `git add -A`. `src/DwarfMapper.Generator` stays
  netstandard2.0 (Roslyn host requirement); do not "upgrade" it.
- **Measured floors, no cushions** (invariant R1): every gated floor/break equals the measured value at the
  gate's stated precision and moves only in a commit containing the re-measurement. The rejected
  "break = measured − 4" philosophy stays rejected. **R2's mandatory-raise engine now exists** and is live —
  a leg measuring ≥ floor + q fails the deep run with a message telling you to raise the floor in this
  commit. That is the "slowly" engine; do not defeat it by not re-measuring.
- **No gate on a nondeterministic oracle** (R4): branch coverage % and wall-clock stay informational.
- **Whole-solution build gate:** any generator diagnostic or emission change builds the WHOLE solution,
  **samples included**, before it is called done. Samples are the over-eager-refusal detector — `dotnet test`
  and the manifest do not cover `samples/`.
- **Five-file diagnostic sync** for every new id: descriptor + `AnalyzerReleases.Unshipped` +
  `docs/diagnostics.md` + `NegativeCases` + `CHANGELOG.md` (the generated index self-heals). Scan9 now guards
  every id with no exemption set, so a missed CHANGELOG entry is an immediate red.
- **The sibling hunt is mandatory on every product fix.** Round 20's headline finding was that **nine** of its
  defects were "a guard exists on a sibling path and the new code did not inherit it", and W3 found a tenth
  this round. A fix is not done until every construction/emission site that asks the same question has been
  enumerated and either fixed or pinned as not-applicable, in the fixing commit.
- **H7 termination discipline:** no loop lands without a provable termination variant or a progress guard, and
  **no detection anywhere may ride a wall clock**. A test that can hang forever under a defect is a bad
  diagnostic — bound it.
- **RepoWriteGuard / ARCH-06:** every raw file-write API use in `tests/` is a registered pattern with a reason
  and a pinned count; new test projects inherit the scan on arrival — design for temp dirs only.
- **Mutation-leg integrity (binding maintainer ruling):** never narrow `mutate`, never filter the test set,
  never exclude a test project. Restated in `ci.yml` itself.
- **Ruling (b) boundary:** no new `// Stryker disable` may appear in `src/` — the count is pinned at exactly 1
  (H7's grandfathered `DocSnippetInjector` progress guard, test-infrastructure honesty, not score
  adjudication). `[ExcludeFromCodeCoverage(Justification = …)]` is a different instrument with its own
  sanctioned-category scan and exact pin; it may only use a category the maintainer has ruled in.
- **Agents verify in the FOREGROUND.** Background subagents do not reliably wake from their own background
  children in this environment (confirmed three times, and round 22 lost an agent mid-W3). The controller
  verifies completion itself — monitor plus ping — and no agent's result is assumed delivered. Round 22 lost
  nothing to this because every W-task was committed before its agent died; keep that property.
- **One task per commit** (per-item commits where a population moves), and **every tool adopted must be RUN
  with measured output in the task report** — never merely referenced.
- Populations of ten or fewer stay **exactly pinned** (`AssertRatchet` refuses a ≤10 ceiling); the matrix
  census stays 866/866 or moves with its re-measurement in the same commit.

---

## Layer 0 — arm the dormant instruments (maintainer-gated; NOT agent tasks)

**Carried forward from round 22 entirely undone**, because all three items wait on one act that no agent may
perform: pushing master with the `workflow` OAuth scope. They are listed so nothing hides, and so the first
agent of round 23 does not spend a session discovering they are blocked.

### Z1 — first real runs of the `mutation` matrix and `deep-test` jobs *(maintainer-gated)*

After the push, trigger (or let the cron fire) the four nightly jobs and verify against the first-run
checklist: `Assert-StrykerConfigSane` executed per leg, the non-vacuity guard counted scoreable mutants, the
`git diff --exit-code` clean-tree invariant held, per-leg artifacts uploaded, coverage report present.
**Why:** a gate that has never run is decoration; T5's own ledger names the nightly jobs as the UNEXERCISED
half of its work. **Verification:** per-leg hosted-runner wall-clocks recorded next to the local figures
(expect ~2–3× on 4-core runners; Stryker concurrency drops 6 → 2); all four jobs green, or each red diagnosed
to a named cause. **Exit:** one complete nightly green on master with wall-clocks in the ledger.
**Note the dependency nobody has written down: R22-05's saturation trigger cannot begin counting until Z1
arms the nightly** — "five consecutive nightly deep-count K1 runs" needs a nightly that runs.

### Z2 — Linux firsts: the ILVerify ref-pack chain and the 13 allocation pins *(maintainer-gated)*

The `deep-test` ubuntu run is the first exercise of the ILVerify ref-pack resolution chain (Windows-verified,
Linux by-construction only) and of the 13 byte-exact allocation pins (Windows-measured). If any Linux byte
differs, take the per-OS decision **in that same commit**: identical (record the measurement, single baseline
stands) or per-RID pin columns in `allocation-baseline.json`, each column measured on its OS — **never a
tolerance band**. The Dict-throughput platform caveat (2.13× Windows / ~1.14× Linux) is about throughput,
which is not gated; allocation is *expected* identical, and expectation is not measurement.
**Exit:** the gate is measured on both OSes it runs on.

### Z3 — flip `roslyn-forward-compat` off `continue-on-error` *(maintainer-gated on the first green run)*

`continue-on-error: true` → `false`, citing the first green run **on pushed master** by run id in the flip
commit. The flip is agent work; the push that enables both the observation and the landing is not.
**Why:** the leg's own comment states the obligation — *"a gate that cannot fail is decoration."*

---

## Layer 1 — the product defects (the round's core)

### N0 — the shared null-across-a-synthesized-pair ruling *(maintainer DECIDE; blocks N1 and N2)*

**What:** one ruling covering both `I5` and `I7`, because they are one question asked in two places: *when a
synthesized element/nested map sits behind a `Nullable<T>` or a nullable-capable destination, does DwarfMapper
**lift** the null, **refuse** loudly, or **document** the throw?* Specify all three branches with their cost
if wrong, in the D-section format, and take the ruling before either fix starts.

- **(a) Lift.** Generalize the lossless `NullableProject` emission that already exists in `MapEmitter` over
  re-kinded pairs and over collection/dictionary element loops: `HasValue ? Helper(v.Value) : null` on the
  value side, a null-check on the reference side. *Cost if wrong:* changes emitted output for existing
  consumers who today get an exception — a behaviour change needing a CHANGELOG `### Fixed` entry and a
  before/after measurement; the `I5` shapes emit nothing today (they do not compile), so only `I7`'s throwing
  cells are consumer-visible.
- **(b) Refuse loudly.** A new `DWARF096` (adjacent to `DWARF027`) refusing the shape until lift is
  implemented. *Cost if wrong:* a working-if-throwing shape becomes a build break; five-file sync; the
  whole-solution build gate is the over-eagerness detector.
- **(c) Document the throw as a `NullStrategy` extension.** *Cost if wrong:* the `I5` half cannot take this
  branch at all — it emits code that does not compile, which is never a documentable semantic. So (c) is at
  most a partial answer and must be paired with (a) or (b) for `I5`.

**Why:** `I7`'s row says *"decide once for both"*, and the two defects would otherwise be answered by two
agents on two days with two philosophies — exactly the endpoint-inconsistency genre they belong to.
`I7`'s current behaviour is additionally classified **UNDOCUMENTED**: `docs/options.md`'s `NullStrategy`
sentence covers *"nullable-value source → **non-nullable** target"*, and every throwing destination here is
nullable-capable. **Exit:** a ruling recorded with its cost-if-wrong, in `Issues/round20/TASKS.md` and the
round's ledger, before N1 opens a file.

### N1 — I5: synthesized element maps do not lift over `Nullable<T>`

**What:** `List<S?> → List<D?>` — and every measured wrapper: `T[]`, `IReadOnlyList<T>`, `HashSet<T>`,
dictionary value — where the element pair `S → D` needs a **synthesized** element map and the **source**
element `S` is a struct or record struct. The emitted helper loop calls `__DwarfMap_Obj_S_D(__item)` with
`__item` typed `S?`: **CS1503 in generated code, generator silent** — the `EmittedInvalidCode` genre, in the
one population this repository says must not exist (`EmittedInvalidCodeCellCeiling` is exactly pinned at 0).
Implement N0's ruling.

**Measured boundaries, from the filing — the fix must not disturb any of them:** class elements are fine
(`?` is annotation-only); non-nullable struct elements are fine; non-collection nullable nested **struct**
members are fine (the plain `S? → D?` member path already lifts); struct-source × class-dest **diverges**;
class-source × struct-dest does **not** (the implicit `D → D?` covers the write side).

**Why:** found at K0, 2026-08-22, by the type-graph smoke's **first** 25-sample run; CsCheck-shrunk, then
scoped by a 12-cell probe. Evidence: `Issues/round20/TASKS.md` row I5;
`.superpowers/sdd/2026-08-21-round22/progress.md` K0 section.

**The checklist this task owes, all in the fixing commit:**
1. **Sibling hunt** across every synthesized-map construction site — the five wrappers are five call sites,
   and the dictionary path has *two* (key and value). Enumerate them; fix or pin each.
2. **Whole-solution build**, samples included, `--no-incremental`.
3. **Five-file sync** if N0 chose (b) and `DWARF096` is minted.
4. **Delete `PinnedCorpus.NullableStructElementMap`** (corpus row `I5-nullable-struct-element-map`) **and the
   keyed sampled-space exclusion in `TypeGraphGen.Assemble`** — both are keyed to this row and both are
   red-on-fix by construction. A fix that leaves either in place is not finished.
5. **Re-run K0 and K1 at deep counts** with the exclusion gone, to prove the widened sampled space is clean.

**Verification (measured):** build 0W/0E with samples; full suite N/0 with the before/after delta stated;
census 866/866 or re-measured in-commit; `EmittedInvalidCodeCellCeiling` still exactly 0; K0 deep 250 and K1
deep 1,000 green with their **new** wall-clocks recorded (the space widened — the old figure does not apply).
**Exit:** the divergence is gone, its pins and exclusion are deleted, and the sampled space is clean at deep
count.

### N2 — I7: null across a RE-KINDED nested pair throws instead of lifting

**What:** a plain (non-collection) nullable nested member `S1? M0_0 → D1? M0_0` where the pair is
**cross-kind**. The measured table:

| Source kind → dest kind | Behaviour today |
|---|---|
| Struct → Struct | **lifts** null → null (the `NullableProject` ternary) |
| Class → Class | **propagates** null → null |
| Struct → Class | `s.M0_0 ?? throw new InvalidOperationException("Source member 'M0_0' was null")` |
| Struct → Record | same throw |
| RecordStruct → Class | same throw |
| Class → Struct | throws differently: `"Cannot map a null 'global::T.S1' to value-type 'global::T.D1'."` |

In **every** throwing case the destination (`D1?` reference, or `Nullable<D1>`) can hold the null, and the
lossless emission exists next door — `MapEmitter`'s `NullableProject` path, gated today on **both** sides
being `Nullable<T>`. Implement N0's ruling; if it is (a), the gate is what widens.

**Why:** found at K1, 2026-08-22, by the differential oracle's **first** 1,000-sample deep run, CsCheck seed
`0vihQF5Vee7b`, minimized by a 6-cell kind-pair probe. Classified **UNDOCUMENTED** — per the K1 discipline the
oracle was deliberately NOT taught the throw. Evidence: `Issues/round20/TASKS.md` row I7; the K1 section of the
round-22 progress log.

**The checklist this task owes, all in the fixing commit:** the same five items as N1, with these specifics —
the pins to delete are corpus rows `I7-nullable-rekind-value-to-reference` and
`I7-nullable-rekind-reference-to-value` plus
`DifferentialOracleTests.I7_pinned_runtime_divergence_null_across_rekinded_pair_throws` (which asserts the
**exact current throw messages** and is red-on-fix), and the keyed `TypeGraphGen.Assemble` exclusion is the
*plain member, value-kind source × reference-kind dest* one. **If the ruling is (a), `docs/options.md`'s
`NullStrategy` prose and `CHANGELOG.md` `### Fixed` both change** — the documented sentence currently describes
a narrower world than the code will implement.

**One thing to note and not paper over:** the reverse genre (Class → Struct) is unreachable in *sampling* only
because the oracle population never nulls reference members — a declared bias recorded in the
`ReflectionOracle` header. Its pin is the deterministic executor. Do not delete that pin on the grounds that
sampling covers it; sampling does not.

**Verification (measured):** as N1, plus the four throwing kind-pairs measured before and after in a table,
and K2's three relations re-run at deep counts (MR-3 re-kinds types, so it is the relation most directly
affected by a re-kinding fix). **Exit:** the divergence is gone, all three pins and the exclusion deleted, MR-3
green at deep 150 with its wall-clock.

### N3 — B28: the registry pre-sizes from `s.Count` without checking `Count` is a public instance member

**What:** `CollectionConverter.TryGetEnumerableElement` sets `CountKind.Count` the moment `ICollection<T>` or
`IReadOnlyCollection<T>` appears anywhere in `AllInterfaces`, and `MapToGenerator.CountExpr` then emits
`s.Count` as the `List<T>` capacity argument. **Implementing an interface is not exposing a member:**
`ImmutableArray<T>` implements both **explicitly**, so `s.Count` does not bind and the emitted helper does not
compile. Two failure modes, and the second is worse: in the generated file as written (no usings) it is
`CS1061`, a clean break; in a consumer project **with implicit usings on**, `System.Linq` is in scope, `s.Count`
binds to the extension **method group**, the capacity overload stops matching and the compiler reaches for
`List<T>(IEnumerable<T>)` — `CS1503`, *overload selection quietly moving to a different constructor*. The fix
is presumably a member lookup (is there a public instance `Count`/`Length`?) rather than an interface test,
plus a decision about whether `Length` is preferred where both exist. Not confined to value types.

**Why:** `Issues/round20/TASKS.md` row B28, found by A13. It is the **same genre as N1** — generated code that
does not compile, generator silent — and `TypeFacts.cs`'s own doc comment names the class: *a predicate correct
only for the inputs that reach it today*. **Flagged as a source contradiction, not silently patched:** the
round-22 plan's Layer 4 table claims to disposition "every non-`DONE` row of `Issues/round20/TASKS.md`" and
**B28 is absent from it** — the only such omission. It has never been dispositioned. Layer 1 is its honest
home because it is a product defect of the round's own genre.

**Verification (measured):** an `ImmutableArray<T>`-membered fixture that reproduces both failure modes today
(one with implicit usings on, one off) and compiles after; a `Length`-vs-`Count` decision recorded with its
reasoning; the class-engine sibling checked — it reads the same helper — and fixed or pinned; whole-solution
build with samples; suite delta stated. **Exit:** B28 flips DONE with the two before/after compile results.

### N4 — I6: the housekeeping AOT stage's NETSDK1207 *(one line; the maintainer's blessing, then trivial)*

**What:** delete `-p:PublishAot=true` from `scripts/housekeeping.ps1` L312. The flag sets a **global** MSBuild
property that flows down the whole publish graph and hits the two netstandard2.0 projects
(`DwarfMapper.Generator`, `DwarfMapper.CodeFixes`) with NETSDK1207. `ci.yml`'s own `aot-trim-gate` comment
(L362–363) states the mechanism verbatim and CI therefore relies on the **csproj-level**
`<PublishAot>true</PublishAot>` — **which AotBench's csproj already carries (L5)**. So the flag adds nothing but
the failure.

**Why:** row I6, found at K0's closeout — the first full `housekeeping.ps1 -Coverage` run of round 22.
Reproduced **identically** at the round-22 branch tip and at the branch base `73c58c3` on master: no round-22
commit is the cause, it is pre-existing, likely SDK-side tightening since the 10.0.101 pin (ISSUE-038,
2026-08-11). Stages 1/1b/2 of the same run all passed; only stage 3 breaks.
**Verification:** `housekeeping.ps1 -Coverage` stage 3 green with the published binary asserted NativeAOT and
the behavioural gate run; the AOT output compared against CI's, which never had the flag.
**Exit:** the local pipeline runs end to end for the first time this year; I6 DONE.

### N5 — I4: widen the mutation decontamination sweep

**What:** after any Stryker leg, string-scan **every** `DwarfMapper*.dll` under `tests/**/bin` against the
src-built originals — not just the leg's own target DLL beside its `*.stryker-unchanged` backups. P5 found six
test bins (`ConsumerTests/CleanCorpus`, `ConsumerTests/Host`, `CorpusTests`, `DifferentialTests`,
`IntegrationTests`, `Testing.Tests`) each carrying a mutated `DwarfMapper.Generator.dll` **with no backup
marker** — Stryker backs up only what it overwrites, and these bins had no pre-run copy (analyzer-only
reference, no CopyLocal), so restore never touched them and `RepoWriteGuard`'s leftover-backup signal cannot
fire there. **Hazard:** an incremental build keeps the newer mutant and those suites then exercise a mutated
generator, silently green.

**In-task ruling, defaulted and reversible:** fold the sweep into `scripts/housekeeping.ps1 -Mutation`, which
already owns `Assert-MutantsWereTested` and is where the precedent lives (T3-H1 NOTE 3); the alternative —
extending `RepoWriteGuard`'s detection — is recorded with why it was not chosen. **Why:** row I4, filed at P5
with the six bins named and the mechanism verified. **Verification (measured):** sabotage demo — plant a
string-matching DLL in one of the six bins, the sweep fails naming the file; remove it, green. Post-leg
`git status` clean. **Exit:** no mutated product DLL can survive a leg unnoticed; I4 DONE.

### N6 — B37: a declared `Arguments` list collapses the matrix's ×2 multiplicity axis

**What:** `SurfaceCatalog.ArgumentsFor` returns `ExpandArguments(declared)` **without consulting `variant`**,
so the second application of a declared-arguments element is byte-identical to the first — precisely the
degeneracy the `SampleArgument` doc comment says the variant dimension exists to prevent (*"Variant must vary
so the multiplicity axis renders two DIFFERENT applications"*), fixed there and left unfixed one method up. It
affects every element with a declared list: both `[MapValue]` forms, `[Flatten]`, `[FlattenGraph]`,
`[MapCollectionKey]`, `[MapIgnoreSource]`, `[Reinterpret]`, `[MapDerivedType]`, `[AutoNest]`. The fix is to
rotate the `{Member}` placeholders by variant the way `SampleArgument` rotates its names.

**Why:** row B37, filed at W2 while re-measuring B25 — the probe fix that made those cells measure the
directive is what exposed the flattened axis. **Not maintainer-blocked.** It is placed in Layer 1 rather than
Layer 4 because it moves renderings across the matrix and therefore needs **its own re-measurement commit**,
not a ride on someone else's. **Verification:** the census re-measured in the same commit with every moved
population stated; any newly-differing cell judged on its merits, not absorbed. **Exit:** B37 DONE; the ×2 axis
asks two questions again.

---

## Layer 2 — the 97 % program, round-23 slice

**State the arithmetic honestly first, because the kill-list era is over.** Round 22's P2/P3/P5 killed the
enumerated holes on all three legs. What remains on each leg is **maintainer-gated at the margin** — the next
mutant on every leg needs a ruling, not a test. This is a feature of the program working, not a stall: R2's
mandatory-raise engine already exists and already forces every floor to chase its measurement, so Layer 2 is
now small and honest rather than large and busy.

| Leg | Raw now | rawCeiling | What actually stands between them |
|---|---:|---:|---|
| runtime | 96.46 | **98.23** | `I1` — `Key.Equals(object)`, the last NoCoverage mutant, honestly uncoverable without a product change (`private` → `internal` + IVT, or a denominator ruling). Killing it is ≈ **97.34** raw. |
| generator | 81.59 | **88.05** | 3 killable mutants (`EquatableArray.GetHashCode` ×2, `IsSourceSequential`'s `Any → All`) → ≈ **83.08** raw max. The rest is the 6 probably-equivalent survivors and the 3 dead-code-question NoCoverage mutants — **research Q2**, a maintainer denominator ruling. |
| DocTooling | 95.42 | **96.47** | **Exactly the three `I2` mutants** (`SnippetScanner.ScanAll`'s `OrderBy → OrderByDescending` and `"*.cs" → ""`, `ExampleCatalogue.Scan`'s `"*.cs" → ""`). No `I2` ruling, no movement. |

**Say this in the gate comments and never let it drift:** the research's oft-quoted *"generator ≈ 89.8
ceiling"* is `167/186` — an **adjudicated-denominator** figure that predates ruling (b). It is **not a raw
number and no raw gate may be set from it.** The equivalents ledger says so in its own prose; keep it there.

The research's §3 R23 column is **stale** and is used with correction: it expects runtime "~90s" and DocTooling
"~90" for R23, and both legs already exceed their own R24 expectations. Flagged, not resolved here.

### M1 — B7 + B32: the last two matrix excuse obligations *(the research's own R23 staging)*

**What:** (1) **B7** — the `CrossAssembly` obligation is **placement-blind**: a row confined to one project
satisfies the one category whose entire claim is that it is only observable *across* assemblies. Make the
obligation check the placement, the way B3's `NotApplicable` re-classification now checks observability.
(2) **B32** — a `[DwarfSurfaceSite]` narrowing is validated for **form** and counted by **nothing**:
`SurfaceCatalog.ValidateSiteClaims` checks that a claim names a legal site, does not overlap, states a reason
and does not restate the default — all shape, no limit. `StructurallyExcusedCellCeiling` structurally cannot
see these (`SurfaceParityTests.StructurallyInapplicableOption` parses `Name=value` and returns null for any
directive axis — `ctor(0)` has no `=`). Two things to decide and pin: a **counted population for dropped
(element, site, endpoint) triples**, and whether the premise-pin (a test asserting the narrowing's own premise,
so it fails when the dropped endpoint starts acting) becomes a **convention the catalogue enforces** rather
than a habit.

**Why:** research §2.4 stages B7 + B32 as the R23 half; the round-22 disposition table defers them here by
name. Closing them takes the matrix dimension to its real ceiling — **100 % of excused rows
obligation-backed** — after which a percentage target adds nothing. Note B32's own filing: the A14 brief
*predicted* the narrowing would raise `StructurallyExcusedCellCeiling` and it structurally could not, so the
accounting gap is already misleading readers.
**Verification:** each new guard sabotage-demoed red once (a same-project `CrossAssembly` row; a narrowing
added without its premise-pin; an offsetting swap inside the new triple population) then green; census
866/866; every new population exactly pinned in its measuring commit.
**Exit:** B7 and B32 DONE; every excuse category in the matrix carries a live, re-measured, exactly counted
obligation.

### M2 — the generator leg's three remaining killable mutants → break to measured

**What:** kill `EquatableArray.GetHashCode` (×2) and `BlittableProof.IsSourceSequential`'s `Any → All`, the
three mutants the equivalents ledger names as the currently-killable set. Re-run the leg on a quiet machine
with no concurrent builds; move `break` **and** `low` to the measured floor in the same commit (Stryker refuses
`low < break` with exit 0 — H1's lesson, now guarded by `Assert-StrykerConfigSane`).
**Why:** `Issues/ledgers/equivalent-mutants.md` per-leg arithmetic; `T3-mutation-survivors.md`'s kill-first
ranking. `IsSourceSequential` is the auto-blit **safety gate** — an unsafe *accept* is the worst failure mode
in this repository, so this one is worth killing on its merits regardless of the score.
**Verification:** per-mutant `killedBy` confirmed in the report JSON; `Timeout` still 0; post-run
`git status` clean **and** N5's widened sweep clean; measured score and wall-clock reported against the 21:19
baseline. **Expect ≈ 83 raw — a neighbourhood, not a value to type in.**
**Exit:** the three named mutants Killed or ledger-adjudicated with proofs; `break`/`low` = the new measured
floor; the ledger's denominators recomputed in the same commit if any moved.

### M3 — the contingent leg tasks, pre-specced so a ruling can be executed the day it lands

**Each of these is BLOCKED on a maintainer ruling listed in the maintainer-only section. Do not start one
before its ruling; do specify it now, so the ruling costs a commit and not a design session.**

- **M3a — I1 (runtime).** If the ruling is (a): `private` → `internal` on `DwarfMapperRegistry.Key`
  (`InternalsVisibleTo("DwarfMapper.Generator.Tests")` already exists — the same internal-plus-IVT pattern the
  house constructor stance prefers over reflection), plus a contract test pinning equal pair → `true`,
  half-matching pair → `false`, non-`Key` object → `false`, `null` → `false`. If (b), a denominator ruling in
  the spirit of D-b — noting the override **cannot simply be deleted** (CA1067 demands `Equals(object)` when
  `IEquatable<T>` is implemented). Either way, re-measure and move `break`/`low`; expect ≈ **97.34** raw.
- **M3b — I2 (DocTooling).** If the seam is approved: the composition roots (`SnippetScanner.ScanAll`,
  `ExampleCatalogue.Scan`) take an **injectable file list**, with the current `Directory.GetFiles` reduced to a
  one-line default argument — the seam `Build` already has. The three mutants then die test-side with no file
  planted in the real tree (banned; `RepoWriteGuard` exists to stop exactly that) and no `RepoLayout.Root`
  redirect (banned; a redirect changes what the leg proves). Re-measure; expect the leg **at its 96.47
  rawCeiling**, which would make DocTooling the first leg to reach its own asymptote.
- **M3c — I3 / research Q3 (coverage denominator).** If the maintainer admits the *defensive unreachable arm*
  category, the scan gains a second sanctioned string and the exclusion pin moves 9 → 10 in the same commit,
  and `MapToAttribute` leaves the denominator (runtime floor 91.2 → ≈ 92.4). If instead the `??` arm is
  deleted — `params Type[]` is never null from attribute syntax, and nullable annotations make
  `new MapToAttribute(null!)` a caller bug — the class becomes a clean fit for the **existing** category and no
  new category is minted, which is the smaller change. If neither, the 4 lines stay an honest hole priced into
  91.2 and the row says so.

**Verification, all three:** the leg or the coverage run re-measured on a quiet machine, floors and breaks
moved in the measuring commit, R2's band demonstrated green afterwards.
**Exit:** each ruled item executed within one commit of its ruling, or explicitly still blocked with the date.

### M4 — I9: pair-scoped `[MapValue<TTarget>]` at UpdateInto and Projection *(blocked on a ruling; specced)*

**What:** `MatchPairValues` is consulted at exactly **three** sites — the create-map path
(`MapperExtractor.cs:1077`), the `[GenerateMap]` pair path (`:1417`) and the nested-pair path (`:1607`) — and
at neither the update-into nor the projection branch. So the pair **is** mapped and the directive simply is not
read there, while `DWARF056` tells the caller it *"matches no mapped pair"*, sending them after a missing
`[GenerateMap]` that is not the problem. **(a) Thread it** — read the pair-scoped form at both endpoints, the
D9-shaped answer; changes emitted output for existing consumers, so it needs its own before/after measurement.
**(b) Re-attribute it** — report `DWARF092` (*"the directive is not read at this mapping endpoint"*), which is
what the situation actually is and what `[FlattenGraph]` already reads at those same two endpoints; wording
plus a five-file-adjacent change, and it moves matrix readings.
**Why:** row I9, filed at W2 — the examined half of B25's "unexamined claim". Recorded rather than guessed
because *examining it is all round 22's budget bought*.
**Exit:** the ruling executed with the moved cells re-measured in the commit.

### M5 — B22 and its riders: the `DWARFR` family wording gate *(an r23 arc of its own, as round 22 named it)*

**What:** **no `DWARFR##` message or remedy is pinned anywhere** — the whole registry family, not one id. Every
`DWARF0xx` id has its wording held by `DiagnosticMessageContractTests` and `DiagnosticProseIsCurrentTests`, its
announcement by `Scan9`, and its `NegativeCases` row by `DiagnosticCoverageRatchetTests`. The `DWARFR` family is
**excluded from all of them by written convention** (`AssemblyScanTests.cs:74-80` states the Scan9 exclusion;
`docs/diagnostics.md` declares `[MapTo]` a prototype tier exempt from the DWARF0xx scans). Its only gate is
`RegistryDiagnosticsGenTests`, which checks each id is *triggered* and *mentioned* — neither is a check on
wording. A `DWARFR` message can be rewritten, have its remedy inverted, or stop naming a fix at all, and
nothing fails.

**The decision is the arc:** fold `DWARFR##` into the `DWARF0xx` scheme (`RegistryDiagnostics.cs` has said *"a
later unification could fold them in"* since it was written — the larger change, settles it once), or give the
family its own wording gate. **Round 22 made this sharper, not softer:** W1 minted `DWARFR12` and W3 **retitled
`DWARFR09` and rewrote its `MessageFormat` to `"{0}"` composed per site** — a family-wide wording change that
no gate could see.

**Riders that ride on this decision and are folded here rather than deferred again:** **B16** (`DWARF088`'s
message calls a co-located `[GenerateMap]` host *"this mapper class"* — true in `ExtractCore`'s sense, false to
the reader who wrote a plain DTO); **B35** (`DWARFR04` co-fires with `DWARFR01` on an invalid `[MapTo]` target,
and *the filed remedy was wrong* — `targetCount` is computed before `IsMappableTarget` filters, so the proposed
guard cannot work; **a different remedy is needed and none is proposed yet**); **B36** (`DWARF090` and
`DWARF092` both mean *"the directive is not read here"* and split by DIRECTIVE rather than by OUTCOME — decide
whether the split is worth stating in both prose entries).
**Verification:** whichever mechanism is chosen, sabotage-demoed — invert one `DWARFR` remedy sentence, the
gate goes red naming the id. Two concrete instances settled in the same task: `DWARFR10`'s `[MapIgnore]` remedy,
which at the registry ignores the *source* member and leads straight to `DWARFR02`; and `DWARFR02`'s own text,
which offers the fix a `DWARFR10` reader was just sent away from.
**Exit:** B22 DONE with B16/B35/B36 dispositioned by the same ruling.

---

## Layer 3 — the r23 pressures (the research's adopt-later rows, plus one the maintainer added)

Every row here comes from `Issues/round22/RESEARCH-97-PERCENT-GATES.md` §4's decision table marked
**adopt-later (r23)**, except S7 which the maintainer requested directly. **Nothing enters the nightly without
a measured wall-clock first** (the round-21 rule; ruling (c) removed the arbitration, not the measurement), and
every cost below is an **expectation to be replaced by a measurement**, never a value to type into a gate.

### S2 — wall-time regression: alert-only, never a per-PR gate

**What:** `github-action-benchmark`'s BenchmarkDotNet adapter, `alert-threshold` ~150 %, **comment-only** —
never `fail-on-alert` per-PR. **Why:** wall-clock is R4-nondeterministic on shared runners; industry practice
is baseline-plus-generous-threshold, not exact. **The caveat that must be written into the job:** the
Dict-vs-Mapperly throughput figure is **platform-dependent** (2.13× Windows / ~1.14× Linux) and Linux CI numbers
must never be quoted as the Windows figures. The 3.29× allocation lead is platform-independent and already
gated exactly by T8.
**Expected cost:** ~5–10 min nightly. **Exit:** the leg alerts and never fails; the platform caveat is in the
job's own comment.

### S3 — generator compile-time cost: time-to-first-emit, per-1000-mappers scaling

**What:** a `GeneratorDriver`-based benchmark in the benchmark project; gate = **ratio against a pinned
baseline** (e.g. fail above 1.5×), deep tier; and assert the incremental re-run is **cached** (R22-03 already
pins per-step). **Why:** a real consumer-facing cost with no gate today — and **K0's renderer makes a
1,000-mapper corpus generatable on demand**, which is the enabling piece the research was waiting for. Reuse
K0; do not build a second renderer.
**Expected cost:** unmeasured — **measure before landing**; estimated low minutes.
**Exit:** the ratio gate live in the deep tier with its baseline measured and annotated `{value, run, commit}`
per R1.

### S4 — reproducible-build verification

**What:** `ContinuousIntegrationBuild=true` plus a build-twice-compare-hashes leg (or `dotnet-validate`),
nightly. **Why:** it fits the CRA-defensive posture this repository has taken deliberately — an SBOM plus SLSA
attestation without bit-reproducibility is a claim without a check.
**Expected cost:** ~2 min nightly. **Exit:** two builds of the same commit produce identical hashes, or the
non-determinism is named and recorded.

### S5 — cross-platform nightly legs (windows-latest, macos-latest) and the preview-SDK canary

**What:** two rows, one task because they are the same CI shape. (1) nightly full-suite legs on
`windows-latest` and `macos-latest` — not per-push (runner cost and wall-clock). Correctness is de-facto
dual-platform today (local dev Windows, CI ubuntu, AOT gate both), so macOS is cheap insurance rather than a
discovery. (2) a **.NET preview-SDK canary**: a clone of `roslyn-forward-compat`'s shape,
`continue-on-error: true` until first seen green, then flipped — that leg's own comment states the discipline
and Z3 is the precedent. **Why:** a source generator's worst consumer-facing failure is *"the new SDK refuses
to load it"*.
**Expected cost:** ~10 min each nightly, ~5 min for the canary. **Exit:** both legs live with measured
hosted-runner wall-clocks; the canary's flip obligation written into its own comment with the condition.

### S6 — package/binary size ratchet

**What:** `dotnet pack` in the deep tier; ceiling = the measured KB truncated, raise-only-with-re-measure.
**Why:** pre-1.0, and ApiCompat plus the `PublicAPI.Shipped/Unshipped` files already guard *surface* growth;
size is a cheap secondary signal that catches what surface checks cannot (an accidentally embedded resource, a
dependency that started shipping).
**Expected cost:** seconds. **Exit:** the ceiling pinned with its measurement annotation.

### S7 — generated quality badges, byte-compared *(maintainer-requested)*

**What:** render the coverage floors (from `scripts/housekeeping.ps1`'s `$coverageFloors`) and the three
mutation `break` values and raw scores (from `stryker-config*.json` plus
`Issues/ledgers/equivalent-mutants.md`) into a **marker-delimited region in `README.md`** through the existing
DocTooling injection mechanism — `README.md` already carries 18 `<!-- snippet: … -->` / `<!-- endsnippet -->`
regions that are rendered and **byte-compared** by the doc tests. The badges are plain static shields.io
markdown with the numbers substituted:
`![coverage](https://img.shields.io/badge/coverage-91.2%25-brightgreen)`. No external service, no token.

**Why:** master already carries self-updating badges (`6244f3a` — CI status, NuGet prerelease, downloads, TFM,
license) precisely because they read live sources and cannot rot. **Coverage and mutation badges were
deliberately left out**, because a hand-typed number rots the moment a floor moves — which is exactly the drift
class this repository exists to prevent. Generating them through the byte-compared injection mechanism converts
that rot into a **failing build**. (Also deliberately omitted and to stay omitted: a `DwarfMapper.Testing`
package badge — P4 verified it has never been published to nuget.org, so it would render *not found* — and a
nightly/scheduled-run badge, because the nightly jobs stay dormant until Layer 0's push.)

**Four constraints, all binding:**
1. **Wait for round 22's W4 (B23) to land.** DocTooling is being edited there right now — `ApiReferenceRenderer`
   gains `LoadOptions.PreserveWhitespace` and the whole generated corpus is regenerated. Landing a second
   DocTooling change across that is how a reviewed diff becomes an unreviewable one.
2. **Derive the colour thresholds, do not hand-assign them** — a hand-assigned colour is a second thing that
   rots. Derive from the gate's own semantics (e.g. at-or-above floor, and the R2 band's `floor + q` boundary),
   in one place, with the derivation commented.
3. **Read the numbers from the gates' own files**, never from a copy. The whole point is that the badge and the
   gate cannot disagree.
4. **The external-service alternative is not the agent's to choose** — Codecov or the Stryker dashboard are a
   third-party-service-plus-token decision, listed in the maintainer-only section as an option.

**Verification (measured):** doctor one badge number by hand → the doc test goes **red** naming the region
(sabotage demo, reverted); move a floor → the regenerated README changes in the same commit; the whole
generated-docs corpus stays byte-identical otherwise.
**Exit:** every quality number in `README.md` is generated, byte-compared, and impossible to leave stale.

---

## Layer 4 — the swept task-list remainders

Every row of `Issues/round20/TASKS.md` **not `DONE` at r22 tip `adbe9a3`**, dispositioned. FOLD = a task above;
DEFER = round 24+ with the reason stated; MAINT = maintainer-only, listed in the final section.
**W4–W7 had not landed when this table was written** — see the reconciliation note beneath it.

| Row | Disposition | Where / why |
|---|---|---|
| B4 | FOLD → V1 | round-22 W5's batch; not landed at the time of writing — reconcile |
| B5 | FOLD → V1 | W5's batch; also the third of the excused-row family (B5/B7/B32) the final review named |
| B7 | FOLD → M1 | the research's own R23 staging: obligation completeness |
| B8 | FOLD → V1 | W5's batch |
| B10 | FOLD → V1 | W5's batch |
| B12 | FOLD → V1 | W5's batch |
| B13 | FOLD → V1 | W5's batch |
| B14 | FOLD → V1 | W5's batch |
| B16 | FOLD → M5 | wording; rides on B22's family decision, which is now a task rather than a deferral |
| B17 | FOLD → V2 | cosmetic dead helper in generated output — one-line tidy-up, no behaviour effect; folded rather than deferred a third time |
| B18 | FOLD → V3 | member-form directive on a `[DwarfMapper]` class member is swallowed; needs its own diagnostic decision + five-file sync — a real product silence, of the same genre W1 closed for `[MapIgnore]` |
| B19 | FOLD → V2 (doc half) | round-22 W6 owed the written limitation; if W6 landed it, this row closes there — reconcile. The systemic remedy IS K1/K2, now live |
| B22 | FOLD → M5 | round 22 called it "an r23 arc of its own"; it is now that arc |
| B23 | FOLD → W4 (r22) | **round-22 work, may already be done** — reconcile; S7 is blocked behind it |
| B24 | FOLD → V3 | contradicting `[MapNullSkip<S,T>]` silently discards the second; new diagnostic + five-file sync; unreachable by the matrix (its ×2 axis renders identical applications — which is **B37/N6**'s subject, so N6 must land first or the row stays unmeasurable) |
| B28 | FOLD → **N3** | **NEVER DISPOSITIONED — omitted from the round-22 Layer 4 table.** A product defect of N1's genre; Layer 1 is its home |
| B29 | DEFER | changes WHICH constructor existing projections call; the honest fix is teaching `ConstructorSelector` that object-initializer construction is unavailable for the target, not a second filter — needs its own before/after measurement round |
| B31 | FOLD → V3 | `[DwarfMapperConstructor]` on an unusable constructor is silently ignored; new id + the two-messages design question (is *unusable* a different message from *absent*?). Batched with B18/B24 because all three are "a new id for a silent discard" and one design session answers the shape for all |
| B32 | FOLD → M1 | with B7, per the research staging |
| B35 | FOLD → M5 | no viable remedy proposed yet; the family decision is where a new one comes from |
| B36 | FOLD → M5 | prose-only; rides on B22 |
| B37 | FOLD → **N6** | needs its own re-measurement commit |
| C5 | FOLD → V2 | the open-coded `AssertRatchet` + the private repo-root walk `RepoPaths` exists to replace; round-22 W6's remainder — reconcile |
| D-a | MAINT | ruled (keep + document); the `docs/options.md` write awaits the word |
| D-c | MAINT | edits the agent's own instructions (`CLAUDE.md`) — called out, never done quietly |
| D-f | MAINT | a design ruling: does `[MapTo]` participate in ambient registration at all |
| F1 | FOLD → V2 | reality closed it (merged `dc385d4` / `d131c76`); flip with evidence — round-22 W6's job, reconcile |
| F2 | FOLD → V2 | the round-21 ledger was captured at `96e62f9`; **the round-22 SDD progress log is the same hazard and is still git-ignored — capture it before that worktree is removed** |
| F3 | MAINT | where plan documents live is a repository-layout preference |
| H3 | FOLD → W7 (r22) | Meziantou phase 2 — round-22 work, reconcile |
| H8 | MAINT | the durable-form decision (document the pwsh / reportgenerator / ilverify prerequisites, and/or add a BOM); once ruled the edit is one small task |
| I1 | MAINT → M3a | ruling first, then the pre-specced task |
| I2 | MAINT → M3b | ruling first (a product-shape decision), then the pre-specced seam |
| I3 | MAINT → M3c | ruling first (research Q3's category), then the pre-specced exclusion or deletion |
| I4 | FOLD → **N5** | agent task with an in-task default ruling |
| I5 | FOLD → **N1** | the round's core |
| I6 | FOLD → **N4** | one line, blessed |
| I7 | FOLD → **N2** | the round's core |
| I8 | MAINT (record-only) | no license-compatible fix exists; the standing reasoned suppressions ARE the disposition. **No action** unless the maintainer revisits the benchmark competitor set |
| I9 | MAINT → M4 | ruling first, then the pre-specced change |
| K3 | DEFER (open research) | round-22's static-mutant attribution research **did not run** — no entry in the round-22 progress log. Per ruling (c) it is a wall-time optimization, no longer an enabling constraint: 85 of the generator leg's 201 mutants are `static`, ~14 min of the leg. Carry it as a low-priority research row |

### V1 — the small-guards batch remainder *(reconcile first)*

**What:** whichever of B4, B5, B8, B10, B12, B13, B14 round-22 W5 did not land: the legal same-source
`[FlattenGraph]` shape pinned (B4); the fixture-baseline gate with its four `DWARF001`-by-design exceptions
named (B5); one shrink-only guard over `DiagnosticTestAllowlist` (B8 — note `PredatesTheChangelog` is **gone**,
drained by D-e and deleted at `73c58c3`, so B8's scope has halved and the row's own text is stale); the
`CorpusFor` throwing default arm reached (B10); the Reasons/`StructurallyInapplicable` double-count forbidden
(B12); evidence links **resolved**, not shape-checked (B13); the `IsGeneratorAuthored` `*.g.cs`-collision
remarks completed (B14).
**Verification:** every new guard sabotage-demoed red once. **Exit:** all remaining rows DONE.

### V2 — records hygiene and the stale-status sweep *(reconcile first)*

**What:** whatever of round-22 W6 remains — C5's two items (the open-coded `AssertRatchet`, the private
repo-root walk), B19's written limitation *where the ceilings are read*, and the truth-sweep of
`Issues/round20/TASKS.md`. **The sweep has grown since W6 was written:** the NOW section still describes the
pre-merge branch and quotes `EmittedInvalidCode` **10** when it is **0**; the ratchet table still lists
`PredatesTheChangelog` **76** when the baseline is **deleted**; F1/F2 still read TODO; the "Machines" table's
`PredatesTheChangelog` and mutation-survivor rows are stale; the F1 merge-handoff section describes a merge
that happened. **Statuses only — no history rewritten.** Plus B17's one-line dead-helper tidy-up.
**Exit:** the task list tells the truth at a glance; C5, B19, B17, F1, F2 DONE.

### V3 — the three "new id for a silent discard" rows, decided as one shape

**What:** B18, B24 and B31 are the same question three times — *the caller wrote something, the generator
declined to act on it, and the build said nothing* — and each was deferred separately for the same reason (a
new id needs the full five-file sync). Answer the **shape** once: does an unread/unusable/contradicted
directive earn a `DWARF056`-family "matched nothing" report, a `DWARF092`-family "not read here" report, or its
own id? Then implement all three under that answer, per-item commits.
**Why:** this is precisely how W1 settled B15/B20/B21 — one policy, three rows, one commit — and it worked:
`DWARF095` + `DWARFR12`, whole-solution build clean, no population moved. B18 additionally has a test sitting
over the shape today (`CoLocatedHostMemberDirectiveTests.A_DwarfMapper_class_declaring_a_pair_does_not_read_its_own_members`)
whose remarks say it does **not** bless the behaviour — so the pin already exists and only needs inverting.
**Verification:** five-file sync per minted id; whole-solution build with samples (the over-eager-refusal
detector); matrix re-measured. **Exit:** no directive form in these three shapes is silently inert; B18, B24,
B31 DONE.

### The reconciliation obligation — read this at round-23 start

**This plan was written before round 22's W4–W7 completed and before the round-22 branch merged.** W1
(`390936d`, `6cbc902`), W2 (`b53e6f8`, `e79f99d`) and W3 (`60d8d8c`, `adbe9a3`) had landed and were verified;
**W4 (B23, the renderer whitespace fix), W5 (the small-guards batch), W6 (records hygiene + the stale-status
sweep) and W7 (H3, Meziantou phase 2) had not.**

**Do not guess their outcomes.** The first act of round 23 is to re-read `Issues/round20/TASKS.md` and the
round-22 progress log at the merged tip, and **reconcile Layer 4 against reality**: every row above marked
"reconcile" either closes (delete it from V1/V2, cite the commit) or survives (keep it, with the reason it
survived). Re-verify the state-at-round-start numbers at the same time — the suite count, the census, and any
floor W4–W7 moved. Then, and only then, start N0.

---

## Maintainer-only (not agent tasks — listed so nothing hides)

- **The master / `ci.yml` push with the `workflow` OAuth scope.** All of Layer 0 is gated on this one act, and
  R22-05's saturation trigger cannot begin counting until it happens.
- **The round-22 merge.** This plan assumes it. Until it lands, master (`6244f3a`) has none of round 22 and
  every figure in the state header is a branch measurement.
- **N0 — the shared null-lift ruling (lift / refuse / document).** The single highest-value decision of the
  round: it unblocks both N1 and N2, which are the round's core.
- **Research Q2 — the generator dead-code rulings.** `BlittableProof` L29–L30's apparently unreachable `true`
  return; `ConstructorSelector` L281/L285 (provably dead per the ledger); the L88 `useObjectInitializerOnly`
  flag. Deletion shrinks the denominator honestly; adjudication keeps the code and pins the proof. **The
  generator leg cannot pass ≈ 83 raw without these** — it is the difference between 83.08 and 88.05.
- **Research Q3 / I3 — further sanctioned coverage-exclusion categories.** Is the *defensive unreachable arm*
  admitted, or is it a hole by definition? M3c is specced for either answer, including the third option
  (delete the `??` arm and use the existing category).
- **Research Q4 — branch coverage:** accept "informational until proven stable" (R4, the plan's default), or
  fund the 3-run variance probe now?
- **Research Q5 — R2's quantum:** is one mutant / 1.0 pp the right forcing step, or may a round bank more than
  one quantum before the raise is forced? The plan defaults to one.
- **R22-05's trigger N** — the plan defaults to 5 consecutive clean nightly deep-count K1 runs; the trigger
  stays a **counted condition**, never a judgement call, whatever N becomes.
- **I1, I2, I9** — the three rulings M3a, M3b and M4 wait on.
- **I8 — record-only.** NU1903 High on AutoMapper 14.0.0 has **no license-compatible fix** (both fix versions
  are RPL-1.5, GPL-incompatible per the pin comment). It fires in exactly three non-shipped projects, each with
  a pre-existing reasoned `NoWarn`, and S1 verified the gate has teeth behind them (strip one → restore fails
  exit 1). No action unless the benchmark competitor set is revisited — dropping AutoMapper is the only way to
  clear the row.
- **H8's durable form** — document the `pwsh` / `dotnet-reportgenerator-globaltool` / `dotnet-ilverify`
  prerequisites where a cold reader finds them, and/or add a UTF-8 BOM so Windows PowerShell 5.1 at least
  parses `housekeeping.ps1` instead of dissolving on an em-dash. The decision is the maintainer's; the edit
  afterwards is trivial.
- **D-a, D-c, D-f, F3** — per the Layer 4 table.
- **S7's alternative** — Codecov and the Stryker dashboard would give live badges instead of generated ones, at
  the cost of a third-party service and a token in CI. Listed, not chosen.
- **The equivalents ledger's own discrepancy flag** — the generator proven count was carried as "15" by the
  research and the round-22 plan; the ledger's two independent tallies force **16**, and P5's re-measure took it
  to **24**. The ledger flags this rather than silently correcting it, and asks for a maintainer recount
  against a fresh report. Carry the flag forward until it is answered.

## Ordering

**Layer 0 is maintainer-gated and blocks nothing else.** Inside the agent's queue:

1. **Reconcile Layer 4 first** (the obligation above) — before any task opens a file. It is cheap and it
   prevents redoing W4–W7.
2. **N0's ruling** is requested at once, because N1 and N2 are the round's core and both wait on it. While it is
   outstanding, run the tasks that need no ruling: **N4** (one line), **N5** (sweep), **N6** (matrix axis), and
   **V1/V2** (whatever the reconciliation leaves).
3. **N1 then N2, one task each, one commit each, sequentially** — they touch the same emission paths
   (`MapEmitter`'s nullable handling, the synthesized-map queue) and both delete keyed exclusions from the same
   `TypeGraphGen.Assemble`. Parallelizing them across worktrees would produce two conflicting widenings of the
   same sampled space. **N3** (B28) may run in parallel: it is the registry front door, a disjoint file set.
4. **M2 and M1 are independent** of the N-track and of each other; M2's leg re-measure **serializes on the quiet
   machine** with every other mutation run (the T3/H5 precedent: no concurrent builds during a Stryker run).
   Note that **N1/N2 may move the generator leg's score** by changing emission — so M2's re-measure should come
   *after* the N-track lands, or be re-run if it does not.
5. **M3a/M3b/M3c/M4/M5 unblock on their rulings**, in whatever order the rulings arrive.
6. **The S-track is free-standing** and cheap to parallelize, with two exceptions: **S7 waits on round-22 W4**,
   and every S-task that adds a nightly leg must produce its measured wall-clock before the leg is wired.
7. **V3** last of the V-batch: it is a five-file-sync product change and wants the whole-solution build gate on
   a quiet tree.

**Nothing lands in the nightly without its measured wall-clock. Every floor that moves, moves in the commit
that re-measured it. Every product fix carries its sibling hunt, and no pin or exclusion outlives the defect it
was written to describe.**
