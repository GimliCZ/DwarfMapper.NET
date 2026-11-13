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
| T2 | **The last untested audit cell:** an array blit under SetNull against a method-level config on the element pair's partial. | **defect found - decision open**, see Notes |
| T3 | **The stale XML doc** on `ImplicitConversions` ("suggestion (Info)"). | **done** (`93d8b01`) |
| T4 | **DWARF066 false positive** on a When-guarded member inherited from a positional record. | open |
| T5 | **Audit `docs/diagnostics.md`** (2,700 lines) against the tree. | open |
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
  **Open decision:** (A) the helper replicates the method's directives, as it already does the pair's hooks and
  factory - the right behaviour, and a change in the middle of member resolution; or (B) refuse: an error when a
  declared partial with method-level directives would be bypassed this way, naming the pair-scoped form
  (`[MapIgnore<D>]`, `[MapProperty<C,D>]`) that the helper does honour. Recommended: B first - it stops the silent
  wrong output at once and is small - then A.
