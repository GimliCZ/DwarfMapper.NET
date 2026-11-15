# Contributing

Thank you for digging in. DwarfMapper is licensed **GPL-2.0-only**.

By submitting a contribution you agree it is licensed under GPL-2.0-only and
that you have the right to submit it (per the Developer Certificate of Origin).
Sign your commits with `git commit -s`.

## Building & testing

You need **.NET SDK 10.0.404 exactly** — the whole solution targets `net10.0`, and `global.json` pins the patch
with `rollForward: disable`, so any other SDK, older or newer, fails at SDK resolution before the build starts.

The pin is deliberate and it is strict: a source generator's output is compiler-version-sensitive, and the
suite compares generated code byte-for-byte, so "any 10.0.x" would mean contributor and CI can disagree
about what the build produces. CI installs the same exact version and asserts it matches `global.json`.
If you have a different patch, install `10.0.404` alongside it (side-by-side SDKs are supported); do not
loosen `global.json` locally. Bumping the pin means changing `global.json` **and** the `dotnet-version` in
every `setup-dotnet` step that names the pinned version, in the same commit. Four CI jobs deliberately name
another one and are not part of the bump: `roslyn-forward-compat` (`10.0.x`), `toolchain-floor` (`10.0.1xx`, the
oldest band the README promises), `preview-sdk-canary` (`11.0.x`) and `net11` (`11.0.100`).

```bash
dotnet build DwarfMapper.NET.sln -c Release
dotnet test  DwarfMapper.NET.sln -c Release --filter "Category!=Perf"
```

That is the whole suite, the one way every CI job runs it: every test at its one population size
(`tests/Shared/TestPopulations.cs`), the surface matrix included. Two things run on their own. The wall-clock
ratio tests time code, so they run alone, one project at a time: `dotnet test DwarfMapper.NET.sln -c Release
--no-build -m:1 --filter "Category=Perf"`. The complete power set of feature combinations (262,144
compilations) is housekeeping's stage 2 and CI's `fuzz-exhaustion` job.

**The generator is built twice.** `src/DwarfMapper.Generator` is compiled against Roslyn 5.0 and
`src/DwarfMapper.Generator.Roslyn512` compiles the same files against Roslyn 5.12, plus its own folder; the package
ships both in Roslyn-versioned analyzer folders and the SDK loads one. A file named `*.Floor.cs` belongs to the 5.0
build only, a file in the Roslyn512 folder to the 5.12 build only — the seam is files, never `#if`. Everything the
5.12 build alone does (C# 15 union mapping) is tested in `tests/DwarfMapper.Generator.Roslyn512.Tests`, which runs
Roslyn 5.12 in process; the same project proves the golden corpus is byte-identical on that build, so a change to a
shared file that alters either build's output fails there. Every other project tests the 5.0 build.

The union examples, `samples/DwarfMapper.Unions`, need the .NET 11 SDK and are not in the solution:
`cd samples/DwarfMapper.Unions && dotnet run` (their own `global.json` selects SDK 11). The `net11` job runs them from
the packed package and runs the consumers through the 5.12 build.

Enable the local pre-push gate once: `git config core.hooksPath scripts/git-hooks` (heavier pre-release checks live in `scripts/housekeeping.ps1`).

**Two more tools on your PATH**, beyond the SDK:
- **PowerShell 7 (`pwsh`).** `scripts/housekeeping.ps1` and the gate functions in `scripts/gate-checks.ps1` are PowerShell, and the test suite drives those functions through `pwsh`, so a machine without it fails those tests rather than skipping them.
- **Python 3**, standard library only. `extracted-reach.py`, `seam-reach.py`, `repro-pack-check.py`, `verify-extraction-bodies.py` and `verify-extraction-sites.py` in `scripts/` are called by their PowerShell wrappers, and several CI steps run `python3` inline.

**Running the expensive CI tier on a pull request.** `mutation`, `deep-test`, `perf-tests`, `fuzz-exhaustion`, `isa-matrix`, `reproducible-build`, `cross-platform`, `preview-sdk-canary` and `bench-wall-time-alert` are nightly-only by default and show as *Skipped* on a PR — the mutation matrix alone budgets up to 350 minutes per leg across six legs. **Label the PR `full-ci` to run them all**: the label runs the full CI job set on the pull request — the same checks the nightly runs, before the merge rather than the night after it. Use it for a release PR or any change that needs the expensive checks. Adding a label needs triage (or write) access to the repository; if you contribute from a fork without it, ask a maintainer to add the label. A label is used rather than `workflow_dispatch` because a dispatch runs against a branch, not against the merge result the reviewer is approving. The label takes effect from the next push: CI runs on a pull request's `opened`, `synchronize` and `reopened` events, not on `labeled`, so `gh pr create --label full-ci`, which adds the label just after opening, runs the cheap tier first.

## Quality gates

The mutation and coverage badges at the top of the README are published by CI and read live; they are not committed
numbers. The mutation badge is the Stryker dashboard's aggregate over the six legs of `master` — generator,
doctooling, runtime, code fixes, pipeline and testing. The nightly `mutation` matrix uploads each leg as its own
module, and only after that leg's report has passed the vacuity, tree-cleanliness and decontamination checks. Read
it as one number over six differently scoped populations: each leg covers only part of `src/`. It is also the raw
score. The dashboard has no notion of a proven-equivalent mutant, so where a leg carries adjudicated equivalent
mutants, its raw figure sits above the score the project holds itself to. The thresholds that fail a build are in
the `stryker-config*.json` files. Coverage is Codecov's live figure for `master`; the enforced per-assembly floors
live in `scripts/housekeeping.ps1`.

## Ground rules
- Every source file starts with `// SPDX-License-Identifier: GPL-2.0-only`.
- Builds are warning-clean (`TreatWarningsAsErrors`). Fix analyzer findings;
  do not suppress without a justification comment.
- New mapping behavior requires: a generator snapshot test **and** a runtime
  integration test. A bug fix starts with a failing test.
- User-facing behavior also needs a **documentation snippet**: an example under `samples/` carrying a
  `// <snippet: id>` region, quoted from a `<!-- snippet: id -->` marker in the prose that describes it.
  Hand-written ```csharp fences are refused by `DocFenceScanTests` — code in the docs is an extract of code
  that compiles and runs, so it cannot describe an API that no longer exists. If no sample fits yet, mark the
  fence `<!-- fence-exempt: reason -->`; the reason is required, so a gap is recorded rather than bypassed.
- The generator must remain reflection-free and AOT/trim-safe; the AOT sample
  must publish clean.

## Optional: Roslyn semantic tooling for AI assistants

This repo ships an `.mcp.json` that registers [**roslyn-lens**](https://github.com/jfmeyers/roslyn-lens)
— a token-efficient, Roslyn-based MCP server for Claude Code (and compatible clients). It gives
compiler-grade navigation (`find_references`, `find_callers`, `get_type_hierarchy`,
`detect_antipatterns`, …) over this solution instead of reading whole `.cs` files, which is a large win
on types like `MapperExtractor`, now split into `MapperExtractor*.cs` partials (~19k lines in total).

It's opt-in and requires the tool on your PATH:

```
dotnet tool install --global RoslynLens
```

The `.mcp.json` invokes the bare `roslyn-lens` command, so `~/.dotnet/tools` must be on your PATH
(the .NET SDK installer adds it by default). Nothing in the build or CI depends on it.
