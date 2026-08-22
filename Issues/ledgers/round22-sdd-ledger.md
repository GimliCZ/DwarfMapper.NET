# Round 22 - compiler-grade testing and the 97% gates program - execution ledger
Plan: docs/superpowers/plans/2026-08-21-round22-compiler-testing-and-97-gates.md (commit 4808129).
Branch feat/round22-gates from master 73c58c3 (post D-e: Scan9 guards all 90 ids, no exemption set).
Rulings in force: per-dimension reframe; ledger-only adjudication (RAW-score gates + documented offset);
deep-tier ceiling raised (all legs nightly); Sonar rejected; D-e complete.
Layer 0 (Z1-Z3): PARKED - maintainer-gated on the master push with workflow OAuth scope.
Execution order: P1 first (everything depends on the ratchet invariant + equivalents ledger), then P2-P6,
then K0->K1->K2 sequential, S1 early where idle, W-batch where files are open.
Standing rules: explicit pathspecs; measured floors no cushions; whole-solution build gate; five-file
diagnostic sync; H7 termination discipline; RepoWriteGuard/ARCH-06; agents verify in FOREGROUND
(background wake-ups broken); leg re-measures serialized on a quiet machine.

## P1 — ratchet invariant (R1–R4) scan + equivalents ledger — DONE 2026-08-21
Commits: 8b5368f (ledger) · 6e2538c (gate-checks + wiring + battery) · f37b072 (scan).
- **Ledger** `Issues/ledgers/equivalent-mutants.md`: prose + one fenced JSON table (identity =
  leg+file+member+mutator+original→mutated; lines informational). Transcribed only — nothing newly
  adjudicated. Counts: generator 16 proven + 8 probably; doctooling 1 proven; runtime 1 proven +
  1 ruled-in-practice (Key.Equals, CF 5.4). rawCeiling = (scoreable−proven)/scoreable: 92.03 / 99.64 /
  99.11. FINDING: research/plan's "generator 15 proven" is an arithmetic slip — T3's own rows force 16
  (14 BlittableProof: L28+L29+L58×12, cross-checked 36=14+20+2 and 46=2+8+36; + CS L58, L243). The
  plan's "≈89.8 raw" is the 167/186 adjudicated-denominator figure — flagged, not adopted.
- **R2** `scripts/gate-checks.ps1` (function-only, dot-sourced): Test-CoverageWithinBand +
  Assert-MutationScoreWithinBand/Assert-LegScoreWithinBand; quantum = 1 unit of gate precision
  (1.0 pp / 1 point — Q5-default deviation stated in header; one kill crosses the integer at all three
  current populations); message "raise the floor to the measured value in this commit". Wired: coverage
  loop + all three -Mutation legs. GateBandLogicTests: one pwsh battery, 7 scenarios, both directions +
  band edges, against the REAL functions; ARCH-06-registered temp-only writes.
- **Scan** RatchetInvariantScanTests (9 tests): R1 configs (break≤low static + dated MEASURED + a quoted
  score flooring to break) + floors (5 assemblies, one-decimal, dated, value traceable); R2 existence +
  wiring pins; R3 ledger pins (10 rows/27 occurrences + per-leg/category exact pins, anchors resolve,
  no dupes, summaries==row sums, rawCeiling recomputed, measuredRawScore floors to config break) +
  ExcludeFromCodeCoverage pinned 0 with live justification obligation; R4 Stryker-disable pinned 1
  (DocSnippetInjector, "progress guard", paired restore) + no branch/wall-clock comparison feeds a
  failure in either script.
- **Sabotage demos** (applied → red → reverted): break 90>low 80 → R1 red (exact message); provenance
  date stripped → R1 floors red; 2nd src Stryker disable → R4 red naming ruling (b); ledger 12→11 →
  R3 red (26≠27); runtime band call deleted → R2 red (2≠3 calls).
- **Verification**: whole solution 0W/0E (23.5 s); full suite 7,711/0 foreground (69.2 s vs 66.2 s
  baseline, +4.5 % — inside cap); live `housekeeping -Coverage` PASSED with all five floors exactly
  at measurement (within-band proof + live coverage-side R2). Mutation-side band runs live at the next
  leg re-measure (P2/P3) — this task exercised it via fake reports only. Post-run tracked tree clean.
P1: DONE (8b5368f ledger, 6e2538c R2 bands, f37b072 scan). Controller ACCEPTS all five disclosed
deviations (plan-won ledger format; rawCeiling=(scoreable-proven)/scoreable; quantum=1 gate-precision unit
pending Q5; canonical R1-R4 numbering; enforceable-subset provenance). FINDING: generator proven
equivalents = 16 not 13/15 - the research figure was a slip, recount forced by T3's own rows. RawCeilings
pinned: generator 92.03 / doctooling 99.64 / runtime 99.11 - raw 97 reachable on two legs. Suite 7,711/0
(+10 tests, +4.5% inside cap). Gaps disclosed: CI matrix lacks R2 band (Z1 territory); generator re-measure
will absorb pwsh battery ~+2-3 min (record, not regression). Queue: P2 (runtime kill list + re-measure,
mandatory-raise applies live for the first time), then P3, P4, P5, P6, K0->K1->K2, S1, W-batch.

## P2 — runtime-leg kill list + re-measure — DONE 2026-08-21
Commits: 41560e8 (kill tests, test-side only, src untouched) · b74023f (break 87→96 + ledger + pins).
- **Kills, killedBy-confirmed in `StrykerOutput/2026-08-21.23-10-31`** (all 10 targeted flips):
  holes 2–3 — `Update` L258–261 (×4) / `Map` L133–134 (×2) `ThrowIfNull` statement removals →
  `Update_null_arguments_throw_with_the_offending_parameter_named` (RegistryUpdateContractTests) and
  `Map_null_arguments_throw_with_the_offending_parameter_named` (AmbientRegistryTests): pins
  ArgumentNullException + ParamName per argument, on deliberately unregistered pairs so a deleted guard
  lands on the wrong exception type deterministically. Hole 11 remainder — remedy string tails L98/L101/
  L104 → extended `A_lazy_iterator_with_no_map_at_all_is_told_to_materialize` (iterator diagnosis sentence
  + the complete pasteable `[GenerateMap<IEnumerable<T>, IfDstG>].`) and
  `A_plain_type_is_told_to_declare_the_pair` ("or inject that assembly's concrete mapper directly.").
  NC hole 10 — `TryEnterNode` → new `DwarfRefContextOnStackGuardTests` (3 tests): non-SetNull no-op-true
  clause, enter/re-enter/exit protocol, reference-identity pin (value-equal records are distinct nodes).
- **Adjudications (proofs in E3-E1's round-22 P2 appendix, entries + pins moved same commit):** facade
  `TryGet` guard `&&`→`||` (IDwarfMapper L73, plan hole 13) — PROVEN equivalent, not killable: TryGetValue
  sets out=null exactly on false and Register's ThrowIfNull bans null delegates, so the operands co-vary on
  every reachable input (deviation from the plan's kill-identification, sanctioned by P2's own
  "Killed or ledger-adjudicated" exit). `FormatMessage` `Count: > 1`→`>= 1` (L86) — probably-equivalent per
  the plan's disposition. Ledger 10→12 rows / 27→29 occurrences; runtime proven 2 / ruled 1 / probably 1;
  rawCeiling 99.11→98.23.
- **FILED, not killed: NC hole 7** `Key.Equals(object)` (L286) — private nested struct, reachable only by
  boxing via reflection (house stance forbids); killable only via product change → stopped per the P2 rule,
  filed as TASKS.md **I1** with both candidate dispositions (internal+IVT contract test, or a D-b-style
  denominator ruling; note CA1067 blocks plain deletion).
- **Re-measure (quiet machine, foreground-verified background run + monitor):** 87.61 % → **96.46 %**
  (109 K / 3 S / 1 NC of 113 scoreable, 0 Timeout, 8 CompileError excluded), **8:08** wall (was 11:36).
  `break` 87→**96**, `low` 87→96, `high` 90→97 (kept above `low`), RE-MEASURED provenance sentence in the
  config comment; ledger `measuredRawScore` 96.46 same commit (R3 coupling held green).
  The leg is saturated modulo I1: every remaining undetected mutant is dispositioned.
- **R2 band, first live mutation-side exercise:** at break 87 → threw "RAW score 96.46% (109/113) floors
  to 96, a full point above break 87 … raise the floor to the measured value in this commit"; at break 96 →
  "within [96, 97) - floor equals measurement". Both directions captured on the real report.
- **Verification:** scan tests + band battery 10/10; whole-solution build 0W/0E; full suite **7,716/0**
  (baseline 7,711 + 5 new tests) in 73 s foreground — inside the fast-tier cap. Post-run tracked tree
  byte-clean except intended edits; 8 Debug-bin `DwarfMapper.dll.stryker-unchanged` backups deleted; every
  live `DwarfMapper.dll` in test bins string-scanned clean of Stryker markers; no generated-doc churn.
P2: DONE (41560e8 tests, b74023f break-ratchet+ledger). Queue per plan: P3 next on the quiet machine.
P2: DONE (41560e8 kills, b74023f raise). Runtime 87.61 -> 96.46 RAW, break/low 96, high 97, 0 Timeout,
wall 8:08 (was 11:36 - survivors-to-early-bail kills = ~30% leg speedup, noted as finding). R2 band proven
live both directions on real report. One NEW adjudication (facade TryGet &&->||, case-analysis proof,
sanctioned by P2 exit clause "Killed or ledger-adjudicated") - ledger 12 rows/29 occ, runtime rawCeiling
98.23. I1 FILED (Key.Equals(object) override: private nested struct, killable only via product change -
maintainer options recorded). Suite 7,716/0. CONTROLLER ACCEPTS the adjudication deviation - the proof is
in the committed ledger and the R3 pins moved with it in the same commit, which is the instrument working.
Queue: P3 (DocTooling families A-D + ParseId + triage of the 43), then P4, P5, P6.

## P3 — DocTooling kill list (families A–D + ParseId) + triage of the 43 — DONE 2026-08-22
Commits: 1132586 (kills + seam, test-side except the sanctioned accessibility seam) · 254c500 (ratchet).
- **Baseline re-confirmed first** (foreground run, 4:56, `23-32-50`): 67.96 % = 193/284, 54 S / 37 NC /
  0 T — byte-identical to the H7 phase-2 figure; every kill was then targeted per-mutant off THIS report
  (the ledger's line numbers had drifted with H7 phase 2 — identity by expression).
- **Families, all five landed, killedBy-confirmed in `23-56-18`**: B `DocTableInjectorTests` (new file:
  both refusals with message+path pins, exact-output happy paths incl. CRLF/first-line/two-table/trailing
  newline, ParamName pins); C `ExampleCatalogueTests` (new; `Build` + both `IsNotBuildOutput` predicates
  private→internal + IVT on DwarfMapper.DocTooling — the catalog's own "existing file-list seam", no new
  production code; 059_ decoy pins the D2+underscore prefix); A the message-pin convention (every
  `Assert.Throws<DocToolingException>` in the scanner/injector/property tests now asserts a fragment AND
  the file:line, markers moved off line 1 so i+1→i-1 discriminates); D `OptionTableRendererTests` (new:
  synthetic string/empty/int?/null/bool/enum types, both em-dash fallbacks, ragged + five-cell rows,
  endtable stop, header-masquerade via a property literally named Option, CRLF, unclosed table,
  UndocumentedOptions boundaries); ParseId malformed-marker + empty-id in BOTH files with line pins.
- **NoCoverage 37 → 0**: 34 killed, 3 adjudicated (Build ternary >1→>=1 evaluated only at counts != 1;
  TryCreate catch-block removal = Stryker's return-default epilogue reproducing the removed `return null`;
  Format empty-string conditional-false — arms agree at s == ""). Zero touch-covered: every covered
  mutant is covered by an asserting test or proof-adjudicated.
- **The 43 triaged row-by-row**: all confirmed ordinary corpus holes, no corrupted-corpus residue.
  FINDING: one mis-filing in the T3-H1 record — its DocSnippetInjector "L83" row was the family-E proven
  equivalent double-counted; the actual 43rd is a FOURTH OptionTableRenderer L94 mutant (corrected split
  16/8/9/7/3, recorded in the T3 P3 section). Disposition: 38 killed / 2 adjudicated / 3 left-with-reason.
- **Re-measure (foreground, quiet, 4:36)**: **95.42 % = 271/284, 13 S, 0 NC, 0 Timeout**; 81 flips vs
  baseline (44 S→K, 34 NC→K, 3 NC→S), zero regressions. break/low 67→**95**, high 90→96, RE-MEASURED
  provenance in the config; ledger doctooling measuredRawScore 95.42 / proven 1→10 / rawCeiling
  99.64→96.47 same commit; scan pins rows 21 / occurrences 38 / doctooling-proven 10.
- **R2 band live, both directions on the real report**: at break 67 → threw "RAW score 95.42% (271/284)
  floors to 95, a full point above break 67 … raise the floor to the measured value in this commit"; at
  break 95 → "within [95, 96) - floor equals measurement". RatchetInvariantScanTests + GateBandLogicTests
  10/10 after the pin moves.
- **I2 FILED** (TASKS.md): the 3 FS-composition-root survivors (ScanAll OrderBy + "*.cs", Scan "*.cs") —
  corpus holes, not equivalents (empty GetFiles pattern returns ALL files, measured 105 vs 44), killable
  only by planting files in the real tree or redirecting RepoLayout (both banned); the injectable
  file-list seam is a product-shape decision for the maintainer.
- **Verification**: whole-solution build 0W/0E; full suite **7,747/0** (baseline 7,716 + 31 family tests)
  foreground 62–66 s vs 73 s baseline — cap held. Post-run tracked tree byte-clean; the one
  `DwarfMapper.DocTooling.dll.stryker-unchanged` backup deleted; test-bin DocTooling DLLs string-scanned
  clean. Leg wall-clock 4:36 (was 5:04) — survivors-to-early-bail again.
P3: DONE (1132586 kills, 254c500 raise). The leg is saturated modulo I2: every undetected mutant is
dispositioned; DocTooling raw 95.42 sits 1.05 under its recomputed 96.47 ceiling, and the remaining gap
IS I2's three named survivors. Queue per plan: P4 (coverage denominator + floors, reads off this corpus
growth), P5, P6.
P3: DONE (1132586 kills, 254c500 ratchet). DocTooling 67.96 -> 95.42 RAW, break/low 95, high 96, 0 Timeout,
0 NoCoverage, wall 4:36. Baseline reproduced 67.96 byte-for-byte pre-kill (honest targeting); 81 flips, 0
regressions. 9 NEW proven adjudications with proofs -> doctooling rawCeiling 99.64 -> 96.47; ledger 21
rows/38 occ. NOTE: ceiling now BELOW the 97 aspiration - leg saturated modulo I2 (3 FS-composition-root
survivors, ~1.06pp, maintainer product-shape decision filed as I2). T3 catalog mis-filed row corrected
(the real 43rd was a 4th OTR L94 mutant). Family-C seam: private->internal + IVT (house idiom, no new
production code) - ACCEPTED. Suite 7,747/0 (+31), fast tier held. R2 band proven both directions again.
Queue: P4 (coverage denominator honesty + floors re-measure), then P5 (generator families - the big leg),
P6, K0-K2, S1, W.

## P4 — coverage denominator honesty + floors to measured — DONE 2026-08-22
Commit: d8d83d8 (annotations + scan pin 0→9 + both floor raises, one commit — every same-commit
constraint of R1/R2/R3 satisfied by construction).
- **Classification table (exit: zero unclassified 0%-covered classes, all five assemblies).** The fresh
  baseline run at tip 254c500 (00:21, suite 7,747/0) showed the ONLY 0%-covered classes in the five gated
  assemblies are ten attribute classes in src/DwarfMapper; Generator/DocTooling/CodeFixes/Testing have
  none. Nine → by-design, excluded under the one pre-approved category (*compile-time-only attribute,
  consumed by the generator*): AutoNest 0/4, FlattenGraph 0/6, GenerateWrapperMap 0/2, MapCollectionKey
  0/6, MapDerivedType non-generic 0/6, MapIgnoreSource 0/4, MapProperty`2 0/9, MapIgnore`1 0/4,
  MapValue`1 0/10 — each verified zero runtime semantics (ctors assigning auto-props only), each carrying
  [DwarfSurface] (surface catalog = the non-vacuity anchor). **MapToAttribute 0/4 → stays in the
  denominator**: its ctor's `targets ?? Array.Empty<Type>()` is a defensive arm — research Q3's unruled
  category, a hole by definition until the maintainer rules (the one mixed row, left as the concrete Q3
  exhibit). MapDerivedType`2 / GenerateMap etc. have zero coverable lines and never enter the report.
- **Scan pin moved 0→9 same commit**; the justification obligations ran for the FIRST time (dead loop
  body at pin 0) and were sabotage-demoed red both ways before landing: stripped Justification → "an
  unexplained exclusion is an allowlist (invariant R3)"; unsanctioned category text → "does not name a
  sanctioned category". Both reverted; no new category added to the scan — none was needed.
- **Re-measure (fast tier, Release, 00:35 with annotations)**: runtime coverable 360 → **309** (−51,
  exactly the nine classes' lines; all nine confirmed ABSENT from classesinassembly — coverlet honored
  the attribute), covered 282 unchanged → **91.2** (raw 91.26, truncated). R2 band threw the raise demand
  live for BOTH movers before the floor edit (DwarfMapper 91.2 vs 78.3; DocTooling 95.7 vs 90.7 — P3's
  34 NC kills grew covered lines 90.7→95.7, the plan's predicted co-movement, already visible in the
  pre-annotation baseline run). Floors: DwarfMapper 78.3→**91.2**, DocTooling 90.7→**95.7**, dated
  provenance rewritten (Re-measured 2026-08-22, round-22 P4, line/branch pairs). Generator 93.7,
  CodeFixes 92.4, Testing 83.2 measured UNCHANGED to the decimal — floors untouched. Testing's branch
  wobbled 81.9→82.2→82.0 across three runs with zero code change — R4's exhibit, stays informational.
- **Honesty statement**: the runtime assembly lands at 91.2 — in family with CodeFixes 92.4 / Generator
  93.7, no longer the outlier; the exclusion removed only by-design-dead metadata lines (covered count
  did not move). The REAL residual runtime holes stay in the denominator with their partial coverage:
  MapConfig`2 6/11, MapValueAttribute (method-level) 4/10, DwarfMapValidationException 4/6,
  DwarfMappingDepthException 12/16, DwarfMapMissingException 35/39, MapToAttribute 0/4. Testing 83.2
  remains the lowest floor, unrelated to P4.
- **Verification**: whole-solution build 0W/0E (runtime csproj IsTrimmable+IsAotCompatible — the
  attribute is metadata-only, analyzers quiet); final foreground run HOUSEKEEPING PASSED: suite
  **7,747/0** (baseline held), all five floors green at the raised values; RatchetInvariantScanTests +
  GateBandLogicTests 10/10. Post-run tracked tree clean.
- **Deep-tier caveat (named owner: Z1)**: the old floors comment's "deep tier measures the same five
  values" claim was measured at round-21 T5 and was NOT re-verified for the raised floors (no -Deep run
  in P4). If the deep tier covers ≥1.0 pp more runtime lines (≥4 of 309), its R2 band goes red and
  demands the raise — that measurement belongs to Z1's first nightly, which is dormant until the
  maintainer's push; nothing can go red before then.
P4: DONE (d8d83d8). Runtime 78.3 → 91.2 by denominator honesty alone; DocTooling 90.7 → 95.7 by P3
co-movement; pin 0→9 with obligations proven live. Queue per plan: P5 (generator families), P6.
P4: DONE (d8d83d8 bundle, f6a9e91 I3). Runtime coverage 78.3 -> 91.2 DENOMINATOR-HONEST (covered lines
unchanged; -51 coverable = exactly the 9 annotated compile-time-only attribute classes, confirmed absent
from classesinassembly). Pin 0->9 same commit; obligation checks first-live + sabotage-proven both
directions. DocTooling floor 90.7 -> 95.7 (P3 co-movement, R2 demanded it). Generator/CodeFixes/Testing
floors measured unchanged to the decimal. I3 FILED: MapToAttribute defensive arm = unruled Q3 category,
left in denominator. Caveat owned by Z1: deep/fast floor parity not re-verified for raised floors (nightly
dormant). Suite 7,747/0, all floors green. CONTROLLER ACCEPTS.
Queue: P5 (generator families IsSourceSequential/InstanceFields/ConstructorSelector + ~20min re-measure,
pwsh battery absorption +2-3min expected), then P6, K0-K2, S1, W.

## P5 — generator-leg kill list (top-3 families) + NoCoverage sweep — DONE 2026-08-22
Commits: 6bf89c4 (kills, test-side only, src untouched) · 270d5cf (ratchet: config + both ledgers + pins).
- **Baseline deviation, disclosed**: no fresh pre-kill baseline run (P3 precedent was baseline-first).
  Evidence: all four mutated files unchanged since before T3 (git log per file in the T3 P5 section) and
  H5 re-validated 71.64 with ZERO per-mutant flips — the T3/H5 catalog IS the baseline. Reconciliation
  held on the fresh report: scoreable exactly 201 in exactly the four configured files, zero K→anything.
- **Kills, killedBy-confirmed in `StrykerOutput/2026-08-22.01-14-14`** (20 flips: 16 S→K + 4 NC→K):
  Family 1 `IsSourceSequential` L97 `IsInSource→true` → `CanReinterpret_field_compatible_bcl_struct_is_
  still_refused` (user {float X,Y} vs System.Numerics.Vector2 — the field-compatible pair the old
  name-mismatched BCL test couldn't discriminate; the unsafe-accept mutant). Family 2 InstanceFields →
  `CanReinterpret_partial_file_struct_verdict_is_file_order_independent` (both compile orders, both
  directions, path-order/source-offset inversion geometry): L78 sort deletion, L80/L81 cond-false + <0 +
  coalesce-remove-left, L83 byFile flip, PLUS the L86 cond-false/<0 pair from the probably-equivalent
  rows (T3's no-op claim refuted — SwapIfGreater argument order). Written as the K0 corpus-row shape
  with the forward-reference comment, per plan. Family 3 ConstructorSelector: L288 Any→All via the mixed
  `Dst(int a, ref int b)` DWARF026 test; L55 predicate ×3 via private-param-ctor + obsolete-ctor struct
  tests; L230 ||→&& + continue-NC via the optional-param wide-ctor test; L246/L250 NC via two direct
  `Select` calls (the real pipeline DWARF012s before selection sees an unresolvable [MapProperty] —
  stated in-test). NC sweep bonus: BP L32 killed by the symmetric enum-vs-struct test — T3's
  "unreachable branch" judgement was wrong, it was merely uncovered on the na side.
- **NoCoverage 11 → 7**: 4 killed (L32, CS L230-continue/L246/L250); 4 adjudicated proven-equivalent
  (L80/L81 string.Empty literals — dead arms, stay NC in the denominator); 3 left-with-reason (BP L30
  second conjunct + CS L281/L285 — the plan's Q2 dead-code questions, deliberately NOT ledgered).
- **Adjudications (proofs in T3 P5 section, ledger rows + scan pins same commit 270d5cf):** L80 + L81
  file-path-key guards proven-equivalent (4 occ each: cond-true, >=0, both string literals — every field
  reachable through the source-struct gate has a source location with non-null SourceTree; the killable
  siblings on the same expressions died to the fixture, so the proof discriminates); L86 probably row
  CORRECTED 4→2 with the invalidating kill evidence (ledger shrink rule). Generator proven 16→24,
  probably 8→6, rawCeiling 92.03→88.05; pins rows 21→23 / occurrences 38→44.
- **Re-measure (quiet, 21:19 wall, 5,897 tests):** 71.64 → **81.59 % = 164/201** (30 S, 7 NC,
  0 Timeout, 66 CompileError excluded, 204 Ignored). break 71→**81**, low 80→81 with it, high stays 90;
  RE-MEASURED provenance + NOTE 6/7 refresh in the config. Wall-clock delta vs H5's 19:41 = +1:38 = the
  P1 pwsh battery + 9 new tests × 85 static whole-suite mutants — the predicted absorption, recorded.
  Plan expected ~80 raw; landed 81.59 (the L86 pair + L32 + coalesce-left kills beat the estimate).
- **R2 band live, both directions on the real report**: at break 71 → "RAW score 81.59% (164/201)
  floors to 81, a full point above break 71 … raise the floor to the measured value in this commit";
  at 81 → "within [81, 82) - floor equals measurement".
- **Honest remainder**: every undetected mutant dispositioned — 22 proven + 6 probably survivors, 2 real
  holes left deliberately (EquatableArray.GetHashCode ×2 — NOT in the plan's fold-in; +→− is an affine
  transform within fixed length, killable only by exact-value/cross-length pins, deferred as a
  deliberate decision), 1 real-low (IsSourceSequential Any→All — needs a zero-location struct symbol
  Roslyn doesn't produce), 1 question (CS L88 flag), 3 NC questions. Do-not-attempt list untouched.
- **I4 FILED** (TASKS.md): the run left MUTATED `DwarfMapper.Generator.dll` copies in six
  analyzer-referencing test bins with NO backup marker (Stryker only backs up files it overwrites; these
  bins had no pre-run copy, so neither restore nor RepoWriteGuard's leftover signal covers them). Swept
  here (deleted, rebuilt, string-scanned clean, suite 7,756/0 proves absence correct); the sweep-scope
  decision (housekeeping vs RepoWriteGuard) is the maintainer's.
- **Verification**: whole-solution build 0W/0E; full suite **7,756/0** foreground (baseline 7,747 + 9)
  at 56 s — fast-tier cap held; RatchetInvariantScanTests + GateBandLogicTests 10/10 after pin moves;
  post-run tracked tree byte-clean except intended edits; both `*.stryker-unchanged` backups deleted;
  every test-bin `DwarfMapper*.dll` string-scanned clean against the src-built originals.
P5: DONE (6bf89c4 kills, 270d5cf ratchet). Generator 71.64 → 81.59 RAW, break/low 81, 0 Timeout, wall
21:19 (battery absorption +1:38 separated). Leg saturated modulo the named remainder: the gap to the
88.05 ceiling is EqArr ×2 + Any→All + L88 + 3 NC questions + 6 probably. Queue per plan: P6, K0-K2, S1, W.
P5: DONE (6bf89c4 kills, 270d5cf ratchet, 48f473d I4). Generator 71.64 -> 81.59 RAW, break/low 81, 0
Timeout, wall 21:19 (+1:38 = predicted battery absorption, recorded as cost not regression). 20 flips, 0
regressions. TWO T3 judgements REFUTED BY KILLS (L86 probably-equivalent, L32 "unreachable") - the
discipline cuts both ways. New adjudications: proven 16->24, probably 8->6, rawCeiling 92.03 -> 88.05.
Honest remainder to ceiling fully dispositioned (EqArr GetHashCode x2 deliberate-left, Any->All needs
impossible symbol shape, 3 Q2 dead-code questions, 6 probably). I4 FILED: mutated generator DLLs planted in
6 analyzer-referencing test bins with NO backup marker - swept, string-verified, scope decision
maintainer's. Suite 7,756/0 (56s, cap held). CONTROLLER ACCEPTS incl. the baseline-skip deviation (evidence
chain sound: files untouched since T3 + H5 zero-flip revalidation + exact scoreable match).
MUTATION STATE END OF P-LEGS: runtime 96.46/96, generator 81.59/81, doctooling 95.42/95 - all Timeout 0,
all floors mandatory-raise-enforced. Queue: P6 (matrix obligations B3+B6+B11), then K0->K1->K2, S1, W.
P6: DONE (25d9f02 B11, 68f4cf4 B3, 0fa6737 B6, TASKS.md flips in the closeout commit). The matrix's excuse
categories now all carry executed, counted obligations. B3: NotApplicable rows in ProjectionCells are
re-classified with OptionProbe every run + exactly pinned; the live check's FIRST run caught a stale excuse
(EnumStrategy - "refused before the strategy is consulted" vs the projection resolver's explicit ByValue
cast branch; measured Honoured, re-declared, class 8 -> 7). CoversOption is endpoint-aware (matches the
finding's own measured cells; docs ratchets moved to (option, endpoint) granularity against the renderer's
own column list). B6: NoSuchSite total-only ceiling (116, band -10) replaced by per-cause exact pins
registry-has-no-mapper-class=48 / no-mapping-method=68, unpinned causes fail outright. B11:
PredatesThisProject exact pin 66 + ordinal horizon bound DWARF077 (never moves - history, not a ceiling) +
duplicate refusal; the "add it to the list" prose paths corrected. Sabotage demos all red-then-reverted:
DWARF999 (67>66), doctored NameConvention excuse (live+pin both red), NullCollections cells moved off
Projection (parity red exactly there; old endpoint-blind lookup green on that edit), pins swapped 47/69
(total unchanged 116 - the old gate's blind spot - red naming the cause). No new divergence collected, no
I-row: the one semantic surfaced (enum ByValue projects as a cast) is deliberate shipped behavior whose own
DWARF028 remedy text prescribes it. Census 866/866 unmoved; suite 7,758/0 foreground (baseline 7,756 + 2
new counted-population tests); build 0W/0E; RatchetInvariantScan + GateBandLogic 10/10. Queue: K0->K1->K2,
S1, W.
P6 addendum: the B11 horizon bound got its OWN red (per-guard rule) - DWARF002 swapped for DWARF999,
count unchanged at 66 (the exact pin blind spot), horizon fires naming the id; reverted, 5/5.
P6: DONE (25d9f02 B11, 68f4cf4 B3, 0fa6737 B6, 7ef471c flips, 849ce8a horizon demo). B11: 66 pinned +
ordinal horizon bound at DWARF077 (closes the same-commit-swap blind spot). B3: all NotApplicable rows
re-classified LIVE every run + pinned; FIRST RUN RETIRED A STALE EXCUSE (EnumStrategy ByValue emits a
translatable cast - measured Honoured, prescribed by DWARF028's own remedy; pin 8->7). CoversOption now
endpoint-exact. B6: NoSuchSite 116-with-band -> per-cause exact pins 48+68, unpinned cause fails outright.
5 sabotage demos incl. the offsetting-drift and swap cases. Census 866/866 unmoved; suite 7,758/0.
CONTROLLER ACCEPTS. LAYER 1 COMPLETE (P1-P6).
Queue: K0 (type-graph infra), then K1 (differential oracle, measured wall-clock before nightly entry),
K2 (metamorphic), S1, W-batch, wrap, merge. Then round 23.

## K0 — type-graph descriptor + generators + renderer + smoke + ratchet — DONE 2026-08-22
Commits: 636617f (the whole K0 infrastructure, one commit) · f60bdc3 (I5 filing).
- **Layout**: new `tests/DwarfMapper.CompilerTests/` per the plan (own project so SharpFuzz can later
  instrument in isolation); wired into the sln; coverlet.collector + house conventions (0W/0E under
  AnalysisMode=All); links `Shared/DeepTier.cs` and `Contracts/RepoPaths.cs` (linked-file reuse, no
  cross-test-project reference, no private repo-root walk — W6's rule respected on arrival); zero raw
  file writes (nothing for ARCH-06 to register). Pre-flight confirmed nothing pins the test-project
  count. 8 files: descriptor, gen, renderer, harness, smoke, corpus + tests, ratchet.
- **Validity rules (each a construction invariant + a `Validate()` check naming its false-positive
  class)**: V1 roots resolve/unique node names (CS0246/CS0101); V2 NestedRef DAG by index, j>i only
  (CS0523 family + the H7 termination variant for every graph walk); V3 value-type cycles checked
  INDEPENDENTLY of V2 per the audit's binding correction (CS0523; containment edge = Coll==None
  NestedRef between struct-kind nodes, NULLABLE INCLUDED — Nullable<T> contains T by value); V4
  per-node member-name dedupe (CS0102; names "M{node}_{k}" also remove base/derived CS0108 shadowing
  noise structurally); V5 BaseRef reference-kinds-only + same-kind + strict index order
  (CS0527/CS8864-5/CS0146); **V5b (NEW, derived by compiling the renderer — not in the audit's
  enumerated list)**: a base node must keep its implicit parameterless ctor, i.e. no CtorParam members
  (CS1729 on every derived declaration otherwise); V6 required×CtorParam discharged by construction
  (MemberShape is single-valued — the enum IS the enforcement, no dead check). The RFC's `Assemble`
  tuple-type sketch (syntax error) rewritten, not transcribed; population name-keyed per the audit.
- **Generators (CsCheck 4.7.0, central pin)**: house primitives only — `Gen.Enum<T>()` from the RFC
  sketch DOES NOT EXIST in 4.7.0 (advisor-predicted, build-confirmed); mirrored pairs re-roll node
  kinds AND member shapes independently (A11-F1/F2 generalized to both axes); ~15% BaseRef, ~25%
  NestedRef; `Validate()` runs as a post-condition on every sample.
- **P5 forward reference honoured**: `NodeSpec.SplitAcrossFiles` makes the partial-file shape
  EXPRESSIBLE; pinned corpus row `P5-K0-partial-split-struct-pair` asserts both file orders give the
  same verdict AND byte-identical generated source (emission-level restatement of the P5 seam kill);
  the BlittableProofCoverageTests comment flipped to a landed back-reference. Second deterministic row
  `A11-representation-mirror` (class→struct, the founding CS0037 family).
- **REAL FINDING on the FIRST 25-sample run → I5 (TASKS.md) + pinned corpus row**: collection-wrapped
  nullable elements whose SOURCE element is a user struct needing an element map emit
  `__DwarfMap_Obj_S_D(__item)` with `__item : S?` — silent CS1503, the EmittedInvalidCode genre.
  CsCheck-shrunk, then scoped by a 12-cell probe: ALL wrappers diverge (List/Array/IReadOnlyList/
  HashSet/Dictionary-value); class elements, non-nullable struct elements, and plain (non-collection)
  nullable struct members are fine; struct-source×class-dest diverges, class-source×struct-dest does
  not. Row `I5-nullable-struct-element-map` pins the divergence EXACTLY (KnownSilentCsIds=[CS1503],
  DeclaredDivergences discipline — red on the product fix, never a blanket skip); the matching
  sampled-space exclusion in TypeGraphGen is keyed to the pin and must die with it.
- **Enum-coverage ratchet** (3 tests): TypeKind + MemberShape vs the case-space fixture corpus —
  every string-literal C# fragment under Generator.Tests/Contracts, Roslyn-parsed (SurfaceFixtures'
  own "the fixtures are C# … answered exactly" method); CollShape vs the generator's OWN
  `TargetKind`/`DictTargetKind` enums read reflectively (CollectionCoverageSelfValidationTests
  precedent). Gaps DECLARED + exactly pinned BOTH directions: kind gap Enum (1, reasoned); shape gaps
  none (the scan's stale-row check deleted my speculative "Computed" row on its very first run —
  the instrument bit its author first); collection gaps 17 exactly enumerated (Queue, Stack, 6
  interface heads, 5 immutables, 4 dict heads) — supported = rolled ∪ declared, disjoint, stale rows
  red. Direction stated in-file: descriptor ⊇ case-space; Record/RecordStruct are pinned by the
  sampled space + K2's MR-3, not by the corpus.
- **DeepTier**: `CompilerGraphSmokeSeeds` (25, 250) + DeepTierSelfTests pin, same commit; rationale
  comment carries the measured figures. Verified live: fast prints "sampled 25", deep "sampled 250".
- **Sabotage demos (applied → red → reverted)**: Queue dropped from declared gaps → silent-bias red
  naming Queue; `TypeKind.Struct` renamed away → kind-coverage red naming Struct; I5 exclusion
  disabled → deep smoke red with CS1503 + replay seed printed (`1tnXwEx93WF7`). Plus the live first
  reds: the Computed stale row and the original I5 catch.
- **Measured**: whole solution 0W/0E (14.4 s); full suite **7,766/0** foreground (baseline 7,758 + 8)
  at 63.7 s and 63.0 s across two runs — new project ~1 s in-class fast tier, inside the ~10% cap;
  deep tier 250 samples ≈ 2 s in-class (deep multiplier costs ≈ +1 s wall; ~15–45 ms serial per
  sample, CsCheck-parallelized). Smoke split this session: 25/25 and 250/250 accepted, 0 refused —
  refusal grammar not yet reached by the sampled space (vacuity guard is on accepts, R4 keeps the
  split ungated; worth watching in K1). Census 866/866 untouched; RatchetInvariantScan +
  GateBandLogic + DeepTierSelf green; post-run tracked tree clean.
- **Coverage floors stand at K0 (measured, post-commit)**: `housekeeping -Coverage -SkipExhaustion
  -SkipAot` HOUSEKEEPING PASSED — suite 7,766/0 under collection; DwarfMapper 91.2/91.2 · Generator
  93.8/93.7 (+0.1, inside the R2 band — no raise demanded) · DocTooling 95.7/95.7 · CodeFixes
  92.4/92.4 · Testing 83.2/83.2. The full pipeline run before it also passed stages 1/1b/2
  (exhaustion 766/766, 10:35) and then broke at stage 3 (AOT) — **I6 FILED**: NETSDK1207 under the
  pinned SDK 10.0.101, reproduced identically at branch base `73c58c3` on the master checkout, so
  pre-existing and NOT round-22 work; the mechanism is the global-property flow ci.yml's own
  aot-trim-gate comment documents (housekeeping L312 passes `-p:PublishAot=true` which flows into the
  netstandard2.0 references; AotBench's csproj already sets the property, so the one-line fix is
  deleting the flag — maintainer's to bless).
K0: DONE (636617f infra, f60bdc3 I5, 5bc5cb7 I6). Exit held: the generator produces
valid-or-refused C# only (modulo the ONE declared, pinned, filed I5 exclusion), and the bias scan is
live and sabotage-proven. Handed to K1: the RefusedLoudly true-branch has never executed (0 refusals
across all sampling; K1's mandated hand-broken-emission sabotage demo exercises that seam), and the
audit's name-keyed correction is only half-discharged here (mirroring is name-keyed; the ORACLE
population half is K1's).
Queue: K1 (differential oracle over this corpus — the RefusedLoudly seam, PinnedCorpus row pipeline
and DeepTier knob are its ready-made legs), K2, S1, W-batch.
K0: DONE (636617f infra, f60bdc3 I5, 5bc5cb7 I6). New project tests/DwarfMapper.CompilerTests; 7 validity
rules as construction invariants (V5b base-parameterless-ctor DISCOVERED by compiling the renderer - beyond
the audit list); enum ratchet live (its stale-row check deleted a speculative row on first run; 1 kind + 17
collection gaps declared + exactly pinned); smoke 25 fast / 250 deep registered in DeepTier. REAL FINDING
I5: silent CS1503 - synthesized element maps don't lift Nullable<T> over user-struct SOURCE elements, all 5
wrappers; pinned corpus row (red-on-fix) + keyed sampled-space exclusion + TASKS row. RULING: I5 fix is a
round-23 product-layer task (emission change, needs sibling-hunt discipline; N4 precedent - record, fix in
own task). I6: pre-existing AOT-stage NETSDK1207 (repro'd at branch base; one-line candidate) - round-23
W-material. P5 forward-reference honored (SplitAcrossFiles corpus row). Suite 7,766/0, census 866/866,
floors green in-band. CONTROLLER ACCEPTS. Queue: K1 (differential oracle) -> K2 -> S1 -> W -> wrap/merge.

## K1 — differential oracle over generated graphs (R22-01) — DONE 2026-08-22
Commits: b86cd0d (the whole K1 infrastructure + pins, one commit) · 6608a63 (I7 filing).
- **Reuse vs added (the audit's boundary, held)**: the member-path deep-compare is the SHIPPED
  `[RoundTrip]` differ — `DwarfMapper.Testing.StructuralComparer.Diff` (+ `Render`), exactly what
  `RoundTrip.Verify` itself calls — via a new ProjectReference (dogfoods the Testing package; NOT
  `GraphOracleComparer.CrossTypeDiff`, which encodes AsEmpty internally and would bury the mandated
  explicit switch). What Testing genuinely lacked and K1 added, test-side in CompilerTests:
  (1) NAME-KEYED deterministic population (`ObjectFactory`/V2 are type/seed-keyed, not name-keyed —
  the audit's correction needs member-name-derived values so reorder/re-kind populate identically);
  (2) the deliberately naive copier itself; (3) the harness execution seam (`RunAndEmit` unique-name
  in-memory emit + load, default ALC; `InvokeMap` with TargetInvocationException unwrap). Differ depth
  arithmetic vs its SILENT MaxDepth=12 documented at the call site (max path 10 < 12 at maxNodes=4).
- **Oracle switch list — exactly one, doc-anchored**: `NullCollections = AsEmpty` (docs/options.md
  class-options row; probe-confirmed against emission: `if (src is null) return new List<…>()`). Its
  own deterministic regression pin (`Null_source_collection_maps_to_empty…`) proves the switch is
  load-bearing: hand-forced null collection → both sides empty, diff 0, and the AsNull answer
  DISAGREES. No other default semantics diverge from naive copy in the sampled space; every further
  mismatch is a finding by construction (the discipline is stated in the oracle header).
- **Leg 1 + the refusal seam**: sampled leg re-asserts must-compile-or-refuse on the emit path; the
  `RefusedLoudly` true-branch (never executed through K0) now has a deterministic executor — corpus row
  `K1-refusal-unmapped-dest-member` via the new `ExpectedRefusalIds` contract (DWARF001, error severity,
  docs/diagnostics.md#dwarf001; refusal rows deliberately do NOT assert CompilationErrors empty — an
  unimplemented partial method leaves CS8795-family errors, advisor-predicted, run-confirmed), plus a
  corpus-sweep exclusivity guard (no row pins both silent-CS and refusal). Sabotage demo (applied →
  red → reverted): appended broken unit → leg 1 red CS0246 with replay seed `1DQo-O7DyQ6a`, 4 shrinks.
- **REAL FINDING on the FIRST 1,000-sample deep run → I7 (TASKS.md) + pinned rows + keyed exclusion**:
  seed `0vihQF5Vee7b` — generated map THREW "Source member 'M0_0' was null" on a plain nullable nested
  member across a RE-KINDED pair. Minimized by a 6-cell kind-pair probe: Struct→Struct and Class→Class
  lift null→null; Struct→Class / Struct→Record / RecordStruct→Class emit `?? throw`; Class→Struct
  throws "Cannot map a null … to value-type …" — every throwing case has a nullable-capable dest and
  the lossless NullableProject emission sits next door (MapEmitter, gated on both sides Nullable<T>).
  UNDOCUMENTED (NullStrategy's sentence covers non-nullable targets only) → classified
  product-shaped/documentation-shaped, oracle NOT taught: rows `I7-nullable-rekind-value-to-reference`
  / `-reference-to-value` (normal compile contract) + exact-message runtime pins (red on fix); the
  sampled-space exclusion in `TypeGraphGen.Assemble` (plain member, value-kind source × reference-kind
  dest) is keyed to the pins and dies with them. The reverse genre is sampling-unreachable only via the
  oracle's DECLARED population bias (reference members never null — bias list in the oracle header,
  I7-cross-referenced); its pin is the deterministic executor. I5 exclusion inherited unchanged, keyed.
- **DeepTier**: `CompilerOracleSeeds` (20, 1000) + DeepTierSelfTests pin, same commit; measured BEFORE
  the catalog entry per the round-21 rule: oracle theory 1000/1000 in ~10 s in-class; CompilerTests
  project deep wall 16.2–16.5 s across 3 runs (12-core reference machine); fast 20 ≈ 2 s in-class.
  10,000 stays a knob value only — unmeasured, therefore unused. Deep re-runs post-I7: 3 × green,
  1000/1000 executed, 0 refused (split reported by the R4-ungated vacuity guard, executed > 0).
- **Measured (gates)**: whole solution 0W/0E; full suite **7,776/0** foreground (baseline 7,766 + 10:
  3 corpus-sweep rows + exclusivity + refusal executes in-sweep + oracle sampled + null-collection pin
  + 2 accepted-row anchors + 2 I7 runtime pins). Fast-tier cap: same-session A/B (K1 stashed → K0
  baseline measured on the same machine): baseline 73.0/85.1 s vs K1 75.0/75.1/78.2 s — the delta is
  inside run-to-run variance (K0's ledger 63 s figures were a quieter session; the plan's own
  round-start state records 63–75 s), cap held. Census 866/866 untouched (matrix tests green in-suite);
  RatchetInvariantScan + GateBandLogic + DeepTierSelf + ARCH-06 green in-suite (no raw file writes
  added — all emission in-memory). Post-run tracked tree clean.
K1: DONE (b86cd0d infra+pins, 6608a63 I7). Exit held: both legs live behind the knob with recorded
wall-clocks; the corpus-row pipeline demonstrated END-TO-END on a real first-run finding (shrink →
6-cell probe → classify undocumented → pin exact behaviour → keyed exclusion → I-row), zero silent
exclusions, zero silently-taught oracle semantics. Handed to K2: the name-keyed population half of the
audit's correction now EXISTS (`ReflectionOracle.Populate`) — MR-1's reorder relation can consume it
directly; the refusal seam and I7's cross-kind table are exactly MR-3's re-kinding minefield map.
Queue: K2 (metamorphic relations) → S1 → W-batch.
K1: DONE (b86cd0d infra+pins, 6608a63 I7). Testing's StructuralComparer REUSED (dogfood); added only what
it lacked (name-keyed population, naive copier, RunAndEmit/InvokeMap seam). Oracle switch list = exactly 1,
doc-anchored (NullCollections=AsEmpty), load-bearing-proven. RefusedLoudly branch FIRST-EVER execution
pinned (K1-refusal-unmapped-dest-member, DWARF001). Counts: fast 20 (~2s), deep 1000 (~10s in-class,
16.2-16.5s project-alone - the sanctioned isolated number). FINDING I7: null across RE-KINDED nested pair
throws instead of lifting - kind-inconsistent (S->S lifts, S->C/S->R/RS->C throw, C->S different throw),
every throwing dest nullable-capable, NullableProject emission exists; UNDOCUMENTED (NullStrategy sentence
covers non-nullable targets only); 2 corpus rows red-on-fix + exclusion keyed + I7 row. Oracle NOT taught -
correct. Suite 7,776/0 x3; census 866/866; fast cap held by same-session A/B. CONTROLLER ACCEPTS.
I5+I7 are now round-23's product-layer core (with I4/I6 tooling items). Queue: K2 -> S1 -> W -> wrap/merge.

## K2 — metamorphic relations (R22-02) — DONE (dbc96db)

- **Files**: `tests/DwarfMapper.CompilerTests/MetamorphicTests.cs` (the three relations),
  `ResultFingerprint.cs` (the order-independent member-path/value fingerprint), DeepTier catalog +
  DeepTierSelfTests pins (3 new populations, same commit per the T4 rule).
- **The outcome seam**: every variant reduces to one comparable string — `REFUSED:` + sorted error ids
  (refusal parity IS part of each relation), `THREW:` + type/message (throw parity is relation material —
  the I7 genre; only InvalidOperationException, the product's deliberate-throw family, is caught; anything
  else propagates loud; ABSOLUTE no-throw coverage stays K1's oracle leg's job, stated in-code), or the
  fingerprint of the executed map. **Silent generators + CS errors is NEVER an outcome** — immediate
  K1-leg-1-shaped red, so a relation cannot go green over two identical silent miscompilations
  (advisor-flagged trap, discharged by construction and then EMPIRICALLY by the MR-3 sabotage).
- **ResultFingerprint vs the shipped StructuralComparer** — reuse REJECTED for three measured reasons,
  defended in the file header per house rule: (1) cross-assembly — the variants live in different emitted
  assemblies whose same-named types are distinct runtime types, and StructuralComparer reads one side's
  PropertyInfo off the other (TargetException); (2) its MaxDepth=12 returns SILENTLY (vacuity hazard for a
  relation gate) — the fingerprint walker throws loudly at 32; (3) HashSet enumeration order is
  bucket-layout dependent (identity-hash vs value-hash flips between kinds) — element blocks are sorted.
  K1 keeps using StructuralComparer (one assembly, diff wanted); no parallel comparer grew for that case.
- **MR-1** (order): reversal of every node's member list — the strongest single deterministic permutation;
  ctor parameter order rides along, so the product's name-based ctor matching is under test. Name-keyed
  population (K1's `ReflectionOracle.Populate`) is what makes the relation compare mappings, per the audit.
- **MR-2** (unmapped neutrality): injected `X{i}` int AutoProp into every node of the dest-reachable
  COMPLEMENT (BFS over NestedRef+BaseRef; injecting into a dest-reachable node would change the
  completeness obligation itself — a different experiment, stated in-code). Base config pinned EXECUTABLY:
  Outcome() asserts bare `[DwarfMapper]` present AND `[DwarfMapper(` absent in unit 0, so a renderer that
  ever grows options trips the pin before the relation can mis-blame the product. Collision with V4 naming
  impossible by construction (M-prefix vs X-prefix, asserted defensively; renderer Validate V4 is the
  belt). Empirical premise confirmed: an unmatched source member under bare config produces NO
  error-severity diagnostic (500 sampled pairs, 0 refusals).
- **MR-3** (representation): uniform re-kind of ALL nodes per variant → every nested pair is same-kind, so
  the pinned I7 cross-kind divergence cannot fire by design (population/input management, not shape
  exclusion). Re-kind set: Class + Record UNCONDITIONAL (set always ≥ 2 — never vacuous per-graph);
  RecordStruct drops out exactly under inheritance with the reason in-code (V5/CS0527 — genuinely
  inexpressible, the plan's sanctioned shrink). Every re-kinded spec re-runs `Validate()` — the audit's
  "reuse K0's V3/V5" guard discharged literally (V3 survives any future V2 relaxation). Nested-member
  nullability cleared with THREE load-bearing reasons declared in-code: I5 (struct re-kind of a nullable
  collection element is the pinned silent CS1503), population parity (Nullable<T> nulls ~25% while the
  reference-member bias never nulls — the variants would receive different inputs and the relation would
  compare populations, not mappings), and the I7 family. A DECLARED exclusion keyed to the I5/I7 pins —
  dies with them. Scalar nullability stays (kind-invariant population).
- **DeepTier (measured BEFORE catalog entry, 12-core reference machine, 2026-08-22)**:
  `CompilerMrMemberOrderSeeds` (10, 250) — fast ≈2 s in-class (pays warmup), deep ≈8 s;
  `CompilerMrUnmappedMemberSeeds` (10, 250) — fast ≈0.3 s, deep ≈14 s;
  `CompilerMrRekindSeeds` (8, 150 — up to 3 emits/iteration prices out near MR-1's 250 pairs) — fast
  ≈0.25 s, deep ≈4 s. **Project-alone deep wall 30.4 / 31.7 / 30.7 s across 3 runs** (isolated number,
  stated as such; K1-era baseline 16.2–16.5 s → K2 adds ~14 s deep). Deep 3× green: MR-1 250/250,
  MR-2 250/250, MR-3 150/150 executed, 0 refused (splits reported by the R4-ungated vacuity guards).
- **Divergences found: ZERO.** All three relations held on every deep run — no corpus rows, no I-rows, no
  exclusions added (and none needed: zero silent exclusions stands).
- **Sabotage demos (applied → red with replay seed → reverted, one per relation)**: MR-1 —
  declaration-order index leaked into the fingerprint path → red seed `0000dKt0Up09`, 2 shrinks; MR-2 —
  injected name doctored to `M{i}_0` → the defensive collision assert red at seed `0000dKTzOSw1` (front
  guard fires before V4's belt); MR-3 — `ClearNestedNullability` neutered → the RecordStruct re-kind walks
  into pinned I5, red as **silent-CS1503 immediate red** (not a fingerprint match!) at seed `0000dLTg9ye4`
  — proving both that the normalization is load-bearing AND that silent miscompiles cannot be laundered
  through outcome equality. (A CS0162+IDE0051-clean neutering was needed — warnings-as-errors rejected the
  naive call-site removal, which is itself the posture working.)
- **Measured (gates)**: whole solution 0W/0E; full suite **7,779/0** foreground (baseline 7,776 + the 3
  relations), wall 69.2 s — fast cap held (session baseline ~75 s; the MR fast-tier cost is ~2.6 s
  in-class, well inside the ~10% discipline); census 866/866 untouched; DeepTierSelfTests (registration +
  pin + call-site + deep>fast) green in-suite; ARCH-06 clean (all emission in-memory, no file writes);
  post-run tracked tree clean.
K2: DONE (dbc96db). Exit held: three relations live in the deep tier with recorded wall-clocks; sabotage
demo per relation; zero violations, zero silent exclusions. Queue: S1 → W-batch.
K2 post-commit verification: `git grep SABOTAGE dbc96db -- tests/` empty (no sabotage residue committed);
one deep run AT the committed tree 21/21 green (30 s in-project) — the deep-green claim holds at dbc96db.
K2: DONE (dbc96db). MR-1/2/3 all HOLD - zero divergences, zero exclusions added. StructuralComparer reuse
rejected for 3 measured reasons (cross-assembly TargetException, silent MaxDepth=12 vacuity hazard, HashSet
bucket-order) - own order-independent fingerprint with loud depth-32 throw. MR-2 base config pinned
EXECUTABLY (bare [DwarfMapper] asserted in rendered unit). MR-3 uniform re-kind = I7 cannot fire by design;
nullability exclusion keyed to I5/I7 pins with 3 declared reasons. Sabotage x3 with seeds - incl. proof
that silent miscompiles red immediately, never fingerprint-matched. Deep project-alone 30.4-31.7s. Suite
7,779/0 (69.2s, cap held). CONTROLLER ACCEPTS. K-ARC COMPLETE: infra + I5 + I7 + first RefusedLoudly
execution + 3 standing invariants. Queue: S1 (NuGetAudit + lock files + locked-mode), then W-batch, wrap,
merge, round 23.

## S1 — supply-chain gate: NuGetAudit `all` + lock files + locked-mode — DONE 2026-08-22
Commits: b45df25 (props + RID declarations + all 22 lock files, one atomic commit — a commit carrying
RestorePackagesWithLockFile without its lock files would fail its own locked-mode restore, breaking
bisectability) · 7a7aacb (ci.yml + housekeeping stage 0).
- **Audit gate**: `NuGetAudit=true` + `NuGetAuditMode=all` + `NuGetAuditLevel=low` in Directory.Build.props
  (explicit even though the pinned SDK defaults mode to `all` — pins the gate against SDK-default drift,
  the same reason the SDK is pinned). Restore honors the existing TreatWarningsAsErrors, so NU1901–NU1904
  are restore ERRORS; NU1900/NU1905 stay errors too, deliberately — an audit that silently could not fetch
  its database is a vacuous green (Assert-MutantsWereTested's genre). No WarningsNotAsErrors exception was
  needed anywhere.
- **Today's advisory findings (2026-08-22, nuget.org vulnerability DB base 2026-08-20 + update
  2026-08-21)**: exactly ONE fires — **NU1903 High, AutoMapper 14.0.0, GHSA-rvv3-g6hj-g44x**, affected
  ranges `(, 15.1.1)` and `[16.0.0, 16.1.1)`, in the three AutoMapper-referencing projects (Benchmarks,
  DifferentialTests, ConsumerTests.CleanCorpus). Disposition: ALREADY DISPOSITIONED in-repo — each of the
  three csprojs carries a pre-existing, maintainer-merged, per-project `NoWarn NU1903` with the written
  reason (benchmark/test-only, non-shipped, no untrusted input; AutoMapper is pinned to 14.0.0 as the last
  MIT release because v15+ is RPL-1.5). The fix versions (15.1.1 / 16.1.1) are BOTH license-blocked, so no
  version bump exists at any patch level — a live advisory needing a non-trivial (here: impossible) bump
  is the plan's own I-row example, so **I8 FILED** (TASKS.md) recording the advisory, the license block,
  and the standing suppressions as the disposition (no action demanded — the I3 record-only precedent).
  Verified the audit has
  teeth behind it: suppression stripped → restore FAILS exit 1 "NU1903: warning as error … known high
  severity vulnerability" → reverted (sabotage demo A). `dotnet list package --vulnerable
  --include-transitive` over all 22 projects: no other advisory, direct or transitive.
- **Lock files**: `RestorePackagesWithLockFile=true` solution-wide → **22 packages.lock.json committed**
  (every project in the sln). Floating-version check: NONE anywhere — CPM versions all exact, no wildcard
  in any csproj/props, no `*` in any lock file; CentralPackageTransitivePinningEnabled interacts cleanly
  (locked `--force` restore passed twice back-to-back with zero lock churn — the historical
  transitive-pinning NU1004/churn failure mode did not manifest).
- **RID-graph finding (probe before trust)**: the AOT publishes (`ci.yml` aot-trim-gate `-r linux-x64|
  win-x64`; housekeeping stage 3) re-evaluate the restore of the WHOLE P2P closure under the global RID,
  and a lock file without that RID graph fails NU1004. Fix: `<RuntimeIdentifiers>linux-x64;win-x64</>` on
  the five closure projects (DwarfMapper, Generator, CodeFixes, AotSample, AotBench — restore-graph
  metadata only, nothing shipped changes), lock files regenerated with both RID graphs. Probed
  publish-shaped (`-t:Restore -p:RuntimeIdentifier=$rid -p:RestoreLockedMode=true`): all four
  project×RID combinations green — AND the full CI-shaped publish itself
  (`dotnet publish AotSample -c Release -r win-x64 -warnaserror -p:RestoreLockedMode=true`): restore green
  with 0 NU1004, ILCompiler pinned in both AOT lock files, managed compile + native codegen ran; the run
  then failed at the native LINK step on this machine's toolchain (vswhere.exe not on PATH — local MSVC
  environment, unrelated to lock files; CI's windows-latest carries VS Build Tools). The unexercised CI
  gap is therefore linux-x64-only at the link layer; its restore layer is probed green.
  NOTE: `dotnet restore -r` (CLI restore flag) REPLACES the RID set and
  still NU1004s — CI never invokes that shape; recorded so nobody "fixes" it.
- **Locked-mode proof**: local `dotnet restore DwarfMapper.NET.sln --force --locked-mode` green (×4 across
  the session, incl. twice back-to-back with a churn check). CI: explicit `--locked-mode` on build-test's
  and deep-test's restore steps + `RestoreLockedMode` on `'$(CI)' == 'true'` in Directory.Build.props so
  every IMPLICIT CI restore (surface-matrix, Stryker legs' builds, codeql, CycloneDX, both AOT publishes,
  conformance's dotnet run) is locked without per-job flags. roslyn-forward-compat inherits it and runs an
  unpinned newer SDK — a comment in that job records the diagnosis if SDK prune-data drift ever NU1004s it
  (that leg red + build-test green = SDK-driven graph drift, which is forward-compat information, not
  noise). **ci.yml changes remain UNEXERCISED until the maintainer's push (workflow OAuth scope)** — the
  local publish-shaped probes above are the pre-push evidence.
- **Housekeeping**: new stage 0 — explicit `dotnet restore DwarfMapper.NET.sln --locked-mode` before any
  build stage, loud throw on failure; run live: `HOUSEKEEPING PASSED` (-SkipExhaustion -SkipAot) with
  stage 0 green and suite 7,779/0. The ordinary local inner loop stays unlocked on purpose (regenerate
  with force-evaluate on intended changes — documented in the props comment and the stage comment).
- **Sabotage demo B (lock drift)**: CsCheck 4.7.0→4.7.1 in Directory.Packages.props WITHOUT regenerating →
  `--locked-mode` restore FAILS exit 1, NU1004 naming CsCheck's changed version → reverted, locked restore
  green again, zero lock files modified after revert.
- **Restore-time delta (measured)**: forced unlocked restore ~2.0 s pre-lock-files; forced locked restore
  ~1.5 s (locked mode skips resolution — a speedup, not a cost; warm GPF both sides).
- **Verification**: whole-solution build 0W/0E (26.7 s); full suite **7,779/0** foreground (69.2 s) —
  baseline held EXACTLY (S1 adds no tests); census 866/866 untouched; RatchetInvariantScan + GateBandLogic
  + ARCH-06 green in-suite; post-run tracked tree clean except intended edits.
S1: DONE (b45df25 audit+locks, 7a7aacb wiring, e458e75 I8). Audit live at error severity (mode all, level
low); 22 lock files committed; CI restore locked-mode (unexercised until push, probed locally up to the
full CI-shaped win-x64 publish restore); housekeeping stage 0 agrees with CI; one live advisory
(AutoMapper NU1903, High) reason-suppressed in-repo with gate-teeth verified, bump license-blocked at
every level — I8 FILED record-only. Queue: W-batch (W1–W7), wrap, merge.
S1: DONE (b45df25 audit+22 lock files atomic, 7a7aacb CI/housekeeping wiring, e458e75 I8). NuGetAudit
true/all/low explicit (pins against SDK-default drift); NU1900/1905 kept as errors (unfetchable advisory DB
= vacuous green). ONE live advisory: NU1903 High AutoMapper 14.0.0 in 3 non-shipped competitor projects, both
fix versions RPL-licensed = license-blocked; existing reasoned suppressions VERIFIED to have teeth
(stripped -> restore fails); I8 records it. 22 lock files, zero floating versions anywhere; locked-mode
green x5 local + housekeeping stage 0 + CI wiring (unexercised until push). Found+fixed en route: AOT -r
publishes needed RuntimeIdentifiers on 5 closure projects (NU1004). Sabotage: CsCheck bump w/o regen ->
NU1004 fail. Suite 7,779/0 exactly. CONTROLLER ACCEPTS.
W1-W6 agent #1 DIED mid-W1 (Fable 5 limit, not a work failure) leaving uncommitted UNVERIFIED work: DWARF095
(unscoped ignore no match) + apparent five-file sync + generator changes + 2 new test files. Session model
switched to Opus 5. Agent #2 dispatched with assessment-first mandate: judge the inherited diff on merits
(build+suite), then finish / complete / revert-and-redo - explicitly forbidden from committing unverified
inherited work or assuming the predecessor was right.

## W1 — the silent-discard pair: B15 + B20 (+ B21 decided and pinned) — DONE 2026-08-22
Commits: 390936d (product + five-file sync + tests) · records commit below.
**Verdict on the inherited work (agent #1's uncommitted diff): SOUND BUT INCOMPLETE — completed, not
redone.** Evidence: whole-solution build 0W/0E with samples; full suite green as inherited (7,796/0);
census 866/866; the five-file sync is complete for both ids on the family's own precedent (the generated
index covers the DWARF0xx family only, and the DWARFR family has never had a NegativeCase — DWARFR12 is
pinned in RegistryDiagnosticsGenTests like its ten siblings); tests pin id AND remedy wording (both scope
wordings EXPECT-MESSAGE'd in the NegativeCase, the B21 ruling pinned in both directions). Two gaps closed
before committing: (1) the stand-down flag `classIgnoreLivenessBlinded` has TWO set-sites and only the
top-level-collection one was pinned — the `[MapDerivedType]` dispatch sibling is now pinned too (round 20's
"the guard did not propagate" rule); (2) the class-site judge's completeness rests on nested pairs NOT
consuming class-level unscoped ignores — verified at the source (`ResolveMembers` takes `nestedIgnores`,
the pair-scoped set, at MapperExtractor.cs:1598) and now pinned by a test that goes red if that wiring
changes, so a future rewire cannot turn the guard into a false "names nothing".
- **Decision (one policy, both halves).** B20 → **DWARF095** (Warning): unscoped `[MapIgnore("Name")]`
  matching no destination member anywhere it is read. Method-site judged against that method's own
  destination; class-site is class-WIDE and judged against every pair the class maps (a class ignore about
  one of two pairs stays legitimately silent on the other); an element-pair match stays DWARF090's report;
  the class-site verdict stands down entirely on `[MapDerivedType]` dispatch and top-level collection maps.
  Liveness set = writable ∪ read-only members (an ignore on a read-only member is the remedy DWARF007
  itself prescribes). B15 → **DWARFR12** (Warning): the registry's member-form `[MapIgnore]` keeps its
  behaviour, the discarded argument is reported. B21 → directive names bind **ordinally under every
  option**; `CaseInsensitive` fuzzes auto-matching only.
- **Reliance check (B20's own demand), answered both ways**: whole-solution build with samples under
  warnings-as-errors is 0 warnings, so nothing relies on a dead `[MapIgnore]`; and inverting DWARFR12's
  guard fails the build on `samples/DwarfMapper.Gallery/16_MapToRegistry.cs:36`, proving the Gallery's bare
  `[MapIgnore]` is live and correctly unflagged.
- **Sabotage demos** (each applied → red → reverted): `IgnoreNameComparer` → `OrdinalIgnoreCase` → the B21
  pin red ("Filter not matched", collection empty); one word of the DWARF095 message ("no" → "NO") →
  NegativeCases wording pin red naming the exact missing fragment; DWARFR12's trigger inverted
  (`ArgumentCount != 0` → `== 0`) → whole-solution build red at the Gallery.
- **Measured**: build `--no-incremental` 0W/0E **33.0 s**; full suite **7,798 / 0** foreground (baseline
  7,779; +19 = 14 UnscopedIgnoreNoMatchTests + 2 RegistryDiagnosticsGenTests + 3 NegativeCases, ~1 s of
  Generator.Tests' own time — fast-tier cap untouched); surface-matrix census **866/866** green, no
  population or ceiling moved. Tracked tree clean after the run.
W1: DONE (390936d). B15/B20/B21 flipped with numbers. Two new ids minted this round: DWARF095, DWARFR12 —
Scan9 accepts them from the single CHANGELOG bullet that states the one decision. Queue: W2 (B25+B26),
W3 (B30), W4 (B23), W5 (B4/B5/B8/B10/B12/B13/B14), W6 (C5 + B19-doc + stale-status sweep).

## W2 — matrix measurement integrity: B25 + B26 — DONE 2026-08-22
Commits: b53e6f8 (both rows, one commit — the two are one "cells that look measured and are not" theme
and neither moved a population) · records commit below (B37 + I9 + hashes).
- **B25 — the probe.** `MapValueAttribute<TTarget>` had NO `[DwarfSurfaceProbe]` at all (the row's text
  saying it "declares Arguments = {Id}, \"probe\"" is imprecise — the arguments were SYNTHESIZED by
  `SurfaceCatalog.SampleArgument`, which rotates "Id"/"Name" by position+variant and renders `"probe"` for
  an `object` parameter, so the generic form got `("Id", "probe")` = string into int). Added the same two
  declarations the arity-0 twin carries. **Measured before → after, all 28 cells** (dumped live via a
  throwaway `SurfaceProbe.Classify` harness, deleted after): CreateMap/SpanMap/AsyncStream/CoLocatedHost
  `Refused — DWARF040,DWARF064 (Info) (behind CS8795)` → `Refused — DWARF064 (Info)`; UpdateInto/Projection
  `Refused — DWARF056 (Warning)` unchanged; Registry `NoSuchSite` unchanged. The type error is gone; what
  remains is the shadow-Info the NON-generic twin also draws on this fixture (`Name` exists on both sides
  of the flat pair), i.e. the generic form now reads exactly like its precedent.
- **Populations re-measured, none moved** (forced by temporarily raising each ceiling and reading the
  "well under the ceiling" message): Unaskable **44**, StructurallyExcused **13**, UnhonouredButLoud **14**
  — identical before and after; NotCompilable and EmittedInvalidCode exact-pinned **0**, green; matrix
  **866/866**.
- **B25's second half, examined not assumed.** `MatchPairValues(pairValues, …)` is consulted at exactly
  three sites — create-map `MapperExtractor.cs:1077`, `[GenerateMap]` pair `:1417`, nested pair `:1607` —
  and at neither the update-into nor the projection branch. So the DWARF056 at those two endpoints is not
  a matching subtlety: the pair IS mapped and the directive is not read, while the message says "matches no
  mapped pair". Two candidate remedies (thread it, or re-attribute to DWARF092 as `[FlattenGraph]` already
  reads there), both with consequences the round's budget cannot measure → **I9 FILED**, not guessed.
- **B26 — the trigger.** `ResolveFlattenInfos` now returns `NullableHop` per root and reports nothing;
  `ReportUnguardedFlattenHops` fires `DWARF044` at the END of the resolver's walk for roots a destination
  member actually pulled a leaf up from — the same shape and reason as the `DWARF070` report immediately
  above it. Once per root, ordered by root name (determinism). Projection unaffected by construction
  (`warnNullableHop: false` → the verdict is always false there).
- **Pinned both directions**: a nullable root whose leaves land nowhere reports nothing AND emits no
  `Address.` access to be unguarded; two nullable roots, one landing a leaf → exactly that one reported.
  **Sabotage**: drop `consumedRoots.Contains(fi.Root)` from the filter → both new tests red, reverted →
  6/6 green.
- **Sibling hunt**: the other two `PathNullableHop` sites (`Members.cs:283`, `:889`) are dotted
  `[MapProperty]` source paths — the directive names its target explicitly and a failure to land is
  separately loud (DWARF001/conversion refusal), a different genre from a whole directive with no effect.
  Left alone, deliberately.
- **No matrix cell moved for B26 either**: the `[Flatten]` probe fixture's leaves DO land, so its
  CreateMap/UpdateInto/Projection cells read `Honoured — output differs` before and after. What the fix
  removes is the mechanism that made D10's evidence false, not a current false reading.
- **Found en route, filed not fixed**: **B37** — a declared `Arguments` list collapses the ×2 multiplicity
  axis to two identical applications (`ArgumentsFor` ignores `variant`), the exact degeneracy
  `SampleArgument`'s own doc comment says the variant dimension exists to prevent. Inherited from the
  arity-0 precedent, affects eight elements, not maintainer-blocked, wants its own re-measurement.
- **Measured**: whole-solution build 0W/0E; full suite **7,800 / 0** foreground; matrix **866/866**.
W2: DONE (b53e6f8). B25/B26 flipped with before→after numbers; I9 + B37 filed. Queue: W3 (B30), W4 (B23),
W5 (small guards), W6 (records).

## W3 — `SynthNested` parameterless-ctor guard: B30 — DONE 2026-08-22
Commit: 60d8d8c (+ records commit below).
- **Chosen shape: reuse `DWARFR09`, generalize its wording — NOT a new `DWARFR13`.** Reasoning recorded in
  the descriptor comment and the commit: the defect IS a sibling guard that did not propagate, so answering
  it with a second id would restate the same refusal in different words and re-open the asymmetry. The row's
  hesitation (the remedy sentence reads differently when the type at fault is not the annotated one) is a
  WORDING problem, and `DWARFR11` — minted with both call sites from day one — already shows the fix. Title
  `[MapTo] target has no accessible parameterless constructor` → `A type the [MapTo] registry constructs has
  no accessible parameterless constructor`; `MessageFormat` → `"{0}"`, composed per site. No new id minted,
  so no Scan9 obligation; AnalyzerReleases.Unshipped + docs/diagnostics.md updated for the title change
  (the generated index carries DWARF0xx only).
- **Sibling sweep of the SHAPE**: the front door constructs exactly two kinds of type. `TryCollection` emits
  only `new List<T>(...)` and `.ToArray()` — neither can lack a parameterless ctor — and routes its element
  through `Resolve`, so the guard at `SynthNested` covers nested members and collection elements with one
  call. There is no third construction site and no dictionary path in this generator.
- **Found and fixed alongside, because the new refusal inherited it**: a loud nested refusal also drew
  `DWARFR05` "the source and destination member types are incompatible" — false, and the house rule against
  "two diagnostics about one member, one of them a lie" is already stated in this file for `DWARFR02`.
  `Resolver.RefusalReported` (cleared immediately before every `Resolve`, set by both loud refusals) makes
  the caller stay silent when the null is already explained. This also ends a PRE-EXISTING cascade: a
  recursive nesting drew one false `DWARFR05` per nesting level (measured: 2 for a 1-deep cycle); now only
  `DWARFR06`. A genuine no-conversion on another member still reports — pinned.
- **Pins (6 new tests)**: ctor-only nested member → DWARFR09 naming type + member, nothing emitted, no `?`
  in the type name; ctor-only collection element → same; constructible nested type → still emitted, no
  diagnostic (over-reach guard); no DWARFR05 companion at either loud refusal; genuine no-conversion still
  reports after a refusal on another member.
- **Sabotage**: guard disabled → 3 red, one of them the emission assertion proving `new global::Demo.LeafDto`
  reaches the compiler unrefused (the CS1729 shape B30 describes); reverted → 43/43 green.
- **Measured**: whole-solution build `--no-incremental` **0W/0E** (samples included — no sample carries a
  ctor-only nested registry type, so the refusal is not over-eager); full suite **7,806 / 0** foreground.
W3: DONE (60d8d8c). B30 flipped. Queue: W4 (B23 renderer whitespace), W5, W6.
SESSION RESTART (2026-08-22): the W1-W6 agent and both watchdogs were lost mid-W3. NOTHING LOST - W1
(390936d+6cbc902), W2 (b53e6f8+e79f99d), W3 (60d8d8c+adbe9a3) all landed committed and verified before the
restart; tree clean. W1 verdict on the inherited dead-agent work: SOUND - DWARF095 stands. W2 found two more
cells that "looked measured and were not" -> filed B37 + I9. W3 (B30) ruled ONE id not a new one (DWARFR09
retitled to name the registry, MessageFormat "{0}" composed per site - DWARFR11's precedent), swept the
siblings (registry constructs exactly two kinds; no third site), and fixed a PRE-EXISTING false cascade
found alongside (DWARFR05 companion suppressed via Resolver.RefusalReported - ends one false report per
nesting level). Suite 7,806/0 at W3.
Re-dispatched W4-W6 (fresh agent) + re-armed watchdog. Then W7, wrap, merge, round 23, shutdown.

## W4 — the renderer whitespace fix: B23 — DONE 2026-08-22
Commit: `072c7ca`.
- **The fix is the one argument the row names**: `XDocument.Load(xmlPath, LoadOptions.PreserveWhitespace)`.
  Default `LoadOptions.None` discards whitespace-ONLY text nodes, so a doc comment separating two inline
  elements by nothing but a space lost it before `Flatten` ever ran, and `Flatten`'s own whitespace collapse
  cannot restore what the loader dropped.
- **FULL REGENERATED DIFF, reviewed hunk by hunk — 4 hunks, all explained, all a single space INSERTED**:
  L86 `DWARF038build error` -> `DWARF038 build error` (DwarfMapperAttribute.cs:251, `<c>DWARF038</c>
  <b>build error</b>`); L91 `System.Text.JsonIgnoreCycles` -> `System.Text.Json IgnoreCycles`
  (DwarfMapperAttribute.cs:231); L281 `constructedfrom the class` -> `constructed from the class`
  (PairScopedAttributes.cs:94, `<b>constructed</b> <b>from the class</b>`); L442
  `System.Text.JsonReferenceHandler...` -> `System.Text.Json ReferenceHandler...` (OnCycleStrategy.cs:28).
  **Completeness argument, not a spot check**: grepping for two adjacent inline tags separated by spaces over
  the rendered assembly's public doc comments returns exactly FOUR sites, and the diff has exactly four
  hunks. No word removed, none reordered. `diagnostics-index.md` and `option-support-matrix.md` do not go
  through `XDocument` and did not move — confirmed by running all three doc tests; only the API one failed.
- **Sibling hunt**: `XDocument.Load|Parse` / `XElement.Load|Parse` over `src` + `tests` + `scripts` has
  exactly ONE call site, the one fixed. Nothing to fold, nothing to file.
- **A non-self-referential pin WAS warranted and was added.** The row's filing reason is that no test could
  catch this: `GeneratedDocsAreCurrentTests` compares the committed page against the renderer's own current
  output, so a renderer that deletes characters is self-consistent and green forever.
  `ApiReferenceRendererTests` compares against a HAND-WRITTEN expectation and drives the real file-load path
  through a new `internal ParseSummaries(string)` seam (IVT to Generator.Tests already existed). The design
  point: an in-memory `XElement` fed to `Flatten` would have passed BEFORE the fix too — the defect was in
  the LOAD, not in the flattening. ARCH-06: the fixture's one raw write is temp-dir only and registered with
  its reason.
- **Sabotage**: load option removed -> `Expected: "A DWARF038 build error..." / Actual: "A DWARF038build
  error..."`. Restored -> green.
- **Measured**: whole solution 0W/0E with samples; suite **7,808 / 0** foreground (7,806 + 2 new pins).

## W5 — the small-guards batch: B4 B5 B8 B10 B12 B13 B14 — DONE 2026-08-22
Commit: `77d746e` (one commit; no ceiling moved, no product behaviour changed, each guard names its row).
- **B4** — `FlattenGraph_one_source_into_two_different_collections_is_accepted`. Sabotage: re-key DWARF087
  on the source (`seenTargets.Add(srcNavName)`) -> red. The pre-existing control passes under EITHER keying
  because both its source and its destination differ; this one does not.
- **B5** — new `Contracts/SurfaceFixtureBaselineTests.cs`. **In-task scope ruling, the one real judgement of
  the batch**: measured at `CreateMap` only. Run across all seven endpoints the check fires on TEN fixtures,
  not four — five carry `DWARF028`+CS8795 at `Projection`, `recursive-graph` adds CS7036 at
  `SpanMap`/`AsyncStream`. Those are properties of the ENDPOINT, not of the fixture; they are already
  ratcheted by `The_cells_that_pass_both_claim_branches_are_counted`, whose own doc comment blesses them as
  honest and permanent; and gating them again would need a per-endpoint exception store, i.e. a new
  allowlist. At `CreateMap` the check fires on exactly the four fixtures CF §3.2 predicted — the row's own
  trap description used as a spec. The all-endpoint measurement is recorded in the gate's doc comment so it
  is not lost. The four exceptions are an OBLIGATION: a named fixture must still report `DWARF001`
  specifically, so one that healed and one that rotted into another error both fail. Sabotage both ways
  (entry deleted -> first assert red; healthy `nested-pair` named -> second assert red).
- **B8** — the scope shrank under the row's feet, and that is recorded at the guard: D-e drained and DELETED
  `PredatesTheChangelog` on 2026-08-21, so "one guard over both stores" has one store left. **Exact pin at
  zero**, not a shrink-only ratchet — below eleven the house rule is exactness, which is why `AssertRatchet`
  refuses a ceiling of ten or less. Sabotage: fabricated id -> `Assert.Empty() Failure`.
- **B10** — undefined enum cast reaches the throwing arm; the MESSAGE is asserted too, because the arm's
  value is that it names the corpus list the next person must extend. Sabotage: arm returns a corpus ->
  `Assert.Throws() Failure: No exception was thrown`.
- **B12** — key intersection of `DeclaredOptionCells` and `StructurallyInapplicable` must be empty. Why it
  would stay theoretical-LOOKING is now written at the assertion: a structural cell renders `n/a` before it
  is probed, so it can never read SILENT; the overlap would arrive as a quiet contradiction, not a failure.
  Sabotage: the live `MaxDepth`@`SpanMap` cell added to the structural store -> red.
- **B13** — `AssertEvidenceLinkResolves`: the file is resolved under `RepoPaths.Root` and the anchor must be
  defined in it, as an explicit anchor tag or as a GitHub heading slug. All 26 resolve, so a latent hole
  closed rather than a break found. Slug set under `OrdinalIgnoreCase` rather than lower-casing (CA1308),
  noted at the helper. Sabotage: bogus anchor -> red with the full remedy message.
- **B14** — remarks only, and therefore the one item with NO sabotage demo (there is no assertion to
  redden); stated as such in the commit rather than left as a gap.
- `SurfaceProbe.Baseline` private -> internal so B5's gate reads the process-wide memo instead of
  recompiling what the matrix already compiled.
- **Measured**: whole solution 0W/0E with samples; suite **7,814 / 0** foreground (+6).

## W6 — records hygiene: B19-doc, C5, the stale-status sweep — DONE 2026-08-22
Commit: `1f79718`.
- **B19 (documentation half only — the row forbids scoping a remedy, and none was scoped)**: the limitation
  is written in the `SurfaceParityTests` CLASS DOC, which is the file every ceiling constant is declared in
  — "where the ceilings are read", literally. It carries A7 as the evidence (the `Update` that called itself
  unconditionally and scored `Honoured`), D2 as the weaker `Refused` form, an explicit "no remedy is scoped
  here", and R22-01 / R22-02 (`b86cd0d`, `dbc96db`) as the systemic pressure. A companion note sits on
  `SurfaceEffect.Honoured`, where the verdict is produced. Closing sentence: read these ceilings as "no
  element silently stopped acting", never as "the generator is right".
- **C5, both remainders**: (1) the `NoSuchSite` per-cause loop now CALLS `AssertExactPin` instead of
  open-coding its two asserts — note the row said `AssertRatchet` and by now it was `AssertExactPin`,
  because B6 had already made the population per-cause and exact; the per-cause reasoning and the full
  breakdown ride in as `howToClose` so no message text is lost. (2) `AssemblyScanTests`' private repo-root
  walk AND its private source enumerator deleted in favour of `RepoPaths`; re-measured 56 generator and 443
  test sources either way, so the scanned corpora are unchanged. **NOT folded, stated instead**:
  `GeneratedDocsAreCurrentTests` keeps its own private walk — the row names `AssemblyScanTests` only, and
  that walk carries a dedicated regression test for the `.git`-as-a-FILE defect which replacing it would
  delete. Left as a maintainer's call rather than silently widening scope.
- **Stale sweep** — statuses only, history untouched, every number re-read from code rather than copied:
  the NOW block rewritten to post-merge reality with a dated note explaining why THIS section is edited and
  the ones below it are not; `EmittedInvalidCode` 10 -> **0** with the provenance traced (10 was correct at
  the merge `dc385d4`; round 21's T6/B27 drained it 8 -> 0 at `bda57e0`, merged at `d131c76`); `NoSuchSite`
  116 -> **48 + 68** exact per-cause (B6). F1 DONE (`dc385d4`), F2 DONE (worktree gone; ledger captured
  first at `656042c` / `c8ba8bb`, and round 21 repeated the practice at `96e62f9`). G table:
  `PredatesTheChangelog` struck through, mutation survivors replaced with the round-22 re-measures
  (96.46 / 95.42 / 81.59 at `b74023f` / `254c500` / `270d5cf`), the Stryker-configs row corrected, worktrees
  and all four SDD workspaces brought to today. C1 / C3 / C6 / B27 / B33 and the H rows verified current and
  left alone (H3 is W7's, H8 is the maintainer's).
- **Rows flipped DONE**: B4, B5, B8, B10, B12, B13, B14 (W5), B19, C5, F1, F2 (W6), B23 (W4). **No I-row
  filed and no row skipped** — nothing in W4-W6 needed a maintainer ruling; the two judgement calls (B5's
  endpoint scope, C5's second walk) were ruled in-task and are recorded above.
- **Measured at close**: whole solution 0W/0E with samples; full suite **7,814 / 0** foreground; surface
  matrix census **866 / 866**; RatchetInvariantScanTests + GateBandLogicTests + DeepTierSelfTests **15 / 15**;
  tracked tree clean.

W4/W5/W6: DONE (`072c7ca`, `77d746e`, `1f79718`). Queue: W7 (H3, Meziantou phase 2).
W4-W6: DONE (072c7ca B23, 77d746e seven guards, 1f79718 B19+C5+stale sweep). B23 fix = 4 hunks, each a
single INSERTED space, completeness ARGUED (grep for adjacent inline tags returns exactly 4 sites) + a
NON-self-referential pin added (the old docs test compared the renderer against itself, so it could never
have caught this). B5 scope ruled in-task: gate at CreateMap fires on exactly 4 fixtures as CF predicted;
the all-endpoint measurement (10) is recorded in the gate's doc comment rather than gated again, which would
have needed a new allowlist. B8 held as EXACT PIN AT ZERO (below 11 the house rule is exactness).
12 rows flipped DONE. Suite 7,814/0, census 866/866, scans 15/15.
K3: DONE (master ba48866 + db546f7). 85 static mutants confirmed EXACTLY (CS 50/87, BP 27/88, LI 5/5,
EA 3/21) - reproduces the 3-day-old figure. Mechanism confirmed FROM STRYKER SOURCE, and the decisive
finding: the static flag is RUNTIME COVERAGE ATTRIBUTION, not the C# static keyword (same source line
carries both kinds; split follows reachability) - which kills the "de-static the product" option on
evidence, not just on the mutation-appeasement rule. Cost: >=56x ratio, ~70% of the 21:19 wall. EVERY lever
closed: coverage-analysis already fastest; killing the survivors impossible (15 of 16 are proven-equivalent,
16th is the L88 dead-code question); in-source disable closed by ruling (b); narrowing closed by the
integrity ruling. RECOMMENDATION ACCEPTED: leave it, write the cost down (ruling (c)); per-file parallel CI
jobs is the only clean lever if wall time ever matters (~6 min for 4x compute + 4 ratchets in lockstep).
5 ledger contradictions flagged incl. a NEAR-MISS worth remembering: the ledger's 16 proven-equivalents and
K3's 16 static survivors are DIFFERENT SETS overlapping in 15 - anyone quoting "16" must say which.
Round 22 remaining: W7 only (running).

## W7 — Meziantou phase 2 (H3), 2026-08-22, commits `7b2ee1d` (fixes) + `d57ac03` (wiring) + this closeout

The last agent row of round 22. Round 21's T1 adopted Meziantou.Analyzer 3.0.167 on the five `src/`
projects and deferred the rest as "one session of noise triage across 11 test projects". This is that
session, and the deferral's own count was stale: there are **12** test projects, plus 4 samples and 1
benchmark — **17 projects wired**.

- **Vehicle: per-csproj, not a props file.** Checked first, as the task asked: no `tests/Directory.Build.props`
  exists, no area props file exists anywhere, and every test csproj already repeats its own
  xunit / Test.Sdk / coverlet references. src phase 1 was per-csproj too. A `tests/Directory.Build.props`
  would have had to re-import the root one by `GetPathOfFileAbove` or silently drop every repo-wide property
  — a real failure mode purchased for no precedent. 17 `packages.lock.json` regenerations rode along in the
  wiring commit (the resolved graph is a committed artefact; locked-mode restore refuses anything else).
- **Scoped configuration: three area `.globalconfig` files.** The SDK's discovery is
  `@(_AllDirectoriesAbove->Combine('.globalconfig'))` (`Microsoft.Managed.Core.targets:145`) — *every*
  `.globalconfig` above a compiled file, not just the nearest — so `tests/`, `samples/` and `benchmarks/`
  each get one that is ADDITIVE to the root file and structurally invisible to `src/` (verified: no `src/`
  csproj links a file from outside `src/`). The binding constraint, stated in each file's header: **never
  repeat a key the root sets** — both sit at the default `global_level` 100 and an equal-level conflict
  DROPS the key from both files, which would silently *re-enable* a root disable for those projects rather
  than disable anything.
- **383 findings examined → 31 real defects fixed, 352 disabled as idiom.** The headline number is 31.
  - `MA0002` x15 — culture-sensitive ordering. `OrderBy`/`Order`/`ThenBy` over strings routes through
    `Comparer<string>.Default` -> `string.CompareTo` -> the build machine's locale; each site asserted an
    exact sequence. The neighbouring sites in the same files already passed `StringComparer.Ordinal`, so
    these are the ones that were missed. Four of them order the contents of a committed Verify snapshot.
  - `MA0099` x7 — six `(cell.Endpoints & endpoint) != 0` now name `SurfaceEndpoints.None`; the
    `AttributeUsage.ValidOn` test reads as `HasFlag`.
  - `MA0023` x5 — capture groups nobody reads made non-capturing (`DeterminismSourceScan` D1/D2/D3,
    `ClaimMechanismScan`'s `SEC|COR`); `DiagnosticProseIsCurrent`'s section header got a NAMED group and
    both of its readers stopped indexing `Groups[1]`.
  - `MA0069` x2 — the Conformance sample's `public static int Pass, Fail` became properties with private
    setters (nothing outside the class ever wrote them).
  - `MA0158` x1 (`System.Threading.Lock`), `MA0011` x1 (an `AppendLine` interpolating an int into
    *generated C# source*, now `CultureInfo.InvariantCulture` — culture-fragile by construction, harmless
    today because the ints are non-negative), `MA0004` x1 (the one `await` in its block without
    `ConfigureAwait(false)`; the line below it already had one).
- **Five disables, each naming the idiom it fights** (full reasons live in the config files, not here):
  `MA0002` — xunit assertion overloads (default string equality is *already* ordinal) and
  `Dictionary<string,…>` fixture/payload DTOs whose comparer is the subject under test; `MA0009` — 44
  self-validation scanners over repo-controlled files, and a match timeout would put a WALL CLOCK inside a
  deterministic gate, which H7 forbids; `MA0008` — `BlittableProof` refuses the auto-blit path for ANY
  struct carrying `[StructLayout]`, so annotating a blit fixture changes the path under test (this one is
  not a taste call: the rule's remedy would silently alter the experiment); `MA0047` — the Meziantou twin
  of `CA1050`, already `NoWarn`'d in all five sample/benchmark csprojs; `MA0004` in AotSample only, beside
  its existing `CA2007` `NoWarn`. **Rule for the mixed vehicles:** area-wide idiom -> area `.globalconfig`;
  single-project ruling -> that csproj's `NoWarn`, next to the .NET-analyzer twin it repeats.
- **One reasoned `#pragma`, zero unexplained suppressions.** `SurfaceCatalog`'s claim loop disables
  `MA0099` for three lines because `AttributeTargets` is a BCL `[Flags]` enum with no zero member. Kept as
  a narrow pragma rather than an area disable precisely so the rule stays live for `SurfaceEndpoints`,
  where `None` exists and the six fixes above are therefore enforced.
- **Non-vacuity proved both ways** (T1's discipline; a disable you cannot see failing is decoration): a
  `Dictionary<string,int>` planted in `src/DwarfMapper` still errors `MA0002` — the scoped disables do not
  leak into src; a visible mutable static planted in `CorpusTests` errors `MA0069` — the analyzer really is
  running there and the area file has not switched it off wholesale. Both probes reverted.
- **Build-time delta, measured with one methodology before and after** (whole-solution Release
  `--no-incremental`, quiet machine, medians): **24.8 s** (n=3: 24.3 / 24.8 / 25.7) -> **25.5 s**
  (n=5: 23.6 / 24.5 / 25.5 / 26.4 / 32.2) = **+0.7 s**, at the level of run-to-run noise. Combined with
  T1's +6.3 s that is **~ +7.0 s from the pre-analyzer 19.3 s baseline against the ~9 s cap — inside it,
  and reported rather than assumed.** `ReportAnalyzer` says `MA0002` is still the single hottest rule
  (~18 s CPU) and that it is hot *in the generator*, where it stays on; severity `none` in the test scope
  means Roslyn skips it there, which is why phase 2 costs almost nothing.
- **Measured at close:** whole-solution build **0 W / 0 E** with samples; full suite **7,814 / 0** in the
  FOREGROUND (78 s); surface-matrix census **866 / 866**; `RatchetInvariantScanTests` +
  `GateBandLogicTests` + `DeepTierSelfTests` **15 / 15**; tracked tree clean.
- **Two new findings filed, neither fixed here.** **I10** — the `MA0002` disable is the price of the
  triage: the rule bundles "name the comparer, it is implicit" (noise) with "name the comparer, the default
  is CULTURE-DEPENDENT" (real), and turning it off for tests leaves the second unguarded; the remedy's shape
  already exists as `DeterminismSourceScanTests`' D1, but a new scan is a new gate and was out of budget.
  **I11** — `MetamorphicTests.MR3_…` is an UNSEEDED randomized test in the fast tier, and seed
  `9EqkJlF93ol7` reproduces a violation. Verified **pre-existing at clean tip `1f79718`** by stashing the
  whole W7 tree and re-running, so W7 neither caused nor perturbed it. The counterexample looks like an
  oracle gap rather than a product defect: a `HashSet` destination holds 2 elements under the class
  representation and 1 under record / record-struct, because record equality is structural — MR-3's premise
  does not hold for set-shaped destinations. The nondeterministic-gate half (R4) is separable and is the
  more urgent of the two.
- **Nothing skipped.** Every rule that fired was either fixed or disabled with a written reason; no
  `NoWarn` was widened beyond the single AotSample `MA0004`; the src-scoped configuration was not touched.

W7: DONE. **Round 22's agent queue is empty** — Layer 0 remains maintainer-gated on the master/`ci.yml`
push, and the maintainer-only rows (D-a, D-c, D-e, D-f, F3, H8, the generator dead-code rulings, and the
open research questions Q3/Q4/Q5) are unchanged.
W7: DONE (7b2ee1d fixes, d57ac03 wiring, 66026ad closeout). 17 projects (the "11" in the deferral was
stale: 12 test + 4 samples + 1 benchmark), per-csproj refs + THREE area .globalconfigs (tests/samples/
benchmarks) with the never-repeat-a-root-key constraint documented (equal global_level drops the key from
BOTH files - silent re-enable). 383 findings examined: 31 FIXED (16 BEHAVIORAL - 15x MA0002
culture-sensitive OrderBy asserting exact sequences, 4 of them ordering committed Verify snapshots; 1x
MA0011 int interpolated into GENERATED SOURCE), 352 accounted for per-rule with reasons incl. MA0009's
refusal on H7 grounds (a regex timeout is a wall clock inside a deterministic gate). Non-vacuity probed
BOTH ways (src still errors; tests still catch). Build +0.7s; combined with T1 = +7.0s vs ~9s cap - inside.
Suite 7,814/0. I10 filed (MA0002 bundles noise with the real culture genre).
I11 FILED AND RULED BLOCKING BY CONTROLLER: MetamorphicTests MR-3 is an UNSEEDED randomized test in the
FAST tier (violates the round's OWN R4 invariant) and seed 9EqkJlF93ol7 reproduces a violation - verified
pre-existing at clean tip 1f79718, so it is K2's, not W7's. The counterexample is an ORACLE GAP not a
product bug: a HashSet destination holds 2 under Class (referential equality) and 1 under Record
(structural) - both correct. K2's "zero divergences" was true of its sample, not of the space.
Merging a randomly-reddening fast-tier gate would be a regression handed to the maintainer, so the fix goes
in BEFORE the merge: seed determinism + a declared keyed exclusion (or a population that avoids structural
duplicates, if more truthful) + the counterexample preserved + sabotage + an honest deep re-run.

## I11 - the pre-merge blocking fix (fast-tier determinism + the set-shaped oracle gap) - DONE

- **Files**: NEW `tests/DwarfMapper.CompilerTests/PinnedSampling.cs` (the deterministic sampling seam);
  `MetamorphicTests.cs` (three relations moved onto it; `Normalize` / `NormalizeForInjection` /
  `ListifySetsOfStructuralElements`; two new deterministic executors); `TypeGraphSmokeTests.cs` and
  `DifferentialOracleTests.cs` (moved onto it); `PinnedCorpus.cs` (two new rows + the pinned population
  seed); `tests/Shared/DeepTier.cs` (catalog comment: mechanism change + re-measured wall);
  `Issues/round20/TASKS.md` (I11 -> DONE, I12 filed).

- **(a) The seeding half. The obvious fix does not work, and that was measured before anything was built.**
  CsCheck 4.7.0's `seed` argument is documented as "the initial seed to use for the FIRST iteration", and a
  probe confirmed the consequence: `Sample(seed: s, iter: 8, threads: 1)` produced `502568, <7 random>` on
  every run - iteration 1 pinned, the rest from a randomly seeded thread PCG. Pinning the seed would have
  left the gate exactly as nondeterministic as it was. Two further probes settled the design: `PCG.ToString()`
  round-trips through `PCG.Parse` (so a pinned stream prints a usable `CsCheck_Seed` string), and SEQUENTIAL
  SEEDS ARE A BAD LADDER - `new PCG(0u, (ulong)i)` gave 62/64 distinct first draws where `new PCG((uint)i,
  base)` gave 64/64, so the ladder is over STREAMS, not seeds.
- **The shape chosen**: case k = one `Sample(seed: PCG(stream k, BaseSeed).ToString(), iter: 1, threads: 1)`,
  run inside `Parallel.For(0, count)`. Keeps CsCheck as the generator, keeps parallel throughput, makes the
  case set a pure function of (generator, count), and makes the fast list a strict PREFIX of the deep list.
  The deep tier stays deterministic too: exploration = more pinned streams, never a re-roll. Failure
  handling catches `AggregateException` (never a bare `Exception` - CA1031 is on in tests since W7) and
  rethrows the first inner exception via `ExceptionDispatchInfo` so the assertion message arrives unwrapped.
- **What was traded away, stated rather than hidden**: CsCheck shrinking. It was already notional - the
  original I11 red reported "0 shrinks, 7 skipped, 8 total", i.e. the failing draw consumed the whole
  budget. The repro artifact is the assertion message's `GraphSpec.Describe()`, and house practice already
  minimizes by hand and pins the SHAPE.
- **Determinism proof is case IDENTITY, not three green runs.** Every site prints
  `<label>: pinned case set = N cases, digest <FNV-1a over the index-ordered case descriptions>`. Three
  consecutive foreground runs, all five sites identical: K0 `fdd6312308033aab`/25, K1
  `b0f562b77a7f32b1`/20, MR-1 `cb4f1a634ab9ae78`/10, MR-2 `cb4f1a634ab9ae78`/10 (same digest is CORRECT -
  same generator, same streams, same count), MR-3 `0d320c3f28767faf`/8. **Instrument proved non-vacuous**:
  with `seed:` removed from `PinnedSampling`, two runs printed ten different digests. The digest is
  REPORTED, never gated - pinning it would ratchet the generator's grammar, not the product.

- **(b) The oracle gap. Normalization chosen over population-fixing, on a proof rather than a preference.**
  The alternative ("make the population avoid structurally-duplicate elements") is not harder, it is
  IMPOSSIBLE: a set's content is a function of its element type's `Equals`, and the element value space can
  be smaller than the element count. A node whose only member is `bool` has two inhabitants, so a two-element
  set collides with p ~ 1/2 under ANY populator; `byte` gives ~ 1/256. No seeding discipline removes a
  pigeonhole. The measured counterexample is of exactly that family - both elements' single `long?` member
  drew null under the documented ~25% nullable-scalar bias (verified offline against the populator's own
  FNV mixing before anything was pinned: `count % 3 == 2`, `null? % 4 == 0` for elements 0 and 1).
- **The divergence is at POPULATION time.** The reproduced red shows the SOURCE set holding 2 under Class
  and 1 under Record/RecordStruct, before any mapping; the mapper faithfully reproduces whichever it was
  handed. So MR-3's premise - the variants receive the SAME input - is what fails. This is
  `ClearNestedNullability` reason (2) generalized from nullability to equality, and it is stated that way
  in-code.
- **Scope, deliberately narrow**: only `HashSet`, only when the element is a graph NODE. Scalar sets are
  untouched (scalar equality is representation-invariant); all other `CollShape`s are untouched; and the
  filing's "or a dictionary keyed by the element" branch is UNREACHABLE in this grammar - `TypeGraphRenderer`
  L137 emits `Dictionary<string, E>` only - which is now stated rather than guarded speculatively.
  Rewriting to `List` rather than dropping the graph keeps every pinned case live and keeps the collection
  edge under test; only the container's equality semantics leave. **MR-1 is not normalized at all**: record
  and struct equality are insensitive to declaration order, so set-of-node members keep full MR-1 coverage.

- **A SECOND route to the same precondition, deduced then MEASURED.** MR-2 reaches it without re-kinding:
  injecting the unmapped `X{i}` into a structural-equality set-element node SPLITS A POPULATION TIE, so the
  baseline maps a one-element set and the fattened variant a two-element one. Pinned as
  `PinnedCorpus.SetOfStructuralElementsRecordToClass` (record source element, class dest element) and
  proven by `Set_shaped_members_are_unmapped_member_dependent_by_design`. MR-2's normalizer is NARROWER
  than MR-3's on purpose - it listifies only sets whose element node is already structural, so
  class-element sets keep their full MR-2 coverage; MR-3's re-kind set always contains `Record`, so its
  predicate is unconditionally true. MR-2 had never drawn this shape, which is why it is pinned rather
  than left to sampling.

- **The counterexamples survive the exclusion.** `PinnedCorpus.SetOfStructuralElements` +
  `SetOfStructuralElementsSeed = 269828994`; hand-minimized from the shrunk repro, and the minimization is
  EXACT rather than approximate because population is name-keyed (member `M0_1` keys the set and `M1_0`
  the elements regardless of what else the node declares). The row's `CsCheck_Seed=9EqkJlF93ol7` repro line
  is now dead - that seed indexes nothing under `PinnedSampling` - and the pinned SHAPES replace it, which
  is strictly better: a shape survives generator edits that rot any seed string. The executors assert the
  behaviour IS CORRECT (2/1/1 cardinality across Class/Record/RecordStruct), measure the SOURCE cardinality
  separately so the mapper is exonerated by measurement rather than assertion, and assert that
  normalization removes the divergence - which makes each executor the tripwire for its own exclusion.

- **Sabotage, four directions, all reverted (`git grep SABOTAGE` clean).**
  (1) MR-3 exclusion inert + the original seed replayed as a temporary `Sample(seed: "9EqkJlF93ol7",
  iter: 1)` -> RED with `r.M0_1.#=2` vs `1` vs `1`, the exact original counterexample.
  (2) Same temporary replay with the exclusion ACTIVE -> GREEN: the fix is proved on the ORIGINAL seed, not
  only on the pin.
  (3) MR-3 exclusion inert -> `Set_shaped_members_are_representation_dependent_by_design` reds.
  (4) MR-2 normalizer inert -> `Set_shaped_members_are_unmapped_member_dependent_by_design` reds. This one
  drove a refactor: the executor originally called the helper with its own predicate and therefore stayed
  green under sabotage, so MR-2's normalization was extracted into `NormalizeForInjection` and both the
  relation and the executor now call it - a tripwire has to be on the same wire.
  Note the sabotaged MR-2 and MR-3 RELATIONS stayed green on their pinned sets: the shape is rare in
  sampling, which is precisely the argument for pinning it instead of trusting a draw.

- **(d) K2's claim re-verified honestly.** K2's "zero divergences" was true of an unreproducible random
  draw. Re-run at the catalog counts: 3 deep runs, all green, identical digests (K0 250 `52b3f1ddcaf81d48`,
  K1 1000 `c2035fc35e00d8d1`, MR-1/MR-2 250 `89b0767429d2fa4d`, MR-3 150 `8f0db4b74563de2a`), 250/250/150
  executed and 0 refused, project-alone wall **31.1 / 34.4 / 32.4 s** (K2-era 30.4-31.7 s - the mechanism
  costs nothing measurable). Then a ONE-OFF WIDENED SWEEP with the catalog temporarily raised and reverted:
  **K0 3000 + K1 3000 + MR-1 2000 + MR-2 2000 + MR-3 2000**, 3 m 11 s, 25/25 green, zero divergences, zero
  refusals. So the two set-shaped routes appear to be the whole of it, as far as ~12,000 pinned cases can
  say - and unlike the original claim, anyone can re-run exactly those cases.

- **I12 filed, not fixed.** `RegistryPropertyTests` (3 sites, fast 200) and
  `SelfValidation/DocPipelinePropertyTests` (4 sites, fast 500/500/500/200) are unseeded CsCheck in the
  fast tier - the same R4 class. Left to the maintainer per the brief's "fix only CompilerTests here";
  lower-risk (small closed pools, densely covered at those counts) but nonzero, and the fix is mechanical
  now that `PinnedSampling.Run` exists. `Fuzzing/*` is NOT affected - it enumerates explicit integer seeds
  via `[MemberData]` over `Enumerable.Range(0, DeepTier.Count(...))`, which is the deterministic house
  pattern the CsCheck sites should have mirrored from the start.

- **Measured (gates)**: whole-solution build **0 W / 0 E** with samples; suite **7,818 / 0** FOREGROUND
  (63 s) - baseline 7,814 + 2 corpus rows + 2 executors, exactly accounted; surface-matrix census
  **866 / 866**; `RatchetInvariantScanTests` + `GateBandLogicTests` + `DeepTierSelfTests` **15 / 15** (no
  fast count changed - the self-test's pins are untouched by design; only the MECHANISM changed, recorded
  in the `DeepTier` catalog comment); tracked tree clean apart from the intended files.

I11: DONE. Both halves fixed, plus a second route (MR-2) that the filing had not spotted. Determinism
proved by identical case-set digests across 3 runs at 5 sites and disproved-by-construction with the seed
removed. The gap is closed as a DECLARED precondition with an impossibility proof for the alternative, the
counterexamples are pinned as SHAPES (which outlive seed strings), and every exclusion has its own
tripwire. Deep re-run is wider and deterministic where K2's was narrower and unrepeatable. I12 filed for
the two remaining unseeded CsCheck populations.
