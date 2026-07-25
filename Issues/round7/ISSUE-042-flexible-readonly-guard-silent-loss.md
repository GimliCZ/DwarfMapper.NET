# [ISSUE-042] Read-only silent-loss guard never fires under `NameConvention.Flexible`

| | |
|---|---|
| **Severity** | Medium (silent data loss — a "never silent" violation) |
| **Type** | Missing diagnostic |
| **Component** | Generator / MapperExtractor.Members |
| **Finding ID** | R7-2 |
| **Affects** | `src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.cs` |
| **Status** | **CONFIRMED and FIXED** — commit `0b35338` |

## What
The read-only silent-loss guard (DWARF007) queried `sourceGroups.ContainsKey(readOnly.Name)` with the RAW target
name. Under `NameConvention.Flexible`, `sourceGroups` is keyed by `NormalizeName` (underscore-stripped,
lowercased) with an `Ordinal` comparer — so the raw lookup always missed the normalized key. A source value that
could only reach a get-only destination member was therefore dropped with **no diagnostic**. Read-only members
are not in `WritableMembers`, so the auto-match loop never emits DWARF001 for them either — this guard was the
only signal, and under flexible it silently never fired. Every other `sourceGroups` lookup (MapValue shadows at
:403, auto-match at :491) normalizes; this line was the lone omission.

## Verification — and a process note
The external audit **reasoned** this from code inspection (it did not execute a repro). Confirming it took care:
a first repro using `[GenerateMap<Src,Dst>]` on an empty partial class did NOT activate flexible mode
(`first_name` failed to match `FirstName`), so the test passed with AND without the fix — a non-discriminating
test, the exact "asserts less than it claims" species this project guards against. Flexible matching is wired on
the **declared-partial-method** path; a repro there (snake_case `user_code` source → PascalCase get-only
`UserCode`) isolates the bug: DWARF007 fires only with the fix. Test watched failing without it.

## Fix (applied)
`sourceGroups.ContainsKey(flexible ? NormalizeName(readOnly.Name) : readOnly.Name)` — bringing the lone lookup
in line with its two siblings. Golden manifest unmoved; 0 warnings solution-wide.

## Regression test
`FlexibleReadOnlyGuardTests` — declared-method + Flexible + snake/Pascal get-only destination; asserts DWARF007.
