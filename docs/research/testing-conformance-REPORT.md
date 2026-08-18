<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Testing & conformance — .NET ecosystem gap analysis for DwarfMapper.NET

Scope: how the **whole .NET ecosystem** (Roslyn source generators, analyzers, object mappers, source-gen
serializers) tests and conformance-checks compile-time/codegen libraries, mapped against DwarfMapper's
current suite. Synthesised from the deep-research run (`wf_af90321c-5c6`; 104 verified claims in
`_claims-digest.md`) plus a local audit of `tests/**` on master @ 57d92fc.

## Executive summary

DwarfMapper is already at or above best-in-class on **most** axes: Verify/Verify.SourceGenerators
snapshotting, a CSharpGeneratorDriver harness, oracle/topology differential fuzzing, **full 2^16 power-set**
combinatorial testing (stronger than the pairwise that PICT/Xunit.Combinatorial settle for), adversarial
cycle/depth suites, diagnostics-conformance + analyzer release tracking, corpus parity, and round-trip
property verification. Its `MaxDepth=64` default + `1000` hard cap even mirrors System.Text.Json's
documented DoS threat model.

Three concrete gaps remain, all of which best-in-class .NET projects (Mapperly especially) cover. The
fourth on the original list — mutation testing — has since been adopted and is kept below as a closed row
rather than deleted, so the numbering of the adopt-list still resolves:

1. **🔴 Incremental-generator cacheability testing** — none. The single most important missing test.
2. **🟠 Minimum-supported-Roslyn coverage** — a single floor (`Microsoft.CodeAnalysis.CSharp 5.0.0`), not a
   matrix. CI does now carry a `roslyn-forward-compat` leg that builds the floor on a *newer* host SDK and
   answers the load question (`CS9057`); it does **not** answer the other direction, and it is still
   `continue-on-error: true` pending its first green run.
3. **🟢 Mutation testing (Stryker.NET)** — **adopted.** Three configs (runtime, doc-tooling, generator);
   the runtime leg runs nightly in CI with `break: 61`, unfiltered, scoring 61.02% on 111 mutants.
4. **🟠 Property-based testing with shrinking (CsCheck)** — our seeded `Fuzzer` finds failures but can't
   minimise them to a smallest counterexample.

## Per-category landscape (what the .NET ecosystem does)

1. **Mapping/behavioral correctness & oracle parity** — Mapperly keeps `*.IntegrationTests` separate from
   generator-output tests, asserting actually-mapped objects at runtime. ✅ We match (IntegrationTests +
   CorpusTests + GraphOracleComparer).
2. **Generated-source snapshot/approval** — Verify + Verify.SourceGenerators is the de-facto standard
   (`VerifySourceGenerators.Enable()` in a `[ModuleInitializer]`; one snapshot per `GeneratedSources`
   output + a diagnostics info file). The Roslyn-team canonical harness is `Microsoft.CodeAnalysis.Testing`
   (`CSharpAnalyzerVerifier`, `DiagnosticResult` with line/col pinning; used by roslyn/efcore/runtime).
   ✅ We use Verify.SourceGenerators + a custom driver harness. *Optional:* `Microsoft.CodeAnalysis.Testing`
   gives declarative line/column-pinned diagnostic assertions we don't currently make.
3. **Determinism & incremental cacheability** — Roslyn *requires* deterministic transforms; the driver
   caches outputs for previously-seen inputs **only if** pipeline values have value-equality (records /
   `EquatableArray`; `ImmutableArray<T>`/`SyntaxNode`/`ISymbol` silently break caching). The canonical test:
   run the generator, clone the compilation, run again with `trackIncrementalGeneratorSteps: true`, and
   assert every tracked step's `IncrementalStepRunReason` is `Cached`/`Unchanged` (not `New`). **Mapperly
   does exactly this** (issue #72; `IncrementalGeneratorTest.cs`). 🔴 **We do not.**
4. **Fuzzing / property-based** — **CsCheck** is the C#-native recommendation (random *integrated*
   shrinking, seed-preserving repro, plus built-in model-based, metamorphic, differential, and concurrency
   modes); Hedgehog (integrated shrinking, v2.0.0 Dec 2025) and FsCheck (needs separate shrinkers) are the
   F#-rooted alternatives. 🟠 Our `Fuzzer` is seeded but has no shrinking.
5. **Combinatorial / pairwise** — PICT (Microsoft) and Xunit.Combinatorial (`[CombinatorialData]` /
   `[PairwiseData]`) frame the tradeoff as exhaustive vs pairwise-to-avoid-explosion. ✅ We run the full
   power-set, so we **exceed** this; Xunit.Combinatorial could only formalise/replace hand-rolled loops.
6. **Adversarial / robustness / security** — STJ's threat model: bounded O(n), `MaxDepth` guard, malformed
   input → clear exception, opt-in permissive/reference-preservation modes; over-posting mitigated by
   validating untrusted graphs. ✅ We align (depth guard, cycle/SetNull adversarial suites, over-posting
   protection by design).
7. **Diagnostics & analyzer conformance** — `AnalyzerReleases.Shipped.md`/`Unshipped.md` (RS2000/RS2008),
   move-on-release; `DiagnosticResult` pins locations; harness supports `AdditionalFiles`. ✅ We do release
   tracking + AssemblyScan (every DWARF emitted+tested) — this is a strength.
8. **Mutation testing** — Stryker.NET (`dotnet-stryker` global tool) injects mutants to "test the tests."
   🟢 **Adopted.** A nightly CI leg on the shipped runtime assembly, unfiltered, `break: 61`. The coverage
   gate and `TestTheTestsScan` remain, now as complements rather than substitutes.
9. **Cross-version / multi-target** — Mapperly tests runtime across net7/net48, uses a `VersionedSnapshot`
   attribute for per-TFM snapshots, and ships multi-Roslyn analyzer subfolders
   (`analyzers/dotnet/roslyn4.4/cs`, `roslyn4.11/cs`; SDK loads the highest supported). Andrew Lock cautions
   full multi-Roslyn-targeting is "substantial work — only if necessary." 🟠 We build against a single floor
    (Roslyn 5.0.0) and carry one forward-compatibility leg, not a matrix.
10. **Differential testing** — CsCheck supports keeping a reference impl alongside to prove a refactor
    produces identical results. ✅ Partial (CorpusTests / oracle comparers); CsCheck would formalise it.

## Gap table

| Technique | Best-in-class .NET exemplar | DwarfMapper today | Gap? |
|---|---|---|---|
| Snapshot/approval of generated source | Verify.SourceGenerators (Mapperly) | 86 refs + driver harness | ✅ no |
| Oracle / differential parity | Mapperly IntegrationTests; CsCheck diff mode | GraphOracle/Topology fuzz, Corpus | ✅ no |
| Combinatorial feature-interaction | PICT / Xunit.Combinatorial (pairwise) | full 2^16 power-set | ✅ exceeds |
| Adversarial / DoS / over-posting | System.Text.Json threat model | depth guard + adversarial suites | ✅ no |
| Diagnostics conformance + release tracking | Roslyn AnalyzerReleases + DiagnosticResult | AssemblyScan + tracked releases | ✅ no |
| **Incremental cacheability** | **Mapperly `IncrementalGeneratorTest.cs`** | **0 references** | 🔴 **yes** |
| **Min-supported-Roslyn coverage** | Mapperly multi-Roslyn folders | single pin 4.14.0 | 🟠 yes |
| **Mutation testing** | Stryker.NET | nightly CI leg, `break: 61`, unfiltered | 🟢 adopted |
| **PBT with shrinking** | CsCheck (integrated shrink) | seeded Fuzzer, no shrink | 🟠 yes |
| Declarative line/col diagnostic asserts | Microsoft.CodeAnalysis.Testing | ID-only assertions | 🟡 optional |

## Prioritised adopt-list

1. **🔴 Incremental cacheability test (do first; small, high-value).** Add a test that builds a
   `CSharpGeneratorDriver` with `GeneratorDriverOptions(trackIncrementalGeneratorSteps: true)`, runs it,
   clones+perturbs the compilation, runs again, and asserts each tracked step's `Reasons` are
   `Cached`/`Unchanged` (not `New`). Requires `WithTrackingName(...)` on the pipeline stages in
   `DwarfGenerator`/`MapperExtractor`. We already use `EquatableArray`, so the model side is half-done —
   this test would *prove* the "deterministic" property the README sells. Mirror `riok/mapperly`'s
   `IncrementalGeneratorTest.cs`. Also add a guard asserting no `ISymbol`/`SyntaxNode`/`Compilation` leaks
   into pipeline outputs.
2. **🟠 Minimum-Roslyn CI leg.** Decide the floor `Microsoft.CodeAnalysis.CSharp` version we claim to
   support and add a CI matrix leg that restores/builds the generator against it (not full multi-targeting —
   just a compatibility smoke gate). Cheap insurance against using APIs newer than consumers' SDKs.
3. **🟢 Stryker.NET — DONE, and it went further than this item asked.** Three configs exist
   (runtime, doc-tooling, generator). The runtime leg runs nightly in CI, **unfiltered**: scoping it to the
   23 classes that kill something cuts it to ~3 minutes, and was deliberately NOT adopted, because a leg
   that only runs its own killers stops being able to discover a new one. `break: 61` is the honest score on
   the mutant set — the earlier 66.95% was inflated by `Timeout` being credited as detected.
4. **🟠 CsCheck for shrinking PBT.** Port the hottest `Fuzzer`/oracle properties (round-trip, topology
   preservation) to CsCheck to get minimal counterexamples + seed-reproducible failures; its model-based and
   metamorphic modes also fit graph mapping well.
5. **🟡 (Optional) Microsoft.CodeAnalysis.Testing** for a few diagnostics where exact line/column matters.

## Key sources
Andrew Lock source-gen series (parts 2/9/10/14); riok/mapperly tests (`IncrementalGeneratorTest.cs`,
`TestHelper.cs`, contributing/tests, issue #72); Roslyn `incremental-generators.md` &
`ReleaseTrackingAnalyzers.Help.md`; `Microsoft.CodeAnalysis.Testing` README; Verify.SourceGenerators readme;
CsCheck (`Comparison.md`, stateful), fsharp-hedgehog, FsCheck; microsoft/pict; Xunit.Combinatorial;
Stryker.NET docs; System.Text.Json `ThreatModel.md`. Full list + 104 claims in `_claims-digest.md`.
