# Consumer-test coverage holes — surface cross-reference (tip 20e032c)

Method: extract the shipped consumer surface from `src/DwarfMapper` (29 attributes, 8 option enums, 18
`[DwarfMapper(...)]` properties, 14 registry/facade members), then cross against the four consumer-shaped
assemblies (ConsumerTests.Host, CleanCorpus, DifferentialTests, NegativeCases) and, separately, the whole suite.
Instrument failures disclosed: the enum-member extractor swallowed XML docs (its "untested enum" list is artifact
— `Preserve`/`SetNull`/`Flexible` are heavily tested and were retracted), the Testing-package extractor returned
empty (corrected by direct grep), and all matching is substring-power (stated).

## H1 [Medium] — `RegisterUpdate` has no direct contract tests anywhere
`DwarfMapperRegistry.RegisterUpdate` (`:179`) is referenced exactly twice in the repo: its declaration and the
generator emitting calls to it. It executes *implicitly* (module initializers registering update maps, which
consumer tests then hit via `Update`), but nothing pins its contract: first-wins vs overwrite, ambiguity marking
for duplicate update pairs, interface resolution on the update table, and — the sharp one — **the round-13
concurrency torture covers `Register`/`Maps` only; the update table has no torture coverage.**
→ Action: mirror the four torture invariants onto `RegisterUpdate`/`Update`, plus a small contract fixture.

## H2 [the usage-surface headline] — 13 public attributes absent from all four consumer assemblies
Every one is generator-tested; none appears in any consumer-shaped test:
`[BeforeMap]` (hooks — `[AfterMap]` has exactly 1 consumer use, `[BeforeMap]` zero), `[DwarfMapperDefaults]`,
`[DwarfMapperOptions]` (class/assembly-level defaults — a primary real-consumer pattern), `[MapIgnoreSource]`
(the documented remedy for DWARF039 — the remedy itself is never exercised consumer-side), `[FlattenGraph]`,
`[MapCollectionKey]`, `[Reinterpret]`, `[UsesMap]`, `[DwarfRequiresMap]`, `[DwarfProvidesMap]`,
`[DwarfMapperConstructor]`, `[AutoNest]`, `[DwarfMapperValidationRoot]`.
→ Action: fold into the expansion plan as its item 0 — a **surface-parity sweep**: every public attribute
appears in ≥1 consumer corpus with a stated MAPS/OPT-IN expectation. Priority within: BeforeMap, Defaults,
Options, MapIgnoreSource, FlattenGraph first (highest real-world frequency); rest as corpus rows.

## H3 [Small] — registry *read* API consumer-absent; two members untested anywhere
Consumers integrating the ambient registry write `TryGet`/`IsProvided` — both are generator-side only.
`AmbiguousInterfaces` and `DestinationType` have zero references in any test family.
→ Action: one consumer fixture using the read API the way integration docs show; two trivial property tests.

## Confirmed strong (no hole — kept for the record)
- **Diagnostics wording: 81/81 live ids pinned in NegativeCases** (the 3 absentees — 006/019/029 — are the
  documented retired ids). Consumer-visible refusal coverage is total.
- `[RoundTrip]` (Testing package) **is** consumer-adopted — DifferentialTests and CleanCorpus both use it
  (instrument's zero was extractor failure).
- `Update`/`IsUpdateProvided` have consumer-side coverage (the lookup half of H1's table).
- Core option enums fully exercised (artifact list retracted).

## Ratchet so the class cannot reopen
Add a self-validation scan mirroring the existing samples-surface ratchet (`36d4ae9`) but pointed at the four
consumer assemblies: fail when a public attribute/registry member has zero consumer-shaped references, with an
explicit allowlist for deliberate exclusions. H2 then becomes impossible to regrow silently.
