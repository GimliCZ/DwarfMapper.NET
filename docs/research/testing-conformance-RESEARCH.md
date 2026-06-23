<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Testing & conformance research — session handoff (RESUME ME)

**Status:** deep-research run launched and ~95% done; the final synthesis agent stalled on a
StructuredOutput payload-size limit before emitting the polished report. All underlying research
(searches, fetched sources, adversarially-verified claims) **is captured** in this folder. Next
session: either resume the workflow for the auto-synthesized report, or synthesize directly from
`_claims-digest.md` (104 verified claims) using the plan at the bottom.

## Scope (locked with the user)
- **Goal:** gap analysis vs. *our* suite → landscape survey + technique→do-we?→gap table + prioritised adopt-list.
- **Breadth:** the **whole .NET ecosystem** (mappers, serializers, Roslyn source generators, analyzers).
- **10 test categories:** (1) mapping/behavioral correctness & oracle parity, (2) generated-source
  snapshot/approval, (3) **determinism & incremental-generator cacheability**, (4) fuzzing / property-based,
  (5) combinatorial / pairwise / feature-interaction, (6) adversarial / robustness / security,
  (7) diagnostics & analyzer conformance + release tracking, (8) mutation testing, (9) cross-version /
  multi-target conformance, (10) differential testing.

## Saved artifacts (this folder)
- `_claims-digest.md` — **104 deduped, verified claims** with sources, grouped by category (the substance).
- `_raw-harvest.jsonl` — raw harvested finding/claim objects (164) + 42 unique source URLs (backup).
- This file — scope, our-suite grounding, key sources, resume command, synthesis plan.

## How to resume the workflow (gets the auto-synthesized report)
```
Workflow({
  scriptPath: "/home/jouda/.claude/projects/-home-jouda-RiderProjects-DwarfMapper-NET/d17bc728-c97b-42d8-9b76-07d801a9ad71/workflows/scripts/deep-research-wf_af90321c-5c6.js",
  resumeFromRunId: "wf_af90321c-5c6"
})
```
Completed agents return cached results instantly; only the failed synthesis tail re-runs. If it stalls
again on payload size, skip it and synthesize from `_claims-digest.md` directly.

## Our current suite — grounded inventory (for the gap table)
Confirmed locally on 2026-06-21 (master @ 57d92fc):

| Best-practice technique | DwarfMapper today | Verdict |
|---|---|---|
| Snapshot/approval (Verify + Verify.SourceGenerators) | 86 refs, `GeneratorTestHarness` (CSharpGeneratorDriver) | ✅ strong |
| Oracle / differential parity | `GraphOracleComparer`, `TopologyOracleFuzzTests`, `CorpusTests` | ✅ strong |
| Combinatorial feature-interaction | full **2^16 power-set** (exceeds pairwise/PICT) | ✅ exceeds |
| Adversarial (cycles, depth, over-posting) | SetNull/FlattenGraph/Hetero adversarial suites; depth guard | ✅ strong |
| Diagnostics conformance + release tracking | `AssemblyScanTests`, `RuntimeCoverageScanTests`, `TestTheTestsScanTests`; AnalyzerReleases tracked | ✅ strong |
| Golden fixtures / corpus parity | `GoldenFixturesRuntimeTests`, `DwarfMapper.CorpusTests` | ✅ strong |
| Round-trip property verification | `[RoundTrip]` + `RoundTrip.Verify`, seeded `ObjectFactory`/`Fuzzer` | ✅ strong |
| AOT/trim gate + coverage gate (CI) | `aot-trim-gate` matrix, generator coverage gate | ✅ strong |
| **Incremental-generator cacheability** (`WithTrackingName` → assert `Cached`/`Unchanged` across driver runs) | **0 references** | 🔴 **GAP** |
| **Lowest-supported-Roslyn matrix** | single pin `Microsoft.CodeAnalysis.CSharp 4.14.0` | 🟠 **GAP** |
| **Mutation testing** (Stryker.NET) | none (home-grown coverage/"test-the-tests" instead) | 🟠 **GAP** |
| **Property-based w/ shrinking** (FsCheck/CsCheck) | hand-rolled seeded `Fuzzer` (no shrinking → no minimal counterexample) | 🟠 **GAP** |

## Load-bearing sources (from the harvest)
- Andrew Lock source-generator series — **part 10 (cacheability testing)**, part 9 (perf pitfalls), part 2 (snapshot testing), part 14 (multi-SDK-version support). andrewlock.net
- Mapperly test suite — `IncrementalGeneratorTest.cs`, `TestHelper.cs`, `Riok.Mapperly.Tests.csproj`, docs/contributing/tests. github.com/riok/mapperly
- Roslyn SDK `Microsoft.CodeAnalysis.Testing` README (CSharpAnalyzerVerifier / SourceGenerators.Testing). github.com/dotnet/roslyn-sdk
- Roslyn `docs/features/incremental-generators.md`; analyzer `ReleaseTrackingAnalyzers.Help.md` (RS2000/RS2008).
- Verify.SourceGenerators readme. CsCheck (AnthonyLloyd), FsCheck StatefulTesting, fsharp-hedgehog. Xunit.Combinatorial (AArnott). microsoft/pict. Stryker.NET. System.Text.Json `ThreatModel.md`.
- safia.rocks 2025-09-29 "source generators testing".

## Next-session synthesis plan
1. (Optional) resume the workflow for the polished report; else read `_claims-digest.md`.
2. Produce: **(a)** per-category landscape survey (cite the sources above), **(b)** the technique→who-uses-it→do-WE?→gap table (extend the inventory above with citations), **(c)** prioritised adopt-list.
3. Likely top adopt items to detail with concrete steps:
   - **Incremental cacheability test** — add `[GeneratorTest]` that runs the driver twice with `WithTrackingName` on pipeline stages and asserts every step is `Cached`/`Unchanged` on the 2nd run; assert the model types are equatable (we already have `EquatableArray`). Mirror Mapperly's `IncrementalGeneratorTest.cs`.
   - **Lowest-supported-Roslyn CI leg** — float-test against the minimum `Microsoft.CodeAnalysis.CSharp` we claim to support, not just 4.14.0.
   - **Stryker.NET mutation run** (at least on the generator's core resolver) to validate the suite catches injected faults.
   - **CsCheck** (preferred over FsCheck for C#) for shrinking property-based tests — minimal counterexamples on fuzz failures.
4. Decide what's worth implementing vs. documenting as "deliberately not done."
