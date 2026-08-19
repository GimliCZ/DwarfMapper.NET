<!-- SPDX-License-Identifier: GPL-2.0-only -->

# A7 — the three things worth keeping

The task record is the report at
`.superpowers/sdd/2026-08-16-round20-generator-defects/task-A7-report.md` (untracked — `.superpowers` is
gitignored). This file is only what outlives A7 and would be expensive to rediscover.

## 1. `DWARF038` is `ImplicitConversionApplied` — it is not a refusal of anything

Four rounds of findings cited "refused with `DWARF038`" as evidence that the generator had an opinion about
`[MapProperty]`'s *placement*. It does not. `DWARF038` fires when a bind requires an implicit parse/format
conversion, and the surface probe's fixture binds `int Id` to `string? Name` — so the warning is proof the
rename **happened**, i.e. the exact opposite of a refusal.

The general trap: `SurfaceProbe.Classify` returns `Refused` for *any* added diagnostic, before it compares
output. **A cell reading `Refused` means "a diagnostic appeared", never "the right diagnostic appeared."**
Before citing a `Refused` cell as evidence, read what the id actually means.

## 2. A class-wide directive must be filtered before it is reported against one pair

Class-level `[MapIgnore("X")]` is class-WIDE and is *meant* to be tolerated where it matches nothing: a mapper
with a create map over `(Src, Dst)` and a span map over an unrelated `(Foo, Bar)` legitimately carries an
ignore that concerns `Dst` alone. Any per-endpoint diagnostic raised off a class-scoped directive therefore
needs a member-existence check against that endpoint's own pair, or it prescribes remedies naming members the
type does not have — `[MapIgnore<Bar>("Id")]` where `Bar` has no `Id`, as a warning this repo escalates to an
error.

A7 shipped this bug and the review caught it. The reason it is worth recording: **the same file already
contained the objection**, as the stated reason class-site `[MapProperty]` is excluded from the gate. The
reasoning was written down, then applied to one attribute and not its neighbour. Filter at the point the
class-scoped directive enters an endpoint-specific report, not per attribute.

## 3. Discriminate hooks on partial-ness, never on signature

`void Update(Src, Dst)` — an update-into mapping method — has *exactly* the `[AfterMap]` two-parameter hook
signature. So does a legitimate `void Fix(Src, Dst)` hook on the same class. Nothing about the shape separates
them; the only reliable discriminator is that a mapping method is a **partial declaration with no implementing
part**, which means it has no body, which means it cannot be a hook. That is what `DWARF091` checks, and it is
why it cannot over-fire on the legitimate hook. Pinned by
`HookTests.AfterMap_two_param_on_an_update_into_mapper_is_called_and_not_refused`.
