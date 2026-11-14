# Multi-version generator and C# 15 union mapping — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the generator as a Roslyn 5.0 build and a Roslyn 5.12 build in versioned analyzer folders. The 5.12
build maps C# 15 unions. Union examples go under `samples/`, and a union performance comparison follows.

**Architecture:** One source tree, two csproj files. The 5.12 project compiles the 5.0 project's files except
`*.Floor.cs`, plus its own folder. Build-specific behaviour sits behind a small `UnionSupport` surface implemented
once per build. Union extraction and emission live only in the 5.12 folder. Shared code reaches them through
classic `partial void` hooks (removed from the 5.0 build) or a method whose Floor body returns false.

**Tech Stack:** C# / netstandard2.0 generator; Microsoft.CodeAnalysis.CSharp 5.0.0 and 5.12.0; xunit; BenchmarkDotNet;
GitHub Actions; SDK containers `mcr.microsoft.com/dotnet/sdk:10.0.101` and `:11.0.100`.

**Spec:** `docs/superpowers/specs/2026-11-14-multiversion-generator-union-mapping-design.md`

## Global Constraints

- The pinned SDK on this branch is 10.0.101 (`global.json`, `rollForward: disable`). Nothing in `DwarfMapper.NET.sln`
  may need SDK 11 to build.
- `Microsoft.CodeAnalysis.CSharp` stays 5.0.0 centrally. The 5.12 projects use `VersionOverride="5.12.0"`.
- Both builds have `AssemblyName` `DwarfMapper.Generator`. Package paths: `analyzers/dotnet/roslyn5.0/cs/` and
  `analyzers/dotnet/roslyn5.12/cs/`. `DwarfMapper.CodeFixes.dll` stays in `analyzers/dotnet/cs/`.
- Union mapping is on only when the build is 5.12 AND `LanguageVersion >= CSharp15`.
- No new diagnostic ids. Union refusals are DWARF113, missing or competing union arms DWARF114, and bad explicit arms
  DWARF035.
- Generated code: no warnings, no `!`, no casts on arm values, no wildcard after a null arm, arms most-derived first,
  and a typed null arm (`null => default(global::T)`).
- Every fix and feature ships with a RED→GREEN test in the same commit. No generator file loses coverage (compare with
  `rig/filecov.py`). Run the whole solution before committing an emission change.
- Commits end with `Claude-Session: https://claude.ai/code/session_01MfFADp4wXnvqRmtXed4UUM`. Stage by path only.
- No pull request until a `workflow_dispatch` run of `ci.yml` on the pushed branch is fully green. Every push needs
  the owner's approval through the ask prompt.

## Review Focus

1. **A union from a referenced assembly** (metadata, not source) must map the same as one declared in the same
   compilation. Test in Task 4: a union compiled into a metadata reference.
2. **A C# 14 project referencing a C# 15 library's union** must get DWARF113 "needs C# 15", never CS9327 inside the
   `.g.cs`. That includes a `Use=` converter or hook whose parameter is the union. Test in Task 3.
3. **A constructed generic union** (`Option<Cat>` into `Option<CatDto>`) must pair the substituted case types. Test in
   Task 4.
4. **Overlapping case types** (`union U(Animal, Kitten)` with `Kitten : Animal`) must emit Kitten's arm before
   Animal's, with no CS8510. Test in Task 4.
5. **A `Pet?` member** must keep "null" apart from "a present but empty union". Test in Task 5.

---

### Task 1: Second generator build and the two-folder package

**Files:**
- Create: `src/DwarfMapper.Generator.Roslyn512/DwarfMapper.Generator.Roslyn512.csproj`, `packages.lock.json`
- Modify: `DwarfMapper.NET.sln` (add the project), `src/DwarfMapper/DwarfMapper.csproj` (ProjectReference and pack
  items), `src/DwarfMapper/packages.lock.json` if the closure changes
- Create: `scripts/analyzer-selection-check.sh`
- Modify: `scripts/repro-pack-check.py` (docstring and any entry list), `scripts/gate-checks.ps1` (package-size
  ceiling plus a history note), `.github/workflows/ci.yml` (`package-size` runs the selection check)

**Interfaces:**
- Produces: the project `DwarfMapper.Generator.Roslyn512` (assembly `DwarfMapper.Generator`, constant
  `ROSLYN5_12_OR_GREATER` defined for documentation only, never used as `#if` in shared files). Compile items:
  `..\DwarfMapper.Generator\**\*.cs` minus `bin/**`, `obj/**` and `**\*.Floor.cs`, plus its own `**\*.cs`, the
  linked `..\Shared\DwarfLimits.cs`, and the same `AdditionalFiles`.
- Produces: `scripts/analyzer-selection-check.sh <pack-dir> <expected-folder>`. It writes a throwaway net10.0 C# 14
  consumer into `$RUNNER_TEMP` (or `/tmp`), restores DwarfMapper from `<pack-dir>`, and prints every `@(Analyzer)`
  item after `ResolveLockFileAnalyzers`. It passes only when exactly one `DwarfMapper.Generator.dll` is listed, from
  `analyzers/dotnet/<expected-folder>/cs/`, and the consumer builds and maps one flat pair.

- [ ] **Step 1: RED.** Pack the current tree in `sdk:10.0.101` and run `analyzer-selection-check.sh <pack> roslyn5.0`.
  Expected: FAIL, the generator comes from `analyzers/dotnet/cs/`.
- [ ] **Step 2:** Create the Roslyn512 csproj: same properties as the 5.0 one (`IsRoslynComponent`,
  `EnforceExtendedAnalyzerRules`, `IncludeBuildOutput=false`, `IsPackable=false`, `RuntimeIdentifiers`
  `linux-x64;win-x64`), the four analyzer package references, and `Microsoft.CodeAnalysis.CSharp`
  `VersionOverride="5.12.0"`. Add it to the solution. Restore with `--force-evaluate` to write its lock file.
- [ ] **Step 3:** `DwarfMapper.csproj`: add a ProjectReference to the new project (`ReferenceOutputAssembly="false"`,
  like the 5.0 one). Pack the 5.0 DLL into `analyzers/dotnet/roslyn5.0/cs` and the 5.12 DLL into
  `analyzers/dotnet/roslyn5.12/cs`; leave CodeFixes where it is. Update the comment block that explains the slot.
- [ ] **Step 4: GREEN.** Rebuild, repack, rerun the check with `roslyn5.0` in `sdk:10.0.101`, and with `roslyn5.12`
  in `sdk:11.0.100` (`DOTNET_ROLL_FORWARD=Major` is not needed: the consumer is net10.0 and SDK 11 builds it).
  Expected: PASS both, one generator each.
- [ ] **Step 5:** Whole solution build and test in `sdk:10.0.101` (heavy-run script). Expected: 0 warnings, all green,
  the same test count as the base plus nothing new.
- [ ] **Step 6:** Package size: rehearse the `package-size` job (`rig/preflight-job.sh` with `ci_job.py`), read the
  measured bytes, re-pin the DwarfMapper ceiling in `gate-checks.ps1`, and add a history line naming T23 and the
  second build. Update `repro-pack-check.py` to name both generator paths. Rehearse `reproducible-build`.
- [ ] **Step 7:** `ci.yml` `package-size`: after packing, run `scripts/analyzer-selection-check.sh "$RUNNER_TEMP/pack"
  roslyn5.0`. Rehearse the job.
- [ ] **Step 8: Commit** ("build(round35/T23): ship the generator for Roslyn 5.0 and 5.12 in versioned analyzer
  folders").

### Task 2: The 5.12 test project and golden neutrality

**Files:**
- Create: `tests/DwarfMapper.Generator.Roslyn512.Tests/DwarfMapper.Generator.Roslyn512.Tests.csproj`,
  `packages.lock.json`, `Roslyn512Harness.cs`, `GoldenNeutralityTests.cs`
- Modify: `DwarfMapper.NET.sln`, `src/DwarfMapper.Generator/AssemblyInfo.cs`
  (`InternalsVisibleTo("DwarfMapper.Generator.Roslyn512.Tests")`), `codecov.yml` if components list paths

**Interfaces:**
- Produces: `internal static class Roslyn512Harness`:
  - `GeneratorResult Run(string source, LanguageVersion lang = LanguageVersion.CSharp15, params MetadataReference[] extra)`
    returns `(string Generated, ImmutableArray<Diagnostic> GeneratorDiagnostics, ImmutableArray<Diagnostic> CompileDiagnostics, Compilation Output)`.
  - `string CompilesClean(string source, LanguageVersion lang = LanguageVersion.CSharp15, params MetadataReference[] extra)`
    fails on any generator diagnostic of warning or above and any compile warning or error.
  - `object? Invoke(Compilation output, string typeName, string method, params object?[] args)` emits to memory, loads
    into a collectible `AssemblyLoadContext`, and calls the method.
  - `const string UnionPolyfill` declares `System.Runtime.CompilerServices.UnionAttribute` and `IUnion` (the net10 BCL
    lacks them).
  - `MetadataReference Compile(string librarySource, LanguageVersion lang = LanguageVersion.CSharp15)` builds a library
    and returns it as a metadata reference (for Review Focus 1).
- References: TPA plus `DwarfMapper.dll`, each through one cached `MetadataReference.CreateFromFile` per path (the
  `ReferenceTo` rule from round 34).

- [ ] **Step 1:** Create the project (net10.0, xunit and the collector packages, `Microsoft.CodeAnalysis.CSharp`
  `VersionOverride="5.12.0"`, ProjectReferences to `src/DwarfMapper` and to the Roslyn512 generator as a normal
  reference). Add it to the solution, plus the `InternalsVisibleTo` line.
- [ ] **Step 2: Test** `Harness_runs_the_512_build`: a flat `A`→`B` mapper compiles clean, and `Invoke` maps `X=7`.
- [ ] **Step 3: Test** `Every_golden_case_is_byte_identical_on_the_512_build`. Link `GoldenCorpus.cs`,
  `GoldenCase.cs`, `GoldenManifest.cs`, `GoldenFingerprint.cs` and their corpus inputs (`CombinatorialSchema.cs`,
  `SyntheticSchema.cs`, `GeneratorRegistry.cs`, `GeneratorRunner.cs`, `GeneratorRun.cs`) from Generator.Tests. Parse
  at `LanguageVersion.CSharp14`, the 5.0 harness's language. Assert every fingerprint equals its manifest line. If the
  link chain pulls in more than those files, stop and record it in the task note instead of copying a framework.
- [ ] **Step 4:** Run the project and the whole solution in `sdk:10.0.101`. Expected: green, and 1,021 neutrality rows.
- [ ] **Step 5: Commit** ("test(round35/T23): a test project for the Roslyn 5.12 build; the golden corpus is
  byte-identical on it").

### Task 3: The `UnionSupport` seam and the conversion gate

**Files:**
- Create: `src/DwarfMapper.Generator/Pipeline/UnionSupport.cs` (shared declarations),
  `src/DwarfMapper.Generator/Pipeline/UnionSupport.Floor.cs` (5.0 only),
  `src/DwarfMapper.Generator.Roslyn512/Pipeline/UnionSupport.Roslyn512.cs` (5.12 only)
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.Conversions.cs` (`HasImplicitConversion`; move
  `UnionRefusal` and `IsUnionType` to the Floor file), `Diagnostics/DiagnosticDescriptors.cs` (DWARF113 title and
  remarks), `AnalyzerReleases.Unshipped.md`, `tests/DwarfMapper.NegativeCases/Cases/DWARF113_UnionMemberNotMapped.cs`
  (EXPECT-MESSAGE), `tests/DwarfMapper.Generator.Tests/Round31/UnionTypeRefusalTests.cs` (message substring)
- Test: `tests/DwarfMapper.Generator.Roslyn512.Tests/UnionConversionGateTests.cs`

**Interfaces:**
- Produces, on `internal static partial class MapperExtractor`:
  - `private static partial bool UnionMappingAvailable(Compilation compilation);` Floor: `false`. 5.12:
    `((CSharpCompilation)compilation).LanguageVersion >= LanguageVersion.CSharp15`.
  - `internal static partial bool IsUnionType(ITypeSymbol type);` Floor: the attribute check that exists today. 5.12:
    `type.IsUnion`.
  - `internal static partial string? UnionRefusal(ITypeSymbol srcType, ITypeSymbol tgtType, string what, Compilation compilation);`
    Floor: today's rule, with the message saying union mapping needs the .NET 11 SDK's compiler (Roslyn 5.12) and
    C# 15. 5.12: refuses at C# < 15 ("needs C# 15"), a union to or from a non-union type unless
    `HasImplicitConversion` says a case type converts into the union, and a union with no case types. Returns null
    for two different unions (Task 4 maps them) and for the same union.
  - `internal static bool IsUnionConversion(Conversion conversion)` (shared): `IsImplicit && !IsUserDefined &&
    !IsMethodGroup && MethodSymbol is not null`.
  - `HasImplicitConversion` becomes `IsImplicit && !IsUserDefined && (!IsUnionConversion(c) || UnionMappingAvailable(compilation))`.
- The three existing `UnionRefusal` call sites pass the compilation.

- [ ] **Step 1: RED tests** (5.12 project):
  - `A_Use_converter_taking_the_union_does_not_apply_to_a_case_source_in_CSharp14`: expects the existing UseMethodInvalid
    diagnostic, and no CS9327 in the compile.
  - `The_same_converter_applies_in_CSharp15`: compiles clean, and the run wraps the case.
  - `A_hook_taking_the_union_is_not_matched_by_a_case_source_in_CSharp14`.
  - `A_union_pair_in_CSharp14_is_DWARF113_naming_CSharp15`.
  - `IsUnionConversion_is_true_only_for_union_conversions`: identity, numeric, reference, boxing, nullable,
    user-defined and union conversions classified on Roslyn 5.12.
  Run them. Expected: they fail (no seam yet), or CS9327 appears.
- [ ] **Step 2:** Implement the seam as specified, with no `#if`.
- [ ] **Step 3:** Update the DWARF113 title to "A union type cannot be mapped here", its remarks, the release file,
  the NegativeCases EXPECT-MESSAGE lines and `UnionTypeRefusalTests`' substring.
- [ ] **Step 4: GREEN:** the 5.12 tests pass. The whole solution (5.0) is green and the golden manifest unchanged.
  Expected: the only 5.0 change is DWARF113's wording, so only fingerprints carrying DWARF113 may move. Update those
  lines deliberately with `DWARF_GOLDEN_UPDATE=1` and list them in the commit.
- [ ] **Step 5: Commit** ("feat(round35/T23): decide union conversions in one place; DWARF113 says what is missing").

### Task 4: Union to union at a declared map

**Files:**
- Create: `src/DwarfMapper.Generator/Model/UnionDispatch.cs` (shared model),
  `src/DwarfMapper.Generator.Roslyn512/Pipeline/MapperExtractor.Unions.cs`,
  `src/DwarfMapper.Generator.Roslyn512/Pipeline/MapEmitter.Unions.cs`
- Modify: `Model/MapMethodModel.cs` (`UnionDispatch? UnionDispatch = null`), `Pipeline/MapperExtractor.Phases.cs`
  (endpoint hook after `UnionRefusal`), `Pipeline/MapEmitter.cs` (body hook), the Floor file (endpoint stub returning
  false), `DiagnosticDescriptors.cs` (DWARF114 title and remarks; DWARF035 unchanged id)
- Test: `tests/DwarfMapper.Generator.Roslyn512.Tests/UnionEndpointTests.cs`, `UnionEndpointRuntimeTests.cs`

**Interfaces:**
- `public sealed record UnionArm(string CaseFqn, string? ConverterMethod, string? PassThroughCastFqn, bool ConverterNeedsDepthCtx = false, string? ConverterParamTypeFqn = null)`:
  `ConverterMethod == null` means a pass-through arm. `PassThroughCastFqn` is non-null only when the case type differs
  from the target case type (a numeric or reference conversion written as `(global::T)__s`). `EmitConverterMethod`
  escapes the name, as `DerivedTypeArm` does.
- `public enum UnionEmptyArm { None, Default }`: `Default` emits `null => default(<return type>)`.
- `public sealed record UnionDispatch(EquatableArray<UnionArm> Arms, UnionEmptyArm Empty)`.
- `internal static partial bool TryHandleUnionEndpoint(IMethodSymbol method, MethodExtractionContext ctx, ... the same arguments TryHandleSpanMap takes ...)`.
  Floor: `return false;`.
- `static partial void EmitUnionDispatchBody(StringBuilder sb, MapMethodModel method, string indent);` declared in
  `MapEmitter.cs`, implemented only in `MapEmitter.Unions.cs`. The shape follows `EmitDerivedDispatchBody`:
  before-hooks, `return {p} switch` (a typed local when there are after-hooks), arms in model order, the optional
  typed null arm, no wildcard, then after-hooks.
- Pairing (5.12): an explicit `[MapDerivedType]` arm, else the same case type in the target, else exactly one declared
  method from the case type to a target case type. Arm order: most-derived first over `ITypeSymbol` (value types
  included). Coverage: a case is covered by an arm whose source is the case or an ancestor or interface of it.
  - Uncovered, or two declared candidates: DWARF114 naming the union, the case(s) and both remedies.
  - An explicit arm whose source matches no case type, or whose target does not convert to the target union: DWARF035
    with that reason.
- Empty arm: `Default` when the target is a struct union or a nullable class union. `None` when the target is a
  non-nullable class union and no case type is maybe-null. DWARF113 when the target is a non-nullable class union and
  the source can be empty.

- [ ] **Step 1: RED, across builds:** `The_refused_union_endpoint_maps_on_the_512_build` takes the `union endpoint`
  source from `UnionTypeRefusalTests` with declared case maps added, and expects clean output plus the runtime map.
  Run it. Expected: DWARF113.
- [ ] **Step 2: Tests, all failing now** (compile clean, then `Invoke` and check results):
  - declared case methods; explicit generic arms; identity case; value-type cases with `typeof` arms (`int` into
    `long`, by `PassThroughCastFqn`);
  - a nullable case type, which gets the null arm and no CS8655; an empty source into a struct target gives `default`;
  - a constructed generic union (Review Focus 3); overlapping cases (Review Focus 4); a metadata union (Review Focus 1);
  - a member-provider class union as the TARGET, nullable and non-nullable; a non-nullable class target with a
    maybe-empty source is DWARF113;
  - hooks on the union endpoint (before, after, ref after);
  - DWARF114 for a missing case and for two declared candidates, with the message substrings;
  - DWARF035 for an arm naming a non-case type;
  - a `[Union]` type with no case types is DWARF113.
- [ ] **Step 3:** Implement the model, the extraction, the endpoint hook and the emission.
- [ ] **Step 4: GREEN:** the 5.12 project is green. The whole solution in `sdk:10.0.101` is green, with the golden
  manifest unchanged by this task. Coverage of the new files from the 5.12 project, measured with `filecov.py`.
- [ ] **Step 5: Commit** ("feat(round35/T23): map a C# 15 union to a union at a declared map (Roslyn 5.12 build)").

### Task 5: Nested routes and reference modes

**Files:**
- Modify: `src/DwarfMapper.Generator.Roslyn512/Pipeline/MapperExtractor.Unions.cs`
  (`TryResolveUnionPair`), `Pipeline/MapperExtractor.Conversions.cs` (one hook after `UnionRefusal`), the Floor file
  (stub `return false;`)
- Test: `tests/DwarfMapper.Generator.Roslyn512.Tests/UnionNestedTests.cs`, `UnionMatrixTests.cs`

**Interfaces:**
- `internal static partial bool TryResolveUnionPair(ConversionRequest req, ... the ref/out set TryResolveConversion owns ..., out bool resolved);`
  For two different unions it adopts the declared map for that pair when one exists, else it synthesizes one private
  helper per pair, named through `GeneratedNames` like the other synthesized helpers, whose model carries a
  `UnionDispatch` built by Task 4's pairing (implicit arms only). It returns the helper as the converter, so collection,
  dictionary, `Nullable<T>` and update-into routes reuse it unchanged. A helper whose arms need the context takes it,
  the same way nested dispatch wrappers do.

- [ ] **Step 1: RED tests:**
  - union member; `List<Pet>`, `Pet[]`, `Dictionary<string, Pet>` values; a `Pet?` member keeping null apart from an
    empty union (Review Focus 5); update-into;
  - a case-type member assigned to a union member (C# 15) and the same refused at C# 14;
  - Preserve with a cycle through a case value (identity kept), and SetNull;
  - a projection with a union member stays DWARF113 (message says projections);
  - a span map and an async stream of unions: map, or DWARF113 with a named reason.
  Run them. Expected: DWARF113 everywhere.
- [ ] **Step 2:** Implement `TryResolveUnionPair` and the hook.
- [ ] **Step 3: `UnionMatrixTests`:** endpoint kind {create, update-into, member, list element, array element,
  dictionary value, `Nullable<union>`, span, async stream, projection} × union kind {declaration, hand-written struct,
  member-provider class, value-type cases, nullable case, generic, no case types} × reference mode {None, Preserve,
  SetNull}. Each cell is `Executed` (compiles clean, runs, checks the case value) or `Refused("<id>", "<substring>")`,
  and the expected table is literal in the test file.
- [ ] **Step 4: GREEN**, the whole solution green, coverage checked, the golden manifest unchanged.
- [ ] **Step 5: Commit** ("feat(round35/T23): union members, elements and reference modes on the Roslyn 5.12 build").

### Task 6: Samples, documentation and the `net11` job

**Files:**
- Create: `samples/DwarfMapper.Unions/DwarfMapper.Unions.csproj` (net11.0, not in the solution), `global.json`
  (`11.0.100`, `rollForward: latestFeature`), `Program.cs`, numbered example files `01_UnionToUnion.cs`,
  `02_ExplicitArms.cs`, `03_ValueCases.cs`, `04_UnionMembersAndLists.cs`, `05_EmptyUnion.cs`, `README.md`
- Create: `docs/howto/map-unions.md`
- Modify: `src/DwarfMapper.DocTooling/DocSet.cs` (add `docs/howto/map-unions.md`), `README.md` (a union section
  quoting one snippet, and the version note), `docs/howto/README.md`, `docs/diagnostics.md` (DWARF113, DWARF114,
  DWARF035), `docs/COMPARISON.md` (a union row, after checking each competitor), `samples/DwarfMapper.Gallery/README.md`
  (a pointer outside the generated regions), `CONTRIBUTING.md` (the two builds, the 5.12 test project, how to run the
  union sample), `CHANGELOG.md`, `Directory.Build.targets` (`DwarfGeneratorFlavor=roslyn5.12` swaps the analyzer
  ProjectReference), `.github/workflows/ci.yml` (`net11`)

**Interfaces:**
- The union sample's default mode uses ProjectReferences (`src/DwarfMapper` plus the Roslyn512 generator as an
  analyzer). With `-p:UsePackedDwarfMapper=true -p:DwarfMapperPackageVersion=<v>` it uses a PackageReference
  restored from `$(DwarfMapperPackageSource)`. `Program.Main` runs every example, throws on a wrong result, and returns 0.
- Snippet regions (`// <snippet: union-map>` and so on) are quoted by `map-unions.md` and README, so the orphan rule
  holds.

- [ ] **Step 1:** Write the sample and run it in `sdk:11.0.100` in both modes. Expected: exit 0. In package mode the
  printed analyzer is `roslyn5.12/cs/DwarfMapper.Generator.dll`, and only that one.
- [ ] **Step 2:** Docs as listed. Then run the DocTooling tests (snippet injector, orphan rule) in `sdk:10.0.101`.
  Expected: green.
- [ ] **Step 3:** `Directory.Build.targets` switch, and `net11` job steps: pack; `analyzer-selection-check.sh ...
  roslyn5.12`; build the repo consumers with `-p:DwarfGeneratorFlavor=roslyn5.12` (warnings as errors) and run the
  consumer test projects; build and run the union sample in package mode, behind a vacuity guard (the sample printed
  its five example lines). Rehearse the job with `ci_job.py` in `sdk:11.0.100`.
- [ ] **Step 4: Commit** ("docs(round35/T23): union examples in samples/DwarfMapper.Unions, the how-to, and the net11
  job's proof").

### Task 7: Verification, focused mutation, push, audit

- [ ] **Step 1:** A focused Stryker pass over the 5.12-only files with the 5.12 test project (the rig's one-minute
  loop). Kill the survivors with tests, or name them in the task note.
- [ ] **Step 2:** The whole solution in `sdk:10.0.101` (heavy-run script); `filecov.py` against the base for every
  generator file; rehearse `build-test`, `package-size`, `reproducible-build`, `deep-test` and `net11`.
- [ ] **Step 3:** Task note in `Issues/round35/ROUND35-TASKS.md`; push with the owner's approval; dispatch
  `gh workflow run ci.yml --ref feat/round35-t23`; open the PR (base `feat/round35-t8`) only when the run is fully
  green.

### Task 8 (T24): Union performance comparison

**Files:**
- Create: `benchmarks/DwarfMapper.UnionBenchmarks/DwarfMapper.UnionBenchmarks.csproj` (net11.0, outside the
  solution, its own `global.json`), `UnionMappingBenchmarks.cs`, `Payloads.cs`, `Program.cs`;
  `benchmarks/results/2026-11-1x-round35-t24-union-mapping.md`
- Modify: `docs/PERFORMANCE.md` (a union section), `benchmarks/README.md` if it lists projects

**Interfaces:**
- Benchmarks: `HandWritten` (Baseline, a switch over the union), `DwarfMapperUnion`, `DwarfMapperClassHierarchy`
  (`[MapDerivedType]` over the equivalent abstract hierarchy), plus one benchmark per competitor that supports the
  shape, labelled "class hierarchy" when it cannot map the union. Parameters: `Count` ∈ {1, 1000}; `Shape` ∈
  {ReferenceCases, ValueCases}. `Payloads` draws a seeded 60/30/10 case mix, the same instances for every arm.
  `[MemoryDiagnoser]`.

- [ ] **Step 1:** Check union support in Mapperly 4.3.1, Mapster 10.0.14 and AutoMapper 14.0.0 (compile a union pair
  with each). Record the outcome.
- [ ] **Step 2:** Write the project. A 30-second dry run in `sdk:11.0.100` shows every arm returns identical results:
  a `[GlobalSetup]` check compares each arm's output against `HandWritten` and throws on any difference.
- [ ] **Step 3:** Full run (DefaultJob) in the container, results committed with the platform named; docs updated.
- [ ] **Step 4: Commit** ("bench(round35/T24): union mapping against a hand-written switch and class-hierarchy
  dispatch").
