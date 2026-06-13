<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Feature-gap roadmap — closing the configuration-richness gap

DwarfMapper leads on graph integrity, AOT/SIMD fast-paths, and compile-time guarantees, but trails the
runtime-flexible mappers on *configuration convenience*. This document plans the **addressable** gaps
(see [COMPARISON.md](COMPARISON.md)) as a sequence of small, independently-shippable phases — each adds a
feature **plus its compile-time diagnostics** in DwarfMapper's "no silent surprises" style.

The design is **inspired primarily by Riok.Mapperly** (MIT, and our architectural twin — a Roslyn
incremental generator with compile-time diagnostics), with Mapster (MIT) as a secondary reference. We
study *semantics and API shape only* — no competitor code is copied. AutoMapper is referenced
conceptually (publicly-documented behaviour) and never as a code source.

## Guiding rules (apply to every phase)

1. **Compile-time, reflection-free, AOT-safe** — every feature emits concrete code; nothing defers to
   runtime reflection or expression trees.
2. **No silent surprises** — anything that could mislink, lose data, or throw at runtime must be a
   build diagnostic (Info suggestion → escalatable to Error), never silent.
3. **POCOs stay attribute-free** — configuration lives only on the mapper class/method.
4. **Deliberate non-goals stay out** (they conflict with the thesis): runtime reconfiguration,
   `object`/`dynamic`/anonymous-type mapping, open-generic maps, and arbitrary expression-literal
   conditions (an attribute cannot carry a lambda). Where a competitor relies on these, we adopt the
   *named-method* shape that Mapperly proved translates to codegen.

## How a phase is built (recipe, from the pipeline recon)

Each phase follows the same end-to-end path:

1. **Attribute / enum** in `src/DwarfMapper/` (e.g. `MapPropertyAttribute.cs` is the template).
2. **Parse** it in `MapperExtractor.cs` — add a `ReadXyz(ISymbol)` helper near the existing readers
   (`ReadExplicitMaps` ~`:3067`, `ReadIgnores` ~`:3060`, `ReadFlattenGraphAttributes` ~`:3114`), matching
   `attr.AttributeClass?.ToDisplayString()`.
3. **Thread into resolution** — `ResolveMembers` (`:1654`) for member-level config (process explicit
   config *before* auto-matching, as `[MapProperty]` does at `:1718`), or `TryResolveConversion`
   (`:2218`) for new conversion shapes.
4. **Model** — add value-equatable fields to `MemberMap` (`Model/MemberMap.cs`) or `MapMethodModel`.
5. **Emit** — `MapEmitter.AppendValueExpression` (`:826`) for member values; method-shape emitters
   (`:498`/`:529`/`:571`) for new method kinds.
6. **Diagnostics** — add a `DiagnosticDescriptor` singleton in `DiagnosticDescriptors.cs` with the
   **next free** `DWARF###` id (reserved: `DWARF004/006/029`; last used: `DWARF038`), **plus an
   `AnalyzerReleases.Unshipped.md` entry** (the assembly-scan meta-test enforces descriptor↔release sync).
7. **Tests** — generator unit tests via `GeneratorTestHarness.Run`, a Verify snapshot, an integration
   (runtime-behaviour) test, and — to satisfy the self-validation meta-tests — at least one test that
   *triggers each new DWARF id*, a reference to each new attribute/enum value, and a
   `FeatureInteractionCompileMatrix` case.

---

## Phase 1 — Source-member coverage (`[MapIgnoreSource]` + strict-source diagnostic)

**Goal.** DwarfMapper's completeness gate is currently *target-side only* (`DWARF001` errors on an
unfilled target). Add the **source side**: optionally flag a source member that is **consumed by no
target** — the dual anti-mislinking guarantee. This is the single best philosophical fit and is almost
pure diagnostics (no emission change). Mirrors Mapperly's `RequiredMappingStrategy`/`RMG020`.

**API.**
```csharp
public enum RequiredMappingStrategy { Target = 0, Both = 1, None = 2 } // Target = current behaviour (default)
[DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]          // also require every source consumed
[MapIgnoreSource(nameof(Src.Audit))]                                   // suppress for one source member
```
- Default `Target` preserves today's behaviour exactly (non-breaking). `Both` adds source-coverage;
  `None` relaxes both (target gate downgraded to suggestion).
- Severity: unconsumed-source is a **suggestion (Info)** by default even under `Both`, escalatable; the
  target gate stays an error. (Rationale: unused source is usually benign; unfilled target is the bug.)

**Generator.** In `ResolveMembers` (`:1654`), after building `MemberMap`s, compute the set of *consumed*
source names; diff against readable source members minus `[MapIgnoreSource]`; emit the new diagnostic per
leftover. Parse `[MapIgnoreSource]` like `ReadIgnores` (`:3060`).

**Diagnostics.** `DWARF039 UnconsumedSourceMember` (Info; Error when `RequiredMapping=Both` *and* a new
strict flag — or keep Info and let `#pragma`/editorconfig escalate). No emission change.

**Tests.** unused-source triggers DWARF039; `[MapIgnoreSource]` silences it; `Target` default stays
silent; `Both` flags. **Risk: low.** **Depends on: nothing.**

---

## Phase 2 — `[MapValue]` constant & computed values

**Goal.** Assign a **constant** or **computed** value to a target member (and fill it for the
completeness gate) without a fake source member or a hook. Mirrors Mapperly `[MapValue]`.

**API.**
```csharp
[MapValue(nameof(Dto.Source), "api-v2")]            // constant literal
[MapValue(nameof(Dto.CreatedAt), Use = nameof(Now))] // computed: parameterless method, exact return type
private static DateTime Now() => DateTime.UtcNow;
```
- Constant `value` must be an attribute-legal constant assignable to the target type.
- `Use` names a **parameterless** method whose return type **exactly matches** the target.
- A `[MapValue]`'d target counts as mapped (suppresses `DWARF001`); conflicts with `[MapProperty]`/
  `[MapIgnore]` on the same target are diagnosed.

**Generator.** New `MapValueAttribute.cs`; `ReadMapValues` parser; in `ResolveMembers`, process before
auto-match (like `[MapProperty]`); represent as a `MemberMap` with `SourceName=""` and either a literal
expression or the `Use` method as `ConverterMethod`. `AppendValueExpression` (`:826`) already emits
`ConverterMethod()`; add a tiny literal-emit branch.

**Diagnostics.** `DWARF040 MapValueTypeMismatch` (Error — constant not assignable),
`DWARF041 MapValueUseReturnMismatch` (Error — `Use` not parameterless / wrong return),
`DWARF042 MapValueConflict` (Error — same target also `[MapProperty]`/`[MapIgnore]`). (Mirrors RMG077/078.)

**Tests.** constant emits literal; `Use` emits call; each diagnostic fires; completeness satisfied.
**Risk: low.** **Depends on: nothing.**

---

## Phase 3 — Deep source paths in `[MapProperty]` (read side)

**Goal.** Map from a **nested source path** to a target member: `Order.Customer.Name → Dto.CustomerName`,
explicitly. Foundational *read-side* path infrastructure (also unlocks Phase 4 & 7). Mirrors Mapperly's
segment/dotted `[MapProperty]`.

**API.** Add a segment-array overload (canonical — avoids re-parsing and disambiguates dotted names):
```csharp
[MapProperty(new[]{ nameof(Order.Customer), nameof(Customer.Name) }, nameof(Dto.CustomerName))]
[MapProperty("Customer.Name", "CustomerName")]   // convenience dotted form → split on '.'
```

**Generator.** Extend `MapPropertyAttribute` with a `string[]` source overload; teach the parser to carry
segments; add a `ResolveSourcePath` helper that walks segments via `ReadableMembers`, resolving each hop's
type, then resolves the final conversion through `TryResolveConversion` (`:2218`) as today. The emitted
read is null-safe: `src.Customer?.Name` with the member's `NullHandling` deciding throw/default/lift.

**Diagnostics.** `DWARF043 UnknownPathSegment` (Error — a segment isn't a readable member),
`DWARF044 PathNullability` (Info — a nullable hop feeding a non-nullable target; suppressible). Reuse the
existing precedence: immediate member > explicit path (no silent ambiguity).

**Tests.** 2- and 3-hop paths; nullable mid-path; unknown segment → DWARF043; snapshot of the null-safe
read. **Risk: medium** (null-handling along the path). **Depends on: nothing** (enables 4, 7).

---

## Phase 4 — Unflattening (deep **target** paths)

**Goal.** The inverse of `[Flatten]`: `Dto.AddressCity → Entity.Address.City`, synthesizing the
intermediate target instance. Mirrors Mapperly's target-side path + AutoMapper unflatten.

**API.**
```csharp
[MapProperty(nameof(Dto.AddressCity), new[]{ nameof(Entity.Address), nameof(Address.City) })]
[MapProperty("AddressCity", "Address.City")]
```

**Generator.** Target-side path support in the parser/model; emission groups all assignments sharing an
intermediate and instantiates it once with a null-guard: `target.Address ??= new(); target.Address.City =
…;`. The intermediate type must be constructible (parameterless ctor or all-assigned). Multiple leaves
into the same intermediate must coalesce (don't re-`new`).

**Diagnostics.** `DWARF045 UnflattenIntermediateNotConstructible` (Error — no parameterless ctor and not
fully initialized), `DWARF046 AmbiguousUnflatten` (Error — conflicting paths to one intermediate).

**Tests.** single + multi-leaf into one intermediate (one `new`); non-constructible intermediate →
DWARF045; runtime round-trip. **Risk: medium-high** (intermediate lifecycle, coalescing).
**Depends on: Phase 3** (shared path model).

---

## Phase 5 — Additional mapping parameters

**Goal.** Let a mapping method take **extra parameters** used as additional sources:
`partial Dto Map(Entity e, string tenant)` → `tenant` fills `Dto.Tenant`. Mirrors Mapperly exactly.

**API.** Any extra parameter on a `partial` map method is matched to a target by name (precedence:
`[MapProperty]`/`[MapValue]` > extra parameter > by-name member). **Deliberately not propagated into
nested mappings** (Mapperly's tractability rule — keep it).

**Generator.** In the standard-mapping classifier (`:530`), collect parameters beyond the source; register
them as candidate sources in `ResolveMembers` with the right precedence; emit the parameter name directly
as the value expression. Signatures already flow through the method-shape emitters.

**Diagnostics.** `DWARF047 UnusedMappingParameter` (Info — an extra parameter matched no target;
suppressible) and reuse `DWARF001` if a *required* target is only satisfiable by a missing parameter.

**Tests.** extra param fills a target; precedence vs by-name; unused param → DWARF047; nested-mapping does
*not* see the param. **Risk: medium.** **Depends on: nothing.**

---

## Phase 6 — Naming-convention normalization

**Goal.** Match members across naming styles: `snake_case ↔ PascalCase`, configurable prefix/suffix
stripping. Mirrors Mapster's `NameMatchingStrategy.Flexible` / `ConvertSourceMemberName`.

**API.**
```csharp
[DwarfMapper(NameConvention = NameConvention.Flexible)]   // PascalCase ↔ camelCase ↔ snake_case
[DwarfMapper(SourcePrefix = "m_", TargetSuffix = "Dto")]  // strip before matching
```

**Generator.** A pure name-normalization function applied to *both* sides before the existing match in
`ResolveMembers` (`:1769`). **Crucially**, after normalization, diagnose collisions (two source names
normalize to the same target) — the safety check Mapster omits.

**Diagnostics.** `DWARF048 AmbiguousNormalizedMatch` (Error — post-normalization collision). Reuse
existing `DWARF010` (ambiguous case-insensitive) shape.

**Tests.** snake↔Pascal match; prefix/suffix strip; collision → DWARF048; exact-match default unaffected.
**Risk: medium** (interaction with `CaseInsensitive`, flatten). **Depends on: nothing.**

---

## Phase 7 — `[ReverseMap]` (mechanical inversion)

**Goal.** Generate the inverse direction from one declaration, reducing per-pair ceremony. Because we are
a generator (not runtime), scope to **mechanically invertible** config: swap source/target of renames,
invert ignores, and — leveraging Phases 3–4 — invert flatten↔unflatten paths.

**API.**
```csharp
[DwarfMapper]
public partial class M {
    [ReverseMap]                                  // also generate FromDto
    public partial Dto ToDto(Entity e);
    public partial Entity FromDto(Dto d);          // declared (body generated by the reverse)
}
```

**Generator.** When `[ReverseMap]` is present and a matching inverse partial exists, synthesize its member
config by inverting the forward config (rename A→B becomes B→A; flatten path becomes unflatten path via
Phase 4). Non-invertible config (e.g. a `Use=` scalar converter with no inverse) is **not** auto-inverted
— it is diagnosed so the user supplies the reverse explicitly (Mapperly's stance: be explicit rather than
guess).

**Diagnostics.** `DWARF049 NonInvertibleReverseConfig` (Warning — forward config can't be auto-inverted;
declare the reverse member explicitly), `DWARF050 ReverseTargetMissing` (Error — `[ReverseMap]` with no
inverse partial method).

**Tests.** rename inverts; flatten↔unflatten inverts; `Use=` converter → DWARF049; missing inverse →
DWARF050. **Risk: high** (config inversion semantics). **Depends on: Phases 3 & 4.**

---

## Phase 8 — Per-member null substitution & conditional assignment

**Goal.** Ergonomic finishers. (a) A per-member **null substitute** value; (b) **conditional** member
assignment via a named predicate (the only attribute-expressible form).

**API.**
```csharp
[MapProperty(nameof(Src.Name), nameof(Dto.Name), NullSubstitute = "(unknown)")]
[MapProperty(nameof(Src.Bonus), nameof(Dto.Bonus), When = nameof(IsEligible))]
private bool IsEligible(Src s) => s.Tier > 0;
```

**Generator.** `NullSubstitute` → emit `src.X ?? <literal>` (type-checked like `[MapValue]`). `When` → emit
`if (IsEligible(src)) dst.X = …;` (guard the assignment); a conditional target is still "mapped" for the
gate (it may simply retain its default when the predicate is false — documented).

**Diagnostics.** `DWARF051 NullSubstituteTypeMismatch` (Error), `DWARF052 InvalidWhenPredicate` (Error —
not a `bool`-returning method of the source). Reuse `[MapValue]` type-check logic.

**Tests.** substitute emits coalesce; predicate guards assignment; bad predicate → DWARF052.
**Risk: low-medium.** **Depends on: Phase 2** (literal type-check helper).

---

## Sequencing & rationale

| # | Phase | Value | Risk | Depends | New DWARF (indicative) |
|---|---|---|---|---|---|
| 1 | Source coverage `[MapIgnoreSource]` | ★★★ (philosophy) | low | — | 039 |
| 2 | `[MapValue]` constants/computed | ★★★ | low | — | 040–042 |
| 3 | Deep source paths | ★★ | med | — | 043–044 |
| 4 | Unflattening (target paths) | ★★ | med-high | 3 | 045–046 |
| 5 | Additional parameters | ★★ | med | — | 047 |
| 6 | Naming conventions | ★★ | med | — | 048 |
| 7 | `[ReverseMap]` | ★★★ (ceremony) | high | 3,4 | 049–050 |
| 8 | Null substitute / conditional | ★ | low-med | 2 | 051–052 |

**Recommended order: 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8.** Phases 1 and 2 are high-value, low-risk, and
independent — ideal to ship first and establish the per-feature pattern. Phase 3 builds the path
infrastructure that 4 and 7 reuse, so it precedes them. 5 and 6 slot in anywhere (independent). 7 is last
of the structural work (it needs 3+4). 8 is a low-risk ergonomic capstone reusing Phase 2's type-check.

**Per-phase Definition of Done:** attribute/enum + parser + resolution + emission + diagnostics
(descriptor **and** `AnalyzerReleases.Unshipped.md` entry) + unit tests triggering each new id + Verify
snapshot + integration test + `FeatureInteractionCompileMatrix` case + a `COMPARISON.md` matrix update
flipping the row to ✅. Full suite green, 0 warnings, AOT gate passing.

## Non-goals (explicitly out of scope)

Runtime reconfiguration; `object`/`dynamic`/anonymous mapping; open-generic type maps; arbitrary
expression-literal conditions (lambdas in attributes); `IncludeMembers`-style multi-source composition
(single-source is a deliberate constraint — use a hook or a pre-merge step). These conflict with the
compile-time / AOT / single-source thesis and are intentionally left to the runtime mappers.
