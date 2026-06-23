<!-- SPDX-License-Identifier: GPL-2.0-only -->
# v23 — Type-registry mapping (`[MapTo]`, no user `partial`)

**Status:** spec + **full-scale** implementation. Conversions (numeric / parse / enum), nested objects,
and List/array collections all work, reusing the core converter engine. Items marked _(spec-only)_ are
designed here but not yet built (see Boundary).

## Motivation

The partial-class/partial-method model is powerful but it puts ceremony in the user's face: every
mapping needs a `partial` class, and the call site needs an instance. For people migrating from
AutoMapper/Mapster who just want "convert A to B," that reads as heavyweight. This adds a **second
front door** — declarative, type-annotated, no user `partial`, callable as a one-liner — over the
**same resolution philosophy** (explicit, completeness-as-a-build-error, reflection-free, AOT-pure).

Key realisation that makes it safe: **a multi-target mapping is just N independent single-target
resolutions, each running the existing completeness gate.** A source member that must become `Name`
in one DTO and `FullName` in another is resolved per target; a mismatch is a compile error, not a
runtime swap. The "swapping mistake" the design must prevent is therefore caught by the same engine
that already catches unmapped members — fanned out per target.

## Decisions

1. **Intent lives on the source type, not a mapper class.** `[MapTo(typeof(OrderDto), typeof(OrderSummary))]`
   on a plain class/struct declares one-or-more target shapes. `params Type[]` so targets expand
   indefinitely. (Generic `[MapTo<T>]` sugar for the single-target case is _(spec-only)_.)
2. **Per-member config is STACKED attributes, read in source order, aligned positionally to the
   `[MapTo]` targets.** `[MapProperty]`/`[MapIgnore]` are `AllowMultiple`; the *i*-th directive on a member
   is the directive for the *i*-th target. Each directive is either "map to a destination name" or "ignore":
   - `[MapProperty("Name"), MapProperty("FullName")]` — `Name` in target 0, `FullName` in target 1.
   - `[MapProperty("Name"), MapIgnore]` — mapped in target 0, **ignored** in target 1 (mixing is the point).
   - `[MapProperty("Name")]` / `[MapIgnore]` — a **single** directive broadcasts to **every** target.
   - no attribute → the member's own name, in every target.
   - directive count must be 0, 1, or exactly the target count (**DWARFR04** otherwise).
   - Source order is taken from the attributes' syntax position (robust, not reliant on `GetAttributes()` order).
3. **Stacked over type-qualified or single-params — owner's call.** Two alternatives were considered and
   dropped: `[MapProperty(typeof(OrderDto), "Name")]` (verbose, couples each binding to a target type) and
   a single `[MapProperty("Name", "FullName")]` params array (can't interleave with `[MapIgnore]`). Stacked
   single-value directives read positionally are terser AND compose with `[MapIgnore]` for per-target
   map/ignore mixing. Swap-safety: because each directive resolves against *its* target's members, a
   reordering names a member that doesn't exist in that target → a **DWARFR02 completeness error**, caught
   statically — except when two targets share identical member-name sets (documented residual; spec'd as a
   future DWARFR05 swap-hint).
4. **No user `partial`.** The generator emits its **own** static class with extension methods. The user
   declares nothing partial.
5. **Call site:** extension methods. `order.MapTo<OrderDto>()` (generic, unambiguous by explicit target)
   and a named `order.ToOrderDto()` per pair. We do **not** add a keyless global `DwarfMapper.Map(x)` —
   with only the source type it is ambiguous the moment a source has >1 target.
6. **Same gate, same guarantees.** Per (source,target): completeness is a build error; emitted code is
   direct assignment; reflection-free; AOT-safe. Equatable extraction model (no `ISymbol`/`SyntaxNode`
   in the pipeline) so the incremental generator caches correctly.
7. **Unified attribute surface (default `using DwarfMapper;`).** `[MapTo]` and the member-placement forms
   of `[MapProperty]`/`[MapIgnore]` live in the **`DwarfMapper`** namespace — the same `[MapProperty]`/
   `[MapIgnore]` the class model uses, extended to also target members. The generator (`MapToGenerator`)
   and its `DWARFR` diagnostics are separate from the `[DwarfMapper]` pipeline, but the user-facing
   attributes are one set, so no extra `using` and no name shadowing.

## Public API (namespace `DwarfMapper`)

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public sealed class MapToAttribute : Attribute
{
    public MapToAttribute(params Type[] targets);   // [MapTo(typeof(A), typeof(B), ...)]
    public Type[] Targets { get; }
}

// MapProperty / MapIgnore are the existing class-model attributes, widened to target members.
// Method form: [MapProperty(source, target)] / [MapIgnore(destination)] (class model).
// Member form: [MapProperty(destination)] / [MapIgnore] (the [MapTo] registry) — stack them; read in
// source order, the i-th directive applies to the i-th [MapTo] target.
```

Usage (multi-target):

```csharp
using DwarfMapper;

[MapTo(typeof(OrderDto), typeof(OrderSummary))]
public class Order
{
    [MapProperty("Name"), MapProperty("FullName")]   // OrderDto.Name ← FullName ; OrderSummary.FullName ← FullName
    public string FullName { get; set; }

    [MapIgnore] public string PasswordHash { get; set; }   // single → ignored in every target
    public decimal Total { get; set; }                     // both targets, by name
}

// Mixing — a member mapped in one target, ignored in another (order-sensitive):
[MapTo(typeof(PName), typeof(QName))]
public class Aliased
{
    [MapProperty("Name"), MapIgnore] public string Formal { get; set; }  // PName.Name ← Formal ; QName: ignored
    [MapIgnore, MapProperty("Name")] public string Casual { get; set; }  // PName: ignored ; QName.Name ← Casual
}

// call sites — no instance, no partial:
OrderDto    dto     = order.MapTo<OrderDto>();
OrderSummary summary = order.ToOrderSummary();
```

## Mechanism

1. **Discover** — `ForAttributeWithMetadataName("DwarfMapper.MapToAttribute", …)`; predicate on
   `TypeDeclarationSyntax`.
2. **Extract (equatable model)** — for the source type: its readable members (name, type-FQN), the target
   type FQNs, and member configs (renames keyed by optional target, ignores keyed by optional target).
   No Roslyn symbols cross into the cached model.
3. **Resolve, per target** — build `destinationName → sourceMember` honoring renames (per-target overrides
   global) and ignores; for each destination writable member, require a source (assignable type) or a
   value, else **completeness diagnostic**. Two sources claiming one destination → **conflict diagnostic**.
4. **Emit** — one `internal static class` per source type with `MapTo<T>(this Src)` (typeof-dispatch) and
   `To{Target}(this Src)` workers doing direct member assignment.

### Conversion support (full scale)
The registry's `Resolver` reuses the **core conversion engine** and adds self-contained graph mapping:
- **Scalars** — reuses `NumericConverter` (checked narrowing), `ParsableConverter` (string↔T parse/format),
  `EnumConverter` (enum↔enum/string/int) directly; identity/implicit go straight to assignment. The
  synthesized helper methods are emitted into the generated static class.
- **Nested objects** — `Resolver.SynthNested` recursively synthesizes a `__DwarfMapObj_*` mapper for an
  object→object member pair (by-name, full conversion per member), with a cycle guard: a recursive pair is
  **DWARFR06** (the registry threads no reference context — use the `[DwarfMapper]` class for cyclic graphs).
- **Collections** — `T[]`/`List<T>` (and read-only/enumerable sources) → `U[]`/`List<U>`, each element run
  through the same `Resolver` (so collections-of-nested and element conversions work); null source → empty.

### Boundary (stays on the `[DwarfMapper]` class model)
Custom converters (`Use=`), hooks, projections, `[MapDerivedType]`/`[FlattenGraph]`, reference-cycle
preservation, `Dictionary<,>`, and nullable-unwrap with a `NullStrategy` decision are **not** in the
registry — a converter needs a method to point at (awkward on a domain type), and cycle/null-policy are
class-level concerns. The registry covers the config-light majority; reach for the class model otherwise.
A future unification could route the registry through the full `MapperExtractor` pipeline.

## Diagnostics (separate `RegistryDiagnostics`, prefix `DWARFR` so the DWARF0xx self-validation ignores them)

- **DWARFR01** — `[MapTo]` target is not a mappable type (not a class/struct, or equals the source).
- **DWARFR02** — destination member is not mapped (completeness; the headline gate, per target). This is
  also what catches a positional swap when the swapped name isn't a member of that target.
- **DWARFR03** — two source members map to the same destination member (give them distinct positional names).
- **DWARFR04** — a member's directive count (stacked `[MapProperty]`/`[MapIgnore]`) is neither 1 nor the `[MapTo]` target count.
- **DWARFR05** — no built-in conversion between the mapped source/destination member types (use the class model for a custom converter).
- **DWARFR06** — a nested object pair is recursive (cyclic); the registry threads no reference context.
- **DWARFR07** _(spec-only)_ — swap-hint: a positional value is not a member of its target but IS a member
  of a *different* target (likely a reordering mistake even when names overlap).
- The `DWARFR` IDs could later fold into the DWARF0xx scheme (kept separate for now to avoid coupling the registry diagnostics to the class-model self-validation scans).

## Tasks
- [x] Attributes unified into `DwarfMapper` (`MapTo` new; `MapProperty`/`MapIgnore` widened to members).
- [x] `MapToGenerator` + `RegistryDiagnostics` + emitter.
- [x] Stacked `[MapProperty]`/`[MapIgnore]` directives, source-ordered, positional per target (1 = broadcast) + arity check (DWARFR04).
- [x] **Full-scale conversions:** numeric / parse / enum (reuse core converters), nested objects (cycle-guarded, DWARFR06), List/array collections incl. collections-of-nested; DWARFR05 no-conversion gate.
- [x] Runtime tests: positional rename, order-sensitive map/ignore mixing, and the full conversion matrix (scalars + nested + collections + overflow).
- [ ] _(spec-only)_ generic `[MapTo<T>]`; DWARFR07 swap-hint; struct-target zero-box path; nullable-unwrap (NullStrategy), `Use=`/hooks/`Dictionary` via the shared pipeline; generated-source snapshot + incremental-cacheability test (see `docs/research/testing-conformance-REPORT.md`).

## Resolved / open
- **Resolved: unified.** `[MapProperty]`/`[MapIgnore]` are one attribute each, valid on both the mapping
  method (class model) and the source member (registry) — `[MapTo]` joins them in `DwarfMapper`. The
  class-model attribute reads were already arity-guarded, so widening was safe; `AttributeContractTests`
  updated. This also removes the namespace-shadowing issue.
- Open: should `[MapIgnore]` on a source member also drive a source-coverage gate (RequiredMapping.Both analogue)?
