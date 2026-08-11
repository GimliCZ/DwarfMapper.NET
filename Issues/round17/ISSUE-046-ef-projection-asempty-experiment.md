# ISSUE-046 — the deciding experiment: does `AsEmpty` translate in an EF Core projection?

**Status: experiment run, evidence below. The pick is still the maintainer's** — this file exists so the pick
is made against measurement instead of intuition.

## The question

`NullCollections` is one of the four options the projection resolver accepts and **silently ignores**
(`docs/generated/option-support-matrix.md` marks it `SILENT`). Two candidate resolutions were on the table:

1. **Honour `AsEmpty`** — emit the null guard in the projection too, so `.Map` and `.Project` mean the same thing.
3. **Document projection as AsNull-by-nature** — declare that a query provider cannot express the guard, and
   make the option's projection behaviour explicit rather than silent.

Candidate 3 is only honest if the guard genuinely *cannot* be expressed. That is a fact about EF Core, not a
design preference, and it had never been measured.

## Method

Throwaway console app, outside the repo (no dependency was added to DwarfMapper to answer this):
EF Core **10.0.11** + `Microsoft.EntityFrameworkCore.Sqlite`, real SQLite file, `Blog → Post` collection
navigation plus a genuinely nullable `Blog.Owner` reference navigation with its own collection.

**The method note that matters:** EF Core permits *client* evaluation in the final `Select`, so "it ran
without throwing" is **not** evidence of translation. Every case therefore prints the SQL actually issued,
and there are two controls:

| Control | Expectation | Result |
|---|---|---|
| untranslatable call in a `WHERE` | must FAIL, or the probe is blind | **FAILED** ✔ (probe has power) |
| untranslatable call in the final `SELECT` | expected to pass via client eval | **passed**, SQL was `SELECT "b"."Name"` — confirming the caveat above is real |

The first version of this probe had only the second control, concluded "even untranslatable code passes",
and would have been reported as a non-result. That is why both are here.

## Results

| # | Shape | Outcome | SQL issued |
|---|---|---|---|
| 1 | `b.Posts.Select(p => p.Title).ToList()` (what projection emits **today**) | **OK** | `SELECT "b"."Name", "b"."Id", "p"."Title", "p"."Id"` |
| 2 | `(b.Posts ?? new List<Post>()).Select(…).ToList()` — `??` on the **navigation** | **FAILS** — `InvalidOperationException: The LINQ expression 'p => p.Title' could not be translated` | none |
| 3 | `b.Posts.Select(…).ToList() ?? new List<string>()` — `??` on the **projected list** | **OK** | **identical to #1** |
| 4 | `b.Posts == null ? new List<string>() : b.Posts.Select(…).ToList()` | **OK** | wider: adds `"p"."Id", "p"."BlogId"` and a second `"p0"` join |
| 5 | `b.Owner == null ? new List<string>() : b.Owner.Tags.Select(…).ToList()` (**genuinely nullable** navigation) | **OK** | `SELECT "b"."Name", "o"."Id" IS NULL, "b"."Id", "o"."Id", "t"."Value", "t"."Id"` |
| 6 | same, but AsNull (`: null`) | **OK**, and returns `<null>` for the owner-less row | **identical to #5** |

## What the evidence says

**Candidate 3's premise is false.** `AsEmpty` *does* translate — cleanly, in the shape that matters (#5), and
to exactly the same SQL as `AsNull` (#6). The two differ only in the materialised value. So "a query provider
cannot express this" is not available as a reason.

**Candidate 1 is feasible, with one shape requirement and one caveat:**

- The guard must be written as a **null ternary on the source** (#4, #5), never as `??` applied to the
  navigation (#2) — that one is the only formulation EF refuses, and it refuses it loudly.
- Writing it as `?? new List<T>()` on the *projected list* (#3) is **free but vacuous**: EF elides it (the SQL
  is byte-identical to the unguarded #1), because a projected `.ToList()` is never null. It would look like
  the option was honoured while changing nothing.
- Guarding a **non-nullable** collection navigation (#4) costs a wider query for a branch that can never be
  taken. So the guard is worth emitting only where the source member is genuinely nullable — which is where
  the option means something anyway.

**Suggested reading, for the maintainer to accept or reject:** candidate 1, scoped — honour `NullCollections`
in projection *only* for nullable source members, emitting the ternary form. That removes a `SILENT` row from
the support matrix without pessimising queries where the null branch is dead.

## Reproducing

The probe was deliberately kept out of the repository, so nothing here adds an EF Core dependency. To re-run:
a console app with `Microsoft.EntityFrameworkCore.Sqlite`, the six shapes above, `LogTo` capturing
`DbLoggerCategory.Database.Command`, and both controls. The controls are not optional — without the first
one, the probe reports success for code that cannot be translated at all.
