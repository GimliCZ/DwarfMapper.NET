# [ISSUE-041] `[FlattenGraph]` flat-node helper leaks CS8601/CS8604 for nullable-ref leaves

| | |
|---|---|
| **Severity** | Critical (hard build break under TreatWarningsAsErrors) |
| **Type** | Loud codegen gap (legal input → un-compilable generated code) |
| **Component** | Generator / MapperExtractor.Flatten |
| **Finding ID** | R7-1 |
| **Affects** | `src/DwarfMapper.Generator/Pipeline/MapperExtractor.Flatten.cs` (`AppendFlatNodeMemberExpr`) |
| **Status** | **CONFIRMED (executed) and FIXED** — commit `4190ace` |

## What
The synthesized `__DwarfMap_FlatNode_*` helper assigned a nullable-reference leaf into a non-nullable DTO member
with no null-forgiving `!`. The generated file is always `#nullable enable`, so this emits **CS8601** ("possible
null reference assignment") regardless of consumer nullable settings, which `TreatWarningsAsErrors` (set in the
repo's own `Directory.Build.props`) turns into a hard build error in code the user cannot edit. A sibling gap:
the converter branch bang'd only when `IsSynthesized(conv)`, so a user-declared leaf converter with a
non-nullable ref parameter hit **CS8604** — the same `IsSynthesized`-as-proxy hole the nested-nullable fix
already had to replace.

Same class as the nested-nullable CS8604 bug (ISSUE-040-adjacent), in the `[FlattenGraph]` path, and a **corpus
hole**: existing FlattenGraph tests use non-nullable `string Name = ""` leaves, so the golden manifest never
exercised a nullable-ref leaf.

## Verification (executed, this repo)
`[FlattenGraph("Entries","Nodes")]` over `Node { string? Name }` → `NodeDto { string Name }` emitted
`Name = n.Name`; `GeneratedCodeWarnings` reported **CS8601** at the flat-node helper. Regression test watched
failing before the fix, passing after.

## Fix (applied)
Compute the null-forgiveness decision at the call site (`FlatLeafNeedsBang`) from the leaf and DTO-member
nullability, and pass it into `AppendFlatNodeMemberExpr`. Direct-assign case → forgive when
`NullRefIntoNonNullableRef(leaf, dtoMember)`; converter case → `IsSynthesized(conv)` OR
`ConverterParamIsNonNullableRef(conv,…)` (the recovered fact behind the proxy), so a null-tolerant user
converter is not forgiven. Applied at both call sites (homogeneous + heterogeneous arm). Golden manifest
unmoved; 0 warnings solution-wide.

## Regression test
`FlattenNullableLeafTests` — asserts no CS8601 via `GeneratedCodeWarnings` (CS8601 is a warning, so
`RunAndGetCompilationErrors` would miss it; this also avoids growing the direct-compile-error ratchet).
