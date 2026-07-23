# Design: generator testing framework — refactoring safety net + reusable kit

- **Date:** 2026-07-22
- **Status:** **IMPLEMENTED and MERGED** (status corrected 2026-07-23) — `4e79370` on `master`. Delivered: a
  971-case golden manifest, the cacheability battery over both generators, 21 corpus-derived snapshots,
  framework self-tests, and four ratchets. Suite 5,561 green, 0 warnings.
  Post-merge correction: §4.3's claim that the feature axis is *derived* from the gated taxonomies was
  amended during implementation — it is a **curated** list guarded by a taxonomy-growth ratchet. See §4.3.
- **Component:** `DwarfMapper.Generator.Tests` (test infrastructure) + minimal non-behavioural additions to `DwarfMapper.Generator`

## 1. Problem

This is **sub-project 1 of four** in a maintainability programme for the two Roslyn generators
(`DwarfGenerator`, `MapToGenerator`). The agreed order is:

1. **generator testing framework** ← this spec
2. shared engine core (end the two-engine drift)
3. emission layer (indented writer)
4. split `MapperExtractor.cs` (7,265 lines)

Items 2–4 are large, behaviour-preserving restructurings of code that has no way to *prove* behaviour was
preserved. Three concrete gaps make that unsafe today:

**No output-invariance net.** Nothing asserts that the generator emits the same code before and after a
refactor. Snapshot coverage exists but is a handful of curated cases. The emission rewrite (item 3) and the
extractor split (item 4) are precisely the changes that could silently alter output.

**Cacheability is half-tested and half-testable.** `DwarfGenerator` has six incremental-caching tests, but only
2 of its ~6 registered source outputs are named with `WithTrackingName`. `MapToGenerator` has **no** tracking
names at all, so its pipeline cannot be addressed by a test even in principle — it has zero cacheability
coverage. Its model could stop being value-equatable and nothing would notice. Per the Roslyn cookbook, a
non-equatable model (or a leaked `ISymbol`/`Compilation`) silently disables incrementality and roots old
compilations; neither generator is tested for that leak.

**The harness is generator-coupled.** `CSharpGeneratorDriver.Create(new DwarfGenerator())` is hardcoded at six
sites. Adding the registry generator already required a bespoke `RunMapTo` method rather than passing a
generator in. A third generator would repeat that.

## 2. Goals / Non-goals

**Goals**

- A **safety net**: prove items 2–4 change nothing observable — generated source *and* diagnostics — across a
  broad, deterministic corpus.
- A **reusable kit**: adding a generator should cost a registration, not new bespoke infrastructure.
- Close the known holes: `MapToGenerator` cacheability, the symbol-leak check, harness coupling.
- Make regression **impossible to reintroduce silently** (ratchets), consistent with existing practice here.

**Non-goals**

- Exhaustive feature *interaction* coverage. Full cross-products are combinatorially infeasible;
  `FeatureInteractionCompileMatrix` samples interactions and continues to own that. This corpus covers each
  axis at least once, plus those existing sampled interactions. Stated plainly so the corpus is not read as
  claiming more than it does.
- Any behavioural change to the generators. The only `src/` edits are `WithTrackingName` calls and the step-name
  constants tests address.
- A separate `TestKit` NuGet/project. There is one consumer today; revisit if a second appears (YAGNI).

## 3. Architecture

Four single-purpose units inside `DwarfMapper.Generator.Tests`. Existing tests are untouched.

```
GeneratorTestHarness (exists)  — owns the cached metadata-reference set + BuildCompilation
        │  reused, never duplicated (the reference cache is the expensive part)
        ▼
1. GeneratorRunner        run ANY IIncrementalGenerator -> diagnostics + ALL outputs by hint name
        │                 RunTracked(...) enables TrackIncrementalGeneratorSteps
        ├───────────────► 2. GeneratorCacheAssert   the cacheability battery
        └───────────────► 3. GoldenCorpus           pinned cases -> fingerprints -> manifest
                                     │
                          4. GeneratorTestingScanTests   ratchets over 1–3
```

`GeneratorTestHarness` deliberately stays the **compilation factory only**. It does not gain generic running —
that is `GeneratorRunner`'s job. It is already 8 public methods; growing it further would recreate the god-file
problem items 3–4 exist to fix.

## 4. Components

### 4.1 `GeneratorRunner`

Generator-agnostic execution.

```csharp
internal sealed record GeneratorRun(
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableDictionary<string, string> OutputsByHintName);

internal static class GeneratorRunner
{
    static GeneratorRun Run(IIncrementalGenerator generator, string source,
                            NullableContextOptions nullable = NullableContextOptions.Disable);

    // NOTE: takes no Compilation — the caller drives it with .RunGenerators(compilation).
    // An unused parameter is IDE0060, which is a build error under AnalysisMode=All.
    static GeneratorDriver RunTracked(IIncrementalGenerator generator);
}
```

**Returns every emitted file, keyed by hint name.** This is load-bearing: `GeneratorTestHarness.Run` *excludes*
the assembly-wide aggregates (facade extensions, DI registration, ambient manifests) so per-mapper snapshot
tests select the right file. A golden runner inheriting that filter would fingerprint one file per case and
never cover the aggregate emitters at all.

### 4.2 `GeneratorCacheAssert`

Three assertions, runnable against any generator and any set of tracking names:

- `FullyCachedOnRerun` — identical compilation twice ⇒ every tracked step `Cached`/`Unchanged`.
- `CachedAfterUnrelatedEdit` — adding an unrelated type must not recompute the mapper pipeline.
- `NoSymbolsInPipeline` — no tracked step output holds `ISymbol`, `SyntaxNode`, `Location`, or `Compilation`.
  Built-in steps (which legitimately carry a `Compilation`) are excluded by inspecting only the generator's own
  named steps. **As implemented this walks the output's members recursively** (properties and fields,
  `IEnumerable` elements, depth cap 16, reference-equality cycle guard) and reports the offending member path —
  a top-level type check would miss the realistic leak, `record Model(..., ISymbol Sym)`, whose *output* is the
  record. Hitting the depth cap **fails loudly**: a truncated branch must never be mistakable for a clean one.
  Complements the pre-existing `ModelCacheSafetyTests`, which enforces the same rule *structurally* over declared
  types in the `Model` namespace — see the reconciliation note in the correctness-first design.

`Battery(GeneratorUnderTest generator, string source)` runs all three; the unrelated edit and the tracking names
come from the registry entry rather than being passed separately.

### 4.3 `GoldenCorpus`

Pinned, deterministic cases across three axes, fingerprinted and recorded in one manifest.

**Case sources (IDs pinned, so schema growth appears as explicit added/removed lines):**

| Axis | Source | ID form |
|---|---|---|
| Type | `CombinatorialSchema.DepthOneMatrix()` + `DepthTwoMatrix()` | `cmb:int\|Dictionary\|Identity` |
| Type (fuzz) | `SyntheticSchema` over a **fixed** seed range | `syn:seed-0042` |
| Feature | hand-curated list in `GoldenCorpus.FeatureCases()`, guarded by a taxonomy-growth ratchet | `feat:FlattenGraph`, `feat:NullStrategy.Throw` |

The **feature axis is a curated list, not a derivation** — `GoldenCorpus.FeatureCases()` is hand-written, and
nothing ties its cases mechanically to the attribute/enum taxonomies it covers. What keeps the curation honest
is `GeneratorTestingScanTests.The_feature_taxonomy_has_not_grown_past_its_recorded_baseline`: it counts the
public attribute types and public enum values on `typeof(DwarfMapperAttribute).Assembly` and asserts those
counts equal recorded baseline constants. If either taxonomy grows, the ratchet fails with a message saying a
taxonomy grew and the feature list should be reviewed, then the baseline updated deliberately. This is weaker
than true derivation — it cannot force the SPECIFIC new case the way generation from the taxonomy would — but
it does force a human to notice growth and decide, rather than the corpus silently going stale next to an
attribute or enum value nobody golden-tested.

**Both generators** contribute cases; `[MapTo]` is a separate `IIncrementalGenerator` with its own emission.

**Fingerprint:**

```
sha256( concat(outputs ordered by hint name)
      + "|"
      + join(diagnostics ordered by (Id, Location, Message),
             d => $"{d.Id}:{d.Severity}:{d.Location}:{d.GetMessage(Invariant)}") )
```

Full message text is included by explicit decision: nothing observable may move undetected. The cost is churn on
rewording, mitigated in §6.

**Artifacts:**

```
tests/DwarfMapper.Generator.Tests/Golden/
    output-manifest.txt          one sorted line per case: <case-id> <sha256>
    Snapshots/*.verified.txt     ~15–25 curated, human-readable (Verify)
```

### 4.4 `GeneratorTestingScanTests` (ratchets)

- Every `IIncrementalGenerator` in `src/` is registered in the battery. This is what stops the next generator
  repeating `MapToGenerator`'s zero-coverage history.
- Every `WithTrackingName` step is actually asserted by the battery — an unasserted name is decoration.
- Manifest entry count does not fall below a **recorded floor constant** (set to the count at the time the
  manifest is first established, and lowered only by a deliberate edit with justification — the same pattern as
  the existing allowlist-size gates).
- **Every case source contributes** at least one case: the type axis, the fuzz axis, the feature axis, and each
  registered generator. A corpus that silently loses a whole source is the vacuity failure mode this repo keeps
  encountering, and the count floor alone would not catch it (one source growing can mask another vanishing).
- **Feature-axis taxonomy has not grown past a recorded baseline.** `FeatureCases()` is hand-curated, not
  derived, so nothing mechanically ties it to the attribute/enum taxonomies it covers; this ratchet counts
  public attribute types and public enum values on the runtime assembly and fails when either count exceeds the
  recorded baseline, forcing a deliberate look at whether the new shape needs a pinned case.

## 5. Determinism prerequisites

The manifest is worthless unless output is reproducible. Three conditions, one only recently earned:

1. **LF-normalised output.** Fixed in ISSUE-024 (all `AddSource` goes through `AddNormalizedSource`). Before
   that, hashes differed between Windows and Linux and a shared manifest was impossible. This is a hard
   dependency, recorded so nobody reverts it casually.
2. **Deterministic diagnostic ordering** — emission order is not guaranteed; the fingerprint sorts.
3. **Deterministic corpus** — fixed seed range, pinned IDs, no `Date.Now`/randomness.

The corpus **runs the generator only** — it does not compile or execute the result. That is far cheaper than
`CombinatorialEngineTests` (which compiles and maps), so the expected cost is seconds. Runtime will be measured
and reported; the `SyntheticSchema` seed range is the dial if it needs trimming.

## 6. Error handling and the churn story

**Fail loud, never self-heal.** Every failure names exactly what moved.

| Situation | Behaviour |
|---|---|
| Manifest missing | Fail with instructions. Never auto-created — otherwise CI silently blesses whatever it produced. |
| Case in corpus, absent from manifest | Fail, listing the new case IDs (a new shape needs conscious review). |
| Case in manifest, absent from corpus | Fail, listing them — catches accidental corpus shrinkage, the vacuity failure mode. |
| Fingerprint differs | Fail, listing changed case IDs, and write before/after generated source to the scratch dir for diffing. |
| Manifest below floor / an axis contributes zero | Fail (non-vacuity). |

**Regenerating.** `DWARF_GOLDEN_UPDATE=1` rewrites the manifest in one step. Because hashes do not diff
readably, the ~20 curated Verify snapshots are what a human actually reads during an intentional change: they
show real text, so review happens there while the manifest records breadth. This split is the reason for the
hybrid design.

## 7. Testing the framework itself

The framework concentrates a lot of assertions into few helpers, so it needs its own proof — the same reasoning
that produced `GeneratorAssertSelfTests` this month:

- Each `GeneratorCacheAssert` assertion is proven to **fire**: a deliberately non-equatable model fails
  `FullyCachedOnRerun`; a deliberately leaked symbol fails `NoSymbolsInPipeline`.
- `GoldenCorpus` fingerprinting is proven sensitive: a one-character change to generated output changes the
  fingerprint.
- The ratchets are proven non-vacuous by temporarily removing a registration.

A helper that silently passes everything would neuter the entire net while the suite stayed green.

## 8. Risks

| Risk | Mitigation |
|---|---|
| Manifest churn on message rewording | Curated snapshots carry the readable diff; one-command regeneration. Accepted cost of the chosen strictness. |
| Corpus adds meaningful runtime | Generator-only (no compile/emit); measure and report; seed range is the dial. |
| Feature-axis corpus drifts from the taxonomy | Curated, not derived; a taxonomy-growth ratchet (recorded attribute-type/enum-value counts) fails when the taxonomy outgrows what the corpus was reviewed against, forcing a deliberate look. |
| Ratchets become noise | They fail only on structural regressions (new generator/pipeline unregistered, corpus shrinkage), not on ordinary edits. |

## 9. Out of scope

- Items 2–4 of the programme (each gets its own spec).
- Interceptors (C# 14/.NET 10, now stable) — surveyed during research and noted as a future option for call-site
  work; not part of this sub-project.
- Any behavioural generator change.
