# DwarfMapper.NET — Specification (Plan 19): Complex-type composition

Deep auto-nesting · full collection/dictionary taxonomy · object-graph reconstruction (reference identity) · provably-translatable IQueryable projection.

> This is the **critical** stage: "map any complex datatype." Built grounded in research of Mapperly (Roslyn peer), AutoMapper/Mapster, and EF Core projection-translation rules (see §Research). TDD throughout; holistic + adversarial review; deterministic + property/fuzz tests; defensive coding; every diagnostic is loud (build error), never a silent surprise. SPDX headers; CPM; warnings-as-errors; netstandard2.0 generator / net10 generated code; reflection-free / AOT-safe.

## Owner decisions (locked)
1. **Nested object members → auto-synthesize** a private mapper per `(src,tgt)` pair, **opt-out** via `[DwarfMapper(AutoNest = false)]` (then explicit-declaration as today).
2. **Cycles/recursion → opt-in reference handling**, extended (owner) to **full object-graph reconstruction**: arbitrary topologies (A→B, B⇄C, C⇄D, B⇄D), every distinct node mapped once, all edges relinked; **configurable fidelity** (full vs degraded).
3. **IQueryable projection → extend + prove translatability at compile time**: inline nested member-init + collection `Select().ToList()/.ToArray()` + constructor projection; anything that would throw at query time becomes a **build error**.
4. Collection/dictionary taxonomy → expand to the **common high-value set** + a reusable construction decision tree (exotic types deferred).

## Current state (grounding — from codebase inventory)
- `TryResolveConversion` order: Use → Dictionary → Collection → implicit → `T?→U?` → `T?→U` → `T→U?` → user auto-candidates → Numeric(CreateChecked) → Parsable(IParsable/IFormattable) → Enum → DWARF005.
- Nested objects resolve **only** via user-declared `partial` mapper methods (auto-candidates); **no auto-synthesis**, **no generator-time recursion guard**.
- Collection targets: `T[]`, `List<T>`, `HashSet<T>` only. Dict target: `Dictionary<K,V>` only. Sources: any `IEnumerable<T>` / `IEnumerable<KeyValuePair<K,V>>`.
- Projection (`IsProjection`): **flat only** — direct-assignable members + `[MapProperty]` rename; everything else → DWARF019. No nested/collection projection.
- Diagnostics DWARF001–026 (DWARF004 free). **Next: DWARF027.**
- Fuzzer (`SyntheticSchema`/`BehavioralFuzzTests`/`ObjectFactory`/`StructuralComparer`): scalars, `T?`, `T[]`, `List<T>`, `HashSet<T>` (compile-only), one nested **struct** (`FuzzInner`). **No Dictionary, no nested collections, no nested reference-type objects, no type-divergent collection conversions, no graphs.**

---

# Part A — Auto-synthesized nested object mappers

**Goal.** A member (or ctor param, or collection element, or dict key/value) whose type is a complex object pair `(S, T)` with no implicit conversion and no user-declared mapper is mapped by a **generator-synthesized private instance method** `private T __DwarfMap_Obj_<hash>(S s)`, discovered and emitted automatically. Eliminates per-nested-type boilerplate while preserving completeness (every nested target member is still proven mapped or it's a build error).

**Mechanism (Mapperly-validated): memoized find-or-build + deferred queue.**
- A `NestedMappingRegistry` keyed by `TypeMappingKey(srcFqn, tgtFqn)` (value-equatable strings only — no `ISymbol` in cached models). `FindOrBuild(S,T)`:
  1. If a **user-declared** partial mapper for `(S,T)` exists → use its name (user always wins; no synthesis).
  2. Else if the key is already registered → return its name (dedupe; also the recursion break — see below).
  3. Else **insert the key + reserve the method name BEFORE building the body**, enqueue `(S,T)` for body resolution, return the name.
- Body resolution dequeues and runs the existing member-resolution pipeline (`ResolveMembers` + `ResolveConstructorArguments`) for `(S,T)`, which itself calls `FindOrBuild` for deeper nested members → naturally recursive, terminates because step 2 short-circuits already-seen pairs.
- Generated nested methods are **instance** methods (so they can call sibling nested/auto mappers and respect mapper config), emitted into the partial class, FNV-1a-hashed names like the other synthesizers.

**Generator-time recursion safety (MUST):** the register-before-build (step 3) guarantees the *generator* never infinitely recurses on self-referential types (`Tree { List<Tree> Children }`, `Node { Node Parent }`) — the second encounter hits step 2. Add a hard generator-side depth ceiling (e.g. 100 distinct nesting levels) as a backstop → if exceeded, **DWARF031 (DeepNestingLimit)** with the type path, rather than hang.

**Completeness.** Each synthesized pair runs full completeness: an unmapped nested target member → DWARF001 at the originating method location (carry the member path, e.g. `Order.Customer.Address.Zip`). Read-only/no-conversion nested members → DWARF005/007 as today. So auto-nesting never silently drops data.

**Opt-out.** `[DwarfMapper(AutoNest = false)]` → step 1 only; a nested pair with no user-declared mapper → DWARF005 (today's behavior). Per-mapper.

**Construction strategy for the nested target** reuses Part-18 `ConstructorSelector` (records/immutable/init/settable) — auto-nesting composes with constructor mapping for free.

### Tasks A
- [ ] `NestedMappingRegistry` (find-or-build, deferred queue, dedupe, name reservation) + `TypeMappingKey`.
- [ ] Wire into `TryResolveConversion`: a new branch **after** user auto-candidates and **before** DWARF005 — if both `S`,`T` are mappable named types (class/struct/record, not scalar/collection/enum/string) and `AutoNest` on → `FindOrBuild` and use the synthesized name.
- [ ] Emit synthesized object mappers into the class (extend `MapperClassModel.SynthesizedMethods` flow; they may need ctor-arg + initializer emission → reuse `MapEmitter` construction).
- [ ] `AutoNest` attribute property (default true). DWARF031 depth backstop. DWARF030 reserved (see Part C) — allocate A's at 031.
- [ ] Tests (TDD): 3-level nested class→record graph with zero declared sub-mappers; nested needing conversions (long→int, string→Guid) at depth; nested record + nested class mixed; `AutoNest=false` → DWARF005; user-declared mapper overrides synthesis (assert the declared method is called); unmapped deep member → DWARF001 with path; dedupe (two members same nested type → one synthesized method). Regression: existing declared-mapper tests unchanged.

---

# Part B — Collection / dictionary taxonomy expansion

**Goal.** Map into the common high-value collection/dictionary surface, choosing a concrete type + construction strategy via one decision tree. Element/key/value conversions recurse through the full pipeline (so nested objects, enums, CreateChecked, IParsable, nested collections, `T?` all compose).

**Target taxonomy (this stage):**
| Target | Concrete instantiated | Construction emitted |
|---|---|---|
| `T[]` (✓today) | — | known-count index loop / `Clone` identity / buffer→ToArray |
| `List<T>` (✓today) | — | `new List<T>(cap)`+Add / bulk-ctor identity |
| `HashSet<T>` (✓today) | — | `new HashSet<T>(src)` / Add loop |
| `IEnumerable<T>` / `ICollection<T>` / `IList<T>` / `IReadOnlyList<T>` / `IReadOnlyCollection<T>` | `List<T>` | Add loop / bulk |
| `ISet<T>` / `IReadOnlySet<T>` | `HashSet<T>` | Add loop |
| `ImmutableArray<T>` | — | `ImmutableArray.CreateRange(mapped)` (or builder) |
| `ImmutableList<T>` / `IImmutableList<T>` | `ImmutableList<T>` | `ImmutableList.CreateRange` |
| `ImmutableHashSet<T>` / `IImmutableSet<T>` | `ImmutableHashSet<T>` | `ImmutableHashSet.CreateRange` |
| `Dictionary<K,V>` (✓today) | — | capacity + indexer loop |
| `IDictionary<K,V>` / `IReadOnlyDictionary<K,V>` | `Dictionary<K,V>` | indexer loop |
| `ImmutableDictionary<K,V>` / `IImmutableDictionary<K,V>` | `ImmutableDictionary<K,V>` | `ImmutableDictionary.CreateRange` of mapped KVPs |

**Construction decision tree (precise, applied after element/key/value conversions resolve):**
1. Identity bulk fast-path (src concrete == tgt concrete, element identity) → existing Clone/bulk-ctor.
2. Blittable array→array → existing `SynthesizeBlit` (Part 13/15).
3. Else by target shape (table). Interface → its concrete. Immutable → `CreateRange`/builder.
4. Unknown/unsupported target collection or dict type (e.g. multidim `T[,]`, `SortedDictionary`, `ConcurrentDictionary`, `Queue`/`Stack`, custom) → **DWARF027 (UnsupportedCollectionTarget)** — loud, with a "use `[MapProperty(Use=)]`" hint. (Deferred types listed in §Deferred.)

**Nested collections** (`List<List<T>>`, `T[][]`, `Dictionary<K,List<V>>`, `List<RecordDto>`): element/value type recurses through `TryResolveConversion` → Part A auto-nesting + Part B collection branch compose. Must be tested explicitly.

**Null handling (defensive, explicit, no silent surprise):** null source collection/dict → empty target by default (current behavior, never NRE); document. Null elements/values: existing `ThrowIfNull`/`ValueOrDefault`/`NullableProject` per element honored. A `[DwarfMapper(NullCollections = AsNull|AsEmpty)]` knob (default `AsEmpty`) so users can choose null-passthrough; default stays empty for safety.

**Collections as constructor params / init members** already route through `TryResolveConversion` (Part 18) — add explicit coverage.

### Tasks B
- [ ] Extend `CollectionConverter` (`TargetKind` + decision tree + immutable/`CreateRange` + interface→concrete) and `DictionaryConverter` (interface/immutable dict targets). Construction snippets emitted; capacity hints where source count known.
- [ ] DWARF027 (UnsupportedCollectionTarget) + `AnalyzerReleases.Unshipped.md`.
- [ ] `NullCollections` attribute knob (default AsEmpty).
- [ ] Tests (TDD): each new target type (interfaces, immutables) round-trips values; nested collections (`List<List<int>>`, `int[][]`, `Dictionary<string,List<RecordDto>>`); element/key/value needing conversion (CreateChecked/IParsable/enum/nested record); collection ctor param + init-only collection member; null source → empty (and AsNull mode → null); DWARF027 for `SortedDictionary`/`T[,]`/custom; identity bulk + blit still fire (no regression). Adversarial: covariant/abstract element type without mapper → loud DWARF005, not silent.

---

# Part C — Object-graph reconstruction with reference identity (configurable fidelity)

**Goal (owner).** Treat the entire reachable reference graph as one unit and reconstruct it faithfully into the target structure — **arbitrary topology**: shared nodes, diamonds, and multiple cycles (e.g. `A→B`, `B⇄C`, `C⇄D`, `B⇄D`). Every distinct source object maps to **exactly one** target object; every edge is relinked to the corresponding target. Fidelity is **configurable** (full vs degraded).

**Why the identity map solves the general case.** A single reference-identity dictionary `Dictionary<object,object>` (reference-equality comparer via `RuntimeHelpers.GetHashCode`) threaded through the whole operation, with **register-before-populate**, is the canonical graph-mapping algorithm:
```
T MapNode(S s, ctx):
    if s is null: return null
    if ctx.TryGet(s, out var existing): return (T)existing   // shared/back/cross edge → same target
    var t = new T(...ctor args...)        // (ctor args that are themselves graph nodes use MapNode)
    ctx.Set(s, t)                          // REGISTER BEFORE populating members  → breaks all cycles
    populate t's members via MapNode(...)  // diamonds & cycles resolve to the one registered target
    return t
```
This reconstructs *any* graph: each node visited once, all edges (forward, back, cross, shared) relinked. `A→B, B⇄C, C⇄D, B⇄D` ⇒ `A'→B', B'⇄C', C'⇄D', B'⇄D'` — topology-isomorphic.

**Constructor-arg caveat (defensive):** register-before-populate requires the instance to exist before its members are set. For **settable/init** targets this is exact. For targets where a **graph node is itself a constructor parameter** that participates in a cycle, the node can't be registered before construction → that specific back-edge cannot be satisfied by ctor injection. Detect this at generator time and emit **DWARF030 (CyclicConstructorParameter)**: "a constructor parameter participates in a reference cycle; map it via a settable/init member or break the cycle." Loud, not silent.

**Public API.** `[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.None | Preserve | PreserveDegraded)]` (default `None`).
- **`None`** (default): no tracking, zero overhead. Acyclic deep graphs fine. Cyclic *data* → runtime `StackOverflowException` (documented; same as Mapperly default). Recursive *types* still compile.
- **`Preserve` (full reconstruction):** thread an identity context; reconstruct the complete topology (shared identity + all cycles). One small dict allocation per top-level `Map` call.
- **`PreserveDegraded` (degraded reconstruction):** identity-preserving **up to `MaxDepth`** (configurable, e.g. `[DwarfMapper(ReferenceHandling = PreserveDegraded, MaxDepth = 8)]`); beyond the limit, references are **truncated** (set null / default), yielding a bounded partial graph. For huge or untrusted graphs where full reconstruction is undesirable. (Degraded = "as the user wants": bounded fidelity.)

**Generator-time scoping optimization ("watch recursion properties," owner).** Detect which `(src,tgt)` pairs are **recursion-capable** — a member/element/param type that can reach back to an ancestor type in the graph (Tarjan-style SCC / back-edge detection over the type graph). Only those synthesized methods get the `TryGet/Set` wrapper + ctx parameter; acyclic pairs stay zero-overhead even when `ReferenceHandling = Preserve`. This minimizes the "magic" and cost to exactly where a cycle is possible.

**Mechanism.** Public `T Map(S s)` creates the context and calls an internal `T Map(S s, DwarfRefContext ctx)`; the context threads through all recursion-capable synthesized methods. `DwarfRefContext` is a tiny struct/class wrapping `Dictionary<object,object>` (ref-equality) + depth counter — reflection-free, AOT-safe, in the core runtime package (not Testing). Threading is internal; the public signature is unchanged.

**Interaction with projection (Part D):** reference handling is **incompatible** with IQueryable projection (stateful dict can't live in an expression tree) → a `[ReferenceHandling != None]` mapper used on a projection method → **DWARF028** translatability error.

### Tasks C
- [ ] `DwarfRefContext` runtime type (ref-equality identity map + depth) in `src/DwarfMapper/`.
- [ ] Generator-time recursion-capability analysis (back-edge/SCC over the synthesized type graph) → mark pairs needing tracking.
- [ ] Emit, for tracked pairs: `if (s is null) return null; if (ctx.TryGet(s, out var e)) return (T)e; var t = new T(...); ctx.Set(s, t); …populate…; return t;`. Public/internal overload split; thread ctx through tracked calls only.
- [ ] `PreserveDegraded` + `MaxDepth` truncation; `ReferenceHandlingStrategy` enum + attribute props.
- [ ] DWARF030 (CyclicConstructorParameter) for an unbreakable ctor-arg back-edge.
- [ ] Tests (TDD): self-ref (`Node.Parent`), tree (`Tree.Children` acyclic — Preserve and None both work for acyclic), pairwise `A⇄B`, the **owner graph `A→B, B⇄C, C⇄D, B⇄D`** (assert exact reconstructed topology: shared node B' is the *same instance* referenced by A',C',D'; cycles closed), diamond (two paths to one node → one target instance). `None` + cyclic data → assert documented (don't run the SO; assert generator chose no tracking). `PreserveDegraded` MaxDepth truncation (assert depth-N ref is null). DWARF030 for cyclic ctor param. Recursion-capability scoping: assert an acyclic pair emits NO ctx wrapper even under Preserve (generated-code assertion). AOT gate.
- [ ] **Graph oracle for tests:** a reference-topology comparer (visited-set, compares by structural position + identity-sharing pattern) to verify reconstruction — see Part E.

---

# Part D — Provably-translatable IQueryable projection

**Goal (thesis headline).** Extend projection to deep graphs **and prove translatability at compile time**: a projection that would throw at query time (EF Core ≥3.0 throws on non-translatable expressions) is a **build error** instead. "If it won't translate to SQL, it won't compile."

**Projection-SAFE (emit, inlined into the single `Select` expression tree):**
1. Directly-assignable members; widening numeric cast `(long)x`; enum **by-value** cast `(int)e`/`(E2)e`.
2. **Nested object** → inlined `new InnerDto { A = s.Inner.A, … }`, recursively; null-nav → `s.Inner == null ? null : new InnerDto{…}`.
3. **Collection** → inlined `s.Items.Select(i => new ItemDto{…}).ToList()` / `.ToArray()` (and `IEnumerable<T>`/`IReadOnlyList<T>` via List). Filtering/ordering pass-through allowed.
4. **Constructor projection** `new Dto(s.A, s.B)` (NewExpression) + mixed `new Dto(s.A){ B = s.B }`, when ctor params bind by name and args are translatable.

**Projection-UNSAFE → compile-time DWARF028 (ProjectionNotTranslatable, with `{reason}`):** any mapping needing a synthesized helper or runtime state — `CreateChecked` narrowing, `IParsable.Parse`/`ToString(culture)`, enum **by-name** switch, custom `[MapProperty(Use=)]`, immutable/set/`Dictionary`/`HashSet` collection targets in projection, reference handling, before/after hooks, `T?→U?` NullableProject ternary that isn't pure-translatable, deep-nesting beyond a configurable projection `MaxDepth`. Each rejection names the member + reason so the user can fix it (e.g. "map at runtime instead, or use a by-value enum").

**Mechanism.** A dedicated `ResolveProjectionMembers` recursion that mirrors `TryResolveConversion` but, instead of synthesizing a helper, either (a) emits an inline expression fragment for a SAFE construct or (b) records a DWARF028 for an UNSAFE one. Nested object/collection emit recurses producing inline `new`/`Select` fragments. Depth guard → DWARF028 (or DWARF031 reuse).

### Tasks D
- [ ] Recursive projection resolver emitting inline fragments (nested member-init, collection `Select().ToList()/.ToArray()`, ctor projection) with per-construct translatability classification.
- [ ] DWARF028 (ProjectionNotTranslatable, `{member}`, `{reason}`); keep DWARF019 for the simple non-assignable case or fold into 028 with reasons (decide during impl; document).
- [ ] Reject `ReferenceHandling != None` on projection → DWARF028.
- [ ] Tests (TDD): nested-object projection (2–3 levels, incl. null-nav ternary), collection projection (`List`/array), ctor projection (positional record), mixed; the full SAFE matrix compiles + (where feasible) executes against an in-memory `IQueryable`/EF-Core-InMemory or a translatability check; the full UNSAFE matrix each → DWARF028 with the right reason (CreateChecked, Parse, by-name enum, Use=, immutable target, reference handling). Regression: existing flat projection unchanged. **Note:** runtime EF-SQL translation can't be unit-tested without a provider; assert (a) generated expression compiles, (b) UNSAFE constructs are compile-rejected, and (c) optionally run SAFE projections through EF Core InMemory or `System.Linq` over `IQueryable` to confirm shape.

---

# Part E — Defensive / holistic testing (the bar: cover every hole)

**Fuzzer extension (`SyntheticSchema`/`ObjectFactory`/`StructuralComparer`/`BehavioralFuzzTests`):**
- [ ] Add to the schema pool: `Dictionary<K,V>`, interface + immutable collection targets, **nested collections** (`List<List<T>>`, `T[][]`, `Dictionary<K,List<V>>`), and **nested reference-type objects** (a `class`/`record` member with its own auto-synthesized mapper, multiple levels).
- [ ] Add **type-divergent** collection schemas (Src/Dst element types differ — `List<int>`→`int[]`, `int[]`→`List<long>`, `List<SrcRec>`→`List<DstRec>`) so cross-type collection conversion is fuzzed, not just identity shapes.
- [ ] `ObjectFactory`: populate `Dictionary`/`HashSet`/immutable collections with data; build **graph fixtures** including shared nodes and cycles (for Part C).
- [ ] `StructuralComparer`: make it **graph-aware** — a visited-set/identity-map comparison that verifies reference-topology reconstruction (shared node ⇒ same target instance; cycle ⇒ closed) without infinite recursion. Provide both a structural compare (values) and a topology compare (identity pattern).
- [ ] Behavioral fuzz over emitted assemblies for the new shapes (value-preserving + topology-preserving). Keep deterministic seeds; replayable counterexamples.
- [ ] A **projection fuzz/matrix**: generate SAFE projection schemas (assert compile + DWARF-free) and UNSAFE ones (assert DWARF028).

**Adversarial review (mandatory before fold):** dispatch an independent reviewer over the whole feature with probes incl.: deep nesting depth limit; self/mutual/diamond/multi-cycle graphs; ctor-arg back-edge (DWARF030); covariant/abstract element types; interface targets with no concrete; immutable construction correctness; null source/element/value across every collection type; nested collection-of-records; `AutoNest=false` interactions; projection translatability for every UNSAFE construct (must reject, never emit query-time-throwing code); reference-handling scoping (acyclic pairs unwrapped); AOT/trim cleanliness of `DwarfRefContext` + immutable `CreateRange`. Fix every MUST-FIX (uncompilable output / silent loss / runtime-throw-that-should-be-compile-error) before fold.

**Verification gates:** full solution green (independently re-run); Release 0/0; AOT publish of a sample exercising deep nesting + a graph (Preserve) + a translatable projection.

---

## Diagnostics added (from DWARF027)
| ID | Meaning |
|----|---------|
| DWARF027 | Unsupported collection/dictionary target type (no construction strategy) |
| DWARF028 | Projection mapping not translatable (would throw at query time) — `{member}`, `{reason}` |
| DWARF030 | Constructor parameter participates in a reference cycle (cannot register-before-populate) |
| DWARF031 | Nesting depth limit exceeded (generator backstop) |
(DWARF029 reserved.)

## Build sequence
A (auto-nest + recursion-safe synthesis) → B (collection/dict taxonomy) → C (graph reconstruction) → D (translatable projection) → E (fuzz/adversarial), each TDD + committed + independently verified; fold to trunk after the whole-feature adversarial review.

## Self-Review
- "Map any complex datatype": auto-nested objects (A) + full collection/dict taxonomy (B) + nested compositions thereof; arbitrary graphs reconstructed (C); projections extended yet provably safe (D). ✅
- No silent surprises: completeness on nested members; DWARF027/028/030/031 are loud build errors; null handling explicit + configurable; narrowing still CreateChecked-loud. ✅
- Robust/resilient/scalable: generator-time recursion memoization + depth backstop (never hangs); reference handling scoped to recursion-capable pairs (zero cost otherwise); degraded mode bounds huge graphs; AOT-safe throughout. ✅
- Thesis-defining differentiator: compile-time projection-translatability proof — "if it would throw in SQL, it won't compile." ✅

## Deferred (explicit, with rationale)
- Exotic collections: `SortedSet`/`SortedDictionary` (ordering semantics), `ConcurrentDictionary`, `Queue`/`Stack` (order/ctor nuance), `Span`/`Memory` targets, multidimensional `T[,]`. → DWARF027 until added.
- `ReferenceHandling` for projection (impossible by translation constraint — permanently DWARF028).
- Polymorphic/derived-type element mapping (`[MapDerivedType]`) — separate stage (interacts with auto-nest + collections-of-abstract).
- Custom `IReferenceHandler` injection (Mapperly-style) — `DwarfRefContext` is internal for now.

## Research basis (sources captured in session)
- Mapperly: memoized `FindOrBuildMapping` + deferred queue (nested synth + recursion safety); collection taxonomy + `CreateRange`/interface→concrete construction; `PreserveReferenceHandler` (register-before-populate identity map); IQueryable projection limitations.
- AutoMapper: `ProjectTo` inlined member-init/collection projection; `RecursiveQueriesMaxDepth`; self-referencing destinations unsupported in projection.
- EF Core: ≥3.0 throws (not silently client-evaluates) on non-translatable expressions outside top-level projection — the boundary that justifies compile-time DWARF028. `new List<T>(queryable)` broke in 5.0; `.ToHashSet()/.ToDictionary()`/immutables not translatable; constructor projection (`NewExpression`) supported.
