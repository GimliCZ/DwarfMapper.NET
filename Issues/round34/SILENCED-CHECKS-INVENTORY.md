> Produced by a read-only agent on 2026-10-27 at `42d9c52`..`c85ccdf`; line numbers may drift. Spot-checked before filing: `ReadPairIgnores` drops a null name (`MapperExtractor.Pairs.cs`, unlike its factory sibling); `AotSample/Program.cs` prints "ERROR:" with no non-zero exit; master has no required status checks (rules: deletion, non_fast_forward, code_scanning, code_quality, pull_request; branch not protected). The other claims are the agent's, unverified.

# Silenced-checks inventory — round 34 task B (2026-10-27)

Diffed against `Issues/round30/DE-SILENCING-INVENTORY.md` (taken 2026-09-03) and `Issues/round30/PLAN-de-silencing.md`.
Read-only: nothing was built, run, edited or committed in the repository.

**Tree.** `feat/round34`. The scan started at `42d9c52`. Three commits from other round-34 tasks landed during it:
`28ebb7c` (A2: `ci.yml`, `scripts/gate-checks.ps1`, `DashboardUploadTests.cs`, `RepoWriteGuardTests.cs`), `3144bab` (A1: 39 test
files, among them `ApiReferenceRendererTests.cs` and `GeneratorTestHarness.cs`) and `c85ccdf` (D: one Issues file). Workflows and
scripts were read at `28ebb7c` or later. The `#pragma` population was re-scanned at `c85ccdf`, and no site line moved. Another task
is still editing the tree, so line numbers in `ci.yml`, `gate-checks.ps1` and `tests/` may drift.

**Method.** C# searches go through Rider (`rsearch.py`) and are cross-checked with a filesystem grep; the two gave the same
`#pragma` population. Every "new / pre-existing" call rests on `git blame` against the round-30 date (2026-09-03). CI outcomes and
the master ruleset come from read-only `gh` GETs. Generator bail-outs were found with a heuristic scanner, which lives in the
session scratchpad and not in the repository, and then triaged by hand from a sample. Anything labelled *inferred* was read from
code, not run.

**Status vocabulary.**
- **RESOLVED**: fixed since round 30.
- **STILL OPEN**: round 30 listed it, and it is unchanged.
- **NEW**: not in round 30's inventory. Written *NEW (pre-R30)* when it already existed on 2026-09-03 and round 30 did not
  record it.
- **R30-MISRECORDED**: round 30's record was wrong on its own day. Either the reason it said was missing already existed, or
  a "not a defect" rested on a false premise. Nothing has been fixed, so these must not count as progress.
- **UNCHANGED**: round 30 recorded it under "Not defects", and it is not re-judged here.

**Verdicts.** justified · needs a reason · candidate to remove · needs investigation.

---

## Headline

1. **Round 30's de-silencing plan was not executed, and Task 30.4 (the scans) does not exist.** No test anywhere checks that a
   `<NoWarn>` id or a `#pragma warning disable` carries a reason. `RatchetInvariantScanTests` is unchanged on that axis. Round 30
   closed (PR #5) without recording any of Tasks 30.1–30.6 as done. The files named in 30.2/30.3 have not changed since before
   2026-09-03 (`git log`). Task 30.5 (near-miss) recorded no measurement, and Task 30.6 is still at 19 header files.
2. **Round 30's inventory was wrong in both directions.**
   - It listed as "no reason" several NoWarn files, two Testing ids and one pragma that all had reason comments before
     2026-09-03 (blame dates 2026-06-13 to 2026-08-22).
   - It filed the `DWARF_FUZZ_FULL` gate as "not a defect" because "nightly runs it". In fact no workflow sets the variable, and
     `housekeeping.ps1 -Nightly` has skipped the exhaustion since 2026-08-21.
3. **Because no scan holds the reasons, they drifted, as a class.** Examples:
   - Comments name ids no longer in the list (11 project files).
   - `DwarfMapper.CodeFixes.csproj` keeps a CA1303 reason above an empty `<NoWarn>$(NoWarn)</NoWarn>`.
   - `samples/.globalconfig` cites a CA1050 NoWarn that no sample has any more.
   - `Directory.Build.targets` cites a `generated_code = false` section that does not exist.
   - Two Stryker config comments say there is no test filter, beside a `test-case-filter` line.
4. **Shipped code (`src/`).** Four directive readers drop a `null!` name without a word, while their three siblings report it
   since 2026-09-13. A `[MapTo]` `[MapProperty("Missing")]` is skipped at the directive (*inferred*). The round-30 near-miss
   silence and the undocumented emitted pragma are still open.
5. **CI.**
   - Two of the AOT behavioural gate's checks cannot fail.
   - Three non-blocking jobs have met their own "flip once green" conditions, on 40, 29 and 15 consecutive green nightlies.
   - zizmor stays in reporting mode with 0 findings.
   - harden-runner is in audit mode in all 19 jobs.
   - Outside the tree: master's only ruleset requires **no** status check, so every CI gate is advisory at merge time.

### Round-30 status tally

| Round-30 item | Status now | Evidence |
|---|---|---|
| 30.1 `DwarfMapper.Testing.csproj` NoWarn (P1) | **partly R30-MISRECORDED, partly STILL OPEN** | The CA5394/CA1510 comments date from 2026-07-05. The CA1032 comment was added 2026-09-09 (`e25620c`). CA1510's reason ("style rule; explicit pattern is fine here") is the silence that round 30 said should be fixed instead. |
| 30.2 NoWarn reasons (13 files) | **STILL OPEN** for CorpusTests, AotBench, AotSample, Conformance, Gallery. **R30-MISRECORDED** for CompilerTests, Contracts, ProviderA, ProviderB, Host, Generator.Tests, NegativeCases and Testing.Tests (comments dated 2026-08-12/08-22). | Category 3 |
| 30.2 `AnalysisMode=Recommended` (Conformance, Gallery) | **STILL OPEN** | Category 3 |
| 30.2 AotSample silences DWARF038/DWARF044 | **STILL OPEN**, and in conflict with the owner's ruling of 2026-09-27 | Category 3 |
| 30.3 pragmas without a reason (4 sites) | 3 **STILL OPEN** (UserConversionRuntimeTests, FeatureCombinationInvalidTests, RegistryDiagnosticsGenTests ×2). 1 **R30-MISRECORDED** (SetNullCollectionCycleRuntimeTests: the reason at :4 dates from 2026-06-13). | Category 1 |
| 30.4 scans for NoWarn and pragma reasons | **STILL OPEN**: never built | Step 1 |
| 30.5 `TryExplainNearMiss` noise measurement | **STILL OPEN**: there is no number in the doc comment | Category 9 |
| 30.6 ReSharper `InconsistentNaming` headers | **STILL OPEN**: 19 files. All `// ReSharper disable` lines grew from 33 to 40 (src 7, tests 30, samples 3). | Rider-only |
| P1 the emitted `#pragma` must be named in the docs | **STILL OPEN**: `docs/` never mentions CS0612/CS0618 | Category 1 / 9 |
| P1b undeclared Python runtime | **STILL OPEN**: 5 `scripts/*.py`; `python3` 5× in ci.yml and 1× in actions-security.yml; not in README or CONTRIBUTING | outside the 9 categories |
| P3 `DeclaredDivergences` 13/76 | **not reconciled** | `DeclaredDivergences.Reasons` holds 1 entry (MaxDepth, span and async-stream endpoints), and the file has not changed since 2026-08-23, so round 30's 13/76 counted a different artefact |
| "Not defects": the `DWARF_FUZZ_FULL` gate | **R30-MISRECORDED**: the premise is false | Category 7 |
| "Not defects": the other items (9× ExcludeFromCodeCoverage, codecov ignores, SurfaceFixtures editorconfig, SurfaceMatrix step, the one Stryker disable) | **UNCHANGED** | Categories 4–7 |

Silences **RESOLVED since round 30 that were not on its list**:
- The blanket `NoWarn NU1903` in three projects was replaced by one advisory-scoped `NuGetAuditSuppress` (round 31 T01, `e9e09d4`)
  and is held by `AuditSuppressionScanTests`.
- `NoWarn DWARF070` in IntegrationTests was replaced by a per-site `[MapProperty(AllowNull = true)]` (round 32, `2b27301`).
- User-facing text no longer recommends `NoWarn`/`.editorconfig` for DWARF ids (round 32) and is held by `SuppressionAdviceTests`.
- Null names in `[MapConstructor<,>]`, `[MapProperty<,>]` and `[MapValue<>]` are reported (2026-09-13, `d474e9f`, `fc58fb0`). Their
  four sibling readers are not; see Category 9.

---

## Step 1 — which rules a test already holds

| Test | What it holds | Scope it does NOT cover |
|---|---|---|
| `SelfValidation/RatchetInvariantScanTests` R3 `R3_coverage_exclusions_in_src_are_exactly_pinned_and_individually_justified` | `[ExcludeFromCodeCoverage]` in `src/` is pinned at exactly 9, each with a non-empty Justification naming "compile-time-only" | Only `src/`; no exclusion exists elsewhere today |
| `RatchetInvariantScanTests` R4 `..._stryker_disable_population_is_exactly_the_grandfathered_H7_guard` | Exactly one `Stryker disable` in `src/` (DocSnippetInjector), paired with one restore | Only `src/`, which is the only tree that is mutated |
| `RatchetInvariantScanTests` R1/R3/R5/R4-wallclock | Stryker break provenance; equivalent-mutant ledger pinned at 61 rows / 64 mutants; Codecov carries no absolute target; no gate reads branch coverage or a wall clock | — |
| `Round31/AuditSuppressionScanTests` | No `NU1901`–`NU1904` inside any `<NoWarn>` in any csproj/props/targets | Nothing else in a NoWarn. `NuGetAuditSuppress` entries are not pinned |
| `SelfValidation/SuppressionAdviceTests` | User-facing text (README, docs, `src/DwarfMapper/*.cs`, `DiagnosticDescriptors.cs`) never recommends `.editorconfig` for, or `<NoWarn>` against, a DWARF diagnostic | Project files: three of the repository's own projects NoWarn DWARF ids |
| `SelfValidation/ShippedRuntimeSafetyTests.The_shipped_runtime_needs_no_trimming_or_AOT_escape_hatch` | No `UnconditionalSuppressMessage` / `RequiresUnreferencedCode` / `RequiresDynamicCode` / `DynamicallyAccessedMembers` in `src/DwarfMapper` | The other four `src/` projects |
| `SelfValidation/MutationScopeScanTests` | `Issues/round27/AUDIT-mutation-scope.md` matches what the `mutate` globs actually cover (file counts exact, shares within 1 pp) | Whether any unmutated file has a reason |
| `SelfValidation/PerfCategoryIsolationScanTests` | Every CI-reachable `dotnet test` and every `stryker-config*.json` excludes `Category=Perf` | — |
| `SelfValidation/CiGateScanTests` | Every ci.yml job has a row; the AOT gate executes the binary; the conformance gate is invoked | Whether each AOT check can fail (Category 8) |
| `SelfValidation/CiToolPrerequisiteScanTests` | The SurfaceMatrix exemption applies only to a filtered job; ilverify is installed and pinned | — |
| `SelfValidation/OssScannerImageTests` | The image's filter equals CI's; `DWARFMAPPER_OFFLINE_IMAGE` comes after the online restore; no workflow or script switches the audit off | The `\|\| echo WARNING` on the image's test run |
| `SelfValidation/MutationLaunchContractTests` | Every leg is launched bounded, with `--break-on-initial-test-failure` | — |
| `SelfValidation/TestTheTestsScanTests` T1/T4a | No assertion-free `[Fact]`/`[Theory]`; HollowAllowlist = 1 reasoned entry (`SnapshotSuite`), shrink-only | Tests whose assertions sit behind an env-gated early `return` (Category 7) |
| `GeneratedCodeIsWarningFreeTests` | Emitted code compiles with no CS warning for the schema/fuzz/feature inputs | Analyzer (CA/MA) findings on emitted code (Category 4) |
| `ConsumerReportedEmissionWarningsTests:312`, `EnumToEnumTests:144`, `Round28PatchCoverageTests:247/271/292` | The one emitted `#pragma warning disable CS0612, CS0618` | — |
| `scripts/nowarn-audit.ps1 -FailOnStale` (a script, not a test) | A NoWarn id that no longer fires | **Wired nowhere**: no workflow or housekeeping stage calls it, so its last measurement is the one-off in `d7e4744` (2026-08-27) |

**Not held by any test:**
- Category 1: a reason for each pragma.
- Category 2: a `Justification` on `[SuppressMessage]`.
- Category 3: a reason for each NoWarn id.
- Category 4: a reason for each `.globalconfig` severity downgrade. Only the files' header comments ask for one.
- Category 7: tests that pass on an env-gated early return.
- Category 8: every swallowed failure in C#, PowerShell, bash, workflows and the Dockerfile.
- Category 9: unreported generator conditions.

---

## 1. `#pragma warning disable` in C#

**Convention, as observed.** Three forms are in use:
- (a) a `//` reason on the line or lines above a bare directive (most sites);
- (b) an inline `// reason` on the directive line (`BlittableProof.cs:237/435`, `DeepRecursionPocTests.cs:93`, `DictionaryKeyCollisionRuntimeTests.cs:15`);
- (c) the rule's title inline with the real reason on the line below (`RegistryConcurrencyTortureTests.cs:115`).

The memory note's "reason line + bare directive, never trailing text" is the rule for `// ReSharper disable`. For pragmas, round
30's planned rule was "a `//` comment on the directive's line or the line above", and that is the standard used here. The scan
that implements it must pick one form, and must not accept a rule title as a reason. Two separate notes:
- The `CA5394` sites below that put a blank line between the reason and the directive pass by intent but fail round 30's strict
  wording.
- `FeatureCombinationInvalidTests.cs:9` passes that wording mechanically (the line above is a comment) but fails it by intent.

**Counts.** 45 lines contain the text. Of those:
- **29 hand-written directives in 23 files**: src 3/2, tests 21/18, samples 4/2, benchmarks 1/1;
- **1 emitted** directive;
- **5 directives inside generator-input test strings**;
- **10 mentions** in doc comments or assertion strings, which are not directives.

Round 30 counted "26 sites / 21 files". Eight of today's hand-written sites are new since then, and all eight carry a reason.

| Location | Silenced | Stated reason | Held by | R30 status | Verdict |
|---|---|---|---|---|---|
| `src/DwarfMapper.Generator/Pipeline/BlittableProof.cs:237`, `:435` | CA1508 (one `if` each) | inline: "flow analysis false positive: INamedTypeSymbol can be Class/Enum/Interface/Delegate, not only Struct" | not held | NEW (pre-R30) | justified |
| `src/DwarfMapper/DwarfMapperRegistry.cs:292` | RS0030 (`GetType()`) | :288-291 "object.GetType() is banned project-wide (zero-reflection claim). This call is the ONE sanctioned exception the ban message itself names…" | not held | NEW (pre-R30) | justified |
| `src/DwarfMapper.Generator/Pipeline/EnumConverter.cs:440` (**emitted into consumer code**) | CS0612, CS0618 around an exhaustive enum switch | inline: "[Obsolete] members are still legal values of this enum; an exhaustive map must name them" | the three emission tests (Step 1) | **STILL OPEN** (P1: "the docs page must say it exists"; `docs/` never mentions it) | justified; needs a docs line |
| `benchmarks/DwarfMapper.Benchmarks/Program.cs:469` | CA1008 | :466-468 "a 1-based enum with no 'unset' value is the shape Offset exists for…" | not held | NEW (2026-09-07) | justified |
| `samples/DwarfMapper.Gallery/50_MapDenseEnumKeys.cs:34` | CA1008 | :31-33, the same reason as above | not held | NEW (2026-09-07) | justified |
| `samples/DwarfMapper.Conformance/Program.cs:429` | CA1508 | :427-428 "the analyzer can see INTO the generated method bodies and prove these hold — which is a compliment to the generator, not dead code" | not held | NEW (pre-R30) | justified |
| `samples/DwarfMapper.Conformance/Program.cs:453` | CS0618, CA1508 | :452 "Reading the obsolete member is exactly what the assertion is for…" | not held | NEW (pre-R30) | justified |
| `samples/DwarfMapper.Conformance/Program.cs:687` | CA1508 | **none** (the line above is `f47);`; the reason exists 260 lines earlier at :427) | not held | NEW (pre-R30, 2026-08-16) | needs a reason |
| `tests/DwarfMapper.Generator.Tests/Contracts/SurfaceCatalog.cs:330` | MA0099 | :327-329 "AttributeTargets is a BCL [Flags] enum with NO zero member…" | not held | NEW (pre-R30) | justified |
| `tests/…/Contracts/SurfaceFixtures.cs:34` | CS0414, CA1802, CA1823, IDE0051 | :30-33 "…Disabled for this block only, with the reflective consumer sitting three lines below as the reason." | not held (the twin `.editorconfig` section is UNCHANGED) | UNCHANGED | justified |
| `tests/…/DeepSourcePathGeneratorTests.cs:305` | CA1305 | :303-304 "this method only assembles C# source text … no locale-sensitive formatting" | not held | NEW (pre-R30) | justified |
| `tests/…/FeatureCombinationFuzzTests.cs:11` (file-wide, no restore) | CA1062 | :10 "theory parameters are supplied by [MemberData] (never null); validation would be noise." | not held | NEW (pre-R30) | justified |
| `tests/…/FeatureCombinationInvalidTests.cs:9` (file-wide, no restore) | CA1062 | **none**: the comment above describes the suite's intent and never names CA1062 | not held | **STILL OPEN** | needs a reason (copy the reason from `FeatureCombinationFuzzTests.cs:10`) |
| `tests/…/Round31/DeepRecursionPocTests.cs:93` | CA1031 | inline: "the assertion is on WHICH exception escaped" | not held | NEW (2026-09-26) | justified |
| `tests/…/SelfValidation/ApiReferenceRendererTests.cs:115`, `:130` | CA1034, CA1812 | :111-114 "A PUBLIC NESTED TYPE IS THE SHAPE UNDER TEST…" | not held | NEW (2026-09-21) | justified |
| `tests/…/SelfValidation/ApiReferenceRendererTests.cs:196` | CA1008 | :193-195 "HAVING NO MEMBERS IS THE POINT…" | not held | NEW (2026-09-21) | justified |
| `tests/DwarfMapper.IntegrationTests/DictionaryKeyCollisionRuntimeTests.cs:15` | CA1069 | inline: "Duplicate enum value is the POINT of this fixture…" | not held | NEW (pre-R30) | justified |
| `tests/…IntegrationTests/NoneModeCollectionDepthRuntimeTests.cs:6` (file-wide) | CA5394 | :4 "System.Random is used ONLY for deterministic, seeded test-data generation. Not security." | not held | NEW (pre-R30) | justified |
| `tests/…IntegrationTests/RegistryConcurrencyTortureTests.cs:115` | CA1031 | the inline text is only the rule title ("Do not catch general exception types"); the real reason is on :116 "Deliberate: an unhandled exception on a raw Thread tears down the test PROCESS…" | not held | NEW (pre-R30) | justified (format: move the reason above the directive) |
| `tests/…IntegrationTests/SetNullAdversarialRuntimeTests.cs:7` (file-wide) | CA5394 | :4-5 "…(replayable counterexamples) — never for security." | not held | NEW (pre-R30) | justified |
| `tests/…IntegrationTests/SetNullCollectionCycleRuntimeTests.cs:6` (file-wide) | CA5394 | :4 "System.Random is used ONLY for deterministic, seeded test-data generation. Not security." (blame 2026-06-13) | not held | **R30-MISRECORDED** | justified |
| `tests/…IntegrationTests/SimdWidenListShapeRuntimeTests.cs:166` | CA5394 | :164-165 "a SEEDED generator is the point: a failing case must reproduce…" | not held | NEW (2026-10-12) | justified |
| `tests/…IntegrationTests/SimdWidenRuntimeTests.cs:6` (file-wide) | CA5394 | :4 "seeded System.Random for deterministic adversary/fuzz data only. Not security." | not held | NEW (pre-R30) | justified |
| `tests/…IntegrationTests/UserConversionRuntimeTests.cs:3` (file-wide, no restore) | CA1815, CA1062, CA2225, CA1305, **IDE0079** | **none anywhere in the file** | not held | **STILL OPEN** | needs a reason, and IDE0079 is a candidate to remove: IDE0079 is the "unnecessary suppression" rule, so listing it hides whether the other four ids still fire |
| `tests/DwarfMapper.Testing.Tests/GraphOracleOrderingTests.cs:194` | CA1065, MA0015 | :192-193, one line per id | not held | NEW (2026-09-15) | justified |
| `tests/…Testing.Tests/GraphOracleSensitivityTests.cs:80` | CA1819 | :79 "an array-typed navigation is the shape IsNavigationProperty's array arm exists for." | not held | NEW (pre-R30) | justified |
| `tests/…Testing.Tests/ObjectFactoryGraphShapeTests.cs:64`, `:95` | CA1008, CA1028 | :61-63 "EMPTY IS THE POINT…", :92-94 "ULONG IS THE POINT HERE…" | not held | NEW (pre-R30) | justified |

Directives inside generator-input strings (the compilation under test, not this repository's build):

| Location | Silenced | Stated reason | R30 status | Verdict |
|---|---|---|---|---|
| `tests/…/RegistryDiagnosticsGenTests.cs:458`, `:476` | CS8625 in the test source | The surrounding test (:440-448) explains `[MapProperty(null)]` but never names CS8625 | **STILL OPEN** | needs a reason (low) |
| `tests/…/SelfMapDiagnosticTests.cs:177`, `tests/…/SetNullCycleGeneratorTests.cs:293`, `tests/DwarfMapper.NegativeCases/SuppressionPathwayTests.cs:60` | DWARF076 / DWARF108 | The test exists to prove that `#pragma` does not reach a generator-reported DWARF id | NEW (pre-R30) | justified (subject under test) |

Adjacent form: `#nullable disable` at `tests/DwarfMapper.ConsumerTests/CleanCorpus/OrderingLegacyContracts.cs:3`. Reason at :7-11:
"These types were written before nullable reference types existed and were never annotated…". NEW (2026-09-06), justified.

## 2. `[SuppressMessage]` / `[UnconditionalSuppressMessage]`

There are 6 real attribute sites: 1 in src, 4 in tests, 1 in benchmarks. All carry a `Justification`. Round 30 recorded "4 (tests
only)". `UnconditionalSuppressMessage` occurs 0 times; `ShippedRuntimeSafetyTests` bans it in `src/DwarfMapper` only. The
remaining matches are test inputs or prose: `SelfMapDiagnosticTests:198/219`, `SetNullCycleGeneratorTests:317`, `MapCloneIntentTests`,
`SuppressMessageLookalikeCoverageTests` and the NegativeCases case files.

| Location | Silenced | Stated reason (Justification) | Held by | R30 status | Verdict |
|---|---|---|---|---|---|
| `src/DwarfMapper/DwarfCollectionMap.cs:67` (**shipped**) | CA1002 on `ToList<TSource,TDestination>` | "The return type IS the registry key's destination type. This overload exists because a pair registers `IEnumerable<S> -> List<D>`…" | not held (no Justification scan) | NEW (2026-09-26, `0af2f2e`) | justified |
| `tests/…/Contracts/RepoPaths.cs:24` | CA1819 | "A small, fixed, read-only set of paths consumed by index…" | not held | NEW (pre-R30) | justified |
| `tests/…/Contracts/SurfaceCatalog.cs:790` | CA1031 (the swallow at :805) | "Reflective construction of an arbitrary attribute type… can fail in ways this catalogue does not control…" | not held | NEW (pre-R30) | justified |
| `tests/…/Framework/GeneratorCacheAssert.cs:138` | CA1031 | "A property getter here is arbitrary third-party/generator model code; any exception it throws must be swallowed so this test helper fails for the leak it's checking…" (skips are recorded, see Category 8) | not held | NEW (pre-R30) | justified |
| `tests/DwarfMapper.IntegrationTests/SetNullAdversarialRuntimeTests.cs:157` | **DWARF108** (a product diagnostic, accepted through the generator-read hatch) | "Deliberate: SnStructNodeDto is a struct on purpose, to prove DWARF108 honours [SuppressMessage] the same way DWARF076 does…" | not held | NEW (2026-09-10) | justified (subject under test) |
| `benchmarks/DwarfMapper.Benchmarks/AmbientFacadeBenchmarks.cs:55` | CA1859 | "The interface dispatch is deliberate and is part of the measurement; see above." | not held | NEW (2026-09-26) | justified |

## 3. `<NoWarn>`, `WarningsNotAsErrors`, `TreatWarningsAsErrors=false`, analyzer switches

- `Directory.Build.props` sets `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-all` and `AnalysisMode=All`, with an empty
  `<WarningsNotAsErrors>`.
- No project sets `RunAnalyzers` or `EnableNETAnalyzers` to false.
- There are **19 `<NoWarn>` elements in 19 project files, carrying 105 id entries**. One element is empty.
- **About 34 ids in 7 files have no reason of their own.**
- **11 files carry comments naming ids that are no longer in the list.**

Reasons were judged per id. A general sentence counts only for the ids it plainly describes.

| Location | Ids | Stated reason (quoted or summarized) | Held by | R30 status | Verdict |
|---|---|---|---|---|---|
| `src/DwarfMapper.Testing/DwarfMapper.Testing.csproj:11` (**ships**) | CA5394, CA1510, CA1032 | :7 "System.Random is intentional — seeded determinism is the feature, not crypto." :8 "CA1510: ThrowIfNull style rule; explicit pattern is fine here." :9-10 "RoundTripException and LensLawException always require diff data; parameterless/string ctors would produce useless objects." | not held | CA5394/CA1510 **R30-MISRECORDED** (comments from 2026-07-05); CA1032 RESOLVED (reason added 2026-09-09) | CA5394 justified (project-wide; the plan preferred site scope). **CA1510: candidate to remove**, since round 30 judged it "fixable, not silenceable". CA1032 justified. |
| `src/DwarfMapper.CodeFixes/DwarfMapper.CodeFixes.csproj:19-20` | none (`<NoWarn>$(NoWarn)</NoWarn>`) | :19 "CA1303: code-action titles are short fixed UI strings, not localized resources." That id was removed by `d7e4744` (2026-08-27) | not held | NEW (pre-R30) | **candidate to remove**: an empty element under a reason for a suppression that no longer exists |
| `tests/DwarfMapper.CompilerTests/…csproj:9` | CA1707, CA1515 | :6-7, one per id. :8 names CA1861, which is not in the list | not held | **R30-MISRECORDED** | justified; stale CA1861 comment |
| `tests/DwarfMapper.ConsumerTests/{Contracts,ProviderA,ProviderB}/…csproj:10` (×3) | CA1002, CA2227 | :9 "DTOs expose List<T>/arrays exactly as a real consumer's do." :8-9 name CA1515/CA1819, which are not in the list | not held | **R30-MISRECORDED** | justified; stale comments |
| `tests/DwarfMapper.ConsumerTests/Host/…csproj:17` | CA1707, CA1515, CA1002, CA1031 | :8-16, one paragraph per id | not held | **R30-MISRECORDED** | justified |
| `tests/DwarfMapper.ConsumerTests/CleanCorpus/…csproj:23` | CA1707, CA1515, CA2227, CA1002, CA1034, CA1819, CA1000 | :7-22, per id. :11 names CA1720/IDE1006 (not in the list). :24-26 "NU1903: … same reasoning and same suppression as the benchmark and differential projects" now describes the `NuGetAuditSuppress` at :30 | not held | NEW (pre-R30) | justified; stale comments |
| `tests/DwarfMapper.CorpusTests/…csproj:8` | CA1707, CA1515, CA1002, CA2227, CA1861, CA1008, CA2225, **CA1062** | :6-7, one sentence: "underscored test names, public helper types, mutable collection properties, value fields, and culture/clock literals are all intentional fixtures". It does not cover CA1861, CA1008, CA2225 or CA1062 | not held | **STILL OPEN** | needs a reason (4 ids); CA1062 is a candidate to remove |
| `tests/DwarfMapper.DifferentialTests/…csproj:10` | CA1707, CA1515, CA2227, CA1002, CA1819, **CA1861** | :6-9 per id, except CA1861. :11-14 "NU1903: … Same reasoning and same suppression as the benchmark project" describes the `NuGetAuditSuppress` at :18 | not held | NEW (pre-R30) | needs a reason (CA1861); stale NU1903 heading |
| `tests/DwarfMapper.Generator.Tests/…csproj:13` | CA1707, CA5394, CA1515, CA1861, CA1508 | :6-12 per id; the CA1707 and CA5394 comments appear twice. CA1508: "dead-code analyzer false positive in test assertions" | not held | **R30-MISRECORDED** | justified (CA1508 project-wide for a claimed false positive is weak: needs investigation whether site scope would do); dedupe the comments |
| `tests/DwarfMapper.IntegrationTests/…csproj:34` | 13 CA ids + **DWARF038, DWARF044** | :6-33 per id. DWARF038/044: "these runtime tests deliberately exercise DwarfMapper's SUPPORTED lossy conversions … Suppress them here so the repo-wide TreatWarningsAsErrors does not turn intentional feature coverage into build errors (a consumer downgrades them project-wide in a .globalconfig…)" | not held (`SuppressionAdviceTests` scans text, not project files) | NEW: the conflict arose with the owner's ruling of 2026-09-27, "there should be no reason to do nowarn with dwarf" (`SuppressionAdviceTests.cs:67`) | **needs investigation**. DWARF038 has a per-site remedy (`[MapProperty(Use = …)]`, `docs/diagnostics.md` dwarf038) and a `.globalconfig` route; DWARF044 has no per-site intent, only `.globalconfig`. The project uses the mechanism its own reason tells consumers not to use. |
| `tests/DwarfMapper.NegativeCases/…csproj:8` | CA1707 | :6. :7 names CA1515 (not in the list) | not held | **R30-MISRECORDED** | justified; stale comment |
| `tests/DwarfMapper.Testing.Tests/…csproj:7` | CA1707, CA1515, CA1051, CA2227, CA1002 | :6 "public test-helper DTOs by design". Also names CA1819 (not in the list) | not held | **R30-MISRECORDED** | justified; stale comment |
| `samples/DwarfMapper.AotBench/…csproj:16` | CA1515, CA1303, **CA1819, CA1815** | :14-15 "… fixed UI strings (CA1303); intentional perf-loop shapes." Names CA1050 (not in the list) | not held | **STILL OPEN** | needs a reason (CA1819, CA1815) |
| `samples/DwarfMapper.AotSample/…csproj:24` | CA1515, **CA1303**, CA2007, MA0004, CA1819, **DWARF038, DWARF044** | :13-23 per id except CA1303. DWARF: "this sample DELIBERATELY exercises the narrowing … and parse/format … conversion paths to prove they are AOT-safe … making them explicit would defeat the sample". Names CA1050 (not in the list) | not held | **STILL OPEN** (30.2 special case) | needs a reason (CA1303). DWARF ids: **needs investigation** under the 2026-09-27 ruling; a sample-scoped `.globalconfig` is the route the docs teach |
| `samples/DwarfMapper.Conformance/…csproj:10-11` | `AnalysisMode=Recommended` + 14 ids, incl. CA1062, CA1031, **DWARF038** | :7-9, one sentence: "Relax the repo's maximum analyzer profile like the Gallery does — these are teaching/verification POCOs, not production hygiene models." | not held | **STILL OPEN** | needs a reason for each of the 14 ids. The DWARF038 entry is a candidate to move to `.globalconfig`. AnalysisMode: round 30 said restore `All`. |
| `samples/DwarfMapper.Gallery/…csproj:10,15` | `AnalysisMode=Recommended` + 10 ids | :7-9 "…Console output literals, public sample POCOs in an exe…" (covers CA1303 and CA1515); :11-14 cover CA1051 and CA1815. CA1819, CA2227, CA1002, CA1861, CA5394 and CA1822 have no reason | not held | **STILL OPEN** | needs a reason (6 ids) |
| `benchmarks/DwarfMapper.Benchmarks/…csproj:13` | CA1050, CA1515, CA1819, CA5394, CA1707, CA1815, CA2227, CA1002 | :9-12 cover only CA1515 (and name CA1822 and CA1303, which are not in the list); CA1050's reason is in `benchmarks/.globalconfig:9-11`. :14-17 "NU1903…" describes the `NuGetAuditSuppress` at :21 | not held | NEW (pre-R30) | needs a reason (6 ids); stale comments |
| `NuGetAuditSuppress` GHSA-rvv3-g6hj-g44x ×3 (Benchmarks :21, DifferentialTests :18, CleanCorpus :30) | one advisory: AutoMapper 14.0.0, CVE-2026-32933 | "AutoMapper 14.0.0 (last MIT release, benchmark/differential only): CVE-2026-32933 uncontrolled recursion." Plus the licensing comments | partly held (`AuditSuppressionScanTests` forbids the NU190x NoWarn route; the suppress set itself is not pinned) | NEW (2026-09-26, round 31 T01) | justified |
| `Directory.Build.props:42` | `NuGetAudit=false` when `DWARFMAPPER_OFFLINE_IMAGE=true` | :36-41 "…runs this audit ONLINE while it is built, then declares itself … No workflow or script sets it…" | `OssScannerImageTests` (ordering and no other taker) | NEW (round 33) | justified |
| `.github/workflows/ci.yml:755` (roslyn-forward-compat), `:1177` (preview-sdk-canary) | `-p:TreatWarningsAsErrors=false` | :740-750 "…this leg fails on what would actually answer it NO: a compile error, or a failing test. New diagnostics are reported instead…" (as a `::notice`) | not held | NEW | justified while those legs are informational (see Category 8) |
| `scripts/nowarn-audit.ps1` | — (the detector for stale NoWarn ids) | header: "settles it by removal rather than by reading" | — | NEW | **needs wiring**: no workflow or housekeeping stage runs it, so stale ids accumulate silently (see the stale-comment drift above) |

## 4. `.editorconfig` / `.globalconfig` severity downgrades

There are 14 `severity = none` entries in four `.globalconfig` files, each with a reason. Round 30 recorded only the
`.editorconfig` section. No scan holds the rule; the files' own header comments ("Every disable below has a one-line reason - do
not add one without") are the only guard.

| Location | Silenced | Stated reason | Held by | R30 status | Verdict |
|---|---|---|---|---|---|
| `.globalconfig:9,13,17,21,29,33` | MA0048, MA0051, MA0026, MA0003, MA0006, MA0016 (repo-wide) | one reason per id, e.g. MA0006: "C#'s string ==/!= is compile-time-bound ordinal comparison and null-safe…" | not held | NEW (pre-R30, 2026-08-21) | justified |
| `samples/.globalconfig:12` | MA0047 | :8-11 "…This is the Meziantou twin of CA1050, **which all four sample csprojs already NoWarn** for exactly that reason" | not held | NEW (pre-R30) | justified as a ruling, but **the premise is false**: none of the four sample csprojs lists CA1050 (`d7e4744` removed it) |
| `samples/.globalconfig:19`, `benchmarks/.globalconfig:12,17,23`, `tests/.globalconfig:18,26,33` | MA0008, MA0047, MA0002, MA0009 | per id. MA0008: annotating the struct "would silently switch off the very fast path" (BlittableProof refuses `[StructLayout]`). MA0009: "A timeout would also put a WALL CLOCK inside a deterministic gate" | not held | NEW (pre-R30) | justified |
| `.editorconfig:26-31` | IDE0060 left at suggestion | "…not every overload reads every parameter… Strictness that degrades the code is not strictness worth having." | not held | NEW (pre-R30) | justified |
| `.editorconfig:43-68` | `resharper_*` highlightings (Rider-only) | one paragraph per section | — | NEW (pre-R30) | justified (no build effect) |
| `.editorconfig:77-81` | CS0414, CA1802, CA1823, IDE0051 for `SurfaceFixtures.cs` | :70-76 | — | UNCHANGED | — |
| **Analyzers never see generator-emitted code** (no `generated_code = false` override) | every CA/MA rule on `.g.cs` | in-tree: **`Directory.Build.targets:35-38` says the opposite**: "The override that changes that verdict is the scoped `generated_code = false` section in .editorconfig. The two halves are orthogonal and both are required." No such section exists. The real reason is in commit `d38cda1` (2026-08-23): the override was left OFF because per-rule severity in a path-scoped section was not honoured, which would turn 1,066 intent findings into errors | `GeneratedCodeIsWarningFreeTests` covers CS warnings only | NEW (pre-R30) | **needs a reason in-tree** (correct the comment). **Needs investigation**: is the I21 blocker still real on SDK 10.0.101, and were the two FILED findings from `d38cda1` (CA1852 sealing, CA1508 re-read) ever resolved? |

## 5. Coverage exclusions

This category is held by `RatchetInvariantScanTests` R3/R5. Only the scope gaps and the items that changed are listed.

| Location | Silenced | Stated reason | Held by | R30 status | Verdict |
|---|---|---|---|---|---|
| 9× `[ExcludeFromCodeCoverage]` in `src/DwarfMapper/*Attribute*.cs` | compile-time-only attributes | "compile-time-only attribute, consumed by the generator (round-22 P4's one sanctioned category)…" | R3 (exact pin) | UNCHANGED | — |
| `codecov.yml:117-125` ignore list; `:53-56` project status informational | samples, benchmarks, tests, bin, obj; the whole-repo number | :118-120 "Not shipping code…"; :54-55 "The whole-repo number is informational…" | R5 | UNCHANGED | — |
| `.github/workflows/ci.yml:147` `fail_ci_if_error: false` | a failed Codecov upload | :119-122 "An upload failure means Codecov was unreachable… **The blocking verdict is the STATUS CHECK**, configured in codecov.yml, which simply does not appear if the upload never arrived." | not held | NEW | **needs investigation**: master's ruleset requires no status check (see "Outside the tree"), so a missing Codecov status blocks nothing |
| `scripts/housekeeping.ps1:422` `-assemblyfilters:+<five gated>` | every assembly outside the five | R1's "exactly the five gated assemblies" | R1 | NEW (pre-R30) | justified |
| `src/Shared/DwarfLimits.cs` is in no Codecov component | — | — | — | — | no finding: the file holds three `const`s and has no coverable lines |

## 6. Mutation exclusions

This category is held by R4, `MutationScopeScanTests` and `PerfCategoryIsolationScanTests`. There are 0 `!` negations, 0
`ignore-mutations` and 0 `ignore-methods` in all six configs (UNCHANGED). The single `// Stryker disable all` at
`DocSnippetInjector.cs:36` is pinned by R4 (UNCHANGED). The equivalent-mutant ledger (61 rows / 64 mutants) is an adjudication,
pinned by R3.

| Location | Silenced | Stated reason | Held by | R30 status | Verdict |
|---|---|---|---|---|---|
| `stryker-config*.json` `mutate` inclusion lists | everything outside 29 of 153 `src` files: **12.4 %** of src lines are mutated; the generator **6.9 %** (2,561 of 36,958 lines); Shared 0 % | `Issues/round27/AUDIT-mutation-scope.md` §"Would exposing more pay?" (a ranked cost argument) | `MutationScopeScanTests` (the figures are true) | NEW (pre-R30; round 30 recorded only the negations) | justified as declared. Note that the full-coverage ruling of 2026-09-10 covers branches, not mutation scope |
| all six configs, `test-case-filter: "Category!=Perf"` | `InterfaceBucketTests`' wall-clock ratio in every leg | `aad3cd8` (2026-10-26): "It costs no score: in the last complete local runtime-leg report the test killed 11 mutants and was the sole killer of none." Also ci.yml:322-325 | `PerfCategoryIsolationScanTests.Every_mutation_leg_keeps_the_Perf_category_out_of_its_runs` | NEW | justified. **Scope gap: "sole killer of none" was measured for 1 leg of 6.** **Stale contradicting reasons:** `stryker-config.json` comment NOTE 2: "No 'test-case-filter' and no 'test-projects' key appear here, deliberately…", and `stryker-config.runtime.json` comment: "WHY THERE IS NO TEST FILTER, AND WHY ADDING ONE WOULD BE A REGRESSION". Both now sit beside the filter line. |

## 7. Skipped or filtered tests

There are 0 `Skip =`, `SkippableFact` or `Assert.Skip` (UNCHANGED). The CI category filters are held by
`PerfCategoryIsolationScanTests`, `CiGateScanTests` and `CiToolPrerequisiteScanTests`; those are listed only as context. The gaps
are tests that **pass silently** on a precondition.

| Location | Silenced | Stated reason | Held by | R30 status | Verdict |
|---|---|---|---|---|---|
| `[Trait("Category","Perf")]` (`IntegrationTests/Round31/InterfaceBucketTests.cs`), excluded at ci.yml:109/778/1094/1199/1234, release.yml:49, `.oss-scanner/Dockerfile:46`, housekeeping:411 and the Stryker configs; runs only in `perf-tests` (nightly / full-ci) and housekeeping stage 1c | the wall-clock ratio on every push | release.yml:48 "wall-clock ratio tests run in CI's nightly perf-tests job, never on a release runner"; housekeeping.ps1:407-410 | `PerfCategoryIsolationScanTests`, `CiGateScanTests` | NEW (round 31 T19) | justified |
| `[Trait("Category","SurfaceMatrix")]`: own step at ci.yml:186 | — | — | `CiToolPrerequisiteScanTests` | UNCHANGED | — |
| `[Trait("tier","exhaustive")]` ×5 | — (no filter selects or excludes it) | — | — | NEW | label only; no finding |
| `tests/…/FeatureCombinationFuzzTests.cs:179-182` `Full_power_set_compiles_on_every_emit_path` returns early unless `DWARF_FUZZ_FULL=1` | the full 2^16 power-set proof (~262k compilations) **passes without running in every CI job** | :175-178 "Gated behind DWARF_FUZZ_FULL=1 so routine `dotnet test` stays fast… **CI/nightly/on-demand runs the full exhaustion.**" This is false: no workflow sets the variable, and `housekeeping.ps1:69` sets `$SkipExhaustion = $true` under `-Nightly` (since 2026-08-21, `29351b1`); only a local default housekeeping run executes it (:455-461) | not held (the hollow detector cannot see an env-gated return) | **R30-MISRECORDED** ("Not defects: the DWARF_FUZZ_FULL gate") | **needs investigation**: run it in a nightly job (~6 min), or correct the claim. A no-op reports as *passed*, not *skipped* |
| `tests/DwarfMapper.IntegrationTests/InstructionSetMatrixTests.cs:80-87` | the ISA-matrix anti-vacuity assertion passes as a no-op unless `scripts/isa-matrix.ps1` exports `DWARF_ISA_EXPECT_*` | :85 "Not running under the matrix — nothing is claimed, so nothing is asserted." No workflow or housekeeping stage runs `isa-matrix.ps1` | not held | NEW (pre-R30) | needs a reason (where it runs) or a nightly leg |
| `tests/…/SelfValidation/ConformanceArtifactScanTests.cs:66-69`, `:91-94` | both tests return as a pass when `conformance/results/` is missing | :68 "nothing published yet — the gate creates it on first run". The directory now holds 6 committed artifacts, so the branch can only hide a deletion of the evidence | not held | NEW (pre-R30) | **candidate to remove** (replace with a failure) |
| `GoldenCorpusTests.cs:39` (`GoldenManifest.UpdateEnvVar`), `AssemblyScanTests.cs:224` (`DWARF_SELF_HEAL`) | rewrite modes | asserts it is not running in CI (:41-42); routed through `RepoWriteGuard` | the test itself | NEW (pre-R30) | justified |
| `DWARF_DEEP` (`tests/Shared/DeepTier.cs:278`), `DWARF_BENCH_SMOKE` (`benchmarks/…/Program.cs:24`) | population size / benchmark mode | `.runsettings:3-27` | `DeepTierSelfTests` | NEW (pre-R30) | justified (not a skip) |

## 8. Swallowed failures

### C#: `src/` (8 catch clauses: 0 in the generator, 0 in the runtime)

| Location | Silenced | Stated reason | Held by | R30 status | Verdict |
|---|---|---|---|---|---|
| `src/DwarfMapper.DocTooling/ApiReferenceRenderer.cs:180`, `:196` | a throwing ctor or getter becomes `null`, rendered "—" | :187-188 "A getter that throws costs that one cell, not the page."; :160-170 | `ApiReferenceRendererTests` | NEW | justified (non-shipped doc tooling) |
| `src/DwarfMapper.DocTooling/OptionTableRenderer.cs:132` | a throwing ctor becomes `null`, so the defaults column shows "—" | **none** (its sibling above states one) | not held | NEW | needs a reason (low; non-shipped) |
| `src/DwarfMapper.Testing/GraphOracleComparer.cs:683` (**ships**) | `OverflowException` in the decimal comparison falls back to `ScalarEquals`; a diff is still reported | none | not held | NEW | justified in effect; needs a reason comment |
| `src/DwarfMapper.Testing/GraphOracleComparer.cs:913-920` (**ships**) | a sort-key `ToString()` throwing `InvalidOperationException` / `ArgumentException` returns `0` (treated as equal) | none at the site. Test side, `GraphOracleOrderingTests.cs:192`: "throwing from ToString is exactly the consumer shape the oracle's sort-key catch exists for" | `GraphOracleOrderingTests` (`15e38b4`) | NEW | needs a reason at the site. *Inferred*: the failure mode is a spurious diff, not a masked one |
| `src/DwarfMapper.Testing/ObjectFactoryV2.cs:487` | — (rethrows, named and loud) | :489-490 owner ruling 2026-09-15 | tested | — | not a swallow |
| `src/DwarfMapper.Testing/ObjectFactoryV2.cs:564` `SafeTypes` (**ships**) | a half-loadable assembly yields its loadable types; `LoaderExceptions` are discarded | :557 "A half-loadable assembly must not take the whole scan down." | `51e5f5c` (DanglingDependencyTests) | NEW | justified. *Inferred*: an abstract member never gets a subtype from the unloadable part, and nothing says so |

### C#: tests and samples (42 catch clauses; listed are those that neither rethrow, assert, record nor report)

| Location | Silenced | Stated reason | Held by | R30 status | Verdict |
|---|---|---|---|---|---|
| `tests/…/Fuzzing/IndependenceOracle.cs:109-112` (+ `:66` `depth > MaxDepth` returns) | a throwing getter skips that subtree, and anything deeper than 32 levels is unexamined, so "no shared collection" can pass for parts never looked at | none | not held | NEW (pre-R30) | **needs investigation**: `GeneratorCacheAssert.cs:214/241` records such skips (`RecordSkip`) and fails, for exactly this reason |
| `tests/…/Contracts/SurfaceCatalog.cs:805` `catch (Exception) {}` | reflective attribute construction failing | :807-808 "the default is simply unknown, so every value is emitted" + the `[SuppressMessage]` Justification | not held | NEW (pre-R30) | justified (conservative outcome) |
| `samples/DwarfMapper.AotSample/Program.cs:31-33`, `:52-54` | **"ERROR: expected OverflowException" / "ERROR: expected FormatException" are printed and the program continues with exit 0.** The other 35 ERROR sites `return 1`. The CI `aot-trim-gate` (ci.yml:653-664) and ubuntu-26-canary judge only the exit code | none. ci.yml:650-651 claims the sample "is written as a behavioural gate" | `CiGateScanTests.The_AOT_gate_executes_the_binary_rather_than_only_publishing_it` (not per check) | NEW (pre-R30, 2026-06-10) | **candidate to fix**: two of the gate's checks cannot fail |
| `samples/DwarfMapper.Conformance/Features.cs:39`, `:52` | `Throws<TEx>` / `NoThrow` turn any other exception into `false` | — | counted as FAIL by `R.Check`, and the run returns 1 | NEW | justified (loud, though the exception detail is lost) |

### PowerShell / bash / Python in `scripts/`

| Location | Silenced | Stated reason | Held by | R30 status | Verdict |
|---|---|---|---|---|---|
| `scripts/mutation-battery.sh:147-148`, `:199-200` `git checkout -- … 2>/dev/null \|\| true` | a failed restore leaves a mutated `src/` or `docs/` file in the tree | :197-198 "a half-applied mutation left behind would read as a real regression later". No `git diff --quiet` check follows | `MutationMethodologyScanTests` (catalogue only) | NEW | needs a guard (manual tool; no workflow runs it) |
| `scripts/mutation-battery.sh:174`, `:180` `dotnet test … >/dev/null 2>&1` | any test-run failure, including infrastructure, counts as a kill | compile is checked first (:160-168) | — | NEW | needs investigation (low) |
| `scripts/mutation-battery.sh:155` `sed … \|\| true` | — | caught by the STALE check at :157-160 | — | — | justified |
| `scripts/gate-checks.ps1:83` `catch { return $null }` in `Get-LegAdjudication` | a missing, unfenced or unparseable ledger silently disables the above-ceiling contamination check | :66-68 covers only the case "a leg the ledger does not carry" | indirectly: `RatchetInvariantScanTests` R3 fails the suite if the ledger is missing, unfenced or invalid JSON | NEW | justified; widen the comment |
| `scripts/gate-checks.ps1:219,305,311,529,554`, `housekeeping.ps1:226,480,497,538,586` `-ErrorAction SilentlyContinue` | enumeration and probe errors | each is followed by a vacuity guard or an explicit throw (e.g. gate-checks :531-534, :556-560; housekeeping :229-231, :498-501) | — | NEW | justified |
| `scripts/gate-checks.ps1:495` `catch { }` around `taskkill` | — | it is followed by `throw` (:496) | `MutationLaunchContractTests` | NEW | justified |
| `scripts/gate-checks.ps1:606`, `:617` | — | rethrown (:620-628) | — | — | not a swallow |
| `scripts/conformance-gate.sh:48` `set +e`; `:56`, `:76` `\|\| true` | — | :47 "`set -e` would abort before the exit code can be inspected"; parse failure fails at :66-70 | `CiGateScanTests` | NEW | justified |
| `scripts/corpus-diagnostic-density.ps1:60-61` `git … 2>$null` | if git fails, provenance prints an empty revision and "(src/ clean)" | :56-59 (provenance must be printed) | — | NEW | needs a guard (low) |
| `scripts/repro-pack.ps1:80`, `scripts/isa-matrix.ps1:53,80-82` | cleanup in `finally` | :78-79 | — | — | justified |

### Workflows and the OSS scanner image

| Location | Silenced | Stated reason | Held by | R30 status | Verdict |
|---|---|---|---|---|---|
| **`.oss-scanner/Dockerfile:46-48`** `\|\| echo "WARNING: dotnet test reported failures"` | a red push-time suite (or a hang killed by `--blame-hang`) does not fail the image | :44-45 "CI's push-time suite, so a broken image shows in its own build log… **A failing test does not fail the image: the audit is still worth running against a red tree.**" | **not held** (`OssScannerImageTests.The_image_runs_CI_s_push_time_test_filter` holds the filter, not this line) | NEW (round 33) | justified as stated. `threat_model.md` ("How to exercise it") never tells the agent the image's suite may have been red. Candidate: persist the outcome inside the image and name it there, and pin the line in the test |
| `ci.yml:705` `roslyn-forward-compat` (job-level `continue-on-error`) | the whole job | :699-702 "NON-BLOCKING ON PURPOSE, FOR NOW… **Flip `continue-on-error` to false once it has been seen green** — a gate that cannot fail is decoration" | not held | NEW (2026-08-11) | **candidate to remove**: green on all 40 nightlies 2026-09-18 → 10-27 at step level (e.g. run 40477294097; those runs' overall red comes from other jobs, such as the dashboard 504s and the mutation legs) |
| `ci.yml:1129` `preview-sdk-canary` | the whole job | :1101-1105 "**FLIP … once this leg has been seen GREEN on pushed master, citing that run id**" | not held | NEW (2026-08-22) | **candidate to remove**: red 2026-09-18 → 09-28, then green on 29 consecutive nightlies 2026-09-29 → 10-27 (cite run 40477294097) |
| `ci.yml:1214` `ubuntu-26-canary` | the whole job | :1209-1211 "**Once it has been green for a few nights, move every pin to ubuntu-26.04 … and delete this job.**" | `CiRunnerImagePinScanTests` (pins) | NEW (2026-10-12) | **candidate to remove**: green on 15 of 15 nightlies 2026-10-13 → 10-27 |
| `actions-security.yml:42` `zizmor … \|\| true` | zizmor findings | :9-12 "REPORTING MODE, deliberately… **The first run's findings are triaged by the owner (the baseline), and only then does this become blocking**"; the SARIF-presence assert fails the job | not held | NEW (2026-09-27, round 31 T20) | **candidate to remove**: master run 40502147474 logs `zizmor findings: 0`. Also, the master ruleset's `code_scanning` rule lists only CodeQL, so zizmor alerts cannot block a merge |
| `step-security/harden-runner` `egress-policy: audit` in **all 19 jobs** (ci 16, release 2, actions-security 1) | network egress (recorded, never blocked) | e.g. ci.yml:173-174 "the recorded baseline is what a later round tightens to an allow-list (block mode)" | not held | NEW (round 31 T20) | needs a reason with a date or criterion; a task |
| `ci.yml:110` test step `continue-on-error` | — | :107-108 "so a FAILING TEST still reaches the upload"; re-asserted by "Fail the build if any test failed" at :150-155 | — | NEW | justified. The comment at :113-117 ("The inline gate below still runs") contradicts :157-160 ("It was … deleted"); stale |
| `ci.yml:1376` bench-alert step `continue-on-error` | the wall-clock alert | :1264-1279, :1373-1374 "that single line is what keeps a nondeterministic wall-clock measurement out of the build's verdict (R4)"; absence fails at :1329-1345 | R4 | NEW (pre-R30) | justified |
| `ci.yml:343-357` resource-trace `\|\| true`; `ci.yml:596` `\|\| true`; `if: always()` at :406, :412, :483, :548, :555, :1396 | diagnostics, artifact uploads, decontamination | stated at each site; the Perf lane has a vacuity guard at :597-601 | `MutationDecontaminationSweepTests` (decontamination) | NEW | justified |

## 9. Unreported generator conditions (a sample, not a proof)

The heuristic counted early `continue` / `return` with no report in the 8 preceding lines: **579 candidates in 52 files**. Most are
predicate helpers returning `false`. The sample below covers the passes that decide what gets mapped.

| Location | Silenced | Stated reason | Held by | R30 status | Verdict |
|---|---|---|---|---|---|
| `src/DwarfMapper.Generator/Pipeline/MapperExtractor.Pairs.cs:141-157` `ReadPairIgnores` | **`[MapIgnore<T>(null!)]` vanishes** (with a literal `null`, at most CS8625 in the consumer's build) | Its three siblings in the same file report a null name since 2026-09-13: `:26` "A null factory name names no method, exactly as a name matching none does. It is kept, so the pair reports DWARF059…" (`d474e9f`), and likewise `:88` (`fc58fb0`) and `:304`. The skip is pinned by `Coverage/PairScopedIgnoreReaderCoverageTests` (`7b3e1a0`): "Neither may take the mapper down; the pair maps as if the directive were absent." That is a crash-avoidance reason, and the siblings show reporting does not crash | pinned **as silent** | NEW | **needs a reason, or report like the siblings** (DWARF095 is the misspelled-name twin) |
| `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs:1588-1601` `ReadMapValues` (method-level `[MapValue]`) | **`[MapValue(null!, …)]` vanishes** at create, update-into and element-wise endpoints (callers: Phases.cs:1944, 2422, 2567; MapperExtractor.cs:1050). Its pair-scoped twin reports DWARF042 | `FlattenAndMapValueReachTests.cs:283-285` "ReadFlattenRoots and ReadMapValues each drop an argument that is absent or not a string… The alternative is a message with the word 'null' in it." | pinned **as silent** (`A_null_directive_argument_produces_no_element_wise_report_and_no_crash`) | NEW | needs investigation (the stated reason concerns wording, not the drop) |
| `src/DwarfMapper.Generator/Pipeline/MapperExtractor.Flatten.cs:563-570` `ReadFlattenRoots`, `:580-587` `ReadFlattenGraphAttributes` | `[Flatten(null!)]` / `[FlattenGraph(null!, …)]` vanish | as above | partly pinned | NEW | needs investigation |
| `src/DwarfMapper.Generator/Registry/MapToGenerator.cs:298-303` | *inferred*: under `[MapTo]`, a `[MapProperty("Nmae")]` naming a destination that does not exist hits `continue`. DWARFR01–R16 have no "named destination not found". The completeness loop (:334-350) reports only DWARFR02 at the unmapped destination, if one is left, so the misspelled directive itself is never named. The class model reports DWARF008/009 for the same mistake | none at the site | no test found pinning either outcome | NEW | **needs investigation, likely defect**: write the RED probe first |
| `src/DwarfMapper.Generator/Pipeline/BlittableProof.cs:182-201` `TryExplainNearMiss` | a pair whose field counts or field TYPES differ gets no near-miss report (except a one-sided `Nullable<T>`) | :186-190 "Deliberately narrow… An informational diagnostic that common gets suppressed wholesale…". Task 30.5 asked to measure the widening-primitive case and record the number here; **there is no number** | `BlittableProofNearMissTests` | **STILL OPEN** | needs investigation (Task 30.5) |
| `EnumConverter.cs:440` emitted pragma | see Category 1 | — | — | **STILL OPEN** (docs) | needs a docs line |
| Reasoned silences sampled: `MapperExtractor.cs:972-997`, `DwarfGenerator.cs:530-537`, `MapperExtractor.Members.cs:326`, `MapToGenerator.cs:~372` ("Silent when the refusal already said why") | — | stated at each site | — | — | justified |

## Outside the tree and outside the nine categories (for the record)

- **Merge gating (repository setting, owner's click).** Facts from the GitHub API on 2026-10-27:
  - The branch-protection endpoint answers "Branch not protected".
  - The only ruleset ("Default", active, `~DEFAULT_BRANCH`) holds `deletion`, `non_fast_forward`, `code_scanning` (CodeQL only:
    alerts `errors`, security `high_or_higher`), `code_quality` (`errors`) and `pull_request` (0 approvals).
  - There is **no `required_status_checks` rule**. No CI job, Codecov status or conformance gate is required for a merge.
  - In-tree text assumes otherwise: ci.yml:121 "The blocking verdict is the STATUS CHECK"; codecov.yml:55 "The per-component
    statuses below are the real gate".
- **Python runtime (round 30 P1b): STILL OPEN.** `scripts/` has `extracted-reach.py`, `seam-reach.py`, `repro-pack-check.py`,
  `verify-extraction-bodies.py` and `verify-extraction-sites.py`. `python3` appears 5× in ci.yml and 1× in actions-security.yml.
  Neither README nor CONTRIBUTING mentions Python.
- **Scope not scanned:** `*.DotSettings` (Rider severities), `.claude/`, `.codex/`, `docs/`.

---

## Proposed tasks (ranked by risk)

### Shipped code (`src/`)
1. **Null directive names reach a diagnostic everywhere.** Make `ReadPairIgnores` (Pairs.cs:141-157), `ReadMapValues`
   (MapperExtractor.cs:1588-1601), `ReadFlattenRoots` and `ReadFlattenGraphAttributes` (Flatten.cs:563-587) treat `null!` as the
   empty name, as `d474e9f`/`fc58fb0` did for their siblings. The pinned tests `PairScopedIgnoreReaderCoverageTests` (null row)
   and `FlattenAndMapValueReachTests:281-299` flip RED→GREEN. If the owner wants silence kept for any reader, write the reason in
   the reader.
2. **`[MapTo]` misspelled `[MapProperty]` destination** (MapToGenerator.cs:298-303). Start with a RED probe. If it confirms the
   silence, report at the directive: a new DWARFR id, or the class model's DWARF008/009 wording.
3. **Task 30.5:** measure the widening-primitive near-miss over the golden corpus and the Gallery. Ship it, or write the count
   into `BlittableProof.cs:182-201`.
4. **`DwarfMapper.Testing`:**
   - Fix CA1510 rather than silence it (csproj :8/:11).
   - Add reason comments at `GraphOracleComparer.cs:683` and `:913-920`.
   - Decide whether `ObjectFactoryV2.SafeTypes` (:564) should surface `LoaderExceptions` (owner call).
5. **Docs:** name the emitted `#pragma warning disable CS0612, CS0618` (EnumConverter.cs:440) in the enum section of
   `docs/diagnostics.md` or the howto.
6. **Delete** the empty `<NoWarn>$(NoWarn)</NoWarn>` and its stale CA1303 comment (`DwarfMapper.CodeFixes.csproj:19-20`).
   `OptionTableRenderer.cs:132` needs its reason line (DocTooling; non-shipped).

### The mechanism that keeps the rest fixed: round 30's Task 30.4, never built
7. **SelfValidation scans, each with a positive control:**
   - (a) every `<NoWarn>` id has a comment naming it, **and** every id a NoWarn comment names is in the list (both directions
     catch the drift class);
   - (b) every hand-written `#pragma warning disable` has a reason on the line or the line above, in one chosen form; a rule
     title is not a reason; directives inside string literals are excluded;
   - (c) every `[SuppressMessage]` / `[UnconditionalSuppressMessage]` has a non-empty `Justification`;
   - (d) every `.globalconfig` `severity = none` is preceded by a comment;
   - (e) no `DWARF…` id in any project file's NoWarn, which extends `SuppressionAdviceTests` to project files under the
     2026-09-27 ruling.

   Also run `scripts/nowarn-audit.ps1 -FailOnStale` in the nightly or in housekeeping `-Deep`, after measuring its cost (one
   forced build per project).

### Packaging, CI, scripts
8. **AOT gate holes:** `samples/DwarfMapper.AotSample/Program.cs:31-33` and `:52-54` need `return 1`. Add a `CiGateScanTests` row
   that every "ERROR:" line in the sample is followed by a non-zero return.
9. **Flip the three non-blocking jobs** on their own criteria:
   - roslyn-forward-compat (ci.yml:705) and preview-sdk-canary (:1129), citing run 40477294097;
   - ubuntu-26-canary (:1214): move every pin to ubuntu-26.04 and delete the job.
10. **zizmor blocking** (actions-security.yml:42; 0 findings on master). Keep the SARIF-presence assert. Adding zizmor to the
    ruleset's `code_scanning` tools is an owner click.
11. **Owner decision: required status checks on master.** Every gate inventoried here is advisory at merge time, while ci.yml and
    codecov.yml describe the Codecov status as blocking.
12. **OSS scanner image:** make a red suite visible to the agent (persist the outcome inside the image and name it in
    `threat_model.md`), and pin the `|| echo` line and its reason in `OssScannerImageTests`.
13. **harden-runner:** measure the egress baseline of the 19 audit-mode jobs, then set a date or criterion for block mode.
14. **Stryker configs:**
    - correct the two comments the Perf filter contradicts (`stryker-config.json` NOTE 2; `stryker-config.runtime.json` "WHY
      THERE IS NO TEST FILTER");
    - measure "sole killer of none" for the other five legs (`aad3cd8` measured only the runtime leg).
15. **`Directory.Build.targets:35-38`:** correct the `generated_code = false` claim. Re-test round 24's I21 blocker on the pinned
    SDK, and close or carry `d38cda1`'s two FILED findings.
16. **`DWARF_FUZZ_FULL` exhaustion and the ISA matrix:** run them in a nightly job, or correct
    `FeatureCombinationFuzzTests.cs:178` and declare where `InstructionSetMatrixTests` actually asserts.
17. **Scripts:**
    - `mutation-battery.sh`: add a final `git diff --quiet -- src docs`;
    - `corpus-diagnostic-density.ps1:60-61`: print "revision unknown" when git fails;
    - widen the reason comment at `gate-checks.ps1:83`;
    - ci.yml:113-117: fix the stale inline-gate sentence.
18. **Python runtime (round 30 P1b):** declare it in CONTRIBUTING, or port the five scripts.

### Tests and samples
19. **`IndependenceOracle`** (:66, :109): record truncation and fail on a skip, as `GeneratorCacheAssert` does.
20. **Pragma reasons:**
    - `UserConversionRuntimeTests.cs:3` (and drop IDE0079 from its list);
    - `FeatureCombinationInvalidTests.cs:9`;
    - `RegistryDiagnosticsGenTests.cs:458/476`;
    - `Conformance/Program.cs:687`;
    - move `RegistryConcurrencyTortureTests.cs:115`'s reason above the directive.
21. **NoWarn per-id reasons or removal:**
    - Files: CorpusTests:8, DifferentialTests:10, Benchmarks:13, AotBench:16, AotSample:24 (CA1303), Conformance:11 (14 ids),
      Gallery:15 (6 ids).
    - Remove the stale ids named in comments in 11 files, and dedupe Generator.Tests' comments.
    - Move the DWARF038/DWARF044 NoWarns (IntegrationTests:34, AotSample:24, Conformance:11) to a scoped `.globalconfig` or to
      per-site `Use =`, per the 2026-09-27 ruling.
    - Restore `AnalysisMode=All` in Gallery and Conformance (round 30 Task 30.2).
    - Fix the false CA1050 premise at `samples/.globalconfig:10-11`.
22. **`ConformanceArtifactScanTests.cs:66-69`, `:91-94`:** fail rather than pass when `conformance/results/` is missing (6
    artifacts are committed).
23. **Task 30.6:** the 19 `InconsistentNaming` headers, in one reasoned place (Rider-only; lowest risk).
