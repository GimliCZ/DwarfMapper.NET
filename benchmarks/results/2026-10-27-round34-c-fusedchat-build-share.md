# Round 34 C — DwarfMapper's share of a large build: FusedChat (2026-10-27)

**Setup.** A fresh copy of FusedChat's validation tree (`fc-r34val`). It is the owner's 32-project application, with
the round-32 per-site intents applied, and 10 projects reference DwarfMapper. The copy was repinned to
`1.1.0-r34val.1`, packed from `feat/round34`, whose product code is master `4a169a4`. FusedChat's own `global.json`
selects SDK 10.0.400, so this is the compiler a consumer would actually run.

**Method.** `dotnet build FusedChat.sln -c Release --no-restore --no-incremental -p:ReportAnalyzer=true`, with a
detailed file log and MSBuild's performance summary, on Windows (Ryzen 5 5600, 12 threads), under the heavy-test lock.
Every project compiles in every build.
- **Cold:** `dotnet build-server shutdown` first, so the compiler server and the generator start unjitted.
- **Warm:** the same build again, with the server left running from the cold build.

The pairs ran as cold-1, warm-1, cold-2, warm-2, cold-3, warm-3.

**Novelty check.** Nothing in `benchmarks/results` or `Issues/` timed a FusedChat build before. ReportAnalyzer was
used in rounds 21 and 22 only on this repository's own analyzers. The generator alone is measured (round 23 S3,
repeated in round 33): ~0.5 ms per mapped pair warm. What this adds is that generator's share of a real build.

## Results

| Build | Wall | All compiler calls (summed) | All generators | All analyzers | DwarfMapper's generators | Share of compiler time |
|---|---|---|---|---|---|---|
| cold-1 | 32.4 s | 57.1 s (33 calls) | 15.02 s | 27.06 s | 2.94 s | 5.1 % |
| cold-2 | 26.9 s | 53.4 s | 15.20 s | 24.89 s | 2.87 s | 5.4 % |
| cold-3 | 25.5 s | 50.6 s | 13.71 s | 24.81 s | 2.75 s | 5.4 % |
| warm-1 | 23.2 s | 39.9 s | 11.11 s | 19.87 s | 1.98 s | 5.0 % |
| warm-2 | 21.7 s | 42.5 s | 10.80 s | 21.50 s | 1.93 s | 4.5 % |
| warm-3 | 20.5 s | 47.6 s | 12.07 s | 21.65 s | 2.19 s | 4.6 % |

- **How it is counted.** DwarfMapper's generators run in 20 compiler calls per build: its 10 referencing projects
  and the projects that take the package transitively. Its time is the sum of `DwarfGenerator` and `MapToGenerator`
  over those calls. MapToGenerator never passes 1 ms.
- **The largest single call** is 0.72 s cold. That is the Omega project, whose `DtosMaps` is the 641-pair mapper.
- **What costs more in the same builds.** These are cold-1's totals per assembly, analyzers and generators together:

  | Assembly | Time |
  |---|---|
  | `Microsoft.VisualStudio.Threading.Analyzers.CSharp` | 12.10 s |
  | `Microsoft.CodeAnalysis.Razor.Compiler` | 8.27 s |
  | `Microsoft.CodeAnalysis.CSharp.NetAnalyzers` | 3.38 s |
  | `Microsoft.AspNetCore.App.Analyzers` | 3.23 s |
  | **DwarfMapper.Generator** | 2.95 s |
  | `Microsoft.CodeAnalysis.NetAnalyzers` | 2.33 s |
  | `xunit.analyzers` | 1.78 s |
  | `Microsoft.EntityFrameworkCore.Analyzers` | 1.09 s |

## Reading

- **DwarfMapper is about 5 % of the compiler's time in this application.** The times are summed over parallel
  compiler calls, so its share of the wall clock is at most its share of the critical path. Even the 9-11 % of wall
  time that its sum represents is an upper bound, not a saving anyone could collect.
- **Cold start costs ~0.8 s per cold build:** a median of 2.87 s cold against 1.98 s warm, summed over 20 calls. That
  is everything ReadyToRun could recover, and it would recover less in practice. Round 33's cold-JIT lead does not
  earn a round.
- **Where a slow FusedChat build actually goes:** `Microsoft.VisualStudio.Threading.Analyzers` alone costs four times
  DwarfMapper, and Razor about three times. Those are FusedChat's choices, so tuning them is the owner's call in that
  repository, not work for this one.
- **Not measured: the IDE.** Rider runs generators incrementally on every edit, and that is a different cost; the
  incremental cache is asserted by `GeneratorCacheAssert` and `GeneratorCompileCostTests`.
