# SDD ledger — plan: docs/superpowers/plans/2026-08-13-surface-coverage-architecture.md

Worktree: C:/Users/Jouda/RiderProjects/DwarfMapper-surface
Branch: feat/surface-coverage-architecture
Pre-flight scan: clean. (Task 6 deletes existing tests, but only AFTER the replacement obligations pass — sequenced, not contradictory.)

Task 1: complete (commits 02d1a49..6ba797f, review clean). 35 attribute types annotated (brief prose said 32; table had 35 and was authoritative).
NOTE for all later tasks: 3 GeneratedDocsAreCurrentTests failures are PRE-EXISTING IN THIS WORKTREE ONLY — the repo's doc tests assume .git is a directory; in a worktree it is a file. Verified by stashing. Do not chase.
Task 2: complete (commits 6ba797f..f4bc253, review clean). 137 cases / 35 elements => ~959 cells at 7 endpoints. Catalog types are INTERNAL not public (CS0051 - they carry internal-typed members); fine, same assembly.
Task 3: implemented (commit 24612a5). Brief was DEFECTIVE - bijection demand side checked only element-level ProbeKey, orphaning all 14 property-level fixtures. Controller ruling: demand = elementProbeKeys UNION OptionCatalog.ProbeKeys.Values. 14==14. Plan doc corrected on master (94e95ac).
Task 3: review found 1 Critical (MaxDepth/ProbeOverrides comment content lost in fixture merge) + 1 Important (ProbeKeys public, report said internal). Fix round 1/5 dispatched to original implementer.
Task 3: fix round 1/5 (2 addressed, 0 open; commits 24612a5..c8bd291)
Task 3: complete (commits f4bc253..c8bd291, review clean). 14 fixtures == 14 demands. Contracts 315/0/315 unchanged before+after the move.
Task 4: implemented (456553e, fcbda37). Brief code had FOUR defects, all found empirically: RunAll does not surface CS diagnostics (use RunAndGetCompilationErrors); baseline carries its own CS8795 when the fixture already errors; [assembly:] prepend is CS1529; CoLocatedHost template drops memberAttribute. Plan doc corrected on master (642d9ed).
Task 4: review found 1 Critical (Method and Property/Field sites route to BYTE-IDENTICAL source on 5 endpoints - member cells silently measure method semantics) + 1 Important (baseline CS subtraction keys by id alone, can over-subtract a real NotCompilable). Fix round 1/5 dispatched.
Task 4: fix round 1/5 (2 addressed, 0 open; commits fcbda37..ebd1f69)
Task 4: complete (commits c8bd291..ebd1f69, review clean).
CARRY TO TASK 5 (verified against src/DwarfMapper.Generator by the reviewer, not assumed):
  (a) Member-site [MapProperty]/[MapIgnore] are INERT at the 5 method-based endpoints. MapperExtractor reads these attrs off classSymbol/method only; ONLY Registry/MapToGenerator.cs reads them off member symbols. So Silent is the TRUE verdict for those cells -> narrowing AppliesTo there is structural, NOT a divergence.
  (b) DECLARED GAP, counted by test at baseline 14: all 14 probe fixtures lack a member slot marker, so member-site cells are only measured for elements with NO ProbeKey. Those cells return NoSuchSite (honest refusal), never a method-slot fallback.
Task 5: matrix built and committed (09bfc5f). 854 cells, runtime 14s (memoization held).
  Buckets: under-reach 0 / structural 104 (2 findings, applied) / DIVERGENCE 165 cells = 18 findings (NONE ratified) / instrument-gap 188 (new 4th bucket: case-space cannot pose a meaningful question - one ProbeKey per element vs 15-19 option bags; sampled ctor args).
  Two MORE instrument defects found and fixed en route: (1) the harness never drove MapToGenerator, so all 122 Registry cells were FICTION; (2) Build appended the class-site case beside its own [DwarfMapper] (AllowMultiple=false) => CS0579, 95 cells 'passing' unmeasured.
  ARCHITECTURAL GAP in the design: [DwarfSurface].AppliesTo cannot express a claim that differs BY DECLARATION SITE (method-site refused vs member-site silent, same endpoint). 100 cells currently live in a test-side predicate instead of at the declaration - which is the hand-kept test-side knowledge this design exists to remove.
Task 5: BLOCKED ON MAINTAINER - 18 divergences need fix-vs-record rulings; site-aware AppliesTo needs a decision; instrument-gap policy needs a decision. CI job is continue-on-error:true pending those. Task 5 review DEFERRED until decisions land (code will change).
Task 5a: complete (commits 09bfc5f..7408be0, review clean). [DwarfSurfaceSite] added (AllowMultiple, reason is a REQUIRED 3rd ctor arg so an override without one does not compile). Test-side predicate DELETED - reviewer grepped for residue, none. 2 overrides cover the ~100 cells. Results cell-for-cell identical by TRX diff (353 red / 503 green / 856).
Task 5b: implemented (06d1a5e). [DwarfSurfaceProbe] per-property/per-ctor-arity probes; OptionCatalog.ProbeKeys+ProbeOverrides DELETED (subsumed, knowledge verified to survive). Unaskable 188->44 (shrink-only ceiling, floor 34). Matrix red 353->166. Zero cells moved pass->fail.
  Review split of the 187 fail->pass flips: 134 GENUINELY MEASURED / 40 excused-but-counted / 13 unjudged-and-UNCOUNTED.
  IMPORTANT finding: D11 [FlattenGraph] withdrawal is PREMATURE - its fixture's baseline carries DWARF001 by construction, so a non-acting cell reads UnhonouredButLoud and passes BOTH claim branches. A fixture that cannot compile without the element under test can never show that element doing nothing. Fix round 1/5 dispatched.
Task 5b: fix round 1/5 (2 addressed; commits 06d1a5e..a59037d). D11 CONFIRMED LIVE by measurement: [FlattenGraph] Honoured@CreateMap, Silent at Update/Projection/Span/Async. Withdrawal struck.
  NEW FINDING N4 (directly observed, generated file Demo.M.g.cs): two identical [FlattenGraph] directives make the GENERATOR EMIT UNCOMPILABLE CODE - CS1912 duplicate member init. Found by the AllowMultiple x2 axis; the NotCompilable verdict had been hiding it.
  KNOWN LIMITATION carried forward: 96 cells read NotCompilable due to CS8795 (unimplemented partial method after a blocking DWARF error) - conceptually Refused, not NotCompilable. Pre-existing G4/R4 defect, disclosed, shrink-only pinned. ~11% of the matrix carries the wrong verdict.
Task 5b: fix round 2/5 dispatched - NoSuchSite (137 cells, largest unjudged population) has no cell-level ratchet; needs one, broken down by cause.
Task 5b: fix round 2/5 (1 addressed, 0 open; commits a59037d..4fff594)
Task 5b: complete (commits 7408be0..4fff594, review clean). Four unjudged populations, four cell-level shrink-only ratchets: PosesNoQuestion 44 + NotCompilable 107 + UnhonouredButLoud 14 + NoSuchSite 137 = 302 of 861 cells. BuildAt's new throw proven unreachable two ways (logical + empirical sweep over every single-flag AttributeTargets x endpoint).
  NEW FINDING G5 (reported, NOT ratified): 21 cells are a TEMPLATE limitation, not a structural absence - [MapTo]@Struct (14) and [DwarfMapperConstructor]@Constructor (7) are legal per AttributeUsage, are claimed, and are wholly unmeasured because the endpoint templates declare no struct and no annotatable constructor.
  DEFERRED MINOR (5b): the NoSuchSite ratchet gates the TOTAL only; per-cause counts live in the failure message, so offsetting drift (+N one cause, -N another) passes silently. Hardening opportunity for the final review to triage.

=== PAUSED BY MAINTAINER 2026-08-13 - resume at Task 5c ===
NEXT: Task 5c (task-brief 5c does not exist; Task 5c is tracked only in the session task list + this ledger).
  5c scope: consolidate the MEASURED divergence list (18 original, revised by 5a/5b: D11 CONFIRMED LIVE not withdrawn; D9/D10/D12/D14 evidence re-derived, findings hold; plus N3, N4, G5) into DeclaredDivergences with Issues/round20 links, each asserting the divergence STILL EXISTS so a fix turns the build red. Then remove continue-on-error:true from the surface-matrix CI job.
  Then Tasks 6,7 (sequential) and 8,9,10 (independent; 10 is the real RegisterUpdate defect and can go first).

Task 5c: complete (commits 7e9881e, aa44a13; parent 4fff594). MATRIX GREEN: 865 tests, 0 failures (was 174 red).
  OptionGaps -> DeclaredDivergences, KnownSilent -> Reasons (both old entries' prose VERBATIM). 23 findings
  over 162 cells, one entry per DEFECT not per cell, keyed on all five of (element, arity, axis, site,
  endpoint) so an entry cannot excuse a case nobody measured. Entries derived from a MEASURED dump of the 174
  red cells, not from the findings table - which was stale in seven places.
  RATCHET-NOT-ALLOWLIST proven by mutation both ways: pointing D16 at UpdateInto (where [AfterMap] is
  Honoured) turns Every_declared_divergence_is_still_a_divergence red; deleting D16 turns the parity theory
  red on its own cell. Three ceilings: findings 23, declared cells 162 (catches an entry WIDENED to absorb a
  regression), structural 12.
  THREE NEW FINDINGS the first measurement could not see: D20 (20 cells - the co-located host reads NO
  member-level [MapProperty]/[MapIgnore], though [GenerateMap<S,T>] sits on the annotated type and the
  element's own [DwarfSurfaceSite] claims it), D19 (=5b's N3), D21 (5 cells - the registry-form one-argument
  [MapProperty] accepted at a method site; 5b called it undecidable, but refusal is right whichever way the
  generator reads it, and the class model has no arity check - D5's root cause, D4's mirror).
  JUDGEMENT CALL, flagged: 12 cells (GenerateExtensions + RegisterCollectionShapes at the four non-create
  endpoints, incl. the [DwarfMapperDefaults] twin) are NOT divergences. The findings doc filed them as D17
  while its own S2 called them structural; StructurallyInapplicable has held shape-based reasons for them
  since before this matrix. The surface matrix now reads that dictionary for option-bag axes, counted at 12
  and shrink-only because a structural row - unlike a divergence row - does NOT fail when the shape changes.
  R3 in concrete form: AppliesTo is per ELEMENT and [DwarfMapper] carries 19 options, so no flag value can
  say this; Task 7's "StructurallyInapplicable superseded by AppliesTo" cannot hold for option bags.
  N4 deliberately NOT in the store (uncompilable OUTPUT, not a silence). Recommended home: generator issue +
  NegativeCases pinning test; already pinned meanwhile by the NotCompilable ratchet, which prints CS ids.
  BRIEF DEFECT: the brief said src/DwarfMapper.DocTooling renders these into a generated matrix. It does not
  - the renderer is GeneratedDocsAreCurrentTests.RenderOptionMatrix, and it reads only
  StructurallyInapplicable, so no generated doc changed and nothing needed regenerating.
  CI: continue-on-error removed from the surface-matrix job. Full suite green except the three known
  .git-is-a-file GeneratedDocsAreCurrentTests failures (confirmed by stack trace: get_RepoRoot, before any
  text comparison).
  Option matrix unaffected: 97/0 on 5b's stable OptionEndpointParityTests|OptionContractTests filter.
  Report: .superpowers/sdd/2026-08-13-surface-coverage-architecture/task-5c-report.md
Task 5c: complete (commits 4fff594..aa44a13, review clean). MATRIX FULLY GREEN 865/865.
  OptionGaps -> DeclaredDivergences, KnownSilent -> Reasons (rename MOVED here from Task 7; Task 7 now only owns enum domains).
  23 divergence entries covering 162 cells + 12 cells via StructurallyInapplicable. Reviewer independently adjudicated all 12 against generator source (AggregateEmitter.IsEligible hard-excludes those endpoints) - genuinely structural, NOT a downgrade.
  Ratchet verified GENUINE not allowlist: Every_declared_divergence_is_still_a_divergence re-runs SurfaceProbe.Classify LIVE per declared cell (SurfaceParityTests.cs:457), no cached verdict. Confirmed by mutation both directions.
  Three ceilings, all computed at measured value: findings 23, declared cells 162, structural 12. The cells-ceiling exists specifically to catch an entry being WIDENED later.
  3 findings the first measurement could not see: D19 (=N3), D20 (co-located host reads no member-level MapProperty/MapIgnore though its own [DwarfSurfaceSite] claims it), D21.
  N4 (CS1912 - generator emits UNCOMPILABLE code on duplicate [FlattenGraph]) deliberately NOT in the store; pinned by the NotCompilable ratchet which prints CS ids. Top-severity item in the findings doc. Needs a generator issue + NegativeCases pin.
  DEFERRED MINOR (5c): nothing forbids a cell being both in Reasons and StructurallyInapplicable (double-count vs two ratchets). Currently disjoint; theoretical.

NOTE FOR TASK 6: its brief says to DELETE OptionGaps.StructurallyInapplicable. That is now WRONG - 5c uses it for 12 option-bag cells that AppliesTo structurally cannot express (per-element flag vs 19 options on [DwarfMapper]). It STAYS.
Task 6: implemented (79d6e02). Step 1 produced 7 failures, not round 19's 13 - nine of the thirteen had acquired REAL presence during tasks 1-5c. 8 corpus rows written. Matrix still 865/865 green.
  New internal [DwarfSurfaceOption(option, category, because)] for PROPERTY-level category redirects (5a/5b pattern; fallback is the ELEMENT's category so it cannot name nothing). Reviewer confirmed: genuine mechanism, not Exempt renamed.
  MaxDepth's excuse was STALE, not redirected - AotSample/Program.cs:665 declares MaxDepth=8 against a 10-deep chain, so it demonstrates the CONFIGURED value not the default 64 guard. Independently verified by the reviewer.
  Brief defects found by running: SIX. Most important - the brief's CrossAssembly obligation scanned ONE csproj, which [UsesMap] could satisfy while proving nothing cross-assembly. Also: the '// MAPS:/OPT-IN:/REFUSES:' corpus convention the brief mandated DOES NOT EXIST in this repo (zero grep hits) - I had propagated it unverified from the round-19 RFC.
Task 6: review found 1 Important (the two [UsesMap] corpus rows are compiled usages with NO observing assertion - the exact failure mode this task deletes, and they are what discharges the CrossAssembly obligation) + 2 Minors (bare Contains in TestingOnly; option-level switch _ arm absorbs a new category). Fix round 1/5 dispatched.
Task 6: fix round 1/5 (3 addressed, 0 open; commits 79d6e02..493e052)
Task 6: complete (commits aa44a13..493e052, review clean). SIX ALLOWLIST DICTIONARIES DELETED, every excuse redirected to a firing obligation. Matrix still 865/865.
  The Important finding was UNDERSTATED and the implementer proved it: the assembly-level [UsesMap] row named (Customer, CustomerDto), which AUTO-DETECTION already supplies from a direct Map<CustomerDto> call site - so an assertion over it would have PASSED with the attribute deleted. Re-pointed at (Part, PartDto), the element pair behind Map<ICollection<PartDto>>, which auto-detection cannot record because the call site names the collection shape. Verified un-auto-detectable by the reviewer.
  DEFERRED MINOR (6): CorpusFor's throwing default arm is a correct fail-fast guard for a 7th category but NO test reaches it (enum has exactly 6 members, all explicit arms). Report's claim that it is 'reached by a test' is inaccurate. Cheap fix: Assert.Throws with an out-of-range enum cast. For final-review triage.
Task 7: complete (commits 493e052..c252e07, review clean). Scope was Steps 1-4 only; Step 5's rename was already done by 5c.
  HONEST RESULT: zero previously-unprobed enum members. All 8 option enums have exactly TWO members, so FirstOrDefault was complete BY ACCIDENT. The defect was real but LATENT; fixed as a guard.
  Non-vacuity proved by two reverted mutations: with .Take(1) restored the COMPLETENESS test still passes - only the new derivation test (synthetic 3-member enum, asserts ValueDomain directly) catches it. That derivation test is the load-bearing guard; reviewer confirmed it strong.
  OPERATIONAL NOTE: because RepoRoot needs a .git DIRECTORY, The_option_support_matrix_matches_the_generators_actual_behaviour never runs in a worktree - so any generated-doc change authored from a worktree is silently unverified LOCALLY (CI still catches it).
Task 10: STOPPED for a ruling, then ruled. MEASURED (not read) current behaviour of RegisterUpdate on a duplicate: does NOT throw, first-wins (brief correct), but it MARKS INTO THE CREATE TABLE'S Ambiguous SET - there is only one such field and RegisterUpdate writes to it.
  => a pair with ZERO create maps reports IsAmbiguous==true while IsProvided==false. Cross-table contamination; a worse defect than the brief's 'first-wins unmarked'.
  RULING: option B (additive + CORRECTIVE). IsAmbiguous answering a create-table question with update-table data is not a debatable semantic. Blast radius measured zero in-repo (only reader is the public accessor; AmbientValidator.cs:175 uses IsProvided only; no test asserts IsAmbiguous after RegisterUpdate). Package is 1.0.0-rc.1 so the correction is cheap now. Requires a regression test failing against pre-fix code + a CHANGELOG entry.
  ALSO ruled in: ResetForTests clears Maps/Ambiguous/InterfaceMaps but NOT UpdateMaps - update registrations leak across tests through a shared static.
  ARCHITECTURE GAP CONFIRMED (by design, not oversight): public static methods on DwarfMapperRegistry are a FOURTH taxonomy the Task 6 obligations cannot see - IsProvided, TryGet, Map, Update all share it. That taxonomy is exactly what Task 9's mutation testing exists for.
  Brief defects: torture tests are in IntegrationTests not Generator.Tests (xUnit collections are per-assembly, so the two placement instructions were mutually exclusive); no Mint<T> (real idiom Src<T>/Dst<T>/FreshType); FIVE facts not four; two named invariants have NO update-side twin (Update resolves by declared types only, deliberately; no Provided/TryGet for the update table).
Task 10: complete (commits c252e07..ae21f06, review Approved). REAL RUNTIME BUG FIXED: RegisterUpdate no longer marks the CREATE table's Ambiguous set. Regression guard pins the exact triple (IsUpdateAmbiguous true AND IsProvided false AND IsAmbiguous false) so a differently-shaped bug cannot satisfy it. CHANGELOG entry added. ResetForTests now clears UpdateMaps + UpdateAmbiguous.
  Torture detection power MEASURED not assumed: first observer caught a check-then-act mutant only 2/60 rounds (it polled inside try/catch and spent the contested window throwing). Gating on IsUpdateProvided + 240 rounds -> 23/54/58 per 240. Detections ACCUMULATE and the assert is ==0, so one detecting round fails the run: P(false green) ~ 1e-11.
DEFERRED MINORS (Task 10) - FOR FINAL-REVIEW TRIAGE, two are consumer-facing:
  (a) CHANGELOG: new PUBLIC member IsUpdateAmbiguous appears only inside the Fixed prose, not under '### Added' - a consumer scanning Added for new surface will miss it.
  (b) CHANGELOG: has a consumer-facing 'Fixed' entry for ResetForTests, an INTERNAL member no consumer can reach.
  (c) RegistryUpdateContractTests.cs:15-17 class doc says 'three fail at compile time'; against genuine pre-fix code all FOUR would. On-theme drift.
  (d) RegistryConcurrencyTortureTests.cs:313 - 240 rounds justified on detection power alone; wall-clock cost unmeasured, in a DisableParallelization collection.
  (e) OPEN QUESTION FOR MAINTAINER: ResetForTests is internal, IVT to Generator.Tests only, and has ZERO callers repo-wide (only comments saying it is unreachable). Extending it was ruled; DELETING it is arguably the honest fix.
Task 8: complete (commits ae21f06..20f0196, review Approved). DWARF086 added; WHOLE SOLUTION builds 0 warnings 0 errors incl. all 4 samples/ and the 4-assembly ConsumerTests provider/root graph.
  The brief was wrong TWICE and mutation testing proved both: (1) its suggested discriminator ('generator output has no on-disk path') is BACKWARDS - both harnesses parse the USER's source with path-less ParseText, so it would mark hand-written source as generated. Real discriminator is the .g.cs suffix via IsGeneratorAuthored, sound because AddSource has exactly ONE call site and all 9 hint names end .g.cs. (2) the brief's negative control is GENUINELY VACUOUS - a generator never sees its own emissions (CompilationProvider is pre-generation), proven by forcing IsGeneratorAuthored to return false: the shipped control fails, the brief's stays green.
  REG-05 first run went RED naming DWARF058, NOT DWARF086 - a real PRE-EXISTING hole (DWARF058 is declared incidentally by DWARF081's case; its wording was never pinned). Fixed.
DEFERRED MINORS (Task 8): (a) the *.g.cs collision exemption is unmentioned in IsGeneratorAuthored's remarks; (b) DiagnosticCoverageRatchetTests.cs:684 claims the property 'holds by construction' but adding to PredatesThisProject is a hatch no test blocks.
  MAINTAINER ITEM: CLAUDE.md's working note is stale AND describes a superseded mechanism (says 5 hand-written fences in docs/diagnostics.md; there were already 13 before this task, and exemptions are now inline fence-exempt markers with required reasons). Per that file's OWN rule it should be DELETED, not corrected.

=== PAUSED BY MAINTAINER (limits) - 2026-08-13, at Task 9 ===
Task 9 dispatch was REJECTED by the maintainer before it ran. Task 9 is NOT started.
  Prerequisite check done: dotnet-stryker is NOT installed (dotnet tool list -g confirms). The repo's own stryker-config comments name 'dotnet tool install -g dotnet-stryker' as the prerequisite.
  Wall-clock is the unaddressed risk in Task 9's brief: Stryker re-runs tests per mutant and the brief lists DwarfMapper.Generator.Tests (~7400 tests incl. the ~865-cell matrix). Use --coverage-analysis perTest and pick test-projects by which ones actually cover the five runtime files (IntegrationTests holds the registry torture + contract tests).

=== NEW GOAL SET BY MAINTAINER ===
After the current list, work Issues/round20/GeneratorIssue.txt - i.e. the 'fix separately' work deferred by the 5c ruling. Tasks R20-1..R20-8 created in the session task list.
  AUTHORITATIVE SOURCE is DeclaredDivergences.Reasons (23 entries) + Issues/round20/SURFACE-MATRIX-FINDINGS.md in THIS worktree. GeneratorIssue.txt on master is a STALE mid-work snapshot: it says 18 divergences and recommends withdrawing D11, both superseded (D11 was measured LIVE; 5a/5b/5c refined the list to 23 and added D19/D20/D21/N4).
  KEY PROPERTY when fixing: each DeclaredDivergences entry asserts the divergence STILL EXISTS. Fixing the generator turns the build RED until that entry is deleted and the three ceilings (23 findings / 162 cells / 12 structural) are lowered. That is by design - it is the ratchet, not a failure.
  Task 9 PARTIAL WORK COMMITTED as WIP (see git log): stryker-config.runtime.json + housekeeping wiring. break=0 deliberately - NEVER guess it from the sibling configs.
  FINDING from that partial work, worth acting on independently: Stryker 4.16 REJECTS unknown keys INSIDE the 'stryker-config' object. Both EXISTING configs (stryker-config.json, stryker-config.doctooling.json) keep 'comment' inside it, so THEY CANNOT RUN AT ALL. The repo's mutation testing has been non-functional.

=== R20 PREP (recorded while Task 9 runs) ===
ImplementationRecord.txt does NOT exist in Issues/round20 - it exists ONLY in Issues/round18. Round 20 has GeneratorIssue.txt (master) and SURFACE-MATRIX-FINDINGS.md (this branch). The maintainer's goal wording most likely meant either (a) the round-20 issue record by round-18's filename, or (b) that round 20 should GET an ImplementationRecord.txt following round 18's convention (Decisions.md / ForensicAnalysis.md / ImplementationRecord.txt / ShapeInventory.md). ASK before assuming.
AUTHORITATIVE divergence keys confirmed = 23: D1..D21 + MaxDepth + NullCollections (the two carried over from the old OptionGaps).
COVERAGE GAP in the drafted R20-1..R20-8 list: D1, D2 and D6 are named in NO task, and the two carried-over entries (MaxDepth silent at Span/AsyncStream; NullCollections silent at Projection) are also unassigned. NullCollections is explicitly a DESIGN DECISION with three candidate resolutions recorded and none chosen - it is a maintainer call, not an implementer's. Fold D1/D2/D6 into R20-6 and raise NullCollections/MaxDepth separately.

Task 9: implemented (commits a9616d2 wip, 39abe7b sibling fixes, d2ff7ce runtime config + measured threshold).
  BRIEF DEFECT: Stryker did NOT need installing - already present at 4.16.0. My dotnet-tool-list check was wrong.
  DEFECT 1 (real, repo-wide): 'comment' is not an allowed key INSIDE the stryker-config object. Both sibling configs kept it there, so 'dotnet stryker --config-file stryker-config.json' FAILS OUTRIGHT. The repo's mutation testing has NEVER RUN. Fixed in both.
  DEFECT 2 (worse, and the same silent-green shape this whole branch exists to delete): 'mutate' globs resolve against the PROJECT directory, not the repo root. The runtime config as first committed mutated NOTHING - 185 mutants 'Removed by mutate filter', no score, and EXIT CODE 0. housekeeping.ps1's $LASTEXITCODE check cannot detect a vacuous run. Fixed to **/Name.cs in all three configs.
  MEASURED: 66.95% -> break: 66 (floored). 111 tested: Killed 68, Timeout 11, Survived 32, NoCoverage 7, CompileError 8. Wall-clock 44:02 (plus 4:06 for the vacuous first run).
  39 surviving mutants. KILL FIRST: DwarfMapperRegistry.cs:291 Key.Equals's && (registry key IDENTITY - nothing proves (A,B) and (A,C) are different maps); DwarfMapperRegistry.cs:76 the return; that keeps a duplicate Register out of InterfaceMaps (stated invariant, ZERO tests); DwarfMapExceptions.cs:86 Count: > 1.
  DISCLOSED COST of excluding Generator.Tests: DwarfMapValidationException is unreachable by this run (thrown only by generated code, executed only in AutoValidateRuntimeTests).
Task 9: fix round 1/5 (commits d2ff7ce..9f75d8e). Review found the kill-first ranking WRONG AT THE TOP and self-contradictory.
  DwarfMapperRegistry.cs:291 (Key.Equals's &&) is EQUIVALENT IN PRACTICE, not the #1 hole: Key is a private readonly struct whose only caller is ConcurrentDictionary, which tests hashcode == n._hashcode BEFORE consulting Equals, so (A,B)/(A,C) never reach it. It survived 5,592 tests incl. a mutation-calibrated torture suite.
  THE SHARP PART: the report proposed a test to kill :291 that WOULD HAVE PASSED WITHOUT KILLING IT and been booked as mutation coverage - the false-credit failure this whole branch exists to delete, occurring INSIDE the tool meant to detect it. Implementer agreed; the decisive evidence was already in their own results, misread.
  CORRECTED kill-first: (1) DwarfMapperRegistry.cs:76 the return; keeping a duplicate Register out of InterfaceMaps - drop it and Map's candidates.Count == 1 check at line 163 fails and throws ambiguous; stated invariant, ZERO tests. (2) DwarfMappingDepthException.cs:32 ctor block removal silently zeroes MaxDepth/ActualDepth. (3) DwarfMapExceptions.cs:95 LINQ-iterator discriminator pinned true, never false. :86 restated as a PUBLIC-CTOR contract hole; :291 removed from the ranking.
  Non-vacuous guard Assert-MutantsWereTested added to housekeeping.ps1, one call per leg, + 'json' reporter added to both siblings (neither declared it). Verified by dot-sourcing: real report -> 118 scoreable (exactly the 66.95% denominator), vacuous -> 0 -> throws, missing -> throws.
  The guard caught a genuine bug pre-commit: a bare $Leg: parses as a scope-qualified variable, so housekeeping.ps1 would not LOAD AT ALL. Braced and re-verified at 0 parse errors.
  Corrected: the DwarfMapValidationException exclusion costs ZERO, not 'a real gap' - that type's ctors are bare : base(...) forwarders producing no mutants.
Task 9: complete (commits 20f0196..9f75d8e, review ADDRESSED). Non-vacuous guard verified: reads the JSON status field, excludes CompileError/Ignored, matches Stryker's own 118 denominator, fails closed on missing OR zero-scoreable, runs only after a leg that exited 0. Mutant set unchanged so the 66.95%/break:66 remains valid.
  Newly-promoted survivors spot-checked SOUND: DwarfMappingDepthException MaxDepth/ActualDepth have ZERO test references repo-wide (every MaxDepth hit is the ATTRIBUTE property; nothing reads ex.MaxDepth/ex.ActualDepth).
  DEFERRED COSMETIC (9): the two config deviations are disclosed in Addendum A5 rather than by amending report section 8; self-acknowledged.

=== ROUND-19 REGRESSION PLAN COMPLETE: Tasks 1-10 incl. 5a/5b/5c, all reviewed ===
