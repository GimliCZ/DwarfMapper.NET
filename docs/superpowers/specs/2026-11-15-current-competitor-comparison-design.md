<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 35 T25 — DwarfMapper against today's Mapperly and Mapster: performance, usage, security, validation, docs and samples

## Intent

The owner, 2026-11-14: "After you are done, could you run entire performance and usage comparison against dwarf
mapper? Of course up to date versions (not automapper of course) as next step? Target will be performance, security,
validations, docs and samples validations."

`docs/COMPARISON.md` was last measured against Mapperly 4.3.1, Mapster 10.0.8 and AutoMapper 14.0.0, on .NET 10.0.1,
in August–September 2026. Its performance table is one Windows run; many of its capability cells were read from the
libraries' behaviour once and are held by no test. The owner wants it current, without AutoMapper, and widened from
speed to what a team choosing a mapper actually weighs: is it safe, does it catch mistakes, and can the documentation
and samples be trusted.

## Versions (my call: latest STABLE, measured 2026-11-14 on nuget.org)

| Library | Measured | Notes |
|---|---|---|
| DwarfMapper | this branch (1.0.2-rc.1 + round 35) | both generator builds; .NET 10 and .NET 11 |
| Riok.Mapperly | **4.3.1** | latest stable; 5.0.0-next.13 is a prerelease |
| Mapster | **10.0.14** | latest stable (the repository pins 10.0.8); 10.1.0-pre04 is a prerelease |
| AutoMapper | not measured | the owner's exclusion; 14.0.0 is the last MIT release and carries three advisories |

Prereleases appear only as labelled data points in capability probes (as T24's union probe did), never in a
performance table. AutoMapper's existing columns are retired from the tables, with one line saying where its last
numbers are (`benchmarks/results/`), not deleted from history.

## Goals

1. **Performance.** Re-measure the whole `DwarfMapper.Benchmarks` suite (every category, every probe class) and
   `DwarfMapper.UnionBenchmarks` with Mapster 10.0.14: DwarfMapper, Mapperly, Mapster, hand-written. Windows (the
   machine every published figure came from) and Linux (sdk container, CI's platform). Time and allocation. Claims
   only where every run agrees, as T24 did; anything else reported as the spread it is.
2. **Usage.** For one realistic set of maps — flat, nested, collection, rename, ignore, converter, constructor/record,
   polymorphic, cycles, projection, update-into — what each library needs written, compiled side by side in one
   project so the ceremony count is measured, not estimated.
3. **Security.** Executable probes per library: a deep graph (stack depth), a reference cycle (each default), re-entry
   through user code (the CVE-2026-128081 class T20 fixed here), an unregistered runtime subtype, null into
   non-nullable, NativeAOT and trimming (publish and run), reflection and dynamic code in the shipped path, the
   dependency footprint and licences.
4. **Validation.** For the mistakes people actually make — an unmapped target member, an unmapped source member,
   incompatible types, nullable into non-nullable, a missing nested map, a missing derived arm, an enum member with no
   counterpart, a read-only target, a constructor parameter nothing supplies, a C# 15 union — what each library
   reports and WHEN: build error, build warning, configuration-time failure, runtime throw, or silence.
5. **Docs and samples.** DwarfMapper's own: every sample builds and runs its own check; every C# block in the docs is
   either a snippet injected from a compiled sample or itself compiles; every relative link resolves; every cell of
   `docs/COMPARISON.md` is backed by a test or by a dated measurement in `benchmarks/results/`.

## Non-goals and anti-goals

- No AutoMapper measurement (owner).
- No scoring of other projects' documentation; the docs goal is about ours.
- Not a marketing table: a cell that loses stays a loss (T24 kept the list-fill loss); a cell nobody measured is `~`.
- No DwarfMapper behaviour change inside T25. A defect found becomes its own task with a RED -> GREEN test.

## Approaches considered

1. **Prose refresh** — re-run the benchmarks, re-read the libraries' docs, rewrite COMPARISON.md. Cheap; repeats the
   weakness the owner is pointing at (cells nobody holds).
2. **Executable comparison (chosen).** Every capability, validation and security cell that a library can be driven
   for in-process becomes a test in `tests/DwarfMapper.DifferentialTests` (it already runs DwarfMapper and Mapperly
   side by side; Mapster joins, AutoMapper's role there is unchanged). Build-time cells run the generators in process
   (DwarfMapper's harness; Mapperly's analyzer assembly from its package). What cannot run in a test — NativeAOT
   publishes, the performance numbers — becomes a dated measurement with its script in the rig and its numbers in
   `benchmarks/results/`. The day a library changes behaviour, a test goes red instead of a document going stale.
3. **A separate comparison repository.** Clean, but the evidence would leave the repository whose claims it backs.

## Deliverables

- `Directory.Packages.props`: Mapster 10.0.8 -> 10.0.14 and BenchmarkDotNet 0.14.0 -> 0.15.8, cherry-picked from T9's
  one-package commits (eb18412, 62768af), so this branch measures what T9 will ship.
- `tests/DwarfMapper.DifferentialTests`: `ValidationMatrixTests`, `SecurityMatrixTests`, Mapster in
  `LoudRatherThanSilentTests`; each cell named for the library and the case. Build-time cells run the generators in
  process: DwarfMapper's through its harness, Mapperly's from the analyzer assembly its package ships (Roslyn's own
  `AnalyzerFileReference`, the way any analyzer test loads one). A case that can end in a stack overflow runs in a
  child process — a `StackOverflowException` cannot be caught and would end the test host — and the cell is how that
  process ended.
- `benchmarks/results/2026-11-1x-round35-t25-comparison.md`: method, both platforms' tables, the probes' outputs.
- `docs/COMPARISON.md`: rewritten around the current versions; every cell carries its evidence.
- A docs-and-samples check (a script run by CI, or tests) and its findings.
- `Issues/round35/ROUND35-TASKS.md`: T25 and any defects it finds as their own rows.

## Order

1. Version bumps; the suite and the allocation baseline re-proven (DwarfMapper's own pins must not move).
2. Validation and security matrices (tests first — they decide what the document may say).
3. Usage project.
4. Docs and samples check.
5. Performance runs, last, on a quiet machine, both platforms.
6. COMPARISON.md and the results document.
