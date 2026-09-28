# T17 — EF Core query precompilation and NativeAOT: which DwarfMapper shapes survive (measured 2026-09-27)

Rig: a throwaway console project OUTSIDE the repository (session scratch), net10.0, SDK 10.0.400,
`Microsoft.EntityFrameworkCore.Sqlite` / `.Design` / `.Tasks` 10.0.9, `InterceptorsNamespaces` including
`Microsoft.EntityFrameworkCore.GeneratedInterceptors`, consuming DwarfMapper as a locally packed NuGet package
(`1.1.0-r31probe`, `-r31probe2`) built from `feat/round31`. Model: `Order { Id, Open, Customer? (record, navigation),
List<Line> }` → `OrderDto { Id, CustomerDto?, List<LineDto> }`, an in-memory SQLite database seeded with three orders
(one with a null customer and no lines).

## 1. Translation (T02's and T16's pending EF/SQLite rows)

| query | SQL | rows |
|---|---|---|
| `mapper.Project(db.Orders)` | `SELECT "c"."Id" IS NULL, "c"."Name", "o"."Id", … FROM "Orders" LEFT JOIN "Customer" … LEFT JOIN "Line" …` | 3, null customer mapped to null, empty lines to `[]` |
| `db.Orders.Where(o => o.Open).OrderByDescending(o => o.Id).Select(OrderMapper.ProjectExpression)` | same joins + `WHERE "o"."Open" ORDER BY "o"."Id" DESC` | 2, correct order |

- **T02:** the reference-typed null guard `(object)(__s.Customer) == null ? null : new CustomerDto { … }` over a
  RECORD navigation translates to `"c"."Id" IS NULL` in a LEFT JOIN. Recorded honestly: EF Core 10 also translated the
  pre-T02 form — the hand-written control query `o.Customer == null` compiles to `op_Equality` on the record and ran
  fine. The T02 fix therefore did not unbreak EF; it removed a user-method call from every tree (LINQ-to-objects runs
  it; other providers may not recognise it) and fixed the CS0034 compile error with two operators.
- **T16:** `{Method}Expression` composes with `Where`/`OrderByDescending` into ONE SQL statement.

## 2. Precompilation (`dotnet ef dbcontext optimize --precompile-queries --nativeaot`)

| call site | result |
|---|---|
| hand-written `db.Orders.Select(o => new OrderDto { … }).ToListAsync()` | precompiled (interceptors emitted) |
| `mapper.Project(db.Orders).ToListAsync()` — inline, via a local, or with `.AsEnumerable()` | **refused**: `QueryPrecompilationError … Dynamic LINQ queries are not supported when precompiling queries` |
| `db.Orders.Where(…).OrderByDescending(…).Select(OrderMapper.ProjectExpression).ToListAsync()` | **precompiled**: interceptors for `Where`, `OrderByDescending`, `Select` and `ToListAsync` at that call site |

Why: the precompiler analyses a query only where its whole operator chain is written at the call site; a call into
another method that builds the `Select` hides it. A static expression PROPERTY passed to `Select` is fine. One
precompilation error aborts the whole `optimize` run, so a single `Project(...)` call blocks interceptors for every
other query in the project.

Two incidental findings:
- `Microsoft.EntityFrameworkCore.Sqlite` 10.0.9 pulls `SQLitePCLRaw.lib.e_sqlite3` 2.1.11, which carries a HIGH
  advisory (GHSA-2m69-gcr7-jv3q). This repository's NuGet audit fails restore on that, which is the concrete reason
  the rig stays outside the repo rather than becoming a test project.
- With DwarfMapper consumed by `ProjectReference` + `OutputItemType="Analyzer"`, EF's MSBuild workspace loaded a stale
  Debug generator (the Release one was current); packaging removed the variable. Consumers get the package, so the
  package is what was measured.

## 3. Decision (task step 4)

`DWARF115` (Warning) on every projection method when the consumer sets `PublishAot` AND references EF Core, naming the
precompilable shape. `PublishAot` reaches the generator through a new package file, `build/DwarfMapper.props`
(+ `buildTransitive/`), containing only `<CompilerVisibleProperty Include="PublishAot"/>`. Verified end to end: the
packed `-r31probe2` in the rig with `<PublishAot>true</PublishAot>` reports DWARF115 on both projection methods; the
same rig without it, or a compilation without EF Core, reports nothing (unit-pinned in `EfAotProjectionTests`).
README's projection section documents the precompilable shape.
