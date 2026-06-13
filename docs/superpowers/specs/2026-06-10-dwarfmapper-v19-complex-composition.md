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

**Opt-out granularity.** The `AutoNest` switch lives on the **mapper class** (`[DwarfMapper(AutoNest = false)]` on the `partial class`, NOT on a property) — it is the master "force-explicit-everywhere" toggle, with an optional **per-method** override (a method-level `[AutoNest(false)]`) so one mapping method in a class can differ. **Per-member** opt-out needs no new knob: to exclude one nested member from synthesis while still handling it, the user declares a `partial` mapper for that pair (always wins over synthesis), maps it with `[MapProperty(Use=…)]`, or `[MapIgnore]`s it. Default is `AutoNest = true`. With it off, a nested pair lacking a user mapper → DWARF005 (today's behavior).

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

**Materialized vs. unmaterialized (owner) — each target type carries a projection-translatability flag.** The taxonomy is shared with Part D: every target collection/dict kind is classified **projection-safe** (`List<T>`, `T[]`, `IEnumerable<T>`/`IReadOnlyList<T>`/`ICollection<T>` → inline `Select().ToList()/.ToArray()`, EF-translatable) or **projection-unsafe** (`HashSet`/`ISet`, all immutables, `Dictionary` and dict interfaces → not translatable; in a projection these become DWARF028, see Part D). The single `CollectionInfo` record exposes both the materialized construction strategy and `IsProjectionTranslatable`, so B and D never diverge.

**Deferred / lazy enumerable targets.** When the target is exactly `IEnumerable<T>` (not a concrete collection), emit a **lazy** `src.Select(x => map(x))` **without** forcing `.ToList()` — the result stays deferred/unmaterialized (matching Mapperly + LINQ semantics), so the caller controls materialization. (A concrete target — `List`/array/etc. — materializes eagerly as today.) Document that a lazy target captures the source reference; re-enumeration re-maps.

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

**Constructor-arg caveat (defensive):** register-before-populate requires the instance to exist before its members are set. For **settable/init** targets this is exact. For targets where a **graph node is itself a constructor parameter** that participates in a cycle, the node can't be registered before construction → that specific back-edge cannot be satisfied by ctor injection (the canonical immutable-cyclic-graph deadlock; serializers like System.Text.Json refuse it outright). Detect this at generator time and emit **DWARF030 (CyclicConstructorParameter)**: "a constructor parameter participates in a reference cycle; map it via a settable/init member or break the cycle." Loud, not silent. (Acyclic graphs into immutable targets work — ctor args mapped in topological order.)

### Well-documented graph edge cases (researched) — MUST handle each
Canonical object-graph-copy rules (System.Text.Json `ReferenceHandler`, BinaryFormatter `ObjectManager`, deep-clone literature):
1. **Reference equality, never value equality (CRITICAL).** The identity map MUST use `System.Collections.Generic.ReferenceEqualityComparer.Instance` (≡ `RuntimeHelpers.GetHashCode` + `ReferenceEquals`), NEVER the default comparer. Otherwise **records / value-equality types** (which override `Equals`/`GetHashCode`) silently MERGE two distinct-but-equal nodes into one, or wrongly skip a back-edge. The generator must emit this comparer explicitly. (This is the single highest-severity correctness trap.)
2. **Collections/arrays are graph nodes too.** A shared or cyclic `List<T>`/array/`Dictionary` instance must itself be registered in the identity map (register the target collection BEFORE adding elements), or two parents get distinct target lists / a list containing its ancestor infinite-loops. Generated collection code does register-before-fill for recursion-capable element types.
3. **Self-loop** (`n.Self = n`), **diamonds/shared nodes**, **arbitrary SCCs** (the `A→B,B⇄C,C⇄D,B⇄D` case): all handled by register-before-populate; no special SCC detection needed.
4. **Deep ACYCLIC chains → StackOverflow in the mapper itself.** Even with no cycle, a 100k-node chain recursing through generated methods overflows the stack. Mitigation: a **depth counter on recursion-capable pairs**, throwing a catchable **`DwarfMappingDepthException`** at `MaxDepth` (default **64**, matching System.Text.Json/AutoMapper; hard cap **1000** non-overridable). A silent `StackOverflowException` (uncatchable, crashes the process) is NEVER acceptable — turn it into a loud catchable exception. The counter is emitted ONLY on recursion-capable pairs (a finitely-deep acyclic type graph is bounded by construction → no counter, zero overhead).
5. **Struct / value-type nodes excluded** from the identity map (copied by value; boxed-struct identity is meaningless). Only reference types are tracked. A struct containing a reference field still gets that field tracked when the reference is mapped.
6. **Null edge first.** `if (s is null) return null;` is the absolute first line, before any map lookup (avoids null-key lookup throw).
7. **Immutable cyclic target** = the register-before-populate deadlock → **DWARF030** (compile error). No runtime fix-up/forward-reference machinery (BinaryFormatter `ObjectManager` style) — explicitly out of scope.

### Fidelity model ("full or degraded as the user wants")
- `[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.None | Preserve)]` (default `None`).
  - **`None`**: no identity map. Recursion-capable pairs still get the depth counter → cyclic or over-deep data throws `DwarfMappingDepthException` (loud, catchable), never silent SO. Acyclic-within-depth graphs map fully. Zero overhead on non-recursion-capable pairs.
  - **`Preserve` (full reconstruction):** identity map (`ReferenceEqualityComparer`) threaded through recursion-capable pairs; full topology — shared nodes, diamonds, all cycles — reconstructed isomorphically. One small dict per top-level `Map` call.
- `[DwarfMapper(MaxDepth = N)]` (default 64, hard cap 1000): universal runtime bound on recursion-capable pairs.
- `[DwarfMapper(OnCycle = Throw | SetNull)]` — the **degraded** knob when `ReferenceHandling = None`: `Throw` (default — depth cap) or `SetNull` (≡ System.Text.Json `IgnoreCycles`: the re-entrant back-edge is set to null, yielding a finite acyclic projection of a cyclic source — useful for display/DTO). Ignored under `Preserve` (cycles are reconstructed).

**Public API.** `[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.None | Preserve | PreserveDegraded)]` (default `None`).
- **`None`** (default): no tracking, zero overhead. Acyclic deep graphs fine. Cyclic *data* → runtime `StackOverflowException` (documented; same as Mapperly default). Recursive *types* still compile.
- **`Preserve` (full reconstruction):** thread an identity context; reconstruct the complete topology (shared identity + all cycles). One small dict allocation per top-level `Map` call.
- **`PreserveDegraded` (degraded reconstruction):** identity-preserving **up to `MaxDepth`** (configurable, e.g. `[DwarfMapper(ReferenceHandling = PreserveDegraded, MaxDepth = 8)]`); beyond the limit, references are **truncated** (set null / default), yielding a bounded partial graph. For huge or untrusted graphs where full reconstruction is undesirable. (Degraded = "as the user wants": bounded fidelity.)

**Generator-time scoping optimization ("watch recursion properties," owner).** Detect which `(src,tgt)` pairs are **recursion-capable** — a member/element/param type that can reach back to an ancestor type in the graph (Tarjan-style SCC / back-edge detection over the type graph). Only those synthesized methods get the `TryGet/Set` wrapper + ctx parameter; acyclic pairs stay zero-overhead even when `ReferenceHandling = Preserve`. This minimizes the "magic" and cost to exactly where a cycle is possible.

**Mechanism.** Public `T Map(S s)` creates the context and calls an internal `T Map(S s, DwarfRefContext ctx)`; the context threads through all recursion-capable synthesized methods (only — acyclic pairs keep the plain signature). `DwarfRefContext` is a tiny class wrapping `Dictionary<object,object>(ReferenceEqualityComparer.Instance)` + an `int depth` — reflection-free, AOT-safe, in the core runtime package (not Testing). Under `None` it carries only the depth counter (no dict allocated unless `Preserve`). Threading is internal; the public signature is unchanged.

**Interaction with projection (Part D):** reference handling is **incompatible** with IQueryable projection (stateful dict can't live in an expression tree) → a `[ReferenceHandling != None]` mapper used on a projection method → **DWARF028** translatability error.

### Tasks C
- [ ] `DwarfRefContext` runtime type in `src/DwarfMapper/`: `Dictionary<object,object>(ReferenceEqualityComparer.Instance)` (lazily allocated; only under `Preserve`) + `int depth` + `MaxDepth`; `TryGet`/`Set`/`EnterDepth`(throws `DwarfMappingDepthException` past cap). `DwarfMappingDepthException` type. AOT-safe, no reflection.
- [ ] Generator-time **recursion-capability analysis** (back-edge / SCC over the synthesized `(src,tgt)` type graph) → mark exactly the pairs that can re-enter; only those get the ctx parameter + depth/identity wrapper. Also identify **collection/array element types** that are recursion-capable → register the collection instance too.
- [ ] Emit, for tracked reference pairs: `if (s is null) return null; ctx.EnterDepth(); if (ctx.TryGet(s, out var e)) return (T)e; var t = new T(...); ctx.Set(s, t); …populate…; return t;`. Exclude **value types** from tracking (copy by value). Public/internal overload split; thread ctx through tracked calls only.
- [ ] `ReferenceHandlingStrategy { None, Preserve }` + `MaxDepth` (default 64, hard cap 1000) + `OnCycle { Throw, SetNull }` attribute props. `SetNull` (None mode): break the re-entrant edge via an on-stack guard set → null.
- [ ] **CRITICAL:** identity map uses `ReferenceEqualityComparer.Instance` — emit a generator test proving two **distinct-but-equal record** source nodes are NOT merged.
- [ ] DWARF030 (CyclicConstructorParameter) for an unbreakable ctor-arg back-edge (cyclic→immutable target).
- [ ] Tests (TDD), covering the researched checklist: null-first; self-loop (`n.Self=n` → `t.Self==t`); 2-node `A⇄B`; the **owner graph `A→B,B⇄C,C⇄D,B⇄D`** (assert shared node B' is the SAME instance referenced by A',C',D'; all cycles closed); diamond (one shared child → one target instance, referenced twice); **shared `List<T>`** referenced by two parents → one target list; **list cycle** (list containing its ancestor); two distinct-but-equal records NOT merged; **long chain > MaxDepth → `DwarfMappingDepthException`** (NOT StackOverflow); struct/record/class mixed graph; `OnCycle=SetNull` → back-edge null + finite output; `None` + recursion-capable: assert depth counter present (no silent SO); recursion-capability scoping: an acyclic pair emits NO ctx wrapper even under `Preserve` (generated-code assertion); DWARF030 for cyclic ctor param. AOT gate.
- [ ] **Graph oracle for tests:** see Part E (topology-aware, identity-sharing-aware, visited-set comparer).

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

# Part E — Defensive / holistic testing: exhaustive combinatorial coverage (the bar: cover every hole)

**(owner) Exhaustive combination engine + self-materializer + fixtures.** Beyond random fuzzing, build a *systematic* engine that enumerates the cartesian product of **every system basic type × every complex container/composition shape**, generates a mapper for each, materializes instances, maps, and verifies. This is deterministic, replayable, and exhaustive (not just sampled).

- [ ] **Self-materializer** (`ObjectFactory` v2): construct an instance of ANY type deterministically from a seed — every basic type (the full v17 set: bool, sbyte..ulong, char, float, double, decimal, string, Guid, DateTime, DateTimeOffset, TimeSpan, enums incl. non-`int` underlying), every supported collection/dict/immutable/interface target (with data), nested objects/records, and **reference graphs** with shared nodes + cycles (self-loop, A⇄B, the A→B/B⇄C/C⇄D/B⇄D shape, diamond, shared/cyclic list). Excludes nothing the mapper claims to support.
- [ ] **Combinatorial schema generator** (`CombinatorialSchema`): enumerate the matrix
  `{basic types B} × {shape S}` where `S ∈ { raw, B?, B[], List<B>, IReadOnlyList<B>, HashSet<B>, ImmutableArray<B>, Dictionary<string,B>, Dictionary<B,string>, nested-object{ B }, record(B), List<List<B>>, List<record(B)>, Dictionary<string,List<B>>, graph-node{ B + self-ref } }` — plus a bounded **composition depth** (shape-of-shape up to depth 2–3). For each cell: emit Src/Dst (identity AND a type-divergent variant where the element/member type widens, e.g. `int→long`, `SrcRec→DstRec`), run the generator (assert no unexpected DWARF), `EmitAssembly`, materialize via the self-materializer, map, and verify with the oracle. The matrix is enumerated exhaustively for depth ≤1 and sampled (seeded) for deeper compositions to bound test time; tag the heavy tier `[Trait("tier","exhaustive")]`.
- [ ] **Golden fixtures**: hand-authored extreme cases that must never regress — the owner graph `A→B,B⇄C,C⇄D,B⇄D`; deep chain at MaxDepth boundary; record-equality-merge trap; shared list; immutable cyclic (→DWARF030); empty/null collections; dictionary with enum keys; nested `List<List<RecordDto>>`.
- [ ] **Graph-aware oracle** (`StructuralComparer` v2): two comparison modes — (a) **value** compare (deep, MaxDepth-guarded, deterministic order for sets/dicts); (b) **topology** compare with a visited identity-map that asserts the reconstructed graph's *sharing/cycle pattern* matches the source's (shared source node ⇒ same target instance; cycle ⇒ closed), terminating on revisit. Used for Part C verification.
- [ ] **Behavioral fuzz** over emitted assemblies for the new shapes (value- AND topology-preserving), deterministic seeds, replayable counterexamples; widen `SyntheticSchema` to include dicts, nested collections, nested reference objects, type-divergent collections, and graph shapes (HashSet/dict ordered deterministically for the oracle).
- [ ] **Projection translatability matrix**: enumerate the SAFE constructs (assert compile + DWARF-free, and run through `IQueryable`/EF-InMemory where feasible to confirm shape) and the UNSAFE constructs (assert DWARF028 with the correct reason). Exhaustive over the construct list in Part D.

**Adversarial review (mandatory before fold):** dispatch an independent reviewer over the whole feature with probes incl.: deep nesting depth limit; self/mutual/diamond/multi-cycle graphs; ctor-arg back-edge (DWARF030); covariant/abstract element types; interface targets with no concrete; immutable construction correctness; null source/element/value across every collection type; nested collection-of-records; `AutoNest=false` interactions; projection translatability for every UNSAFE construct (must reject, never emit query-time-throwing code); reference-handling scoping (acyclic pairs unwrapped); AOT/trim cleanliness of `DwarfRefContext` + immutable `CreateRange`. Fix every MUST-FIX (uncompilable output / silent loss / runtime-throw-that-should-be-compile-error) before fold.

**Verification gates:** full solution green (independently re-run); Release 0/0; AOT publish of a sample exercising deep nesting + a graph (Preserve) + a translatable projection.

---

## Diagnostics & runtime exceptions added (from DWARF027)
| ID | Kind | Meaning |
|----|------|---------|
| DWARF027 | compile error | Unsupported collection/dictionary target type (no construction strategy) |
| DWARF028 | compile error | Projection mapping not translatable (would throw at query time) — `{member}`, `{reason}` |
| DWARF030 | compile error | Constructor parameter participates in a reference cycle (cannot register-before-populate; cyclic→immutable target) |
| DWARF031 | compile error | Generator-time nesting/type-graph backstop exceeded |
| `DwarfMappingDepthException` | **runtime** | Recursion-capable mapping exceeded `MaxDepth` (default 64, hard cap 1000) — catchable; replaces silent `StackOverflowException` |
(DWARF029 reserved. The depth limit is a runtime exception, not a compile diagnostic, because cyclicity/length is a property of the data, not the types.)

## Build sequence
A (auto-nest + recursion-safe synthesis) → B (collection/dict taxonomy) → C (graph reconstruction) → D (translatable projection) → E (fuzz/adversarial), each TDD + committed + independently verified; fold to trunk after the whole-feature adversarial review.

## Self-Review
- "Map any complex datatype": auto-nested objects (A) + full collection/dict taxonomy (B) + nested compositions thereof; arbitrary graphs reconstructed (C); projections extended yet provably safe (D). ✅
- No silent surprises: completeness on nested members; DWARF027/028/030/031 are loud build errors; null handling explicit + configurable; narrowing still CreateChecked-loud. ✅
- Robust/resilient/scalable: generator-time recursion memoization + depth backstop (never hangs); reference handling scoped to recursion-capable pairs (zero cost otherwise); degraded mode bounds huge graphs; AOT-safe throughout. ✅
- Thesis-defining differentiator: compile-time projection-translatability proof — "if it would throw in SQL, it won't compile." ✅

## Implemented later (post-v19)
- `OnCycle = SetNull` (Plan 19 Part-C remaining item, shipped v23): None-mode cycle breaking via an on-stack guard (`DwarfRefContext.TryEnterNode`/`ExitNode`, `ReferenceEqualityComparer`); a re-entrant back-edge to a node on the active mapping stack is nulled (≡ STJ `IgnoreCycles`). `DWARF037` (Warning) when combined with `Preserve` (ignored). Covers self-loop / 2-N-node cycles / cyclic diamond (duplicated children, back-edges null) / mutual recursion across types / deep-acyclic depth backstop; fuzz-proven to always terminate with acyclic, deterministic output. Golden snapshot + AOT gate added.
  - **Collection/dictionary-edge cycles (shipped v23, follow-up):** the one shared `DwarfRefContext` is now threaded through collection (`__DwarfMapColl_*_c`) and dictionary (`__DwarfMapDict_*_c`) element mappers in None/SetNull mode — gated on a recursion-capable element, register-before-fill stays Preserve-only. Under `SetNull` a cycle routed through a `List<T>` element or `Dictionary<K,V>` value breaks the same as a direct member (the re-entrant element/value becomes `null`). Implemented by splitting the collection/dict converter's `isPreserve` into `threadCtx` (Preserve OR recursion-capable element) vs `registerBeforeFill` (Preserve mutable only), plus threading an `isSetNull` flag through `TryResolveConversion`/`ResolveMembers`/`ResolveConstructorArguments`. Preserve helper names/snapshots are unchanged (new `_c` tag only for the ctx-only variant). Fuzz-proven over random `List`-wired cyclic graphs.
  - **None+Throw collection/dict-edge depth-guard (shipped v23):** the "never a silent StackOverflow" guarantee now also holds across collection/dict edges in the default mode. A self-map (`Map(Tree)` with `List<Tree>`) whose recursion is visible only through the collection helper is detected by injecting a `helper → element-method` edge into the call graph (so `Map → helper → Map` forms a cycle → the element method gets a `__DwarfMap_Depth_*` companion). A post-pass then re-synthesizes the helper in place (`CollectionConverter`/`DictionaryConverter.SynthesizeInPlace`) to thread `(ctx, depth)` and route through the companion, and adds the helper to `recursionCapableNames` so the existing member-patching loops promote the enclosing methods. Non-recursive collections are never recorded → zero-overhead preserved (no snapshot churn). Overloaded element methods are skipped (fall back to documented behaviour) to avoid call-graph overload-expansion false cycles. Deep/cyclic data now throws the catchable `DwarfMappingDepthException`. Unit + generator + seeded fuzz + AOT gate.

## Deferred (explicit, with rationale)
- Exotic collections: `SortedSet`/`SortedDictionary` (ordering semantics), `ConcurrentDictionary`, `Queue`/`Stack` (order/ctor nuance), `Span`/`Memory` targets, multidimensional `T[,]`. → DWARF027 until added.
- `ReferenceHandling` for projection (impossible by translation constraint — permanently DWARF028).
- Polymorphic/derived-type element mapping (`[MapDerivedType]`) — separate stage (interacts with auto-nest + collections-of-abstract).
- Custom `IReferenceHandler` injection (Mapperly-style) — `DwarfRefContext` is internal for now.

## Research basis (sources captured in session)
- Mapperly: memoized `FindOrBuildMapping` + deferred queue (nested synth + recursion safety); collection taxonomy + `CreateRange`/interface→concrete construction; `PreserveReferenceHandler` (register-before-populate identity map); IQueryable projection limitations.
- AutoMapper: `ProjectTo` inlined member-init/collection projection; `RecursiveQueriesMaxDepth`; self-referencing destinations unsupported in projection.
- EF Core: ≥3.0 throws (not silently client-evaluates) on non-translatable expressions outside top-level projection — the boundary that justifies compile-time DWARF028. `new List<T>(queryable)` broke in 5.0; `.ToHashSet()/.ToDictionary()`/immutables not translatable; constructor projection (`NewExpression`) supported.
