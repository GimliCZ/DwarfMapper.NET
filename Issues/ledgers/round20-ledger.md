# SDD ledger — plan: docs/superpowers/plans/2026-08-16-round20-generator-defects.md

Worktree: C:/Users/Jouda/RiderProjects/DwarfMapper-surface
Branch: feat/surface-coverage-architecture (continues from the round-19 plan, complete at 9f75d8e)
Backlog of everything known-and-not-done: Issues/round20/CARRY-FORWARD.md

Pre-flight: the round-19 final whole-branch review returned 'merge after must-fixes'. Those must-fixes ARE Task 0.
STANDING RULE for every task here: each DeclaredDivergences entry ASSERTS THE DIVERGENCE STILL EXISTS. Fixing a generator defect turns the build RED until (1) the entry is deleted, (2) the three shrink-only ceilings are lowered (currently findings 23 / declared cells 162 / structural 12), (3) Issues/round20/SURFACE-MATRIX-FINDINGS.md is updated. Never lower a ceiling before the fix lands.

Task 0: implementer STOPPED for a ruling before writing code - correctly. My brief assumed a CHANGELOG that predated the diagnostics; it was CREATED THIS ROUND (its own '### Added' says 'This file'), names only ~6 ids explicitly, and there are ~83 live descriptors. 'Every live id must appear' was never satisfiable.
  RULING: frozen-baseline option, with three properties so it is a backlog-with-a-gate and not the seventh allowlist this branch just deleted six of:
    (1) assert EXACT membership, not a count - the set is closed by definition ('ids that existed before CHANGELOG.md did') and can never legitimately grow, so a new id appearing in it is a build failure;
    (2) name it PredatesTheChangelog, comment the freeze point, may only SHRINK - one entry removed each time a diagnostic is written up;
    (3) non-vacuity: the pre-fix run must name EXACTLY DWARF086 and nothing else.
  Scope confirmed DWARF0xx only - the DWARFR01-DWARFR09 range notation is deliberate and would break a per-id match.
  Deferred to CARRY-FORWARD §1 as a tracked obligation: ~80 diagnostics predate the CHANGELOG and have never been announced; the project has never shipped (AnalyzerReleases.Shipped.md is empty), so the first release notes should enumerate them. PredatesTheChangelog IS the worklist.
Task 0: implemented (1aafc7e). Scan9_Every_diagnostic_id_is_announced_in_the_changelog + a non-vacuity twin. PredatesTheChangelog = 76 ids (82 live DWARF0xx - 5 already documented - DWARF086), EXACT-membership, frozen at b723ece. Pre-fix run named EXACTLY DWARF086 and nothing else. Solution builds 0/0. CARRY-FORWARD gained row 1.4 tracking the ~80 unannounced diagnostics.
Task 0: complete (commits b723ece..1aafc7e + carry-forward, review Approved). Baseline verified a GATE not a hatch: exact HashSet membership, math independently derivable (82 live - 5 already documented - DWARF086 = 76). No new repo-root walk. DWARFR exclusion commented with its range-notation reason.
  DEFERRED MINORS -> CARRY-FORWARD 3.8 (shrink-only is prose on BOTH PredatesTheChangelog and the pre-existing DiagnosticTestAllowlist; exact membership means it cannot drift unnoticed, but growth has no gate) and 3.9 (Scan9's control proves the CORPUS is real, not that the ASSERTION is - worth checking across the scan family).
Task 1 (N4): implemented (9193f00). DEFECT IS WIDER THAN THE BRIEF: keyed on the DESTINATION COLLECTION, not on the directives being identical - [FlattenGraph("Entry","Nodes")] beside [FlattenGraph("Other","Nodes")] emits the same CS1912. A duplicate-check would have left half the class in place.
  REFUSE, matching both siblings (DWARF011 [MapProperty] - the exact structural twin: method site, AllowMultiple, two-string ctor, destination-keyed; and DWARF035 [MapDerivedType]). New id DWARF087, Error. FIVE ancillary files synced incl. CHANGELOG (Scan9 from Task 0 did its job on the very next task).
  CEILINGS UNCHANGED at 107, and correctly so: every DWARF Error suppresses emission, so the refused cell now reports CS8795 and JOINED the R4 population (96->97) rather than leaving NotCompilable. CS1912 count is now 0. The cell only becomes Refused once R4 (Task 10) lands - the two tasks are coupled.
Task 1: complete (commits 4aa6172..9193f00, review Approved). Widened diagnosis CONFIRMED from the emitter: MapperExtractor.Flatten.cs:426-435 keys on a HashSet of DESTINATION names, no directive-equality comparison anywhere. DWARF011 verified the verbatim structural twin. diagnostics-index.md hand-render verified BYTE-EXACT (83 descriptors = 83 rows = the '83 diagnostics.' trailer; ordinal sort puts 087 last) so CI will pass.
  Ceiling arithmetic RECONCILES and is mechanism-true, not a skipped step: SurfaceProbe.Classify returns NotCompilable on a new CS error BEFORE looking at DWARF diagnostics, so a refusal (emission suppressed -> new CS8795) CANNOT leave the population. 96+8+2+1 = 107 = 97+8+2+0.
  DEFERRED MINORS (Task 1): (a) task-1-report.md:112 overstates the control - it does NOT catch a source-keyed refusal (both its directives differ in source); the in-code comment is already accurate. (b) NO TEST pins that same-source different-destination (["Entry","NodesA"] + ["Entry","NodesB"]) stays ACCEPTED - it is legal, and it is the one shape a source-OR-destination-keyed implementation would wrongly refuse. One test closes it; worth doing before merge since it guards a brand-new build-breaking Error.

=== PROCESS CHANGE 2026-08-16 (maintainer): every issue found goes into Issues/round20/TASKS.md as a task, when it is found. Not into a ledger line, a report addendum, or a category table. That file is now the single store; CARRY-FORWARD.md keeps the reasoning behind the items. ===

=== PRE-FLIGHT CONFLICT SCAN (run late — should have preceded Task 0; recorded now) ===

Pair rows — tasks sharing a file or interface:

| A | B | A produces | B consumes | Finding |
|---|---|---|---|---|
| every A-task | A10 | a new DWARF Error (refusal) | SurfaceProbe.Classify's CS-before-DWARF ordering | REAL. Every DWARF Error suppresses emission => refused cell reports CS8795 => stays NotCompilable. Confirmed empirically by Task 1. Ceilings cannot settle until A10 lands. |
| A2 | A3 | MapProperty arity check in MapperExtractor | MapProperty named-arg validation, same extractor | OVERLAP, same attribute + likely same file region. |
| A2/A3 | A4 | method-site MapProperty/MapIgnore handling | member-site reading of the same two attributes | OVERLAP. A4 teaches the co-located path to read member-level forms; A2 forbids the wrong arity at the method site. Adjacent, not contradictory. |
| A2/A3 | A7 | method-site directive handling | propagation of method-level directives into the synthesized element mapper | OVERLAP. A7 propagates what A2/A3 validate. |
| A9 | A11 | per-directive endpoint fixes | 21 newly-measurable cells at Struct/Constructor sites | A11 makes cells answerable that A9's directives may occupy => new divergences discoverable only after A11. |
| A1 | A9 | DWARF087 on FlattenGraph | D11 is FlattenGraph at 4 endpoints | Adjacent, disjoint: A1 is the x2 axis, D11 the single-directive silence. Task 1 already separated them explicitly. |

Self-consistency rows — does each task's own text agree with itself:

| Task | Finding |
|---|---|
| A0..A11 | Each says "lower the ceilings to their newly measured values". Task 1 proved the ceilings often DO NOT move (see row 1). Text is not wrong, but it invites forcing a number. |
| A2 | Says "one check, two findings" for D5+D21 — asserted, not verified. Left as an instruction to check, not a mandate. |
| A9 | Mandates one directive per commit — agrees with itself and with the review rubric. |
| A10 | Says it will surface new divergences; no task after it is scheduled to absorb them. |

Ruling: A10 runs AFTER every refusal-adding task (A2,A3,A5,A6,A7,A8,A9) — ordering the plan implies but never states. Reason: each refusal parks a cell in the CS8795 population, and A10 reclassifies that whole population once; running it earlier means measuring the same ceilings twice. Cost if wrong: ceilings churn an extra round; no correctness risk, no rework of generator code.
Ruling: A2 -> A3 -> A4 -> A7 kept in that order for the MapProperty/MapIgnore overlap, each re-measuring rather than trusting the prior task's numbers. Reason: all four touch the same extractor region. Cost if wrong: merge friction inside MapperExtractor and a wasted fix round.
Ruling: A11 runs after A9, and any divergence it surfaces is recorded rather than fixed in-task. Reason: it enlarges the case-space; fixing newly-visible defects inside a template task would conflate two changes. Cost if wrong: new findings land late and round 20 needs a tail task.
Ruling: no task is permitted to lower a ceiling it did not re-measure in the same commit. Reason: row 1 makes predicted movement unreliable. Cost if wrong: a ceiling sits slack and a regression hides under it.

=== RULINGS on the five parked maintainer decisions (skill 6.3.0: rule, do not stall) ===

Ruling: D-a NullCollections@Projection — take option (c): keep today's behaviour, keep the DeclaredDivergences entry (so the ratchet still asserts the divergence exists), and ADD documentation to docs/options.md stating that projection's collection null-semantics are AsNull by nature. Reason: option (a) risks failing inside a translated query at runtime, strictly worse than a documented divergence; option (b) is a capability regression on the default path for every nullable source collection, and was already implemented and reverted after breaking seven tests. (c) changes no behaviour and converts an undocumented surprise into a documented one. Cost if wrong: nothing changes at runtime; the divergence stays recorded and any later maintainer can still pick (a) or (b).
Ruling: D-b delete ResetForTests. Reason: zero callers repo-wide; its IVT targets a project whose registry tests do not use it; the mutation leg excludes that project, so round-19 Task 10's additions to it are unverifiable dead code by construction. Cost if wrong: a future test wants a reset hook and re-adds ~8 lines. Reversible in one commit.
Ruling: D-d KEEP internal + InternalsVisibleTo. Reason: the four meta-attributes genuinely need it and the alternatives are worse — making them public grows the shipped API for test-only metadata, and a separate assembly breaks the single-package delivery story DwarfMapper.csproj documents at length. The CRA-defensive objection was about what the IVT EXPOSES; once D-b lands, what it exposes is inert metadata with zero runtime reads. D-b resolves D-d.
Ruling: D-c delete the stale items from CLAUDE.md's working note. Reason: the file's own stated rule is to delete each item once decided, both surviving items are decided, and one of them describes a mechanism (a fence allowlist) that no longer exists. NOTE: CLAUDE.md is instructions to the agent, so this is flagged prominently rather than done quietly. Cost if wrong: two historical notes lost — both are preserved in Issues/round20/TASKS.md and CARRY-FORWARD.md.
Ruling: D-e keep deferring the ~80 unannounced diagnostics to a first-release-notes task; PredatesTheChangelog is the worklist and shrinks as they are written. Reason: the project has never shipped, so nothing is currently mis-announced to anyone. Cost if wrong: the first release notes are incomplete — caught by Scan9 only for NEW ids, so this needs a human before the first tag.

=== CONTROLLER ERROR 2026-08-16 — A2's implementation was swept into a docs commit ===

What happened: I ran `git add Issues/round20/TASKS.md && git commit -m "docs(...)"` with NO pathspec on the
commit. The A2 implementer had already STAGED its work. `git commit` commits everything staged, so d6cf498
("docs(round20): surface the in-flight and branch-level work in the task list") actually contains the entire
A2 implementation:

  DWARF088 (new descriptor + AnalyzerReleases + docs/diagnostics.md + diagnostics-index + CHANGELOG),
  MapperExtractor.cs (+76), MapToGenerator.cs (+40), MemberFormDirectiveTests.cs (+202, new),
  NegativeCases/Cases/DWARF088_MemberFormDirectiveOnMapper.cs (new), RegistryDiagnosticsGenTests.cs (+24),
  DeclaredDivergences.cs (-92: four entries deleted, 23 -> 19),
  SurfaceParityTests.cs (ceilings 23->19 findings, 162->113 cells),
  Issues/round20/SURFACE-MATRIX-FINDINGS.md (+95).

Three consequences: (1) the commit message is false; (2) the code BYPASSED the task-review gate, which is the
one thing this process exists to prevent; (3) A2 is still running and will find a clean tree when it tries to
commit.

Ruling: do NOT rewrite history to split the commit. Reason: nothing is pushed so the history is private, but a
subagent currently holds this worktree and rebasing under it risks losing work that is not yet reported - a
strictly worse outcome than a wrong commit message. The content is intact and fully reviewable as a range.
Cost if wrong: the branch carries one commit whose message understates it, permanently, unless amended later
when no agent is running.
Ruling: A2 gets its normal task review, over the range 9193f00..d6cf498, treating d6cf498 as the A2 commit.
The review gate is restored, only late. Cost if wrong: none - this is the gate running as designed.
Ruling: PROCESS FIX, binding for the rest of this effort - every controller commit uses an explicit pathspec
(`git commit -m "..." -- <paths>`), never a bare `git commit` after `git add`. I had been using pathspecs on
the master worktree and dropped them on this one. Cost if wrong: recurrence of exactly this.

Corrected facts my notes had wrong (they said 23/162 from Task 5c's report, not from the code):
  DivergenceFindingCeiling = 19, DivergentCellCeiling = 113, live entries = 19.

=== PAUSED AT MAINTAINER LIMIT SIGNAL (92%) — 2026-08-16 ===
State: A2 implemented (inside d6cf498, see controller-error entry) + rationale commit 1b43bff. Its task
review was dispatched and may still be in flight; if no review report exists, re-dispatch over 9193f00..1b43bff.
A2 also closed A3/D3 unplanned. Divergences now 19 findings / 113 cells.
RESUME AT: read Issues/round20/TASKS.md (NOW section) -> finish the A2 review -> A4 -> A7 -> A5 -> A6 -> A8 ->
A9 -> A10 (must be last among refusal tasks) -> A11.
One open judgement carried into the A2 review: DWARF088 is a Warning and the stated reason was ratchet
perturbation, which is not a valid basis. Decide it on product grounds.

Task A2: complete (implementation in d6cf498 + rationale 1b43bff, review Approved, 3 minors deferred).
  D4 correction CONFIRMED by the reviewer from source: MapPropertyArity's guard is the STACKED count
  (directives.Count > 1 && targetCount > 0 && directives.Count != targetCount) with a triggering test at
  RegistryDiagnosticsGenTests.cs:73-75. My brief's "dead descriptor" premise was false. No new id minted.
  Counts reconcile EXACTLY: D3 20 + D4 2 + D5 22 + D21 5 = 49 cells; findings 23->19; cells 162->113.
Ruling: DWARF088 stays a Warning FOR NOW and escalates to Error as part of A10. Reason: the reviewer's point
is decisive - DWARF011 and DWARF087 pay the identical CS8795 cascade and remain Errors, so the cascade is not
a product reason to demote this one, and DWARFR04 is an Error for the exact mirror misuse. But escalating
before A10 lands makes 25 cells NotCompilable-by-CS8795, i.e. correct and invisible. Warning is a staging
post, not the end state. Cost if wrong: a suppressible warning ships over a silent-data-loss case
([MapProperty("Name", Use = nameof(F))]) until A10; one-line change to escalate.
Task A2 minors (deferred, for the round-20 final review):
  (a) MapToGenerator.cs:~97 dropped the old targetCount > 0 guard, so DWARFR04 can co-fire with DWARFR01 on
      an invalid target type. Noise, both Errors.
  (b) RegistryDiagnostics.cs:37-41 reused message states no remedy for the ctor-arity case and has no
      helpLinkUri; the class-model side got a composed message, the registry side did not.
  (c) No unit control for the LEGAL registry one-arg member form - guarded only by a Gallery sample compiling
      and by the matrix. Also MapperExtractor.cs:249 calls a co-located DTO host a "mapper class".

=== RESUMED — executing by DEPENDENCY LAYER, not impact (plan: partitioned-knitting-lampson.md) ===
Layer 0 (repair the instruments before trusting any measurement): C2, B2, then B1+B9 batched.
Rationale: four remaining items are defects in the MEASURING INSTRUMENTS. Grading a product fix with a broken
instrument produces a green that means nothing, and this codebase has produced SIX such instances.
C2 sequenced first: fixing RepoRoot makes the three dead GeneratedDocsAreCurrentTests run, which
RETROACTIVELY VERIFIES A1's hand-rendered diagnostics-index row and A2's DWARF088 additions - before more
diagnostics pile up on top of unverified doc state.
B2 gates A4: Property and Field share one BuildAt arm that discards `site`, so every Field cell is
byte-identical to its Property cell. A4 is entirely about member-site directives; doing it first would grade
it on evidence never independently gathered.
C2: dispatched (BASE b0236c0).
C2: implemented (2af179c). Only 1 of 4 repo-root walks had the bug; the other three key off DwarfMapper.NET.sln. HasGitMarker extracted + regression test covering both shapes.
  RETROACTIVE VERIFICATION SUCCEEDED - the three previously-dead tests now PASS: diagnostics-index.md and option-support-matrix.md matched their renderers EXACTLY. A1's hand-rendered row and A2's DWARF088 addition were correct, no regeneration needed.
  STATE CHANGE: the '3 known .git failures' caveat that every task in this effort has carried IS GONE. Full solution test run is green outright. Any future failure in that class is now REAL.

=== GOAL SET BY MAINTAINER 2026-08-16: finish ALL of rounds 19, 20 and 21. Pathway: always improvement, no regression. ===
Round 19: complete (10 tasks + 5a/5b/5c, final review returned merge-after-must-fixes; those were A0).
Round 20: Issues/round20/TASKS.md sections A-G.
Round 21: Issues/round21/RESEARCH.md — now IN SCOPE as work, not just research (E1-E4 + the C1/C3/C6 chain).

NO-REGRESSION RULE, binding on every task from here:
  1. Surface matrix must be green at its current cell count before AND after. A red cell is a stop, not a note.
  2. `dotnet build DwarfMapper.NET.sln` must be 0 warnings / 0 errors, samples included. Not `dotnet test`.
  3. NO ratchet may be RAISED. Ceilings move down or not at all. A task needing a raise must say why in the
     commit message and it becomes a maintainer item, not a silent edit.
  4. The full solution test run is now green OUTRIGHT (C2 removed the last standing caveat). Any failure from
     here is REAL - no task may re-introduce a "known failing" allowance.
  5. Every task re-measures what it lowers, in the same commit.
ALWAYS-IMPROVEMENT RULE: a task may not close a finding by narrowing a claim, widening an entry, or moving a
cell into an unjudged population. Closure means the cell is judged and correct.
C2: complete (b0236c0..2af179c, review Approved). No-drift claim verified non-circular - no docs/generated file in the diff.
B2: implemented (7f8da87). 70 cells stopped being duplicates (my brief said ~11 - that was the count inside a ratcheted population; the real one was 70). Measured by hashing every cell's BuildAt source before/after: duplicate (Field,Property) pairs 70 -> 0.
  ZERO verdicts changed. 502 cells' SOURCE text changed (new Tag field in every template) but every population is identical: Honoured 148 / Refused 175 / Silent 248 / NotCompilable 107 / NoSuchSite 137 / Unasked 25 / UnhonouredButLoud 14. The 70 cells had been reading the RIGHT ANSWER FOR THE WRONG REASON.
  No ratchet moved, none raised. Matrix 865/865 green. Full solution 7,420 tests / 0 failed. D20 still accurate - its ten Field rows now rest on a real Field measurement instead of a Property one wearing a Field label.
B2: complete (2af179c..7f8da87, review Approved). Reviewer traced SlotMarkerFor as the SOLE site->marker map
consulted by both SiteAbsenceReason and BuildAt, with SpliceAtSlot throwing rather than falling back - aliasing
ruled out by trace, not by the report. Guard derives its element set from SurfaceCatalog/ValidOn and is
non-vacuous (restoring the single arm yields 70 offenders); a Field-always-declines regression would trip the
NoSuchSiteCellCeiling band instead. SurfaceFixtures.cs UNTOUCHED, so the hard-won MaxDepth/NullCollections/
EnumStrategy shapes are verbatim.
B2 minors (deferred to the documentation-accuracy batch, B15):
  (a) SURFACE-MATRIX-FINDINGS.md:249 says "seven Registry Field cells"; it is TEN (7 MapProperty + 3 MapIgnore).
      A wrong number in the DURABLE DECLARED RECORD - exactly the drift class this effort exists to prevent.
  (b) Endpoints.cs:102-104 Build's <summary> still says memberAttribute goes "on the mapping method (or, for
      the registry, on the source type)" - the new refusal at :115 makes that false.
  (c) Endpoints.cs:120 blames nameof(endpoint); the offending arguments are memberAttribute/extraMembers.
  (d) task-B2-report.md:114-116 quotes D20 as reading "byte-identical and silent there" - D20 contains no such
      wording. Conclusion right, cited evidence not.
Ruling: accumulate prose/record minors into ONE batched cleanup task (B15) rather than a fix round each -
they are same-shape one-line edits across files no one is holding. Cost if wrong: the durable records carry
wrong numbers until B15 runs; (a) is the one that actually misleads.
B1+B9: dispatched as a batch (BASE 7f8da87) - same file, same shape.
B1+B9: complete (7f8da87..7fa9a56, review Approved). 3 of 9 examined genuinely vacuous. Both deletions
independently verified coverage-neutral: Scan6b could NOT be fixed (zero qualified TargetKind.<value> refs
exist anywhere under tests/), and T3b is strictly superseded by CollectionCoverageSelfValidationTests, which
demands each value be EMITTED by the matrix and the fuzz schema. My prescribed fix was empirically falsified
(excluding the declaring file leaves 14 of 17 values with zero refs) and the 14-of-17 figure reproduced exactly.
B1+B9 minors -> B15 batch: (a) InternalEnumCoverageSelfValidationTests.cs:11 still cites deleted T3b and the
renamed Scan6; (b) T3a corpus asymmetry - the "exclude this file" convention was not extended to it (verified
not currently self-satisfied); (c) the stated control limit is not echoed at Scan8's/Scan6a's controls.

=== LAYER 0 COMPLETE (C2, B2, B1, B9). The instruments are repaired; measurements from here are trustworthy. ===
Seven vacuous mechanisms now found across this effort; T3b was the first found by SWEEPING rather than by a
reviewer pointing at it.

Layer 1 begins: A4 -> A7 -> A5 -> A6 -> A8 -> A9 (extractor-adjacency order, ruled in the pre-flight scan).
A4 dispatched (BASE 7fa9a56). It is sequenced first because B2 is what makes its Field-site cells honestly
measurable - before B2 every Field cell was a Property cell wearing a Field label.
A4: implemented (7fa9a56..ac898e7), review CHANGES NEEDED. Boundary verified by TRACE not argument:
MemberDirectives.Read has exactly two call sites and the co-located one is reachable only under separateEmit
via ExtractGenerateMapHost, which returns null for [DwarfMapper] classes. Hoist confirmed - old ParseDirectives
and both attribute-name constants deleted, no second walk survives. 088/089 partition by SYMBOL KIND.
DWARF089's Warning severity is justified by EMISSION SUPPRESSION (HasBlockingError gates emission), not by
ratchet avoidance - checked, and that matters given the DWARF088 precedent.
A4 fix round 1/5 - IMPORTANT, A REGRESSION: MapperExtractor.CoLocatedHost.cs:93 injects `d.Name!` as Source
when ArgumentCount == 1 but the argument is not a string ([MapProperty(null)] is legal; an error constant
mid-typing hits it too). DirectivesArePlaceable checks ARITY ONLY, so it passes, then
MapperExtractor.Members.cs:299 srcName.IndexOf('.') throws -> CS8785. Pre-A4 the shape was silently ignored,
so a GENERATOR CRASH now exists where none did. THREE sibling paths already guard it and one of them
(MapperExtractor.cs:2712) names this exact hazard in a comment.
Correction for the record: the DWARF038 bucket is 4 cells, not 6 (2 Honoured + 4 refused-solely-by-DWARF038).
The 2/18 total is right. Reviewer confirmed those cells are GENUINE closure - DWARF038 fires only because the
rename actually bound, and a regression to non-reading returns them to Silent and trips the ratchet.
A4: fix round 1/5 (2 addressed, 0 open; commits ac898e7..069987f). Crash guard traced to the SOLE injection
site (config.Explicit.Add at CoLocatedHost.cs:96, gated at :76 by DirectivesArePlaceable). No code defect.
A4: complete (7fa9a56..069987f, review Approved after fix round).
A4 deferred to B15 batch (report-text only, code is right):
  (e) the kept [MapProperty(42)] test does NOT exercise the new guard - it yields 0 ConstructorArguments
      (overload resolution fails outright) so it hits the PRE-EXISTING arity branch. The report describes it as
      covering a path the first test misses; it does not.
  (f) the IsNullOrEmpty justification cites an identity-binding fallback that belongs to a different call site;
      in THIS path an empty name would pre-fix have produced MapPropertyUnknownSource. Choice still defensible.
Noted, judged acceptable: the empty-string shape is now dropped as a whole directive set although it would not
have crashed - slightly wider than "only members that would have crashed", defensible under the half-applied-
member rule, small population.

Layer 1 continues: A7 dispatched (BASE 069987f). Closes D1, D2, D16 - all three are the element-wise endpoints
failing to inherit method-level directives, the same shape as the DWARF077 gap already fixed once.
A7: PAUSED MID-TASK for a power interruption, committed durably at b2eff56. BUILDS CLEAN (0/0, samples) and
matrix green 865/865 with ceilings lowered 18->15 findings, 93->84 cells, NotCompilable 107->99. None raised.
INCOMPLETE: doc sync for two new ids (DWARF090/DWARF091) - CHANGELOG (Scan9), docs/diagnostics.md (Scan7),
diagnostics-index, two NegativeCases files, and the D1/D2/D16 resolution notes. THOSE SCANS WILL FAIL until
written, so the suite is red even though the build is clean. Resume by writing the CHANGELOG entries FIRST.
TWO FINDINGS TO CARRY (detail in Issues/ledgers/A7-wip-notes.md, committed):
  1. SEVERE PRODUCT BUG. [AfterMap] on `void Update(Src,Dst)` was emitting `Update(s, d);` INSIDE Update -
     shipped INFINITE RECURSION. The surface matrix scored that cell HONOURED. This is the first case in the
     whole effort where the matrix gave a confidently WRONG POSITIVE - it graded broken behaviour as working,
     rather than failing to measure it. Worth its own look at how many other Honoured cells assert only that
     output CHANGED, not that it is correct.
  2. The brief's D2 premise was wrong: DWARF038 is ImplicitConversionApplied, not a refusal.
A7: complete (069987f..c8ba8bb) then review APPROVED WITH ONE IMPORTANT -> fix round 1/5 dispatched.
  TWO root causes confirmed, not the one I briefed. D1+D2: the DWARF077 check had been WRITTEN TWICE; now one
  gate (ReportElementWiseDirectiveGaps, one impl at MapperExtractor.cs:2702, two call sites) reporting the new
  DWARF090. D16: separate - CollectHooks accepted the partial mapping method itself as a hook, so UpdateInto
  emitted `Update(s, d);` INSIDE Update = SHIPPED INFINITE RECURSION. New DWARF091.
  Hook exclusion verified PRECISE: keys on m.IsPartialDefinition && m.PartialImplementationPart is null, i.e.
  on PARTIAL-NESS not signature - which is why `void Hook(TSource,TTarget)` (the documented two-arg shape, and
  the same shape as `void Update(Src,Dst)`) cannot over-fire.
  NotCompilable 107->99 RECONCILES: both new ids are Warning, and DWARF091 fires BEFORE the signature check so
  it displaces DWARF018 (an Error) which used to suppress emission -> CS8795. 3 AfterMap + 5 BeforeMap = 8 cells
  leave NotCompilable for Refused. Direction explained, not assumed.
  Ceilings 18/93 -> 15/84, NotCompilable 107 -> 99. None raised. 7,493 tests, matrix 865/865, build 0/0.
  diagnostics-index REGENERATED AND VERIFIED BY THE TEST (85->87), not hand-rendered - C2 made that possible.
A7 IMPORTANT (fix round 1): MapperExtractor.cs:2718 reports class-level unscoped [MapIgnore] with NO check that
  the named member exists on the element pair. classIgnores is class-wide, so a mapper with a create map over
  one pair PLUS a span map over an unrelated pair gets DWARF090 telling it to write [MapIgnore<Bar>("Id")] for
  a pair it never wrote about, on a type that may lack the member, as a warning this repo escalates to error.
  THE CODE MAKES THIS EXACT OBJECTION ABOUT ITSELF at :2721-2723, as the reason class-site [MapProperty] is
  excluded from the gate - reasoned through for one attribute, not carried to the other.
PATTERN NOW THREE TASKS RUNNING: the defect is "a guard exists on a sibling path and the new code did not
  inherit it" - A2 (a descriptor checking something else entirely), A4 ([MapProperty(null)] crash), A7 (this).
  This generator has enough near-duplicate paths that correctness does not propagate between them, which is
  also why A4's hoist and A7's gate consolidation were the right instincts.
A7 minors -> B15 batch: (g) "measured Honoured" overstated for [MapProperty<S,T>] in 3 places - the real
  measurement is "applied, DWARF038 rides along" and the classifier reads Refused; (h) SurfaceParityTests.cs:215
  xmldoc still says "UNCHANGED at 107" beside the now-99 constant; (i) no test for a legitimate two-arg hook on
  an UPDATE mapper (all existing ones sit on create-map mappers) - folded into the fix round since it guards the
  over-fire risk; (j) Issues/ledgers/A7-notes.md is byte-identical to the report.
A7 fix round 1/5: commits c8ba8bb..70be306. Class-site DWARF090 now requires the named member to exist as a WRITABLE member of that endpoint's element target (via MemberFacts.Writable, the set resolution already consults it; OrdinalIgnoreCase so a case-differing member still reports). METHOD site stays unfiltered BY DESIGN with a test pinning the asymmetry. Pre-fix failure captured literally. THREE tests not one, so 'never report the class site' cannot pass as a fix. Two-arg hook on an UPDATE mapper now pinned - passes first time, so DWARF091 demonstrably cannot over-fire on the signature collision. No ceiling moved, all seven re-measured.
  IMPLEMENTER'S OWN OBSERVATION, worth keeping: the over-fire needs two UNRELATED pairs on one class, which the case-space never builds - so a unit test, not the matrix, was the right instrument. Another blind spot of the matrix found by product work rather than by the matrix.
A7 fix round 2/5: commits 70be306..e69df8b. The comparer is now MapperExtractor.IgnoreNameComparer, a single declaration read by ALL SEVEN ignore-set sites (four classIgnores constructions, both ResolveMembers/ResolveProjectionMembers restatements, and the new gate). Implementer's words: 'It was ordinal in four places by four separate coincidences; that was the drift surface.' That is the correct fix shape - not 'use Ordinal' but 'make it impossible for them to disagree'.
  Pre-fix failure captured. The test also asserts Id = s.Id is STILL EMITTED at the create map, which is the load-bearing half: it establishes the directive is inert everywhere, so the comparer choice is pinned against something rather than nothing.
  B20 filed (a [MapIgnore] naming nothing is silently inert at EVERY endpoint - same shape as B15) and B21 (CaseInsensitive=true vs the ordinal ignore set - measure, do not assume).
  No ceiling moved, all seven re-measured. THIRD matrix blind spot this round: the probe case-space writes [MapIgnore("Id")] with the fixture's exact casing, so no cell exercises a case mismatch. A short unit test saw what 865 cells could not.
A7: fix round 2/5 (1 addressed, 0 open; commits 70be306..e69df8b). Reviewer confirmed ONE declaration (MapperExtractor.cs:2677) with SEVEN readers (:365, :580, :954, :1160, Members.cs:148, Projection.cs:158, gate :2787); no site supplies its own comparer. Hoist verified BEHAVIOUR-PRESERVING - four sites were default-comparer, two explicitly Ordinal, and only the gate flipped, which was the bug. ResolveProjectionMembers confirmed the same concern, not swept in.
A7: complete (069987f..e69df8b, review clean after 2 fix rounds). Closed D1, D2, D16. Five new diagnostics now exist in round 20 (DWARF087-091).
A7 minor -> B15 batch: (k) ElementWiseDirectiveTests.cs:75-93 - the new test was spliced between an existing <summary> and its [Fact], so two summaries attach to the new test and Method_level_MapIgnore_on_the_span_method_is_reported_even_when_it_names_nothing lost its doc entirely. Doc-only.
A5: dispatched (BASE e69df8b). Closes D17, D18, D19 - MapToGenerator ignores assembly-level config. D19 is a TRUST BOUNDARY.
A5: implemented (aa45757). New Pipeline/AssemblyConfiguration - ONE lookup called by all three front doors
(DwarfGenerator had an inline SECOND copy of the PublicExtensions read); MapperExtractor.ReadAutoMatchMembers
made internal rather than copied. D19 = new DWARFR10 (Error). Ceilings 15->13 findings, 84->82 cells.
Matrix 865/865; classified all 854 cells before/after, EXACTLY 2 rows differ, both Registry/Assembly.
7505/7505 whole solution. This is the FIFTH instance of the pattern and the third structural (hoist) remedy.

Ruling: KEEP D18's breaking change - the registry's generated extensions now default to assembly-INTERNAL.
Reason: PublicExtensions' own XML doc has always read "Defaults to false - all generated extensions are
assembly-internal", so the registry was contradicting its documented contract; the package has NEVER shipped
(AnalyzerReleases.Shipped.md is empty) so no consumer depends on the old behaviour; and defaulting generated
API to internal is the safer direction for a project with a CRA-defensive stance - accessibility wider than
documented is a defect, not a convenience. Cost if wrong: a library shipping [MapTo] types for another
assembly must add [assembly: DwarfMapperOptions(PublicExtensions = true)]; discovered at COMPILE time, one
line, and already announced in CHANGELOG under Changed/BREAKING.
Ruling: D17 is STRUCTURAL, not a divergence - reclassify it to StructurallyInapplicable and ALLOW the
StructurallyExcused ceiling to go 12 -> 13. Reason: measured, the [MapTo] front door emits an extension class
and nothing else - no DwarfProvidesMap, no Register call - so RegisterCollectionShapes has nothing there to
withhold. That is precisely "there is nothing here to configure", which is what StructurallyInapplicable is
for. My no-ratchet-raised rule exists to stop ceilings ABSORBING unfixed defects; this is the opposite - a cell
moving from "we owe a fix" to a measured structural fact, while DivergentCell falls further. Condition: the
row's reason must be the MEASUREMENT, never the pun. Cost if wrong: one cell excused that deserved a fix;
recoverable by deleting the row, which turns the build red.
Ruling: file "should [MapTo] types participate in ambient registration at all?" as a NEW task, not part of A5.
Reason: D17's justification was false in a way that exposes a real design question - if [MapTo] emits no
ambient registration, the option is structurally inapplicable AND the absence may itself be a gap. Building it
is feature work, out of round-20 scope. Cost if wrong: a genuine capability gap sits in the backlog rather
than being fixed now.
NOTE: the false D17 justification ("the one endpoint whose entire output IS registry rows" - a pun on
"registry") was carried in MY round-20 plan text as well as the entry. Corrected by measurement before any
code was written, which is the right order.
A5 fix round 1/5: commits aa45757..7fe1b80. The new [MapProperty("")]/(null) test was validated against the WRONG implementation a careless author would write (explicitOnly && m.Directives.Count == 0): both theory cases FAIL and the other 26 tests PASS - proving nothing else covered that edge. 'The claim had been resting on nothing.'
  README.md:366-368 fixed; swept every .md/.cs, only one doc asserted the old accessibility contract. Both other false report claims FIXED rather than disclosed (DWARFR range now R01-R10 in both places that state it; the api-reference spacing artifact fixed in the AUTHORED comment and verified by reading the regenerated file this time).
  B22 filed: NO DWARFR## message or remedy is pinned ANYWHERE - the whole registry family is excluded from all four DWARF0xx wording gates by written convention. This is REG-05's hole, closed for the main family in round 19, still open for its sibling. The [MapIgnore]->DWARFR02 remedy cascade folded in as instance (a).
  B23 filed - EIGHTH self-measuring mechanism, and a new shape: ApiReferenceRenderer loads with LoadOptions.None, dropping whitespace-only text nodes, and NO TEST CAN SEE IT because the docs test compares the file against the renderer's OWN OUTPUT. A test that compares output to the thing that produced it cannot catch a bug in the producer.
A5: fix round 1/5 (2 addressed, 0 open; commits aa45757..7fe1b80). Sweep verified REAL by the reviewer's own grep - every remaining accessibility claim across CHANGELOG, docs/diagnostics.md, api-reference, options.md, README, Gallery README states the new internal-by-default contract. Nothing missed.
  Scan9's DWARFR exclusion verified STRUCTURAL, not range-text: GetAllDescriptors() reflects only typeof(DiagnosticDescriptors); the DWARFR ids live in a separate class. So the exclusion is correct regardless of the prose - which is why fixing the stale R09 wording was safe.
  B23 mechanism CONFIRMED from code: ApiReferenceRenderer.cs:189 calls XDocument.Load with no LoadOptions (defaults to None, drops whitespace-only nodes) and GeneratedDocsAreCurrentTests.cs:104-107 compares the committed file against the renderer's OWN output - a renderer bug is self-consistent and invisible. Eighth self-measuring mechanism, and a new shape: not an empty corpus or a text-satisfiable needle, but a test whose oracle IS the thing under test.
A5: complete (e69df8b..7fe1b80, review clean after 1 fix round). Closed D18, D19. D17 reclassified pending the sanctioned ceiling raise.
A6: dispatched (BASE 7fe1b80). Closes D6, D7 - the [MapNullSkip] inverses. This is the case the last four tasks each cited as the failure mode they were avoiding.

=== E3/E1 (isolated worktree, branch worktree-agent-a9f1113954c15097b, cut from 20e032c then FF'd to 7fe1b80) ===
E3 DID ITS JOB AND SAVED THE TASK. Filtering to MY list of 7 classes would have lost 18 KILLED MUTANTS
(66.95% -> 51.69%). The DERIVED list is 23 classes / 127 killer tests - 16 classes I never named, incl.
FacadeUpdateIntoTests, AmbientRegistryTests and the whole preserve/graph family. With the derived 23: ZERO
kills lost. This is exactly why the brief said derive the list from the data rather than trusting mine.
TWO FINDINGS THAT FALSIFY EARLIER WORK I APPROVED:
  1. `test-projects` is INERT - Stryker enumerates every solution test project regardless. So Task 9's
     deliberate Generator.Tests exclusion NEVER TOOK EFFECT, and the config comment's "measured cost is ZERO"
     is FALSE: those classes hold 37 kills, 8 of them EXCLUSIVE. I approved both claims at Task 9 review.
  2. `test-case-filter` DOES exist in Stryker 4.16 - absent from --help, verified in Stryker.CLI.dll.
MEASURED: 5,592 -> 288 tests; 44:02 -> 02:50 (15.5x); same 111 mutants; 66.95% -> 61.02%.
The agent STOPPED and reverted rather than commit a lowered break, per my rule. Correct behaviour.
Ruling: ACCEPT break: 61. Reason: NO KILL WAS LOST - all 7 losses are Timeout -> Survived, and the 66.95%
baseline was INFLATED. Four of those mutants delete ThrowIfNull calls or turn a hash `* 397` into `/ 397` and
provably CANNOT hang; one (maxDepth < 1 -> <= 1) is a provably EQUIVALENT mutant no test can ever detect, yet
was credited as detected. Bail-on-by-default biases the Timeout bucket toward survivors. 61.02% is the honest
score on the same mutant set. My no-lowering rule exists to stop a ratchet ABSORBING a regression; it does not
require freezing a number that was measured wrong - same reasoning as the D17 reclassification. Condition: the
config comment must state the inflation mechanism and the date so nobody "restores" 66 later.
Cost if wrong: the leg admits ~6 points of real regression it would previously have caught. Bounded, and the
holes it exposes are being filed as tasks rather than left implicit.
Ruling: proceed to C1. 02:50 qualifies as CI-able, which was the actual prize - the leg currently runs in NO
CI job, so the score is free to regress silently either way.
A6: PARTIAL (6 of 9 cells; commits e51de6b, f3c4659). THREE paths, not the two I briefed: (A) ReadMapNullSkip
at the update/create ResolveMembers calls saw only the METHOD form; (B) ResolvePairNullSkip at the [GenerateMap]
pair loop and the auto-synthesized nested/element loop (which serves SpanMap/AsyncStream/CoLocatedHost) saw only
the PAIR form and had no method; (C) the projection call site passed bare skipNullSrc and saw NEITHER. Unified
into one ResolveNullSkip(pairNullSkips, method?, src, tgt, classDefault) called by all four front doors.
NEITHER FORM WAS WRONG - the interesting answer. Both declare AppliesTo = All, and the generated
option-support-matrix already recorded SkipNullSourceMembers as honoured at four endpoints with DWARF028 at the
fifth. The DECLARATION was right; the implementation had three partial readers. Sixth instance of the pattern.
Contradictory values now most-specific-wins (method > pair > mapper > assembly), pinned in BOTH directions.
Separately found and NOT fixed: the same pair named twice with OPPOSITE values is legal (AllowMultiple) and
first-wins; refusing it needs a new diagnostic.
Ceilings: DivergentCell 82 -> 76. Findings 13 UNCHANGED - D6/D7 survive NARROWED, which is the honest state.
Ruling: A6's projection deferral is CORRECT and I am accepting the partial close. The implementer MADE the
one-line projection change, MEASURED it, and reverted: DWARF028 is an Error, so emission is suppressed and all
three cells read NotCompilable (CS8795), which would have raised NotCompilableCellCeiling 99 -> 102 - a
forbidden raise AND a closure by relocation into an unjudged population. Both rules fired as intended and the
measurement is recorded at the call site. Cost if wrong: three cells stay silent one task longer.
Ruling: file a new task A12 - "land the one-liners that are blocked only on A10's reclassification". Reason:
A6 discovered that projection null-skip is one line whose ONLY blocker is CS8795 classification, and the
class-level option and its assembly twin are ALREADY in that population for the same reason. A8 will inherit
the same dependency. Moving A10 earlier is worse: its value is reclassifying the whole population ONCE, so
running it mid-stream means two passes over the same ceilings. Sequence stays A8 -> A9 -> A10 -> A12.
Cost if wrong: a small tail task after A10 instead of a cleaner single pass.
A6 fix round 1/5: commits f3c4659..94bcda1. Corrected tail now carries the REASON, not just the fact: "silent
at projection (recorded as D6 - an object initializer constructs the destination, so 'keep its current value'
has nothing to keep)". Better than the sentence I proposed.
  The (false) remedy test was confirmed BY MUTATION: forcing `var arg = "true"` FAILS the new test and PASSES
  the pre-existing bare-form test - the hole demonstrated rather than asserted. Restoring the false projection
  claim also fails it, so the tail is pinned in both directions.
  B24 filed, and its row records that the surface matrix STRUCTURALLY CANNOT reach the case: the x2 axis renders
  two IDENTICAL applications on purpose (bool samples to `true` at every variant), so none of the 865 cells
  poses the question. FOURTH matrix blind spot this round, and the same family as the other three - the matrix
  covers one element across many endpoints and is blind to VARIATION WITHIN a declaration (wrong case, wrong
  arity, two unrelated pairs, contradicting duplicates).
  Ceilings: all seven re-measured, none moved. SurfaceParityTests.cs has no diff.
CARRY TO A8 - a possible reclassification, not a fix: A6's own explanation for projection silence is that an
object initializer CONSTRUCTS the destination, so "keep its current value" has nothing to keep. If that holds,
D6/D7's projection cells may be STRUCTURALLY INAPPLICABLE rather than divergent - the same shape as the D17
ruling. Counter-evidence to weigh: DWARF028 exists to refuse null-skip at projection, which implies the
generator considers it meaningful enough to reject rather than ignore. A8 owns projection; it should settle
which, and a correct reclassification MAY raise StructurallyExcused, as D17's did.
A6: fix round 1/5 (3 addressed, 0 open; commits f3c4659..94bcda1). All three clauses of the corrected tail VERIFIED TRUE against source; pinned both ways (Assert.Contains 'silent at projection' + Assert.DoesNotContain 'refused at projection').
  The x2-identical claim verified real at SurfaceCatalog.cs:729 -  IGNORES the variant parameter entirely, so both applications of a bool ctor param render true and the axis can never pose a contradicting-value question. That is the mechanism behind the fourth blind spot, in one line.
A6: complete (7fe1b80..94bcda1, review clean after 1 fix round). PARTIAL BY DESIGN - D6/D7 survive narrowed to their projection cells only. Closed 6 of 9.
A8: dispatched (BASE 94bcda1). Closes D9, D10 - projection does not honour member directives. Carries the open question A6 raised about whether projection null-skip is structural.
A6: fix round 1/5 (3 addressed, 0 open; commits f3c4659..94bcda1). All three clauses of the corrected tail
VERIFIED TRUE against source; pinned both ways (Assert.Contains "silent at projection" + Assert.DoesNotContain
"refused at projection").
  The x2-identical claim verified real at SurfaceCatalog.cs:729 - the bool arm returns "true" and IGNORES the
  variant parameter entirely, so both applications of a bool ctor param render true and the axis can never pose
  a contradicting-value question. That is the mechanism behind the fourth blind spot, in one line.
A6: complete (7fe1b80..94bcda1, review clean after 1 fix round). PARTIAL BY DESIGN - D6/D7 survive narrowed to
their projection cells only. Closed 6 of 9.
A8: dispatched (BASE 94bcda1). Closes D9, D10 - projection does not honour member directives. Carries the open
question A6 raised about whether projection null-skip is structural rather than divergent.
A8: done (0a938bc, ea385ab). D10 closed, D9 narrowed. Ceilings findings 13 -> 12, declared cells 76 -> 62.
LEAD FINDING - D10's OWN EVIDENCE WAS FALSE. Against nested-pair (whose Dst still declares the NESTED member)
[Flatten("Child")] emitted BYTE-IDENTICAL output at all five endpoints; the two "Refused" readings were an
incidental DWARF044 nullable-hop warning about a hop nobody takes. The finding claimed a divergence between
endpoints that were all doing the same nothing. THIRD recorded divergence whose filed evidence was wrong
(D2's DWARF038 premise, D17's registry-rows pun, now D10) - all three from the first matrix run, all three
where the FIXTURE was inadequate to pose the question.
Per directive: [Flatten]@Projection FIXED (ResolveFlattenInfos is now one walk both resolvers call - seventh
structural unification); [Flatten] and [MapValue] @ Span/Async REFUSED via DWARF090, and BOTH REMEDIES WERE
MEASURED Honoured BEFORE being prescribed (the D7 trap, avoided deliberately); [MapValue]@Projection built,
measured, REVERTED - three malformed axes hit DWARF042/041 (Errors) -> NotCompilable 99 -> 102, so it waits on
A10/A12 exactly as A6's did.
The object-initializer argument reaches NEITHER D9 nor D10 - settled with evidence, not generalised. A threaded
directive measuring Honoured at Projection is by construction not structurally inapplicable. StructurallyExcused
stays 12; the permitted raise was deliberately NOT taken.
DISCLOSED AND NEEDS REVIEW SCRUTINY: Flatten's CreateMap/UpdateInto cells also change verdict because the
FIXTURE changed (new flattenable-nested-member), not the generator. Also a non-ceiling baseline raised
deliberately: SurfaceProbeTests fixtures-without-a-slot 17 -> 18, which its own message prescribes on adding a
fixture. Filed B25 ([MapValue<T>] probe args still nonsense) and B26 (DWARF044 fires when no leaf lands).
A8 fix round 1/5: commits ea385ab..4945b2e. TryFormatConstant's inline Array-or-Type-or-Error check was MOVED
into a shared IsRenderableConstant(TypedConstant) that BOTH readers call - hoisted, not copied, so there is no
second copy to diverge. That is the eighth structural unification of the round and the correct answer to the
seventh instance of the guard-did-not-propagate pattern.
  typeof(X) now renders the BARE form [MapValue("Name")] - target named, value omitted, never the word null.
  Both new cases confirmed failing against pre-fix code with literal errors captured: the array case as
  CS8785 ... InvalidOperationException: TypedConstant is an array, the typeof case as an Assert.Contains
  failure showing the emitted [MapValue("Name", null)].
  AsyncStream now pins both prescribed remedies - passed first run, so "the evidence was missing, not the
  behaviour". Per-cell residual stated explicitly as "a symmetric swap is the only unverified possibility".
  No ceiling moved, all seven re-measured.
A8: fix round 1/5 (3 addressed, 0 open; commits ea385ab..4945b2e). IsRenderableConstant verified the SOLE
TypedConstantKind test in the file; all three kinds present; both readers call it; FormatWrittenConstant's one
caller handles the null return at both the written text and the remedy. Hoist verified BEHAVIOUR-PRESERVING on
the create-map path it came from - the new predicate is the literal De Morgan negation of the prior inline check.
A8: complete (94bcda1..4945b2e, review clean after 1 fix round). D10 closed, D9 narrowed. Findings 13 -> 12,
declared cells 76 -> 62.
Ruling: split A9 into A9a and A9b rather than one six-directive task. Reason: the plan mandates one commit per
directive, and six in one dispatch is both a long single run and an unreviewable surface; grouping by SHAPE
keeps each dispatch's reasoning coherent. A9a takes the three that act at CreateMap only and are silent at four
(D8 [MapDerivedType], D11 [FlattenGraph], D13 [ReverseMap]); A9b takes the three mixed ones (D12 [Reinterpret]
silent at the two element-wise endpoints only, D14 [MapCollectionKey] acting at UpdateInto only, D15
[GenerateWrapperMap] refused at CoLocatedHost and silent at all five). Cost if wrong: one extra review cycle.
A9a: dispatched (BASE 4945b2e).
A9a: complete (25621c5 D11, a3dc01e D8, ca2eddd D13, 352e424 TASKS, b292990 test follow-up). All three REFUSED
via ONE new gate ReportCreateMapOnlyDirectives called from the four non-create-map branches and reading through
the create-map branch's OWN readers - ninth structural unification. New id DWARF092 (Warning). 28 cells
Silent -> Refused (8 + 16 + 4).
FILED EVIDENCE FALSE IN TWO OF THREE:
  D8 PARTLY FALSE - "acts at CreateMap in both forms" was wrong; the OPEN form acted NOWHERE, because the flat
  pair declares no hierarchy so the sampled args were typeof(Dst), typeof(Dst) -> DWARF035 -> NotCompilable.
  New polymorphic-hierarchy fixture; that cell is now Honoured.
  D13 MECHANISM MISSTATED - [ReverseMap] does not GENERATE an inverse; the caller declares it and the directive
  makes it inherit inverted renames, with a missing one being DWARF052. Corrected, and the new message
  deliberately does not repeat the wrong model.
  D11 held EXACTLY as filed - the one that did, and the one I flagged as most likely to be false.
RUNNING TALLY: FIVE round-19 divergences have now had their evidence falsified by the task sent to fix them -
D2 (wrong diagnostic named), D17 (a pun on "registry"), D10 (measuring its own fixture), D8 (partly), D13
(mechanism). Every one was found by MEASURING before fixing. The common cause in four of five is a fixture too
thin to pose the question the entry claimed to answer.
Ceilings, each re-measured in its own commit: findings 12 -> 9; declared cells 62 -> 34; NotCompilable 99 -> 98
(DOWN - a fixture that could not pose its question had been counting a cell there). Others unchanged, none
raised. Non-ceiling baseline fixtures-without-a-member-slot 18 -> 19, deliberate.
A9a fix round 1/5: commit c854bb6. The corrected descriptor remark now SPLITS the two reasons instead of
fusing them, and states the wrong model explicitly in order to mark it wrong: "task A9a's own finding entry
(D13) asserted otherwise and was measurably wrong, so the wrong model is restated here only to say it is
wrong." Better than silent replacement - a future reader who half-remembers the old story finds it addressed.
  Fixture comment reattached; ReinterpretableArrayMember sits under its own [Reinterpret] rationale again,
  which matters immediately because A9b's first directive is [Reinterpret].
  Minor 2 MEASURED rather than re-reasoned: I was right that IErrorTypeSymbol implements INamedTypeSymbol, the
  pattern matches, and the arm IS reported - safe, no crash, CS0246 regardless. The wrong reason was replaced
  with a test rather than a better sentence.
  Ceilings none moved, re-measured after the descriptor edit. At HEAD: build 0/0 with samples, matrix 865/865,
  Generator.Tests 6548, conformance 75/75, literal output in the report appendix.
A9a: fix round 1/5 (3 addressed, 0 open; commits b292990..c854bb6). The corrected descriptor's SURROUNDING
claims were verified too, not just the removal: the [ReverseMap] match really is by signature (param[0] ==
targetType, return == sourceType, MapperExtractor.cs:1029-1038), a missing inverse really is DWARF052
(DiagnosticDescriptors.cs:383-388, Error), and the update-into "no construction step" reason is fair for both
[FlattenGraph] and [MapDerivedType]. Restating a wrong model to mark it wrong is only good practice if the
surrounding claims hold - they do.
  Diff confined to <remarks> prose: id, severity and the "{0}" format string all unchanged, so no cell Detail
  could shift. SurfaceParityTests.cs absent from the diff. Suite output confirmed from HEAD (6547 -> 6548, the
  one new test in that commit).
A9a: complete (4945b2e..c854bb6, review clean after 1 fix round). D8, D11, D13 all closed as refusals.
  Findings 12 -> 9, declared cells 62 -> 34, NotCompilable 99 -> 98.
A9b: dispatched (BASE c854bb6). D12 [Reinterpret], D14 [MapCollectionKey], D15 [GenerateWrapperMap] - three
different shapes, one commit each. D15 is a genuine fork (already refused at CoLocatedHost via DWARF067, silent
at all five mapper endpoints) and the dispatch asks for the answer argued from what the directive MEANS.
A9b: complete (695a50d D14, 6edb923 D12, eaece0d D15, aa8262c self-caught doc fix). All three REFUSED.
Findings 9 -> 6, declared cells 34 -> 12, NotCompilable 98 -> 96. Non-ceiling baseline
DirectCompileErrorCallBaseline 52 -> 53, deliberate.
TENTH structural unification, and the right kind: DWARF092's gate was GENERALIZED to
ReportDirectivesNotReadHere, called from all FIVE branches with each arm naming its own home endpoint (create
map for the original trio, update-into for [MapCollectionKey]), and the id retitled "Directive is not read at
this mapping endpoint". A second gate was the obvious move; generalizing the first was the better one.
ALL THREE ENTRIES' EVIDENCE WAS FALSE - tally now EIGHT of the divergences examined:
  D14 "acts at UpdateInto" - the fixture had List<Item>/List<ItemDto> but the v1 upsert needs ONE element
  type, so that cell was DWARF074 behind CS8795. Fixture fixed.
  D12 "acts at Projection" - the projection branch never reads it at all; that cell is UnhonouredButLoud and
  was left where it is rather than being claimed as closed.
  D15 mechanism - DWARF067 is an opinion about the wrapper TYPE, not about placement; the silence was an early
  return on an empty pair list, not a refusal that failed to travel.
D15 fork resolved as REFUSE, argued from the attribute's contract rather than implementation cost: it is an
EXPANSION OF THE [GenerateMap] PAIR LIST, a partial mapping method is a different mechanism, and four of the
five endpoints have no W<A> -> W<B> create-map shape to synthesize at all - so emitting would have been feature
work at one endpoint and still left four needing this refusal.
B27 filed: [GenerateMap] beside a same-pair partial is a bare CS0111 with no DWARF diagnostic.
A9b: complete (c854bb6..aa8262c, review Approved). Gate generalization verified: ONE impl at
MapperExtractor.cs:3074, exactly FIVE call sites, homes correct, and the original three reproduce their old
message text BYTE-FOR-BYTE at the four non-create endpoints so existing EXPECT-MESSAGE pins are untouched.
Retitle sync complete and GeneratedDocsAreCurrentTests renders d.Title, so a descriptor-only retitle WOULD have
failed it. D15 refuse independently endorsed by the reviewer, with the counterpoint disclosed rather than
buried ([GenerateMap] already emits undeclared methods, so emitting is not unprecedented - filed as B27).
A9b minors -> B15 batch: (l) CHANGELOG.md:47 says "both of its cells close" for D12, which has FOUR; same
defect class the aa8262c commit was cut to fix. (m) SURFACE-MATRIX-FINDINGS D14 banner cross-references a
paragraph that was deleted (claim survives quoted verbatim; the pointer dangles). (n) the DWARF092 NegativeCases
case still names its class CreateMapOnlyDirectiveMapper - the stale-name rot the file rename was justified by.
Ruling: A10 does CLASSIFICATION ONLY. The DWARF088 Warning -> Error escalation I ruled earlier "as part of A10"
moves to A12 instead. Reason: escalating a severity while A10 is reclassifying the whole CS8795 population
would confound the measurement - two causes moving the same cells in one commit, and this round has shown
repeatedly that predicted ceiling movement is unreliable. Cost if wrong: one extra small commit in A12.
A10: dispatched (BASE aa8262c). Ceilings entering: findings 6, declared cells 12, NotCompilable 96,
StructurallyExcused 12, UnhonouredButLoud 14, Unaskable 44, NoSuchSite 137.

=== END OF DAY 2026-08-17 — A10 asked to pause durably mid-task ===
STATE: Layer 0 complete (C2, B2, B1+B9). Layer 1 complete (A4, A7, A5, A6, A8, A9a, A9b). A10 was IN FLIGHT
and told to commit whatever it has, honestly labelled. A11 and A12 not started.
Divergences: 23 findings / 162 cells at round-20 start -> 6 / 12 now. Six new diagnostics DWARF087-093.
Ten structural unifications. Two generator crashes introduced by this round's own fixes, both caught at review.
RESUME ORDER: finish/redo A10 (classification only - the DWARF088 escalation was RULED into A12) -> A11
(struct + constructor template slots, 21 cells) -> A12 (DWARF088 Warning->Error, plus the one-liners A6 and A8
each measured, reverted and parked at their call sites) -> merge the mutation branch -> B15 documentation
batch -> the rest of B/C -> final whole-branch review -> F1 merge (needs the maintainer).
OPEN FOR THE MAINTAINER, unchanged: D-e (~80 diagnostics predate CHANGELOG.md and have never been announced;
Scan9 guards only NEW ids, so nothing catches this before the first tag) and F1 (the merge itself).
THE FINDING TO CARRY: eight of the round-19 divergences examined so far had FALSE filed evidence - D2, D17,
D10, D8, D13, D12, D14, D15. Four or more trace to a fixture too thin to pose the question the entry claimed
to answer. Round 19 did not find 23 defects; it found 23 cells worth investigating, and a large fraction
dissolve or change shape once the fixture actually bites. The instrument was sound; its inputs were not.
A10: PAUSED DURABLY at eb4f64f. Test project builds 0/0; solution build NOT run. Working tree clean, temp dump
never committed.
  MATRIX IS RED ON EXACTLY ONE ASSERTION: NotCompilableCellCeiling is still 96 while the population now
  measures 10, so AssertRatchet's LOWER bound fails. That is the floor-forces-it-down guard working as
  designed - the ceiling cannot silently keep slack after a population shrinks.
  Measured 86 cells NotCompilable -> Refused, ZERO other transitions, 96 = 86 + 10 exact.
  FINDING CONTRARY TO MY BRIEF: zero new divergences, and none is POSSIBLE. All 96 cells were already CLAIMED,
  and the reclassification rule's codomain is {Refused, NotCompilable} - it can never yield Silent. So A10
  surfaces no defects by construction; its entire value is that 86 cells stop being exempt from the
  instrument. I briefed it to expect a fresh crop of findings; that expectation was wrong, and the reasoning
  against it is sound. Verify the codomain claim at review.
  RESUME = ONE EDIT: lower NotCompilableCellCeiling 96 -> 10 at SurfaceParityTests.cs:202, then the two pin
  tests (CS8795-with-blocking-DWARF must read Refused; a real placement rejection must stay NotCompilable) and
  the R4 doc rewrite. All named in Issues/ledgers/A10-notes.md, committed.
A10: complete (eb4f64f wip, a96b42d final). NotCompilableCellCeiling 96 -> 10; all seven re-measured in the
same commit, none raised. Whole solution builds 0/0 with all four sample projects linked.
  EIGHTH RATCHET REFUSED BY DESIGN, NOT BY EXEMPTION: the first pin test called RunAndGetCompilationErrors
  directly and would have pushed DirectCompileErrorCallBaseline 53 -> 54. Instead Classify now names the
  compiler error in its own detail string ("DWARF005 (behind CS8795)", on exactly the 86 rows), so the test
  asserts the same fact from the probe's output. Baseline stays 53 and the printout got better. That is the
  right instinct: a ratchet that resists a raise should be answered by improving the design, not by raising it.
  THE 86 SATISFY THEIR CLAIMS - MEASURED, not inferred: the parity theory's body was replayed over every cell
  and all 86 read PASS-claimed-branch.
  FIRST COMPLETE CENSUS OF THE MATRIX: 535 pass-claimed / 104 pass-unclaimed / 147 skipped-unjudged /
  44 skipped-no-question / 24 would-fail-claimed. Sums to 854 cells. The 24 are exactly the 12 declared
  divergences plus the 12 structurally-excused option cells - i.e. every would-fail cell is accounted for by a
  record, none is a surprise.
  ZERO-DIVERGENCES CLAIM, stated falsifiably: the branch returns in both arms so its codomain is
  {Refused, NotCompilable}; Silent - which a divergence requires - is unreachable from it; and all 96 cells
  were already CLAIMED, so the under-reach direction is empty by construction. My briefed expectation of a
  fresh crop was WRONG. A10 surfaces no defects; its value is that 86 cells stop being exempt from the
  instrument. Ninth time this round that measurement beat an expectation I wrote into a brief.
A10: complete (aa8262c..a96b42d, review Approved). Rule verified EXACTLY right: IsGeneratorRefusal requires an
added Error whose ids are non-empty AND ALL in {CS8795}; a Warning cannot satisfy it (pinned by truth table);
DWARF078 is filtered BEFORE the list reaches the predicate, not smuggled back; and no CS error can
self-justify because RunAll returns generator diagnostics only.
  ZERO-DIVERGENCES ARGUMENT HOLDS, all three conjuncts, and conjunct 3 is OVER-DETERMINED by the census
  identity: 535 pass-claimed = 160 Honoured + 361 Refused + 14 UnhonouredButLoud exactly, so every judged
  acting cell is claimed. My briefed expectation was wrong and the argument survived an attempt to falsify it.
  Census reconciles completely: 535+104+147+44+24 = 854 cells; 147 = 137 NoSuchSite + 10 NotCompilable;
  854 cells + 11 facts = 865, matching the leg exactly. The 24 would-fail cells are pinned by TWO separately
  ratcheted populations (StructurallyExcused 12, DivergentCell 12), and an unaccounted one would Assert.Fail.
  Ratchet avoidance judged LEGITIMATE: baseline still 53, no new harness call, detail feeds only the NoSuchSite
  grouping and print strings, AssertRatchet compares counts. Reviewer noted the NotCompilable detail widened
  too (first-id -> joined), also neutral, so "exactly the 86 rows" undersells the footprint.
A10 minors -> B15 batch: (o) SurfaceProbe.cs:133 - the "DWARF078 precedes the predicate" invariant is UNTESTED;
a refactor could migrate the filter below the predicate and nothing goes red. Move the exclusion inside
IsGeneratorRefusal plus one truth-table row. (p) the ledger references A10-wip-notes.md; the committed file is
A10-notes.md.
A11: dispatched (BASE a96b42d). Entering ceilings: findings 6, declared cells 12, NotCompilable 10,
NoSuchSite 137, StructurallyExcused 12, UnhonouredButLoud 14, Unaskable 44.
A11: implemented (f16fb0a), MATRIX DELIBERATELY RED on 4 SurfaceParityTests assertions. All 21 G5 cells became
measurable; the no-fixture-declares-one cause is GONE from the breakdown entirely. NoSuchSite 137 -> 116, now
wholly structural (68 no-mapping-method + 48 registry-has-no-mapper-class, unchanged to the cell).
  A11-F1 - A REAL PRODUCT DEFECT, REPORTED NOT FIXED, covering 14 of the 21 cells: MapToGenerator.Emit
  (367/382) writes `if (source is null) throw ...` into EVERY generated extension method regardless of whether
  the source is a VALUE TYPE. So [MapTo] on a struct - legal per its own AttributeUsage, claiming all seven
  endpoints - emits CS0037 and does not compile at ANY endpoint. No sample or test in the repo applies [MapTo]
  to a struct, which is why nothing ever noticed. This is A11's whole payoff: making the cells measurable
  exposed that a documented, legal placement has never worked.
  3 new divergences, all [DwarfMapperConstructor] on a Constructor: Silent at UpdateInto, Projection and
  Registry; Honoured at CreateMap, SpanMap, AsyncStream, CoLocatedHost. Collected, not ratified - no AppliesTo
  narrowed, no entry added.
  NotCompilable MEASURES 24 against a constant HELD AT 10. The implementer refused to raise it because that
  would bury a live product defect in the matrix's unjudged population. That is precisely what the
  no-raise rule exists for, and it is the first time it has fired against a defect rather than against slack.
  One src/ edit disclosed: DwarfMapperConstructorAttribute's [DwarfSurface] ProbeKey removed - the old fixture
  ("internal-member") has no constructor on its destination and a DWARF001 baseline, so all seven cells could
  only ever read UnhonouredButLoud, which would have pushed that ceiling 14 -> 21. Measurement metadata on an
  internal attribute; no generator source touched.
A11: fix round 1/5 (3 addressed, 0 open; commits f16fb0a..e2f3b51). Reviewer confirmed a COLD READER of
SurfaceParityTests.cs now learns all four facts: population 24, constant HELD not stale, why (shrink-only rule
vs a live defect), and that the NotCompilable label is knowingly wrong for those 14 cells. Diff verified
docs/comments/BOM only - no constant, assertion or predicate changed.
A11: complete (a96b42d..e2f3b51, review clean after 1 fix round). All 21 G5 cells measurable; NoSuchSite
137 -> 116 and now wholly structural. Matrix RED on four assertions BY DESIGN.
Ruling: A11-F1 becomes its own task A13 rather than folding into A12. Reason: it is a generator fix and must
not land in the commit that re-measured the instrument - the same separation A6 and A8 observed when they
built, measured and reverted their projection one-liners. Cost if wrong: one extra task boundary.
Ruling: the three [DwarfMapperConstructor] divergences (Silent at Projection, UpdateInto, Registry; Honoured
at the other four) become task A14, not part of A13. Reason: A13 is a bounded generator fix with a measurable
end state; bundling three divergences whose right answer may be fix, refuse OR structural would make one
commit unreviewable. The reviewer already traced two of the three to source - Registry because Emit hardcodes
`return new T { ... }` and ConstructorSelector has no caller under Registry/, Projection because
usesCtorProjection is false when a parameterless ctor and writable members both exist - so A14 starts with
evidence rather than a filed claim. Cost if wrong: the matrix stays red on three assertions one task longer.
Ruling: add a THIRD VERDICT to A12's scope - "the generator emitted code that does not compile". Reason: N4
(CS1912, duplicate [FlattenGraph]) and A11-F1 (CS0037, [MapTo] on a struct) are both that shape, and both
currently land in NotCompilable, which MEANS "the compiler rejected the placement, so AttributeUsage was
telling the truth". That is exactly backwards for an output defect: the placement was legal and the GENERATOR
produced invalid code. The reviewer named this gap independently. Cost if wrong: instrument work with a new
counted population, deferrable if it proves larger than it looks.
A13: dispatched (BASE e2f3b51).
A13: done (7591f25). A11-F1 FIXED - all 14 [MapTo]-on-a-struct cells now read Honoured at all seven endpoints.
NotCompilable measured 10 against constant 10, already correct and NOT edited; all seven ceilings equal their
constants. Conformance sample F49 added (75 -> 78 assertions) so the shape a reader can run now exercises it.
  Predicate is NEW - TypeFacts.CanBeNull(t) => !t.IsValueType || t is Nullable<T> - but the notion already
  existed INLINE as src.IsReferenceType in MapToGenerator.SynthNested, FIFTY LINES BELOW THE DEFECT. Eighth
  instance of the guard-did-not-propagate pattern, and the first where the sibling guard was in the same file.
  Three registry call sites now read the one predicate.
  DELIBERATELY NOT UNIFIED, and this is the right restraint: the two equivalent idioms on the mapper path
  (Members.cs:732, Projection.cs:474) drive SkipIfSourceNull/DWARF028 and answer DIFFERENTLY for an
  unconstrained T. Over-unification would have been a defect of its own; the round's hoist habit was applied
  with judgement rather than reflexively.
  New finding, reported not fixed: a GENERIC struct source (Box<T>) loses CS0037 but keeps CS0246, because T
  is not in scope in the generated class - and this is PRE-EXISTING AND IDENTICAL FOR GENERIC CLASSES. Needs
  filing as a B-item; it is a [MapTo] limitation nothing currently records.
  Matrix now red on THREE assertions, all A14's [DwarfMapperConstructor] divergences.
A13 fix round 1/5: commit ff06458. FOURTH emission site fixed (TryCollection's collection helper), the line
OMITTED entirely for a value-type source rather than wrapped. New test uses a CLASS source and fails pre-fix
with the literal CS0037 on reverting the gate. Corrected count: FOUR emission sites in MapToGenerator
(Emit x2, SynthNested, TryCollection) via THREE CanBeNull invocations - the Emit pair shares one through
Model.SourceCanBeNull. The notion existed THREE times inline in that one file, not twice; report table and
CHANGELOG both corrected.
  THE LESSON, sharper than the fix: TypeFacts.cs's own doc comment - written in the SAME COMMIT - warns that
  "a predicate correct only for the inputs that reach it today" is the next version of this defect, and that
  is exactly what happened. Ninth instance of guard-did-not-propagate, and the first where the failure was not
  "no sibling was found" but "THE SEARCH FOR SIBLINGS STOPPED AT THE FIRST TWO". The remedy for that is a
  sweep, not a fold.
  New finding reported not fixed: ImmutableArray<T> STILL fails here for a SEPARATE pre-existing cause -
  CountKind.Count is chosen for any ICollection<T>/IReadOnlyCollection<T> implementer without checking that
  Count is a public INSTANCE member, so s.Count gives CS1061, or CS1503 via a LINQ method group where implicit
  usings are on. Needs filing as a B-item; confirm at re-review.
  Red: exactly the three [DwarfMapperConstructor] assertions. Ceilings none moved, all seven equal constants.
A14: implemented (4850b8b Projection FIXED, d394df4 Registry REFUSED via DWARFR11, de94475 UpdateInto
STRUCTURAL, a66457c struct-target pin). Three endpoints, three different answers, each argued from what the
directive MEANS. THE SURFACE MATRIX IS GREEN.
  Projection was WORSE THAN TRACED - four defects: the Select call's return value DISCARDED; a second
  independent copy of the widest-arity pick for nested targets; an annotated PARAMETERLESS ctor projected as
  the WIDEST one; the nested path assigning its own arguments TWICE via a case-sensitivity bug. All four
  necessary to close the cell. ConstructorSelector decision now made ONCE, two call sites, no third copy.
  UpdateInto structural call ENDORSED by the reviewer (would have ruled the same): the reason is the SIGNATURE
  SHAPE (caller-supplied destination), not what the generator reads; refusal is genuinely wrong because the
  attribute is type-scoped and would fire on a correct create+update pairing. Both halves pinned - including
  the one that cuts against it (a NESTED destination IS constructed at update-into and the annotated ctor IS
  called there). Implementer settled it without consulting me despite the brief asking; DISCLOSED it, and
  reversal is one attribute + two tests. Correct behaviour on both counts.
  MY BRIEF WAS WRONG about the sanctioned ceiling raise: StructurallyExcusedCellCeiling counts OPTION-BAG cells
  only (its lookup parses a Name=value axis, returns null for ctor(0)). So a directive's structural narrowing
  enters NO counted population. Reviewer confirmed: [DwarfSurfaceSite] narrowings are validated for FORM by
  ValidateSiteClaims but COUNTED BY NOTHING - the set of dropped endpoints can grow unratcheted forever. Filed
  as B32. This is a real accounting gap and belongs in the final review.
A14 fix round 1/5 (commit 1f0f3f7): CRITICAL - DWARFR11's message format string had an unescaped { ... };
string.Format threw, Roslyn caught it, and the diagnostic rendered with literal {0} three times. NO TEST
CAUGHT IT because all four asserted only d.Id. THIS IS B22'S THESIS COMING TRUE ON ITS FIRST OPPORTUNITY - no
DWARFR## message or remedy wording is pinned anywhere. Fixed by escaping, plus TWO gates both proven
non-vacuous by reverting the escape: a per-id message assertion, and a FAMILY-WIDE
Every_registry_message_format_actually_formats - B22's thesis made executable for the one failure mode
needing no per-id prose. Filed B29 (projection widest-arity fallback unfiltered), B30 (SynthNested has no
DWARFR09 equivalent -> CS1729), B31 (unusable annotated ctor silently ignored), B32 (site narrowings uncounted).
A14: fix round 1/5 (2 addressed + 2 minors, 0 open; commits a66457c..1f0f3f7). Family gate verified REAL: it
reflects every public static DiagnosticDescriptor on RegistryDiagnostics (count-guarded >= 6 so a future
DWARFR12 is auto-covered), calls string.Format with sample args, and fails on FormatException OR on a render
that swallows the arg - a genuine format attempt, not a brace scan. Reviewer's own grep found no other stray
braces in the family. B29-B32 verified filed with mechanisms; B32 states both WHAT is uncounted and WHY.
A14: complete (8f9226f..1f0f3f7, review clean after 1 fix round). THE SURFACE MATRIX IS GREEN AND EVERY
PRODUCT DIVERGENCE IN ROUND 20 IS SETTLED. Six declared divergences remain BY DESIGN: NullCollections@Projection
(a ruled design decision, documented), MaxDepth@Span/Async (a tighter bound ignored, default 64 still applies),
D6/D7's projection cells and D9's projection cell (all three parked on A10 - now landed - and owed to A12), and
D17 (reclassified structural pending the sanctioned StructurallyExcused raise, also A12).
Ruling: A12's scope is now FIVE items, all instrument-or-follow-through, no new product investigation:
  (1) DWARF088 Warning -> Error (ruled twice; the cascade-avoidance reason is dead now that A10 landed);
  (2) the [MapNullSkip]@Projection one-liner A6 measured and reverted;
  (3) the [MapValue]@Projection threading A8 measured and reverted;
  (4) D17's reclassification to StructurallyInapplicable with the ONE sanctioned StructurallyExcused raise 12->13;
  (5) a new probe verdict for "the generator emitted code that does not compile" - N4 (CS1912) and A11-F1
      (CS0037) both landed in NotCompilable, which MEANS the placement was illegal, when the placement was legal
      and the GENERATOR was wrong.
  Reason for bundling: (2)-(4) were each explicitly parked ON A10 and A10 has landed; (1) is a one-line
  severity flip whose only prior blocker was the same cascade; (5) is instrument work of the same kind. Each
  gets its OWN commit so the review surface stays per-item. Cost if wrong: one large review instead of five
  small ones.
A12: dispatched (BASE 1f0f3f7).
A12: complete (2645010 item1, a7b1799 item2, ed75f67 item3, f38fbd1 item4, 874d83d item5). Matrix GREEN after
each commit, 866/866 at HEAD. 7654 pass / 0 fail across 8 projects. Build 0/0 with samples.
  Item 1: DWARF088 -> Error. NO population moved (all 47 cells stay Refused, 45 as "behind CS8795"). The real
  work was the SWEEP - DWARF088-as-Warning was load-bearing prose in SIX other places (five sibling diagnostic
  citations, docs, CHANGELOG, two test doc comments), each of which would have shipped false.
  Item 2: [MapNullSkip]@Projection - 3 cells Silent -> Refused. D6, D7 DELETED. And DWARF090's "silent at
  projection" tail was FALSE AGAIN (it had been corrected once in A6) and is flipped with a both-direction pin.
  Item 3: [MapValue]@Projection - NOT a one-liner as A8's note suggested; rebuilt. All 4 cells close. Create-map
  guard HOISTED (TryValidateMapValueTarget, one statement, two callers) plus a sibling IgnoreObsoleteMembers
  gap swept - the guard-propagation lesson applied proactively. D9 DELETED.
  Item 4: D17 -> StructurallyInapplicable on the A5 measurement. StructurallyExcused 12 -> 13, THE ONE
  SANCTIONED RAISE, measurement in the commit. Exactly one cell moved, no verdict changed.
  Item 5: NEW VERDICT SurfaceEffect.EmittedInvalidCode, keyed on WHERE the error was reported (hoisted
  GeneratorTestHarness.IsInGeneratedCode), asked BEFORE A10's rule - safe by measurement, 137/137 CS8795 are in
  user source. Seven pins, two mutations.
  SURFACED NOT ABSORBED: the new verdict measured TEN, not the expected zero. Every remaining NotCompilable
  residual was the GENERATOR emitting invalid code into a .g.cs file: 8 are pre-filed B27 (duplicate Map
  emitted), 2 newly filed as B33 (Preserve @ SpanMap/AsyncStream, CS7036 in emitted code - the old note's
  "template fits no overload" was WRONG). Ratcheted at the measured 10. NotCompilable 10 -> 0, still reachable
  and pinned. Item 4 also required widening Every_exemption_names_a_real_option_and_endpoint, which asserted
  the option matrix's endpoint domain over a store that now has two consumers.
FINAL CEILINGS: findings 2, declared cells 4, NotCompilable 0, EmittedInvalidCode 10, NoSuchSite 116,
StructurallyExcused 13, UnhonouredButLoud 14, Unaskable 44. From 23 findings / 162 cells at round-20 start.
The two remaining findings are NullCollections@Projection (a ruled design decision) and MaxDepth@Span/Async
(a tighter bound ignored; default 64 still applies) - both maintainer-acknowledged, neither a silent defect.
MUTATION STREAM MERGED (d2d54cf, --no-ff, zero conflicts, three files). VERIFIED BEFORE MERGING that the
maintainer's "retain no exclusion" ruling held in the committed config: no test-case-filter, no test-projects
(both gone), additional-timeout 120000, break 61, comment states MEASURED, calls test-projects inert, and says
66 must not be restored. CI leg is nightly cron + workflow_dispatch, dormant until it lands on master.
  THE FULL-SUITE RUN MEASURED 12m25s ON A QUIET MACHINE, not 44 minutes. The original 44 was taken under
  concurrent builds; contention was a LARGE confound, as I had noted in RESEARCH.md and then let harden into
  a fact. That confound, not test count, was most of the gap between the scoped 2:50 and the full-suite figure.
  A phantom `M` appeared on one NegativeCases file after the merge - bytes IDENTICAL to HEAD by cmp, no BOM,
  a stale-stat artefact under an eol=lf attribute. Cleared with checkout, not committed. Noting it because a
  bare `git add -A` here would have committed a no-op and made a reviewer chase it.
A12: fix round 1/5 (1 addressed, 0 open; commit a2f7d9d). Both false present-tense lines FIXED NOT DELETED -
each now records what was true then, what changed it, and what is true now, with verdicts RE-MEASURED.
  Grep widened REPO-WIDE over *.md + *.cs for DWARF088 within three lines of "Warning": NO THIRD INSTANCE.
  Ledger mentions (11) deliberately left - every one is a dated ruling in a chronological log, none a
  present-tense claim about HEAD. That is the correct distinction: a decision log records what was decided
  WHEN; a findings doc asserts what IS. Only the second kind must track HEAD.
  Minor taken: EmittedCodeErrorIds's doc no longer claims more than an id-keyed match guarantees.
A12: complete (1f0f3f7..a2f7d9d, review clean after 1 fix round).

=== THE A-TRACK IS CLOSED. Every round-20 generator task (A0-A14) is complete and reviewed. ===
Final ceilings: findings 2, declared cells 4, NotCompilable 0, EmittedInvalidCode 10, NoSuchSite 116,
StructurallyExcused 13, UnhonouredButLoud 14, Unaskable 44. From 23 findings / 162 cells at round-20 start.
The two remaining findings - MaxDepth@Span/Async and NullCollections@Projection - are both maintainer-
acknowledged design decisions with their reasoning recorded; neither is a silent defect.
Seven new diagnostics DWARF086-093 + DWARFR10, DWARFR11. Eleven structural unifications. Three product
defects no test or sample reached: [AfterMap] shipping infinite recursion, duplicate [FlattenGraph] emitting
CS1912, [MapTo] on a struct never compiling. Nine round-19 entries with false filed evidence.
