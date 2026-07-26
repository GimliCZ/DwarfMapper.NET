<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Working notes

> **TEMPORARY.** Scratch notes carried across sessions, not project documentation. Delete each item once it is
> decided. If this file is still here months from now with the same three entries, it has become the kind of
> stale prose the rest of this repository is built to prevent — delete it rather than let it rot.

## Open decisions from the derived-documentation work (2026-07-26)

All three are **judgement calls left deliberately to the maintainer**, not defects, and **none is blocking**.
The work is merged, pushed, and CI is green at `d6e583f`.

### 1. The orphan rule is scoped to non-example files

`DocReconciliationTests.No_snippet_region_outside_a_declared_example_is_orphaned` only demands that regions in
files which exist *solely to be quoted* are referenced by some document. Regions inside a declared
`[DocExample]` are exempt.

**Why it was scoped that way:** enforcing it everywhere would have demanded that all 32 Gallery regions be
quoted somewhere or have their markers deleted — deleting evidence to satisfy a guard. A `[DocExample]` region
is already reader-facing: the file is linked from the generated index and runs on every `dotnet run`.

**The alternative if you want it strict:** an illustrated Gallery README quoting all 32 regions inline. Real
reader value (every example's core code on one page), at the cost of a much longer page. Fully generated
either way, so no maintenance burden — only a formatting preference.

### 2. Eight C# fences remain hand-written, each with a stated reason

37 fences total: 29 snippet-backed, 8 exempt. The ratchet allowlist is empty, so every one is *accounted for*
and no unaccounted fence can be added.

- **5 in `docs/diagnostics.md`** — each illustrates the shape that *triggers* a diagnostic. A compiling sample
  would defeat the point, since the code must fail the build to demonstrate the rule. Structural, unlikely to
  ever change.
- **3 structural** — two in `README.md` (a `CaseInsensitive` demo needs a case-mismatched fixture, which would
  trip the naming analyzers; one contrasts two overloads on one class to make a point about signatures), and
  one in `docs/howto/common-changes.md` showing two declaration styles side by side that no single sample file
  contains.

The `CaseInsensitive` one is the only plausible candidate for conversion, and it needs a fixture with a
deliberately lower-cased member — decide whether that is worth an analyzer suppression in the Gallery.

### 3. The `Dict` row wants one Windows re-run

`benchmarks/results/2026-07-25-realistic-payloads-comparable.md` called a ~2× lead over Mapperly "the
headline". Four Linux re-measurements put it at **~1.14× (range 1.09–1.19)**. `Dict_Dwarf` reproduces across
platforms; **`Dict_Mapperly` does not** (20,050 ns on Windows vs 11.37 µs here), so the movement is entirely on
Mapperly's side.

Ruled out: Mapperly version (pinned `4.3.1` throughout), benchmark change (the fixture change and that results
file are the *same* commit), and any difference in work done (allocation is identical on both platforms).

**Cannot be settled from Linux** — it needs a Windows host. Windows CI works again as of `d6e583f`, so this is
now cheap to arrange. Until then the defensible figure is the Linux one. The 2× is **not** claimed in
`README.md` or `docs/COMPARISON.md`; the 2026-07-25 file carries a correction banner pointing at the evidence.

The stronger half of that row never depended on any of this: `Dict` allocates **3.3× less** than
Mapster/AutoMapper, and allocation is load-independent.
