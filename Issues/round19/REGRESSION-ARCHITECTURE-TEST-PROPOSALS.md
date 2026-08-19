# DwarfMapper.NET — proposed regression & architecture tests (kernel-report style)

Format per entry: subject / Where / Why (with the engagement evidence that motivates it) / Test design /
Fires-when. Ordered by defect-class frequency across rounds 1–17, not by ease. All designs use the repo's own
self-validation idioms (scan tests, ratchets, golden tables) — no new framework dependency required unless noted.

---

[REG-01] contracts: endpoint-parity matrix for every class-level option
  Where: new `Contracts/OptionEndpointParityMatrix.cs`, generalizing `OptionGaps`/`9146d99`'s method.
  Why: the single most frequent defect class of the whole engagement — an option honoured at one endpoint and
       ignored at another (ISSUE-044 flatten, ISSUE-046 projection, the three fixed in `9146d99`, DWARF077 in
       `763a201`). Each was found one at a time; the matrix finds the next one wholesale.
  Test: generated Theory over {option} × {Map, Update, Project, collection-element, registry route}: a cell
        declared Honoured must produce *observably different* output vs the option's other value; declared
        gaps must match `OptionGaps.KnownSilent` exactly (both directions — no undeclared gap, no stale entry).
  Fires-when: any new option lands wired to fewer than all endpoints, or a documented gap silently closes.

[REG-02] pipeline: ban optional parameters on private resolvers
  Where: `SelfValidation/` Roslyn scan over `Pipeline/**`.
  Why: ISSUE-043 and ISSUE-044 were both "optional param defaults to the permissive value, one call site
       forgets it". The fix made them required; nothing stops the next resolver reintroducing the pattern.
  Test: walk private/internal static methods in Pipeline; fail on any optional `bool`/`Compilation?` parameter
        whose name matches the option vocabulary (autoNest, allowNonPublic, explicitOnly, ignoreObsolete, …),
        allowlist for reviewed exceptions.
  Fires-when: the 043/044 hazard class is reintroduced anywhere in the pipeline.

[REG-03] conversions: golden scalar-conversion table
  Where: `Golden/ConversionMatrixGolden.cs` + one checked-in table file.
  Why: round-10 measured the 34-pair matrix by hand; `CharConversionPolicyTests` pins char only. The rest of
       the matrix can drift cell-by-cell without any single test noticing the *policy* changed.
  Test: enumerate all scalar pairs (+ DateOnly/TimeOnly/DateTimeOffset when adopted), record
        {diagnostic id, severity, emits, CreateChecked} per cell, golden-compare with the existing
        CI-bless guard. A policy change becomes a reviewed one-line diff of the table.
  Fires-when: any conversion cell changes class (silent↔warn↔refuse) unintentionally.

[REG-04] determinism: byte-identity harness in CI
  Where: `SelfValidation/DeterminismTests.cs`.
  Why: H1 was *measured* in round 10 (6 runs = 1 output; reversed member order = byte-identical) but lives in
       an audit doc, not a test. Ordering leaks regress silently — the defect the EquatableArray/sort work
       fixed once already.
  Test: canonical rich model (nested, collections, dict, nullable) → run generator twice + member-reversed
        variant; assert all three outputs byte-equal. Cost ≈ 3 generator runs.
  Fires-when: any emitter iterates an unordered structure into output.

[REG-05] diagnostics: no id ships without a remedy-wording pin
  Where: extend `DiagnosticMessageContractTests` (NegativeCases).
  Why: 81/81 live ids are pinned today — the round-18 coverage check proved it — but the property holds by
       diligence, not by construction; the next DWARF08x can ship with id-only assertions.
  Test: fail when a descriptor exists (excluding the documented retired set) with no NegativeCases row pinning
        both the id and its remedy phrase.
  Fires-when: a new diagnostic lands wording-unpinned.

[REG-06] registry: update-table torture + contract
  Where: extend `RegistryConcurrencyTortureTests`; new `RegistryUpdateContractTests`.
  Why: round-18 coverage found `RegisterUpdate` has zero direct tests — declaration + emitted call only. The
       Maps table has four torture invariants; the update table has none, and it is the same shared static.
  Test: mirror the four invariants (same-pair race first-wins+ambiguous, distinct none-lost, churn monotonic,
        base/interface resolution switches once) onto `RegisterUpdate`/`Update`.
  Fires-when: the update table's atomicity or precedence diverges from the read table's contract.

[REG-07] portability: CRLF-checkout CI leg
  Where: one CI job: `git clone --config core.autocrlf=true` → run `DocSnippetInjector` + raw-string-heavy suites.
  Why: ISSUE-048 was demonstrated by a paired experiment (2/9 fail on CRLF); `.gitattributes` now prevents it,
       but nothing *proves* the prevention holds as new raw-string tests land or the attributes file is edited.
  Test: the experiment, as a job; green = the policy file still covers every sensitive path.
  Fires-when: a new file pattern escapes `.gitattributes` and reintroduces terminator-sensitive expectations.

[REG-08] suppression: loud-about-quiet stays loud
  Where: `NegativeCases`.
  Why: round-17's sharpest measurement — `[SuppressMessage]` on an Error still reports, still suppresses
        emission, and raises DWARF078. That three-part contract is exactly one refactor away from silently
        becoming "suppressed means silent nothing".
  Test: pin all three observables (error present, genLen=0, DWARF078 present) in one test.
  Fires-when: suppression handling weakens any leg of the contract.

---

[ARCH-01] layering: package reference directions are frozen
  Where: `SelfValidation/AssemblyGraphTests.cs`.
  Why: the runtime package is GPL-consumer-facing and net10; the generator is netstandard2.0 analyzer-slot.
       One accidental ProjectReference collapses the packaging story (single-package bundling depends on it).
  Test: assert the reference graph exactly: Generator/CodeFixes → (M.CA only); DwarfMapper → (none);
        Testing → DwarfMapper; nothing references Generator except tests via the pinned InternalsVisibleTo set.
  Fires-when: a convenience reference crosses a packaging boundary.

[ARCH-02] emission hygiene: the round-16 instruments as permanent scans
  Where: `SelfValidation/EmittedLiteralHygieneTests.cs`.
  Why: measured clean once (0 unqualified `global::`, 0 culture calls, 0 real `Environment.NewLine`);
       cleanliness by measurement decays, cleanliness by test does not.
  Test: three scans over `src`: emitted-statement literals must qualify System/Microsoft/DwarfMapper tokens
        with `global::`; no `ToLower()/ToUpper()`/culture-free `ToString` on user data; `Environment.NewLine`
        only in comments. Allowlist file for reviewed exceptions.
  Fires-when: a new emitter or fix reintroduces any historically-cleared defect class.

[ARCH-03] diagnostics: single point of declaration
  Where: same scan family.
  Why: 84 descriptors live in one file and every sync mechanism (AnalyzerReleases, docs, contract tests)
       assumes that; an inline `new DiagnosticDescriptor` elsewhere silently exits every one of those nets.
  Test: fail on `new DiagnosticDescriptor(` outside `DiagnosticDescriptors.cs` (+`RegistryDiagnostics.cs`).
  Fires-when: an id is minted outside the tracked surface.

[ARCH-04] consumer parity: public surface must exist consumer-shaped
  Where: `SelfValidation/ConsumerSurfaceParityTests.cs` (the round-18 finding, as its own ratchet).
  Why: 13 public attributes had zero consumer-assembly presence; the samples ratchet (`36d4ae9`) proved the
       idiom works — it just points at the wrong audience for this property.
  Test: every public attribute/registry member of the shipped packages appears in ≥1 of the four consumer
        assemblies, explicit allowlist for deliberate exclusions, count ratcheted.
  Fires-when: a new public surface lands generator-tested only.

[ARCH-05] structure: growth ceilings on the known hotspots
  Where: `SelfValidation/StructureRatchetTests.cs`.
  Why: `ExtractCore` is 2,237 lines and every option lands inside it; the partial split was verified lossless
       once (no duplicate members) but nothing keeps it so.
  Test: (a) method-length ceiling on `ExtractCore` at current+ε — lowering is deliberate, growth fails;
        (b) duplicate-member-name scan across the `MapperExtractor` partials (the round-6 check, as a test).
  Fires-when: the biggest auditability hotspot grows further, or a partial merge collision appears.

[ARCH-06] write paths: repo mutation requires a registered pattern
  Where: same scan family.
  Why: four test-side repo writers exist; one (`DWARF_SELF_HEAL`, ISSUE-036) is pass-enabling and unguarded —
       the class was extended twice without the shared guard the issue recommended.
  Test: any `File.Write*` targeting the repo tree from tests must either call the shared `CiEnvironment`
        refusal or match the write-then-`Assert.Fail` regenerator pattern; enumerated allowlist.
  Fires-when: a fifth writer lands with neither guard — or ISSUE-036's path is touched without gaining one.

---

Suggested landing order: REG-01/02 and ARCH-02/04 first (they convert the four most expensive historical
defect classes into compile-time/CI impossibilities), then REG-05/06/08, then the golden/determinism pair
(REG-03/04), then the structural ratchets. Every entry above is falsifiable by design: each names the exact
change that must turn it red, which is the property the round-5 framework established as the house standard.
