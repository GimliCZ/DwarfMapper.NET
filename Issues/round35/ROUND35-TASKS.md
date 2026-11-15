# Round 35 — task list

Opened 2026-11-13 from the owner's "I suggest converting those into task list and fix all". Each task ends with a
measured result and, where code changes, a RED -> GREEN test in the same commit. Status is updated here as it lands.

## Done before this list was written (PR #12)

- .NET 11: tests warning-free under SDK 11 (`33a092a`), the `net11` CI job (`83feca9`).
- The nightly ILVerify stage finds the reference pack behind a symlinked `dotnet` (`f83c6ac`).
- Preserve keeps identity through a nullable struct holder: measured, pinned (`4c52ee0`).
- `[FlattenGraph]` under Preserve refuses a widened or block-copied collection leaf (DWARF075, `4f5c384`; owner
  decision 9).
- Nineteen outdated doc statements (`7b6c035`, `0683030`).

## Tasks

| # | Task | Status |
|---|---|---|
| T1 | **Silently dropped guarantees.** Every place the generator decides by helper family (`IsComplexHelper`, `IsSynthesized`, `IsObjectMap`), against every helper family, under Preserve / SetNull / AsNull: find the pairs a fast-path helper slips through. | **done** - see Notes |
| T2 | **The last untested audit cell:** an array blit under SetNull against a method-level config on the element pair's partial. | **done** - defect fixed, see Notes |
| T3 | **The stale XML doc** on `ImplicitConversions` ("suggestion (Info)"). | **done** (`93d8b01`) |
| T4 | **DWARF066 false positive** on a When-guarded member inherited from a positional record. | **done** - see Notes |
| T5 | **Audit `docs/diagnostics.md`** (2,700 lines) against the tree. | **done** - see Notes |
| T6 | **Widen span maps** (`void Map(ReadOnlySpan<int>, Span<long>)`): measure the scalar loop against a vectorised one; vectorise if it pays. | **done on its own branch** (`feat/round35-t6`): it pays - 1.56x to 6x at 1,024; see Notes |
| T7 | **Move the SDK pin** to the current 10.0 servicing release; re-measure every gate tied to it; drop the AOT compiler override if the new pin makes it dead. | **done on its own branch** (`feat/round35-t7`, stacked on T8): 10.0.404, override removed, and a `toolchain-floor` job - see Notes |
| T8 | **Ubuntu 26.04 for every Linux job**, the canary removed, on a `full-ci` run. | **done on its own branch** (`feat/round35-t8`), the owner's yes of 2026-11-13 - see Notes |
| T9 | **Test and tooling packages** brought up to date, one at a time, gates re-measured. | open |
| T10 | **The in-process mutation runner** into the repository: tool project, tests, catalogue step, a CI job comparing its report with Stryker's. | open |
| T11 | **The three Stryker findings filed upstream.** The owner's to send; the write-up is ready. | owner |
| T12 | **A `[MapDerivedType]` pair used as a list or array element calls the public method with no context** (abstract base, every mode): a cycle through the collection is a stack overflow, and Preserve loses identity between elements. | **done** - see Notes |
| T13 | **A `[MapDerivedType]` pair with a concrete base, nested under Preserve or SetNull, skips the dispatch**: a derived instance is mapped as the base, its own members dropped, with DWARF071 (Info) advising the attribute that is already there. | **done** - see Notes |
| T14 | **SetNull, a concrete-base `[MapDerivedType]` pair and a cycle through an arm: the generated code does not compile** (CS7036, an arm's helper calls a context-taking helper without one). | **done** - see Notes |
| T15 | **A `[FlattenGraph]` method's pair nested under Preserve or SetNull is refused as DWARF001**, whose advice (`[MapIgnore("Nodes")]`) would empty the flattened list. | **done** - see Notes |
| T16 | **A declared mapping method with an enum or scalar destination returns the default for every input** (`return new StatusDto { };`), where `[GenerateMap]` of the same pair converts. | **done** - see Notes |
| T17 | **A cycle that runs through a `[FlattenGraph]` method's own node leaf is a stack overflow** in every mode (a node pointing at the object that holds the graph). | **done** - refused as DWARF118, see Notes |
| T18 | **A base instance at a dispatch is refused in the build, not at run time** (the owner, 2026-11-13, on T13's "now throws": "compile time error would be better"). | **done** - DWARF119, see Notes |
| T19 | **A derived arm adopts a declared method that takes the BASE type**, and the derived members are dropped with no diagnostic. Found while proving T18's advice. | **analysed: not the defect I called it** - the owner's to rule, recommendation "no change"; see Notes |
| T20 | **A hand-written converter, hook or factory that calls the mapper again overflows the stack** (process death) in every mode. Found by probing the 2026-11-13 AutoMapper advisories' shapes. Older than this round. | **done** - a stack check at every declared mapping method of a mapper holding user-written code, see Notes |
| T21 | A class whose only constructor takes one parameter of its own type is refused (DWARF026). | **not a defect** - a deliberate, tested rule (`AnnotatedConstructorUnusableTests`: "COPY constructor"); see Notes |
| T22 | **A record carrying `[DwarfMapper]` or `[GenerateMap]` is ignored without a diagnostic.** Met while testing T20. | **done** - DWARF120, see Notes |
| T23 | **Multi-version generator, and C# 15 union mapping on the Roslyn 5.12 build** (the owner, 2026-11-14: "Prepare multiversion and if detected .net 11 enable Union mapping ... with tests"). Design: `docs/superpowers/specs/2026-11-14-multiversion-generator-union-mapping-design.md`. | **done on its own branch** (`feat/round35-t23`) - see Notes |
| T25 | **DwarfMapper against today's Mapperly and Mapster** (the owner, 2026-11-14: "run entire performance and usage comparison against dwarf mapper? Afcource up to date versions (not automaller afcourse) ... Target will be performance, security, validations, docs and samples validations"). Design: `docs/superpowers/specs/2026-11-15-current-competitor-comparison-design.md`. | **measured** (`feat/round35-t25`, stacked on T23) - see Notes |
| T24 | **A performance comparison of union mapping** ("After that prepare performance comparison testing"). Same design document. | **done on T23's branch** - `benchmarks/DwarfMapper.UnionBenchmarks`, results in `benchmarks/results/2026-11-14-round35-t24-union-mapping.md`; see Notes |
| T26 | **Release readiness for the next NuGet upload** (the owner, 2026-11-15: "For the nuget, I am pretty sure we should do an update in close future"). The version, the CHANGELOG, the analyzer release files, the release procedure and every claim about releases made true before a tag. | open - needs owner decisions D1, D2; see Notes, "Documentation review" |
| T27 | **README.md as the package page** (the owner: "I saw for example in readme some old mid statistics from old tests, that are probably not that apropriate to have"). Broken first-screen rendering, relative links NuGet cannot resolve, stale statistics, overclaims, internal noise. | open; see Notes |
| T28 | **The two security documents**: one policy page, one claim register, no contradiction between them. | open; see Notes |
| T29 | **`docs/diagnostics.md` in product terms**: present-tense entries under one rule for history, no round/task/finding labels, no ledger links, no benchmark figures, two rendering defects and one contradiction. | open; see Notes |
| T30 | **The how-to guides**: versions, the testing package, the packaging story told once, a sample region that stops before its call, the index. | open; see Notes |
| T31 | **The core docs**: wrong enum advice in the migration material, COMPARISON's performance section rebuilt around the current run, PERFORMANCE.md's views story, the dated plan and research files, the API reference renderer. | open - owner decisions D4, D5; see Notes |
| T32 | **Samples, benchmarks and contributor notes**: stale comments and SDK images, the Gallery's hand-written headings, a samples index, subject names for round-named files. | open; see Notes |
| T33 | **Repository layout as an outsider sees it**: `Issues/` and `docs/superpowers/` under one clearly internal folder, user docs stop linking into round ledgers, `benchmarks/results/` indexed. | open - owner decision D3; see Notes |

## Notes

- **T1.** All 24 places that decide by helper family were read. One real defect: `IsAnySynthesized` knew only names
  under `__DwarfMap`, so the fast-path collection helpers (`__DwarfWiden*`, `__DwarfBlit*`) and the dense-enum helper
  counted as methods the author declared, and DWARF116 told the reader their value "goes through
  '__DwarfWidenL_21d8533...'". Fixed; `AllowNullIntentTests` holds it (3 of 4 shapes red before). The other sites
  were checked against that reading: the two collection-element sites and the dictionary one require an object
  pair, which a collection helper's types never are, and the suite and the golden outputs are unchanged with the
  wider answer. The `[FlattenGraph]` leaf rule was the other one, fixed before this list (`4f5c384`). The plain
  Preserve path keeps identity through these helpers (`FlattenGraphPreserveLeafTests`).
- **Found on the way: the package-size ceilings.** Rehearsing the nightly `package-size` job at this branch's head
  was red (363 over 362 KB, 57 over 56): the README is packed into both packages and the documentation corrections
  lengthened it. PR #12's checks could not show it, the job being nightly-only. Ceilings re-measured entry by entry
  and the job moved to every event (it takes 0.7 min on a runner).
- **T2. A real defect, wider than the cell.** Under `ReferenceHandling.Preserve` or `OnCycle.SetNull`, a NESTED use
  of a pair that has a declared partial - a member, or the elements of a collection - does not call that partial.
  It is routed through a generated `__DwarfMap_Obj_*` helper (`PrefersSynthesizedObjectMap`: the public method
  cannot take the shared context), and that helper applies the class's pair-scoped directives but NOT the
  directives written on the method. Measured with `[MapIgnore("Dropped")] partial D MapCell(C c)` beside
  `partial RD Map(R r)` where `R` has a `C Child` and a `List<C> Kids`:
  - no reference handling: `Child = MapCell(r.Child)` - honoured;
  - Preserve and SetNull: the helper contains `Dropped = s.Dropped` - the ignored member is mapped, with no
    diagnostic. For an array of layout-identical structs under SetNull the same pair is block-copied instead
    (`__DwarfBlit_*`), with the same result.
  `[MapIgnore]` is how a field is kept out of a transfer object, so this is a correctness defect with a
  confidentiality edge. `ArrayBlitElementConfigTests` holds the mode where it is right.
  **Decided by the owner, 2026-11-13: A** - the helper replicates the method's directives. Done: the helper built
  for a pair the class declares as a partial reads that method's `[MapProperty]` (renames, `NullSubstitute`, `When`,
  `StringFormat`), `[MapIgnore]`, `[MapValue]`, `[MapIgnoreSource]`, `[MapNullSkip]`, flatten roots, `[Reinterpret]`,
  `[MapShare]` and `[MapDenseEnumKeys]`, merged with the pair-scoped forms as the method merges them, with the
  class-level ignores; and the block-copy gate counts a directive on that method as customization. Tests:
  `NestedDeclaredPairDirectiveTests` (generator, every new line and branch covered), and at runtime
  `NestedDeclaredPairDirectivesTests` and `ArrayBlitElementConfigTests`, one fact per mode. Not replicated, and
  then unmeasured: `[FlattenGraph]` and `[MapDerivedType]` on such a method. Measured since: both are bypassed, and
  the dispatch pair had older holes beside it - T12 to T15 below.

- **T5.** All 111 sections and 17 registry rows match their descriptors on id, title and severity. Eight prose
  defects corrected: a `.globalconfig` downgrade of DWARF088 does not let the build proceed; the registry
  diagnostics ARE release-tracked; DWARF074 never refused a non-update method (that is DWARF092); DWARF044 is a
  Warning; a dead `#dwarfr11` anchor; a misspelled path; DWARF102 missing from the reserved ids; a wrong id in a
  fence comment. Two stale source comments fixed with it (`[MapNullSkip<,>]` duplicates are DWARF099, not "first
  wins"; DWARF103's `in` advice starts at 64 bytes, not 32).

- **A red `net11` check on PR #13, and its cause.** At `0ebe50c` the job failed once:
  `Span_map_into_caller_memory_allocates_zero_bytes` read 4,896 bytes over 1,000 maps (run 43439030599; the re-run
  was green). Not the mapper and not the JIT: the test's own body read zero in every one of 3,000 windows across
  tier-up, on runtime 10 and 11, under five tiering settings. The cause is the counter:
  `GC.GetAllocatedBytesForCurrentThread` jumps by the unused rest of a thread's allocation buffer when a
  **background** gen-2 collection started by another thread reaches it. Probe:
  [`AllocationCounterProbe.cs`](AllocationCounterProbe.cs), in `sdk:10.0.101` and `sdk:11.0.100`, four CPUs, about
  four seconds a mode:

  | what the other thread does | runtime 10.0.1 | runtime 11.0.0 |
  |---|---|---|
  | `GC.Collect(0)`; small allocations (gen-0 collections) | 0 windows moved | 0 |
  | blocking compacting gen-2 collections; `GC.GetTotalMemory(true)`; `GetTotalAllocatedBytes(true)`; no-GC regions | 0 | 0 |
  | non-blocking gen-2 collections (`bgc`) | 91 to 705 of 17,000 to 30,000 | 10 to 64 of 17,000 to 26,000 |
  | 200 KB allocations (`loh`, which start background collections) | 55 to 225 of about 55,000 | 41 to 128 of 28,000 to 53,000 |
  | the last two with `DOTNET_gcConcurrent=0` | 0 | 0 |
  | the last two when the measuring thread allocates once, before its first window | 0 of about 270,000 | 0 of about 275,000 |

  The deltas run from 16 to 8,176 bytes (one allocation quantum less one object), and the collection count does not
  mark a moved window (every moved window had an unchanged gen-2 count), so a window cannot be discarded by asking.
  It is not new in .NET 11; the job that happened to catch it runs the suite there.

  The last row of that table is about 18 exposures, not 270,000: a thread that allocated once has one unused rest
  to lose, once. So the oracle was measured with the failing test's own body instead
  ([`ZeroAllocationOracleProbe.cs`](ZeroAllocationOracleProbe.cs)): trials of five windows of 1,000 `MapSpan`
  calls, each trial allocating its arrays first as the test does, beside a thread that keeps background
  collections going; six seconds a run:

  | | runtime 10.0.1 | runtime 11.0.0 |
  |---|---|---|
  | trials a run | 7,793 and 8,064 | 8,423 and 7,787 |
  | the FIRST window non-zero (the old one-window test fails) | 123 and 113 | 108 and 108 |
  | any one window non-zero | 2,605 and 2,740 | 2,771 and 2,573 |
  | two or more windows non-zero | 0 and 0 | 0 and 0 |
  | neighbour idle: any window non-zero | 0 of 35,524 | 0 of 36,480 |
  | `DOTNET_gcConcurrent=0`: any window non-zero | 0 of 1,937 | 0 of 1,805 |
  | one allocation planted per window: all five non-zero | 7,826 of 7,826 | 8,924 of 8,924 |

  So the old test's failure is reproduced on demand (about 1.5 % of trials under that neighbour), and the two
  tests that assert an exact zero now read five windows and allow at most one of them to be non-zero: the counter
  never moved twice in about 32,000 trials, and an allocating map moves all five every time. The tests with a per-call bound are not exposed (8 KB over their iteration counts is under their slack).
  IntegrationTests keeps concurrent collection on, as it keeps every runtime default: it measures what a consumer
  runs.

- **T12-T15, found by the follow-up T2 left open** ("not known to be bypassed: `[FlattenGraph]` and
  `[MapDerivedType]` on such a method"). They are bypassed, and the dispatch pair has older holes beside it. None of
  the four came with round 35; all are on `master`. Probe: one mapper per mode, a `Zoo` holding an `Animal` as a
  member, in a `List<Animal>` and in an `Animal[]`, `[MapDerivedType<Dog, DogDto>]` on `MapAnimal`, `Dog.Home`
  pointing back at the zoo for the cyclic rows, one process per case (2026-11-13, at `b0e51e2`):

  | base, mode | acyclic | cycle through the member | cycle through the list / the array |
  |---|---|---|---|
  | concrete, none | `DogDto` everywhere | `DwarfMappingDepthException` | `DwarfMappingDepthException` |
  | concrete, Preserve | **`AnimalDto` everywhere** (T13) | maps, as `AnimalDto` | maps, as `AnimalDto` |
  | concrete, SetNull | **does not compile** when an arm reaches back into the graph (T14) | | |
  | abstract, none | `DogDto` everywhere | `DwarfMappingDepthException` | **stack overflow, the process dies** (T12) |
  | abstract, Preserve | `DogDto`; **the list's two references to one dog become two objects** (T12) | maps | **stack overflow** (T12) |
  | abstract, SetNull | `DogDto` | maps | **stack overflow** (T12) |

  The generated text says why. A member of the pair calls `__DwarfMap_Depth_MapAnimal(x, ctx, depth)`, the
  context-taking companion. The collection helpers for an abstract base call the public `MapAnimal(item)`, which
  opens a fresh context: no depth, no identity, nothing on the stack to find. For a concrete base under Preserve or
  SetNull the pair is "a mappable object pair", `PrefersSynthesizedObjectMap` hands it to a `__DwarfMap_Obj_*`
  helper built from the base type's members, and the dispatch is never asked. T14 is the same hand-over seen from
  the arm: nothing calls the dispatch, so the arm's helper is never marked as needing the context its callee takes.
  T15: `[FlattenGraph(nameof(G.Entry), nameof(GDto.Nodes))] GDto MapG(G)` alone is clean in all three modes; a
  holder with a `G` member or a `List<G>` is clean without reference handling (it calls `MapG`) and DWARF001 on
  `Nodes` under Preserve and SetNull.

- **T12, T13, T14 fixed.** After the fixes every one of the 24 probe cells is right: a `DogDto` on every route in
  every mode and with either base, `DwarfMappingDepthException` for a cycle without reference handling, the
  shared instance under Preserve, `null` on the back edge under SetNull, no process death, and the SetNull case
  compiles.
  - **T14, the cause of two things.** `MarkRecursionCapableCallers` read `recursionCapableNames` once per method,
    in list order, so a caller sitting before a callee that only became context-taking further down was never
    revisited; a second pass existed for Preserve alone. The step is now repeated until a pass changes nothing
    (`PropagateContextTo`). That also closed something filed elsewhere: a span or async-stream method over an
    element with a self-referencing member called the element's helper with one argument (CS7036). The option
    matrix compares text, both settings of `MaxDepth` produced the same uncompilable text, and the cell was
    recorded as "silent" - the last row of `DeclaredDivergences`, now removed, both ceilings at 0.
  - **T12.** A declared mapper method as a collection or dictionary element is a context-upgrade candidate for
    every pair and under SetNull too (it was: no reference handling and a "mappable object pair" only, which an
    abstract source is not). Under Preserve the helper calls the method's depth companion from the first synthesis
    (`NestedMappingRegistry.RequireDepthCompanion`), because identity needs the shared context whether or not
    there is a cycle; that route was chosen over a Preserve form of `SynthesizeInPlace`, which would have grown two
    methods already over the parameter ceiling. An overloaded name keeps the public call, as everywhere a
    companion is chosen by name.
  - **T13.** `PrefersSynthesizedObjectMap` no longer hands a `[MapDerivedType]` dispatch to a synthesized map of
    the base type (`IsDeclaredDispatchMethod`, asked through the registry as the pair-customization rule is).
    Behaviour to know: a plain base instance nested under Preserve/SetNull now throws as the dispatch does
    everywhere else; it used to be mapped as the base.
  - Tests: `DispatchPairUnderReferenceHandlingTests` (generator: 46 cases - every mode x base x cyclic for
    "compiles", "the helper calls the context-taking form", "no map of the base type, no DWARF071"; the overloaded
    name; the span/stream case; the two predicates) and `DispatchPairNestedRuntimeTests` (runtime: 36 facts over
    four type families and three modes, member / list / array / dictionary value, identity under Preserve, the
    four cycles per mode). RED on record: the generator theory failed 1 of 12 before T14 (SetNull, concrete,
    cyclic); the runtime file could not have been run before T12 (three of its mappers killed the process, one did
    not compile), and after T12+T13 two Preserve facts still failed (`Assert.Same`, the acyclic families) until
    the companion route. Patch coverage of the six generator files: 0 uncovered, 0 partial (cobertura, whole
    Generator.Tests, 18,269 passing).

- **T15 fixed, by the rule T13 added.** The probe first: with the flatten method keeping its declared route, the
  holder builds in all three modes, the nested member and both list elements carry the two flattened nodes
  (also when the nodes form a ring), and under Preserve `Many[0]`, `Many[1]` and `Inner` are one object - the
  list reaches the method through its depth companion (T12), the member through the same context. So nothing is
  refused and nothing is lost: `IsDeclaredDispatchOrFlattenMethod` answers for both kinds, the two whose body is
  not a member-by-member map of the pair. Tests: `FlattenGraphNestedPairTests` (generator, 6 cases) and
  `FlattenGraphNestedPairRuntimeTests` (runtime, 6 cases). RED: before, the Preserve and SetNull mappers of the
  probe did not build (DWARF001 'Nodes', then CS8795 for every partial of the class).

- **T4 fixed.** `HasNonNullInitializer` read property and field initializers and nothing else, so a member whose
  declaring syntax is a record's parameter had "no initializer to read" and was reported - pinned as a known false
  positive in round 32 and carried through rounds 33 and 34. A parameter with no default is always passed, and its
  type is the member's (non-nullable, or DWARF066 is not asked at all), so it now counts as a non-null default; a
  parameter default is read as an initializer is. The test that pinned the false positive is replaced by its
  opposite, and by a theory over the three defaults. RED: 2 of 12 in `WhenGuardInitializerTests` with the tests
  changed and the generator not; GREEN: 12 of 12. A `KeepDefault = true` written to quiet the old report is now
  DWARF116, which the changelog says.

- **The T12 route, checked against what it was not written for** (review before the push). Under Preserve the
  companion was first given to every non-overloaded declared method reached as an element. Probed on five shapes:
  an enum map as list element, dictionary key and value; a struct pair; a class pair under `AutoNest = false`; a
  plain map from an abstract source; a hand-written converter. All compiled, but the enum got a depth companion and
  a depth check per element, for a value that has no identity. The companion is now for a reference-type element
  only (`PreserveCompanionFor`), and the shapes are tests: `DeclaredElementUnderPreserveTests` (generator, 3) and
  `DeclaredElementUnderPreserveRuntimeTests` (runtime, 3 - `Assert.Same` across member, list, array and dictionary
  for the `AutoNest = false` pair and the abstract-source pair).
- **T13's throw is pinned.** `DispatchPairNestedRuntimeTests` gained, for the two concrete-base families in each
  mode, a fact that an instance of the base type itself is refused with `ArgumentException` when the dispatch is
  called directly and on every nested route (member, list, array, dictionary) - 6 facts, 42 in the file. This is
  the one change of the round that turns output a caller may have relied on into an exception: under Preserve and
  SetNull such an instance used to be mapped as the base type (by the same skip that turned a dog into an
  `AnimalDto`).
- **T16, found by one of those tests.** The enum row of the runtime test read `[Open, Open]` for
  `[Closed, Open]`: the declared enum map returned the default. Probe, 15 declared pairs: a destination that is an
  enum, `int` or `long` was generated as `return new T { };` for 8 of them, in a clean build; a `string`
  destination was DWARF024 x4 (the parameters of `string`'s constructors), a nullable enum DWARF007 (`HasValue`).
  The same 15 as `[GenerateMap]`: converted, through the helpers a member gets. `ExtractGenerateMapPairs` carries
  the comment of the earlier fix - "An ENUM target needs the same treatment ... Every VALUE-like target" - and the
  declared method's "Fix 1" block, the same route at the other door, never got it: a fix applied to one of two
  identical sites. First attempt, taken back: a refusal (the destination "has no members"); the attribute probe
  showed the feature exists, so the declared method now takes the same route through one shared predicate
  (`IsValueLikeDestination`, which also looks through `Nullable<T>`). What that route cannot emit as
  `return helper(value);` is DWARF117 at both doors: no helper (the same type, an implicit conversion - a bare
  CS8795 before, or nothing at all from the attribute) or a null to handle first (uncompilable when first tried:
  CS1503, `Status?` handed to a helper taking `Status`). Tests: `ScalarTargetMapMethodTests` (generator, 28: nine
  conversions, the two doors agreeing on each, four no-conversion pairs and four nullable pairs refused at both,
  two object destinations unchanged), `ScalarMapMethodRuntimeTests` (runtime, 5 - the enums are declared in
  opposite orders, so a map by name and a cast disagree), the negative case, and `DeclaredPairReuseTests`' int to
  long pair, which pinned "compiles clean" for an attribute that generated nothing and now pins DWARF117 beside a
  converting pair. RED: with the first version of the tests and the generator unchanged, 12 of 15.

- **T17, found by the last probe row asked of T15.** T15's tests cycled inside the walk (the nodes in a ring). A
  cycle THROUGH it - a node member `Owner` pointing at the holder, whose map flattens the graph again - killed the
  process in all three modes: `none back`, `preserve back`, `setnull back`, each "PROCESS DIED". Without reference
  handling that is on `master`; under the other two it had been unreachable only because the shape did not build
  before T15. The generated text says why: `MapG` fills `Nodes` through `__DwarfMap_FlattenGraph_*`, whose
  flat-node helper is text with one parameter and calls `Map(n.Owner)`; `Map` calls `MapG`. The call graph never saw
  the helper's call, so nothing was on a cycle and nothing got a depth guard - and a guard could not be threaded
  through that helper anyway. So the shape is refused: each leaf loop records what its flat-node helper calls
  (`RecordFlattenLeafCall`), and `ReportFlattenLeavesLeadingBack` asks, on the call graph as the cycle phase left
  it, whether that call reaches the flatten method. DWARF118, Error, at the flatten method, naming the member and
  the converter. Tests: `FlattenGraphLeafLeadsBackTests` (generator, 6: refused in each mode; a leaf through a
  declared method that does not lead back is flattened; the direct case, a member of the flattened type) and the
  negative case. T15's own tests are unchanged and pass: their nodes have no such member.

- **"The last correction had to break something" - checked directly** (the owner's question, 2026-11-13). The same
  oracle probe against IntegrationTests built from `6719413`, the commit BEFORE T2, in a fresh clone: the old
  one-window test fails there too - 75 and 63 of about 7,360 trials on runtime 11.0.0, 86 and 82 of about 7,700 on
  10.0.1 - and never with an idle neighbour (0 of 36,457; 0 of 34,736). After T2 the figures were 108 and 108, 123
  and 113. Never two disturbed windows in a trial on either build. The T2 change did not introduce the reading; the
  allocation counter moves under a background collection on both.

- **The exact-zero tests read on a fresh thread** (the owner, 2026-11-13: "tests must be fixed"). The five-window
  oracle tolerated the counter's movement; this removes it. Two things measured first:
  - **The before/after-T2 rates do differ.** Pooled, 306 disturbed trials of about 30,100 before T2 and 452 of about
    32,000 after: a two-proportion test gives z of about 4.5, so it is not noise, and my earlier "not tested for
    significance" should not be read as "probably equal". It does not make T2 the cause - the reading occurs on both
    builds and on neither with an idle neighbour - but the size of the effect depends on what else the assembly
    holds, and the test must not depend on that.
  - **A collection counter cannot mark the disturbed windows.** With `GC.CollectionCount(2)` and
    `GC.GetGCMemoryInfo(GCKind.Background).Index` read around each window, 11,423 of 11,449 disturbed windows saw
    neither move (runtime 10.0.11, continuous background collections): the collection that moves the counter began
    before the window. So "discard a window that saw a collection" is not available.
  - **What is exact: a thread that owns no allocation buffer.** The jump is the unused rest of the measuring
    thread's buffer; a thread started for the one window, touching only what its caller allocated, has none.
    [`FreshThreadAllocationProbe.cs`](FreshThreadAllocationProbe.cs), one window of 1,000 `MapSpan` per trial, each
    on its own thread:

    | runtime | neighbour | trials | window non-zero | thread had allocated before the window |
    |---|---|---|---|---|
    | 10.0.11 (Windows) | continuous background collections | 59,227 | 0 | 0 |
    | 10.0.11 | one every 20 ms | 152,572 | 0 | 0 |
    | 10.0.11 | idle | 177,613 | 0 | 0 |
    | 10.0.11 | continuous, **allocation planted** | 19,437 | 19,437 | 0 |
    | 11.0.0 (sdk:11.0.100 container) | continuous | 26,839 | 0 | 0 |
    | 11.0.0 | one every 20 ms | 71,752 | 0 | 0 |
    | 11.0.0 | continuous, **allocation planted** | 7,091 | 7,091 | 0 |

    The same loop on the calling thread, same neighbour, same build: 11,449 of 188,735 windows non-zero.
  - **The change.** `CostContract.ExactBytesOnFreshThread(operation, iterations)` returns the bytes the thread had
    allocated when the window opened and the bytes allocated in it. `Span_map_into_caller_memory_allocates_zero_bytes`
    and `Blittable_struct_map_allocates_zero_bytes` assert both are exactly 0, in one window - the five windows and
    the "at most one" are gone. Two tests hold the oracle itself: one object allocated in the window is read, and an
    exception from the operation is rethrown on the calling thread. The test host keeps the runtime's defaults
    (concurrent collection on), as E3 requires.

- **T18: DWARF119, "a [MapDerivedType] dispatch has no arm for its own instantiable base type"** (the owner's
  ruling on T13's consequence). The rule: the dispatch's source is a class, not abstract, not `object`, and no arm's
  source is that type. Reported beside DWARF114, at the plain dispatch and at the node dispatch of a
  `[FlattenGraph]` method - the three places that emit the "no [MapDerivedType] registered" fallback are the
  dispatch method, its Preserve/SetNull wrapper (same arms) and the flatten node dispatch.
  - **The advice was proven before it went into the message.** Probe, one mapper per mode, the base arm declared
    FIRST, a zoo holding a plain `Animal` and a `Dog` as member, list element and dictionary value: the base comes out
    as `AnimalDto` and the dog as `DogDto` on every route in all three modes; under Preserve one target per source
    across all routes and the cycle through the base closes on the same zoo; SetNull nulls the back edge; None
    throws `DwarfMappingDepthException`. A `[FlattenGraph]` node dispatch with a concrete node base threw
    `ArgumentException` for a base node and flattens it with the arm.
  - **Two sources, one target** (`Command`, `AliasCommand` -> `CommandOverviewDto`, the consumer shape of F44): the
    base arm's pair is the method's own pair, and the method's own `[MapIgnore(nameof(Alias))]` is read for that
    arm only - a plain command gives `Alias = ""`, an alias command still fills it. That is what F44 and
    `DerivedTypeArmCompositionTests` now declare.
  - **Blast radius, measured by building and running everything with the check in:** 9 mappers in 4 files of this
    repository did not build (Conformance F44, DifferentialTests' `DwarfShapes`, `Par_DerivedMapper`, the six
    concrete-base mappers of `DispatchPairNestedRuntimeTests`), and 31 tests failed - 21 on fixtures of that shape
    (each given the arm, or an abstract base where the test is about a base that maps nothing), the golden
    `DerivedTypes` case (source, manifest line and snapshot: the arm and its helper, nothing else), and the
    registration checks. `DispatchPairNestedRuntimeTests`' six "base instance is refused on every route" facts are
    now "is mapped as the base on every route". `LoudRatherThanSilentTests` says what is now true: DwarfMapper
    refuses the undeclared base in the build, Mapperly at run time, AutoMapper not at all.
  - **What the arm matches - for the owner.** An arm matches every subtype of its source (DWARF114's coverage rule
    says so). So with the base arm, a subtype that has no arm of its own is mapped as the base, its own members
    dropped, where it threw before. That was already how a base arm behaved (F43, and the "sibling" test, are on
    `master`); DWARF119 makes more people write one. The docs section says it. Making the base arm match EXACTLY
    the base type would keep the unknown subtype loud; it would also change mappers that use the base arm as the
    catch-all today. Not done; a decision.
  - Verified: whole solution 0 warnings; 20,034 tests green; the new method's lines 10/10 branches, and no generator
    file changed its count of uncovered lines or partial branches against the run before it (whole files compared,
    not only changed lines - the hole Codecov found in T12).
- **T19, found by T18's probe and NOT fixed.** `[MapDerivedType<AliasCommand, OverviewDto>]` beside a declared
  `[MapIgnore(nameof(OverviewDto.Alias))] private partial OverviewDto PlainToDto(Command c)`: the ALIAS arm adopts
  `PlainToDto` (an `AliasCommand` converts to its parameter), so an alias command comes out with `Alias = ""`, in a
  clean build. Measured: `B: plain=(n,'') alias=(n,'')` against `A: plain=(n,'') alias=(n,'al')` with the ignore on
  the dispatch method instead. It is the same adoption rule "an arm may resolve to any declared method" (F43)
  applied to a method whose parameter is a BASE of the arm's source. Older than this round.

- **A fresh thread alone was not enough: macOS** (PR #13's `cross-platform (macos-latest)` at `420e7f8`, run
  43501831387). Three tests failed on `Assert.Equal(0, reading.BeforeWindow)` with 152: on macOS a new thread has
  allocated 152 bytes by the time its entry point runs, so it owns an allocation buffer like any other thread. The
  window itself read zero; it was the premise I had asserted that was false there, and with it the claim that the
  reading could not be moved. Windows and Linux start a thread having allocated nothing (0 of 389,412 and 0 of
  98,591 trials), which is why the probes had not shown it.
  - **What is exact on a thread that HAS allocated: retire its buffer first.** The counter never moved under a
    blocking collection, which retires every thread's buffer with the unused rest accounted for. So the measuring
    thread runs `GC.Collect(0, GCCollectionMode.Forced, blocking: true)` and allocates nothing more before the
    window. [`RetiredBufferAllocationProbe.cs`](RetiredBufferAllocationProbe.cs), one window of 1,000 `MapSpan` on the
    CALLING thread, which allocates three arrays of varying size before each:

    | runtime | before the window | neighbour | trials | window non-zero |
    |---|---|---|---|---|
    | 10.0.11 | nothing | continuous background collections | 216,186 | 2,686 (largest 8,176 B) |
    | 10.0.11 | blocking gen-0 collection | continuous | 27,664 | 0 |
    | 10.0.11 | blocking gen-0 collection | one every 20 ms | 418,087 | 0 |
    | 10.0.11 | blocking gen-0 collection, **allocation planted** | continuous | 7,500 | 7,500 (24 B) |
    | 11.0.0 | nothing | continuous | 99,953 | 852 (largest 8,184 B) |
    | 11.0.0 | blocking gen-0 collection | continuous | 10,367 | 0 |

    And the form the tests use ([`FreshThreadAllocationProbe.cs`](FreshThreadAllocationProbe.cs), modes
    `mapdirty`/`plantdirty`): a thread started for the window that allocates 152 bytes first, as macOS's does, then
    collects - 0 of 20,810 and 0 of 67,932 on 10.0.11, 0 of 11,379 and 0 of 41,076 on 11.0.0; planted, 6,375 of
    6,375 and 3,754 of 3,754.
  - **The change.** `ExactBytesOnFreshThread` runs that collection on its thread before the window and returns the
    bytes of the window alone; the `BeforeWindow` reading and the three assertions on it are gone. macOS itself is
    not available here: its verdict is the `cross-platform (macos-latest)` job of the next run.

- **T19 read again, in the code (2026-11-14): I over-called it.** The adoption rule is one rule for every site
  (`FindUserDeclaredConversion`): a declared method is adopted when the source converts implicitly to its parameter
  and its return converts to the target. So a method declared for a base type serves a derived source - a member, an
  element or an arm. What that can lose is narrow: the adopted method maps base to target COMPLETELY (the
  completeness gate holds every target member to a source member or an explicit ignore), so the only target member a
  derived source could have filled and did not is one the author **ignored by name on that method**. In my probe
  that was `[MapIgnore(nameof(OverviewDto.Alias))]` on `PlainToDto(Command)`, written by me, and the alias arm then
  honoured it. Nothing was dropped that the declaration did not say to drop.
  - The two changes I considered, and why neither is made. (1) *An arm adopts only a method declared for exactly its
    own source type.* That breaks a sound pattern loudly: an abstract `Item`, arms `Audio -> ItemDto` and
    `Book -> ItemDto`, and ONE declared `ItemDto ToBaseDto(Item)` carrying the `[MapProperty]` configuration for
    both - each arm would synthesize a map of its own that knows none of it and fail completeness. (2) *A warning
    when the adopted method ignores a target member the derived source has.* Precise, but it second-guesses an
    explicit `[MapIgnore]`, and it needs the adopted method's attributes at a point that today carries only its
    name and two types.
  - Recommendation: no change; the base-arm form of the same intent (`[MapIgnore]` on the dispatch method, read for
    the arm of the method's own pair only) is what the docs and F44 show. If the owner wants (2), it is a small
    feature, not a fix.

- **The 2026-11-13 AutoMapper advisories** (GHSA-6vhm-j9h7-f9gh / CVE-2026-128082 and GHSA-gpwp-5797-mjff /
  CVE-2026-128081, both high, published the evening PR #13 was pushed). NU1903 failed every restore: six jobs red on
  the next run, none from this branch's changes; `master`'s nightly is red from the same hour.
  - **The gate.** AutoMapper 14.0.0 is the last MIT release; the fixes are in 15.1.4 and 16.3.0 only, which are
    RPL-1.5. The repository's answer to the first advisory (round 31, T01) was an advisory-scoped
    `NuGetAuditSuppress` in the three projects that use AutoMapper as a comparison oracle, so that the NEXT advisory
    would fail the restore - which is what happened. The two are suppressed the same way, at the same three sites,
    with the reason beside them. **This is an audit exception and was the owner's decision, asked and answered on
    2026-11-14** (the safety classifier, rightly, would not let me add it unasked).
  - **The same shapes against DwarfMapper.** Probe `cveprobe` (one process per case, because the failure looked for
    is a process death), 30,000 levels deep, every shape the two advisories name that can be declared here:

    | shape | none | Preserve | SetNull |
    |---|---|---|---|
    | constructor parameter (`Ctor(int v, Ctor? next)`) | depth exception | build error DWARF030 | depth exception |
    | positional record | depth exception | build error DWARF030 | depth exception |
    | constructor parameter that is a list | depth exception | build error DWARF030 | depth exception |
    | chain of derived objects (dispatch), direct and as a member | depth exception | depth exception | depth exception |
    | interface-typed member (dispatch) | depth exception | depth exception | depth exception |
    | dictionary value | depth exception | depth exception | depth exception |
    | **hand-written converter that calls the mapper** (`Use = nameof(Conv)`, `Conv` calls `MapN`) | **PROCESS DIED** | build error DWARF032 | **PROCESS DIED** |

    "depth exception" is `DwarfMappingDepthException`, catchable. Held from now on by
    `AdvisoryRecursionShapesRuntimeTests` (7 facts, depth 500 as in `DeepRecursionGuardRuntimeTests`, so a regressed
    guard fails an assertion and does not end the test host).
  - **T20, the last row.** `MapN` is emitted as a plain object initializer that calls `Conv(n.Next)`; `Conv` is the
    user's code and calls `MapN`; nothing on that loop counts depth, because the generator's cycle analysis sees
    only the calls it emits. It is the analogue of the advisory's "recursive type converters ignore MaxDepth", and
    it contradicts SEC-01 and COR-06 as they are worded ("no configuration yields a silent
    StackOverflowException"). Open: the fix is a design change to what a mapping method emits at its entry, to be
    measured before it is made.
  - **T21, met on the way.** The advisory's own PoC class, `CircularClass(CircularClass? Next)`, does not build as a
    destination: `ConstructorSelector.IsUsableCandidate` drops any constructor with a single parameter of the target's
    own type as "the record copy constructor", for classes that are not records too, and the report is DWARF026 "no
    accessible non-obsolete instance constructor". The probe's class was given a second parameter to get past it.

- **T20 done: a stack check where user-written code can come back in.**
  - **RED, widened first** (probe `ReentryProbe.cs`, one process per case, a chain 200,000 levels deep): a
    `Use=` member converter, a hand-written method adopted by its signature, an `[AfterMap]` hook without reference
    handling and under Preserve, the converter under SetNull, an update-into method re-entered from its converter, a
    converter that calls `Dwarf.Map`, and a span map whose element converter maps a span - **eight of eight
    "PROCESS DIED"**. After the fix, eight of eight `THROWS InsufficientExecutionStackException`.
  - **Why a stack check and not the depth counter.** The loop runs through code the generator did not write and
    cannot thread a context through; the only thing every such loop has in common is that it comes back in through a
    declared mapping method. `RuntimeHelpers.EnsureSufficientExecutionStack()` there is the BCL's own answer (and
    AutoMapper's fix for the same class), written inline: nothing new in the runtime library.
  - **Why not everywhere.** Measured (`50,000,000` calls, best of five, three rounds): a four-member class map
    4.7 ns -> 5.8 ns (+1.1 ns, about a quarter), a three-field struct map 1.8 ns -> 2.8 ns (+0.9 ns, half). So the
    check is emitted only for a mapper that holds user-written code.
  - **The rule** (`MapperExtractor.HoldsUserWrittenCode`): the mapper class or a class it derives from declares an
    ordinary method with a body - anything but an abstract method or a partial declaration nobody implemented, which
    is what the mapping methods themselves are while the generator runs. Asked of the class, not of what each method
    binds: converters, adopted methods, factories, `When` predicates, `[MapValue(Use=)]` and hooks are all methods on
    the mapper, the list of ways one comes to be called is long, and a missed kind is a process death; the price of
    asking the class is a nanosecond on a mapper whose helpers never call back. Every declared method of such a
    mapper gets the check - create, dispatch, flatten, projection, update-into, span, async stream; the helpers the
    generator writes for itself do not, they are not entered from outside.
  - **Not covered, and said in the docs:** code that is not on the mapper and reaches one by itself (a property
    getter or conversion operator on a mapped type). And `MaxDepth` still does not count the loop: the exception is
    `InsufficientExecutionStackException`, not `DwarfMappingDepthException`.
  - **What moved:** four golden cases (`Hooks`, `KeepDefault`, `WhenGuardDefaults`, `ElementViaDeclaredMap` - each
    mapper has a method body of its own) and three snapshots, each by the one added line; `MapMethodModel` has a
    19th flag, `ChecksStack`, classified in `StructuralSurfaceCoverageTests`. Claims SEC-08 and COR-07 are bound to
    `ReentryStackCheckRuntimeTests`.
  - Tests: `ReentryStackCheckTests` (generator, 10: each entry kind of such a mapper opens with the check and
    nothing else carries it; a hook, any method with a body, an inherited method; none for partial declarations
    only, `[GenerateMap]` only, an abstract method, a property) and `ReentryStackCheckRuntimeTests` (8: the eight
    routes on a thread with a one-megabyte stack, so three platforms agree; and a chain the stack can hold maps
    level for level).
- **T21 corrected.** The single-parameter-of-own-type rule in `ConstructorSelector.IsUsableCandidate` is deliberate
  and tested for classes as well as records (`AnnotatedConstructorUnusableTests`, the "COPY constructor" row). A
  linked-list class built only through `Node(Node? next)` is therefore not a destination DwarfMapper can construct;
  DWARF026's text for that case does not say why. Left as it is.

- **T22: DWARF120.** A test of T20 wrote `[DwarfMapper] public partial record M` and got the compiler's CS8795 and
  nothing from DwarfMapper: every pipeline in `DwarfGenerator.Initialize` filters on `ClassDeclarationSyntax`, so a
  record carrying `[DwarfMapper]` - or `[GenerateMap]` in its co-located form, which is the likelier thing to write
  on a record DTO - never reached an extractor. The first failed loudly but unexplained; the second generated
  nothing and said nothing, which is the "never silent" tenet broken. A ninth, collected pipeline reports both where
  they are written (Error, at the record's name, naming the attribute). Supporting a record host was not attempted:
  seven pipelines read class syntax, and that is a feature of its own. Nothing that built before stops building.
  Tests: `MapperOnRecordTests` (5) and the negative case.
- **T8: one Ubuntu.** The owner asked why the pipeline used two, and said yes to moving. 16 `runs-on` lines in
  `ci.yml` (the `aot-trim-gate` matrix entry among them), 2 in `release.yml` and 1 in `actions-security.yml` name
  `ubuntu-26.04`; `ubuntu-26-canary` is removed, with its row in `CiGateScanTests` and its allowance in
  `CiRunnerImagePinScanTests`, whose constant moved in the same commit as the rule asks. What the canary had already
  proven on 26.04, blocking and green since 2026-10-22: the push-time suite and the linux NativeAOT gate. What it had
  NOT: the six mutation legs, deep-test (coverage floors, ILVerify, benchmark smoke), reproducible-build, sbom,
  codeql, package-size, fuzz-exhaustion, isa-matrix, perf-tests, net11, and the release workflow - the `full-ci`
  run of the T8 pull request is their first measurement there, and the release workflow gets none until a tag.
  The check named `aot-trim-gate (linux-x64, ubuntu-24.04)` becomes `(linux-x64, ubuntu-26.04)` and
  `ubuntu-26-canary` disappears: a ruleset that requires either by name has to be edited by the owner
  (`master` has no classic branch protection - the API answers 404 - so nothing was found to edit).

- **T6: a widening span map takes the `Vector.Widen` loop.** Measured first (one-minute probe, then BenchmarkDotNet's
  DefaultJob on an idle machine; `benchmarks/results/2026-11-14-round35-t6-span-widen.md`): at 1,024 elements `int ->
  long` 1.56x, `float -> double` 4.51x, `byte -> ushort` 6.00x; at 16 elements 2.36x, 2.71x and 0.91x (+0.56 ns: a byte
  pair shorter than one 32-wide vector never enters the loop). A guard that keeps short spans on the plain loop bought
  nothing measurable, so the emitted loop is the List shape's, unguarded, as the array and List shapes have it.
  - **The decision is the array arm's own**: `CollectionConverter.IsWidenPair` on the resolved element pair, with no
    converter test, because the resolver assigns an implicit pair ahead of any user method (pinned by
    `SimdWidenGeneratorTests` for the array arm). A widen pair is never also a blit. No reference is involved, so
    Preserve does not change the answer. `MapMethodModel.SpanMapWidens`, the 20th flag, is classified in
    `StructuralSurfaceCoverageTests` as an internal flag of the span-map mode, as `SpanMapBlits` is.
  - **The loop** writes only inside both spans: it runs while `__i + __w` is at most the source's length, the length
    guard has made the destination at least that long, and `__w` is twice `__half` for every widen pair.
  - **Tests**: `SpanMapWidenTests` (generator, 12: the seven pairs, a mutable source span; two-step widening, kind
    change, nullable and same-size pairs keep the loop or the block copy) - eight of them RED with the new branch
    disabled; `SpanMapWidenRuntimeTests` (runtime, 10: every pair at lengths 0-70 and seven longer, random bits,
    element-for-element against the scalar widening, nothing past the source written, a short destination refused
    first, zero allocation), green natively and under no-intrinsics, 128-bit and no-AVX2 - the ISA matrix's rows.
- **T7: the pin 10.0.101 -> 10.0.404, and what it took with it.**
  - **Which SDK.** The 2026-11-10 release (runtime 10.0.15) ships two SDKs, 10.0.115 (the 1xx band, Roslyn 5.0
    serviced) and 10.0.404 (4xx, Roslyn 5.9). Microsoft publishes container images only for the newest band - the
    registry's 10.0 tags stop at 10.0.103 for 1xx - and package-size and every CI rehearsal here are measured in
    the SDK's container, so 10.0.404. 10.0.400, the one installed on this machine, ships runtime 10.0.11, still
    inside GHSA-p3jp-9v83-76w9.
  - **The override is dead.** `Directory.Build.targets` named runtime 10.0.15 for PublishAot projects because
    10.0.101 implied ILCompiler 10.0.1. 10.0.404 implies 10.0.15 itself; the override and
    `AotCompilerPatchScanTests` (written to fail on exactly this day) are removed. `dotnet restore
    --force-evaluate` in sdk:10.0.404 moved one lock file: src/DwarfMapper's Microsoft.NET.ILLink.Tasks
    10.0.1 -> 10.0.15. (That test's own rule "an SDK's last two digits are its runtime's patch" was true for the
    1xx band only: 10.0.404 ships 10.0.15, not 10.0.4.)
  - **Gates, in the sdk:10.0.404 container.** Build with warnings as errors: 0 warnings (the 4xx analyzers found
    nothing new). The whole suite: green. package-size: green at 370 / 57 KB. deep-test: **refused the allocation
    baseline for its SDK**, by design ("allocated bytes are deterministic per SDK"); re-measured with the job's own
    `housekeeping -Nightly` - all 63 benchmarks executed, all 28 pins byte-identical - and
    `allocation-baseline.json` now records SDK 10.0.404, runtime 10.0.15 and the container. No pin changed.
  - **The floor the pin used to hold.** README: "Toolchain floor: .NET SDK 10.0.100 or newer (Roslyn 5.0+)".
    10.0.101's compiler is Roslyn 5.0.0-2.25569.105, so until now every job built on the floor and the promise was
    held without being written down; 10.0.404 carries 5.9.0-1.26529.119. Measured what still runs on 5.0 after the
    move: the suite's in-process compilations (the tests reference Microsoft.CodeAnalysis 5.0.0, so the generated
    code of the whole corpus is still compiled by the floor compiler) - but no real project: not the samples, the
    consumer tests or the integration tests. **`toolchain-floor`** (new, blocking) installs the newest 10.0.1xx SDK
    into a directory of its own - with the pin dropped `dotnet` takes the newest SDK it can see, and a runner image
    carries newer 10.0 ones - proves the band, then builds (locked, warnings as errors) and tests the solution.
    Locked mode can stay on: 10.0.115 implies the same 10.0.15 packages the lock files hold.
  - **roslyn-forward-compat** runs on `10.0.x`, which is the pin itself until a newer 10.0 SDK ships; its comment
    says so. It starts looking ahead again then, before the pin follows.
  - **reproducible-build, found by rehearsing it:** NuGet 7.9 (in 10.0.404; 10.0.101 carried 7.0) names the
    core-properties part `nuget.psmdcp` instead of a fresh GUID, and the check required the GUID form - "0 .psmdcp
    part(s), expected exactly 1" on all four packages. Two packs compared entry by entry: every entry identical,
    the .psmdcp and `_rels/.rels` included; the whole-file hash differs only by the pack time in the zip headers of
    the four OPC entries. The check accepts the fixed name by its exact spelling beside the GUID form. Also
    rehearsed at this head: build-test, aot-trim-gate (linux; native binary published and run), conformance-gate.
  - **cross-platform (macos-latest), found by the full run (43596272659):** from the 10.0.4xx SDK on, an executable
    with PublishAot and no RuntimeIdentifier gets the host's portable RID as its default publish RID, appended to
    RuntimeIdentifiers (`Microsoft.NET.RuntimeIdentifierInference.targets`; 10.0.101 had no such rule). Linux and
    Windows hosts are already in the two AOT projects' list; macOS arm64 is not, so the locked restore refused the
    lock file there (NU1004). Reproduced on Linux with `-p:NETCoreSdkPortableRuntimeIdentifier=osx-arm64`; both
    projects now set `UseDefaultPublishRuntimeIdentifier=false` (every publish names its RID), and the same
    restore passes, the native solution restore passes, the explicit-RID AOT publish still runs green, and no lock
    file changed.
  - **For the owner:** the branch needs SDK 10.0.404 installed to build on this machine (`global.json` refuses any
    other, by design). I could not install it here (no administrator rights), so every check of this branch ran
    in containers.
- **T23 (2026-11-14, `feat/round35-t23`, stacked on T8).** Design and plan in `docs/superpowers/`. What landed:
  - **Two generator builds, one package.** `src/DwarfMapper.Generator.Roslyn512` compiles the 5.0 project's files
    (minus `*.Floor.cs`) plus its own folder against Roslyn 5.12; the package carries `analyzers/dotnet/roslyn5.0/cs`
    and `roslyn5.12/cs`, and `scripts/analyzer-selection-check.sh` proves from the packed package that 10.0.101 loads
    the 5.0 build and 11.0.100 the 5.12 build, one each (RED on the base: the generator came from `analyzers/dotnet/cs`).
    The golden corpus (1,021 cases) is byte-identical on the 5.12 build (`GoldenNeutralityTests`). Package size 370 ->
    636 KB, entry by entry in `gate-checks.ps1`.
  - **Union mapping on the 5.12 build** (C# 15 only): a declared union map, nested members, collection and dictionary
    elements, `Nullable<T>`, update-into, span and async stream, under None, Preserve and SetNull - 17 endpoint tests,
    13 route tests, a 105-cell matrix. Strict pairing (explicit arm > same type > one declared method), never by shape
    or name; the empty union maps to the empty union.
  - **Found on the way, each with RED -> GREEN:** a Roslyn 5.9+ compiler classifies a union conversion as implicit in
    C# 14 too, so `Use=`, hooks and direct assignment could write CS9327 into a C# 14 consumer's `.g.cs` (9 of 11 gate
    tests red on the old code); `[MapTo]` mapped union members as ordinary structs (`Pet` into `object` boxed the
    wrapper); counting union conversions made a `Pet` hook match every `Cat` map (DWARF109); a `Pet?` member into
    `PetDto?` was CS0037 in generated code (a bare `null` beside a union value converts to the union).
  - **The `net11` job now builds the consumers through the 5.12 build** (`-p:DwarfGeneratorFlavor=roslyn5.12`, an
    evaluation-time swap in `Directory.Build.targets`; 14 projects report it, the job counts them), packs and proves
    SDK 11 loads `roslyn5.12/cs` alone, and runs `samples/DwarfMapper.Unions` from the package. Rehearsing it found two
    things: MSBuild refuses `%(Filename)` in an item condition outside a target (MSB4190 - item functions instead), and
    **.NET 11 adds `string.StartsWith/EndsWith/LastIndexOf(char, StringComparison)`**, so CA1307 fails a char call that
    is clean on net10.0 - nine in Roslyn512.Tests and the fuzz schemas it links, now span forms (`MemoryExtensions` has
    no such overloads; the reference pack's XML docs omit the new ones, reflection shows them).
  - **Known gap, not fixed:** the 5.0 build detects a union by its `[Union]` attribute, and Roslyn does not return the
    compiler-synthesized attribute for a union declared in source. The 5.0 build meets a source-declared union only on
    a 5.10/5.11 preview compiler (5.12 loads the other build), where such a member is handled as a struct - for
    `Pet` into `object`, the old silent boxing. The 5.12 build asks `IsUnion`, which is right for both.
  - **Calls made in the owner's absence**, listed in the design document: strict pairing; empty maps to empty;
    `[MapDerivedType]` names union arms (no new attribute); no new diagnostic ids; the package grows ~260 KB.
- **T24 (2026-11-14).** `benchmarks/DwarfMapper.UnionBenchmarks` (net11.0, outside the solution): a three-case union and
  the class hierarchy holding the same data, one value from a ring of 512 and a batch of 1,000, payload content from
  `ObjectFactoryV2`; arms hand-written, a control (hand-written in the generated structure), DwarfMapper, and on the
  hierarchy Mapperly and Mapster; a value-case union; a `dry` mode comparing every arm with the hand-written one first.
  Measured on Linux (sdk:11.0.100, twice) and Windows (user-local SDK 11): the generated union map equals the control
  (8.14 against 8.12 ns, 6.29 against 6.27 ns) and trails the inline switch by 4-11 %, the call per case; a union maps
  faster than the hierarchy by hand (the hierarchy 9-25 % longer); DwarfMapper ahead of Mapperly on the hierarchy in
  every run; Mapster about 3x; allocations identical everywhere. Recorded as a loss, not fixed: the span list fill was
  4-7 % slower than `Add` for a union element. Before measuring, a probe of the competitors (Mapperly 4.3.1 and
  5.0.0-next.13, Mapster 10.0.8, 10.0.14 and 10.1.0-pre04, AutoMapper 14.0.0) found none maps a union: an empty union
  with a warning, a throw, or the first case mapped and the rest thrown after a passing configuration validation -
  now a row in `docs/COMPARISON.md`. Found on the way: Mapster cannot `Include` into an abstract record destination
  (fixed in its 10.1 prerelease), and the SDK 11.0.100 analyzers' CA1508 misreads a union switch statement.
- **The NoWarn audit and net11.0-only projects (2026-11-14, found preparing T23's push).** `scripts/nowarn-audit.ps1`
  builds every project from the repository root, under the pinned SDK 10, which cannot build the two net11.0 projects
  (NETSDK1045, measured): every code they declare would have read as stale and failed the nightly deep-test.
  `Get-DeclaredFrameworks` / `Get-FrameworksNewerThanSdk` (gate-checks.ps1, `NoWarnAuditFrameworkTests`) skip and name
  them there, and the net11 job audits them (`-Framework net11.0`, failing if nothing matched). Run in full locally, the
  audit then found two more of this branch's own: CA1861 and CA1508 in Roslyn512.Tests, copied from Generator.Tests'
  list in c1c67c5 for the linked corpus files, fire nowhere there - removed; its three remaining codes all fire. The
  net11 job's audit, rehearsed, found the third: CA1303 in both net11.0 projects, taken over from the Gallery's list,
  while they print only interpolated strings - removed.
- **Two more, from running deep-test's recipe (`housekeeping.ps1 -Nightly`) on the host before the push.**
  - **The generator's 100.0 % line floor: 99.9 %.** Eleven lines no test reached - ten in the 5.12 build's
    `MapperExtractor.Unions.cs` (two DWARF035 paths, a member-provider SOURCE union, a guard no caller reaches) and the
    5.0 build's `UnionMappingAvailable`, which only a Roslyn 5.9+ compiler loading that build reaches. Closed:
    `HasImplicitOrUnionConversion` is implemented per build (the 5.0 one IS `HasImplicitConversion`), the guard is
    gone, `AppendDispatchTail` tests two values instead of switching over three, and nine tests on the 5.12 build. The
    partial branches left are the base's four (measured on 98a2c3b). One measurement limit, recorded: coverage tools do
    not combine one shared line's branch outcomes across two builds, so a shared line whose union side only the 5.12
    build takes is covered whole on that build (its non-union side tested there too), not split.
  - **Both generator builds in one folder.** BenchmarkDotNet's child build passes one global `OutDir` to the whole
    graph; the package project referenced both generator builds (to pack them), both write `DwarfMapper.Generator.dll`,
    and the last one written won: the benchmark smoke loaded the Roslyn 5.12 build under SDK 10 - CS9057. The package
    project now references the analyzer projects only while packing (`_IsPacking`); `PackageProjectAnalyzerReferencesTests`
    (RED on the old file) holds it. The union benchmark had the same collision, and only worked because the 5.12 build
    happened to be written last.
- **The sbom job, from the first dispatch (run 43673564878 on 3e357e5).** CycloneDX (6.2.0, and 6.3.1, the newest)
  reads project files as XML, names a project by the `<AssemblyName>` written in it, and keys references by that name:
  two project files writing `DwarfMapper.Generator` stopped it on `DwarfMapper.csproj` ("An item with the same key has
  already been added"; reproduced in sdk:10.0.101, also with `--exclude-test-projects`). Both builds keep the name -
  the coverage floors and Codecov merge them under it - so the 5.12 project sets it from `AssemblyIdentity.props`,
  where CycloneDX does not read; 6.2.0 then completes on the solution (98 components). `ProjectFileAssemblyNameTests`
  (RED on the old files) reads project files the way CycloneDX does.
- **T25 (2026-11-15, `feat/round35-t25`, stacked on T23).** Against the latest stable releases - Mapperly 4.3.1, Mapster
  10.0.14 (T9's bump, cherry-picked with BenchmarkDotNet 0.15.8) - without AutoMapper, at the owner's direction.
  Write-up: `benchmarks/results/2026-11-15-round35-t25-comparison.md`; `docs/COMPARISON.md` gains the current
  performance table and the validation and security matrices.
  - **Performance**, Windows and Linux, one run each: DwarfMapper ahead where it has a path the others lack (blit
    2.1-2.3x, value-element list 2.4-2.8x, SIMD widen, nested fills), level on flat/flatten/nested/by-name enum,
    Mapperly 17 % ahead on the reference array on Windows (the documented null-guard trade); platform-dependent: the
    dictionary (2.3x Windows, 1.16x Linux). Mapster 1.5-3.5x on single objects.
  - **The Linux by-value enum gap** (2.1x Mapperly on Linux, level on Windows) is a measurement artefact, not the
    emitted code (`benchmarks/comparison-probes/enum-gap`). It is the same on runtimes 10.0.1 and 10.0.15. The JIT
    output of the two methods differs only by DwarfMapper's `ThrowIfNull`. In the probe, the method without any check
    (Mapperly's 73 bytes) measured 5.5 ns in one Linux process and 19.6 ns in the next, while every variant is within
    0.3 ns on Windows on the same CPU. The emitted check stays.
  - **Validation** (`benchmarks/comparison-probes/validation`, 8 mistakes x 3 libraries + Mapster strict): DwarfMapper
    5 build errors and 2 warnings; Mapperly 1 error and 5 warnings, each warned case compiling to a mapper whose output
    is quietly wrong; Mapster nothing before run time.
  - **Security** (`benchmarks/comparison-probes/security`, one process per cell): no input ends a DwarfMapper process
    (depth guard, stack check), but a 100,000-deep chain is refused; Mapperly and Mapster overflow the stack on a cycle
    and on re-entry; Mapster fails under NativeAOT on its first map.
  - **Docs and samples**: 0 dead relative file links in 230 Markdown files; 28 dead anchors, all in one round-20
    document; the Gallery (the examples README quotes) is built in CI but never run.
  - **Found and fixed here**: BenchmarkDotNet 0.15.8 cannot run .NET 11 (the union suite stays on 0.14.0); the cherry-
    picked BenchmarkDotNet lock file failed locked-mode restore (NU1004, it named T9's DependencyInjection 10.0.15).
  - **Left, proposed in the write-up**: the probes as DifferentialTests tests; the Gallery run and its output pinned in
    CI; the link check as a test.
- **Documentation review (2026-11-15), the source of T26-T33.** Four read-only reviews of every user-facing document
  (README/CONTRIBUTING/security; docs/; diagnostics and how-tos; samples, benchmarks, CHANGELOG, layout), the key claims
  re-checked by hand. Line numbers are as of `feat/round35-t25` at the review. Ground rules for every task:
  - **The pipeline owns part of the text.** Code fences in the 19 documents of `src/DwarfMapper.DocTooling/DocSet.cs`
    are injected from sample regions (`DocsAreSnippetCurrentTests`), the options tables and the Gallery index are
    rendered, and `docs/generated/*` is rebuilt by `GeneratedDocsAreCurrentTests`. A wrong code block or table is
    fixed in its sample, renderer or XML doc - never in the Markdown. Only prose outside the markers is hand-written.
  - **README.md is packed into both packages**, and the package-size ceilings are exact KB: re-measure and re-pin in
    the same commit as any README change (`readme-moves-package-size`).
  - **Published facts** (api.nuget.org, 2026-11-15): DwarfMapper 1.0.0-rc.1, 1.0.1-rc.1, 1.0.2-rc.1, all pushed by
    hand (no tag, no GitHub Release, no SBOM or attestation); DwarfMapper.Testing never. 1.0.2-rc.1 was packed from
    `fed98c4c` (2026-08-03): one generator in `analyzers/dotnet/cs`, 82 diagnostic ids (DWARF001-077 live, DWARFR01-09).
    Since then: 50 new ids (DWARF078-120 but 102, DWARFR10-17), unions, the two-build package.
  - **Owner decisions** (the tasks wait on these):
    - **D1** the next version: a minor bump (1.1.0-rc.1) fits unions, the two-build package and 50 new diagnostics.
      `PackageValidationBaselineVersion` stays 1.0.2-rc.1.
    - **D2** publish DwarfMapper.Testing in that release, or exclude it (`IsPackable=false`). `release.yml` packs the
      whole solution and pushes every `.nupkg`, so today the next tag publishes it with no decision recorded. Until it
      is published, `[RoundTrip]` - a headline feature, a no-op without the package - is unreachable from NuGet.
    - **D3** move `Issues/` (127 files) and `docs/superpowers/` (52) under one internal folder, e.g. `maintainers/`
      (scripts/gate-checks.ps1:75 reads `Issues/ledgers/equivalent-mutants.md`; move it along).
    - **D4** COMPARISON.md:295/302 says the dictionary lead "must not be reintroduced" while PERFORMANCE.md:224-230
      quotes it per platform - rule which stands.
    - **D5** AutoMapper 14's default depth: MIGRATION.md:146 says no default limit, the `MaxDepth` XML doc
      (src/DwarfMapper/DwarfMapperAttribute.cs:268) says it matches AutoMapper's default.
  - **Found and fixed during the review** (not a task): 22 XML-doc crefs the IDE reported as unresolved while CI stayed
    green - 18 `cref="System.X"` (the owner's hint) and 4 found by building both generator projects with documentation
    on (`42a99c5`, `ccc046d`; memory `ide-only-cref-errors`).
- **T26 release readiness.**
  - Version: bump `<Version>` (Directory.Build.props:71); version strings in README:24, :1026, :1157, :1197,
    SECURITY.md:4, docs/howto/README.md:68, docs/howto/common-changes.md:24-28 - better, drop the number from prose.
  - CHANGELOG.md: one `[Unreleased]` of 2,045 lines / 200 KB, no version sections. Add `[1.0.0-rc.1]`, `[1.0.1-rc.1]`,
    `[1.0.2-rc.1]` (dates from nuget.org); condense the rest into `[next]` (Highlights, Breaking, Security, New
    diagnostics one line each, Fixed; "DwarfMapper.Testing: first release" if D2 says so); fresh empty `[Unreleased]`.
    Wrong today: :1915-1918 "has never shipped"; :2046-2049 "No security-relevant change" (the stack-overflow fixes
    :61-72, DWARF118 :83-88, :117-120 are); "Fixed:"/"New:"/"Security:" entries under Changed (:39, :43, :56, :61, :73,
    :83); stale :2037 (pin 10.0.101), :2033 (continue-on-error removed), :11-12, :653 vs :246-252, :665 vs :462, :224,
    "1.1.0-rc5" :1148; internal noise (87 "round N", 41 task ids, commit `4190ace`, links into Issues/).
  - Release body: `release.yml:81-95/123` passes the section through `GITHUB_ENV` into `--notes "$NOTES"` - 200 KB is
    over GitHub's 125,000-character body and Linux's 128 KiB argument limit. Switch to `--notes-file` and rehearse.
  - AnalyzerReleases: Shipped.md is empty by a stable-only policy while PublicAPI.Shipped.txt marks the never-published
    Testing package shipped. Add `## Release 1.0.2-rc.1` (the 82 ids) and `## Release <next>` (the 50), reset Unshipped;
    give DWARF068/069 titles, mark DWARF102 retired.
  - docs/RELEASING.md: tag example :81-86 is the published 1.0.2-rc.1 (use `vX.Y.Z`); missing steps: version bump,
    baseline move after publishing, CHANGELOG section (the release job exits 1 without it), AnalyzerReleases, the D2
    decision; say the three hand-pushed rcs cannot be verified (and consider deprecating them on nuget.org); split the
    consumer-verification half (user material, README links it 3x) from the maintainer checklist; :67-73 task history.
  - Claims to qualify: README:921-931, :1034-1035, :1156-1157 (names tag `v1.0.2-rc.1`, which does not exist), :1199;
    SECURITY.md:134-136 ("released packages ship with an SBOM ... provenance") - true from the first tagged release on.
  - SECURITY.md:8-9 "nothing published to compare it against": state supported versions (latest nuget.org + master).
  - Help links point at `blob/master` (DiagnosticDescriptors.cs:15-16); consider tag-pinned links; DWARFR descriptors
    have none.
- **T27 README.md.**
  - Rendering (first screen on nuget.org): `**bold**` spans split across lines show as asterisks - :18-19, :30-31,
    :433-435, :568-569, :598-599, :847-848; rejoin each. 27 relative links do not resolve on nuget.org: make them
    absolute GitHub URLs (:1192 already is).
  - Stale statistics: the performance study :942-1000 (BenchmarkDotNet 0.14.0, `Job=short`, 3 iterations, one machine,
    round 29; already in docs/PERFORMANCE.md) - delete, keep three sentences on DWARF103/DWARF101 and a link; :901,
    :1070 "78 runtime assertions" (hand-counted, drifted 47-72-75-78) - drop the number; :1208 the quality-gates
    footnote - keep "badges are live CI figures", move the rest (and its Issues/ links) to CONTRIBUTING.
  - Wrong or overclaimed: :117-118, :917 "every source member must be consumed" (source side is opt-in,
    `RequiredMapping = Both`, DWARF039 Info); :85-86, :913-914 over-posting (by-name matching wires a same-named field;
    at a trust boundary `AutoMatchMembers = false`, DWARF072 - link docs/SECURITY.md); :72, :87, :915
    "`Unsafe`"/"provably-safe unsafe" (Unsafe is banned in shipped assemblies, BannedSymbols.txt:9 - say "proof-gated
    blit"); :66, :41-43 Mapperly's RMG012 is on by default; :69-74 "pulls ahead only where blittable" (also
    dictionaries and graph fills; Mapperly 17 % ahead on reference arrays on Windows) - keep it qualitative and link
    COMPARISON; :222 "once it's on nuget.org" (it is); :13, :28, :49-51, :819-822, :841, :853, :1022 the Testing package
    (D2); :1029-1032 "DevelopmentDependency" (no effect on the shipped package); :486-487 "(documented follow-up)".
  - Samples (fix the sources): :1141-1151 a stray `}` - samples/DwarfMapper.Gallery/ex15/dtos/PersonDto.cs `// </snippet>`
    after the namespace brace; :225-245, :828-839, :855-866 none shows the call - widen regions around
    Gallery/16_MapToRegistry.cs:67-71, 25_RoundTrip.cs:50, 26_InformedDumps.cs:52; :414 back the fence with
    Conformance/Features.cs:599.
  - Internal noise: :343-344 links a June design spec - point at docs/diagnostics.md#registry-diagnostics-mapto; :956,
    :966-976 round-29 raw output; :1078-1152 "Roadmap" is a changelog - delete or list what is next; :1064-1065 layout
    misses UnionBenchmarks, comparison-probes, results.
  - CONTRIBUTING.md: :34 "(round 35 T23)"; :52 the full-ci paragraph (needs triage rights; "round-closing" jargon);
    :11 an older SDK fails at SDK resolution, not NETSDK1045.
- **T28 security documents.** Root SECURITY.md and docs/SECURITY.md duplicate the CVE-2026-32933 story, the
  reflection-free claim, the report address and two claim lists; they contradict on reflection (root :21 omits the
  Testing caveat of docs :93-100), on `unsafe` (root :22-24, :152-153 vs root :42), and on release SBOMs (root
  :134-136). Make the root a policy page (versions, reporting, scope, response times); move "Trust model" and
  "Auditing" into docs/SECURITY.md's register with the scan that checks "Held by" names (ClaimMechanismScanTests).
  docs/SECURITY.md:19 lists one suppressed advisory of three (also GHSA-6vhm-j9h7-f9gh, GHSA-gpwp-5797-mjff) and :7-8
  contradicts :19; :28-31 the AutoValidate story is obsolete. Link `.oss-scanner/threat_model.md`. Keep :73-78 (the
  endpoint-parity notice for the published rcs).
- **T29 docs/diagnostics.md.** Accurate: all 115 ids, titles and severities (DiagnosticProseIsCurrentTests); the 17
  DWARFR rows match RegistryDiagnostics.cs but nothing guards that table. **Rule for every entry:** present tense - what
  triggers it, why this severity, the fix, when it stays quiet; a before/after note only if it crossed a published
  version, written as a version, one sentence; never round/task/finding ids, paths into Issues/ or docs/superpowers/,
  test or classifier vocabulary, internal type names, benchmark figures, or anecdotes from the maintainer's
  migrations. Apply to the DWARFR table too.
  - :1488-1495, :1672-1681 the matrix column "What the matrix measures"; :1491 labels the recommended remedy
    "Refused" and links B19 - call the column "Result", write "applied; also reports DWARF038".
  - Labels: :68, :418, :586, :632, :679, :687, :702, :1971, :2172; "finding D1-D14" :1440, :1444, :1458, :1473, :1578,
    :1583, :1602, :1622. Ledger links :1141, :1491, :2052, :2133.
  - Figures: DWARF101 :2052-2056, DWARF103 :2132-2143, :2150 - one sentence on the mechanism plus a PERFORMANCE link.
  - Anecdotes :68-71, :989, :1034-1036, :1077-1081, :1146-1147, :1153, :1192-1193, :1235, :2014, :2437, :2403-2405;
    "used to" history in ~20 places incl. the "What used to happen" table :1533-1539, :698, :1287-1291, :1326, :1405,
    :1760, :1913, :1941, :2606, :2654, and 9 DWARFR rows (R02: "a project that built yesterday ...").
  - "Until round 35 ..." :847-848, :2723-2726, :2737-2738, :2770-2772, :2778-2780 - "Before <version>" or delete.
  - Internal names :2159 `CanReinterpret`, :2182 `TransferModelShape`, :2154-2155, :107-109.
  - Contradiction :578 DWARF065 "Collections are always rebuilt" vs DWARF074 keyed merge (`[MapCollectionKey]`); the
    same omission in migrate-from-automapper.md:318, migrate-from-mapster.md:133-134, migrate-from-handwritten.md:139-141.
  - Rendering: :1961-1964 and :2016-2023 a paragraph directly above `---` renders as a heading - blank line before it.
  - Polish: :4-9 three pages then "Both"; 084/085 before 082/083; DWARFR table order and severity on 6 of 17 rows;
    `---` separators stop after DWARF108; :11 says "each build error" (help links exist for DWARF### only).
- **T30 how-to guides.**
  - migrate-from-mapperly.md:104-105 "~2x on widening" - the current run says 1.2-1.3x (blit 2.1-2.3x holds).
  - Testing package (D2): common-changes.md:16-22, :173-174; migrate-from-automapper.md:299; -mapperly.md:92, :102;
    -mapster.md:119; -handwritten.md:125 - lead with the old-vs-new equivalence test until it is published.
  - Packaging told once: diagnostics.md:947-949 (DWARF078) and migrate-from-automapper.md:71 contradict
    common-changes.md:24 ("a single reference") - "each project that declares mappers needs its own PackageReference".
    common-changes.md:32-42 the from-source recipe misses `InterceptorsNamespaces;DwarfMapper.Generated` and
    `CompilerVisibleProperty PublishAot` (or: import src/DwarfMapper/build/DwarfMapper.props), wires only the 5.0 build.
  - common-changes.md:189-201 "verify ad-hoc" shows no verify call - widen region `informed-dumps`
    (Gallery/26_InformedDumps.cs) to lines 50-63 (README:855 has the same gap).
  - docs/howto/README.md:2-4, :18 "three questions" then four; ambient-cross-assembly-maps.md is not listed.
  - common-changes.md:162-163 "full catalog in MIGRATION.md" - link diagnostics.md; :137-138 "no severity override"
    vs diagnostics.md:24.
  - deploy-and-optimize.md:25 "allocation zero" (Preserve allocates one dictionary per call); :23, :80
    DevelopmentDependency; :125 "2.3-3.3x" has no source; :66 "rejected by ILC 10.0.1" - re-verify or date.
  - Small: automapper.md:37-38, :49, :347 "a real ~300-map migration"; :255 README link without anchor; mapster.md:130
    "silently dropped" (DWARF071/033 report); handwritten.md:72 `nameof(Order.PasswordHash)` should be `OrderDto`;
    CI job names in README:47, common-changes.md:45, deploy-and-optimize.md:74-76; ambient-cross-assembly-maps.md:1
    lacks the SPDX header, :74-75 "as this repository's tests do".
  - Duplication: the guides' tables restate MIGRATION.md - keep steps in the guides, link MIGRATION for the rows.
- **T31 core docs.**
  - MIGRATION.md:139 and migrate-from-automapper.md:44, :262 advise `EnumStrategy = ByValue` "for AutoMapper's
    default": AutoMapper 14 maps defined members BY NAME (EnumOrderSensitivityTests.AutoMapper_maps_by_name_as_well) -
    the advice introduces the reorder bug it claims to prevent. Delete. :44-46 add CVE-2026-128081/-128082 (not stopped
    by MaxDepth). :2 "methodics" -> "guide"; :263 vs COMPARISON:52.
  - COMPARISON.md: :73, :104 "Mapperly reaches the same conclusion by default" - measured only under
    `[MapEnum(ByName)]`; its default is a cast. :114-116 Mapster is in the harness since 2026-08-26. :54 AutoMapper's
    depth cap is opt-in. :17-29 "left out at the owner's direction" - "AutoMapper 14.0.0 is not re-measured: the last
    MIT release, unchanged, three open advisories". :308-435 rebuild the performance section around the 2026-11-15
    table (add Flatten, Enum by name, List, Widen), delete the old table :318-335, keep three reading notes rewritten
    (blit size curve, the reference-array null-guard trade, enum strategies), link the August sweep for AutoMapper
    figures only; session language :287, :367-368, :372, :398, :412, :450, :489, :530; :315 "none run in CI" (allocations
    are gated nightly); :22 the union table was measured 2026-11-14 (date it); :498-532 the AOT block (ILC 10.0.1) -
    date it and drop the ISA claim, or re-run; :188-189 vs :183; :175-183 competitor cells nothing here verifies;
    :191-245 repeats MIGRATION §0.
  - PERFORMANCE.md: :108-127 views were built and WITHDRAWN on 2026-09-07 (a view of a mutable source can return a
    record that never existed) - say so; the file is a struct-DTO study under the name an evaluator opens: fold §2a and
    §5 into diagnostics.md#dwarf103, §6 into COMPARISON, move the rest to docs/research/ (update DocSet.cs, README:957,
    :972, :996, diagnostics.md:2056, :2141).
  - CORRECTNESS.md:137 points at IMPROVEMENT-PLAN.md; :121 "before round 35".
  - Delete or move as dated history: docs/IMPROVEMENT-PLAN.md (plans DWARF059-062 that now mean other things; :16, :130
    contradict diagnostics.md:25-28) - drop its SuppressionAdviceTests.cs:32-33 exemption; docs/research/* (June
    numbers; RESEARCH.md:27 **exposes a home-directory path**; `_raw-harvest.jsonl` missing) - fix the
    DocPipelinePropertyTests.cs:11 comment.
  - Generated docs (fix the sources): `ApiReferenceRenderer` drops `<typeparamref>`, generic arity and `<code>`, and
    lists members inherited from System.Attribute/System.Exception (api-reference :167, :254, :295, :301, :305, :320,
    :347, :443, :550, :560); the `GenerateMapAttribute` XML doc should name DWARF060; `RenderDiagnosticsIndex` leaves out
    the 17 DWARFR ids and should say "default severity"; `RenderOptionMatrix`'s legend conflates refusal with intended
    diagnostics (AutoNest-005, ImplicitConversions-038, AutoMatchMembers-072, RequiredMapping-039).
  - options.md: :4 "every knob" omits [MapTo], [ProvidesMap], [UsesMap], [DwarfMapperValidationRoot], MapConfig; link
    the generated reference and matrix; :86 "v1:".
- **T32 samples, benchmarks, contributor notes.**
  - Gallery: 26_InformedDumps.cs:11 `ObjectFactory` -> `ObjectFactoryV2`; 21_BlittableSimd.cs:3 "the one place
    DwarfMapper is faster" -> "one of the places"; 06_DeepPaths.cs:9 DWARF044 is a Warning; 27_PatchMerge.cs:43-44
    indentation; 25_RoundTrip.cs comment `(seed, count)` -> `(seed, iterations)`. README.md:189 broken text, :734 #51's
    heading disagrees with the generated index (:46), :13-15 round-27 noise: render the section headings from the
    example catalogue (`GalleryIndexRenderer`) so they cannot drift. DocSet.cs lists the Gallery README twice (:19,
    :37).
  - Add `samples/README.md`: AotSample (the AOT gate), AotBench (built, never run), Conformance ("every feature" is no
    longer true: no [MapShare], [MapClone], Dwarf.Map, [MapDenseEnumKeys], unions), Gallery, Unions.
  - Benchmarks: comparison-probes/README.md:18, security/run.sh:5, validation/run.sh:5 name sdk:10.0.101 (the pin is
    10.0.404, rollForward disable); Benchmarks/README.md:57 and DifferentialTests/README.md:110 Mapster 10.0.8 ->
    10.0.14; Benchmarks/README.md:45-47 "CI does not run benchmarks" (nightly smoke gates allocations); :7-20 the
    scenario table misses eight categories and the other classes; :68-69 three suppressed advisories, not one;
    `Round31*.cs`, `Round33*.cs`, `Round35SpanWidenBenchmarks.cs` and "round 35 T24/T25" in READMEs - name by subject;
    `comparison-probes/linkcheck.py` is a repo-wide link checker - move to scripts/ (and make it the T25 link test).
  - conformance/results: only the newest file is read (scripts/conformance-gate.sh:76, floor 78) - keep one floor (or
    the number in the script), drop the dated copies, add a README.
  - tests notes: CorpusTests/CORPUS.md:50-51 (`SkipNullSourceMembers` exists now), :17 date the survey;
    ConsumerTests/README.md:36-43, :51 (23 assertions, CleanCorpus missing), :70; CleanCorpus/PATTERN-PROVENANCE.md:30
    "this session"; CONVERSION-NOTES.md:25 dangling "see below", numbering 4 -> 7 (:115).
- **T33 repository layout** (D3). User docs linking into internal trees: Issues/ - README:956, :1208;
  PERFORMANCE.md:20-24, :166, :240; diagnostics.md:1141, :1491, :2052, :2133; RELEASING.md:70; CHANGELOG :538, :614,
  :682, :1913, :2057. docs/superpowers/ - README:344, IMPROVEMENT-PLAN.md:63. benchmarks/results/ - COMPARISON.md:21,
  :124, :301, :388, :415, :422, :449; UnionBenchmarks README:41; probes README:7; CHANGELOG :54, :172. Keep
  benchmarks/results/ (the evidence) with an index marking current vs superseded, subject file names, and the four
  process measurements (test-host memory, test start-up, mutation legs, the fusedchat build share - **names the owner's
  private app**) moved to the internal folder. `.oss-scanner/` stays (the scanner reads that path,
  OssScannerImageTests). Also visible at the root: tracked `.claude/hooks`, `.mcp.json`, six `stryker-config*.json`.
