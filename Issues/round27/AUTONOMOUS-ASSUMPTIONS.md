<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Decisions taken without you, and how to reverse each

Written 2026-08-26, when the session went ahead unattended. Four decision points existed; each was taken on
the **most conservative** reading rather than the most convenient one. None is hidden and none is expensive
to undo.

| # | Decision | Taken | Why this way | To reverse |
|---|---|---|---|---|
| 1 | How far to go | Finish tasks **1.5, 1.6, 1.7** | Exactly the work already discussed and planned in `TASKS.md` — no new scope invented | Nothing to undo; each task is its own commit |
| 2 | The branch | **Stay on `feat/round27-arch`** — no merge to master | A merge is a side effect outside the branch and has always needed your say-so. Rounds 25/26 are sitting the same way | `git merge feat/round27-arch` when you have reviewed it |
| 3 | Pushing | **Nothing pushed** | The standing rule is per-instance approval, and silence is not approval. Blocked by a missing OAuth `workflow` scope in any case | Push yourself when back |
| 4 | Genuine blockers | **Safest option, recorded here** | Stopping dead would waste the unattended time; deciding silently would hide it. Each entry below names the alternative | Per entry |

## Blockers actually hit

*(Appended as they occur. An empty section means none arose.)*

### 1.5 — the append-only guards described a field shape that no longer exists

`RegistryAppendOnlyTests` asserts "at least the five registry tables" and "at least four `TryAdd` calls".
Both are counts of the **old** four-loose-dictionary layout. Binding each map table to its own ambiguity set
makes the counts three and two, so both guards failed — correctly: they detected exactly the structural
change they exist to notice.

**Taken:** update both guards to describe the new shape, keeping what they actually protect — that every
insert is append-only and that the scan still finds every table. The guards are re-pointed, not weakened,
and the reasoning is recorded in their own comments.

**The alternative** would have been to leave the registry as four dictionaries so the guards stayed green.
That is the tail wagging the dog: the four-static layout is what allowed the recorded bug where
`RegisterUpdate` marked duplicates in the create table's ambiguity set.
