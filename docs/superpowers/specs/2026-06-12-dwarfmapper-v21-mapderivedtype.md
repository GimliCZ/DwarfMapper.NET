# DwarfMapper.NET — Spec (Plan 21): `[MapDerivedType]` polymorphic mapping

**Goal.** Map a base-class- or interface-typed source to the correct DTO based on its **runtime type**, via a generated type-switch. For `partial AnimalDto Map(Animal a)` annotated with `[MapDerivedType<Dog,DogDto>]` + `[MapDerivedType<Cat,CatDto>]`, generate:
```
return a switch {
    global::Ns.Dog __s => <map Dog→DogDto>,
    global::Ns.Cat __s => <map Cat→CatDto>,
    _ => throw new ArgumentException("no [MapDerivedType] registered for runtime type ..."),
};
```
Unlocks polymorphic hierarchies and (later) heterogeneous `[FlattenGraph]`.

## Owner-locked v1 semantics
- **Attribute (both forms):** generic `[MapDerivedType<TSource, TTarget>]` AND non-generic `[MapDerivedType(typeof(TSource), typeof(TTarget))]`. `[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]`. Both in `src/DwarfMapper/` (net10/LangVersion latest → generic attributes OK). The generator reads both via attribute data (generic → `AttributeClass.TypeArguments`; typeof → `ConstructorArguments`).
- **Applies to a partial mapping method** whose source type is the base/interface. Each `TSource` MUST be assignable to the method's source type; each `TTarget` MUST be assignable to the method's return type. Else **DWARF035**.
- **Emission:** a `switch` on the source, one arm per registered derived pair, `_ => throw new ArgumentException(...)` for an unregistered runtime type (LOUD — no silent surprise). Null source → the existing public-mapper null-guard throws `ArgumentNullException` (unchanged), so the switch only sees non-null.
- **Most-derived-first ordering (CRITICAL correctness):** C# type-pattern `switch` matches in order, so derived types MUST be emitted most-derived first (sort by inheritance depth / `IsAssignableFrom`), or a base pattern would shadow a more-derived one (e.g. `Puppy : Dog : Animal` — `Puppy` arm must precede `Dog`). Validate + sort.
- **Each derived pair `(TSource, TTarget)` is resolved via the normal pipeline:** a user-declared `partial TTargetDto Map(TSource)` overload wins; otherwise auto-nest synthesizes `__DwarfMap_Obj_*`. The arm casts and calls the resolved mapper. (Reuse `TryResolveConversion` / the nested-mapper registry.)
- **Composes with collections:** `List<Animal>`→`List<AnimalDto>` where the element mapper is the derived-dispatching method → polymorphic element dispatch flows through the existing collection element-conversion recursion. Must be tested.
- **Base members:** each arm maps the FULL derived type to its derived DTO (the derived DTO carries base members). The base method does NOT separately construct — it only dispatches.
- **Interface source:** supported (switch on interface, arms are implementing types).
- **AOT-safe:** the switch is concrete type patterns + concrete mapper calls — no reflection. `_ =>` throw uses `a.GetType()` only for the message (allowed).

## Diagnostics
- **DWARF035 (InvalidMapDerivedType):** a `TSource` not assignable to the method source; a `TTarget` not assignable to the method return; a derived pair `(TSource,TTarget)` not mappable (no declared mapper + not auto-nestable); duplicate `TSource` registration; `[MapDerivedType]` on a method whose source is sealed/has no derived types (warn-or-error — error, with hint). Add to `DiagnosticDescriptors` + `AnalyzerReleases.Unshipped.md` (DWARF034 used; 035 next; 004/029 reserved).
- **DWARF036 (AmbiguousMapDerivedType):** *(added during implementation)* two `[MapDerivedType]` arms whose source types cannot be ordered most-derived-first because neither is assignable to the other (overlapping/ambiguous dispatch) — the runtime `switch` could not deterministically pick an arm. Distinct from the duplicate-`TSource` case folded into DWARF035.

## Tasks (TDD)
1. `MapDerivedTypeAttribute` (generic + non-generic) in `src/DwarfMapper/`. SPDX + XML doc.
2. Generator (`MapperExtractor`): read both attribute forms on a mapping method → list of `(srcDerivedType, tgtDerivedType)`. Validate assignability + mappability + duplicates → DWARF035. Resolve each derived pair's converter (declared overload or auto-nest registry). Sort arms most-derived-first. Mark the method as derived-dispatch (a flag/model like `IsProjection`); the method body emits the switch instead of normal construction. The method's own (base) member resolution is SKIPPED (dispatch-only).
3. Emit (`MapEmitter`): the `switch` expression with sorted arms `TDerived __s => <converter>(__s)` and the throwing default. Preserve the `var __dwarf_target`/after-hook form if hooks apply (hooks on a dispatch method → decide: apply to the result, or DWARF — simplest v1: a derived-dispatch method may keep before/after hooks wrapping the switch result; if non-trivial, defer hooks-on-dispatch with a note).
4. Tests (TDD; generator + runtime):
   - abstract base class + 2 derived → dispatch maps each runtime type to the right DTO; values correct.
   - interface source.
   - **3-level hierarchy `Puppy:Dog:Animal`** → most-derived-first ordering proven (a Puppy instance maps via the Puppy arm, NOT Dog). (Assert by a Puppy-only member surviving.)
   - **unregistered runtime type → ArgumentException** (loud).
   - null source → ArgumentNullException.
   - derived pair via **declared overload** vs **auto-nest** (both).
   - **collection of polymorphic elements** `List<Animal>`→`List<AnimalDto>` (mixed Dog/Cat list → each element dispatched).
   - generic `[MapDerivedType<D,DDto>]` AND typeof `[MapDerivedType(typeof(D),typeof(DDto))]` both work.
   - derived target is a **record/ctor** type.
   - **DWARF035**: src-derived not assignable; tgt-derived not assignable; unmappable derived pair; duplicate src.
   - Regression: full suite green (base 1360); existing mapping unaffected.
   - **Golden snapshot**: one `[MapDerivedType]` mapper (follow Snapshots/ Verify pattern; accept baseline).
   - **AOT**: add a `[MapDerivedType]` mapper to `samples/DwarfMapper.AotSample`; publish (report IL).
5. README/spec: document `[MapDerivedType]` (both syntaxes, runtime-type dispatch, exhaustive-or-throw, most-derived-first, collection composition; deferred items).

## Deferred
- Fallback to mapping the base type for an unregistered runtime type (v1 = throw).
- Heterogeneous `[FlattenGraph]` element partitioning using `[MapDerivedType]` (separate follow-up — now unblocked).
- Hooks on a dispatch method beyond simple wrap (if non-trivial).
- Target-type discriminator / open-world derived discovery (v1 = explicit registration).
