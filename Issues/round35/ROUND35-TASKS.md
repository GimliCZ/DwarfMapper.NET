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
  - **For the owner:** the branch needs SDK 10.0.404 installed to build on this machine (`global.json` refuses any
    other, by design). I could not install it here (no administrator rights), so every check of this branch ran
    in containers.
