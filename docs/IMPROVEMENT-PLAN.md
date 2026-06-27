<!-- SPDX-License-Identifier: GPL-2.0-only -->
# DwarfMapper improvement plan

Output of the methodology research workflow (test generalization, generics, diagnostics) plus AOT/non-AOT
benchmark regression scrutiny. Items are ranked by payoff/effort across all three dimensions; each is
marked **[DONE]** (landed this round) or **[PLANNED]** with effort (S/M/L) and risk. Items that conflict
with the library's stated stances (resilience-first, reflection-free/AOT-safe, no-silent-surprises,
open-generics non-goal) were dropped during synthesis and are listed at the end with the reason.

## Diagnostic ID allocation

Used: **DWARF053** (generic mapper method), **DWARF054** (generic mapper class), **DWARF055** (mapper
too large / build-budget Info — landed). Free slots for the planned diagnostics below: **DWARF056–DWARF059**.
Reserved/unused historical: 004, 006, 029.

> **Landed since this plan was written (2026-06-18, correctness-first design):** structural cache-safety
> meta-test (`ModelCacheSafetyTests`), incremental-caching test (`IncrementalCachingTests` — closes item 4's
> caching leg), generator-determinism test (item 4), DWARF055 build-budget, XML-doc mapping plan on
> generated methods, **the reflection-free half of item 16** (`ReflectionFreeMetaTests`), and
> `docs/CORRECTNESS.md`. Remaining from item 16: the allocation-bound (`GC.GetAllocatedBytesForCurrentThread`)
> invariants. See `docs/superpowers/specs/2026-06-18-correctness-first-generator-design.md` for the phased
> design (Phases A/B/E done; C = CodeFixes package, D = per-method granularity remain).

## Benchmark regression scrutiny (capstone)

- **AOT real-world infrastructure: GREEN.** `samples/DwarfMapper.AotSample` NativeAOT-publishes with
  **zero** IL2xxx/IL3xxx trim/AOT warnings; native exe ~1.53 MB; the runtime gate passes every check
  (checked-narrowing overflow, IParsable, IFormattable, record ctor, auto-nest, depth-guard, None/Preserve/
  SetNull cycles incl. through collections, FlattenGraph homo+hetero, MapDerivedType dispatch, update-into,
  zero-alloc span map, SIMD widen == scalar, async streaming). No AOT regression.
- **Blit (1000 structs): confirmed +29% doc drift — [DONE].** Measured ~0.50 µs vs documented 0.39 µs
  (repeatable across two runs + the committed artifact). Not a code regression — the 0.39 µs was a
  best-case figure; steady-state is ~0.50 µs and the SIMD lead is ~2.0–2.3× (was stated 2.5×).
  `docs/COMPARISON.md` corrected.
- **Array (1000 objects): confirmed code regression — fix [DONE] and VERIFIED.** DwarfMapper trailed
  Mapperly ~1.5× (and even AutoMapper) on `FlatSrc[]`→`FlatDst[]`. Root cause: the non-recursive array
  fill used `foreach` + a separate running write-index, leaving the destination store not provably
  in-bounds (bounds check survives). Fixed by emitting an index `for` over `src[__i]` in the None-mode
  array path only (`CollectionConverter.cs`), so both source-read and dest-write bounds checks elide.
  Preserve/SetNull/ctx-threaded/non-array paths are unchanged. **Re-measured: 7.73 µs → 5.21 µs (~33%),
  now the FASTEST of the four** (Mapperly 5.30, AutoMapper 5.76, Mapster 6.17 µs). The secondary cost
  (per-element non-inlined `MapFlat` + `ThrowIfNull`, item 22) is therefore moot — no further work needed.

## Ranked plan

### Landed this round
1. **[DONE] DWARF053 — generic partial mapper method (Arity>0) is refused loudly** instead of emitting a
   type-parameter-less body that silently fails to satisfy the declaration. Error + skip.
2. **[DONE] DWARF044 emitted for `[Flatten]` over a nullable-reference root** — previously the dotted
   `[MapProperty]` path warned but the `[Flatten]` path emitted unguarded `src.Root.Leaf` that NRE'd
   silently. Now consistent (Info, suppressible).
3. **[DONE] DWARF054 — `[DwarfMapper]` on a generic class is refused loudly** (it would emit a non-matching
   `partial class Foo` with no `<T>`). Error + skip.
4. **[DONE] Generator determinism property test** — `DeterminismFuzzTests` runs the generator twice over
   the behavioural/advanced-feature seed generators + Preserve/SetNull sources and asserts byte-identical
   output and stable diagnostics. Guards a whole class of unordered-iteration non-determinism that
   single-run tests are blind to; also a prerequisite for trustworthy Verify snapshots. (No latent
   non-determinism found — all green.)
22. **[DONE] Array fill bounds-check elision** (see benchmark section) — indexed `for` over `src[__i]` in
   the None-mode array path.

### Planned — highest-leverage test investment (M, low risk)
5. **[PLANNED·M] Metamorphic "all emit paths agree" value oracle.** The value oracle currently runs only
   on the default None-mode identity-shape mapper. Parameterize `SyntheticSchema.BuildSource` with a
   mode/attribute string and a construct/update kind; for each seed build the same Src/Dst under None,
   Preserve, SetNull, and update-into, map an **acyclic** instance, and assert `CrossTypeDiff` is empty AND
   all four results are mutually value-equal. On acyclic input every mode must agree; divergence is a bug.
   Catches the exact Preserve/SetNull/update-into value-regression class that historically hid (deferred
   unflatten) — today only checked for *compiles*, never *correct value*.
6. **[PLANNED·M] SetNull-yields-a-DAG and Preserve-is-isomorphic properties over randomized graphs.**
   `TopologyOracleFuzzTests` fuzzes random graphs only under Preserve and only asserts `TopologyDiff==0`.
   Add (1) a SetNull DFS asserting the destination graph is acyclic (catches emitters that break only
   *some* back-edges → residual cycle → SO at consumption), and (2) a Preserve distinct-node-**count**
   equality check (catches silent de-dup failures that pass value + edge-sampled topology). Generalizes
   the anti-mislinking core property from ~5 hand-built shapes to the whole graph space.

### Planned — diagnostics (mostly S; one has a breaking-change caveat)
7. **[PLANNED·S] DWARF055 (Info) — nullable value-type unwrap (`T?`→`T`) under `NullStrategy.Throw`.**
   Default Throw emits `?? throw new InvalidOperationException` with no compile-time signal. Emit only when
   the strategy is Throw; keep Info (suppressible) to control noise. Makes the runtime throw a visible
   choice, consistent with DWARF044.
8. **[PLANNED·S, breaking-risk] Re-tier DWARF044 → Warning and split DWARF038 lossy sub-cases → Warning.**
   These describe runtime-throwing / data-losing behaviour yet are Info, while DWARF037/051 (mere no-op
   settings) are Warning — severity is inverted vs the design's own consequence ordering.
   `EmitImplicitConversionDiag` already takes a severity arg (SeverityOverride-only change). **Caveat:**
   Warning can break `-warnaserror` builds — keep enabled-by-default and document the `.editorconfig`
   downgrade before shipping.
12. **[PLANNED·S] DWARF056 (Info) — `[MapValue]` shadows an auto-matchable source member.** A leftover
   `[MapValue]` stub silently masks newly-added real source data (DWARF039 does not fire here). Emit only
   when a same-name readable source member exists. Additive, suppressible; catches a refactor hazard.
13. **[PLANNED·M] DWARF057 — update-into nested-member replacement, and fix the overstated XML doc.** The
   update-into doc claims "identity preserved" while nested members are silently *replaced*; surface this
   so callers expecting a deep merge are warned. Pairs with a doc correction.
14. **[PLANNED·M, FP-risk] DWARF058 — `[MapProperty(When=)]` false leaves a non-nullable member at default.**
   Many legitimate uses want exactly that; restrict to non-nullable targets and keep Info to limit
   false positives.
15. **[PLANNED·S] Enrich the DWARF038 parse/format message** to name `FormatException`/`OverflowException`.
   Folds naturally into item 8.

### Planned — test-shape coverage (M)
9. **[PLANNED·M] Add non-trivial type shapes to the fuzz pools** — `readonly`/`record struct`, init-only,
   `required` members, deep nullability (`List<T?>`, nested nullable). The power-set fuzz cannot find
   assignment-strategy bugs (init-only/readonly assigned outside an initializer, missing `required`
   member CS9035, `List<int?>` element-converter NRE) because the pools contain only mutable classes and
   one trivial struct. Route the new shapes through the same compile + `CrossTypeDiff` value oracle.
11. **[PLANNED·M] Convert example-based parity/round-trip/update-into tests into metamorphic properties.**
   ~25 feature tests assert a single hand-picked input each. Use `ObjectFactory` + `RoundTrip.Verify` to
   assert round-trip idempotence over N random inputs, update-into idempotence (`Update(s, Map(s))` is a
   no-op), and identity value-preservation for all random inputs. Pin intentional non-round-trippable
   cases (enum by-name). Overlaps conceptually with items 5–6; sequence after them.
16. **[PLANNED·M, flaky-risk] Allocation-bound + AOT-token static-analysis invariants.**
   `GC.GetAllocatedBytesForCurrentThread()` assertions (0 bytes for a blittable struct map) under
   `[Collection]` isolation, plus a meta-test scanning generated source for `typeof`/`Activator`/
   `GetType().GetProperty`/`MakeGenericType` tokens in hot methods (an AOT-safety proxy needing no AOT
   toolchain).
17. **[PLANNED·M] Parameterize the topology fuzz over dict/array/struct-wrapper edge carriers** — targets
   the v23 collection/dict-edge cycle path; requires `GraphOracleComparer.TopologyDiff` to traverse those
   edges first.
18. **[PLANNED·M] Cross config modes pairwise + extend update-into under Preserve/SetNull in the fuzz** —
   needs the `ExpectedError` oracle to enumerate valid-vs-diagnosed combinations.
19. **[PLANNED·L, medium-risk] Async-stream / Span / IQueryable-projection property coverage.** Projection
   translatability legitimately fails for some shapes; net10 ref-struct schema generation is the cost.

### Planned — features & docs
10. **[PLANNED·L] `DwarfMapper.CodeFixes` analyzer package.** No CodeFix/Analyzer project exists, yet
   DWARF001/007 already embed `[MapIgnore(...)]` in their message text, so users expect a lightbulb. A
   netstandard2.0 package (Microsoft.CodeAnalysis.CSharp.Workspaces) keyed on existing ids: insert
   `[MapIgnore]`/`[MapProperty]`, disambiguate ambiguous matches (DWARF010), complete incomplete enums
   (DWARF015). Largest single ergonomics win; packaging cost is the reason it's not a quick win.
20. **[PLANNED·L, demand-gated] Opt-in generic element-wrapper family** (`Envelope<TIn>`→`Envelope<TOut>`),
   **closed instantiations only** — one synthesized mapper per used `(A,B)` pair, emitting concrete types
   (AOT-safe; open generics remain a non-goal). Reduces boilerplate for `Result<T>`/`Page<T>` DTO
   families. Build only if users ask; gate strictly on single-payload non-collection generics.
21. **[DONE-partial] Document the generic method/class limits.** The DWARF053/054 diagnostics now make the
   limit loud; the `docs/MIGRATION.md` non-goals table notes generic methods/classes are diagnosed. (Full
   support, if ever, is item 20's sibling and remains out of scope.)

## Dropped during synthesis (with reason)
- **Open-generic mapping (`CreateMap(typeof(S<>), typeof(D<>))`-style).** Conflicts with the documented
  open-generics / reflection-free non-goal; the audit did not argue the stance should change. The
  closed-instantiation wrapper family (item 20) is the AOT-safe alternative.
- **Per-pair → generic-helper codegen refactor.** Pure snapshot churn with zero runtime payoff.
