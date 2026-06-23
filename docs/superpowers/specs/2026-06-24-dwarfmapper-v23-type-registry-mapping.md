<!-- SPDX-License-Identifier: GPL-2.0-only -->
# v23 — Type-registry mapping (`[MapTo]`, no user `partial`)

**Status:** spec + prototype slice (this commit). The prototype implements the no-config happy path;
items marked _(spec-only)_ are designed here but not yet built.

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
7. **Isolation.** New attributes live in namespace `DwarfMapper.Registry`; the generator and its
   diagnostics are separate from the existing `[DwarfMapper]` pipeline. The two front doors share design
   DNA but not code paths (yet); a later refactor can unify the resolver.

## Public API (namespace `DwarfMapper.Registry`)

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public sealed class MapToAttribute : Attribute
{
    public MapToAttribute(params Type[] targets);   // [MapTo(typeof(A), typeof(B), ...)]
    public Type[] Targets { get; }
}

// Stack these on a member; read in source order, the i-th directive applies to the i-th [MapTo] target.
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public sealed class MapPropertyAttribute : Attribute
{
    public MapPropertyAttribute(string destinationMember);
    public string DestinationMember { get; }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public sealed class MapIgnoreAttribute : Attribute { }
```

Usage (the user's example, expanded to multi-target):

```csharp
using DwarfMapper.Registry;

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

1. **Discover** — `ForAttributeWithMetadataName("DwarfMapper.Registry.MapToAttribute", …)`; predicate on
   `TypeDeclarationSyntax`.
2. **Extract (equatable model)** — for the source type: its readable members (name, type-FQN), the target
   type FQNs, and member configs (renames keyed by optional target, ignores keyed by optional target).
   No Roslyn symbols cross into the cached model.
3. **Resolve, per target** — build `destinationName → sourceMember` honoring renames (per-target overrides
   global) and ignores; for each destination writable member, require a source (assignable type) or a
   value, else **completeness diagnostic**. Two sources claiming one destination → **conflict diagnostic**.
4. **Emit** — one `internal static class` per source type with `MapTo<T>(this Src)` (typeof-dispatch) and
   `To{Target}(this Src)` workers doing direct member assignment.

### Degradation to the class model _(spec-only)_
Custom converters (`Use=`), hooks (`[BeforeMap]`/`[AfterMap]`), nested/collection/enum conversion, and
projections do **not** fit cleanly on source-member attributes (a converter needs a method to point at,
and putting it on the domain type couples model→DTO). The registry front door covers the **simple,
config-light majority**; anything needing those keeps using the `[DwarfMapper]` class. Long-term the
registry resolver should call the **same** `MapperExtractor` pipeline so nested/collection/converter
support comes for free; the prototype uses a minimal standalone resolver (identity/implicit member
assignment only) to prove ergonomics.

## Diagnostics (separate `RegistryDiagnostics`, prefix `DWARFR` so the DWARF0xx self-validation ignores them)

- **DWARFR01** — `[MapTo]` target is not a mappable type (not a class/struct, or equals the source).
- **DWARFR02** — destination member is not mapped (completeness; the headline gate, per target). This is
  also what catches a positional swap when the swapped name isn't a member of that target.
- **DWARFR03** — two source members map to the same destination member (give them distinct positional names).
- **DWARFR04** — a member's directive count (stacked `[MapProperty]`/`[MapIgnore]`) is neither 1 nor the `[MapTo]` target count.
- **DWARFR05** _(spec-only)_ — swap-hint: a positional value is not a member of its target but IS a member
  of a *different* target (likely a reordering mistake even when names overlap).
- Final IDs fold into the DWARF0xx scheme if/when this graduates from prototype to product.

## Tasks
- [x] Attributes in `DwarfMapper.Registry`.
- [x] `MapToGenerator` + `RegistryDiagnostics` + minimal emitter (identity/implicit members, ref-type targets).
- [x] Stacked `[MapProperty]`/`[MapIgnore]` directives, source-ordered, positional per target (1 = broadcast) + arity check (DWARFR04).
- [x] Runtime test: single + multi-target, positional rename, and per-target map/ignore mixing (order-sensitive).
- [ ] _(spec-only)_ generic `[MapTo<T>]`; DWARFR05 swap-hint; struct-target zero-box path; converters/hooks/nested via the shared pipeline; generated-source snapshot + incremental-cacheability test (see `docs/research/testing-conformance-REPORT.md`).

## Open questions
- Unify with the existing attributes (one `[MapProperty]` valid on both method and member) vs. keep the
  registry attributes separate? (Separate for now to protect the existing suite / contract tests.)
- Should `[MapIgnore]` on a source member also drive a source-coverage gate (RequiredMapping.Both analogue)?
