# Contributing

Thank you for digging in. DwarfMapper is licensed **GPL-2.0-only**.

By submitting a contribution you agree it is licensed under GPL-2.0-only and
that you have the right to submit it (per the Developer Certificate of Origin).
Sign your commits with `git commit -s`.

## Ground rules
- Every source file starts with `// SPDX-License-Identifier: GPL-2.0-only`.
- Builds are warning-clean (`TreatWarningsAsErrors`). Fix analyzer findings;
  do not suppress without a justification comment.
- New mapping behavior requires: a generator snapshot test **and** a runtime
  integration test. A bug fix starts with a failing test.
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
