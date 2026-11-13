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
| T6 | **Widen span maps** (`void Map(ReadOnlySpan<int>, Span<long>)`): measure the scalar loop against a vectorised one; vectorise if it pays. | open |
| T7 | **Move the SDK pin** to the current 10.0 servicing release; re-measure every gate tied to it; drop the AOT compiler override if the new pin makes it dead. | open |
| T8 | **Ubuntu 26.04 for every Linux job**, the canary removed, on a `full-ci` run. | open |
| T9 | **Test and tooling packages** brought up to date, one at a time, gates re-measured. | open |
| T10 | **The in-process mutation runner** into the repository: tool project, tests, catalogue step, a CI job comparing its report with Stryker's. | open |
| T11 | **The three Stryker findings filed upstream.** The owner's to send; the write-up is ready. | owner |
| T12 | **A `[MapDerivedType]` pair used as a list or array element calls the public method with no context** (abstract base, every mode): a cycle through the collection is a stack overflow, and Preserve loses identity between elements. | **done** - see Notes |
| T13 | **A `[MapDerivedType]` pair with a concrete base, nested under Preserve or SetNull, skips the dispatch**: a derived instance is mapped as the base, its own members dropped, with DWARF071 (Info) advising the attribute that is already there. | **done** - see Notes |
| T14 | **SetNull, a concrete-base `[MapDerivedType]` pair and a cycle through an arm: the generated code does not compile** (CS7036, an arm's helper calls a context-taking helper without one). | **done** - see Notes |
| T15 | **A `[FlattenGraph]` method's pair nested under Preserve or SetNull is refused as DWARF001**, whose advice (`[MapIgnore("Nodes")]`) would empty the flattened list. | **done** - see Notes |
| T16 | **A declared mapping method with an enum or scalar destination returns the default for every input** (`return new StatusDto { };`), where `[GenerateMap]` of the same pair converts. | **done** - see Notes |
| T17 | **A cycle that runs through a `[FlattenGraph]` method's own node leaf is a stack overflow** in every mode (a node pointing at the object that holds the graph). | **done** - refused as DWARF118, see Notes |

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
