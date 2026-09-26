# DwarfMapper.NET — what to do after round 30 merges (research, 2026-09-22)

State at time of writing [MEASURED]: `feat/round30` (3fa3fcb) is **not yet merged**; master = 4b18afd (round 29,
PR #4). Already adopted on round 30 (verified by grep, so not re-proposed): PackageValidation with baseline
1.0.2-rc.1, NuGetAudit mode=all level=low + lock files + locked-mode CI, Sigstore provenance + SBOM, SHA-pinned
actions, CompilerTests with R22-00/01/02 (TypeGraph, DifferentialOracle, Metamorphic) + compile-cost test,
Verify snapshots, Fix-All providers, help links, incremental step tracking, Stryker in CI.
Absent: interceptors, NuGet trusted publishing (push is manual by design), zizmor/harden-runner, EF-backed
projection tests, nullability-attribute handling, C# 15 awareness.

Labels: MEASURED = run in container this session; EVIDENCE = external source; INFERRED = reasoning, verify first.

---

## Tier A — hardening (small, evidence-backed, do first)

### [A1] projection: `x == null` emits a user-operator call for records and value objects  [MEASURED]
What: the projection emitter builds null guards as `{srcExpr} == null` (Projection.cs:908, :925, :1235, :1742).
Why:  in an expression tree, `==` on a type with a user `operator ==` compiles to `Equal(method=op_Equality)`,
      not reference equality. Measured on SDK 10.0.101: record → `op_Equality`; class with user operator →
      `op_Equality`; plain class → reference equality; `(object)x == null` → reference equality. **Also measured while
      preparing the task list:** a nested type declaring two `operator ==` overloads makes the generated projection
      fail to compile (CS0034, ambiguous operator on `V` and `<null>`) with no DWARF diagnostic — the same cast fixes it. Records are
      the default shape for DTOs, value objects and EF Core complex types, so the most common nested-member
      shape puts a *user method call* into every generated projection. LINQ-to-objects runs user code; SQL
      providers must recognise the call or fail translation (INFERRED — needs the EF rig). The same site is
      where Mapperly just fixed CS9342 ambiguous null checks for types with several equality operators
      (5.0.0-next.9, #2324). `is null` is not an option here — pattern matching is illegal in expression trees.
How:  emit `(object){srcExpr} == null` for reference-typed operands (keep `== null` for Nullable<T>, where it
      is already a HasValue check). One helper, four call sites.
Regression tests: (1) expression-shape test — compile a projection whose nested member is a record, walk the
      tree, assert no `BinaryExpression` with `Method != null` among null guards; (2) CS9342 row — a nested type
      with two `operator ==` overloads must compile; (3) EF Core + SQLite translatability row for a record-typed
      complex/owned member (the ISSUE-046 rig pattern).
Red when: any generated null guard routes through a user operator.

### [A2] supply chain: blanket `NoWarn NU1903` → advisory-scoped `NuGetAuditSuppress`  [MEASURED + EVIDENCE]
What: DifferentialTests, CleanCorpus and Benchmarks suppress *every* High advisory (`<NoWarn>$(NoWarn);NU1903`)
      to tolerate AutoMapper 14.0.0 (GHSA-rvv3-g6hj-g44x, confirmed High by `dotnet list package --vulnerable`).
Why:  a future High advisory in Mapster, Mapperly, BenchmarkDotNet or any transitive dependency of those three
      projects will now pass silently — the exact vacuous-green class the audit gate's own comment forbids.
      NuGet supports per-advisory suppression by URL since the .NET 8.0.400 SDK.
How:  `<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-rvv3-g6hj-g44x" />` in those three
      projects; delete the NU1903 NoWarn. Keep the existing licensing comment (v15+ is RPL-1.5).
Regression test: SelfValidation scan — no `NU190[1-4]` inside any `<NoWarn>`; audit suppressions must be
      `NuGetAuditSuppress` items naming an advisory URL, each with an adjacent reason comment.

### [A3] security claim: pin the AutoMapper CVE-2026-32933 PoC against DwarfMapper  [EVIDENCE]
What: AutoMapper's High DoS (Mar 2026) is uncontrolled recursion: ~25–30k nesting levels overflow the stack and
      kill the process. The advisory's recommended mitigation is a default max depth with a configurable limit.
Why:  DwarfMapper already ships exactly that (`MaxDepth = 64`, catchable `DwarfMappingDepthException`). It is a
      real differentiator for teams leaving AutoMapper — but only if it is pinned as a claim, not implied.
How:  port the advisory PoC verbatim (self-referential `Circular`, 30,000 levels) across every ReferenceHandling
      mode, plus collection- and dictionary-routed recursion; add the row to SECURITY.md's claim register and a
      paragraph to MIGRATION.md.
Regression test: the PoC must end in `DwarfMappingDepthException` with the process alive, for each mode.

### [A4] release: NuGet Trusted Publishing without losing the manual gate  [EVIDENCE]
What: push is manual with a long-lived API key "for deliberate control".
Why:  nuget.org Trusted Publishing exchanges a GitHub OIDC token for a ~1-hour single-use key — no stored secret.
      Deliberate control is kept with a GitHub Environment that has required reviewers.
How:  `NuGet/login@v1` (SHA-pinned) in a `publish` job bound to a `release` environment; delete the key.
Regression: zizmor (A5) flags any reintroduced long-lived NuGet secret.

### [A5] CI: zizmor + Harden-Runner (audit first)  [EVIDENCE]
Why:  SHA pinning (already done) stopped tj-actions (CVE-2025-30066) but not `pull_request_target` or template
      injection classes; the March 2026 trivy-action compromise was exactly that. zizmor catches these statically;
      Harden-Runner in audit mode records egress so publish jobs can later be locked to an allow-list.
How:  zizmor → SARIF, reporting mode, then blocking after baseline triage; Harden-Runner audit on every job.

### [A6] semantics: honour nullability attributes  [INFERRED — probe first]
What: no generator code references `MaybeNull/AllowNull/NotNull/DisallowNull`.
Why:  a `[MaybeNull] string Name` is annotated non-nullable but may return null; mapped to a non-nullable
      destination the null-into-non-nullable warning (DWARF070 class) would not fire — a silent path. Mapperly
      fixed this exact pair in 5.0.0-next.9 (#2333, #2334).
How:  probe first; if silent, fold the attributes into the member nullability model.
Regression test: NegativeCases row per attribute, including attributes on members from a referenced assembly.

---

## Tier B — structural, from the round-30 review

### [B1] ExtractionContext + parameter-ceiling ratchet  [MEASURED]
Wide signatures went 30 → 47 when ExtractCore was split into 20 partials without a context object (worst 16).
Code already drafted in the R26 v2 addendum; land it under the byte-identity lock.

### [B2] CS8795 companion noise: the only real fix is a throwing stub  [EVIDENCE]
A `DiagnosticSuppressor` cannot help: suppressible diagnostics must not be errors, and CS8795 is an error.
The option is a per-method stub for refused methods (`=> throw new DwarfMappingNotGeneratedException("DWARFnnn…")`)
so only the DWARF error shows and clean sibling methods stay green. Trade-off to decide deliberately: it breaks the
current "refusal ⇒ genLen = 0" invariant that several tests pin. If adopted, the new invariant is "refused methods
emit only a stub that throws with the diagnostic id", pinned per refusal id.

---

## Tier C — strategic (language and platform timeline)

Dates [EVIDENCE]: .NET 11 / C# 15 GA **2026-11-10**, STS, supported to 2028-11-09. .NET 10 is LTS.
Consequence: the net10.0-only runtime TFM needs no change — net10 libraries run on net11. What does change is
the compiler consumers will run the generator inside.

### [C1] CI leg on the .NET 11 SDK (forward compatibility)
Build the corpus with the .NET 11 preview SDK. The generator is a netstandard2.0 analyzer loaded into whatever
Roslyn the consumer has; C# 15 syntax in consumer models is the risk, not the TFM.

### [C2] C# 15 closed hierarchies → inferred `[MapDerivedType]` arms  [EVIDENCE + INFERRED]
A `closed` base allows direct derivation only inside its assembly, so its direct descendants are a known,
complete set. System.Text.Json already uses this to infer derived types instead of requiring registration.
Proposal: for a closed source base, infer the arm set and report a new DWARF error when a direct descendant has
no arm — compile-time exhaustiveness, i.e. the doctrine applied to polymorphism.
Catch [INFERRED]: reading `closed` needs a Microsoft.CodeAnalysis newer than the 5.0.0 floor (SDK 10.0.1xx). That
is the first concrete trigger for versioned analyzer folders (`analyzers/dotnet/roslyn5.0/` + a newer folder)
instead of raising the floor.

### [C3] C# 15 union types → refuse loudly first
Until a mapping policy exists, a union-typed member should produce a dedicated DWARF refusal rather than falling
into generic member enumeration. Pin it before .NET 11 GA so no consumer ever sees silent behaviour.

### [C4] Interceptors for registry-style call sites — prototype, don't commit  [EVIDENCE + INFERRED]
Interceptors are stable from SDK 9.0.2xx with the opaque `InterceptableLocation` API (the path-based preview form
is obsolete); every DwarfMapper consumer already has a new-enough SDK. Call sites of the ambient registry inside
the consuming compilation could be rewritten to direct calls into the generated mapper: static dispatch, no
dictionary lookup, trivially AOT-safe. Limits: only call sites compiled with the generator are rewritten, so the
registry must stay for cross-assembly and reflection callers; the package must add its namespace to
`InterceptorsNamespaces` through build props. Gate adoption on a benchmark showing the lookup is a measurable share
of real mapping cost — for small DTOs the dictionary hit is probably below noise.

### [C5] Market position after AutoMapper went commercial
AutoMapper went commercial on 2025-07-02 (v15+), and 14.x keeps the unpatched CVE-2026-32933; practitioner guides
now default new .NET 10 work to Mapperly. DwarfMapper's differentiators for that audience are depth safety (A3),
refusals where others are silent, and projection translatability diagnostics. A "from AutoMapper" migration page
with the CVE angle is the cheapest reach lever available.
Free probe list: Mapperly 5.0.0-next.9 fixes are a list of bugs a mapper can have — CS9342 (A1),
nullability attributes (A6), upcast parenthesisation when inlining `Use=` in projections (#2341), generic user
mapping overriding explicit mappings (#2339), derived types with generic targets (#2230). Turn each into a
DwarfMapper differential or NegativeCases row.

---

## Not recommended (falsified or not worth it)
- DiagnosticSuppressor for CS8795 — cannot suppress errors (B2).
- Adding a net11.0 TFM — no benefit; net10 assemblies run on net11 (C1 covers the real risk).
- Replacing the ambient registry with interceptors — cannot reach cross-assembly or reflection callers (C4).

## Suggested order after merge (v1 — superseded, see end of file)
1. A1, A2, A3 (one short hardening commit set — two measured, one pinned claim)
2. B1 (enabler for everything structural)
3. A6 probe, then A4 + A5
4. C1 CI leg now; C3 before 2026-11-10; C2 once the Roslyn packaging decision is made
5. C4 as a benchmark-gated prototype; C5 docs in parallel

---

## Tier D — coverage-test audit of round 30  [MEASURED]

Scope: every test file whose path or name says "coverage" on `feat/round30` — 133 files, 570 tests, 12,900 lines
(118 files in `Generator.Tests/Coverage/`, 114 of them new in round 30). Method: parse every `[Fact]/[Theory]`
body and classify its assertions, then read every test the instruments flagged.

Verdict: **they make sense.** 568 of 570 assert a real behaviour or contract. What the instruments found:

| check | result |
|---|---|
| tests with no assertion | 0 real — the 6 candidates call local assertion helpers (`AssertDwarf005NotDwarf033`, `AssertUntranslatable`, `AssertRefusedOnMemberI…`) |
| try/catch in a test body | 0 |
| test name claims a DWARF id the body doesn't assert | 0 |
| tests whose name describes silence ("ignored", "drops", "not reported") | 10 — every one asserts the deliberate or loud outcome; none pins a silent defect as expected |
| existence-only assertions (`NotNull`/`Null`/no-throw) | 16 — 14 are genuine "returns null" contracts; 2 are weak (D1) |
| duplicate of a test outside the coverage folder | 1 (D2) |
| assertions on emitted internal identifiers (`__dwarf_target`, `__ra`, …) | 67 in 27 files (D3) |
| unit tests calling internal helpers directly | 147 in 45 files — none call a ≥7-parameter method, so B1 does not break them (a first count of 16 was a false match on LINQ `Select`; retracted) |
| files stating why they exist (branch, mutant, issue) | 3 of 118 (D4) |

### [D1] strengthen two position tests in `LocationInfoCoverageTests`
`From_location_with_zero_length_span_does_not_throw` asserts only that nothing was thrown;
`From_location_at_end_of_file` asserts only non-null. The second does catch a `>` → `>=` slip in the bounds guard,
but neither checks the position itself — the one thing consumers see (where the squiggle lands).
Fix: assert `FilePath`, `SourceSpan` and the mapped line/column, and that `ToLocation()` round-trips.

### [D2] remove one duplicate
`ResolveMembersDirectiveCoverageTests.A_shared_member_that_is_also_ignored_is_refused` uses the same source,
verbatim, as a test in `IgnoreConflictDirectiveNameTests`. Keep the one that pins wording; delete or retarget the other.

### [D3] decouple 67 assertions from emitted local names
Asserting `Contains("__dwarf_target.A = src.A;", generated)` breaks on a pure rename of a generated local while
behaviour is unchanged. Under the byte-identity lock this is tolerable, but it is coupling that grows with every
round. Prefer executing the generated mapper and asserting the result, or a helper that normalises `__`-prefixed
locals before comparing.

### [D4] one-line provenance per coverage file
Only 3 of 118 files say which branch, mutant or issue they exist for; the rest rely on commit subjects. Codecov's
patch gate (`target: auto`, `threshold: 0%`) rewards hitting lines, so the "why" is what keeps a future reader from
deleting a test that looks redundant. Convention: a `// Covers:` header naming the method and branch, checked by a
SelfValidation scan.

---

## Tier P — performance  [MEASURED, standalone bench, SDK 10.0.101, median of 9, single core — ratios are the result]

Already landed on round 30 (verified, not re-proposed): the R25 enum/struct blits (`MemoryMarshal.Cast`, 17 emission
sites), `CollectionsMarshal.SetCount`/`AsSpan` list paths, and pre-sizing of direct collection mappings via
`TryGetNonEnumeratedCount` (the merged `perf/presize-collection-targets` branch).

### [P1] auto-registered collection shapes in the registry never got the pre-size work
What: `AggregateEmitter.cs:421-428` emits `new List<T>()` (no capacity), a `foreach` over `IEnumerable<S>`, and
`.ToArray()` for array targets — while `CollectionConverter.cs:692` already pre-sizes the direct path.
Measured (source `List<S>` passed as `object`, per-element DTO map):

| n | array: current → pre-sized | array: + concrete-type span loop | list: current → pre-sized | allocation (array) |
|---|---|---|---|---|
| 16 | 1.49x | 3.01x | 1.20x | 1032 → 704 B |
| 1,024 | 1.07x | 2.81x | 1.08x | 57.6 → 41.0 KB |
| 65,536 | 1.43x | 3.80x | 1.95x | 3.67 → 2.62 MB |

The span loop wins most because the registration is keyed on `IEnumerable<S>`, so the current `foreach` goes through
a boxed interface enumerator per call.
How: emit the same `TryGetNonEnumeratedCount` pre-size as the direct path, plus a fast path for `S[]` and `List<S>`
(`CollectionsMarshal.AsSpan`); keep the buffered path for true streams. Register-before-fill ordering for Preserve
must stay as it is on the direct path.
Regression tests: SIMD-parity-style differential — current and new emission produce equal results for array, list,
`HashSet`, lazy iterator and empty sources; an allocation assertion (`GC.GetAllocatedBytesForCurrentThread`) on the
array target; the R25-05 ratio gate at n = 1,024.

### [P2] resolve registry pairs at compile time; keep the lookup only for runtime-typed calls  [MEASURED]
Where the cost is: injected mapper classes already call generated code directly. The lookup only hits the
`IDwarfMapper` facade and the ambient registry.

**P2a — `Map<TSource,TDestination>`: both types are static, so the lookup is avoidable.** Today it hashes
`typeof(TSource), typeof(TDestination)` into a `ConcurrentDictionary` and calls an `object→object` delegate (boxing
value-type sources). Its documented semantics are already "use the static pair".
How: the generator emits, in the existing `[ModuleInitializer]`, a typed bind per pair —
`DwarfPair<S, D>.Bind(Mapper.Map)` into `static Func<TSource,TDestination>? Map`, first-wins via
`Interlocked.CompareExchange` (a `false` return is the ambiguity signal, marked exactly as today). The facade reads
the static slot and falls back to the registry only when the slot is empty (runtime `Register` calls, late loads).
Measured, small DTO: today 120 ns (class source) / 143 ns (struct source); typed slot 39 ns / 40.4 ns — 3.1x / 3.5x;
struct-source overhead vs a direct call 0.9 ns and allocation 64 → 32 B (boxing gone). The remaining ~15 ns for
class sources is generic sharing over reference types (static-field access through the runtime generic dictionary)
plus the delegate call.
Regression tests: every emitted pair binds its slot (generated-code scan); second bind of a pair reports ambiguity
exactly as `Register` does; `Map<Base,Dto>(derived)` still uses the static pair; torture: bind-while-reading never
serves a wrong delegate; ratio gate ≥2.5x vs the registry path.

**P2b (revised) — `Map<TDestination>(object)`: generate a static dispatcher per destination; the cache is only for
what compile time cannot see.**  [MEASURED, model at 500 pairs]
The runtime type of `source` is unknown at compile time, but the *set of sources that map to each destination* is
known to the compilation that sees the whole reference graph — the app marked
`[assembly: DwarfMapperValidationRoot]`, which already verifies that graph. That generator emits one dispatcher per
destination, a type switch in registry precedence order (exact/most-derived first, then bases, then interfaces), and
binds it into a static slot (`Dispatch<TDestination>.Fn`) from its module initializer:

```csharp
static OrderDto ToOrderDto(object src) => src switch
{
    Order o                 => OrderMapper.Map(o),        // also catches EF/Castle proxies: they derive from Order
    _                       => (OrderDto)DwarfMapperRegistry.Map(src, typeof(OrderDto)),   // residue only
};
static List<OrderDto> ToOrderDtoList(object src) => src switch
{
    IEnumerable<Order> e    => OrderMapper.MapList(e),
    _                       => (List<OrderDto>)DwarfMapperRegistry.Map(src, typeof(List<OrderDto>)),
};
```

| call (app with 500 pairs) | direct call | today | static dispatch | gain | overhead vs direct |
|---|---|---|---|---|---|
| single object | 16 ns | 153 ns | 19 ns | 7.9x | 4 ns |
| runtime proxy of a mapped type | 17 ns | 216 ns | 19 ns | 11.1x | 2 ns |
| 8-element `List<S>` | 777 ns | 16.6 µs | 788 ns | 21.0x | 11 ns |

Cost is per destination, not per app, so it stays flat as pairs are added — and it beats the memo (34 ns single,
~1.1 µs list). It is also *more correct* than the registry:
- two referenced assemblies providing the same pair, or a source type matching two registered interfaces for the
  same destination, becomes a **build error at the root** instead of a first-wins outcome decided by module-initializer
  load order;
- arm order is checked by the C# compiler itself (an arm subsumed by an earlier one is CS8120).

What stays dynamic, and only this:
1. assemblies loaded at runtime that the root did not reference (plugins, `AssemblyLoadContext`) — the `_` arm falls
   back to the registry, now reached only on a true miss;
2. apps with no validation root — a library compiled alone cannot know the final set of assemblies, so each assembly
   binds its own pairs and the registry composes them at runtime as today;
3. `Map(object, Type)` with a runtime `Type` value — one lookup is irreducible when the destination itself is data;
   use a `FrozenDictionary<Type, …>` built once by the root, and steer callers to the generic overloads.
For those three, the version-invalidated memo from the earlier draft is the right tool — as the fallback, not the design.

Regression tests: precedence parity — for every (runtime type, destination) in the corpus the static dispatcher and
the registry return the same mapper, including proxies and interface-only sources; cross-assembly conflict at the root
is a DWARF error (NegativeCases pin); a dynamically loaded assembly's pair still maps through the `_` arm; ratio gate
on the 500-pair case; flat-cost gate (500-pair cost ≤ 1.2x 10-pair cost).

**P2c — interceptors (C4) are the only fully compile-time form.** For `Map<TS,TD>` call sites in the consumer's own
compilation, the generator can rewrite the call to the generated method: zero overhead. For `Map<TD>(object)` the
rewrite is sound only when the static source type is sealed, a struct, or a C# 15 closed hierarchy the generator can
see completely — otherwise a derived type from another assembly could need a different map. P2a gets most of the
gain without the opt-in, so it goes first; interceptors remove the last ~15 ns where call sites allow it.

### [P4] other runtime resolutions — audited for the same compile-time treatment  [MEASURED unless marked]

Method: every lookup, reflection-style call and `GetType()` in the runtime library and in the generator's emission
strings on `feat/round30`, then a faithful model of the interesting path benchmarked (same loop, same checks).

**The big one — collection sources fall into a linear interface scan that grows with the application.**
Each mapped pair auto-registers six collection shapes, all keyed on `IEnumerable<S>` (an interface), so every pair
adds six entries to `_interfaceMaps`. A collection passed to the facade never hits an exact key, so each call does
`GetType()`, an exact miss, the base-type walk, then scans every interface entry
(`DwarfMapperRegistry.cs:195-215`). 8-element `List<S>` → `List<D>` through the facade:

| mapped pairs in the app | interface entries | today | with P2b memo | with typed slot |
|---|---|---|---|---|
| 10 | 61 | 1.9 µs | 1.8x faster | 1.8x |
| 100 | 601 | 7.8 µs | 7.2x | 7.6x |
| 500 | 3,001 | 15.4 µs | 14.2x | 14.9x |

(the mapping itself costs ~1 µs here). This is a per-call tax that rises as the consumer adds DTOs — more important
than the single-object ~85 ns, so **P2b moves ahead of P2a**. The fix is the static per-destination dispatcher in
P2b (revised): 21x at 500 pairs and flat in app size; the memo columns above are kept only as the comparison.
Regression tests: a registry with 500 pairs must resolve a collection source in time independent of pair count
(ratio gate: 500-pair cost ≤ 1.5x 10-pair cost); ambiguity between two interface matches must still be reported
on the memoised path, not cached as a silent first match.

**Update-into facade `Map<TSource,TDestination>(source, destination)` — yes, same as P2a.** It resolves by the
declared types (`typeof(TSource)`, `typeof(TDestination)` into `UpdateMaps`), which are static at the call site, so a
typed per-pair update slot applies unchanged. Gain inferred from P2a (same lookup shape), measure when landing.

**Registry queries (`TryGet`, `IsProvided`) from generic callers — yes, trivially,** by reading the slot; low value.

**Already resolved at compile time — no work:**
- enum conversions are generated switches (0 `Enum.Parse`/`GetName`/`IsDefined` in emission);
- the depth/reference context is only emitted for recursion-capable pairs (`MapEmitter.cs:283`), so non-recursive
  mappings pay nothing for `MaxDepth`;
- derived-type dispatch is a generated type-pattern switch; the only `GetType()` in emission is inside the error
  message of its fallback arm (C2 can make that arm provably unreachable for closed hierarchies).

**Not worth resolving:**
- `AmbientValidator`'s generated `IsProvided` checks run once at startup, and the validation root already proves the
  graph at compile time;
- the Preserve identity map is inherently runtime (object identity).

**Caution on binding at startup [INFERRED — measure first].** Eager binding in the module initializer instantiates one
generic slot type per pair (and six more per pair if collection shapes were bound too). At hundreds of pairs that is
startup cost paid by every consumer. A lazy read-through slot (`Pair<TS,TD>.Map ??= resolve-with-version`) keeps the
JIT-specialised read path with no startup cost and needs no generator change; prefer it unless startup measurements
show eager binding is free.

### [P5] projections: two cases, two outputs, decided at compile time where possible

Today [MEASURED by reading `feat/round30`]: a method is a projection when it takes `IQueryable<S>`
(`MapperExtractor.Projection.cs:91`); its body is an inline `Queryable.Select` lambda (`MapEmitter.cs:339`) and nested
collections inside it are `Enumerable.Select(...).ToList()` (`Projection.cs:881`). There is no reusable
`Expression<Func<S,D>>` surface for consumers.

**Case 1 — provider projection (EF Core and any LINQ provider).** Output: an expression tree, and only that. Nested
collections stay `Enumerable.Select` *inside* the tree, because the provider turns them into a subquery or join; they
must never become calls to generated mapper methods, which no provider can translate.
- P5a — build the tree once: hoist it into a `static readonly` field (measured: 15.0 µs / 5,400 B → 1.0 µs / 424 B
  per `Project()` call, 14.6x). Closure-free projections only; one that captures a method parameter stays inline.
- P5d — expose it: `public static Expression<Func<Order, OrderDto>> OrderDtoExpression` on the mapper, so a consumer
  writes the EF-native shape `db.Orders.Where(…).OrderBy(…).Select(OrderMapper.OrderDtoExpression)` and composes
  freely, instead of handing the whole query to `Project()`.
- P5c — EF precompiled queries / NativeAOT: EF only precompiles queries it can analyse start to finish at one call
  site. Test both shapes (`Project(q)` and `.Select(OrderDtoExpression)`) with
  `dotnet ef dbcontext optimize --precompile-queries`; whichever is skipped gets a build-time DWARF warning when the
  consumer references EF Core and sets `PublishAot`, instead of a runtime "query wasn't precompiled" exception.

**Case 2 — general mapping (in memory: `IEnumerable<S>`, lists, arrays, `list.AsQueryable()`).** Output: plain C#
through the general mapping pipeline — the `Select` *is* a collection mapping. Nested objects call the generated nested
mappers; nested collections go through `CollectionConverter`, so they inherit pre-sizing, the enum/struct blits and the
SIMD widenings (for blittable and enum element types the measured R25 gains apply; for DTO elements expect little
difference, since .NET already pre-sizes `Select(...).ToList()` over a `List` — inferred).
- Chosen at compile time by the declared parameter type: `IEnumerable<S>`, `List<S>`, `S[]` parameters already get
  general mapping today.
- Chosen at runtime only for an `IQueryable<S>` parameter whose instance is in memory: one type test
  (`q is EnumerableQuery<S>`) routes to the Case 2 code instead of letting LINQ compile the tree on every enumeration
  (P5b, measured: 10 rows 985 µs → 1.3 µs, 741x; 1,000 rows 1,014 µs → 53 µs, 19x; identical results).
- Semantics are decided per pair at compile time: when projection semantics equal map semantics (the normal case),
  Case 2 **reuses `Map`** — one code path, all its optimisations. Where they differ by design (the pairs already listed
  in `OptionGaps`), the generator emits a projection-semantics twin for that pair only, so in-memory results always
  equal what the tree would return.

Regression tests:
- **Case 1 purity** — walk every generated tree and fail on any `MethodCallExpression` into a generated mapper type
  (an optimisation leaking into the provider path would break translation);
- **parity** — for every corpus projection, the tree executed by LINQ-to-objects and the Case 2 code return equal
  results, including null nested members and empty or null collections;
- **routing** — an `IQueryable` built from a list takes the Case 2 path (emission scan for the type test, plus an
  allocation gate showing no tree compilation);
- **P5d** — the exposed expression is one static instance and composes with `Where`/`OrderBy` in the EF/SQLite rows;
- P5a gates as before (static field referenced; captured parameters honoured across calls).

### [P6] startup registration allocates quadratically  [MEASURED]
What: interface-keyed registrations grow `_interfaceMaps` by allocating a new array one element larger and copying
the old one, under a lock, once per registration (`DwarfMapperRegistry.cs:~115`) — chosen so readers can take a
lock-free snapshot. With six interface-keyed collection shapes per pair, module initializers do this thousands of times:

| pairs | interface entries | time | allocated |
|---|---|---|---|
| 100 | 600 | 2.9 ms | 4.1 MB |
| 500 | 3,000 | 51.9 ms | 103 MB |
| 1,000 | 6,000 | 50.5 ms | 412 MB |

(single-core container; the allocation column is the reliable one, and it is O(n²) — once the array passes 85 KB
every copy lands on the Large Object Heap). This is cold-start cost for every consumer.
How: register per assembly in one batch — the generated module initializer passes all its interface entries at once
and the registry grows the snapshot once per assembly, keeping the lock-free read. With P2b at a validation root the
collection shapes need not be registered at all.
Regression tests: allocation gate — registering 3,000 interface entries allocates O(n) (< 1 MB); batch registration
still detects ambiguity across batches from different assemblies and keeps first-wins order; the round-13 torture
invariants hold for concurrent batches.

### [P7] trim what nobody calls  [INFERRED — measure AOT size first]
The root already collects every `IDwarfMapper.Map` call site (`AmbientRequiresCollector`, used for DWARF061). Module
initializers today reference every mapper's delegate, which roots all of them, so NativeAOT/trimming cannot drop
mappers the app never calls. A root that emits P2b dispatchers only for destinations with call sites — and stops
registering the rest — lets the trimmer remove unused mappers. Evidence to gather before committing: publish a
sample with NativeAOT before/after and compare binary size.

### [P8] smaller ideas, unmeasured — measure before proposing
- `[MethodImpl(AggressiveInlining)]` on small generated nested mappers the JIT may refuse to inline by size;
- a `DwarfRefContext` identity map sized from the source collection count when Preserve maps a collection root;
- `[SkipLocalsInit]` on generated mapper classes that stackalloc or use large locals.

### [P3] A1 removes a user-operator call per null guard in in-memory projections
Not measured separately; listed so the A1 benchmark row is not forgotten when that fix lands.

---

## Suggested order (v2, supersedes the list above)
1. A1, A2, A3 — hardening, one short commit set
2. P1, P5a, P6 — small, mechanical, measured (registry collections pre-size; hoisted projection trees; batch registration); then P5 Case 2 routing (reuse `Map`, twin only for OptionGaps pairs) and P5d expression surface; P5c experiment before any NativeAOT claim involving EF
3. B1 — ExtractionContext + parameter ceiling
4. P2b static per-destination dispatch generated at the validation root (fixes the scan that grows with app size; memo only for plugins, root-less apps and `Map(object, Type)`), then P2a typed slots for map and update, P2c interceptors to remove the last delegate hop
5. D1–D4 — coverage-suite tidy-up (an afternoon)
6. A6 probe, then A4 + A5
7. C1 CI leg now; C3 before 2026-11-10; C2 after the Roslyn packaging decision; C4 only if P2 leaves a measurable gap
