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
| T4 | **DWARF066 false positive** on a When-guarded member inherited from a positional record. | open |
| T5 | **Audit `docs/diagnostics.md`** (2,700 lines) against the tree. | **done** - see Notes |
| T6 | **Widen span maps** (`void Map(ReadOnlySpan<int>, Span<long>)`): measure the scalar loop against a vectorised one; vectorise if it pays. | open |
| T7 | **Move the SDK pin** to the current 10.0 servicing release; re-measure every gate tied to it; drop the AOT compiler override if the new pin makes it dead. | open |
| T8 | **Ubuntu 26.04 for every Linux job**, the canary removed, on a `full-ci` run. | open |
| T9 | **Test and tooling packages** brought up to date, one at a time, gates re-measured. | open |
| T10 | **The in-process mutation runner** into the repository: tool project, tests, catalogue step, a CI job comparing its report with Stryker's. | open |
| T11 | **The three Stryker findings filed upstream.** The owner's to send; the write-up is ready. | owner |

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
  not known to be bypassed: `[FlattenGraph]` and `[MapDerivedType]` on such a method (the second has its own
  dispatch helper) - to be measured as a follow-up.

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
  It is not new in .NET 11; the job that happened to catch it runs the suite there. The two tests that assert an
  exact zero over one window now read five and require a zero among them: a map that allocates does so in every
  window. The tests with a per-call bound are not exposed (8 KB over their iteration counts is under their slack).
  IntegrationTests keeps concurrent collection on, as it keeps every runtime default: it measures what a consumer
  runs.
