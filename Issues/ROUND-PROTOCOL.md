<!-- SPDX-License-Identifier: GPL-2.0-only -->

# The round protocol

How DwarfMapper.NET is developed, written as a goal that an agent can pursue unattended without stepping outside
the rules this repository has built up. None of the rules is new. Each is distilled from the rounds recorded in
`Issues/`, `Issues/ledgers/` and `docs/superpowers/`, or from the gates those rounds left behind, and each section
names its source.

What is new is only the machinery for running those rules unattended:

- the approval line in a checklist (§1.1);
- a reviewer with a fresh context standing in for [O] (§4);
- the `[!]` mark (§6);
- the goal texts (§9).

> **This file holds no counts and no status.** A figure written here would be stale within a round, and that is
> why the previous instruction file for agents was deleted (`40594cd`). Figures live in the files that enforce
> them: `scripts/gate-checks.ps1`, `scripts/housekeeping.ps1`, `stryker-config*.json`,
> `Issues/ledgers/equivalent-mutants.md` and the test sources. This file names mechanisms only (§11), and it
> changes only by owner ruling, in a commit that says so.

1 the pattern · 2 the goal · 3 invariants · 4 decision rights · 5 procedures · 6 STOP and escalation · 7 evidence ·
8 environment · 9 the goals to paste · 10 amending · 11 mechanisms named here

## 1. The pattern

Every round does one thing. It takes claims that nothing enforces yet: a defect an audit found, a guarantee the
documentation makes, a performance idea, a gate that could be fooled. Each one becomes either a mechanism that
fails loudly when the claim stops being true, or a recorded decision not to build one. Whatever a round cannot
finish becomes measured input to the next.

The result is a **ratchet**: the set of executable guarantees only grows, and nothing once reported loudly becomes
silent. Every gate is one tooth of it:

- generated output is locked byte for byte;
- allowances may only shrink;
- mutation and coverage floors are never lowered to get green. Round 30 removed the mutable source instead, and
  round 31 built `Dwarf.Map` rather than lower the runtime leg's floor;
- a claim register fails when a claim outlives its mechanism (`docs/CORRECTNESS.md`, `ClaimMechanismScanTests`);
- scans must show that they can fail.

### 1.1 The loop

Artefacts live under `Issues/round<N>/` unless named otherwise.

| # | Phase | Artefacts | Done when |
|---|---|---|---|
| 0 | **Baseline** | T00 entry in `TASK-LOG.md`; the round audit's `baseline` mode | The build and the default lane are green on the untouched tree. The round audit reports every task TODO, which proves it can see work. |
| 1 | **Find** | `RESEARCH-*`, `AUDIT-*`, `FINDING-*`, `SPIKE-*`, an RFC; the previous round's open owner actions and next-round candidates | Every item carries evidence: a measurement with its method, a `file:line`, a failing reproduction or a commit. Every claim is labelled MEASURED, EVIDENCE or INFERRED (§7). |
| 2 | **Specify** | `ROUND<N>-TASKS.md`, `ROUND<N>-CHECKLIST.md`, optionally `round<N>-audit.sh` | Every task has a role, files, numbered steps, an acceptance test that must fail first, an audit command and STOP conditions. Open design forks sit in a decisions table. The checklist records the owner's approval. |
| 3 | **Re-anchor** | `DELTA-ON-MERGED-MASTER.md` | Every anchor is re-located on the actual base by its quoted code text. Any task that collides with a recorded ruling, or with work already landed, is flagged before anyone executes it. |
| 4 | **Prove and land** | commits | Each task goes RED for the stated reason, then GREEN. Generated output stays byte-identical unless the task declares the change. Gates are green. |
| 5 | **Record** | `TASK-LOG.md`, the checklist, `Issues/ledgers/`, `CHANGELOG.md` | Every departure from the literal task is recorded with its reason, not silently resolved. User-visible changes are under `[Unreleased]`, citing round and task. |
| 6 | **Gate** | T99 | The final gate (G8) passes after the last commit. |
| 7 | **Rule** | "Owner actions" in the checklist; dated owner rulings | Every decision reserved to the owner (§4) was put as options with a recommendation, and the answer is recorded where it outlives the task. |
| 8 | **Close and carry** | the round PR, labelled `full-ci`; next-round candidates | The expensive CI tier ran on the merge result and the owner merged. Nothing found is left only in a transcript (`Issues/round20/CARRY-FORWARD.md`). |

Approval is one line in the checklist: `Status: approved by the owner on <date>`. The owner writes it, or an agent
writes it on the owner's word in that session, quoting the words. A PLAN run leaves
`Status: proposed — awaiting owner approval`, and an EXECUTE run refuses to start without the approval line.

Documents are either frozen or living:

- **Frozen** documents are corrected only by a dated banner or an appended note, never rewritten. These are a
  delivered task list, research, a FINDING and a ledger.
- **Living** documents are updated in the commit that moves them. These are the checklist and the TASK-LOG.

All state is in the checklist, the TASK-LOG and the commits, so the protocol is resumable by construction: a later
run with the same goal picks up the next open task.

### 1.2 Roles

These are round 31's roles, as defined in `Issues/round31/ROUND31-TASKS.md`:

- **[S] Executor.** Carries out a task exactly as written and never improvises design. When a STOP condition
  fires, it stops and hands over the evidence.
- **[O] Designer and reviewer.** Owns the forks in a task list's decisions table and every OPUS-REVIEW gate. In an
  unattended run, a reviewer with a fresh context plays this role (§4). The agent never reviews its own change.
- **[H] The owner.** The human maintainer, responsible for scope, rulings, account and repository settings, merges
  and releases.

## 2. The goal

> **Advance the ratchet by one round.** Every task on the list the owner approved ends in one of three ways:
> landed with red-to-green proof, decided with a recorded reason, or escalated with its evidence. Every gate is
> green and none was loosened. Nothing that was loud became silent. No decision was taken that belongs to the
> owner.

Automation runs between two human gates and never crosses them:

    owner approves a task list ──► EXECUTE goal ──► owner reviews, labels full-ci, merges
          ▲                                                                           │
          └──── owner approves ◄── PLAN goal drafts round N+1 ◄───────────────────────┘

An agent never approves its own task list, never merges its own round, and never reads silence as approval
(`Issues/round27/AUTONOMOUS-ASSUMPTIONS.md`). Escalation is a legitimate way for a task to end. A run that stops at
an owner question with the evidence written down has done its job. A run that decided silently has not.

## 3. Invariants

None of these is traded for progress. If the only way forward breaks one, the task stops (§6).

| | Invariant | Held by |
|---|---|---|
| I-1 | **Loud, never silent.** A change may add a diagnostic, an exception or a failing check. It never turns a reported problem into silence, and a refusal names its fix. | the diagnostic and negative-case suites; `Issues/round30/PLAN-de-silencing.md` |
| I-2 | **Generated output changes only where a task declares it,** and only as declared. | `GoldenCorpusTests`, whose `DWARF_GOLDEN_UPDATE=1` is refused under CI; the Verify snapshots |
| I-3 | **Gates only tighten.** No floor, threshold, ceiling, pin, filter or allowance is loosened to get green. An allowance shrinks in the commit that removes its cause. To pass a gate, remove the unreachable or mutable source, prove an equivalent mutant in the ledger, or escalate. Only ceilings that track intended growth, such as package size and allocation pins, legitimately move up. They are re-measured exactly, with an account of what grew, in the commit whose declared change caused the growth (see `7e112c7`). An unattended run never moves them; it records the re-measurement as owed. | `RatchetInvariantScanTests`, `ResolverParameterDisciplineTests`, `DocFenceScanTests`, `DiagnosticCoverageRatchetTests`, `SurfaceParityTests`, the band and ceiling checks in `scripts/gate-checks.ps1` |
| I-4 | **No new suppression without an owner ruling** and a justification at the site. This covers `NoWarn`, `#pragma warning disable`, `[SuppressMessage]`, `[ExcludeFromCodeCoverage]`, `Stryker disable` and skipped tests. | `TreatWarningsAsErrors`, `AuditSuppressionScanTests`, the pins in `RatchetInvariantScanTests` |
| I-5 | **Red before green.** A fix starts with a test that fails for the stated reason. New mapping behaviour gets a generator snapshot test and a runtime integration test. A new guard is shown able to fail. | `CONTRIBUTING.md`, `TestTheTestsScanTests`, `SelfAuditNonVacuityTests` |
| I-6 | **Public surface is tracked and announced.** New public API goes in `PublicAPI.Unshipped.txt`. A new diagnostic id gets a negative case, an `AnalyzerReleases.Unshipped.md` row, a `docs/diagnostics.md` entry and a `CHANGELOG.md` line. Ids are never reused. | the PublicAPI and release-tracking analyzers, `DiagnosticCoverageRatchetTests`, `AssemblyScanTests` |
| I-7 | **Shipped code is reflection-free and AOT- and trim-safe.** | `BannedSymbols.txt`, `ReflectionFreeMetaTests`, the CI `aot-trim-gate` |
| I-8 | **One toolchain.** SDK 10.0.101 exactly, locked restore, central package versions. | `global.json` (`rollForward: disable`), the CI SDK assertion, `RestoreLockedMode` |
| I-9 | **Documentation is derived.** C# in the docs is quoted from compiled samples. Generated pages are rewritten only by their own tests. A published claim names the mechanism that holds it. | `DocFenceScanTests`, `DocsAreSnippetCurrentTests`, `GeneratedDocsAreCurrentTests`, `ClaimMechanismScanTests` |
| I-10 | **Recorded owner rulings stand** until the owner overrules them, in a commit that says so. | `Ruling:` lines and "Owner rulings" sections under `Issues/`; the commit history |
| I-11 | **Every source file starts with** `SPDX-License-Identifier: GPL-2.0-only`. | IDE0073 as an error (`.editorconfig`) |

## 4. Decision rights

| Who decides | What | Recorded as |
|---|---|---|
| **The executing agent** | Only what a task leaves open **and** costs nothing, or one revert, if wrong. Examples: the literal an assertion pins (read from the emitter, not guessed); an anchor re-located by its quoted code; the existing descriptor a task mis-named; a name within convention; the more conservative of two readings; how commits are split. | `Ruling: <decision> — costs if wrong: <cost>` in the round's `TASK-LOG.md`, the ledger form described in `Issues/ledgers/README.md` |
| **[O].** When unattended, a reviewer subagent with a fresh context, given the task text, §3 and the diff. | A design fork that the approved decisions table settles. Every OPUS-REVIEW gate: a golden or snapshot diff must consist solely of the declared change, and a concurrency or ordering argument must hold. A new diagnostic's severity and id (the next free id, never a reused one). A reviewer that rejects, or is unsure, escalates. | the verdict and its reasons in `TASK-LOG.md` |
| **The owner, only** | **Scope:** adding, dropping or reordering tasks; approving a task list. **Rulings:** overruling a recorded ruling. **Gates:** loosening a gate or accepting a red one. **Public surface:** API or options that no approved task names. **Governance:** CI security posture, licences, third-party services. **Releases:** pushing to `master`, merging, tagging, releasing, publishing to nuget.org (manual by ruling, round 31 T21). **Settings:** account, environment and repository settings. **Outside this repository:** the owner's other repositories (including the FusedChat consumer), user-level agent configuration, memory. **Agent instructions:** this file, `CLAUDE.md`, `.claude/`. **Shared machines:** starting a machine-heavy leg. | a dated entry under "Owner rulings" in the round's `TASK-LOG.md` or checklist, quoting the owner's words and stating the options and the one chosen |

If a choice is not clearly in the first row, it is not the agent's.

A defect a user can reach — a miscompile, a silently wrong mapping, a crash — is never backlog. If it lies in the
code the current task changes, fix it inside that task, as round 31 T02 did with the CS0034 it uncovered.
Otherwise, write it up as `FINDING-*.md` with its reproduction and escalate it.

## 5. Procedures every task follows

These generalise round 31's G1–G7, so a future task list can cite them instead of copying them.

- **G1 Custody.** Before a baseline or a measurement, `git status --porcelain` lists only files this task created.
  Anything else is a STOP; a leftover once produced a false bug report. Stage paths explicitly: a bare commit once
  swept another task's staged work into a docs commit.
- **G2 Long commands.** Run them in the background and poll. A run that timed out, crashed or ran under memory
  pressure is void, not a result.
- **G3 Red–green.**
  1. Write the test and run it. It must fail for the reason the task states, so read the failure message.
  2. Implement, and see the test pass.

  A test that is green before the fix is accepted only when the task calls it a guard. Both runs go in the commit
  body.
- **G4 Declared emission changes.**
  1. Regenerate with `DWARF_GOLDEN_UPDATE=1`, locally only.
  2. List the changed case ids from the manifest diff.
  3. Compare every Verify `*.received.txt` with its `*.verified.txt`.

  A hunk is acceptable only if it consists solely of the declared change; anything else is a STOP. The list of
  changed cases goes to OPUS-REVIEW.
- **G5 Files.**
  - New tests go under `tests/<Project>/Round<N>/`, in namespace `<ProjectNamespace>.Round<N>`.
  - Line 1 of every new file is the SPDX header.
  - A file under `Coverage/` carries its `// Covers:` line.
  - Throwaway probes are named `ZZ*.cs` and are deleted before the commit.
- **G6 Commits.** A commit belongs to exactly one task, though a task may take several commits.
  - Subject: `<type>(round<N>/<Tnn>): <what changed, in plain words>`.
  - Body: why; the proof, which is RED and GREEN, or (for a task that must not change behaviour) the golden and
    snapshot suites green without regeneration; the changed golden case ids; and an `Owed:` line for every check
    not run.
- **G7 Anchors and history.** Locate code by its quoted text, never by line number alone. Before calling something
  a defect or proposing work:
  - read both sides of the code involved;
  - search the rulings: `grep -rn "Ruling:\|owner ruling\|Owner rulings" Issues/`;
  - search the history: `git log -S'<text>'`. A shallow clone hides decisions, so run `git fetch --unshallow`
    first.

  Round 30 withdrew a "defect" that was in fact a ruling git had already recorded
  (`Issues/round30/FINDING-array-null-ternary.md`).
- **G8 The final gate (T99).** Run it after the last commit. All of the following must hold:
  - the solution builds with 0 warnings and 0 errors;
  - the default lane (`--filter "Category!=SurfaceMatrix&Category!=Perf"`) passes in every test assembly;
  - `GoldenCorpusTests` passes with `DWARF_GOLDEN_UPDATE` unset;
  - the round audit, if there is one, shows no TODO for a landed task;
  - `CHANGELOG.md` `[Unreleased]` has a line for each landed user-visible task;
  - `git status --porcelain` is empty.

  Some checks cannot run here: coverage floors and mutation legs (`scripts/housekeeping.ps1`), the deep tier, and
  everything behind the `full-ci` label. List them as owed. Never report them as passed.

## 6. STOP, escalate, continue

A task stops when any of these happens:

- its own STOP condition fires;
- its RED fails for another reason, or does not fail at all;
- a golden or snapshot diff goes beyond what the task declares;
- it needs a decision that §4 does not give the agent;
- a gate is red and the fix lies outside the task;
- the tree disagrees with the task: the code moved, is already built, or the task contradicts a ruling;
- the only way forward breaks an invariant;
- the task is still not green after five fix attempts. This is the SDD ledgers' "fix round n/5" budget, and the
  escalation lists what was tried.

When a task stops, there is no workaround (round 31 §1.5). Take these three steps.

1. Append an entry to `## Escalations` in the round's `TASK-LOG.md`:

       ### T<nn> — <the condition that fired>, step <k>
       - Command: `<what was run>`
       - Output: <the relevant lines, trimmed>
       - Needs: owner ruling | [O] decision | environment | upstream fix
       - Options: (A) … (B) … — recommended: <one>, costs if wrong: <cost>
       - Blocks: <tasks that depend on this one>

2. Mark the task `[!]` in the checklist, and mark every task that depends on it `[!]` as well, naming the
   blocker.
3. Carry on with the next task that does not depend on it.

Stopping dead wastes the unattended time, and deciding silently hides the decision (`AUTONOMOUS-ASSUMPTIONS.md`).

Checklist marks:

| Mark | Meaning |
|---|---|
| `[ ]` | open |
| `[x]` | landed |
| `[~]` | landed, with an open owner step |
| `[d]` | decided not to build, with the reason recorded |
| `[!]` | escalated, or blocked by an escalated task; waiting on the owner or [O] |

## 7. What counts as evidence

- **Labels.** Every claim carries one (`Issues/round31/POST-ROUND30-IMPROVEMENT-RESEARCH.md`):
  - MEASURED: run on this tree in this session, with the command stated;
  - EVIDENCE: an external source, cited;
  - INFERRED: reasoning, verified before it becomes a task step.
- **A number names its tree, command and machine.** Ratios are evidence; absolute times are only indicative. A
  container is not the owner's machine.
- **Void is not green.** None of these counts as a pass: mutants credited en masse by timeout, a run that shared
  the machine with memory pressure, a scan that saw nothing. Round 31's two void pipeline runs (`1288253`) are the
  example.
- **A guard is shown failing once,** by a planted mutant or by its RED run, and the commit says how.
- **Say what was counted.** "Right number, wrong population" is this repository's recurring measurement error.
- **Corrections are visible.** An earlier claim that measurement overturns gets a dated correction; it is not
  quietly edited. Round 29's ledger marks its overturned framings in capitals.

## 8. Environment for an unattended run

A run checks these first. If one cannot be met, it stops with `ENVIRONMENT BLOCKED: <what>`. It never substitutes
another SDK, never edits `global.json`, and never filters out the tests that need a missing tool.

- **.NET SDK 10.0.101.** Install it with `dotnet-install.sh --version 10.0.101 --install-dir "$HOME/dotnet"`, then
  run `export PATH="$HOME/dotnet:$HOME/.dotnet/tools:$PATH"`. The install needs `builds.dotnet.microsoft.com` and
  `api.nuget.org`. A cloud environment whose network policy blocks either host cannot run the goal.
- **`dotnet-ilverify`**, at the version CI installs (`.github/workflows/ci.yml`). Without it,
  `EmittedIlIsVerifiableTests` fails.
- **PowerShell 7 (`pwsh`).** Without it, the `PwshBattery` tests fail, and `scripts/housekeeping.ps1` needs it.
- **python3**, for the round audit scripts.
- **Full history:** run `git fetch --unshallow` (G7).
- **The heavy tier** is not run in a goal turn unless the goal itself says so. It covers mutation legs, the deep
  tier, exhaustion, the AOT benchmark, cross-platform, reproducible build and package size. It runs in CI on a PR
  labelled `full-ci`, or on the owner's machine. There, a mutation leg needs the machine to itself and starts only
  when the owner says so.
- **Workflow files.** A commit that changes `.github/workflows/` may need a push with the `workflow` scope. If the
  push is refused, that is an owner action, not a reason to drop the change.
- **Nothing outside this repository is assumed.** A cloud run does not have the owner's user-level instructions,
  memory, Rider or roslyn-lens. The hooks in `.claude/hooks/` are wired only in the owner's own settings, so a
  cloud run keeps their discipline by hand: windowed reads of large files, and searches scoped to a path.

## 9. The goals

Paste one of these into Claude Code's `/goal` command (`https://code.claude.com/docs/en/goal`). A separate model
judges the goal after every turn, and it sees only the conversation. So every condition is something the run must
*print*. Each text stays under the command's 4,000-character limit.

| Placeholder | Meaning |
|---|---|
| `<N>` | the round number |
| `<BRANCH>` | the branch to work on, never `master` |
| `<PUSH>` | `push each landed task to origin/<BRANCH> and nowhere else` in a cloud session, whose container is discarded; `push nothing; the owner pushes` on the owner's machine, which is the standing rule there |
| `<INPUT>` | the owner's brief for a PLAN run, as a path or a sentence, or `none` |
| `<MAX_TURNS>` | a turn budget; a run that reaches it stops with its report, and the next run continues from the checklist |

The `round` skill prints a filled-in goal on request. For unattended work, run the goal in auto mode, or headless:
`claude -p "/goal …" --output-format stream-json --verbose`.

### 9.1 EXECUTE: land an approved round

```text
Work through DwarfMapper.NET round <N> on branch <BRANCH> until its final gate passes, following Issues/ROUND-PROTOCOL.md and the `round` skill (read both before the first task). Push policy: <PUSH>.

Before the first task: if Issues/round<N>/ROUND<N>-CHECKLIST.md does not record the owner's approval of the task list, print NOT APPROVED and stop; the goal is then impossible. If the toolchain of protocol section 8 cannot be installed, print ENVIRONMENT BLOCKED: <what> and stop; the goal is then impossible.

MET when the latest turn prints a ROUND REPORT showing all of:
1. Every task in Issues/round<N>/ROUND<N>-TASKS.md is marked in the checklist as [x] landed (commit hashes), [~] landed with an open owner step, [d] decided, not built (reason), or [!] escalated or blocked by an escalated task (entry under "Escalations" in Issues/round<N>/TASK-LOG.md). None is left [ ].
2. Every commit belongs to one task, its subject is "<type>(round<N>/<Tnn>): <what>", and each landed task's commit body states its proof: the new test's RED run (failing for the reason the task states) and its GREEN run, or, for a task that must not change behaviour, the golden and snapshot suites green without regeneration.
3. The final gate, run in the same turn as the report and after the last commit, each command's output quoted from that turn: solution build "0 Warning(s)" and "0 Error(s)"; every test assembly of the default lane (--filter "Category!=SurfaceMatrix&Category!=Perf") passed with 0 failed; GoldenCorpusTests passed with DWARF_GOLDEN_UPDATE unset; the round's audit script, if any, shows no TODO for an [x] task; `git status --porcelain` empty; the push policy met.
4. CHANGELOG.md [Unreleased] has a line for every [x] task with a user-visible effect.
5. The report lists the open owner actions and every check owed but not run (coverage floors, mutation legs, deep tier, full-ci).

NEVER, on any turn:
- work on anything the task list does not contain, or edit the task list (deviations go to TASK-LOG.md);
- loosen any floor, threshold, ceiling, pin, filter or allowance; add a NoWarn, #pragma, [SuppressMessage], Skip or ratchet row; touch global.json;
- regenerate golden or Verify output except in a task that declares that emission change, and then only after the changed case ids pass the OPUS-REVIEW of protocol section 4;
- make a reported problem silent, or count a timed-out, crashed or vacuous run as a pass;
- take a decision the task list or protocol section 4 reserves to [O] or the owner, or contradict a recorded owner ruling;
- push anywhere the push policy does not allow; merge, tag, release, publish a package, change repository or environment settings, edit CLAUDE.md, .claude/ or the protocol, or write outside this repository.
If one happens anyway: undo it if uncommitted, print BREACH: <what>, and stop; the goal is then impossible.

On a STOP condition, an unsettled decision, a red gate the task cannot fix, a fifth failed fix attempt, or a tree that disagrees with the task: no workaround. Record it under "Escalations" in TASK-LOG.md, mark the task and every task depending on it [!], and continue with the next task that does not depend on it.

End every turn with one line: landed / decided / escalated / remaining, and the turn number. If not met earlier, stop after <MAX_TURNS> turns with the ROUND REPORT.
```

### 9.2 PLAN: draft the next round for the owner

```text
Draft round <N> of DwarfMapper.NET as a task list for the owner to approve, on branch <BRANCH>, following Issues/ROUND-PROTOCOL.md and the `round` skill (read both first). Push policy: <PUSH>. Owner's brief: <INPUT>. Documentation only: the run creates Issues/round<N>/ROUND<N>-TASKS.md, ROUND<N>-CHECKLIST.md and optionally round<N>-audit.sh, and changes nothing else.

MET when the latest turn prints a PLAN REPORT showing all of:
1. Issues/round<N>/ROUND<N>-TASKS.md exists in the shape of Issues/round31/ROUND31-TASKS.md: context for [O] (repository facts, invariants, traps, a "Decisions required" table with options and a recommendation per row, the escalation protocol), the procedures (citing protocol section 5), then the tasks in execution order from T00 (baseline) to T99 (final gate).
2. Every task has a role ([S], [O] or [H]), the files it touches, numbered steps, an acceptance test with the failure it must show first (or "guard" and why), an audit command, at least one STOP condition, and its source: a path under Issues/, a commit, a CI run, or a measurement made in this session with its command. Every claim is labelled MEASURED, EVIDENCE or INFERRED.
3. Every candidate came from recorded input: the owner's brief, the previous round's open owner actions and next-round candidates, open FINDING, PARKED and CARRY-FORWARD items, and checks that are red or owed on the current tree or in CI. Each was checked against the rulings and the history (protocol G7); nothing decided, withdrawn, parked or already built is proposed; the rejected candidates are listed with their reasons.
4. Issues/round<N>/ROUND<N>-CHECKLIST.md lists every task as [ ], names the base commit, and says "Status: proposed — awaiting owner approval".
5. The draft is committed as "docs(round<N>): task list for owner review", `git status --porcelain` is empty, and the push policy is met.

NEVER: change any existing file (code, tests, scripts, CI, findings, ledgers, other rounds' documents); propose a task without recorded evidence; propose loosening a gate; mark anything approved; settle a row of the decisions table.

End every turn with one line: candidates found / accepted / rejected, and the turn number. If not met earlier, stop after <MAX_TURNS> turns with the PLAN REPORT.
```

## 10. Amending this protocol

This file changes only by owner ruling, in a commit whose subject says so. It holds no counts and no status (see
the banner at the top). Where it disagrees with a newer ruling, the ruling wins, and this file is corrected in the
same round.

A rule here that nothing enforces is a candidate for a gate, not for more prose. As one of the repository's hooks
puts it: "Writing the rule down did not stop it" (`.claude/hooks/Deny-RootScopedSearch.ps1`).

## 11. Mechanisms named here

Each row is a path this file relies on. If a row stops resolving, this file is wrong: fix the file, not the
mechanism. Binding these rows with a scan, in the style of `ClaimMechanismScanTests`, is the natural first task
for a round that adopts this protocol.

| Id | Mechanism | Path |
|---|---|---|
| M-01 | SDK pin | `global.json` |
| M-02 | Contributor ground rules and the `full-ci` label | `CONTRIBUTING.md` |
| M-03 | Byte-identity lock on generated output | `tests/DwarfMapper.Generator.Tests/Golden/GoldenCorpusTests.cs` |
| M-04 | Golden manifest | `tests/DwarfMapper.Generator.Tests/Golden/output-manifest.txt` |
| M-05 | Verify snapshots | `tests/DwarfMapper.Generator.Tests/Snapshots` |
| M-06 | Claim register binding | `tests/DwarfMapper.Generator.Tests/SelfValidation/ClaimMechanismScanTests.cs` |
| M-07 | Floors, bands and pins | `tests/DwarfMapper.Generator.Tests/SelfValidation/RatchetInvariantScanTests.cs` |
| M-08 | Equivalent-mutant ledger | `Issues/ledgers/equivalent-mutants.md` |
| M-09 | Doc fence ratchet | `tests/DwarfMapper.Generator.Tests/SelfValidation/DocFenceScanTests.cs` |
| M-10 | Resolver parameter discipline | `tests/DwarfMapper.Generator.Tests/SelfValidation/ResolverParameterDisciplineTests.cs` |
| M-11 | Hollow-test scan | `tests/DwarfMapper.Generator.Tests/SelfValidation/TestTheTestsScanTests.cs` |
| M-12 | Self-audit non-vacuity | `tests/DwarfMapper.Generator.Tests/SelfValidation/SelfAuditNonVacuityTests.cs` |
| M-13 | Negative cases | `tests/DwarfMapper.NegativeCases/Cases` |
| M-14 | Diagnostic coverage ratchet | `tests/DwarfMapper.NegativeCases/DiagnosticCoverageRatchetTests.cs` |
| M-15 | Diagnostic release tracking | `src/DwarfMapper.Generator/AnalyzerReleases.Unshipped.md` |
| M-16 | Public API tracking (runtime) | `src/DwarfMapper/PublicAPI.Unshipped.txt` |
| M-17 | Public API tracking (testing) | `src/DwarfMapper.Testing/PublicAPI.Unshipped.txt` |
| M-18 | Descriptor, docs and CHANGELOG sync | `tests/DwarfMapper.Generator.Tests/SelfValidation/AssemblyScanTests.cs` |
| M-19 | Suppression scan | `tests/DwarfMapper.Generator.Tests/Round31/AuditSuppressionScanTests.cs` |
| M-20 | Surface parity ceilings | `tests/DwarfMapper.Generator.Tests/Contracts/SurfaceParityTests.cs` |
| M-21 | Reflection-free emitted code | `tests/DwarfMapper.Generator.Tests/SelfValidation/ReflectionFreeMetaTests.cs` |
| M-22 | Snippet-derived docs | `tests/DwarfMapper.Generator.Tests/SelfValidation/DocsAreSnippetCurrentTests.cs` |
| M-23 | Generated docs | `tests/DwarfMapper.Generator.Tests/SelfValidation/GeneratedDocsAreCurrentTests.cs` |
| M-24 | ilverify prerequisite | `tests/DwarfMapper.Generator.Tests/SelfValidation/EmittedIlIsVerifiableTests.cs` |
| M-25 | pwsh prerequisite | `tests/DwarfMapper.Generator.Tests/SelfValidation/PwshBattery.cs` |
| M-26 | Coverage floors, ILVerify, mutation legs | `scripts/housekeeping.ps1` |
| M-27 | Gate bands, size ceilings | `scripts/gate-checks.ps1` |
| M-28 | Mutation leg configs | `stryker-config.json` |
| M-29 | CI tiers | `.github/workflows/ci.yml` |
| M-30 | Release pipeline | `.github/workflows/release.yml` |
| M-31 | Local pre-push gate | `scripts/git-hooks/pre-push` |
| M-32 | Changelog | `CHANGELOG.md` |
| M-33 | Diagnostic reference | `docs/diagnostics.md` |
| M-34 | Correctness claim register | `docs/CORRECTNESS.md` |
| M-35 | Exemplar task list | `Issues/round31/ROUND31-TASKS.md` |
| M-36 | Exemplar checklist | `Issues/round31/ROUND31-CHECKLIST.md` |
| M-37 | Exemplar task log | `Issues/round31/TASK-LOG.md` |
| M-38 | Exemplar re-anchoring | `Issues/round31/DELTA-ON-MERGED-MASTER.md` |
| M-39 | Exemplar round audit | `Issues/round31/round31-audit.sh` |
| M-40 | Unattended-decision precedent | `Issues/round27/AUTONOMOUS-ASSUMPTIONS.md` |
| M-41 | Carry-forward precedent | `Issues/round20/CARRY-FORWARD.md` |
| M-42 | Ledger form | `Issues/ledgers/README.md` |
| M-43 | Agent entry point | `CLAUDE.md` |
| M-44 | Per-turn procedure | `.claude/skills/round/SKILL.md` |
