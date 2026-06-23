# Research claims digest — 104 deduped verified claims

_Auto-extracted from the deep-research run (run id wf_af90321c-5c6). Category numbers (CatN) map to the 10 test categories in the original query._


## Category 1

- **Cat1/2: Mapperly uses Verify plus Verify.SourceGenerators to snapshot diagnostics and source via TestSourceBuilder plus TestHelper; integration tests verify mapped objects.**
  - confidence: high
  - evidence: Mapperly docs. Merged claims 10,11.
  - https://mapperly.riok.app/docs/contributing/tests/

## Category 2

- **Cat2/7: Microsoft.CodeAnalysis.Testing is canonical: CSharpAnalyzerVerifier, CSharpCodeFixVerifier, SourceGenerators.Testing/CodeRefactoring.Testing packages, declarative DiagnosticResult API; adopted by roslyn, runtime, efcore.**
  - confidence: high
  - evidence: README plus NuGet Used By. Merged claims 2,3,4,5.
  - https://github.com/dotnet/roslyn-sdk/blob/main/src/Microsoft.CodeAnalysis.Testing/README.md
  - https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp.Analyzer.Testing
- **Cat2: Verify plus Verify.SourceGenerators wraps Roslyn CSharpGeneratorDriver, one verified file per GeneratedSources output plus a diagnostics info file; works for ISourceGenerator and IIncrementalGenerator.**
  - confidence: high
  - evidence: README. Merged claims 0,1.
  - https://github.com/VerifyTests/Verify.SourceGenerators/blob/main/readme.md

## Category 3

- **Cat3: Roslyn incremental-generators.md requires determinism, driver caches/reuses prior outputs for seen inputs, use value types and equatable data model as final Register-Output item.**
  - confidence: high
  - evidence: Roslyn doc. Merged claims 6,7,8,9.
  - https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md

## Category 4

- **Cat4: CsCheck supports stateful and parallel testing; Hedgehog integrated shrinking; FsCheck needs separate shrinkers.**
  - confidence: high
  - evidence: Merged claims 12,17.
  - https://github.com/AnthonyLloyd/CsCheck/blob/master/Comparison.md
  - https://github.com/hedgehogqa/fsharp-hedgehog

## Category (untagged)

- **All values flowing through an IncrementalValuesProvider<T> must implement value-based equality to be cacheable, which is why generator authors use records (auto-implementing IEquatable<T>) for their extracted data models.**
- **CI pipelines (GitHub Actions) install multiple SDK versions and use global.json to pin specific SDK versions for different build stages to test across all supported SDK targets.**
- **Cacheability tests should assert that no Compilation, ISymbol, or SyntaxNode (Roslyn) types appear in the pipeline outputs, since these prevent caching.**
- **Caching correctness is verified by asserting each tracked step's run reason equals IncrementalStepRunReason.Cached across compilation runs.**
- **Combining the CompilationProvider into the normal pipeline destroys incrementality because it emits a new Compilation on every keystroke; generator authors should avoid it.**
- **Cross-version testing is done by duplicating the snapshot test project to run against each Roslyn version independently, using conditional #if directives for version-specific features, and integration tests use MSBuild SDK-version detection to conditionally reference the matching generator project.**
- **CsCheck is a C# property-based testing library whose generation AND shrinking are both based on random samples, unlike QuickCheck-style ports (e.g. FsCheck) that use tree-based shrinking requiring separate Arb classes; this enables automatic shrinking with reduced boilerplate.**
- **CsCheck preserves the random seed so a shrinking/failure case from a CI run can be reproduced and continued later.**
- **CsCheck provides built-in model-based testing (Check.SampleModelBased), which Lloyd calls the simplest and most powerful form of property-based testing, useful for stateful scenarios.**
- **CsCheck supports a differential/regression workflow where the original implementation is kept alongside the test so a refactored implementation can be proven to produce identical results.**
- **CsCheck supports both stateful (model-based) and parallel/concurrency property-based testing, distinguishing it from most other .NET property-based testing libraries.**
- **CsCheck supports metamorphic (two-path) testing and parallel/concurrent random testing as distinct built-in modes (Check.SampleMetamorphic, Check.SampleConcurrent).**
- **CsCheck uses a Monte-Carlo / random shrinking method rather than the tree-based shrinking used by Hedgehog, generating a new value, checking its Size is less than the current failing case, and retesting.**
- **CsCheck uses a fast PCG random generator and an int64-tree Size algorithm for efficient shrinking of complex composed types and collections.**
- **CsCheck's random shrinking is advantageous for parallel tests because path-explorer (tree-based) shrinkers struggle when tests do not fail deterministically.**
- **Deep-recursion / stack-overflow attacks are mitigated by a MaxDepth limit of 64 by default on both reading and writing, with a writer fail-safe at 1,000 — a canonical guard that adversarial robustness tests should exercise.**
- **Each tracked rule entry records Rule ID, Category, Severity, and Notes; changed-rule tables additionally record New Category, New Severity, Old Category, and Old Severity columns.**
- **Expected diagnostics are specified declaratively via a DiagnosticResult fluent API that pins line/column locations (and can target compiler diagnostics like CS0246), enabling precise diagnostic-conformance assertions.**
- **Generator output models must use value-equality (typically record types / EquatableArray) for the incremental pipeline to recognize unchanged outputs; a non-equatable field breaks caching.**
- **Generator tests should validate both that generated code is syntactically correct and emits no error/warning diagnostics, and that it executes correctly at runtime.**
- **Hedgehog is a property-based testing library usable from C# as well as F# and other .NET languages, making it applicable to testing .NET source generators and mappers.**
- **Hedgehog is actively maintained, with v2.0.0 released December 8, 2025.**
- **Hedgehog provides composable generators for structured and recursive data types plus range combinators, enabling generation of complex inputs (e.g. recursive types, cycles) for robustness fuzzing.**
- **Hedgehog supports deterministic, reproducible test runs through explicit seed control, relevant to seeded fuzzing of generators.**
- **Hedgehog uses integrated shrinking that preserves invariants by construction, unlike approaches requiring separate shrinker definitions (e.g. FsCheck/QuickCheck-style).**
- **ImmutableArray<T> does not provide value-based equality (it only compares whether two instances wrap the same underlying array), so using arrays/collections directly in the pipeline silently breaks caching and necessitates a wrapper like EquatableArray.**
- **Incremental generator determinism/caching is tested by enabling step tracking in the generator driver and inspecting the run result object from generator execution.**
- **Incremental generators strictly require deterministic transformations (same inputs must yield same outputs across compilation cycles), unlike v1 ISourceGenerator where determinism was only a recommendation; this is what makes caching correct.**
- **Malformed input is rejected by strictly honoring RFC 8259 by default, throwing a clear JsonException, while permissive parsing modes (trailing commas, quoted numbers, comments) are opt-in and introduce cross-deserializer interpretation risks — a malformed-input/feature-flag interaction concern for combinatorial and adversarial testing.**
- **Mapperly explicitly tests incremental-generator cacheability by configuring GeneratorDriverOptions with trackIncrementalGeneratorSteps: true, enabling validation of incremental compilation steps and caching behavior.**
- **Mapperly handles snapshots that differ across target frameworks using a VersionedSnapshot attribute applied to test classes or methods, storing per-framework snapshot results.**
- **Mapperly maintains separate test projects including Riok.Mapperly.Tests (snapshot/unit), Riok.Mapperly.IntegrationTests (runtime behavioral correctness), and Riok.Mapperly.Abstractions.Tests, separating generator-output verification from runtime mapping parity.**
- **Mapperly provides a generator test harness consisting of a TestSourceBuilder class (to generate mapper source code) and a TestHelper that runs the source generator and enables assertions or snapshot verification.**
- **Mapperly uses VerifyTests/Verify and VerifyTests/Verify.SourceGenerators for snapshot/approval testing of both reported diagnostics and emitted source code.**
- **Mapperly uses the Verify snapshot/approval testing library (specifically Verify.SourceGenerators) for verifying generated source code, via a VerifyGenerator() helper that calls Verify(result) and supports parameterized cases through UseParameters().**
- **Mapperly's benchmark performance is on par with hand-written mapping code and faster than reflection-based alternatives, but the article provides no testing methodology or reproducibility framework for this claim.**
- **Mapperly's contributing docs document Verify-based snapshotting, a generator harness, and cross-version testing, but do not mention fuzzing, mutation testing, incremental-generator determinism/caching testing, or combinatorial feature-interaction testing.**
- **Mapperly's initial source generator implementation regenerated nearly all output on every compilation, failing to leverage incremental source generation — motivating an architectural rework. This is a concrete real-world example of the incremental-cacheability problem the research question covers (category 3).**
- **Mapperly's integration tests run across multiple supported target frameworks including .NET 7.0 and .NET Framework 4.8, verifying both generated code and actually mapped objects.**
- **Mapperly's test harness drives its MapperGenerator through Roslyn's CSharpGeneratorDriver, building CSharpCompilation instances with configurable nullable contexts and language versions, the standard Roslyn GeneratorDriver test-harness approach.**
- **Mapperly's test suite includes dedicated diagnostic validation, filtering diagnostics by ignore lists, grouping them by ID, and asserting against allowed severity levels.**
- **Mass-assignment / over-posting risk is addressed by guidance to validate deserialized object graphs from untrusted sources before processing, and by noting that constructor logic participates in state management during deserialization.**
- **Microsoft publishes Microsoft.CodeAnalysis.CSharp.Analyzer.Testing as an official Roslyn analyzer test framework providing C# types for testing analyzers, making it the canonical Roslyn-team harness for analyzer conformance testing in .NET.**
- **Microsoft.CodeAnalysis.Testing provides language- and component-specific verifier types for testing Roslyn analyzers, code fixes, code refactorings, and source generators (e.g. CSharpAnalyzerVerifier<TAnalyzer,TVerifier>, CSharpCodeFixVerifier<TAnalyzer,TCodeFix,TVerifier>, plus dedicated SourceGenerators.Testing and CodeRefactoring.Testing packages).**
- **Mutation testing with Stryker.NET works by temporarily inserting bugs (mutations) into code to validate whether the test suite catches them — i.e., 'testing the tests.'**
- **PICT generates minimal but more effective test suites than manual case design, supporting the claim that pairwise tooling reduces test-design effort for combinatorial coverage.**
- **PICT is Microsoft's open-source command-line tool that generates test cases and test configurations for combinatorial/pairwise testing, directly relevant to feature-interaction and attribute-combination testing of a source generator.**
- **PICT ships a benchmark harness (pict-benchmark) that exercises generation with different random seeds, indicating built-in support for seeded/randomized generation runs.**
- **PICT uses pairwise (all-pairs) coverage as a tractable alternative to exhaustive combinatorial testing, the canonical technique for reducing power-set/matrix feature-flag testing to a minimal effective suite.**
- **Package version compatibility is tracked by major version: v1.x supports xUnit 2 and v2.x supports xUnit 3, with the latest release being v2.0.24 on January 13, 2025.**
- **Pipeline stages must be annotated with WithTrackingName(name) so that named stage outputs can be retrieved and compared across generator runs.**
- **Property-based random testing makes roundtrip testing of serialization code trivial, and Lloyd argues serialization bugs are common precisely because this easy technique is underused.**
- **Reference cycles are detected during serialization via the maximum depth setting and surface as a clear JsonException, while general reference-preservation handling is opt-in and not enabled by default — directly relevant to reference-cycle adversarial test cases for object mappers.**
- **RegisterImplementationSourceOutput is an alternative to RegisterSourceOutput that guarantees the generated code does not change the semantic meaning of the rest of the code, enabling further IDE performance optimizations.**
- **Release tracking categorizes diagnostic rule changes across versions into additions (new rules), removals (previously shipped rules now removed), and changes (rules whose category, default severity, or enabled-by-default status changed).**
- **Returning SyntaxNode or ISymbol instances from a generator pipeline breaks incremental caching because each SyntaxNode is treated as new even when it logically represents the same node; a Roslyn-independent value-type data model is required.**
- **Roslyn analyzer release tracking requires two markdown additional files — AnalyzerReleases.Shipped.md (tracks rules for shipped releases) and AnalyzerReleases.Unshipped.md (for the upcoming/unshipped release, starting empty each release cycle).**
- **Roslyn recommends building a dedicated data model with well-defined equality as the final item passed to Register...Output, so the driver can correctly compare revisions of the model for caching.**
- **Roslyn's official guidance is to use value types in generator pipelines because they have well-defined comparison semantics that are amenable to caching.**
- **Setup requires a one-time module initializer call to VerifySourceGenerators.Initialize(), after which Verify can snapshot generator output.**
- **Snapshot testing a source generator requires the Microsoft.CodeAnalysis.CSharp and Microsoft.CodeAnalysis.Analyzers packages, which provide methods for running a generator in memory via a GeneratorDriver and examining the output.**
- **Stryker.NET fills a gap in the .NET landscape, providing mutation testing capabilities that other language ecosystems had had for years.**
- **Stryker.NET is a mutation testing tool for .NET Core and .NET Framework projects, addressing a gap that the .NET ecosystem previously lacked.**
- **Stryker.NET is a mutation testing tool for both .NET Core and .NET Framework, providing mutation testing capabilities for the broader .NET ecosystem.**
- **Stryker.NET is distributed and installed via the NuGet package manager.**
- **Stryker.NET is distributed as a global .NET CLI tool (NuGet package dotnet-stryker), integrating into standard .NET project workflows.**
- **Stryker.NET is part of a multi-language Stryker mutation testing ecosystem with parallel implementations for JavaScript/TypeScript and Scala.**
- **Stryker.NET supports a wide compatibility range, working with .NET Core 1.1+, .NET Framework 4.5+, and .NET Standard 1.3+, making it applicable across multi-target codegen/source-generator libraries.**
- **Stryker.NET validates the quality of a test suite itself by injecting temporary bugs (mutants) into source code and checking whether tests detect them — the canonical 'test the tests' meta-testing approach.**
- **System.Text.Json's deserializer is designed to handle untrusted input with bounded O(n) algorithmic complexity, with no known way to craft JSON that forces super-linear work — a concrete DoS-robustness design guarantee that an adversarial test suite for a codegen serializer/mapper should verify.**
- **Testing caching correctness is substantially harder than testing output correctness, yet it is critical for IDE performance.**
- **The Mapperly project formally tracked improving incremental generator support as a discrete issue (#72) opened May 11, 2022 and later closed, showing that a best-in-class .NET mapper treated incremental-generator cacheability as an explicit engineering concern.**
- **The NuGet package layout for multi-Roslyn-version generators places each version's output in version-specific analyzer subfolders (analyzers/dotnet/roslyn4.4/cs and analyzers/dotnet/roslyn4.11/cs), and the .NET SDK automatically loads the highest supported version.**
- **The analyzer testing framework is itself depended upon by the largest .NET codegen/analyzer codebases including dotnet/roslyn, dotnet/efcore, and dotnet/runtime, evidencing it as the best-in-class adopted standard.**
- **The author cautions that multi-targeting Roslyn versions adds substantial work and complexity and should only be undertaken when truly necessary.**
- **The canonical test harness runs the generator via Roslyn's CSharpGeneratorDriver against a compilation built from the input code.**
- **The code-fix verifier (VerifyCodeFixAsync) performs multi-step verification including confirming the analyzer reports expected diagnostics, that the fixed output has no remaining fixable diagnostics, single-fix application, and Fix All operations.**
- **The current testing packages are test-framework-agnostic via DefaultVerifier (with the older MSTest/NUnit/xUnit-specific variant packages marked obsolete), so the same verifiers work across xUnit, NUnit, and MSTest.**
- **The framework is actively maintained, with version 1.1.4 released May 22, 2026 and roughly 11.8 million total downloads, indicating current, widely-adopted tooling rather than abandoned guidance.**
- **The generator driver caches and reuses previously computed transformation outputs when it sees inputs it has seen before, which is the mechanism that gives incremental generators their performance benefit.**
- **The issue was opened to solicit multiple candidate solutions rather than prescribe one, indicating incremental-generator pipeline design had several viable architectural approaches under consideration.**
- **The library exposes attributes including CombinatorialData, PairwiseData, CombinatorialValues, CombinatorialRange, CombinatorialMemberData, and CombinatorialRandomData for specifying and generating test argument sets.**
- **The library produces a separate verified (snapshot) file for each generated source output from GeneratorDriverRunResult.Results.GeneratedSources, plus an info file capturing diagnostics metadata (descriptors, severity, locations).**
- **The library supports pairwise testing to reduce combinatorial test-case explosion when a method has more than two parameters, offering reasonable coverage without exponential growth.**
- **The package builds on Roslyn Workspaces (Microsoft.CodeAnalysis.CSharp.Workspaces) and targets broad runtimes (net8.0, netcoreapp3.1, netstandard2.0, net461/net472), enabling cross-version/multi-target analyzer test harnesses.**
- **The package is the foundational layer beneath framework-specific testing variants (xUnit, NUnit, MSTest), confirming the standard pattern of choosing a test-runner-specific Microsoft.CodeAnalysis.*.Analyzer.Testing verifier package.**
- **The release workflow requires moving all entries from the unshipped file into a new release section in the shipped file at release time, then resetting the unshipped file to empty.**
- **The test harness creates a Compilation from source, runs the generator via GeneratorDriver, and the harness must explicitly add assembly references because the compilation has none by default.**
- **The testing harness supports supplying AdditionalFiles to the compilation under test, allowing tests of generators/analyzers that consume non-source inputs.**
- **To support multiple .NET SDK / Roslyn versions, a source generator project should be split into multiple projects, each referencing a different Microsoft.CodeAnalysis.CSharp version (e.g. Roslyn 4.4.0 for .NET 7 SDK and 4.11.0 for .NET 8.0.400+), with the outputs packed into versioned analyzer folders so the SDK loads the highest supported version.**
- **To test that an incremental generator's outputs are cacheable, you run the generator against a compilation, clone the compilation, run the generator a second time, and assert that the second-run steps report IncrementalStepRunReason.Cached or .Unchanged (not New).**
- **Verify automatically captures both the generated source code added to the compilation and the emitted diagnostics in the snapshot, so diagnostic conformance is tested alongside output.**
- **Verify.SourceGenerators (extensions to Simon Cropp's Verify library) is the recommended tool for snapshot/approval testing of source generator output in .NET.**
- **Verify.SourceGenerators is an extension to the Verify snapshot/approval-testing framework that enables verification (snapshot/approval testing) of C# Roslyn source generators, integrating with the GeneratorDriver/CSharpGeneratorDriver Roslyn APIs.**
- **Verify.SourceGenerators is recommended for snapshot/approval testing of generated source, and is considered superior to manual snapshot testing approaches used in earlier generators (e.g., RDG).**
- **Verify.SourceGenerators requires calling VerifySourceGenerators.Enable() (typically via a [ModuleInitializer]) to register the converters that handle source generator outputs.**
- **Verify.SourceGenerators supports scrubbers to manipulate/normalize generated source output and an IgnoreGeneratedResult() predicate to filter/exclude specific outputs by hint name or content.**
- **Xunit.Combinatorial frames its two strategies as a tradeoff: full combinatorial for exhaustive coverage versus pairwise for reasonable coverage without exponential test growth.**
- **Xunit.Combinatorial provides combinatorial (exhaustive) testing for xUnit, running a test method once for each combination of possible argument values via the [Theory, CombinatorialData] attribute.**
