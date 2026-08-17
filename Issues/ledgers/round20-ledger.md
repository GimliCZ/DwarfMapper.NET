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
