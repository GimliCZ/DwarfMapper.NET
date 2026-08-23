# [RFC] DwarfMapper.NET — Round 26: architecture styling & maintainability
# Subject: reduce the structural cost of the next two years of options without touching behaviour

Grounding [MEASURED, tip `fac40f3`]: `ExtractCore` body is now **2,751 lines** (+514 since the round-14
measurement of 2,237 — the "every option lands inside it" growth law, confirmed), inside a 3,939-line file
that heads an **11-partial family** (Flatten 1,567 / Projection 1,487 / Conversions 1,305).
**30 resolver methods carry ≥7 parameters** — worst: `ResolveProjectionCtorExpr` **16**,
`ResolveUnflattenTarget` **15**, two more at 13 — the direct scar tissue of the ISSUE-044 required-params
fix (correct, and ergonomically brutal). The registry hand-mirrors **four** `ConcurrentDictionary` tables
(Maps/Ambiguous × read/update) — the update-side ambiguity mirror was added by hand after the asymmetry
was found, i.e. table drift has *already happened once*. `DiagnosticDescriptors.cs` is a single 1,537-line
file carrying 90+ descriptors.

One planned entry is withdrawn before proposal [MEASURED]: emission style needs no unification — 149
`w.Line` writer calls, **zero** raw-string emitters. The codebase already made that choice cleanly.

The load-bearing idea of the whole round: **every refactor below runs under a byte-identity lock.** The
golden-fingerprint corpus + the determinism property mean any structural change can be validated as
"same bytes out for the whole corpus" — the audit history's strongest asset turned into a refactor
harness. No entry here is allowed to change a single emitted byte; the tests that prove that already
exist and green/green before-after is the merge criterion.

---

## [R26-01] pipeline: decompose ExtractCore along its own 30 marked seams

What: split the 2,751-line method into per-phase private static methods in phase-named partials,
      cut exactly at the **30 seam comments the method already contains** — the comments become the
      method names.
Why:  growth is measured, not speculative: +23% in the span between two audit measurements, and every
      new option lands inside it. It overflows any review window, is the single biggest obstacle to
      auditing (round-9 had to read it in seam-cut batches — the seams are that real), and its length
      is the reason 16-param signatures exist: phases share state through parameters because they
      cannot share scope.
How:  mechanical extraction, one phase per commit, each commit proven byte-identical on the golden
      corpus. Phase methods take/return the R26-02 context (below) so extraction and de-parameterization
      land as one motion. No logic edits permitted inside the same commit as a move — the round-8
      "best work in project history" contract-matrix commit showed the maintainer already separates
      these; this codifies it.
Regression/architecture tests: golden fingerprints (existing) = behaviour lock; determinism byte-identity
      (REG-04) = ordering lock; ARCH-05 ceiling ratchet re-pinned to the post-split maximum phase length
      (~300 lines) so the next 2,751-liner cannot regrow; the round-6 duplicate-member scan across the
      now-12+ partial family becomes a permanent test.

## [R26-02] pipeline: ExtractionContext — collapse the 13–16-param signatures without reopening 043/044

What: a sealed `readonly record ExtractionContext` carrying { Compilation, options (AllowNonPublic,
      AutoNest, ExplicitOnly, …), the per-mapper defaults } threaded as ONE required parameter.
Why:  30 methods at ≥7 params [MEASURED] is what the 044 fix cost: totality was bought with parameter
      bloat, and 16-param signatures are where the *next* threading bug hides (a transposed pair of
      bools compiles clean). The context restores ergonomics while keeping the property that made
      required-params the right fix: there are no defaults anywhere — the context has exactly one
      construction site per mapper extraction, where every option is decided once, compiler-enforced.
How:  `internal sealed record ExtractionContext(Compilation Compilation, OptionSet Options, …)` with a
      single factory reading the attribute model. Mechanical signature migration phase-by-phase riding
      the R26-01 commits. Positional bools inside OptionSet become named properties — the transposition
      hazard dies with the parameter lists.
Regression/architecture tests: REG-02's optional-param ban stays; add its dual — a Roslyn scan pinning a
      **per-method parameter ceiling at current counts** (ratchet downward as R26-01 lands): a new
      resolver may not exceed ~6 params, so the parameter-bloat era cannot return either. Byte-identity
      lock applies to every migration commit.

## [R26-03] runtime: RegistryTable<TDelegate> — one implementation for the four mirrored dictionaries

What: a private generic `RegistryTable<TDel>` (Maps + Ambiguous + walk/resolution + torture invariants)
      instantiated twice (read table, update table) instead of four hand-mirrored dictionaries.
Why:  the drift is not hypothetical: the update table shipped **without** ambiguity marking until the
      audit's contract test exposed the asymmetry, and the fix was a hand-copied mirror [MEASURED:
      `UpdateAmbiguous` at `DwarfMapperRegistry.cs:196` with the doc comment citing the same contract].
      A third table family (interface-resolution cache, projection registry, …) would drift the same way.
How:  extract `Register/TryGet/IsProvided/IsAmbiguous` + the base-type/interface walk into the generic;
      public API stays byte-compatible (thin forwarding members — the public surface is pinned by
      consumer tests and must not move). The round-13 torture suite becomes **table-generic**: the four
      invariants run over both instances from one parameterized spec, so any future table inherits its
      torture coverage on arrival.
Regression/architecture tests: existing torture tests re-pointed through the generic (green before/after);
      a parity contract test generated from one spec asserting read-vs-update tables expose identical
      semantics per operation; ARCH-01 layering scan unchanged.

## [R26-04] conversions: one declarative ConversionPolicy table, three consumers

What: unify the scattered conversion knowledge — `WidenPairs` (`CollectionConverter.cs:21`), the char
      policy cells, the checked-narrow classes, the R25 blit allowlist — into a single declarative
      table: `(src, dst) → { class: Blit|Widen|Checked|Refuse, diagnostic, simdEligible }`.
Why:  today the same fact ("int→long is lossless") lives in the widen table, the emission branch, the
      matrix tests, and prose docs — four places that can disagree silently. The R25 work is about to
      *add* rows (Guid, Nullable, decimal, refusals); adding them four times each is how matrices rot.
How:  one internal static table in the generator; emission dispatches on it; the golden conversion
      matrix (REG-03) is *generated from it* and diffed against measured behaviour — so the table
      claiming a cell and the generator doing it are cross-checked; DocTooling renders the consumer
      docs table from the same source (the docs-regenerator pattern already exists and is fail-loud).
Regression/architecture tests: REG-03 golden becomes table-vs-behaviour differential (the strongest
      form: the spec is executable); a scan banning `SpecialType`-pair literals in emission code outside
      the table file — the single-source property enforced structurally, ARCH-03 style.

## [R26-05] diagnostics: split the 1,537-line descriptor file by category with owned id ranges

What: `DiagnosticDescriptors.cs` → partial family by area (Members, Conversions, Projection, Flatten,
      Registry, Suppression…), each owning a **disjoint id range** declared in a header comment.
Why:  1,537 lines [MEASURED], 90+ descriptors, and every sync mechanism (AnalyzerReleases, docs,
      wording pins) treats it as one unit — which is correct and must survive the split; but finding
      the right neighbourhood for DWARF09x currently means scrolling, and id allocation is tribal
      knowledge.
How:  mechanical partial split; ARCH-03's single-point-of-declaration scan relaxes from "one file" to
      "the descriptor partial family only"; a new scan asserts each partial's ids fall in its declared
      range and ranges are disjoint — allocation stops being tribal.
Regression/architecture tests: ARCH-03 (adjusted), the range-ownership scan, and the existing
      completeness pins (REG-05, AnalyzerReleases sync) all green across the split — which they will be
      by construction since ids and wordings do not change.

## [R26-06] contributors: the test-helper vocabulary as documented, scanned surface

What: a `docs/testing-vocabulary.md` table of the house assertion helpers (`Col.*`, `R.Check`,
      `NoErrors`, `GeneratorAssert.*`, `Has*/Expect*`, the torture `RunAll/Mint` idioms) — what each
      asserts and when to use it — kept honest by a scan.
Why:  the vocabulary is excellent and *undiscoverable*: the round-16 assertion-forensics needed three
      widening passes just to learn the helper names, and a new contributor faces the same wall with
      less patience. Undocumented idioms get bypassed, and bypassed idioms are how `Assert.True(true)`
      tests eventually appear.
How:  generate the skeleton from the helper classes' XML docs (DocTooling again); the scan asserts
      every public helper in the test-support assemblies appears in the table (fail-loud regenerator
      pattern, same as the docs-current tests).
Regression/architecture tests: the scan is the test; the assertion-forensics allowlist (round-16's
      instrument) is re-pointed at the generated table — the audit instrument and the contributor doc
      become the same artifact, which is the definition of documentation that cannot rot.

---

Landing order: R26-02 context first (it is the enabler), then R26-01 phase-by-phase with the byte-identity
lock, R26-03 and R26-05 as independent one-day items, R26-04 when the R25 rows land (its natural trigger),
R26-06 whenever DocTooling has a free evening. Nothing in this round changes behaviour; everything in it
changes what the *next* behaviour change costs — which, at a measured +514 lines per audit interval on the
hot method alone, is the round's entire justification.

---

# v2 addendum — proposed code per entry (kernel-RFC convention: written in house idioms, NOT compiled
# against `fac40f3`; substitution seams marked `// SEAM:`; every test names the change that turns it red)

## [R26-01] code: the byte-identity refactor lock + phase ceiling

```csharp
// SPDX-License-Identifier: GPL-2.0-only
// SelfValidation/RefactorLockTests.cs — the merge criterion for every R26 commit, as a test.
// Complements the golden fingerprints: this locks the WHOLE corpus in one manifest so a structural
// commit can assert "zero bytes moved" in a single red/green signal.

public sealed class RefactorLockTests
{
    [Fact]
    public void Corpus_wide_output_manifest_is_unchanged()
    {
        var sb = new StringBuilder();
        foreach (var (name, src) in RefactorCorpus.All)        // SEAM: reuse golden corpus enumeration
        {
            var (_, gen) = GeneratorTestHarness.Run(src);
            sb.Append(name).Append(':')
              .Append(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(gen)))).Append('\n');
        }
        GoldenAssert.MatchesManifest("refactor-lock.manifest", sb.ToString());
        // SEAM: GoldenAssert = the existing golden/bless-guard helper (CI refuses bless — round-5 idiom).
    }
}
```

```csharp
// SelfValidation/StructureRatchetTests.cs — ARCH-05 realized; ceilings ratchet DOWN as R26-01 lands.
public sealed class StructureRatchetTests
{
    // (file, method) -> max body lines. Start at measured+ε; every extraction commit lowers its row.
    private static readonly Dictionary<string, int> Ceilings = new()
    {
        ["MapperExtractor.cs::ExtractCore"] = 2_800,   // today: 2,751 — growth fails FIRST
        // post-split target rows land as phases extract: ["MapperExtractor.Phases.cs::BindMembers"] = 320, …
    };

    [Fact]
    public void No_method_exceeds_its_ceiling()
    {
        foreach (var (key, ceiling) in Ceilings)
        {
            var lines = RoslynBodyLines(key);              // SEAM: syntax-walk helper, round-14 measurer
            Assert.True(lines <= ceiling,
                $"{key} = {lines} lines > ceiling {ceiling}. Extract a phase (R26-01) — do not raise the ceiling.");
        }
    }
}
```
Red-when: any emitted byte moves during a structural commit; ExtractCore (or any phase) grows past its row.

## [R26-02] code: ExtractionContext + the parameter-ceiling dual

```csharp
// SPDX-License-Identifier: GPL-2.0-only
// Pipeline/ExtractionContext.cs — ONE construction site, NO defaults anywhere: 044's totality, kept.
internal sealed record ExtractionContext(
    Compilation Compilation,
    OptionSet Options,
    MapperDefaults Defaults)                                // SEAM: existing defaults model
{
    internal static ExtractionContext Create(Compilation c, AttributeModel m) => new(
        c,
        new OptionSet(
            AllowNonPublic:      m.AllowNonPublic,          // every option decided HERE, once,
            AutoNest:            m.AutoNest,                // compiler-enforced by positional-required
            ExplicitOnly:        m.ExplicitOnly,            // record parameters — no partial construction
            IgnoreObsolete:      m.IgnoreObsolete,
            CaseInsensitive:     m.CaseInsensitive,
            ImplicitConversions: m.ImplicitConversions),
        m.Defaults);
}
internal readonly record struct OptionSet(
    bool AllowNonPublic, bool AutoNest, bool ExplicitOnly,
    bool IgnoreObsolete, bool CaseInsensitive, bool ImplicitConversions);
// Migration shape:  before: Resolve(..., compilation, allowNonPublic, autoNest, explicitOnly, ...)
//                   after:  Resolve(..., in ExtractionContext ctx)   // 16 params -> ~5
```

```csharp
// SelfValidation/ResolverParameterCeilingTests.cs — REG-02's dual: the bloat era cannot return.
[Fact]
public void No_pipeline_resolver_exceeds_the_parameter_ceiling()
{
    var offenders = new List<string>();
    foreach (var file in Directory.EnumerateFiles(RepoPaths.PipelineDir, "*.cs"))
        foreach (var m in ParseMethods(file))               // SEAM: same Roslyn walk as REG-02's scan
        {
            int allowed = LegacyAllowance.TryGetValue($"{Path.GetFileName(file)}::{m.Identifier}", out var a)
                ? a : 6;                                    // new code: hard 6; legacy rows ratchet down
            if (m.ParameterList.Parameters.Count > allowed)
                offenders.Add($"{file}::{m.Identifier} = {m.ParameterList.Parameters.Count} > {allowed}");
        }
    Assert.True(offenders.Count == 0,
        "Thread state through ExtractionContext, not new parameters:\n  " + string.Join("\n  ", offenders));
}
private static readonly Dictionary<string, int> LegacyAllowance = new()
{ ["MapperExtractor.Projection.cs::ResolveProjectionCtorExpr"] = 16, /* shrink with each migration */ };
```
Red-when: a resolver gains a parameter instead of a context field; a migrated method regrows its list.

## [R26-03] code: RegistryTable<TDelegate>

```csharp
// SPDX-License-Identifier: GPL-2.0-only
// src/DwarfMapper/RegistryTable.cs — the four mirrored dictionaries become one implementation, twice.
internal sealed class RegistryTable<TDel> where TDel : class
{
    private readonly ConcurrentDictionary<Key, TDel> _maps = new();
    private readonly ConcurrentDictionary<Key, byte> _ambiguous = new();

    public void Register(Type s, Type d, TDel del)
    {
        var key = new Key(s, d);
        if (!_maps.TryAdd(key, del)) _ambiguous.TryAdd(key, 1);   // first-wins + loud ambiguity: ONCE
    }
    public bool TryGet(Type s, Type d, out TDel? del) => _maps.TryGetValue(new Key(s, d), out del);
    public bool IsProvided(Type s, Type d) => _maps.ContainsKey(new Key(s, d));
    public bool IsAmbiguous(Type s, Type d) => _ambiguous.ContainsKey(new Key(s, d));
    public TDel? ResolveWithWalk(Type runtime, Type d) { /* SEAM: existing base/interface walk moves here */ return null; }
}
// DwarfMapperRegistry keeps its exact public surface as thin forwarders:
//   static readonly RegistryTable<Func<object, object>>   Read   = new();
//   static readonly RegistryTable<Action<object, object>> Update = new();
```

```csharp
// IntegrationTests: torture goes TABLE-GENERIC — four invariants, one spec, both instances.
public static IEnumerable<object[]> Tables() // SEAM: adapters exposing Register/TryGet/IsAmbiguous per table
{
    yield return new object[] { RegistryAdapters.Read };
    yield return new object[] { RegistryAdapters.Update };
}
[Theory, MemberData(nameof(Tables))]
public void Same_pair_race_first_wins_ambiguity_marked(ITableAdapter t) { /* round-13 body, via t */ }
// + distinct-pairs, churn-monotonic, walk-switches-once — all four re-pointed through the adapter.
```
Red-when: read and update tables diverge in any operation's semantics; a future table family ships
without inheriting the four invariants (its adapter row is one line — absence is visible in review).

## [R26-04] code: the ConversionPolicy single source

```csharp
// SPDX-License-Identifier: GPL-2.0-only
// Pipeline/ConversionPolicy.cs — one row per (src,dst); emission, golden test, and docs all read THIS.
internal enum ConvClass { Blit, Widen, Checked, Refuse }
internal sealed record ConvRule(SpecialType Src, SpecialType Dst, ConvClass Class,
                                string? DiagnosticId, bool SimdEligible);
internal static class ConversionPolicy
{
    internal static readonly ConvRule[] Table =
    {
        new(SpecialType.System_Int32,  SpecialType.System_Int64,  ConvClass.Widen,   null,       true),
        new(SpecialType.System_Int64,  SpecialType.System_Int32,  ConvClass.Checked, "DWARF038", false),
        // SEAM: fold WidenPairs (CollectionConverter.cs:21), char-policy cells, R25 blit rows — then
        // DELETE the originals; the scan below makes resurrection impossible.
    };
}
```

```csharp
// SelfValidation: the single-source property, enforced two ways.
[Fact] public void Golden_matrix_is_generated_from_the_policy_table()
{   // REG-03 upgraded: spec-vs-behaviour differential — for every row, run a probe pair and assert the
    // measured class (blit emitted / widen kernel / DWARF id / refusal) matches the row. The table
    // cannot claim what the generator doesn't do, and vice versa.
}
[Fact] public void No_SpecialType_pair_literals_outside_the_policy_file()
{   // scan src/DwarfMapper.Generator for `SpecialType.System_*` appearing pairwise in conversion
    // branches outside ConversionPolicy.cs (allowlist: the policy file itself). ARCH-03 style.
}
```
Red-when: a conversion fact is added anywhere except the table; the table and measured behaviour disagree.

## [R26-05/06] code: id-range ownership + vocabulary scans (compressed)

```csharp
[Fact] public void Each_descriptor_partial_owns_a_disjoint_declared_id_range()
{   // parse `// ids: DWARF060-DWARF079` header per partial; assert every descriptor's numeric id falls
    // in its file's range, ranges are disjoint, and no descriptor lives outside the partial family.
}
[Fact] public void Every_public_test_helper_is_documented_in_the_vocabulary_table()
{   // reflect public members of the test-support assemblies; assert each appears in
    // docs/testing-vocabulary.md (generated skeleton, fail-loud regenerator pattern — write then FAIL).
    // This table IS the round-16 assertion-forensics allowlist: instrument and doc, one artifact.
}
```
Red-when: DWARF09x lands in the wrong partial or outside a declared range; a helper ships undocumented
(and thereby invisible to the assertion-forensics instrument).

---

v2 status recap: all code above is RFC-grade — house idioms, uncompiled, seams marked. The byte-identity
lock (R26-01's manifest test) is deliberately the FIRST code to land: it is the harness that makes every
other patch in this round reviewable as "structure moved, bytes did not," which is the entire safety
argument of an architecture round.
