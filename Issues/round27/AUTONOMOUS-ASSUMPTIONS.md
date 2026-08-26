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

### 1.6 — the descriptor split was investigated and NOT made

`DiagnosticDescriptors.cs` is 1,746 lines and 96 descriptors, and the plan was to split it by owned id
range. Three measurements say the split's premise does not hold:

- **There is no grouping to split along.** All 96 share one category constant. Ownership by raising file is
  not a partition — 73 descriptors are raised from one file, but **23 are raised from two to four**, mostly
  because the same member-mapping concern is reported from both the mapper and the projection endpoint. And
  the id decades carry no themes: the `070s` run 071, 075, 076, 077, 078, 079, 074, 073, 072, 070, covering
  polymorphism, flatten leaves, self-maps, element-wise enforcement and nullability. Ids were allocated
  chronologically, which is the honest description of the id space.
- **Navigation is already solved.** `docs/generated/diagnostics-index.md` renders all 96 with severity and
  title, generated from the declarations themselves and guarded by `GeneratedDocsAreCurrentTests`. A second
  index inside the file would be the duplication this repository refuses.
- **The split would have silently vacated a scan.** `Scan2` proves every descriptor is referenced somewhere
  outside its own declaration file, and it achieves that by excluding *exactly* `DiagnosticDescriptors.cs`.
  Partials named `DiagnosticDescriptors.<something>.cs` would have stayed in the searched text, all 96 would
  have matched their own declarations, and the scan would have gone green while measuring nothing — the
  failure mode this repository has now caught in its own instruments seven times.

**Taken:** leave the file whole, and fix the trap the investigation found. `Scan2` now excludes the
declaring class by prefix AND asserts the property directly — that no part of the class reached the searched
text — so the guard cannot go vacuous however the file is later named or split.

**The alternative** is to split anyway, for file size alone. That remains available and is now safe to do;
the scan will no longer silently stop working if someone does. What it will not do is create a grouping,
because there is not one to create.

**One thing the check found on its own.** The first formulation asserted that no `public static readonly
DiagnosticDescriptor` reached the searched text, and it failed immediately — on `RegistryDiagnostics.cs`,
which declares the twelve `DWARFR` descriptors. That turned out to be correct behaviour and a wrong
assertion: they are a different class with their own gates in `RegistryDiagnosticsGenTests`, including an
explicit non-vacuity count, and none of the 96 field names appears in that file. The assertion was narrowed
to the declaring class rather than "fixed" by excluding a file that was never a problem.
