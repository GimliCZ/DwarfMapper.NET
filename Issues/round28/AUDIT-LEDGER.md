# Full-scale DwarfMapper audit — ledger (session 6ab6d3e9, 2026-09-01)

Request: "Run benchmarks again. And audit dwarfmapper at full scale."
Constraints: no subagents (not requested); no commits to master; no pushes; no secrets.

## Phases
- [x] P0 benchmarks: full 55-benchmark suite DONE (scratchpad/bench-full) — all 20 allocation pins exact, table in the final report
- [x] P1 static audit of uncommitted diff — F6–F11 (all fixed)
- [x] P2 static audit: BlittableProof, blit emission, registry, ConstructorSelector — F12–F16 (F12/F13/F15 fixed, F14 reported, F16 doc fixed + design reported); ConstructorSelector clean (Policy 5's AllParametersHaveASource mirrors ResolveConstructorArguments' binding exactly: same MemberFacts.Readable, same explicit-map/dotted-path/optional rules; [MapValue] cannot feed a ctor parameter on either side)
- [x] P3 instrument integrity — housekeeping gates read real artefacts; fuzz oracle is compile-level by design; locationless scan misses DiagnosticInfo(null) paths (F17 instrument hole, NOTE)
- [x] P4 corpus-hole hunt — 12 shapes probed at diagnostic + runtime level; F17 (nested-pair diagnostics had no location, FIXED); no silent-wrong-value shape
- [x] P5 compute: whole solution build (Debug+Release), full suite fast tier, DWARF_DEEP=1, coverage — F19/F20/F21; `housekeeping.ps1 -Deep -Coverage -ILVerify` (the -Nightly set minus -BenchSmoke; the full 55-benchmark suite with all 20 pins exact already ran as P0) then ran END TO END for the first time since 2026-08-26: HOUSEKEEPING PASSED, EXIT=0 at 00:39 (deep suite 15264 Generator.Tests green, coverage gate at the 96.4 floor, exhaustion 766 tests 10 m 13 s, AOT publish + execute all checks passed, ILVerify 3 known-unverifiable matched)
- [x] P6 compute: 20x-scaled deep tier (DeepTier.cs catalog x20, subset order capped at 4; temporary patch, reverted clean) — ALL GREEN: Testing.Tests 80, IntegrationTests 1763 (1 m 52 s), CompilerTests 52 (15 m 54 s), Generator.Tests 130,784 (16 m 9 s); no failure, no hang; scratchpad/p6-scaled.log
- [x] P7 compute: generator mutation leg — RE-RUN 07:23–08:14 (51 min wall-clock, 41 min of mutant testing ≈ 9.5/min): 330 killed / 48 survived / 13 uncovered / 0 timeouts of 391 scoreable = 84.40 %, band ok, break/low stay 84; population 258 → 391 from this audit's BlittableProof/LocationInfo fixes; 43 BlittableProof survivors = the next kill program (recorded in the config comment). The FIRST attempt (06:33) was stopped by hand at 30 min of testing on a MISREAD — Stryker's dots reporter buffers when redirected, so the file showed 84/378 near the end of the run; the 90-min cap was adequate. Finding out of it: `housekeeping -Mutation` runs this leg with TimeoutMinutes 60 against a measured 51 min — tight, F3's shape one leg over; reported, not changed
- [x] P8 packaging: nupkg contents, analyzer placement, deps, README/license/symbols — F4 FIXED (ceiling re-measured 280 KB), F22 (analyzer PDBs ship nowhere), F23 (attestation/SBOM scope); observations
- [x] P9 IDE-grade inspection — jb inspectcode not installed; Release build under AnalysisMode All + Meziantou is clean; notes recorded (no defect)
- [~] P11 Rider inspection list (user, 06:15) — 199 of 214 listed sites resolved in the scratch copy (patch verified: whole-solution Release build clean, fast suite green); 15 left on purpose + ~330 nullability guesses + ~40 reflection-consumed fixture members reported, not changed (see P11); apply to the tree after P7
- [x] P12 consumer-reported codegen defects (teammate session) — F24/F25/F26 FIXED with failing-first tests, in the scratch copy; sweep (projection, dictionary values, [MapTo] registry) added; applied to the tree
- [x] P13 closing run on the FINAL tree 08:17–08:5x: `housekeeping -Deep -Coverage -ILVerify -BenchSmoke` with exhaustion + AOT ON — HOUSEKEEPING PASSED: deep suite green (Generator.Tests 15281), coverage band all inside (Testing 96.4/96.4), exhaustion 766 tests green, AOT execute all checks, ILVerify 3 known, BenchSmoke 55/55 with all 20 allocation pins exact and blit ratio 2.69×/1.87× (floor 1.50×); scratchpad/closing.log
- [x] P10 docs/diagnostics sync, CHANGELOG, security posture — F18 fixed; SEC-01…SEC-07 are mechanised by ClaimMechanismScanTests (job-name binding only, see F18); nothing further

## Findings (F-n), severity: BLOCKER / HIGH / MEDIUM / LOW / NOTE

### F1 HIGH — the nightly CI tier has been fully red every night since 2026-08-23 (10 consecutive nights); the repo's own doc names only one of the five failing jobs
Evidence: GitHub API, runs 33488068348 (09-01), 33379107299 (08-31), 33303507682 (08-30). Failing jobs each night:
deep-test (housekeeping -Nightly, 4-7 min), package-size, mutation(generator), mutation(pipeline), preview-sdk-canary (continue-on-error).
Issues/round27/PLAN-mutation-follow-on.md says only "mutation (generator) has failed ... plausible candidate for a genuine floor breach".

### F2 HIGH — the mutation(generator) and mutation(pipeline) CI failures are NOT a score breach: they fail at step 9 "Assert no mutated product binary survived the leg"
ci.yml:292 dot-sources Assert-NoMutatedProductBinaries WITHOUT the Remove-PlantedMutants call that housekeeping.ps1:542/611 runs first.
gate-checks.ps1:173-190 documents that the residue is EXPECTED for analyzer-only references (no CopyLocal => no backup => Stryker cannot restore) and says
"CI never noticed because each nightly leg runs in a throwaway container" — false: CI runs the assert in the same container and it fires every night.
The Stryker step (6) itself PASSED on 08-30 and 08-31 for both legs => the scores were above `break`. The repo's hypothesis of a floor breach is wrong.
Fix: add `Remove-PlantedMutants -Leg ... -Root ...` before the assert in ci.yml (mirror housekeeping), or make the assert itself perform the remove.

### F3 MEDIUM — mutation(pipeline) wall-time: 66.8 min (08-31) -> 97.9 min (08-30) -> cancelled at the 350-min timeout (09-01). Same commit (master unchanged since 08-27).
A 5x swing on identical inputs points to a hanging mutant (infinite loop in a mutated phase) rather than load; Stryker's per-test timeout should bound this — check `timeout-ms`/`additional-timeout` in stryker-config.pipeline.json and whether xunit blame-hang is on.

### F4 MEDIUM — FIXED (P8) — package-size gate: DwarfMapper.nupkg is 277-280 KB against a ceiling of 247 KB measured 2026-08-22 at 81c4ace. Even rc1 (built at master 08-27) is 277 KB. Re-measured on this tree 2026-09-02: 287,189 B → 280 KB; after F24–F26 and the sweep 288,884 B (Windows) = 282 KB / 288,676 B (ubuntu container, the gate's own) = 281 KB — the platforms straddle the boundary, ceiling set to the larger measurement, 282; Testing 48,7xx B → 47 KB (unchanged).
Rounds 24-27 grew the package ~30 KB (12%) without the "re-measure in the same commit" move the gate demands. The gate did its job; nobody read the nightly.
Package contents (rc5): lib/net10.0/DwarfMapper.dll 42,496 B; DwarfMapper.xml 168,571 B; analyzers/dotnet/cs/DwarfMapper.Generator.dll 576,512 B; DwarfMapper.CodeFixes.dll 24,576 B; README.md 74,062 B. No Roslyn deps shipped (correct).

### F5 NOTE — preview-sdk-canary (11.0 preview) fails at "Build the whole solution on the preview SDK" in 0.3 min. continue-on-error so it doesn't gate; but it is the canary firing, and nobody has read what it says. Cannot fetch logs unauthenticated.

## P1 — adversarial review of my own uncommitted diff (findings F6–F11)

### F6 LOW (own diff) — `LocationInfo.From`: unreachable `catch (ArgumentOutOfRangeException)`
- Guard `SourceSpan.End > SourceTree.Length → return null` precedes the try. TextSpan invariants (Start ≥ 0,
  End ≥ Start) plus that guard mean `GetLineSpan()` cannot throw AOORE → the catch is dead → ~2 equivalent
  mutants (catch-body / catch-removal) on the generator leg whose margin is ~2 mutants of 258.
- Fix: remove the try/catch, keep the guard, state the proof in the comment.
- Correction (later in P1): the mutation-margin argument is secondary. The defect is that the catch is dead
  code whose comment claims it catches a "residue" of spans past the tree's end — a residue the guard two
  lines above already excludes, so the comment asserts a hazard that cannot occur. Removed for that reason.

### F7 LOW (own diff) — `AmbientValidator.AmbiguousProviders` has two consecutive `<summary>` blocks
- Lines ~193–199: the pre-change summary was left above the new one. Delete the stale one.

### F8 MEDIUM (own diff) — `MapperExtractor.Phases.cs`: helpers inserted between a doc comment and its method
- `ClassIgnoredSources` / `IgnoredSourcesFor` were inserted AFTER `ProcessDeclaredMethod`'s 30-line
  `<summary>/<remarks>` block and BEFORE the method. Result: `ProcessDeclaredMethod` has no doc comment;
  `ClassIgnoredSources` carries three `<summary>` blocks and `<paramref>`s naming parameters it does not have;
  `IgnoredSourcesFor` has none. Build did not catch it: DwarfMapper.Generator does not set
  GenerateDocumentationFile, so CS1734/CS1573 never fire there (only src/DwarfMapper does).
- Fix: move both helpers above `ProcessDeclaredMethod`'s doc comment, each with its own summary.

### F9 LOW (own diff) — UTF-8 BOM added to 5 files against `.editorconfig` `charset = utf-8`
- BOM added by me: MapperExtractor.Members.Phases.cs, MapperExtractor.Members.cs, MapperExtractor.Phases.cs,
  MemberResolutionContext.cs, tests/DwarfMapper.NegativeCases/DiagnosticCoverageRatchetTests.cs.
- Pre-existing at HEAD (NOTE, not mine): 11 further .cs files carry a BOM (DiagnosticDescriptors.cs,
  EnumConverter.cs, EnumStringSource.cs, 5 Generator.Tests files, 2 IntegrationTests files, 1 NegativeCases
  case). Nothing enforces the charset line.
- Fix: strip the 5 BOMs I added. (Repo-wide sweep is a separate, trivial housekeeping change — report only.)

### F10 MEDIUM (own diff, incomplete fix) — DWARF064 remedy check compares the TARGET spelling, Ordinal
- The shadow is detected with the pair's matching key (OrdinalIgnoreCase under CaseInsensitive, `NormalizeName`
  under NameConvention.Flexible) but the disown check is `IgnoredSourceMembers.Contains(targetName)` Ordinal.
  Under either mode, when the source spelling differs (`name` vs `Name`, `first_name` vs `FirstName`), writing
  `[MapIgnoreSource("<real source name>")]` — what source-coverage (`ReportUnconsumed`, Ordinal on real names)
  requires and the natural thing to write — does NOT silence DWARF064; only the target spelling does, and that
  spelling disowns nothing for source coverage. The message compounds it: both `{0}` slots are the target name.
- Fix: the predicate becomes "which source member would auto-match and is not disowned" (returns the real
  source name or null); the message names that real name in the remedy (`{1}`).
- Applied: `Diagnostics/DiagnosticInfo.cs` gained `MessageArg2` (a second message slot; every other site passes
  null) so the DWARF064 site in `MapperExtractor.Members.cs` can carry the real source name without changing
  the record's construction elsewhere. FIXED with F11, both endpoints, NegativeCases pairs green.

### F11 HIGH (own diff, "fix applied to 1 of N identical sites") — projection endpoint untouched
- `TryValidateMapValueTarget` has two callers; only `ResolveMapValues` (create map) got the [MapIgnoreSource]
  predicate. `MapperExtractor.Projection.cs:551` still passes `sources.ContainsKey` → on a projection method
  the remedy DWARF064 names still does nothing. The remedy NegativeCase covers only the create-map endpoint.
- Fix: thread `ignoredSourceMembers` into `ResolveProjectionMembers`, pass `IgnoredSourcesFor(decls, method)`
  at its one call site, use the same predicate; add a projection remedy case (EXPECT none) and a
  CaseInsensitive remedy case with the real-source-name spelling.

## P2 — safety-critical generator paths (findings F12–F15)

Scope read: `Pipeline/BlittableProof.cs` (whole), `CollectionConverter.cs` blit emission (~1032, 1144–1159),
`Issues/round26/AUDIT-unsafe-reachability.md` (the previous adversarial pass over the same surface), the
DWARF100 near-miss tests and the K0 partial-split corpus row. Method: for each input the runtime layout
depends on, ask whether the proof reads it; then build the counter-example and RUN it (scratchpad/blitpoc,
never to be committed). Context that makes all of this HIGH rather than hardening: commit 92663a9 (round 26)
deleted the emitted `Unsafe.SizeOf` guard, so the proof is the ONLY thing between an accepted pair and
`MemoryMarshal.Cast`. Wider→narrower cast throws "destination is too short"; narrower→wider silently copies
N/k elements and leaves the rest zeroed.

### F12 HIGH — FIXED — the proof re-sorted fields by ordinal file path; accepted a pair whose layouts were reverses
- `InstanceFields` sorted the `GetMembers()` list by (ordinal `FilePath`, `SourceSpan.Start`) — commit
  c30052e / ISSUE-018, added for determinism across partial files, justified by "for a Sequential struct the
  declaration order is also the emitted layout" — which is true of the UNSORTED list and is exactly why
  sorting it is unsound. Roslyn emits fields in `GetMembers()` order (compilation tree order for partials);
  MSBuild's item order on Windows is case-insensitive (`point.cs` < `point.extra.cs`: `c` < `e`), ordinal is
  the reverse (`Point.Extra.cs` < `Point.cs`: `E` 0x45 < `c` 0x63). Compiled [X, Y] (Point.cs first); sorted
  list = [Y, X]; twin declared [Y, X] lines up by name → accepted; real layouts [X, Y] vs [Y, X].
- PoC (scratchpad/blitpoc, executed on .NET 10): `P{X=1,Y=2}` → `Q{X=2,Y=1}` through the emitted
  `MemoryMarshal.Cast`, silently, no diagnostic, no exception.
- Round-26 audit did not probe this shape (its table has no partial row); K0 corpus row
  `PartialFileSplitStructPair` pinned only "verdict is file-order independent", which the sort satisfied
  vacuously by being wrong in both orders the same way.
- Fix: sort deleted; `FieldsSpanPartialDeclarations` refuses any struct with `DeclaringSyntaxReferences > 1`
  whose instance fields (placed through backing-field owners; unplaceable = refuse) sit in more than one
  `TypeDeclarationSyntax` (tree, span) — the CS0282 shape. Applies to `[Reinterpret]` too. DWARF100 reason
  (a/b arms). Single-declaration partials still blit.
- Tests: `BlittableProofCoverageTests` (both compile orders, both directions, `SameBytesIgnoringNames`,
  one-declaration positive control, within-one-file split, backing-field placement, primary-ctor capture),
  `BlitSoundnessTests.Partial_struct_split_across_files_maps_by_name_in_the_order_the_build_used` (E2E: emits,
  maps, values survive, no `__DwarfBlit_` helper), near-miss row; K0 docs updated (the row's verdict is now
  REFUSE in both orders).

### F13 HIGH — FIXED — three layout inputs the proof never read: `StructLayout.Size`, `[InlineArray(n)]`, fixed-buffer length
- `Unsafe.SizeOf` measured (PoC): `struct{int}`=4 vs `[StructLayout(Size=32)] struct{int}`=32 → accepted;
  `struct{int E}`=4 vs `[InlineArray(4)] struct{int E}`=16 → accepted; `fixed int Buf[4]`=16+4 vs
  `Buf[8]`=32+4 → accepted (both fields are `int*` to `SymbolEqualityComparer`; length lives on
  `IFieldSymbol.FixedSize`). Narrow→wide: 3 SrcV (12 B) into 32-B elements = 0 elements copied, every mapped
  element default — silent. Wide→narrow: ArgumentException "destination is too short".
- Fix: `IsSourceSequential` also reads `Size`; `InlineArrayLength`; per-field `SameFixedBuffer`
  (`IsFixedSizeBuffer` and `FixedSize` must agree); all three compared in `LayoutIdentical`, each with a
  DWARF100 reason (`SizeWord`/`InlineArrayWord`/`FixedBufferWord`, invariant culture).
- Tests: three seam theories (each: absent/present, present/absent, differing, equal-positive), three E2E
  tests in `BlitSoundnessTests` (Size and InlineArray execute and read values back; fixed-buffer refusal is
  held at generated-source level because the scalar fallback hits F14; equal-length fixed buffers blit and
  values survive), three near-miss rows, `No_reason_branch_exists_that_no_fixture_reaches` still covers
  every `reason =` site (11 sites, 8 kinds).

### F14 LOW — NOT FIXED (reported with fix) — scalar path emits `Buf = s.Buf` / `P = s.P` for fixed-buffer and pointer members
- Any struct with a fixed buffer or pointer-typed public field that reaches the scalar path (i.e., whenever
  the blit is refused or the member is not an array) makes the GENERATED code fail with CS1666/CS0214 — a
  compiler error inside generated source, no DWARF diagnostic. DWARF005 is the nearest id but its remedy
  (`[MapProperty(Use = …)]`) cannot apply: any pointer-typed expression needs an unsafe context, so only
  `[MapIgnore]`/`[MapIgnoreSource]` works. Loud, not silent — hence LOW.
- Proposed fix: in member pairing, before `TryResolveConversion`, detect `TypeKind.Pointer`/`FunctionPointer`
  member types or `IsFixedSizeBuffer` fields on either side and report a dedicated diagnostic ("Member 'X'
  has a pointer or fixed-buffer type, which generated (safe) code cannot read or assign; mark it
  [MapIgnore]/[MapIgnoreSource] or map the containing type by hand"). Minting = the 5-file sync; the user's
  call, so not done here.

### F15 LOW — FIXED — README, COMPARISON, deploy-and-optimize, gallery sample 22 still advertised the runtime size guard
- `README.md:90,121,652,656`, `docs/COMPARISON.md:331`, `docs/howto/deploy-and-optimize.md:116`,
  `samples/DwarfMapper.Gallery/22_Reinterpret.cs:8` all described a "JIT-folded"/"runtime" size guard that
  92663a9 deleted. Reworded to describe the generation-time proof (and its new inputs). Historical records
  (`docs/superpowers/*`, `Issues/round25`, `benchmarks/results`) left as written.
- Pre-existing doc gap closed alongside: `docs/diagnostics.md` `## dwarf100` listed three reasons; `Pack` was a
  fourth the code reported and the doc never named. Now eight, plus the note that all but the name reason bind
  `[Reinterpret]` too.

### P2 observations (no defect)
- `[Reinterpret]` width-only primitive admission (`byBytesOnly`): `byte`↔`bool`, `char`↔`short`,
  `float`↔`int` are all accepted. `bool` with a non-0/1 payload is an invalid value the runtime tolerates in
  practice; `float`↔`int` is bit-casting the caller explicitly asked for. Consistent with the round-26 ruling
  ("same WIDTH is what the caller is there to assert"). No change.
- Pointer-typed fields compare by symbol equality (`int*`==`int*`), unmanaged, so a pointer inside a blitted
  struct is copied as bytes — correct (a copy of an address is an address).
- `Auto`-layout nested type in both sides short-circuits on identity — correct, as the round-26 audit ruled.

### F16 LOW — doc FIXED, design gap REPORTED — `DwarfMap.Validate()` checks presence, never uniqueness
- `DwarfMapperRegistry.cs` (370 lines, read whole): `RegistryTable.TryRegister` is first-wins and marks
  `_ambiguous`; `TryGet`/`Map` never consult it; the only readers of `IsAmbiguous`/`IsUpdateAmbiguous` are
  tests. `AmbientValidator.EmitValidateMethod` (236–305) emits `IsProvided` per consumed pair only; the
  "Runtime fail-fast fallback" comment in `DwarfGenerator.cs:245` is the fallback for DWARF061 alone —
  DWARF063 (compile-time, Warning, manifests the root can SEE) has no runtime counterpart.
- The scenario the feature exists for makes this matter: `docs/howto/ambient-cross-assembly-maps.md` names
  lazy-loaded plugins the root never references as a supported shape, which is exactly the shape DWARF063
  cannot see. Two such plugins providing one pair → first-wins in load order, `Validate()` green, no signal.
- `Issues/round27/DESIGN-surface-and-security.md` SEC-2 states the trust boundary as "a shadowed duplicate
  resolves silently unless the consumer opts into `[DwarfMapperValidationRoot]`" — only true when the
  duplicate is in the compile-time graph; the root's runtime half does not cover it. Historical record, left.
- Doc fix applied: the howto's "One provider per pair" limit now says the check is manifest-scoped, a
  runtime-only duplicate stays first-wins in load order, `Validate()` checks presence not uniqueness, and
  `IsAmbiguous` is the runtime question to ask after plugins load.
- Design proposal (user's call — it is a behaviour change and stricter than the compile-time Warning):
  `EmitValidateMethod` additionally collects `IsAmbiguous`/`IsUpdateAmbiguous` per consumed pair and either
  (a) throws `DwarfMapValidationException` with a second "provided more than once (first registration wins)"
  section, or (b) exposes them through a `Validate(strict: true)` overload so existing callers keep their
  contract. (b) is consistent with DWARF063 being a Warning; (a) is what "fail-fast" would mean literally.
- Registry itself: clean. Interface path deliberately throws on >1 candidate; `Register` mirrors into
  `InterfaceMaps` only on a successful `TryAdd` (a duplicate cannot double-count into that ambiguity);
  `Update` resolves by declared types; `Key` equality is `Type ==` (identity — correct across ALCs, since two
  loads of one assembly are two types and must not share a map).

## P3/P4 — instrument integrity and the corpus-hole hunt (findings F17+)

Scope read: `scripts/housekeeping.ps1` (stages, `-Deep`/`-Coverage`/`-Nightly`, the three Assert-* gates),
`FeatureCombinationFuzzTests` (16 features x modes x kinds; oracle = compiles clean / expected DWARF038),
`DwarfMapper.DifferentialTests` (`ShapeCatalog`, 14 shapes vs Mapperly + AutoMapper — the only VALUE-level
oracle), `LocationlessDiagnosticsSelfIdentifyTests`, `ParsableConverter`, the option enums' defaults,
`NestedMappingRegistry` / `DrainNestedMappingQueue` / `ConversionRequest.Location`, `DiagnosticInfo`.
Method for P4: a scratch project (scratchpad/p4poc, never committed) with one shape per suspected hole,
compiled against the LOCAL generator; read the diagnostics, then the generated code and the runtime values.

### F17 MEDIUM — FIXED — every diagnostic raised while resolving a synthesized nested pair carried `Location.None`
- `MapperExtractor.Phases.cs` `DrainNestedMappingQueue` (~3183): `LocationInfo? nestedLocation = null;` with a
  comment recording null as the contract (ISSUE-012 found C3's "first declared method's location" code dead
  and pinned the null instead of computing the anchor). `nestedLocation` is the `ConversionRequest.Location`
  every member of the pair resolves under, so DWARF001/005/025/026/038/044/070 … from any nested pair —
  auto-nested or not, at any depth — went out as `DiagnosticInfo(…, Location: null)` → `Location.None`.
- Observed (p4poc, S3): `CSC : error DWARF001: Destination member 'TargetOnly' has no matching source member
  … annotate the method` — no file, no line, the pair unnamed, a remedy naming a method that does not exist
  for a synthesized pair; Rider titles it "Generator 'DwarfGenerator' failed to generate sources". Same
  failure shape as the round-27 DWARF063 incident, one layer down.
- Instrument hole: `LocationlessDiagnosticsSelfIdentifyTests` scans only literal
  `DiagnosticDescriptors.X, Location.None` sites in `DwarfGenerator.cs` (7). A `DiagnosticInfo` whose
  `LocationInfo?` is null is the same outcome by a different route and the scan cannot see it — so the test
  that exists to keep locationless diagnostics self-identifying was green over a whole class of them.
- Fix: `NestedMappingRegistry.GetOrReserve` already received the requester's `location` (its one call site,
  `MapperExtractor.Conversions.Arms.cs:60`, passes `req.Location`) and dropped it. The queue tuple now carries
  it as `Origin`; `Dequeue` hands it back; the drain loop resolves the pair under it. A pair N levels down
  inherits the declared method's anchor transitively (its requester was itself resolved under that anchor).
  Generator builds 0/0.
- Tests: `NestedPairDiagnosticLocationTests` (4): completeness error anchored at the declared method's line;
  two levels down inherits through the pair between; the anchor is the method that REACHED the pair, not the
  first method on the class (C3's original wording); DWARF005 anchored the same way. p4poc re-run:
  `Program.cs(15,61): error DWARF001 …` — file, line, column.
- Remaining NOTE (not fixed): the nested-pair MESSAGE still does not name the pair. Two nested pairs each
  missing an `X` produce two DWARF001s on the same line with identical text. A `(while mapping ChildS ->
  Child)` suffix would need a descriptor message change = the 5-file sync; the user's call.
- Follow-up NOTE for the instrument: `LocationlessDiagnosticsSelfIdentifyTests` should also cover
  `DiagnosticInfo` construction with a null `LocationInfo` — e.g. assert in `DiagnosticInfo.ToDiagnostic` (or a
  scan over `new DiagnosticInfo(` sites) that a null location is only ever passed for the descriptors the
  self-identify list names. Not done here: it is a new test contract, and the drain loop was the only
  null-location producer found in `Pipeline/`.

### P3 observations (no defect)
- `housekeeping.ps1` stages match CI: `-Nightly` = `-Deep -Coverage`; `Assert-MutantsWereTested`,
  `Assert-StrykerConfigSane`, `Assert-BenchAllocationsPinned` all read the artefacts they claim to (JSON,
  config, `allocation-baseline.json`) — none passes by construction.
- Fuzz oracle is compile-level only (clean / expected DWARF038). Value-level coverage lives solely in
  `DwarfMapper.DifferentialTests` (14 shapes). That is a known and documented split, not a hole in either
  instrument; but it means a silent-wrong-VALUE shape outside the 14 is invisible to both — hence P4's probes.
- `ParsableConverter`: string→T parses under `CultureInfo.InvariantCulture` (RoundtripKind for
  DateTime/DateTimeOffset); only bool/char use the culture-free `Parse(v)`. No culture defect.
- Option defaults (`NullStrategy.Throw`, `RequiredMappingStrategy.Target`, `EnumStrategy.ByName`,
  `NameConvention.Exact`, `ImplicitConversions = true`) match what `docs/` states.
- Naming NOTE: `AmbiguousAmbientProviderTests.cs` and `GeneratorResilienceAdversarialTests.cs` (both
  uncommitted, mine) mention "FusedChat" in fixture text; harmless, but it is a name from another project and
  should be replaced with a neutral one before commit.

### P4 probes — loud (no defect)
- Case-insensitive ambiguity → DWARF010; `NameConvention.Flexible` ambiguity → DWARF048; target-only member
  → DWARF001; `long → int` → DWARF038 Warning; `string? → string` → DWARF070 Warning; nullable path hop
  `A.Inner.Name` → DWARF044 Warning; `[MapProperty]` on a class → CS0592 (AttributeUsage); `[MapIgnore]`
  on a class-model DTO member is NOT a directive (documented surface decision; `[MapIgnore<S,T>("M")]` on the
  mapper class is the nested-pair form).
- NOTE (inconsistency, loud either way): `double → float` is REFUSED (DWARF005) while `long → int` is
  ADMITTED with DWARF038. Both are narrowing; the Warning-vs-Error split is by kind (integral vs floating).
  Documented nowhere I could find; a one-line note under DWARF038 would settle it.

### P4 probes — runtime values (p4poc executed on .NET 10; no defect)
| shape | observed | verdict |
|---|---|---|
| S1 direct member `HomeCity` vs flatten path `Home.City` | direct wins | correct: flattening is the fallback |
| S3a update-into, nested `Child` | new instance (`ReferenceEquals` false), `TargetOnly` reset | documented (`dwarf065`, Info, pinned by `UpdateInto_nested_member_reports_DWARF065`); Info is invisible on the CLI — a design choice (item 13) |
| S3b update-into, null source `Child?` / `List<int>?` | `Child = null`; `Nums` = new empty list | documented: `NullCollections = AsEmpty` default (`docs/options.md`, README 557) |
| S6 record positional + init | X=1 Y=2 | correct |
| S9 `new`-hidden member on derived source | `derived` | correct (most-derived wins) |
| S13 enums with disagreeing names/values | ByName → `X` | correct (default `EnumStrategy.ByName`) |
| S15 `[MapProperty("A.Inner.Name")]` through two nullable hops | raw `NullReferenceException` | DWARF044 Warning says exactly "a null interior value throws at runtime"; both emission sites carry the same text; doc entry agrees. Loud. |
| S17 `long.MaxValue → int` | `OverflowException` (checked narrowing) | DWARF038 at build time + checked at runtime. Loud. |
| S18a `int? null → int` under `NullStrategy.Throw` | `InvalidOperationException: Source member 'N' was null` | correct |
| S18b `string? null → string` | `S = s.S!` — null passes through | documented in `NullStrategy` (reference sources raw-assign, DWARF070 is the signal). Loud at build time. |

Conclusion for P4: no silent-wrong-value shape found outside the differential catalogue. Every probe that did
something a reader might not expect has either a diagnostic naming it or a documented option governing it.
The one defect P4 surfaced is F17 (where the diagnostic went, not whether it fired).

## P5 — compute: suite, deep tier, coverage (findings F19–F21)

Scope run: `dotnet build DwarfMapper.NET.sln` (Debug + Release, samples included), the fast tier, DWARF_DEEP=1,
`scripts/housekeeping.ps1 -Coverage` (fast) and `-Deep -Coverage`, and — because both coverage runs threw at
the floor gate — a per-project coverage collection at the floor commit cb14993 in a detached worktree
(scratchpad/cov-cb14993, since removed) to learn whether the floor was ever met.

### F19 HIGH — FIXED — the DwarfMapper.Testing floor (87.1) was never met by any committed tree; every `-Coverage`/`-Nightly` run stops at the gate
- Fast tier, deep tier and the floor's own commit cb14993 all measure DwarfMapper.Testing at 704/809 =
  87.02 %, with byte-identical covered-line sets (scratchpad/testing-lines-now.json vs cov-cb14993). The floor
  comment at `scripts/housekeeping.ps1:102` records `Testing 82.7 -> 87.1 the object-factory graph-shape
  tests`; the tree that commit produced measures 87.0. Nothing in the tree, in either tier, reaches 87.1.
- Consequence: `Test-CoverageWithinBand` (scripts/gate-checks.ps1:36) fails on `measured < floor`, the gate
  throws, and stage 1b aborts the script — exhaustion, AOT publish+execute, ILVerify, BenchSmoke and the
  mutation legs never run behind `-Coverage`. `-Nightly` is `-Deep -Coverage -ILVerify -BenchSmoke`, so CI's
  nightly deep-test job has the same wall in front of everything after coverage (one of F1's ten red nights'
  causes; the others are F2/F3).
- Fix: the floor is beaten, not lowered — F20's sensitivity tests and the RoundTrip/StructuralComparer pins
  raise the measured value (re-measured in this commit, see the housekeeping-cov2 log), and the floor is set
  to that measurement's `Floor(x*10)/10` with the dated reason in the script's comment block, the README
  badge regenerated from the script, and a CHANGELOG bullet. Rule kept: raising is normal, the value is the
  measurement, no cushion.

### F20 HIGH — FIXED — every violation-reporting line of `GraphOracleComparer` was executed by no test; the oracle's consumers assert only `Count == 0`
- `src/DwarfMapper.Testing/GraphOracleComparer.cs`: `TopologyCompare`'s only `violations.Add` (515–518),
  both `FlattenGraphDiff` violations (217–218 count mismatch, 238–239 edge-not-degraded), every
  `CrossTypeCompare` diff (646 null, 675 float→double, 685 numeric, 699 overflow fallback, 712 enum, 720
  scalar, 748–752 count) and `ValueCompare`'s null (381) and count (419–423) arms are uncovered in the fast
  AND deep tiers across all four projects that reference the assembly. `TopologyPreserved` (91) is never
  called; topology through public fields (577–580) and FlattenGraph reach through dictionary `Values`
  (304–319) never execute; the null-first comparator arms (904/909) never execute.
- All ten `TopologyDiff` sites (TopologyOracleFuzzTests), both `FlattenGraphDiff` sites
  (FlattenGraphFuzzTests) and both `CrossTypeDiff` sites (CombinatorialEngineTests) assert
  `Count == 0`. A mutant turning any `violations.Add`/`diffs.Add` into a no-op — or `TopologyCompare`'s
  `ReferenceEquals` check into `true` — survives the whole suite, and no mutation leg covers
  DwarfMapper.Testing (the five legs are generator, doctooling, runtime, codefixes, pipeline). The topology
  oracle's own doc says "a duplicate-shared-node bug … would pass value tests but fail topology tests" and
  no test had ever demonstrated it. Same hazard class as [[test-infra-holes-pattern]]: the instrument that
  grades every graph fuzzer had never been shown to be able to fail.
- Fix: `tests/DwarfMapper.Testing.Tests/GraphOracleSensitivityTests.cs` — 27 negative controls, each paired
  with the positive control one edit away (diamond with duplicated shared node — the value oracle passes it,
  the topology oracle names `root.Right`; cycle the target did not close; field edges; null mismatch left to
  the value oracle; a 70-deep chain whose back-edge above the 64 cap is still found; FlattenGraph count
  mismatch, all three navigation shapes left populated including an EMPTY list, reach through dictionary
  values, null guards; ValueDiff null/count/field/cycle/enum/depth/null-first ordering; CrossType null vs
  empty-only, float→double, int→long, decimal overflow fallback, cross-enum, scalar, count, cycle; the three
  Render helpers' prefixes and null guards). `RoundTripTests` gains the null-mapper guards and a replay test
  (`ex.Seed` rebuilds the failing instance and reproduces `ex.Diffs`); `StructuralComparerTests` gains the
  count path, the cycle termination (no visited set: MaxDepth is the only brake) and `Render(null)`.
  80/80 green.

### F21 LOW — NOT FIXED (reported with fix) — both comparers report NaN≠NaN and ∞≠∞ as mismatches
- `StructuralComparer.ScalarEquals` and `GraphOracleComparer.ScalarEquals` compare `double`/`float` only by
  `Math.Abs(a - b) < eps`; `NaN - NaN` is NaN and `∞ - ∞` is NaN, so a mapping that copies NaN or ±∞
  faithfully is reported as a diff. Latent in-tree: `ObjectFactoryV2` draws 0, ±1, MinValue, MaxValue but
  never NaN/∞, so no fuzzer sees it; user-supplied data through `ValueDiff`/`CrossTypeDiff`/`RoundTrip.Verify`
  does (any DTO with a `double.NaN` sentinel fails round-trip verification with a diff that shows
  `expected NaN, actual NaN`).
- Proposed fix (one line each): `return da.Equals(db) || Math.Abs(da - db) < DoubleEpsilon;` (and the float
  twin) — `Equals` is true for NaN/NaN and ∞/∞, false for ∞/−∞, and the epsilon keeps its role for finite
  values. Not applied here because it changes a public oracle's verdict on inputs the tree never produces and
  the mutation ceiling for the testing assembly is unmeasured; the user's call.

### P5 observations (no defect)
- Coverage is deterministic run-to-run and tier-to-tier (all test randomness is seeded); the deep tier adds
  no DwarfMapper.Testing lines over the fast tier — the multiplied counts re-walk the same paths.
- Dead by construction (no test can reach; noted, not changed): `CrossTypeCompare`'s
  `catch (InvalidCastException)` (688–694; `IsNumeric` restricts both sides to numeric primitives),
  the comparator's `catch` blocks (931–937), `RuntimeId.Equals(object)` (986), `ObjectFactoryV2` fallbacks at
  361/382/403/788 and the interface/abstract-with-no-concrete arm (492).

## P8 — packaging (findings F4 fix, F22, F23)

Scope: the rc5 packs of this tree unpacked into scratchpad/pkg/{main,testing} (nuspec, entry table, deflated
sizes), the shipped-project csproj packaging items, release.yml / ci.yml packaging jobs, docs/RELEASING.md,
and a fresh `dotnet pack` of both shipped projects with the nightly job's exact switches.

Verified correct (no finding): `analyzers/dotnet/cs/` carries Generator + CodeFixes only (no Roslyn, no
System.* shims — the generator's references are all `PrivateAssets`/analyzer-scoped); `lib/net10.0/` holds
`DwarfMapper.dll` + XML docs; the nuspec has an EMPTY net10.0 dependency group for both packages (Testing has
no ProjectReference to DwarfMapper and its DLL references no DwarfMapper assembly, so that is right, not a
missing edge); `requireLicenseAcceptance` true, `GPL-2.0-only` expression, README shipped and shown;
repository metadata carries commit + branch; PublicApiAnalyzers shipped files match the package surface;
`RestoreLockedMode` engages on CI so release.yml's plain restore is locked; Deterministic +
ContinuousIntegrationBuild + EmbedUntrackedSources + SourceLink on.

### F4 — FIXED — the ceiling is re-measured in this diff
- Root cause, not just the symptom: rounds 24–27 added ~60 KB of generator IL and ~15 KB of XML docs and no
  commit re-measured, because the ONLY place the gate runs is the nightly CI `package-size` job.
  `scripts/housekeeping.ps1` never packs; `-Nightly` cannot show this red. Same class as F1/F19: a gate
  nobody local can trip.
- Fix applied: `scripts/gate-checks.ps1` `'DwarfMapper' = 280` (floor(287189/1024)), Testing stays 47
  (floor(48769/1024)), with the dated measurement, the per-entry growth table and the "runs only in
  nightly" note in the ceiling comment block; CHANGELOG bullet under Fixed. The measurement was made
  locally (CI unset); the 2026-08-22 number was made with CI=true. The DLLs are deterministic and the
  CodeView path is not source-mapped, so no byte difference is expected, but the headroom to the first red byte (287,744) is 555 bytes —
  CONFIRMED in the gate's own environment: the same tree packed in a `mcr.microsoft.com/dotnet/sdk:10.0.101`
  container (ubuntu, CI=true, locked restore — ci.yml's exact recipe; scratchpad/linuxpack) measures
  286,985 B → 280 KB and Testing 48,715 B → 47 KB. Every DLL is byte-identical raw on both platforms; the
  204-byte delta is CRLF vs LF in DwarfMapper.xml/nuspec and 10–20 B of deflate variance per DLL. Headroom
  on the gate's platform is 759 B. Observation for the maintainer: all earlier ceilings were measured on
  Windows while the gate runs on ubuntu, and the Windows pack is always the larger, so a Windows measurement
  is the conservative side of the KB boundary — the platform note now sits in the comment block.
- Recommendation (not applied — it adds a ~1 min pack to the script and is the maintainer's call): under
  `-Nightly`, pack both shipped projects to a temp dir and call `Assert-PackageSizeWithinCeiling` so the one
  gate the script does not mirror becomes visible locally.

### F22 LOW — NOT FIXED (reported with fix) — the analyzer assemblies' PDBs ship nowhere
- `DwarfMapper.Generator.dll` and `DwarfMapper.CodeFixes.dll` are packed as `<None Pack>` items from
  `bin\$(Configuration)\netstandard2.0` into `analyzers/dotnet/cs/`; their portable PDBs are not
  included (`IncludeSymbols`/snupkg only covers `IncludeBuildOutput != false` projects, i.e. the runtime
  library), and no `DebugType` is set for either analyzer project. Generator.dll carries a CodeView entry
  pointing at a PDB nobody can obtain.
- docs/RELEASING.md:31 describes the main package as the bundle "plus symbols"; the .snupkg it points at holds
  DwarfMapper.dll's PDB only. The statement is true of the runtime library and untrue of the two analyzer
  assemblies bundled in the same package. Either the doc narrows or the PDBs ship (embedded).
- Consequence: the generator has no catch-all, so a crash surfaces to the consumer as Roslyn CS8785 with the
  exception text — and with no PDB, no line numbers. Every crash report from a consumer (the
  `AmbiguousAmbientProviderTests` fixture is one such report) arrives without the one thing that locates it.
- Fix: `<DebugType>embedded</DebugType>` in the two analyzer csproj files (embedded PDBs are the standard
  answer for analyzer packages, which cannot use snupkg). It grows `Generator.dll` by roughly its PDB size,
  so the ceiling above must be re-measured in the same commit. Not applied because it changes shipped
  package content and SECURITY.md's package-content claims should be re-read alongside it.

### F23 LOW — NOT FIXED (reported with fix) — the release attestation and SBOM scope
- release.yml attests (`attest-build-provenance`) `artifacts/*.nupkg` + `*.snupkg` only; `SHA256SUMS` and the
  SBOM are uploaded unattested, so the provenance statement does not cover the document that lists the
  provenance. Fix: add `artifacts/sbom/*` and `artifacts/SHA256SUMS` to `subject-path`.
- The SBOM is `dotnet CycloneDX DwarfMapper.NET.sln`, the WHOLE solution (documented as such in
  docs/RELEASING.md), so it lists AutoMapper, Mapster, Mapperly, BenchmarkDotNet, xunit, coverlet… as
  components of a release whose shipped packages have an EMPTY dependency group. For a CRA reader that
  overstates the supply chain by every dev dependency in the tree. CycloneDX 6.2.0 has
  `--exclude-test-projects` (`-t`) and `--exclude-dev` (`-ed`) (verified from `dotnet CycloneDX --help`);
  generating from the two shipped csproj files (or the solution with both switches) yields the SBOM of what
  ships. Not applied: RELEASING.md documents the whole-solution choice, so it is a policy the maintainer set.

### P8 observations (no defect)
- Both packages ship the SAME README (the main one); DwarfMapper.Testing's nuspec tags include `aot` while
  its description says "never AOT-published". Cosmetic; a Testing-specific README/tag set is a nicety.
- No `<icon>` in either nuspec.
- Local `rcN` packs from a dirty tree carry the clean HEAD commit hash in `<repository commit=…>` (SourceLink
  reads HEAD); artifacts/ is git-ignored and the official flow packs from a tag, so this is local-only.
- Official tag scheme is `vX.Y.Z-rc.N`; the local artifacts used `rc5` (no dot). Local-only.
- The release workflow builds release notes from the `## [VERSION]` CHANGELOG section and fails if it is
  missing; CHANGELOG has only `## [Unreleased]`, so the first tag needs the section cut first (documented).


## P11 — Rider inspection list (user-supplied, 2026-09-02 06:15), resolved where the compiler agrees

The user pasted Rider's solution-wide inspection report (~600 entries, character offsets). The compiler build
is clean under warnings-as-errors + AnalysisMode All + Meziantou, so every entry is Rider-only. Triage:

- **~330 nullability guesses (`Possible 'null' assignment`, `Possible NullReferenceException`) — NOT
  changed, by rule.** Every sampled site is an indexer, an enumerator `Current`, a tuple item, a Roslyn
  `ITypeSymbol` member or an AutoMapper call inside netstandard2.0 code, where Rider reads its own external
  annotations for an unannotated framework and guesses "may be null". The compiler's flow analysis (NRT on)
  disagrees at every one. "Resolving" them in code means `!` or a dead null check per site — the exact thing
  CLAUDE.md forbids. This class is an IDE-severity setting for the two netstandard2.0 projects, not code.
- **Rider-vs-Roslyn-analyzer / test-contract conflicts — NOT changed.** `McDst`'s `aliasText?.` is the
  CA1062 guard (public surface); retyping the three never-updated dictionaries to `IReadOnlyDictionary` trips
  CA1859; `OptionTableRenderer.ExistingProse`'s "always false" `markdown is null` is a TOLERATED-null contract
  pinned by `OptionTableRendererTests.A_null_committed_document_still_renders_the_mechanical_columns` (found
  when the copy's suite ran; the first attempt to replace it with a throwing guard failed that test). The
  analyzers and the tests win — they gate the build.
- **Hook-contract parameters (`OnBeforeNode(HookNode source)`, Conformance `After(F15S s, F15D d)`) — NOT
  changed:** the signature is the generator's hook contract; the parameter is the contract, not clutter.
- **Rider lambda/precondition nudges (`ResultFingerprint.Walk depth`, `GeneratorRegistryTests g =>`,
  `QualityBadgeRendererTests b =>`, `SurfaceProbeTests baselineCounts`, `CombinatorialEngineTests asm is
  null` which is the compiler's own flow guard, `ProjectionAgreementTests projectedMember?.`) — NOT changed.**
- **Everything else — CHANGED, in the scratch copy first** (scratchpad/linuxpack/src, branch `rider-cleanup`,
  baseline 226c71d = this working tree; patch = scratchpad/rider-cleanup.patch): 100 files. Of the 214
  worklist entries (one per distinct site; a fixture file's naming entries collapsed to one), 199 are resolved
  in the patch and 15 are deliberately left (listed above). Suppression form is mechanical: the reason on its
  own comment line, the bare `// ReSharper disable [once] <Id>` directive below it, so Rider parses nothing
  but the id.
  - Generator docs: 14 `<see cref>` to runtime types the generator cannot reference → `<c>`; a doubled
    `<summary>` (Conversions.cs) whose method had moved; `ResolveProjectionMembers`' doc still described nine
    individual parameters that were folded into `in MapperOptions options` → one `options` paragraph plus the
    missing `ignoredSourceMembers`; `ResolveFlattenInfos` gained its missing `compilation` tag; the tests'
    `<see cref="ObjectFactory">` (merged into ObjectFactoryV2), `FirstNewOccurrence` (renamed
    `NewOccurrences`), three ambiguous crefs, GoldenManifest's unescaped `<id> <sha256>`.
  - Generator dead surface: parameters never read — `srcFq` on four collection emitters, `shape` on
    EmitImmutableArray, `targetName` (AddEnumByName), `compilation` on ImplementsIEnumerable /
    ReadDerivedTypeAttributes / RenderConstantLiteral, `comp` (ExpandWrapperMaps), `targetType`
    (ResolveUnflattenTarget), `nullAsNull` (ResolveFlattenGraphDirectives), `referenceHandling`
    (ResolveProjectionMembers — its doc paragraph moved into `options`), `comparer` (TryBindProjectionCtorParam);
    dead locals `srcFq`, `nodeBaseNoAnnot`, the `pl` designation; the P9 dead initialisers (`methodDiagStart`,
    four `withheld`, and the `withheld` PARAMETER ReportSourceMemberCoverage overwrote before reading — now a
    local); `em is not null` on a pattern-proven value; `ConstructedFrom?.` on a non-null member; the
    comparer's dead `obj is null` arm; three redundant `!`; a redundant trailing `return`; 15 unused usings.
    `LocationInfo.From(Location)`'s dead `location is null` arm: DEFERRED, not changed — every caller passes a
    non-null `Location` (`GetLocation()`, `Location.Create`, or `FirstOrDefault() ?? …`), so the check is
    defensive only; but LocationInfo.cs is one of the four files the generator mutation leg measures (P7 ran
    on the pre-patch tree), and the repo's precedent for moving that threshold is a token diff showing zero
    code tokens changed in the mutated files. A `?` would fail that bar. Re-measure the leg before touching it.
  - Naming: `isRC` → `isRecursionCapable`, `innerNN` → `innerNonNull`, `KeyIsPublicObj`/`ValIsPublicObj` →
    camelCase locals, `EmitDWARF028` → `EmitDwarf028` (19 uses), `D4a/D4b` → `D4A/D4B`, `srcMM/dtoMM` →
    `srcMinMax/dtoMinMax`, record component `Oracle_` → `OracleValue`. Deliberate names get a one-line
    `// ReSharper disable ... -- <reason>`: `Fnv1a` (the algorithm's name), the two `TargetKind`-style enums
    whose members ARE the BCL interface names, Conformance's `user_name`/`itemcount` (the shapes F22/F36 map),
    constructor parameters spelled like the members they bind to (ConstructorMappingRuntimeTests).
  - Namespaces: `.csproj.DotSettings` marking `guides` (Gallery) and `Snapshots` (Generator.Tests) as
    non-namespace-provider folders (the Rider quick-fix's own file shape); `CheckNamespace` once for the
    `System.Runtime.CompilerServices` polyfill and the three registry tests that live in consumer namespaces on
    purpose.
  - Runtime library: the two public-surface null guards (`SetReference`'s `src is null`, `MapToAttribute`'s
    `targets ??`) KEPT with a suppression stating why — an oblivious caller can still pass null, and an
    attribute constructor must never throw.
  - Testing library: `IsUnorderedCollection(IEnumerable)` → `(object)` (it only inspects the type), which is
    what made Rider count a second enumeration at 8 sites; two `Set` local functions static.
  - Tests: 6 local functions static; `StaleLocation(out tree)` → `StaleLocation()`; `CompileFiles` returns only
    what its two callers read; `OnlyCtor`'s never-read third argument (a fixture knob that was never wired)
    removed with the expression its one caller computed for it; `BuildSource`/`BuildPolymorphicDispatchSource`
    drop `basicType`; `BuildSourceWithFlattenGraph(members, seed)` → `(seed)`; `CountMethodDefinitions` drops
    two names it never read; three `generated` results never read; `sites`/`byGenerator` materialised; the
    `GetEnumerator().MoveNext()` probe → `Cast<object?>().Any()`; two struct `Equals("string")` probes via a
    typed `object`; redundant casts/qualifiers/type-args; `Assert.Equal<List<long>>`; `.WithCancellation` on a
    token already passed; `B { get; } = ""` whose ctor always assigns; the torture test's `_ =>` parameter
    that `_ = mine;` was assigning (now `source =>`, so `_` is a true discard). Runtime oracles that Rider
    calls "always true" because the FACTORY assigns through reflection and can null any member
    (PolymorphicMemberFuzz, ObjectFactoryGraphShape, ObjectFactorySubstitution ×2, AotSample's SetNull check,
    PreserveGraphEdgeCases' shared enumerable ×2, the torture closures) KEPT with the reason on the line.
  - AotBench: `dst.X != src.X` on floats → `BitConverter.SingleToInt32Bits` compare, which is what a blit
    correctness check should have been (NaN payloads and signed zeros are now caught).
- Verification in the copy: `dotnet build DwarfMapper.NET.sln -c Release` clean (0 warnings, 0 errors, the
  full analyzer set); fast-tier suite: all 9 projects green after one correction — `OptionTableRendererTests.A_null_committed_document_still_renders_the_mechanical_columns` proved the "always false" `markdown is null` check is a TOLERATED contract (a null committed document renders the mechanical columns), so that file is left exactly as it was and Rider's finding there is wrong in substance; Generator.Tests 7192, IntegrationTests 868, CompilerTests 52, NegativeCases 120, Testing.Tests 80, DifferentialTests 70, the three consumer/corpus suites.
- Applied to the working tree at 07:20 (scratchpad/rider-cleanup.patch, 100 files) together with the F24–F26 fix (scratchpad/consumer-fixes.patch, 5 files); verification on the real tree: scratchpad/verify.log.

## P12 — consumer-reported codegen defects (teammate session, 07:05; findings F24–F27, all FIXED)

Handed over by the session working on the consuming solution (four mapper projects on 1.1.0-rc5): nine CS
warnings emitted from inside `.g.cs` files, three distinct causes, with the empirical note that neither
`#pragma` nor an `.editorconfig` `[*.g.cs]` section suppresses a compiler diagnostic raised in a generated tree.
Reproduced in-tree first (each minimal shape failed against the unfixed generator with the consumer's id),
fixed at the emit site, verified in the scratch copy. Common root: `GeneratedCodeIsWarningFreeTests` is the
right oracle and has run since it was written — none of the three shapes exists in the combinatorial or fuzz
schemas. Corpus holes, the [[test-infra-holes-pattern]] again.

### F24 HIGH — FIXED — nullable-element collection → synthesized object helper emits CS8604 per element
- `CollectionConverter.ElementExpr` built `helper(__item)` / `helper(src[__i])` for a `Child?[]` source while
  the synthesized `__DwarfMap_Obj_…(Child s)` takes a non-nullable parameter (and null-guards internally,
  `if (s is null) return null!;`). Runtime right, annotation wrong, one CS8604 per element. Fix: `Synthesize`
  (both variants) computes `srcElemIsNullableRef` from the element's `NullableAnnotation`; `ElementExpr`
  null-forgives the argument for synthesized converters, in the plain and the `is null ? null :` arms (the
  array fast path indexes `src[__i]` twice and flow analysis tracks no indexer, so the second arm needs it too).
  Not applied to USER element converters: the member path resolves that through `ForgiveNestedNullableArg`
  with the method tables, which `Synthesize` does not receive — recorded as the next hole to close.
- Kept: the helper's `S → T` signature with `return null!`. Making it `S? → T?` plus `[return: NotNullIfNotNull]`
  is the honest signature, but it rewrites every object helper in every golden snapshot; not in an audit diff.

### F25 HIGH — FIXED — constructor-argument binding omits the `!` (and DWARF070) the member path applies
- `ResolveConstructorArguments` built its `MemberMap`s without `NullRefIntoNonNullable` at both sites (the
  explicit `[MapProperty]` path and the by-name path), so `AppendValueExpression` — shared with members — never
  appended `!` for a constructor argument, and the DWARF070 report in `ResolveMembers` never saw it (it walks
  the member list only). `alias: source.Alias` (CS8604) beside `Alias = source.Alias!` in ONE expression. Fix:
  both sites set `NullRefIntoNonNullable: IsDirectNullRefAssign(conv, nullH, srcType, param.Type)`, and the
  resolver reports DWARF070 for the flagged arguments (ordered by parameter name).

### F26 MEDIUM — FIXED — enum switches name `[Obsolete]` members bare (CS0618 inside the generated file)
- Every enum↔string and enum↔enum emitter (`AddEnumToString`, `EmitStringToEnum`, `EmitSwitchByName`,
  `EmitFlagsByName`, `EmitStringToFlags`) names every member, as an exhaustive map must; a domain enum keeps
  deprecated values on purpose. Fix: `IsObsolete(member, out isError)` from the attribute's second ctor
  argument; `ObsoleteGuard` wraps only a switch that names a warning-level obsolete member in a scoped
  `#pragma warning disable/restore CS0618` (deliberately not the file header, so a CS0618 anywhere else stays
  visible); `EnumMembers` skips the error form (CS0619 — no pragma lifts it, nobody can reference it), so the
  emission compiles instead of failing.
- The "1 of N sites" sweep (advisor, after the first fix; F11's lesson): the same two shapes through every other
  emitter that binds them, each added as a test FIRST —
  - explicit `[MapProperty("Alias", "fullCommandToExecute")]` constructor binding (the consumer's real form):
    already covered by the first fix (both resolver branches were patched); pinned.
  - **projection endpoint** (`.Project`): `new Dst(__s.Format, __s.Alias)` — bare, CS8604 (and the initializer
    form would be CS8601). `ResolveProjectionExpr`'s identity arm now null-forgives a nullable reference into a
    non-nullable target and reports DWARF070 once per source member per method (deduplicated across the
    initializer and constructor bindings). Expression trees accept `!`; it is erased.
  - **dictionary values** (`Dictionary<string, Child?>`): `DictionaryConverter.Expr`, the twin of
    `ElementExpr`, gets the same forgiving; the value nullability is read off the source's
    `IEnumerable<KeyValuePair<K,V>>` (`SourceValueIsNullableRef`) so both emitters — `Synthesize` and the
    self-recursion `SynthesizeInPlace` re-synthesis — answer alike.
  - **the `[MapTo]` registry generator** (`MapToGenerator`): three defects in one shape — the object helper
    argument bare (CS8604; the helper null-propagates `s is null ? default! : …`, so forgiven), the collection
    helper declared over `Child[]` for a `Child?[]` member (CS8620) and returning `ChildDto[]` where the member
    is `ChildDto?[]` (CS8619). Fixed with nullable-aware type formatting (`FqNullable`/`FqNullableParam`, the
    registry twin of the class model's) for the helper's key, parameter and return type — annotations exactly
    as the member declares them; a first cut that also re-added the OUTER `?` on every collection parameter
    (honest, since the helper null-guards) moved the registry golden snapshot and the manifest for no consumer
    benefit and was withdrawn: the outer form is byte-identical to before. The warning oracle
    ran only `DwarfGenerator`; `GeneratedCodeWarnings` gained `includeRegistry` for this test, opt-in so the
    combinatorial cells keep measuring the generator they were written against.
- Tests: `tests/DwarfMapper.Generator.Tests/ConsumerReportedEmissionWarningsTests.cs` — 17 tests (13 + the
  four sweep variants, of which 3 failed before their fixes), 11 of the first 13 failed
  before the fix; positive controls: the deprecated value is still mapped, a clean enum gets no pragma, an
  error-level member disappears, a nullable-element array still round-trips null slots.
- Verification: whole-solution Release build clean in the copy; fast-tier suite in the copy: all 9 projects green (Generator.Tests 7205 incl. the 13 new, IntegrationTests 868, CompilerTests 52, NegativeCases 120, Testing.Tests 80, DifferentialTests 70, consumer/corpus suites); no golden/snapshot file moved — none of the three shapes was in the corpus, which is the finding.
- Verification on the REAL tree after both patches (scratchpad/verify.log, 07:16–07:21): tests/**/bin+obj
  cleaned post-Stryker; `dotnet build DwarfMapper.NET.sln` Debug and Release clean (samples included);
  `housekeeping.ps1 -Coverage -ILVerify -SkipExhaustion` PASSED — fast suite green, coverage band
  DwarfMapper 91.5/91.2 · Generator 94.8/94.5 · DocTooling 96.3/96.0 · CodeFixes 96.2/96.2 · Testing 96.5/96.4
  (all inside the band, no mandatory raise), AOT publish + execute all checks passed, ILVerify 3 known.
- Consumer re-verification: 1.1.0-rc6 packed to artifacts/nuget (main + Testing, snupkg) at 07:21 and handed to
  the consumer session. CONFIRMED 07:55: all eight FusedChat projects bumped rc5 → rc6, `dotnet build -t:Rebuild`
  — generated-file warnings 9 → 0 (5× CS8604, 4× CS0618 gone), solution warnings 310 → 302 with the delta
  exactly the generated set, `dotnet test` 709 passed / 0 failed across 9 test projects. DWARF070 for the ctor
  parameter is reported (24 → 26 under `-p:NoWarn=`, the two new ones being the ctor binding) and suppressed by
  that project's existing, documented NoWarn. The deprecated enum values still map (their polymorphic-dispatch
  test passes through `BotPlatform.DLive`). No user-declared per-element converter exists in that consumer, so
  the remaining F24 gap is not reachable there. rc7 (rc6 + the sweep) was packed at 08:15 and offered; the
  consumer session stays on rc6 with evidence that none of the three swept shapes is reachable in that solution
  (no `.Project`, no `[MapTo]`, every mapped dictionary value type non-nullable — read off the emitted trees),
  so FusedChat is a non-signal for the sweep sites, not an untested consumer. At the user's request (09:0x)
  the consumer session then bumped all eight references rc6 → rc7 (packed 08:15 from the code committed as
  d09cc16 on audit/round28) and re-verified: 0 errors, 0 generated-file warnings, 0 DWARF leaks, the same 78
  unique solution warnings as rc6 (empty delta both ways), 709 passed / 0 failed / 4 pre-existing skips — rc7
  identical to rc6 in every measured respect there, as predicted. Both sides uncommitted pending review.
- Package after the fix and the sweep: Windows 288,884 B = 282 KB, container 288,676 B = 281 KB — the two
  platforms straddle the boundary by the 208-byte CRLF/LF delta. Ceiling set to 282 (the larger measurement, so
  one tree is green wherever the gate runs; 281 would leave 92 B on ubuntu and be red on every Windows pack),
  both numbers and the reasoning in the gate's comment block; the gate passes on scratchpad/pkgsize4 at 282.
  Observation for the maintainer: writing the doc XML with LF on all platforms would make the numbers coincide.

### F27 MEDIUM — FIXED — the F26 guard named CS0618 only; the bare `[Obsolete]` raises CS0612 and went through
- Found by the round-28 patch-coverage pass (Codecov: 92.88% patch, 28 lines missing across 8 generator files),
  not by a consumer: the test written to cover `IsObsolete`'s "fewer than two constructor arguments" branch
  used the message-less `[Obsolete]`, and the warning-free assertion failed with **CS0612** — the id the
  compiler uses for the bare form, distinct from CS0618 for `[Obsolete("message")]`. F26 was tested against
  the message form only (the consumer's shape), so its guard was a `#pragma warning disable CS0618` and the
  bare form still leaked out of the generated file, unsuppressible consumer-side exactly as F26 described.
- Fix: `ObsoleteGuard` disables and restores `CS0612, CS0618`. No golden snapshot names the pragma; the two
  pinning assertions in `ConsumerReportedEmissionWarningsTests` and the CHANGELOG entry now name both ids.
- Same class as every round-28 finding: a corpus hole (one attribute form of two), not subtle code. The
  patch-coverage pass is what found it — a branch nobody had executed was a branch nobody had asserted on.
- Patch-coverage pass itself (`Round28PatchCoverageTests`, 21 tests): the 28 missing lines are all executed;
  the partial-branch outcomes that remain are structural — `BlittableProof` 496 (`declaration is null`: every
  field's declaring syntax has a type-declaration ancestor), 548 (`attr.AttributeClass?.` null: an attribute
  with no class), `IgnoredSourceMembers?.` null (every in-product caller passes the set; the parameter default
  exists for direct callers), `ImplementsIEnumerable(tgt)` true (the collection arm claims an enumerable
  target with DWARF027 before the object-pair predicate is asked), and `IsObsolete`'s `AttributeClass?.` null.
  `DictionaryConverter.SourceValueIsNullableRef` had an unreachable `return false` (every admitted source
  implements `IEnumerable<KeyValuePair<,>>`); rewritten as one expression so no dead line remains.
- Coverage band: Generator 94.8 -> 95.5 (11042/11560), a full point over the 94.5 floor, so the band rule
  made the raise mandatory; floor re-pinned at the truncated measurement, 95.5, in scripts/housekeeping.ps1.
- The generator's Stryker leg was NOT re-run (51 min; margin 0.4 pp over the 84 break): the two generator edits
  are the pragma string (killed by the `Contains` assertions) and the removed dead line (fewer mutants). The
  next `-Deep` reading is the measurement.

## P9 — IDE-grade inspection (notes only)

`jb inspectcode` is not installed; the Release build of the whole solution under `AnalysisMode All` +
Meziantou + warnings-as-errors is the inspection that matters and it is clean. What a reader would still flag
in the touched generator files, none of it behavioural:
- `MapperExtractor.Members.cs`: `withheld` and `methodDiagStart` are initialised and then unconditionally
  assigned before first read (dead initialisers); `ReportSourceMemberCoverage` takes a `bool withheld` it
  never reads.
- `MapperExtractor.Phases.cs`: one `em is not null` check on a value the preceding pattern already proved
  non-null.
- `BlittableProof.cs:488`: a redundant cast on `f.AssociatedSymbol` — removed in this diff.
- `AmbiguousAmbientProviderTests.cs` / `GeneratorResilienceAdversarialTests.cs`: a fixture named after a real
  product ("FusedChat") — to be renamed to a neutral name before commit.

## P10 — docs / CHANGELOG / SECURITY sync (findings F18)

### F18 LOW — FIXED — `docs/SECURITY.md` and `docs/CORRECTNESS.md` said the AOT gate only compiles; it executes
- `docs/SECURITY.md:44` "until Phase 5 lands `aot-trim-gate` only *compiles* the AOT sample rather than
  running it" (written in 0a30e7c, P3) and `docs/CORRECTNESS.md:72-74` "the behavioural gate in its
  `Program.cs` is run locally, not executed by CI". Phase 5 landed in 0c0da41: `ci.yml` `aot-trim-gate`
  publishes with `-warnaserror`, asserts no managed assembly / no runtimeconfig.json (a NativeAOT proof), and
  runs the binary on linux-x64 and win-x64. The sentence that promised the update was never updated.
  Understating a gate is the safe direction, but the security doc's claim register is the document a CRA
  reader is pointed at, and it must describe what runs. Both reworded; the 36 `return 1` sites re-counted.
- `ClaimMechanismScanTests` only checks that the `CI:` job NAME exists in ci.yml — it cannot see what the
  job does, so it could not catch this. Consistent with the doc's own "weakest bindings" caveat.
- Other SECURITY claims spot-checked against code: runtime has zero non-analyzer PackageReferences (all
  `PrivateAssets="all"` analyzers); generator references only `Microsoft.CodeAnalysis.*`; narrowing is
  `CreateChecked` (S17 threw `OverflowException`); parse culture invariant (P3). Hold.
- `docs/diagnostics.md` `## dwarf064` said "same-named" source member; the fixed check is name-convention aware
  and the message now names the real source spelling for the `[MapIgnoreSource]` remedy. Entry reworded.
- CHANGELOG: F17 bullet (Fixed), doc-corrections bullet (Fixed: howto Validate() semantics + AOT gate wording).
