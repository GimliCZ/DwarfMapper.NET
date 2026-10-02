---
name: round
description: The per-turn procedure of DwarfMapper.NET's round protocol (Issues/ROUND-PROTOCOL.md). Use when a /goal or the owner asks to execute, continue, plan or close a numbered round under Issues/round<N>/, to land any task from a ROUND<N>-TASKS.md, or to print a ready-to-paste /goal for a round.
---

<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round: one turn of the protocol

The rules live in `Issues/ROUND-PROTOCOL.md`, and section numbers below point there. This file gives only the order
of work. If the two disagree, the protocol wins and this file is wrong.

## Start of every turn

1. Run `git status --porcelain`. Only files the current task created may show (G1); anything else is a STOP.
2. Check the branch and history:
   - `git branch --show-current` must be the goal's branch, never `master`.
   - `git rev-parse --is-shallow-repository` must print `false`. If it does not, run `git fetch --unshallow`.
3. Check the toolchain (§8):
   - `dotnet --version` prints `10.0.101`;
   - `ilverify --version` and `pwsh --version` resolve;
   - `python3` exists.

   Install whatever is missing as §8 describes. If that fails, print `ENVIRONMENT BLOCKED: <what>` and stop.
4. Read the round's state:
   - the checklist: its approval line and the task marks;
   - the `## Escalations` section of the TASK-LOG;
   - `git log --oneline -15`.

## EXECUTE: pick the next task

- If the checklist does not record the owner's approval of the task list, print `NOT APPROVED` and stop.
- Walk the tasks in execution order. Skip:
  - tasks marked `[x]`, `[~]`, `[d]` or `[!]`;
  - tasks whose prerequisites have not landed;
  - optional tasks, until nothing else is left.
- What to do depends on the task's role:
  - **`[S]`:** execute it.
  - **`[O]`:** execute the parts the task specifies (measure, prototype, record). Take a choice the task leaves
    open only from a settled row of the decisions table or a recorded ruling; otherwise escalate.
  - **`[H]`:** never execute it. Record it as an owner action and mark it `[!]`.
- Read the task's full text in windows, because it can be long. Read §3–§6 of the protocol once per session.

## EXECUTE: land the task

1. **Re-anchor (G7).** Find each anchor by its quoted code, and search the rulings and history for the area.
   - A moved anchor: re-locate it and note the move.
   - Work that is already built, or a contradiction with a ruling: STOP.
2. **RED (G3).** Write the test exactly as the task gives it, placed per G5. Build, run only that test, and read
   the failure message.
3. **GREEN.** Make the smallest change the task describes. The build must show 0 warnings and 0 errors. Run the
   test again.
4. **Gates for this task.**
   - The task's acceptance and audit commands.
   - `GoldenCorpusTests` without regeneration. If the task declares an emission change, follow G4 instead, then
     step 5.
   - The test projects you touched.
   - The whole default lane, at least every few tasks and always before T99.
5. **OPUS-REVIEW, when the task names one.**
   1. Start a reviewer subagent with a fresh context.
   2. Give it the task text, §3 of the protocol and the diff. For golden changes, include the changed case ids and
      every changed hunk.
   3. Ask it to reject anything that is not solely the declared change, and to answer APPROVE or REJECT with
      reasons.
   4. Record the verdict in the TASK-LOG. REJECT, or an unsure answer, is an escalation.
6. **Bookkeeping,** only where the task adds public surface (I-6):
   - the `PublicAPI.Unshipped.txt` line that RS0016 prints, copied verbatim;
   - for a new diagnostic:
     - the next free id;
     - `tests/DwarfMapper.NegativeCases/Cases/DWARFnnn_<Name>.cs` with its `// CASE:`, `// WHY:`, `// EXPECT:`
       and `// EXPECT-MESSAGE` headers (copy an existing case);
     - the `AnalyzerReleases.Unshipped.md` row;
     - the `docs/diagnostics.md` entry;
   - user-facing docs that quote a compiled sample (`<!-- snippet: id -->`) or carry
     `<!-- fence-exempt: reason -->`.
7. **Commit (G6).**
   - No `ZZ*.cs` file is left.
   - Stage paths explicitly.
   - Subject: `<type>(round<N>/<Tnn>): <what>`.
   - Body: the proof, the changed case ids, and an `Owed:` line.
8. **Record.**
   - A TASK-LOG entry: "done as specified", or each deviation with its reason, plus any
     `Ruling: … — costs if wrong: …`.
   - The checklist row: `[x]` with the commit hash.
   - For a user-visible change, a `CHANGELOG.md` `[Unreleased]` line in the file's own style, citing
     "(Round <N> T<nn>.)".
9. **Push** as the goal's push policy says.

## When something stops the task

Follow §6 exactly:

1. Write the escalation entry.
2. Mark the task `[!]`, and mark every task that depends on it `[!]` too, naming the blocker.
3. Move on to the next independent task.

No workaround, no retrying the same thing in a new disguise, no silent decision. A task still red after five fix
attempts is escalated, with the attempts listed.

## End of every turn

Print one line:

    Round <N>: landed a · decided b · escalated c · remaining d — turn t/<MAX_TURNS>

## When no task is left: the final gate

Run G8 after the last commit, in the same turn as the report. The evaluator checks the report against that turn's
own command output, so a report pasted from an earlier turn does not count. Then print:

    ROUND REPORT — round <N> @ <hash> on <branch>
    Tasks: landed a · decided b · escalated c · remaining 0
    | task | mark | commits | note |
    Final gate (after <hash>):
      build   <the "Warning(s)" and "Error(s)" lines>
      tests   <one summary line per test assembly>
      golden  GoldenCorpusTests passed, DWARF_GOLDEN_UPDATE unset
      audit   <the audit's summary line, or "no audit script">
      tree    git status --porcelain: (empty)
      push    <what the push policy required, and that it happened>
    Owed, not run here: <coverage floors, mutation legs, deep tier, full-ci, ...>
    Owner actions open: <escalations and [H] tasks, one line each>, and "open the round PR with the full-ci label"

## PLAN mode

1. **Gather inputs, in this order:**
   1. the owner's brief, if there is one;
   2. the previous round's "Owner actions" and next-round candidates;
   3. open `FINDING-*`, `PARKED-*` and `CARRY-FORWARD*` items;
   4. checks that are red or owed, on the tree and in CI.
2. **Vet each candidate.**
   - Label it MEASURED, EVIDENCE or INFERRED (§7).
   - Search the rulings and history (G7).
   - Drop it, with a reason, if it is decided, withdrawn, parked or already built.
3. **Write `ROUND<N>-TASKS.md`** in round 31's shape:
   1. context for [O]: facts, invariants, traps, the decisions required (each with options and a recommendation),
      and the escalation protocol;
   2. the procedures, cited from §5 rather than copied;
   3. tasks T00 to T99, each with its role, files, steps, acceptance test and the failure it must show first,
      audit command, STOP conditions and source.
4. **Write `ROUND<N>-CHECKLIST.md`.**
   - Every task marked `[ ]`.
   - The base commit.
   - The line `Status: proposed — awaiting owner approval`.

   Optionally write `round<N>-audit.sh` with `baseline`, `static` and `tests` modes. If you write it, `static`
   must report every task TODO on the current tree.
5. **Commit and report.** Commit as `docs(round<N>): task list for owner review`. Then print the PLAN REPORT:
   - the tasks proposed, each with its id, role and source;
   - the decisions required;
   - the candidates rejected, each with its reason.

## Printing a goal

When asked for a goal:

1. Copy §9.1 or §9.2 of the protocol.
2. Fill in `<N>`, `<BRANCH>`, `<PUSH>`, `<INPUT>` and `<MAX_TURNS>`.
3. Check the result is under 4,000 characters.
4. Print it as one block that starts with `/goal `.
