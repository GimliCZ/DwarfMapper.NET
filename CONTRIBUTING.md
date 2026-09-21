# Contributing

Thank you for digging in. DwarfMapper is licensed **GPL-2.0-only**.

By submitting a contribution you agree it is licensed under GPL-2.0-only and
that you have the right to submit it (per the Developer Certificate of Origin).
Sign your commits with `git commit -s`.

## Building & testing

You need **.NET SDK 10.0.101 exactly** — the whole solution targets `net10.0` (an older SDK fails with
`NETSDK1045`), and `global.json` pins the patch with `rollForward: disable`.

The pin is deliberate and it is strict: a source generator's output is compiler-version-sensitive, and the
suite compares generated code byte-for-byte, so "any 10.0.x" would mean contributor and CI can disagree
about what the build produces. CI installs the same exact version and asserts it matches `global.json`.
If you have a different patch, install `10.0.101` alongside it (side-by-side SDKs are supported); do not
loosen `global.json` locally. Bumping the pin means changing `global.json` **and** the `dotnet-version` in
every `setup-dotnet` step in the same commit.

```bash
dotnet build DwarfMapper.NET.sln -c Release
dotnet test  DwarfMapper.NET.sln -c Release
```

Enable the local pre-push gate once: `git config core.hooksPath scripts/git-hooks` (heavier pre-release checks live in `scripts/housekeeping.ps1`).

**Running the expensive CI tier on a pull request.** `mutation`, `deep-test`, `reproducible-build`, `package-size`, `cross-platform`, `preview-sdk-canary` and `bench-wall-time-alert` are nightly-only by default and show as *Skipped* on a PR — the mutation matrix alone budgets up to 350 minutes per leg across six legs. **Label the PR `full-ci` to run them all**, which is what a round-closing or release PR wants: the same checks the nightly runs, before the merge rather than the night after it. A label is used rather than `workflow_dispatch` because a dispatch runs against a branch, not against the merge result the reviewer is approving.

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
on files like the ~6.6k-line `MapperExtractor.cs`.

It's opt-in and requires the tool on your PATH:

```
dotnet tool install --global RoslynLens
```

The `.mcp.json` invokes the bare `roslyn-lens` command, so `~/.dotnet/tools` must be on your PATH
(the .NET SDK installer adds it by default). Nothing in the build or CI depends on it.
