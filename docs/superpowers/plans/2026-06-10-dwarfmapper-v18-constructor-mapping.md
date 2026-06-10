# DwarfMapper.NET — Plan 18: Constructor / record / immutable target mapping

> TDD throughout. SPDX header on new files; CPM; warnings-as-errors; warning-clean. New diagnostics → `AnalyzerReleases.Unshipped.md`. Subagent-driven; commit per task; independent review; fold to trunk.

**Goal.** Let DwarfMapper construct targets that have **no parameterless constructor and no public setters** — positional `record`s, `record struct`s, and constructor-based immutable classes/structs — by binding constructor parameters from source members, then object-initializing any remaining writable members. Today `MapEmitter` always emits `new T { ... }` (object initializer) and `MapperExtractor` hard-errors (`NoParameterlessConstructor`) when no public parameterless ctor exists, so records (the dominant modern DTO shape) are unsupported.

**Thesis fit.** A constructor parameter is *mandatory by definition* — if it cannot be satisfied from the source, that is a **build error** (`DWARF024`). This extends "completeness = build error" from settable members to constructor parameters. Conversions on ctor args reuse `TryResolveConversion`, so `CreateChecked` narrowing, `IParsable` string parsing, enum, nullable, and `T?→U?` all apply to constructor arguments for free.

**Validated by research (2026-06-10):** Mapperly supports constructor mapping with a selection policy ([MapperConstructor] > parameterless-preferred > most-params, case-insensitive param match, obsolete skipped). We adopt a simpler, stricter v1 and add `[DwarfMapperConstructor]` as the disambiguation escape hatch.

**Builds on:** master tip (`0526093`, Plan 17 folded). Branch `feat/v18-constructor-mapping`.

---

## Current shape (read before coding)
- `MapEmitter.WriteMethod` (~line 85): emits `return new {ReturnTypeFullName} { Members… };` (object initializer only). Value-expr logic (NullableProject / converter / null-handling) lives inline ~96–149 — **factor it into `BuildValueExpression(member, paramName)`** and reuse for ctor args.
- `MapperExtractor.Extract` (~line 102): `if (!HasAccessibleParameterlessCtor(targetType)) → DWARF NoParameterlessConstructor; continue;` — this gate must become "choose a construction strategy".
- `ResolveMembers` resolves writable members (public set/init + public fields) via `WritableMembers`; completeness diagnostics come from `ReadOnlyMembers`. Each member → `TryResolveConversion`.
- `MapMethodModel` carries `EquatableArray<MemberMap> Members`. `MemberMap(TargetName, SourceName, ConverterMethod, NullHandling)`.

---

## Task 18.1: Constructor selection + model
- [ ] Add `Pipeline/ConstructorSelector.cs`: `static IMethodSymbol? Select(INamedTypeSymbol target, List<DiagnosticInfo> diagnostics, LocationInfo? loc, out bool useObjectInitializerOnly)`.
  - Candidate ctors = `target.InstanceConstructors` where `DeclaredAccessibility == Public`, `!IsStatic`, **`!IsImplicitlyDeclared`** (drops the record copy-constructor `R(R original)` and the implicit struct parameterless ctor), and NOT a copy constructor (single param whose type == target). Also skip ctors marked `[Obsolete]`.
  - Selection policy (deterministic):
    1. If exactly one candidate is annotated `[DwarfMapperConstructor]` → use it. (>1 annotated → `DWARF025` ambiguous.)
    2. Else if an accessible **parameterless** ctor exists → return it with `useObjectInitializerOnly = true` (the EXISTING path; no behavior change for current targets).
    3. Else if exactly one non-parameterless candidate → use it.
    4. Else if multiple non-parameterless candidates → pick the one with the **most parameters**; if there is a tie for the max → `DWARF025 AmbiguousConstructor` (Error) and skip the method.
    5. No candidate at all → `DWARF026 NoMappableConstructor` (Error).
  - Return the chosen ctor; `useObjectInitializerOnly` true only for case 2.
- [ ] Add `[DwarfMapperConstructor]` attribute (AttributeTargets.Constructor) to `src/DwarfMapper/`. SPDX header. Document it.
- [ ] Extend `MapMethodModel` with `EquatableArray<MemberMap> ConstructorArguments` (ordered by ctor parameter position; empty when `useObjectInitializerOnly`). Update both construction sites (regular + projection) — projection passes empty.
- [ ] Add diagnostics `DWARF024 ConstructorParameterUnmapped` (Error: "Constructor parameter '{0}' of '{1}' has no mappable source member"), `DWARF025 AmbiguousConstructor` (Error), `DWARF026 NoMappableConstructor` (Error) to `DiagnosticDescriptors` + `AnalyzerReleases.Unshipped.md`.
- [ ] Commit `feat(gen): constructor selection + [DwarfMapperConstructor] + model/diagnostics (no emit yet)`.

## Task 18.2: Resolve constructor arguments + remaining members
- [ ] In `MapperExtractor.Extract`, replace the parameterless-ctor gate (line ~102) with: `var ctor = ConstructorSelector.Select(targetType, diagnostics, methodLocation, out var objInitOnly); if (ctor is null) continue;`.
- [ ] New `ResolveConstructorArguments(ctor, sourceType, …, explicitMaps, ignores, …)`:
  - For each ctor parameter (in order): find the source member by name using the SAME matching rules as `ResolveMembers` — honoring `CaseInsensitive`, `[MapProperty(src, paramName)]` (target name == parameter name), and flatten roots. Param matching is case-insensitive **iff** `CaseInsensitive` is set (mirror member matching; do not silently relax).
  - Resolve via `TryResolveConversion(source.Type, param.Type, useMethod, …)`. If it resolves → add `MemberMap(TargetName: param.Name, SourceName, ConverterMethod, NullHandling)` to the ordered ctor-arg list. If NOT (no source member, or no conversion) → `DWARF024` at `methodLocation` naming the parameter + target type, and mark the method failed (skip emit). Every parameter MUST resolve.
  - Return the ordered ctor-arg `MemberMap[]` + the SET of parameter names consumed (case-normalized).
- [ ] `ResolveMembers` for the object-initializer part must now **exclude** any writable/positional member whose name matches a consumed ctor parameter (records expose positional members as init props too — do not double-assign). Pass the consumed-param name set in. Completeness diagnostics for writable members exclude consumed params. Read-only members that are satisfied by a ctor param are considered mapped (no diagnostic).
- [ ] When `objInitOnly` → ctor-arg list empty, behavior identical to today (regression-free).
- [ ] Commit `feat(gen): resolve constructor arguments (mandatory; DWARF024 on unmapped param) + dedupe initializer members`.

## Task 18.3: Emit `new T(args) { inits }`
- [ ] Refactor the value-expression building in `MapEmitter` into `static void AppendValueExpression(StringBuilder, MemberMap, string paramName)` (covers NullableProject, converter+nullhandling, converter-only, plain). Use for both ctor args and initializer members.
- [ ] Emit construction:
  - ctor args present: `new {ReturnTypeFullName}(\n  {p0}: <expr>,\n  {p1}: <expr>… )` (use named arguments `paramName: value` for clarity + safety against reordering).
  - initializer members present: append `\n{ {Member} = <expr>, … }`.
  - both → `new T(args) { inits }`; only inits → `new T { inits }` (current); only args → `new T(args)`; neither → `new T()` (object with no mappable members — keep existing behavior/diagnostic).
  - Preserve the `var __dwarf_target = …` form when after-hooks exist; ctor form works identically.
- [ ] Projection path: if a projection target is constructor-only, emit `new Dto(args)` with **directly-assignable params only** (projection stays converter-free — if a param needs a converter/enum/etc., emit the existing projection-unsupported diagnostic `DWARF019`). If parameterless+initializer works, keep current. (Keep projection scope minimal; record projection with simple params is the valuable case — EF Core translates constructor projections.)
- [ ] Commit `feat(gen): emit constructor invocation with named args + object-initializer tail`.

## Task 18.4: Exhaustive defensive tests (runtime + generator)
Runtime (`tests/DwarfMapper.IntegrationTests/ConstructorMappingRuntimeTests.cs`) + generator (`tests/DwarfMapper.Generator.Tests/ConstructorMappingTests.cs`). Every case green:
- [ ] **positional record → positional record** (identical shape): all members via ctor; values preserved.
- [ ] **class source → positional record target**.
- [ ] **record with extra init/set props** beyond positional params: ctor for positional, initializer for the rest; both preserved; positional member NOT double-assigned (assert generated uses named ctor arg, not also in initializer).
- [ ] **ctor param needing conversion**: `long`→`int` ctor param (CreateChecked; in-range preserved, overflow throws), `string`→`Guid` ctor param (IParsable), enum ctor param, `int?` ctor param (`T?→U?` null-preserving).
- [ ] **constructor-only class** (no parameterless ctor, no setters) → maps.
- [ ] **`record struct`** and **`readonly record struct`** targets.
- [ ] **readonly struct with explicit ctor** target.
- [ ] **`required` members + ctor combo** (required prop set via initializer alongside ctor args).
- [ ] **regression**: class with parameterless ctor + settable props still uses object initializer (assert generated has NO ctor args), all existing tests green.
- [ ] **`[MapProperty(src, "ctorParamName")]`** renames a source member onto a ctor parameter.
- [ ] **CaseInsensitive** ctor-param matching only when `[DwarfMapper(CaseInsensitive=true)]`.
- [ ] **DWARF024**: a ctor param with no matching source member → build error (assert diagnostic id + parameter name).
- [ ] **DWARF025**: class with two same-arity non-parameterless ctors, none annotated → ambiguous error; adding `[DwarfMapperConstructor]` resolves it.
- [ ] **copy-constructor exclusion**: `record R(int X)` → same `record R` must map via the positional ctor (X copied), NOT pick the implicit copy ctor and skip; assert X preserved.
- [ ] **completeness still holds**: a settable non-ctor target member left unmapped still errors (existing DWARF).
- [ ] **nested**: a ctor param whose type is itself a mapped record (auto-discovered nested mapper invoked for the arg).
- [ ] **fuzzer**: confirm both existing fuzz suites still green; (optional) add a record-shaped variant to `SyntheticSchema` if low-risk, else note deferral.
- [ ] Commit `test: exhaustive constructor/record mapping coverage (records, structs, conversions, ambiguity, completeness)`.

## Task 18.5: AOT + docs
- [ ] Add a positional-record target (with a converted ctor param) to `samples/DwarfMapper.AotSample`; run the AOT publish (report IL2xxx/IL3xxx status — named-arg ctor calls are concrete, AOT-safe).
- [ ] README + spec: document constructor/record/immutable target support, the selection policy, `[DwarfMapperConstructor]`, and that every ctor parameter is mandatory (DWARF024). Update feature list/roadmap.
- [ ] Commit `docs+test: AOT-gate record mapping; document constructor target support`.

---

## Self-Review
- Records / immutable targets supported; ctor params mandatory (DWARF024) — completeness thesis extended. ✅
- Conversions (CreateChecked/IParsable/enum/nullable/T?→U?) apply uniformly to ctor args via shared `TryResolveConversion` + `AppendValueExpression`. ✅
- Object-initializer path unchanged for existing targets (regression-free); positional members not double-assigned. ✅
- Copy-constructor excluded; ambiguity is a build error with `[DwarfMapperConstructor]` escape hatch. ✅
- AOT-safe (concrete named-arg ctor calls). ✅

## Deferred
- Constructor-param projection with converters (EF) — directly-assignable only for now (DWARF019 otherwise).
- `PreferParameterlessConstructors=false`-style tuning; obsolete-ctor opt-in; partial ctor + init for the same member by choice.
- Tuple targets; `with`-expression / non-destructive mutation mapping.
