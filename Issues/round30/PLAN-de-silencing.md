# Round 30 — de-silencing, and the scan that keeps it de-silenced

**Goal:** every remaining silence in the repository is either removed or carries a reason a reader can check, and
a self-validation scan fails the build when a new one appears without one. Inventory:
`Issues/round30/DE-SILENCING-INVENTORY.md` (taken 2026-09-03).

**Why this shape.** The repo already enforces this discipline in one place — `RatchetInvariantScanTests` pins the
single `// Stryker disable all` to exactly one site and requires its disable/restore pairing — and that pin is
why nobody has quietly added a second one. Everything in the inventory that has no reason today is a place where
the same discipline was never mechanised. A rule nobody can violate silently beats a cleanup nobody repeats.

**Ordering rule.** The scan lands LAST in each group: first make the tree satisfy the rule, then make the rule
enforceable. A scan added first would be a red build that invites a blanket exemption list, which is the same
defect wearing a different hat.

---

### Task 30.1: shipped code carries no unexplained silence

`src/DwarfMapper.Testing/DwarfMapper.Testing.csproj:10` — `<NoWarn>CA5394;CA1510;CA1032</NoWarn>`, no reason.
This assembly ships. Fix each id rather than silence it:
- **CA5394** (insecure randomness): the testing package generates fixture data. Either state the non-security use
  in a per-id comment (the `tests/DwarfMapper.IntegrationTests` csproj is the template: one comment per id) or,
  better, take the id off the list and put a reasoned `#pragma`/`[SuppressMessage]` at the one or two call sites
  so the silence is bounded by the code it excuses.
- **CA1510** (`ArgumentNullException.ThrowIfNull`): fixable, not silenceable — apply it.
- **CA1032** (standard exception constructors): add the missing constructors, or state why the exception type is
  deliberately narrow.
Verify: `dotnet build DwarfMapper.NET.sln -c Release` clean with the ids removed from `NoWarn`; the package's
public surface unchanged (`PublicAPI.Unshipped.txt` untouched unless CA1032 adds constructors — then it moves and
that is a deliberate, reviewed line).

### Task 30.2: every `<NoWarn>` id has a reason beside it

Thirteen project files list ids with no adjacent comment (see the inventory's Priority 2 list; the template is
`tests/DwarfMapper.IntegrationTests/DwarfMapper.IntegrationTests.csproj:6-24`, one comment per id). For each id,
in order of preference: (1) delete it and fix the warning; (2) keep it with a one-line reason naming the shape
that earns it. Two special cases:
- `samples/DwarfMapper.AotSample/…csproj:24` silences **DWARF038 and DWARF044** — the product's own diagnostics.
  A sample that hides the mapper's advice teaches the reader to hide it. Fix the sample's mappings so the
  diagnostics do not fire, or, if the shape is deliberate, keep the diagnostic and add the sample comment
  explaining why the advice is declined there.
- `samples/DwarfMapper.Conformance` and `samples/DwarfMapper.Gallery` downgrade `AnalysisMode` to `Recommended`.
  Restore `All` and reason (or fix) whatever it surfaces; the gallery is the synthetic project the mandate wants
  exercising the engine, so it should be held to the same analyzer bar as the product.

### Task 30.3: every hand-written `#pragma warning disable` has a reason line

Four sites have none: `tests/DwarfMapper.IntegrationTests/UserConversionRuntimeTests.cs:3` (five ids, no comment
anywhere in the file), `tests/DwarfMapper.Generator.Tests/FeatureCombinationInvalidTests.cs:9`,
`tests/DwarfMapper.IntegrationTests/SetNullCollectionCycleRuntimeTests.cs:6`, and
`tests/DwarfMapper.Generator.Tests/RegistryDiagnosticsGenTests.cs:458,476` (inside test-source strings — the
reason belongs in the surrounding test). Repo rule: a suppression is a reason line followed by the bare directive.

### Task 30.4: the scan that keeps it that way

Extend `tests/DwarfMapper.Generator.Tests/SelfValidation/RatchetInvariantScanTests.cs` (it already owns the
Stryker-disable pin) with two checks over the repository text:
1. **`<NoWarn>` needs a reason.** For every project file, every id in every `<NoWarn>` must appear in an XML
   comment within the N lines preceding the element (or on the same line). Failure message names the file, the
   id, and the template to copy. Deliberate exemptions live in ONE list in the test with a stated reason each —
   and the list is a ratchet: shrink-only, like `DeclaredDivergences`.
2. **A hand-written `#pragma warning disable` needs a reason line.** Every `#pragma warning disable` in `src/`,
   `tests/`, `samples/`, `benchmarks/` must have a `//` comment on the directive's line or the line above.
   Generated code is exempt by construction (the scan reads source files, not `.g.cs`), and the ONE pragma the
   generator emits is pinned separately by `EnumConverter`'s tests.
Both checks need a **positive control** — a fixture string proving the check fails when the reason is missing —
per the repo's "prove the gate can fail" rule.

### Task 30.5: the deliberately-silent generator condition

`BlittableProof.TryExplainNearMiss` stays silent when field counts or field types differ. Round 25 chose that
narrowness so DWARF100 stays rare; round 29 T0.1 already carved out the one-sided `Nullable<T>` case. Under the
standing mandate ("tell the user at the exact location what is wrong"), re-examine one shape: **names line up,
types differ, and the differing pair is a widening primitive** (`int` vs `long` on the same member name) — a
consumer's most likely honest mistake, currently silent. Measure the noise first: run the near-miss over the
whole golden corpus and the gallery, count how many pairs would newly report. Ship only if the count is small
and every new report is a genuine mistake; otherwise record the measurement and keep the silence, with the
number in the doc comment so the next reader does not re-litigate it.

### Task 30.6: ReSharper naming headers

Twenty-five test files carry an identical `// ReSharper disable InconsistentNaming` header. Replace with one
`.editorconfig`/`.DotSettings` naming rule for test methods (Given_When_Then), so the exemption lives in one
reasoned place. Rider-only, no build effect — but 25 copies of anything is a DRY failure the mandate names.

---

**Gate for the round:** `pwsh scripts/housekeeping.ps1 -Deep -Coverage -ILVerify -BenchSmoke` passes; the new
scans fail on a seeded violation and pass on the tree; no ceiling or floor moves without a re-measure in the
same commit; the inventory document is updated to show what remains and why.
