# Round 31 — checklist and completion audit

Source: `ROUND31-TASKS.md` (the full steps live there) and `POST-ROUND30-IMPROVEMENT-RESEARCH.md` (the research ids in
brackets). Final state 2026-09-27 on `feat/round31`; `./round31-audit.sh static` reports **30 DONE, 0 TODO**, the
default lane is green in all nine test assemblies, and the golden corpus passes without `DWARF_GOLDEN_UPDATE`. Every
deviation from a task's literal steps is recorded in `TASK-LOG.md` with its reason.

Legend: `[x]` done · `[~]` done with an open owner step or an explicit remainder · `[d]` decided, deliberately not
built (reason recorded).

**Not pushed.** The branch holds commits touching `.github/workflows` (T19, T20, T21, T22), which this token cannot push;
per the owner's ruling they are committed on `feat/round31` for a workflow-scoped push.

## Tier 1 — hardening

- [x] **T00** Baseline — recorded at `b688bb9`.
- [x] **T01** Advisory-scoped audit suppression [A2] — `e9e09d4`; scan hardened against nested checkouts `3548b70`, `850d821`.
- [x] **T02** Projection null guards via `(object)` [A1] — `0938e21` (+ CS0034 fix). EF/SQLite row answered in T17's finding: translates to `IS NULL`.
- [x] **T03** CVE-2026-32933 PoC pinned [A3] — `ca831d8`.
- [x] **T04** `LocationInfo` tests strengthened [D1] — `031c39a`.
- [x] **T05** Duplicated coverage sources [D2] — `031c39a`.
- [x] **T06** Byte-identity lock confirmed [B1] — 1,014 golden cases.
- [x] **T07** Parameter-ceiling ratchet [B1] — `031c39a` (Roslyn-walk table, 54 rows).

## Tier 2 — structural and performance

- [x] **T08** Extend the bundles [B1] (owner ruling: not one ExtractionContext) — `cd7a09e` ProjectionRequest (15/13/13 → 9/7/7), `f678f2f` FlattenGraphRequest built by the caller (17 → 5, row deleted; fixed a silent `ImplicitConversions` gap), `8a4d075` new `ResolutionSettings` for the conversion family (TryResolveConversion 24 → 17, ResolveUnflattenTarget 23 → 15, ResolveConstructorArguments 21 → 12), `4ea22db` (span/async-stream element strict-mode fix it exposed), `7fad162` ResolveMembers 29 → 26 / ResolveProjectionMembers 15 → 13, `45c8ccc` the coverage/element-wise emitters take `MapperPolicy` (no adjacent policy bools left) and dense-enum validation takes the settings. Ratchet 54 → 52 rows, every remaining row lowered where touched. **Left loose on purpose:** 22 context parameters in 16 methods, each a leaf CONSUMER of one `(Compilation, bool)` pair or of one flag (converters, `TryMeasureMember`, `ConstructorSelector.Select`, `MemberFacts.TryResolvePath`, `JudgeUnscopedIgnores`, …) — no same-typed pair to transpose, and several callers hold no settings; recorded in `45c8ccc`.
- [x] **T09** Registry collection pre-size [P1] — `0af2f2e`, `4a6821f` (helper, not inlining; span read removed).
- [x] **T10** Hoist projection trees [P5a] — `d23184d`. Measured 8.5x / −89 % allocation (`8a4a5ac`).
- [x] **T11** `RegisterMany` [P6] — `ac93420`. Note (`8a4a5ac`): after T14 single `Register` no longer copies the whole list either, so generated startup costs the same both ways.
- [x] **T12** Exact-pair slot [P2a] — `ed69922`, `c65c4fe` (found-only cache, no version counter).
- [x] **T13** In-memory projection route [P5 Case 2] — `761a69a`. Measured ~2,200x at 10 rows, 40x at 1,000.
- [x] **T14** Collection dispatch flat in app size [P2b/P4] — measured `ea57e5e`, fixed `f0dca83` (destination buckets, 55x at 1,000 pairs). Root-generated dispatchers deliberately not built.
- [d] **T15** Case 2 nested selects → general mapping — measured `8a4a5ac`: 1.14x on DTO lists (under threshold), SLOWER on widening. Not built. The widening loss is a round-32 finding.
- [x] **T16** `{Method}Expression` + DWARF112 [P5d] — `e76a231`. EF composition answered in T17's finding.
- [x] **T17** EF precompiled queries [P5c] — `3c342d6`: `Project(q)` never precompiled, `.Select(Expression)` is; DWARF115 + package `build/DwarfMapper.props`.
- [x] **T18** NativeAOT size [P7] — `1f29025`: ambient registration = 10 % of the AOT sample. Recommended opt-out option left for the owner (new public API).
- [x] **T19** Perf lane + benchmarks — CI lane `6747ed9`; benchmarks `d177479`, `924f237`, `ea57e5e`, `8a4a5ac`. P8: inlining no gain; pre-sized Preserve map 2.3x → round-32 candidate; SkipLocalsInit excluded by policy.

## Tier 3 — CI, release, platform

- [x] **T20** zizmor + Harden-Runner [A5] — `0047b49`: 19 findings fixed → 0; harden-runner step 1 in all 17 jobs. (Committed, not pushed.)
- [~] **T21** NuGet Trusted Publishing [A4] — `a6ba0c9`, gated on `vars.NUGET_TRUSTED_PUBLISHING`. **Owner:** the five [H] steps in `docs/RELEASING.md`, then [O] review before the first tag.
- [x] **T22** .NET 11 SDK leg [C1] — `afe8d2f` (existing `preview-sdk-canary` aligned). First run happens on the owner's push.
- [x] **T23** Nullability attributes [A6] — `30ac793`: A6 confirmed (CS8601 leaked into .g.cs), fixed.
- [x] **T24** C# 15 unions refused [C3] — `1628bda` DWARF113 (before the 2026-11-10 deadline).
- [x] **T25** C# 15 closed hierarchies [C2] — `2c6ecfa` DWARF114; no packaging change needed (`IsClosedTypeAttribute`). Same-compilation `closed` classes need a newer Roslyn.
- [d] **T26** Interceptors — gate not met this round: the remaining facade gap is ~8.6 ns; a safe form needs a new public static entry point (owner decision). `PROPOSAL-T26-compile-time-binding.md` question (1) is partly answered: a package props file can set `InterceptorsNamespaces`.
- [d] **T27** CS8795 stubs — owner ruling: keep suppression.

## Tier 4 — test-suite tidy-up and docs

- [x] **T28** Normalize `__` locals [D3] — `a9c91c1`: 41 sites; 19 kept on purpose (they pin a helper family's name).
- [x] **T29** `// Covers:` headers [D4] — `f36991f`: 118/118, 0 TODO(opus).
- [x] **T30** AutoMapper guide [C5] — `5938bb0` (MIGRATION.md §1 extended in place).
- [x] **T31** (added) Runtime-surface architecture tests — `0729198`.

## Final gate

- [x] **T99** — audit 30/30 DONE (`8b763e4`); full solution build + default lane green; round-31 tests 85 + 12 green; golden green without update; CHANGELOG lines for every user-visible task (`e069a20`).

## Owner actions still open

0. **The runtime mutation leg is red: 90.45 % against `break` 97** (housekeeping `-MutationLeg runtime`, 2026-09-27),
   red since T12's first pass (the T26 proposal records 93.13 %). This round's new registry code (`RegisterMany`, the
   destination buckets) has **zero** survivors. Of the 17 survivors, **4 (at 3 sites) were real holes and are killed** by
   `17fe673` (planted RED -> GREEN): through the facade the update slot is invisible, because a miss falls back to
   `Update` on the same declared pair, so gutting `TryGetUpdate` or inverting the slot's found test passed every
   behavioural test. **The other 13 are equivalent by construction** and no honest test reaches them: 9 cache
   fast-path mutants in `ExactPairSlot`/`ExactUpdateSlot` (skip the cache, or skip storing into it, and the same answer
   is re-resolved), 2 `ThrowIfNull(source)` guards CA1062 requires but the enumerable path makes unobservable
   (contract pinned in `5585dc8`), and the 2 depth-clamp boundaries adjudicated in round 30. **Honest ceiling now:
   165/178 = 92.7 %**, so 97 is unreachable while the slots exist. Round 30's precedent was to remove mutable source
   rather than lower the floor (`e01ff23`); here that means the choice the T26 proposal names: **delete the slots**
   (the facade goes back to the registry lookup, 1.25-1.8x slower on the two-type overloads), **replace them with
   T26's compile-time binding**, or **rule the 13 equivalent and re-pin `break` from a fresh measurement**. Not
   re-measured here: the host was reaping background runs for memory.
   **The pipeline leg is owed a clean run.** T08 (`8a4d075`) touched `MemberResolutionContext.cs` and
   `MapperExtractor.Members.Phases.cs`. It ran TWICE on 2026-09-27 (`StrykerOutput/2026-09-27.13-23-40` and
   `.14-17-54`), and both "100.00 %" results are void: 266 and 263 of the 284 tested mutants were TIMEOUTS, which
   Stryker counts as detected. The last good run (`2026-09-26.10-11-27`) had 0 timeouts on the same 284. The cause is
   the machine, not the suite. Both runs overlapped host memory pressure (the host reaped their shells), and a
   timed-out mutant planted by hand (`Members.Phases.cs:59`, `||` -> `&&`) is killed by one failing test in a normal
   1 m 21 s run. Both runs' post-leg decontamination also died on a console-handle error, so every `tests/**/bin` was
   cleared and rebuilt afterwards (all nine suites green). Run `housekeeping.ps1 -MutationLeg pipeline` with Rider
   closed, or on CI. The generator, DocTooling, CodeFixes and
   Testing legs mutate no file this round changed. Coverage floors: all five pass after `59edd01`.
1. ~~Validate against FusedChat~~ **Done 2026-09-27, in a scratch copy** (your tree, which carries uncommitted rc13
   repins, was not touched): packed `1.1.0-r31val.1` from `ce993f4` with package validation on against 1.0.2-rc.1,
   clean; FusedChat restores and builds with 0 errors and no DWARF diagnostic, and none of its 81 warnings is in
   DwarfMapper-generated code (the generated-file ones are all Razor). Tests: 1,603 pass, 3 fail, 4 skipped, and
   **the 3 fail identically on rc13**: `MapDonationToPremiumDocument [Typical/Sparse/Dense]` golden snapshots
   embed the run date via FusedChat's own `BuildDocumentId` (`20260927_…` vs the recorded `20260902_…`), a clock
   dependency in the app, not a mapping change. The other 894 mapping tests pass, the golden-snapshot cases among them
   matching byte for byte - the value-level A/B earlier validations lacked. Worth fixing on the FusedChat side: inject
   the clock into `BuildDocumentId`.
2. Push `feat/round31` with a workflow-scoped token (T19/T20/T21/T22 touch `.github/workflows`).
3. T21 [H]: nuget.org policy, `release` environment with reviewers, `NUGET_USER`, then `NUGET_TRUSTED_PUBLISHING=true`.
4. Decide: T18's ambient-registration opt-out option; T26's static entry point. (T08 is closed; the 16 leaf consumers left loose are listed on its row, should you want them bundled anyway.)
5. Round-32 candidates from measurement: pre-sized Preserve identity map (2.3x); the generated `int[] → List<long>` widening losing to LINQ (1.6x).
