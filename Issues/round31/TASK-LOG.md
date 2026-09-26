# Round 31 — task log

One entry per executed task: what was done, what the evidence was, and every place the task list's
instructions were **not** followed literally, with the reason. A task list is a plan written before the tree was
read; where the tree disagreed, the disagreement is recorded here rather than silently resolved either way.

## T01 — advisory-scoped audit suppression · `e9e09d4`

Done as specified. `AuditSuppressionScanTests` lists exactly the three `.csproj` that may carry a
`NuGetAuditSuppress`, and the suppression is by advisory URL rather than by severity, so a *new* High advisory
in a benchmark-only dependency is not silently pre-approved.

## T02 — projection null guards must not call user operators · `0938e21`

Done, and it found a defect the task did not predict. The specified test (a user-defined `==` being called by a
generated null guard) was correctly RED. Writing its sibling turned up a second, worse case: a nested type with
**two** equality operators made the generator emit `CS0034 Operator '==' is ambiguous` — generated code that
does not compile, with no DwarfMapper diagnostic to explain it. Fixed by casting the operand to `object` at all
four emission sites.

**Deviation:** my own delta note had claimed *five* sites. Four is right; the fifth grep hit is a comment. The
commit says so.

## T03 — AutoMapper CVE-2026-32933 proof of concept · `ca831d8`

Done as specified, green as expected (a guard, not a fix): six graph shapes — self-reference, collection-routed
and dictionary-routed, each in `None` and `Preserve` mode — at the advisory's own 30,000 levels, on a 1 MB-stack
thread, all ending in a catchable `DwarfMappingDepthException` rather than a dead process.

## T04 — strengthen two `LocationInfo` tests

Done as specified; `BuildLocation`'s signature matched the task's STOP condition, so no stop. Both tests moved
from `Assert.NotNull` to pinning every field, including the line position — the one value `From()` computes
rather than copies. Sabotage check pending with the T04–T07 commit.

## T05 — duplicated coverage sources

Three pairs, three decisions, each by the task's own rule:

| pair | decision | why |
|---|---|---|
| `ResolveMembersDirectiveCoverageTests` ↔ `IgnoreConflictDirectiveNameTests` | **deleted** the coverage-file test | Rule 1. The other home drives the identical source as its `Share` row and asserts strictly more: same single `DWARF012` naming `'Items'`, **plus** that the message names the directive written. A subset test is maintenance cost with no extra evidence. |
| `PairScopedIgnoreReaderCoverageTests` ↔ `UnscopedIgnoreNoMatchTests` | **kept both**, marked | Rule 2. Only the two type declarations are shared. One file asserts what a pair-scoped `[MapIgnore<T>]` with a MISSPELLED member reports (`DWARF095`); the other, what one with NO member name does (nothing — skipped). Different directive, different outcome; neither subsumes the other. |
| `AssemblyNameNamespaceCoverageTests` ↔ six framework/golden tests | **kept both**, marked | Rule 2, and the audit's own table already said so. Nothing there varies the source — the point is to hold it fixed and vary the ASSEMBLY NAME, so a difference in the generated namespace can only have come from the name. Making the literal textually unique would weaken that argument. |

## T06 — the byte-identity lock covers the refactor

`Golden/output-manifest.txt` line 1: **1,014 cases**, above the task's ≥ 1,000 floor. `GoldenCorpusTests` green
without `DWARF_GOLDEN_UPDATE` on the untouched tree.

## T07 — parameter-ceiling ratchet

**Deviation, and it matters.** The task supplies a `(?:private|internal) static` regex to generate the legacy
allowance table. Two problems:

1. It reports **40** rows on this tree, not the research's 47 — the task's own STOP threshold is "more than a
   few", so this is reported rather than assumed away. The likely causes are that the regex sees only
   `private`/`internal` `static` methods and that `[^)]*` mis-parses any signature whose parameter list contains
   a closing parenthesis.
2. More seriously, the table it produces **does not cover what the task's test checks.** The test walks every
   `MethodDeclarationSyntax` — public and instance methods included. A table built from the narrower measurement
   would leave the ratchet RED on an untouched tree, which is the one thing a ratchet must never be.

So the table is generated from the same Roslyn walk the assertion uses, and the test says so at the point where
a reader would otherwise reach for the regex. It also uses `RepoPaths.PipelineDir` / `RepoPaths.SourceFiles`
instead of a raw `Directory.EnumerateFiles`, so `obj/` and `bin/` copies cannot inflate the table.

## T09 — registry collection registrations · `0af2f2e`

**Deviation, measured before adopting it.** The task specified inlining the pre-size and concrete fast paths
into each emitted registration. Measured first: the golden snapshot corpus grew **70 %** (113 KB → 193 KB),
because the shape is ~20 lines and there are six registrations per mapped pair — 114 extra lines of generated
source for a *single* pair, and a 500-pair application would carry tens of thousands. One generic runtime helper
(`DwarfCollectionMap`) gives the identical machine code, because these are ordinary generics the JIT specialises
per type argument, while the emitted registration stays one line and is testable once rather than six times per
pair by inspecting strings.

## T12 — exact-pair slot for the generic facade · `ed69922`

Done to the spec, including the ordering the OPUS-REVIEW gate names. Three things the spec did not say:

- **`RegisterMany` does not exist yet** (it is T11's deliverable), so there was nothing to bump. The obligation
  is recorded in `_version`'s remarks instead, including that a batch may bump once for the whole batch.
- **`TryGetUpdate` is `internal`**, and the registry's asymmetry note now records why. That note's ruling was
  about public surface — the update delegate is meaningful only applied to a destination the caller already
  holds — and the sole caller is the facade's update overload, which holds it.
- **The update overload's null guards moved ahead of the delegate**, so a slot hit cannot turn an
  `ArgumentNullException` into an `NullReferenceException` raised from inside generated code.

**The task's first test was not actually red, and that is worth recording.** As specified,
`An_exact_pair_registered_after_first_use_is_picked_up` passes a `TSource` instance — and it then passes against
a slot with no invalidation at all, because the stale "absent" answer falls through to the runtime-type
fallback, which resolves the very registration the slot missed. The two paths agree by accident. Only a source
whose runtime type resolves ELSEWHERE separates them. Proven by planting the broken slot: 2 of 4 fail, and both
recover on the real one.

**The torture test asserted more than the design provides.** The spec asks for "per reader, never goes from
'exact' back to 'base'". That is not guaranteed: the version is bumped AFTER the table write, so a reader that
looked before the write can store its "absent" answer over a reader that already found the map, and the second
reader legitimately falls back again. Every answer in that window is one the registry gives. The assertion was
removed rather than the slot made monotonic — that would cost a compare-and-swap loop for a property no caller
needs — and both the test and `ExactPairSlot`'s remarks now state the limit. A flaky test is how a mutation run
manufactures phantom kills, which is the contamination that cost round 30 a re-pin.

**Mutation scope grew with this task, deliberately.** `ExactPairSlot.cs` and T09's `DwarfCollectionMap.cs` were
both outside every `mutate` glob — new *shipped runtime* code the leg did not touch. Both are now in
`stryker-config.runtime.json` (`Issues/round27/AUDIT-mutation-scope.md`: runtime share 25.9 % → 32.9 %, files in
a leg 6 → 8). A larger denominator of never-mutated code can only push the score down first; the answer is to
kill the survivors, not to lower `break`.

**One ledger row retired, in the good direction.** The facade's `TryGet(...) && map is not null` was
proven-equivalent because the operands co-vary — the null test was flow analysis, not a branch. It is now the
branch the whole design turns on, and `ExactPairSlotTests` drives both arms. runtime `provenEquivalent` 3 → 2,
rows 62 → 61, mutants 65 → 64.

## T19 — benchmarks · `d177479` and the round-31 matrix

Beyond the T09 A/B in `d177479`, round 31 adds:

- **`AmbientFacadeBenchmarks`** — the facade path, which nothing in `MapperBenchmarks` had ever touched, against
  Mapster and AutoMapper (Mapperly has no runtime-dispatch form, so it has no arm and that is a fact about
  Mapperly rather than an omission).
- **`CollectionReadProbeBenchmarks`** — two questions the comparison run raised, measured before either becomes
  an emission change: how a `List<T>` source is READ for reference elements, and what the emitted null guards
  cost relative to Mapperly's guard-free loop.

**Methodology defect found and fixed in my own benchmark.** The first version of the facade A/B called
`DwarfMapperRegistry.TryGet` straight from the benchmark method for the "before" arm, which skips the
`IDwarfMapper` dispatch the real call pays. That arm was measuring a strictly cheaper call shape, and the change
looked worth about a nanosecond. Both arms now enter through an interface-typed field
(`LegacyAmbientFacade` holds the pre-T12 bodies verbatim), and the measured win is 1.25–1.66x.

Results: `benchmarks/results/2026-09-26-round31-full-matrix.md`.

## T09 (second pass) — the span read removed · `4a6821f`

A defect I introduced in T09 earlier the same day, found by writing a benchmark for a different question.
`DwarfCollectionMap.ToList` read a `List` source through `CollectionsMarshal.AsSpan` and called the element map
inside that loop; the element map is a generated mapper, so it can run a user hook or converter that mutates the
source list, and the span then keeps reading the old backing array. A loud `InvalidOperationException` had become
a silently stale result — the same trade `b25ae56` refused on the element null-ternary.

The safety was **free**: priced with a job able to resolve it, the version-checked walk is faster at every element
count (0.83x / 0.74x / 0.94x) and allocates the same bytes, because the whole allocation win is the pre-size.

**And the benchmark that said otherwise was mine.** `RegistryCollectionBenchmarks` shipped with
`invocationCount: 16, iterationCount: 5` — fine for the 0.45x-vs-1.00x gap it was built for, and actively
misleading on a finer question: it reported the span at 31,549 ns against 42,780 at N = 1,024, the opposite
ordering, with 99.9 % intervals wider than the difference. A config that can flip a sign is worse than no config.
Job now MediumRun's shape. Allocation was never affected, which is why T09's actual claims (the pre-size, and the
70 % golden-corpus growth that made a helper preferable to inlining) stand while its time column did not.

## T31 — architecture tests for "the runtime does not re-decide" · `0729198`

Not in the task list; added on the owner's ruling that the principle must hold at every public API and be
tested. Six tests: a generated mapper body never names the registry (7 feature fixtures); only declared methods
resolve through it; only declared mutable static state exists (this is what catches a cache); only declared
runtime type tests exist, counted per type; **every public type declares how it behaves at run time**, driven off
`PublicAPI.Shipped.txt`/`Unshipped.txt` so new API cannot ship unclassified (48 types, four buckets); and no
public type outside the ambient entry point resolves at run time.

Sabotage-checked on both sides. Detection side: swapping `Run` for `RunAll` fails 6 of 7 rows of test 1, because
`RunAll` includes the registration aggregate. Pin side: removing `DwarfMapperFacade` from the resolution list
fails three tests, deleting a mutable-static row fails one, a wrong type-test count fails one.

Two honest limits recorded in the file: the `span` row of test 1 is insensitive to its sabotage (a span map emits
no ambient registration, so there is nothing to reveal — the row still guards the property), and the first
type-test detector was wrong, flagging `candidates is { Count: > 1 }` as a type test. It now counts only patterns
that name a type.

## T12 (second pass) — the version machinery deleted · `c65c4fe`

The slot's registration version counter, entry object, paired volatile writes, two `Interlocked.Increment` calls
and written-down ordering argument all existed to invalidate a cached MISS. Registration is add-only and
first-wins, so a resolved delegate is immutable for the process; not caching the miss deletes all of it, and a
late registration is still picked up because the next call re-asks.

The simplification bought a **stronger** guarantee than the machinery it replaced: `_map` goes from null to one
value and never back, so the per-reader non-monotonicity the first version had to disclaim does not exist, and
`ExactPairSlotTests` now asserts it. The unreachable `return;` in the update overload — the leg's only NoCoverage
mutant — went with it.

T31's mutable-static test **demanded** the pin change rather than allowing it: it failed until
`DwarfMapperRegistry._version` was struck and `ExactPairSlot._entry` became `._map`. First real use of the
ratchet, working in the intended direction.

Re-measured: facade 1.51x / 1.24x / 1.79x against the pre-T12 shape, within one run. Recorded with the caveat
that the unchanged `_Before` arms moved 18.17 → 21.19 ns between runs, so cross-run ratio comparisons are noise.

## T26 — compile-time binding: proposal, not a plan

`Issues/round31/PROPOSAL-T26-compile-time-binding.md`. Two findings that cut against the idea as first stated:
C# interceptors are **unsound** for an interface-typed receiver (they would silently shadow a consumer's own
`IDwarfMapper` decorator or test double — a behaviour change in someone else's DI graph), so a static non-virtual
entry point is the shape that could be intercepted safely; and whether `InterceptsLocation` is usable on
`net10.0` without a preview gate is **unverified**, named with three concrete checks.

## Owner rulings this round, recorded because they outlive the tasks

1. **What the compiler decided, the runtime does not re-decide.** Get rid of lookups and caches; resolve at
   generation time; keep runtime manipulation minimal. Applies at every public API, and is now tested (T31).
2. **Trust the generator-level tests at the compilation stage** — they cannot be changed after build, and the
   programmer is compile-time warned when a linkage or type is broken, so emitting a runtime type test to
   re-check it is duplicated work.

## Not executed

T08 (contradicts the three-bundle ruling in `ae9c7ea` — needs an owner decision), T10/T13 (projection hoisting
and its in-memory route; T10's stop condition fires because the lambda is written across many `sb` sites), T11,
T14 (needs an Opus spec first), T15–T18, T20 (touches `.github/workflows`, which this session's token cannot
push), T21–T30. `round31-audit.sh` needs `python3` on PATH; `python` is what resolves on this machine.
