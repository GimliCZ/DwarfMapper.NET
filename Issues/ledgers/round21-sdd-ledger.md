# SDD ledger — plan: docs/superpowers/plans/2026-08-19-round21-depth-tier.md

Worktree: C:/Users/Jouda/RiderProjects/DwarfMapper-r21
Branch: feat/round21-depth-tier (forked from master dc385d4+, post-F1-merge)
Baselines: fast tier build 26s + suite 64s (7,657 tests); runtime mutation 12m25s @ 61.02%; matrix 866/866.
Deep-tier budget: < 44 min TOTAL, per-component measured.

Pre-flight scan (pairs sharing files):
| A | B | Shared | Finding |
| T1 | T4 | Directory.Build.props / test csprojs | T1 adds analyzers, T4 adds CsCheck + env knobs. Sequential (T1 first) to avoid props churn. |
| T3 | T5 | stryker configs + housekeeping + ci.yml | T5 composes T3's measured legs. Sequential, T3 first. |
| T6 | T7 | generator src vs runtime src + mutation re-run | Disjoint files, but BOTH re-measure ratchets. Sequential. |
| T1 | T3 | none | PARALLEL, separate worktree for T3 not needed - same worktree, disjoint files, sequential commits. Run T1 in-worktree, T3 as background agent in THIS worktree is unsafe (shared index). RULING: T3 runs FIRST alone (it is measurement-only + config edits), T1 after. Cost if wrong: serialization, no correctness risk.
Self-consistency: T5's 44-min gate depends on T3+T2+T4 measured components - T5 must be late. OK as ordered.
Ruling: fast-tier growth cap ~10% is binding - every task measures the fast suite before/after. Cost if wrong: creeping fast tier, caught at T5's table anyway.
T3: done (77eb240, 7c4c97a). Generator leg 21:02 @ 71.64% -> break 71 (46 survivors + 11 no-coverage);
DocTooling 05:57 @ 83.10% -> break 83 (11 + 37). Both pass non-vacuity. Generator run 1 was inflated by 2
timeouts on STATIC mutants that cannot hang - fixed with additional-timeout, NOT filtering. DocTooling's 3
timeouts are GENUINE infinite loops, no cushion added - the discrimination measured per leg, not assumed.
BUDGET FLAG: 12:25 + 21:02 + 5:57 = 39:24 of the 44-min deep tier, leaving ~4.5 min for everything else.
Measured cause: 85 of the generator's 201 scoreable mutants are STATIC, each re-running the whole suite
(~493k executions vs ~1,580 for instance mutants). T5 decides with this data; mutate was NOT narrowed.
HAZARD (T3-H1, maintainer-grade): the DocTooling leg EMPTIED README.md, CONTRIBUTING.md, docs/diagnostics.md
- 2,523 deleted lines across 5 files. Reverted, uncommitted. Mechanism (to verify in fix): Stryker runs tests
from a sandbox under StrykerOutput/, the sandbox lacks DwarfMapper.NET.sln at its own root, so the tests'
RepoRoot walk ESCAPES the sandbox upward into the real repo - and a MUTATED doc renderer then writes garbage
through the self-heal/write-back path onto real files. housekeeping.ps1 -Mutation still runs it unconditionally.
Ruling: T3-H1 is fixed NOW, before T1, as its own task - it is data-destroying and every future -Mutation run
re-triggers it. Fix shape: the repo-writing test paths must refuse when the resolved root is reached by
walking OUT of a Stryker sandbox (or when mutation env is detected), pinned by a test; round 19's ARCH-06
("repo mutation requires a registered pattern") named this class and was never implemented - this is its
concrete instance. Cost if wrong: doc tests lose self-heal under mutation, which is exactly the point.
Dispatched (parallel): T3-H1 fix agent (worktree; verify sandbox-escape mechanism, central refusal guard next
to the repo-root helper, pinned non-vacuously, re-run DocTooling leg + post-run git status must be clean;
tests still RUN under mutation - only write-back refused, honouring the no-exclusions ruling) and T1 RESEARCH
agent (read-only, no builds - avoids contending with H1's verification run; decision table for Meziantou/
Roslynator/Sonar/ErrorProne/PublicApi/BannedApi/IDisposable/VSTHRD + ApiCompat/ILVerify vs the 10% fast-tier
growth cap). T1 IMPLEMENTATION waits for both: needs the worktree quiet and the research verdict.
D-e drafting offer to user: parked, not accepted yet ("resume" read as continue-queue, not as yes).
T1 research: done (advisor-reviewed). Adopt-now: Meziantou.Analyzer 3.0.167 (5 src projects, phase 1, via new
root .globalconfig disabling MA0048/51/26/03/16); PublicApiAnalyzers 5.6.0 (runtime + Testing, Shipped empty /
full surface in Unshipped until 1.0.2 final, RS0037 on); BannedApiAnalyzers 5.6.0 (runtime: reflection ban;
Generator+CodeFixes: determinism ban - DateTime.Now/Guid.NewGuid/Random/CurrentCulture/Environment.NewLine).
Estimated +3-5s on the 90s fast tier (<10% cap); MUST be measured before/after, not assumed. Rejected: Roslynator
(style overlap), IDisposable (stale + 1-scope surface), VSTHRD/AsyncFixer (no async surface), security analyzers
(CodeQL already runs), StyleCop. Deep tier: EnablePackageValidation on both packable projects; ILVerify
10.0.11 over runtime DLL + one generated-consumer assembly.
Rulings on the research's open questions:
  R1 Sonar: PARKED, not adopted - v10.x is SSAL (non-OSI) and this repo documents a per-dependency license
     stance; adopting non-OSI silently is not mine to do. Goes to TASKS.md as a user ruling. Cost if wrong:
     nightly misses symbolic-execution rules; recoverable any time.
  R2 PublicAPI scope: BOTH shipped assemblies (runtime + Testing). Cost if wrong: Unshipped.txt churn - trivial.
  R3 Package validation: option (a) - baseline 1.0.2-rc.1 now, ApiCompatSuppressions.xml for intentional rc
     breaks. Catches accidental breaks during the rc phase, which is where we live. Cost if wrong: suppression churn.
  R4 Meziantou phase 2 (tests/samples): DEFERRED to a TASKS.md item; phase 1 src-only ships first.
  R5 Environment.NewLine ban: implementer verifies AddNormalizedSource centralizes newlines; if any legit
     emission path uses it, refactor that path in-task or drop that ONE ban line with a comment - not the whole list.
  R6 ErrorProne one-off audit: YES as a local, no-CI-footprint step; findings to ledger, fixed by hand if real.
  R7 Roslyn-floor check (CS9057) runs FIRST in T1 implementation, per the research's warning; fallback to the
     newest loading version is pre-approved.
T1 implementation: BLOCKED on H1 agent finishing (worktree builds + doctooling leg re-run in flight). Dispatch
after its notification; then measure fast-tier build before/after with -p:ReportAnalyzer=true.
T3-H1: FIXED at 3982f25. Mechanism hypothesis FALSIFIED by the fix agent: no sandbox, no escape - Stryker 4.16
swaps the mutated DLL into the real bin/ (leaves *.dll.stryker-unchanged markers; pre-fix run had left mutated
DLLs LIVE in nine test bins - decontaminated). RepoWriteGuard.cs centralizes refusal on the marker /
StrykerOutput ancestors; 4 repo writers rewired; ARCH-06 scan added (every raw file-write in tests/ registered).
DocTooling re-measured under guard: 67.96% (was 83.10 - 43 kills were corpus-self-corruption artifacts);
break 83->67, low 80->67. Also found: break>low makes Stryker refuse AND EXIT 0 - the leg was silently
unrunnable since T3. Post-run git status byte-identical; suite 7,665/0.
TASKS.md updated + committed (2f38371): C3 -> DONE, new section H (H1 done, H2 Sonar license DECIDE-user,
H3 Meziantou phase 2, H4 break<=low + scored-report guard, H5 generator floor re-validation, H6 budget note).
Dispatched H5 agent: re-run generator leg under guard on quiet machine, artifact-vs-noise breakdown against
T3 catalog, set break to measured floor same-commit, post-run status must be clean. T1 IMPLEMENTATION queued
AFTER H5 (machine must stay quiet for the 21-min leg; T1 builds would contend).
H7 (NEW, maintainer directive): Timeout kills are a clock oracle over ns-scale ops - dissect every
Timeout-classified mutant, name the broken termination/progress invariant, kill it deterministically;
production-loop hardening only on its own robustness merits; exit criterion Timeout=0 across all legs.
TASKS.md row committed (320484c). Phase 1 dispatched READ-ONLY (no dotnet, writes to scratchpad only -
H5's Stryker timing run is still in flight and its post-run git status must stay clean): enumerate Timeout
mutants from newest JSONs per leg + the runtime leg's seven Timeout->Survived load-noise flips (tagged
separately), per-mutant WHY + concrete deterministic test sketch + hardening verdict. Phase 2
(implementation) queues behind H5 completion, alongside T1 implementation - ordering decided then.
2026-08-21 resume (weekly limit had cut off two agents mid-flight on 08-19).
H5 CLOSED by controller (736f6b3): the Stryker run itself had completed before the limit hit. Verdict:
471 mutants, ZERO status flips vs pre-guard run, score 71.64% identical (144K/46S/11NC), Timeout 0,
wall-clock 19:41. break 71 stands - earned by re-measurement. The corruption-artifact class was
DocTooling-only. Generator leg is clock-clean; H7's genuine-hang population = DocTooling's 2-3 Timeouts.
H7 phase-1 agent RESUMED via message (progress saved at cutoff; was pulling DocTooling hang-site data).
Constraint relaxed: may run dotnet now; still read-only on repos, scratchpad-only writes.
T1 IMPLEMENTATION dispatched: Meziantou 3.0.167 (5 src projects + .globalconfig with reasoned disables),
PublicApiAnalyzers + BannedApiAnalyzers 5.6.0 (Roslyn-floor check first, 3.11.0 fallback pre-approved),
EnablePackageValidation baselined 1.0.2-rc.1, ErrorProne one-off audit, before/after build medians vs the
~9s cap, zero unexplained suppressions.
H7 phase 1: done (analysis at scratchpad/H7-timeout-dissection.md - COPY INTO Issues/ledgers/ with phase 2's
commit, scratchpad is volatile). Headline: only THREE mutants ever genuinely hung, all DocTooling, all
Statement-deletion of a loop's progress statement. Sharpest evidence for the directive: mutant 204 flipped
Timeout->Killed between same-day runs purely because its runaway StringBuilder allocates (~4GB OOM at
30-90s, ~63 Mchars/s measured) while 525/534 spin pure-CPU - same mutation class, two classifications,
decided by an accidental allocation side channel. The clock classifies the race, not the mutant. Also:
doctooling config NOTE 1's "204 killed outright before the loop is reached" is FALSE (kill is OOM inside
the loop) - must be corrected.
Rulings for phase 2 (controller):
  R-H7a Mutant 204 (DocSnippetInjector.cs:39, i++ deleted): ADOPT the progress guard (if (i <= previous)
    throw, wrapped in Stryker disable all/restore) + the terminates-and-reproduces test. Genuine merit
    accepted: parser loop, two advance sites, a future branch forgetting to advance = CI hang; guard makes
    all 10 coverers kill in microseconds. Cost if wrong: ~3 lines of defensive code in a parser loop.
  R-H7b 525/534 (SnippetScanner.cs:138/139, RemoveAt deleted): ADOPT the FindIndex/FindLastIndex/GetRange
    restructure of Dedent's trim (behavior-identical incl. empty-region throw) + the direct
    Leading_and_trailing_blank_lines pin test (today the sole coverer is one CsCheck property). Honest label
    stands: testability-motivated restructure, NOT hardening - the original loops are provably terminating.
    Cost if wrong: a behavior-identical rewrite of ~10 lines with a new direct test over it.
  R-H7c Correct NOTE 1 (and NOTE 7 if implicated) in stryker-config.doctooling.json.
  R-H7d Re-measure the DocTooling leg after both changes; expect Timeout 0, score ~unchanged (67.96 was
    counted with 2 Timeout kills; guard+restructure convert those to Killed, so score should hold or rise);
    set break only per measurement.
  R-H7e Runtime leg: no genuine hangs in evidence; its Timeout entries are clock noise (incl. a string
    mutation that cannot hang drawing a Timeout). No test work, no hardening. Nightly CI re-measures.
  R-H7f dotnet test --blame-hang --blame-hang-timeout 5m as runner-level backstop on regular (non-Stryker)
    CI legs: folded into T5's ci.yml scope, not done here. xunit [Fact(Timeout=)] rejected per analysis
    (async-only, cooperative, a smaller clock not a kill).
SEQUENCING: phase 2 BLOCKED until T1 implementation agent completes - both would write DocTooling/src and
race the index; round-20's bare-commit disaster is why two writers never share a worktree.

T1: DONE - static-analyzer adoption (Meziantou + PublicApiAnalyzers + BannedApiAnalyzers), phase 1 src-only.
  Packages (all MIT, PrivateAssets=all, license comments in Directory.Packages.props): Meziantou.Analyzer
  3.0.167; Microsoft.CodeAnalysis.PublicApiAnalyzers 5.6.0; Microsoft.CodeAnalysis.BannedApiAnalyzers 5.6.0.
  Roslyn floor (R7): all three LOAD under SDK 10.0.101 / Roslyn 5.0.0 - no CS9057, no fallback needed;
  each verified non-no-op by a deliberate trip (MA0011 fired, RS0016 x554, RS0030 on the registry GetType).
  Wiring: Meziantou -> all 5 src projects; PublicApi -> DwarfMapper + DwarfMapper.Testing (packable pair);
  BannedApi -> runtime (reflection list) + Generator/CodeFixes (determinism list). NOT Testing/DocTooling
  (reflection is their product - comments in csprojs). R5 verified: Environment.NewLine exists only in doc
  comments; AddNormalizedSource is the single newline funnel - the ban line stands, no refactor needed.
  Findings FIXED (18): MA0002 x9 explicit StringComparer.Ordinal (8 Generator collection/Contains sites,
  1 DocTooling GroupBy); MA0021 x1 (projection comparer GetHashCode -> StringComparer.Ordinal); MA0008 x1
  (EnumPolicy [StructLayout(Auto)]); MA0009 x1 (DocTooling Regex.Replace given a 1s timeout); MA0015 x4
  (ObjectFactoryV2 ArgumentException paramName); RS0027 x2 (ObjectFactory/V2 Create<T>(int seed = 0) split
  into Create<T>() + Create<T>(int seed) - public surface change, rc-phase, PublicAPI reseeded).
  Suppressions/disables (ALL reasoned in-place): .globalconfig disables MA0048/MA0051/MA0026/MA0003/MA0016
  (pre-approved) + MA0006 ADDED DURING TRIAGE (130 sites; string ==/!= is compile-time ordinal + null-safe,
  `name == "literal"` is the generator idiom; MA0002/MA0021 still force explicitness where a comparer is
  actually selectable) - the one disable beyond the plan list, reported. One #pragma RS0030 at
  DwarfMapperRegistry.Map (the documented runtime-type dispatch - the ban message itself names it as the
  sanctioned exception). Zero NoWarn additions.
  PublicAPI files: Shipped = header only (nothing stable shipped); Unshipped seeded by the RS0016 code fix
  (dotnet format): runtime 277 symbols, Testing 42 symbols; #nullable enable headers (RS0037 clean).
  Package validation (R3): EnablePackageValidation on both packables. DwarfMapper baselined vs nuget.org
  1.0.2-rc.1 - ONE intentional rc break suppressed with comment (CP0006: IDwarfMapper.Map<TSource,TDest>
  update-into overload added post-rc.1) in ApiCompatSuppressions.xml wired via ApiCompatSuppressionFile.
  DEVIATION: DwarfMapper.Testing has NEVER been published (nuget.org verified 2026-08-21) -> no
  PackageValidationBaselineVersion there (structural validation only, csproj comment says why). Both pack.
  Measurements: baseline build median 19.3s (warm runs 23.2/17.3/23.5/19.3/18.8; cold 32.9 discarded) ->
  after median 25.6s (25.6/26.4/23.5). Delta +6.3s, inside the ~9s fast-tier cap. ReportAnalyzer (CPU time,
  analyzers run concurrently): Generator is the hot project - 40.2s total analyzer CPU, Meziantou 25.4s,
  single hottest rule MA0002 UseStringComparerAnalyzer 18.6s; PublicApi/BannedApi <=0.2s everywhere.
  If the cap ever tightens, MA0002's analyzer cost is the first knob. Full suite 7,665/0 green (72.7s
  wall - test projects carry NO new analyzers; delta vs 64s baseline is co-agent contention noise).
  ErrorProne one-off audit (R6): ErrorProne.NET.Structs 0.6.1-beta.1 (newest available) is a SILENT NO-OP
  under the Roslyn 5.0.0 host: dll reaches csc's /analyzer list, no CS8032/CS9057/AD0001, yet zero EPS
  output even on a designed defensive-copy trip (readonly field of non-readonly struct calling non-readonly
  member) and with EPS05/06/12 severities forced to warning. No findings CAPTURABLE - the hidden-copy audit
  needs a Roslyn-5-compatible release; re-run then. Reference fully reverted, no repo footprint.
T1 IMPLEMENTATION: DONE (f02b049 wiring, ff6f389 18 finding fixes, 62f9ecf ApiCompat suppression).
All three packages at target versions, Roslyn floor held (no CS9057, trip-diagnostics proved non-no-op).
Build delta +6.3s (19.3 -> 25.6 median) vs ~9s cap: PASS. Hottest analyzer MA0002 18.6s CPU (first knob if
cap tightens). 18 real findings fixed incl. RS0027x2 (ObjectFactory optional-param split - rc-phase surface
change, Unshipped reseeded). One triage disable beyond pre-approved list: MA0006 (string == is compile-time
ordinal + the generator's pervasive name-literal idiom; MA0002/21 still enforce selectable-comparer sites).
One #pragma RS0030: Registry.Map's source.GetType() - the documented runtime-type dispatch, named by the ban
message as the single sanctioned exception. ACCEPTED by controller. PublicAPI: Shipped empty, Unshipped 277
(runtime) + 42 (Testing). Pack + validation green; CP0006 suppression for the intentional rc-phase
IDwarfMapper.Map update-into member. Testing package: EnablePackageValidation WITHOUT baseline (never
published - verified) - accepted deviation, csproj comment. ErrorProne.NET.Structs 0.6.1-beta.1 is a SILENT
NO-OP under Roslyn 5.0.0 host (proven 3 ways incl. forced-severity trip); audit blocked upstream, reference
fully reverted. Suite 7,665/0.
Dispatched: H7 phase 2 + R22-03 as one sequential agent (guard + restructure + NOTE rewrite + sabotage demos
+ DocTooling re-measure expecting Timeout 0; then the all-TrackedSteps caching contract with
caching-breaking sabotage demo, red-at-tip = finding not test-weakening).
Remaining round-21 queue after it: T2, T4, T6, T7, T5, T8. Round 22 PARKED until user signal.
H7 PHASE 2 + R22-03: DONE (09c3dda H7; R22-03 commit follows).
H7: progress guard in DocSnippetInjector.Inject (Stryker-disabled block, invariant comment: two advance
sites, every branch must advance i) + Dedent trim restructured RemoveAt-loops -> FindIndex/FindLastIndex/
GetRange (behaviour-identical incl. empty-region throw). Two new direct-pin tests. Sabotage demos: i++
deleted -> guard throws 25 ms; first/last hardcoded -> trim test fails 31 ms; all reverted. DocTooling
re-measure: 5:04, K193/S54/T0/NC37, 284 scoreable, 67.96 % -> break 67 / low 67 RE-AFFIRMED (identical
floor; the 2 stable Timeouts became ordinary Kills, guard's 7 mutants Ignored via in-source disable,
previous=-1 literal mutant Killed - no guard survivor). Post-run git status clean (RepoWriteGuard held);
stryker-unchanged backups deleted + bins verified string-clean. NOTE 1 rewritten to the verified mechanism
(pre-guard 204 kill = OOM inside the runaway loop racing the clock, NOT "killed before the loop is
reached"); NOTE 5/7 refreshed (score no longer rides on any Timeout). TASKS.md H7 row DONE - Timeout 0
across all legs, exit criterion met.
R22-03: IncrementalCachingTests gains the allowlist-free contract - AssertEveryTrackedStepCached enumerates
run2.Results[*].TrackedSteps wholesale (driver carries BOTH DwarfGenerator and MapToGenerator, so a future
stage or generator is covered on arrival), two facts: rich mapper corpus + validation-root compilation with
non-empty provider manifests (the CompilationProvider-fed path). FINDING at tip: the RFC sketch's bare
enumeration can NEVER pass for any generator - Roslyn's OWN tracked plumbing reports Modified by definition
("Compilation" input node conveys the new compilation; compilationAndGroupedNodes_ForAttributeWithMetadataName
carries the compilation in its output). Filter excludes ONLY Roslyn-named infrastructure (WellKnownGeneratorInputs
+ _ForAttribute* suffixes) - zero product steps exempted; every product step is Cached/Unchanged at tip.
Sabotage demo: Compilation threaded into rootInfo's tuple -> both facts red naming
DwarfMapperAmbientRegistration: Modified (+ SourceOutput: Modified in the root case); reverted, detection
proven undamaged by the filter. Suite 7,669/0; build 0/0.
H7 phase 2: DONE (09c3dda). Guard kills the i++ mutant in 25ms; Dedent restructure kills both RemoveAt
mutants; DocTooling re-measured 5:04, 193K/54S/0T/37NC = 67.96%, break 67 re-affirmed FRESHLY MEASURED.
TIMEOUT 0 ACROSS ALL LEGS - H7 exit criterion met. RepoWriteGuard held; NOTE 1/5/7 rewritten to verified
mechanisms. TASKS.md H7 -> DONE.
R22-03: DONE (cedad48) with one judged deviation, ACCEPTED by controller: the RFC's bare all-steps
enumeration can never pass for ANY generator (Roslyn's Compilation input node is definitionally Modified on
every edit); committed test excludes ONLY Roslyn-named infrastructure (WellKnownGeneratorInputs +
_ForAttribute* internals), zero product steps exempted present or future, non-vacuity pinned, both
generators covered, sabotage demo (Compilation threaded into rootInfo) went red naming the step. Ruling: the
exclusion is a domain correction, not a weakening; revert path noted (git revert cedad48) if overruled.
Suite 7,669/0. Worked-on set CLOSED: T3, H1-H7, T1, R22-03 all landed. Continuing queue: T2 next
(coverage floor + ILVerify; ApiCompat already landed inside T1). Round 22 parked for user signal.

## T2 — coverage floor + ILVerify (2026-08-21, commits 59c75d6 + 026a171; ApiCompat leg landed earlier inside T1 at 62f9ecf)

Per-assembly coverage, full suite (Release, 7,669 tests), coverlet XPlat collector merged by
ReportGenerator 5.5.11 — measured at cedad48 + the four collector refs:

| assembly | line % | branch % | covered/coverable lines | floor (line) |
|---|---|---|---|---|
| DwarfMapper | 77.7 | 76.1 | 280/360 | 77.7 |
| DwarfMapper.Generator | 93.8 | 87.4 | 8341/8892 | 93.8 |
| DwarfMapper.DocTooling | 90.7 | 84.2 | 382/421 | 90.7 |
| DwarfMapper.CodeFixes | 92.4 | 68.4 | 220/238 | 92.4 |
| DwarfMapper.Testing | 83.2 | 82.2 | 641/770 | 83.2 |

Floors = measured line values truncated DOWN to one decimal (the only slack; no cushion), live in
`scripts/housekeeping.ps1` (`$coverageFloors`) with date/commit/rounding rule; gate = line only, branch
informational (Testing branch wobbled 81.9–82.2 across three runs; line was IDENTICAL all three times).
Coverlet refs added to the four test projects that lacked them (DifferentialTests, NegativeCases,
ConsumerTests CleanCorpus + Host) so all 8 test assemblies contribute. DwarfMapper's 77.7 is depressed by
compile-time-only attribute classes (MapToAttribute, FlattenGraphAttribute, ... at 0% — generator metadata,
never instantiated at runtime); left honest per the "don't chase coverage" rule.

Wall-clock (this machine, Release, --no-build): plain suite 74.5 s; collecting suite 92.9 s (**+18.4 s,
~+25%**); ReportGenerator merge ~1.2 s. `-Coverage` folds the collector into stage 1, so its price is the
+18 s, not a second suite run; full `housekeeping -Coverage -ILVerify -SkipExhaustion -SkipAot` measured
81.8 s and 89.1 s end-to-end. **T5 arbitration: trivially inside the ~4.5 min deep-tier headroom** (H6:
mutation legs ≈ 39 min of 44).

ILVerify 10.0.11 over (i) shipped `DwarfMapper.dll` — **fully verified, zero diagnostics** — and (ii)
`DwarfMapper.Gallery.dll` (generated-consumer IL incl. blit/SIMD paths, Ex21/Ex22 MemoryMarshal.Cast
emissions verify clean): exactly 3 Unverifiable instructions, all in the sample's HAND-WRITTEN
`Ex18.Example::Run()` — localloc @0x03/0x2F + cpblk @0x1E = the two `stackalloc` buffers at
18_SpanMap.cs:25–26, confirmed against the ilspycmd IL dump; the generator-emitted
`Mapper::Map(ReadOnlySpan<int>, Span<long>)` verifies clean. Known-failures filter allows only that exact
method signature with the construct named; empty map matches NOTHING ('(?!)' — an empty regex would have
matched everything, caught during self-review). Resolution root: newest installed net10 ref pack (10.0.1),
verified empirically; Gallery's bin dir is its own dependency root. ILVerify wall-clock ~0.5 s both targets.

Both gates sabotage-tested: floor 99.9 → coverage stage fails naming assembly+numbers; known-map emptied →
ILVerify stage fails naming the three errors. Restored, clean run = HOUSEKEEPING PASSED.

Deviations/findings: **H8 filed** (Issues/round20/TASKS.md) — no pwsh existed on the machine and PS 5.1
cannot parse housekeeping.ps1 at all (UTF-8 no BOM; em-dash's 0x94 byte = U+201D closing smart quote under
cp1250, which PS treats as a quote delimiter); installed pwsh 7.6.5 via `dotnet tool install -g powershell`,
plus `dotnet-reportgenerator-globaltool` 5.5.11 and `dotnet-ilverify` 10.0.11. Plan doc carries no T2
checklist to tick (verified — no checkbox syntax in the file). Coverage numbers are informational; the
deliverable is the floor + the regression guard, both live.

Post-closeout probe (advisor): an uncaught `throw` inside the script's try/finally under `pwsh -File`
exits **1** (measured with a minimal structural probe, 2026-08-21) — the gate's failure is carried in the
exit code, not just the printed message, so T5 can wire the switches into CI on the exit-code contract.
T2: DONE (59c75d6 coverage, 026a171 ilverify, f6230d9 H8). Line floors gated in housekeeping -Coverage
(truncate-down-one-decimal rule, vacuity guard mirrors Assert-MutantsWereTested, sabotage-tested): runtime
77.7 (depressed by compile-time-only attribute classes - honest, unchased), Generator 93.8, DocTooling 90.7,
CodeFixes 92.4, Testing 83.2. Branch informational only (Testing branch wobbles 81.9-82.2 across runs).
Collection +18.4s over plain suite - trivially inside deep headroom. ILVerify: runtime CLEAN; Gallery 3
Unverifiable = hand-written Ex18 stackalloc only (localloc/cpblk, IL-dump-confirmed); generator-emitted
span/blit Map verifies clean; known-failures map is signature-exact, empty-map='(?!)' (empty regex would
suppress everything - caught in self-review). NEW H8: housekeeping.ps1 unparseable under PS5.1 (UTF-8 noBOM
em-dash reads as cp1250 smart quote = quote delimiter); pwsh 7.6.5 installed via dotnet tool; durable fix
maintainer's. Queue: T4 next (DWARF_DEEP knob + E4 culture/registry-static isolation + E2 torture timing).

## T4 — DWARF_DEEP knob + E2 timing + E4 culture/registry isolation (2026-08-21)

One env var, ONE reader: `tests/Shared/DeepTier.cs` (linked source, compiled into Generator.Tests,
IntegrationTests, Testing.Tests). Unset/0 = byte-for-byte today's counts; `DWARF_DEEP=1` = catalog
multipliers. `DeepTierSelfTests` (5 tests): fast column pinned to an INDEPENDENT copy of the historical
counts, deep > fast for every entry, scan-ban on direct env reads outside the helper, and every catalog
entry must have a call site. `scripts/housekeeping.ps1 -Deep` sets the var for stage 1 (DWARF_FUZZ_FULL
idiom); verified end-to-end (`-Deep -SkipExhaustion -SkipAot` = HOUSEKEEPING PASSED, deep counts).

### Population inventory (fast → deep, multiplier rationale at each DeepPopulation member)

| population (class) | fast | deep | why this multiplier |
|---|---:|---:|---|
| FeatureCombination subset order | singles+pairs+all | + all C(16,3)=560 triples ×5 consumers (+2,800 cases) | next complete structural tier |
| BehavioralFuzzTests seeds | 60 | 600 | ×10, ~87 ms/seed |
| ExtendedBehavioralFuzzTests seeds | 40 (200–239) | 400 (200–599) | ×10 |
| AllEmitPathsAgreeFuzzTests seeds | 50 | 250 | ×5 — ~300 ms/seed, ×10 would make one serial class the suite ceiling |
| CompilesAlwaysFuzzTests seeds / advanced | 200 / 50 | 1000 / 250 | ×5 — two theories dial together in one serial class |
| DeterminismFuzzTests broad seeds | 120 | 1200 | ×10, ~18 ms/seed |
| IndependenceOracleFuzzTests seeds | 60 | 600 | ×10 |
| MetamorphicPropertyFuzzTests seeds | 50 | 250 | ×5 — two theories share the class |
| TopologyOracleFuzzTests graph seeds | 12 | 120 | ×10 (three theories) |
| FlattenGraphFuzzTests homo / hetero | 25 / 15 | 250 / 150 | ×10 |
| CrossConfigFuzzTests update seeds | 20 | 200 | ×10 (pair matrix already exhaustive, no knob) |
| CultureInvarianceFuzzTests behavioral / advanced | 8 / 4 | 80 / 40 | ×10 (InlineData → MemberData, fast set identical) |
| DocPipelinePropertyTests CsCheck iters / scan | 500 / 200 | 5000 / 2000 | ×10, in-process text transforms |
| Torture create / update rounds | 60 / 240 | 240 / 960 | ×4 — the multiplier the update side's measured power history justified |
| PolymorphicMemberFuzzTests seeds | {1,2,3,5,8} | + seeds 9–48 (45 total) | fast keeps the exact original five |
| ObjectFactoryV2Distribution seeds | 400 | 4000 | ×10, all reach assertions are existence/depth claims |

### Measurements (Release, --no-build, this machine quiet — no concurrent agent builds; every number stated)

- Pre-change baseline, full sln: 66.1 / 63.9 / 65.8 s (median 65.8), 7,669 tests.
- Post-change fast tier: 62.9 / 62.5 / 65.3 s (median 62.9), **7,674 tests** (+5 = the self-tests;
  every population count byte-identical, IntegrationTests still 789). Fast tier cap held.
- Deep tier: **15,786 tests** (+8,072 theory cases, exactly the catalog-computed delta, +40 polymorphic),
  **104.9–106.5 s** ⇒ **delta ≈ +42 s vs fast median** against the ~3.5–4 min ceiling (44:00 − 39:24
  mutation − 18 s coverage). Zero failures. Headroom deliberately left to T5 rather than spent: the ×5
  reductions above are the budget trade recorded per the ceiling-wins directive — the binding constraint
  is per-class serialization (xunit parallelises across classes, never within one), so the marginal deep
  second buys least exactly where a single class would dominate.

### E2 — torture collection × 49, measured (was: wrongly "refuted" on kill-count)

Fast: 13 tests, **1.04 s in-test** (trx sum; the two one-key storms are 0.68 + 0.18 s of it),
1–2 s reported, 3.2/3.7/4.2 s command wall. **×49 = ~51 s in-test ≈ 1.9% of the 2,642 s mutation run**
(~181 s ≈ 6.9% even charging full host-start wall, which Stryker pays per mutant anyway). R21-3's
"10 s per pass → ~8 min" speculation is refuted; hypothesis (2) is a minor co-cause. No follow-up H-row:
nothing here dominates anything, the 240-round decision stands, E1's covering-set story stays whole.
Deep (×4 rounds): 3.6/4.6/4.6 s in-test, 6.3/6.7/6.7 s wall — trivial inside the deep delta.

### E4 — culture-swap isolation + registry-static audit

`culture-swap` serial collection (ConversionDefensive, ParsableConversion, SecurityRegression — the only
CurrentCulture mutators in the assembly); assembly-wide `DisableTestParallelization` REMOVED, replacement
comment in AssemblyInfo.cs carries the expired-cost citation and the audit. Registry audit result: all
registering tests outside `registry-torture` use distinct closed types + per-key asserts; the one
`Provided` read outside it is Contains-of-own-key (AmbientRegistryTests) — safe under concurrent
registration of different keys. Torture collection stays serial (contention vs noise, unchanged).
Measured: assembly median 3.91 → 3.50 s (3 runs each; ~10%, near noise at suite scale — kept for
correctness isolation + per-mutant multiplication under Stryker, per the "worth having at zero speed
win" call). 3 full-suite runs + 1 deep run after: **0 flakes** — no H-rows from re-enabled parallelism.

### Deviations / scope notes

- Plan-T4's third bullet (NEW CsCheck property tests for the runtime registry / TypeFacts / C6 survivors)
  was NOT in this task's scope and overlaps T7 — deferred to T7 explicitly, not silently dropped.
- The full 2^16 power set stays behind DWARF_FUZZ_FULL (its own ~6 min switch, own housekeeping stage);
  DWARF_DEEP deliberately does not imply it — the deep delta must fit T5's budget, the power set has its
  own line item.
- ExtendedBehavioralSeeds deep (200–599) overlaps BehavioralSeeds deep (0–599) seed ranges; different
  oracle over the same schema, harmless — the fast tier's "no overlap" property is preserved exactly.
- No new H-rows from T4/E2/E4: no flake, no measurement surprise that demands action.

T4: DONE (knob + self-test commit, E4 commit, this ledger + TASKS.md commit). E2/E4 rows updated with
numbers in Issues/round20/TASKS.md.

Post-closeout note (advisor): E4's scope is IntegrationTests only. Generator.Tests also contains culture
swappers (CultureInvarianceFuzzTests, TurkishCultureMatchingTests) in an already-parallel assembly —
pre-existing state, safe by mechanism (CurrentCulture setter is thread-local, restored in finally, flows
with the swapping test's own ExecutionContext), but the deep tier makes those swap windows ~10× more
frequent there. Stated so E4 cannot be misread as repo-wide culture serialization.
T4+E2+E4: DONE (568c459, a647e58, f35280b). One DeepTier reader, 21-entry catalog each with rationale,
self-tests pin fast counts to an INDEPENDENT historical copy + deep>fast + env-read scan-ban + call-site
requirement. Fast tier unchanged (62.9s median vs 65.8 baseline, within noise; counts byte-identical).
Deep: 15,786 tests (+8,072 cases = exactly the catalog-computed delta), +42s - far under ceiling, headroom
left to T5 deliberately. E2 MEASURED: torture x49 = ~51s in-test = ~1.9% of the mutation run; the ~8min
speculation refuted; 240-round decision stands. E4: 3 culture mutators -> serial collection, registry-static
audited (distinct closed types + per-key asserts; one safe Contains read), ~100 classes re-parallelized,
3.91->3.50s median (~10%, near noise, kept for isolation), 0 flakes in 4 verification runs. Deferred
explicitly: plan-T4 bullet 3 (CsCheck registry properties) -> T7's scope. Queue: T6 next.

## T6 — EmittedInvalidCode 10 → 0: B33 (correct emission) + B27 (DWARF094)

Two commits, one per cell family, each ceiling movement re-measured in its own commit (10 → 8 → 0).
The population the matrix file says must not exist is EMPTY, and its exact pin at 0 plus the end-to-end
SurfaceProbeTests pin (the old exemplar cell now asserted Refused/DWARF094) make a regression red twice.

### B33 — Preserve@Span/AsyncStream CS7036: correct-emission, and the sibling hunt paid twice

Root cause: the Preserve second pass patches the span/async models' element member to
ConverterNeedsDepthCtx=true (the synthesized converter gains a `(DwarfRefContext, int)` tail), and
EmitSpanMapMethod/EmitAsyncStreamMapMethod ignored the flag — `conv(x)` against a 3-parameter converter,
CS7036 in the .g.cs. Correct-emission over refusal was NOT a judgement call: the top-level collection path
already defines element-wise Preserve semantics (one ctx per call, shared across elements), so the emitters
now allocate one shared DwarfRefContext (flags per mode, mirroring the create map's block) and pass
`(ctx, 0)` per element. Sibling findings, both sabotage-verified red pre-fix: (1) the SAME missing tail
reaches CS7036 with no Preserve in sight — a recursive element pair under default None; (2) under
OnCycle=SetNull the SetNull post-pass ALSO skipped span/async models because it read the PARAMETER type
(a span struct) where the ELEMENT is what cycles, so the shared ctx would have lacked its stack set —
pass extended, and the emitter-only sabotage run proved the extension load-bearing (SetNull span test
fails without it). Executing pins per B19 (the matrix grades difference, not correctness):
ElementWiseReferenceHandlingRuntimeTests — same source object in two span slots / stream elements maps to
the SAME instance under Preserve, a diamond spanning two elements stays one child, None throws
DwarfMappingDepthException on a cyclic element (bounded, H7-clean), SetNull cuts the back-edge to null.

### B27 — [GenerateMap] same-signature collision: refusal, DWARF094, five-file sync

Root cause: ONE detection gap for all 8 cells — the DWARF060 signature pass `continue`d on identical
(name, params, return) as "a duplicate-pair concern" that nothing downstream owned, so both the duplicate
attribute (the ×2 axis, and the co-located host whose template already declares the pair) and the
declared-partial collision emitted twice → CS0111 + CS0121 cascade in the .g.cs. Refused (Error) in that
same pass, message per shape, synthesized duplicate dropped (DWARF060's pattern; the Error suppresses the
class anyway, so the consumer sees the refusal — CS0111-absence pinned for both mode 1 and the mode-2
host). Five-file sync: descriptor + AnalyzerReleases.Unshipped + docs/diagnostics.md (fence-exempt
illustration) + NegativeCases DWARF094 case (both remedy wordings pinned; DWARF078+CS8795 co-expected)
+ CHANGELOG; diagnostics-index regenerated by the doc self-heal. Knock-on wording: DWARF093's sharp-edge
sentence named the raw CS0111 — message, its EXPECT-MESSAGE pin, docs and the WrapperMapExpansionReach
test all moved to DWARF094 together. Sibling search: wrapper-expanded pairs flow through the same genPairs
list AND ExpandWrapperMaps already defers to an explicitly-declared closed pair — that existing guard is
now pinned (removing it fails a test instead of resurfacing as broken emission). Legal neighbours pinned
per the B4 lesson: differently-named partial over the same pair, shared-source (stays DWARF060, no
double-report), shared-target overloads, wrapper+explicit-closed-pair. DirectCompileErrorCallBaseline
53 → 55 (two sanctioned CS0111-absence assertions, rationale in FixtureAdoptionScanTests).

### Ceilings and populations, measured not predicted

EmittedInvalidCodeCellCeiling 10 → 8 (B33 commit) → 0 (B27 commit), exact pin both times. The 2 B33 cells
now read Honoured, the 8 B27 cells Refused (DWARF094, behind CS8795 where the endpoint declares a
partial); every other ceiling byte-identical — MaxDepth@Span/AsyncStream divergence measured unmoved (its
cells are None-mode, non-recursive pair, so no ctx-tailed converter is in play). Matrix 866/866.

T6: DONE (37210f2 B33, bda57e0 DWARF094/B27, fa98664 stale-prose sweep). Final gate at HEAD: solution build
0 warnings / 0 errors (samples included), full suite 7,691 passed / 0 failed across 8 projects, matrix
866/866, EmittedInvalidCode pinned EXACTLY at 0. No cells resisted; no H-rows. Note for the maintainer's
CLAUDE.md scratch item 2: its fence counts ("37 total / 8 exempt / 5 in docs/diagnostics.md") were stale long
before T6 (docs/diagnostics.md alone now carries 28 fence-exempt markers; dwarf094 added one more) - the
enforcement ratchets are green either way, only the scratch note's numbers drifted. Queue: T7 next.
T6: DONE (37210f2 B33 correct-emission, bda57e0 B27 refusal DWARF094, fa98664 stale prose).
EmittedInvalidCode 10 -> 0 EXACT PIN, both re-measured in-commit. B33 root: span/async emitters ignored
ConverterNeedsDepthCtx (sibling-guard-not-inherited #10); sibling hunt found TWO more live defects, both
sabotage-verified: same CS7036 under default None with recursive element pair, and SetNull post-pass reading
parameter type not element type. 7 executing runtime tests pin semantics (matrix grades difference, not
correctness - B19). B27 root: DWARF060 pass continued on identical signature; refusal chosen (positional
directive binding makes silent dedupe actively dangerous); DWARF094 minted, five-file sync + index 89->90;
DWARF093 wording repointed; wrapper-dedupe pre-existing guard now pinned (initial test REFUTED by
measurement and rewritten - honest). DirectCompileErrorCallBaseline 53->55 with rationale. Suite 7,691/0,
matrix 866/866. Noted, not fixed: CLAUDE.md scratch-note fence numbers stale (maintainer's file).
Queue: T7 next (C6 survivors + T4's deferred CsCheck registry properties), then T5, T8.
R22 97%-gates research: DONE, committed on master (5123a18 + rulings 3a3dff8). USER RULINGS: (1) per-dimension
reframe ACCEPTED; (2) adjudication markers REJECTED - equivalents ledger-only, gates work on RAW scores with
the ceiling gap as documented offset; (3) deep-tier 44-min ceiling RAISED - all legs run every night, no
rotation. DIRECT IMPACT ON T5: assemble the nightly with EVERYTHING (runtime+generator+doctooling mutation,
coverage, deep suite, ILVerify, blame-hang backstop), record the measured total, no arbitration needed; fast
tier cap unchanged. The old 44-min arbitration language in the plan/H6 is superseded by this ruling.

## T7 — C6 runtime mutation survivors + T4's deferred CsCheck registry properties (2026-08-21)

### The three named kills, per-mutant, killedBy-confirmed in the JSON

All test-side; src untouched (preferred path — no H-row needed). Report:
`StrykerOutput/2026-08-21.19-37-59/reports/mutation-report.json`.

1. **`DwarfMapperRegistry` duplicate `Register` → `InterfaceMaps` (old `:76`, now the `return;` at L76 +
   siblings L75/L81/L82)** — the stated invariant (mirror appended ONLY on a successful `TryAdd`) had zero
   tests. Killed by `RegistryInterfaceLookupTests.A_duplicate_interface_registration_is_marked_but_does_not_
   poison_lookup`: the same interface pair registered twice must be MARKED ambiguous yet still resolve with
   the FIRST delegate through a single mirror entry — the mutant lets the duplicate fall through, one
   registered pair reads as two candidates, and single-candidate resolution throws. L75 additionally killed
   by `RegistryPropertyTests.A_duplicate_registration_is_first_wins_and_marked`.
2. **`DwarfMappingDepthException` (old `:32`, L28–L32)** — all 5 mutants (4 string + ctor block removal)
   killed by `DwarfExceptionContractTests.Depth_exception_carries_both_depths_and_the_remedies`: pins
   MaxDepth/ActualDepth properties AND the message fragments (both figures — 64/65 chosen to not collide
   with the 1000 hard-cap digits — both remedies, the cap).
3. **`DwarfMapExceptions.FormatMessage` iterator fork (old `:95`, L95+L97)** — all 7 mutants killed. The
   load-bearing new direction: `DwarfExceptionContractTests.A_plain_type_is_told_to_declare_the_pair`
   asserts the DECLARE remedy (and DoesNotContain the iterator one) for an attribute-nameable type — the
   direction nothing asserted, so flipping the heuristic TRUE-for-everything used to survive. The other
   direction was already pinned by `RegistryInterfaceLookupTests.A_lazy_iterator_with_no_map_at_all_is_told_
   to_materialize` (postdates the 61.02 measurement). The remaining branches (ambiguous-interface naming,
   update-into key-space sentence) pinned in the same file.

**`:291` (`Key.Equals` `&&`→`||`, L282 at this measurement) SURVIVES DELIBERATELY** — ruled equivalent in
practice (C6 row, CF §5.4): diverges only on an engineered hash collision with exactly one matching key
component. Left unchased, stated in the config comment and the C6 row.

### T4's deferred bullet — CsCheck properties over the ambient registry

`RegistryPropertyTests` (IntegrationTests; CsCheck 4.7.0 added to that csproj, version from central pins):
three properties — register/lookup round-trip (`Register`→`IsProvided`/`TryGet`/`Map` agree per key, re-
registration never unregisters), duplicate handling per the stated invariant (first-wins + MARKED, poison
registered inside the iteration), ordered-key identity (reversed/reflexive/cross-entry keys resolve nothing;
create never surfaces from the update table). No reset hook exists (D-b) and none was added: per-property
POOLS of closed generic types (torture-suite convention), per-key assertions only, delegates derived from
the KEY not the iteration, deterministic baselines in the static ctor — order- and thread-independent under
CsCheck's parallel sampling, so NO serial collection (the E4 audit statement in AssemblyInfo.cs remains
true). Iterations dialed via new `DeepPopulation.RegistryPropertyIters` (fast 200, deep ×10), catalog entry
with rationale + independent pin in `DeepTierSelfTests` in the same commit; both self-test scans green
(call-site exists, deep > fast, no direct env reads).

### The runtime leg, re-measured (quiet machine, foreground-idle)

`dotnet stryker --config-file stryker-config.runtime.json` from the repo root, per T3/H1 precedent.

| | 2026-08-17 (C1 merge figure) | 2026-08-21 T7 |
|---|---|---|
| score | 61.02 % | **87.61 %** |
| detected / scoreable | 72 of 118 (71 K + 1 T) | **99 of 113 (99 K + 0 T)** |
| survived / no-coverage | 39 / 7 | **12 / 2** |
| wall-clock | 12 min 25 s | **11 min 36 s** |

Zero timeouts under the 120 s cushion — the rise is real kills, not clock artifacts: rounds 20–21 added the
interface-lookup + update-table families and T7 added the targeted kills + properties. Scoreable 118 → 113:
ResetForTests deletion (D-b) removed its 5 NoCoverage mutants; interface lookup added scoreable code.
`break` 61 → **87** (87.61 floored, same commit as this measurement); `low` 80 → **87** because Stryker 4.16
refuses `low < break` WITH EXIT 0 (H1) — a lagging `low` makes the leg silently unrunnable. The 66-note
stays as history of the timeout-inflation mechanism.

Remaining 12 survivors + 2 no-coverage, mapped to E3-E1's holes: null-guard statement removals on
`Map` (L133–134) and `Update` (L258–261) [holes 3, 2]; three iterator-remedy string tails L98/L101/L104
(the fragments no test quotes — killable by extending the lazy-iterator assertions, not attempted here) [hole 11, 4 of its 16 with L86];
`ambiguousInterfaces is { Count: > 1 }` → `>= 1` (L86) — only observable on a 1-element list, which `Map`
can never produce (count==1 resolves instead of throwing), so near-equivalent via the public path;
`Key.Equals` `||` (ruled, above); facade `TryGet` guard `&&`→`||` (IDwarfMapper L73) [hole 13];
`Equals(object)` override NoCoverage (L286) [hole 7]; `TryEnterNode` bool NoCoverage (DwarfRefContext L155)
[hole 10].

### Housekeeping after the run

Post-run `git status`: byte-clean (only the intended edits existed, already committed pre-run — the measured
tree was a committed tree). Eight `DwarfMapper.dll.stryker-unchanged` backups across the test bins deleted;
every live sibling `DwarfMapper.dll` string-scanned clean of Stryker markers first (H7 phase-2 precedent).
Stryker restored the clean assemblies itself; no decontamination rebuild needed.

Session note: the 14:47 `StrykerOutput/2026-08-21.14-47-41` DOCTOOLING-population report predates this task
— it is the H1-era doctooling re-run already recorded in the T3 ledger, not a wrong-config invocation from
T7; T7 ran exactly one leg, the runtime one, at 19:37.

T7: DONE (c613e7f tests + DeepTier entry; 3f71225 break 61→87 + TASKS; a5621ac C6-row hole-number
correction caught by review — holes 1 and 5 are CLOSED in this measurement, the survivors map to 2–3, 6,
11-partial, 13 + NC 7, 10). Deep tier of RegistryPropertyIters executed once green (DWARF_DEEP=1, 46 ms).
C6 → DONE with numbers; T4 deferral drained. Queue: T5 next, then T8.
T7/C6: DONE (c613e7f tests+DeepTier, 3f71225 break-ratchet+TASKS, a5621ac C6 correction). Runtime leg
61.02 -> 87.61% (99K/12S/2NC of 113 scoreable, 0 Timeout, 11:36 wall), break 61->87, low 80->87, same-commit
measurement. All 3 named survivors + siblings killed test-side (src untouched); :291 equivalent left per
ruling, stated in config+row+ledger. Registry property tests (CsCheck, no reset hook - closed-type pools,
order-independent, NOT serialized) + DeepPopulation.RegistryPropertyIters 200/x10. Scoreable 118->113
(ResetForTests deletion removed 5 NC). Remaining survivors mapped to E3-E1 holes 2-3, 6, 11p, 13 + NC 7,10 -
in C6 row. Suite 7,699/0. The 14:47 doctooling report predated T7 (H1-era re-run) - no wrong-config run.
Agent background wake-ups failed twice (harness issue) - resumed by controller ping; monitor pattern worked.
Queue: T5 (nightly assembly under RAISED-CEILING ruling - all legs nightly, record measured total), then T8
(benchmark smoke + allocation exact-pin per R22 research).

## T5 — the nightly deep tier assembled (2026-08-21)

Executed under the maintainer's RAISED-CEILING ruling (2026-08-21, recorded on master in
Issues/round22/RESEARCH-97-PERCENT-GATES.md, commit 3a3dff8): ALL legs run EVERY night, no rotation, no
44-minute arbitration — the plan's "fail if total > 44 min" was NOT implemented, deliberately. Record the
measured total instead (below).

### What landed

**ci.yml — the `mutation` matrix (was `runtime-mutation`).** One job, three legs via
`strategy.matrix.include`: runtime (stryker-config.runtime.json, timeout 120), generator
(stryker-config.json, timeout 200), doctooling (stryker-config.doctooling.json, timeout 60) — timeouts per
the existing ~10x-measured idiom (11:36 / 19:41 / 5:04-5:30 measured on 12 cores; hosted runners have ~4).
`fail-fast: false` so one leg's break cannot cancel the others' nightly reports. Same schedule guard as
before (cron 17 3 * * * + workflow_dispatch only). Per leg: (1) H4 pre-flight "Assert the config is
runnable" — python3 checks thresholds.break <= thresholds.low BEFORE the tool install, because Stryker 4.16
refuses a mis-ordered config AND EXITS 0; (2) pinned stryker 4.16.0; (3) the run; (4) the generalized
non-vacuity guard (leg-named, counts scoreable statuses in the newest report); (5) NEW "Assert the tracked
tree survived the leg" — `git diff --exit-code`, turning RepoWriteGuard's post-H1 clean-tree contract into
a checked CI invariant; (6) `mutation-report-${{ matrix.leg }}` artifact, if: always(). The runtime leg's
artifact keeps its exact old name (mutation-report-runtime).

**ci.yml — the `deep-test` job.** Same schedule guard. checkout, setup-dotnet 10.0.101, pinned tool
installs (dotnet-reportgenerator-globaltool 5.5.11, dotnet-ilverify 10.0.11 — the locally verified
versions), restore, build Release, then ONE command: `pwsh scripts/housekeeping.ps1 -Nightly`. The job body
is deliberately the maintainer's local recipe so CI and local cannot drift. timeout-minutes 30 = measured
(~105 s deep suite + ~18 s coverage + ~15 s ReportGenerator + ~1 s ILVerify + ~3 min build + ~1 min tools
≈ 7 min local, ~3x for a 4-core runner ≈ 20, +50%). Coverage report uploaded if: always().

**housekeeping.ps1.** New `-Nightly` aggregate = -Deep -Coverage -ILVerify + SkipExhaustion + SkipAot,
mutation NOT implied (own cost class). New `Assert-StrykerConfigSane` (H4's other half) — break <= low per
config, called for ALL THREE configs before leg 1 of -Mutation so a leg-3 mistake fails in milliseconds.
Stage-1 `dotnet test` now carries `--blame-hang --blame-hang-timeout 5m` (H7-f; exhaustion stage and
Stryker legs deliberately excluded). ILVerify ref-pack resolution made cross-platform for the CI job:
DOTNET_ROOT / dotnet-on-PATH / ProgramFiles packs dirs then the NuGet cache, newest by [version] with
prerelease suffixes stripped (string sort ranks 10.0.9 above 10.0.11).

**Blame-hang (H7-f, dissection §5 item 4)** on every plain `dotnet test` step: build-test, surface-matrix,
roslyn-forward-compat, housekeeping stage 1 (hence deep-test). Smoke-verified locally WITH the XPlat
coverage collector in one invocation: accepted, "all tests completed, sequence file not generated",
coverage attachment produced. Stryker legs excluded — per-mutant ceiling plays that role.

**CiGateScanTests** grew rows for `mutation` and `deep-test` (with the honest caveat in-comment: nightly
jobs only trigger on the default branch's cron, so "declared" is an even weaker claim there).

**Config comments** (all three stryker-config*.json): the CI sentence now names the 'mutation' matrix job
and the leg. Old ledgers (E3-E1) still say 'runtime-mutation' — history, left alone.

### The coverage floor incident (found BY this task's verification, fixed per protocol)

First -Nightly run: deep suite 15,786-equivalent all green, then the floor gate threw — Generator 93.7%
vs floor 93.8%, and ILVerify never ran. NOT a deep-tier artifact and NOT a lost test: fast tier at HEAD
measures the identical five line values (Generator raw 93.781%, 8,384/8,940). T6 (B27's DWARF094 refusal,
B33's context threading) grew the coverable-line denominator AFTER the floors' measuring commit (cedad48).
Floors re-measured at HEAD in the wiring commit per the file's own protocol: DwarfMapper RAISED 77.7 ->
78.3 (normal move), Generator LOWERED 93.8 -> 93.7 with the written reason in the comment + commit,
others unchanged (90.7 / 92.4 / 83.2). Deep and fast agree to the decimal on the same tree — the
superset claim in the deep-test job comment is corroborated, not assumed.

### Verification actually run (and its limits)

- yaml: python yaml.safe_load — parses; 9 jobs incl. mutation + deep-test. No actionlint available.
- H4 gate: positive (all three configs pass, CI script body extracted from the parsed YAML and executed)
  AND negative (crafted break=90 config: CI step exits 1 with the ::error, pwsh function throws).
- housekeeping parse (pwsh 7.6.5 Parser::ParseFile): clean — H8's encoding hazard not aggravated.
- Full `-Nightly` end-to-end AFTER the floor fix: HOUSEKEEPING PASSED (deep suite green with blame
  collector active, five floors met exactly, ILVerify: runtime fully verified, Gallery 3 known
  stackalloc findings matched). Whole-solution Release build: 0 warnings / 0 errors. Fast-suite-green is
  subsumed: the deep tier is the same test set with multiplied populations, run without the SurfaceMatrix
  exclusion — a superset of the fast run, and it passed.
- UNEXERCISED, stated plainly: (1) the nightly jobs themselves — `schedule` fires only on the default
  branch, so both stay dormant until merged to master; workflow_dispatch can exercise them by hand before
  that. (2) The Linux path of the ILVerify ref-pack chain — verified on Windows plus by construction
  (DOTNET_ROOT is set by setup-dotnet; NuGet-cache fallback behind it); its first real run is the job's.
- Push note: ci.yml changes need the `workflow` OAuth scope at push time.

### The measured nightly total (the ruling's "just record it")

Mutation aggregate ~36:47 (runtime 11:36 + generator 19:41 + doctooling ~5:30) + deep-test ~2:15 of
gate work ≈ **~39-40 min measured compute**, before per-job restore/build/tool-install overhead (~3-5 min
x 4 nightly jobs on hosted runners). The four jobs run in PARALLEL on separate runners: nightly
wall-clock ≈ the generator leg, not the sum.

### Rows

H4 -> DONE (both halves: pre-run config sanity + per-leg scored-report/non-vacuity, local and CI).
H6 -> superseded note recorded in the row (ruling cited by master path+commit, totals recorded).
H7 row + dissection ledger -> §5 item 4 closure noted. H8 untouched per brief (maintainer's).
T5: DONE. Queue: T8.
T5: DONE (29351b1 wiring, d7b60cd records). deep-test job = pwsh housekeeping -Nightly (CI IS the local
recipe, no drift), timeout 30. runtime-mutation -> mutation MATRIX (runtime/generator/doctooling, fail-fast
false, timeouts 120/200/60), each leg: H4 pre-flight break<=low BEFORE tool install, non-vacuity guard,
git diff --exit-code clean-tree invariant (H1 contract CI-checked), per-leg artifacts. Housekeeping mirror
Assert-StrykerConfigSane. Both guards tested positive AND negative. H4 -> DONE. blame-hang 5m on all plain
test steps (excl. Stryker legs, exhaustion stage, pre-push hook - scoped ruling). Totals: ~39-40 min compute,
jobs parallel so wall ~= generator leg; 44-min gate NOT implemented per raised-ceiling ruling (H6 superseded
note cites 3a3dff8). -Nightly aggregate added; ILVerify ref-pack chain made cross-platform (Linux path
by-construction only - meets reality on first CI run). FOUND: first -Nightly failed HONESTLY - T6 emission
grew Generator coverable denominator; floor 93.8->93.7 with written reason, DwarfMapper 77.7->78.3 same
measured pass; fast==deep to the decimal. Full -Nightly PASSED end-to-end. Dormant until master; ci.yml push
needs workflow OAuth scope. Agent wake-failures: 3rd occurrence confirmed; monitor+ping pattern is now the
standing workaround. Queue: T8 (last round-21 task).

## T8 — benchmark smoke + allocation exact-pin gate (2026-08-21, last round-21 task)

Plan T8 EXTENDED by the maintainer-ruled rider (Issues/round22/RESEARCH-97-PERCENT-GATES.md §4,
allocation-gate row, verdict adopt-r22-with-T8): the smoke leg carries an allocation exact-pin gate.

### What landed

- **Smoke mode** (`benchmarks/DwarfMapper.Benchmarks/Program.cs`): `SmokeConfig` — DefaultConfig +
  `Job.ShortRun` (1 launch, 3 warmup, 3 measured) + `JsonExporter.Full`, activated by
  `DWARF_BENCH_SMOKE=1` (env, not CLI, so the args-forwarding contract stays untouched). The
  timing-numbers-are-NON-GATES statement lives in the config's own doc comment, as the rider demands.
- **Baseline** (`benchmarks/DwarfMapper.Benchmarks/allocation-baseline.json`): 13 `*_Dwarf` scenarios
  pinned byte-exact; SDK (10.0.101), runtime, OS, date, tree recorded inside; `totalBenchmarks: 41`
  pins the executed count (BenchmarkRunner exits 0 for NA/crashed rows and for a filter-shrunk suite —
  the count + per-row Statistics/Memory presence check makes "every benchmark still runs" mechanical);
  `unstable: {}` — empty, see stability verification. Update protocol written in the file: the gate
  fails on BOTH directions (unexplained increase = regression, unexplained decrease = also a finding);
  the fix is a deliberate re-measure + baseline update with the reason in the same commit, so
  `git log -1 -- allocation-baseline.json` is always the measuring commit.
- **Gate** (`scripts/housekeeping.ps1 -BenchSmoke`, folded into `-Nightly`):
  `Assert-BenchAllocationsPinned` — count pin, per-row crash detection, exact pins with direction-naming
  failures, unaccounted-`*_Dwarf` guard (a new benchmark cannot land unpinned), SDK-drift check (the one
  legitimate silent-change vector for allocated bytes, named instead of masquerading as a regression),
  stale-results wipe before the run (the coverage-wipe analog). Competitor rows print as
  `[info]`, never gated. A separate function so it could be tested negatively without a third smoke run.
- **ci.yml**: no step change needed — deep-test runs `housekeeping -Nightly`, which now carries the
  smoke (CI IS the local recipe, no drift). Timeout re-derived 30 → 45 in the comment's own style;
  nightly-cost comment updated; `bench-smoke-results` artifact uploaded `if: always()`.

### Measured (the placement decision's inputs, not predictions)

- Smoke wall-clock: **6:36 (396 s)** run 1, **6:44 (404 s)** run 2 — full 41-benchmark suite, 12-core
  machine, `dotnet run --no-build`. Single-digit minutes as demanded; proportionate to the raised-ceiling
  nightly (mutation aggregate ~37 min rides sibling jobs), so it FOLDS INTO `-Nightly` rather than
  standing alone.
- Stability verification (the rider's CAVEAT, verified first): **all 41 benchmarks byte-identical
  across the two runs** — every one of the 13 DwarfMapper scenarios included, so all 13 are pinned and
  the unstable set is empty. No JIT-determinism pin was needed.
- Pins (B/op): Flat 40 · Nested 112 · Array 48048 · Seq 48088 · List 48112 · Blit 12048 · Widen 8048 ·
  Flatten 48 · Enum 24 · Dict 31120 · NullMismatch 32 · Set 17856 · Immutable 4048.
- **No bit-rot**: the suite builds 0/0 against current source (the RS0027 `ObjectFactory.Create` overload
  split did not bite — `Create<T>(int)` still exists and `RealisticPayloads` calls it) and all 41
  benchmarks executed in both runs. No H-row needed; nothing was fixed because nothing was broken.

### Verification actually run

- pwsh 7.6.5 `Parser::ParseFile` on housekeeping.ps1: clean (H8: new comments ASCII, no BOM).
- Gate POSITIVE: real baseline vs run-2 report — passed, 13 pins matched, table printed.
- Gate NEGATIVE (T5/H4 discipline, via AST-extracted function against the real report + doctored
  baseline): all five doctorings reported in one throw — increase direction (Enum pinned low), decrease
  direction (Flat pinned high), missing pinned row (bogus pin), unaccounted `*_Dwarf` (Set pin removed),
  count mismatch (40 vs 41).
- Whole-solution Release build: 0 warnings / 0 errors. Fast suite: green, all 8 projects, 0 failures.

### UNEXERCISED, stated plainly

- The pins are **Windows-measured**; deep-test runs ubuntu-latest, and `schedule` fires only on the
  default branch — the gate's first Linux run is post-merge. Managed allocation sizes should be
  identical on linux-x64/same SDK, but that is expectation, not measurement; if Linux disagrees, the
  documented update path (re-measure, update with reason) is the remedy — no per-OS machinery built now.
- ci.yml changes still need the `workflow` OAuth scope at push time (same caveat as T5).

### Rows

T8 → DONE. Round 21 queue empty.
H2 RULED by maintainer (2026-08-21): Sonar REJECTED outright - SSAL's no-unapproved-AI clause is
disqualifying for an AI-developed repo; derivative-works-must-be-public and no-competing-services terms
also cited. LGPL 9.x pin declined too (frozen line). Recorded in the master research doc. H2 row in
TASKS.md flips to DONE in the wrap commit after T8 lands (avoiding an index race with the running agent).

Post-commit stage-level verification (the audit-found-selfreview-misses lesson - the STAGE, not just
the function): `pwsh scripts/housekeeping.ps1 -BenchSmoke -SkipExhaustion -SkipAot` end-to-end -
HOUSEKEEPING PASSED in 8:13 total (suite + smoke 6:36 + gate), 13 pins exact-matched, 41/41 executed.
A THIRD smoke run through the real path with every pin byte-identical - the stability claim now rests
on three runs, not two.
T8: DONE (de23ce0). 41 benchmarks, all byte-identical allocations across 3 runs - unstable set EMPTY, all 13
Dwarf scenarios exact-pinned. Smoke ~6:36 in-stage, folded into -Nightly (proportionate under raised
ceiling); gate tested positive + 5 negative doctorings; deep-test timeout 30->45. No bit-rot (RS0027 suspect
cleared). Dict pin 31,120 B/op vs Mapster/AutoMapper ~102k - the platform-independent allocation lead now
GATED, not just quoted. Linux bytes meet reality post-merge; documented update path is the remedy.
ROUND 21 COMPLETE. Wrap commit 0-per-below; H2 row flipped DONE (Sonar rejected). Branch state: 22 commits,
all gates green, nothing pushed. Open with the maintainer: D-e drafting offer, merge/push approval, round-22
start signal. Post-merge firsts: nightly legs arm on master; ci.yml push needs workflow OAuth scope;
ILVerify Linux path + benchmark Linux pins get their first real run.
