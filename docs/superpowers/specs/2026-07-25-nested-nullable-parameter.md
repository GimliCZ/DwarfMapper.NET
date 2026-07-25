# Investigation: nullable nested reference → user-declared map method emits CS8604 with no diagnostic

- **Date:** 2026-07-25
- **Status:** Characterized; fix designed; NOT yet implemented
- **Branch:** `investigate/nested-nullable-cs8604`
- **Component:** `DwarfMapper.Generator` — `MapEmitter` + member resolution
- **Severity:** Medium correctness (a "never silent" violation: uncompilable output, no DWARF signal)

## 1. Symptom

```csharp
public class NestedSrc { public FlatSrc? Inner { get; set; } }   // nullable nested reference
public class NestedDst { public FlatDst Inner { get; set; } = new(); }

[DwarfMapper]
public partial class M
{
    public partial FlatDst MapFlat(FlatSrc s);      // user-declared, non-nullable parameter
    public partial NestedDst MapNested(NestedSrc s);
}
```

Generated:

```csharp
public partial NestedDst MapNested(NestedSrc s)
{
    global::System.ArgumentNullException.ThrowIfNull(s);
    return new NestedDst { Inner = MapFlat(s.Inner) };   // s.Inner is FlatSrc? → CS8604
}
```

`MapFlat(s.Inner)` passes a nullable reference into a non-nullable parameter → **CS8604**. It is a nullable
*warning* in a plain compilation (verified via `GeneratedCodeWarnings`), which this repo's
`TreatWarningsAsErrors` escalates to a build error — so a strict consumer's build breaks on generated code they
cannot edit. **No DWARF diagnostic is emitted** to explain it. For a
generator whose thesis is "never silent," emitting uncompilable code with no actionable message is the defect —
the failure is not the CS8604 itself but the absence of a DWARF070-style signal against the user's own DTO.

Observed live while making a benchmark's nested reference nullable (2026-07-24). Auto-nesting (no declared
`MapFlat`, synthesized `__DwarfMap_Obj_` helper) does NOT reproduce it — see §3.

## 2. Root cause

`MapEmitter` decides whether to null-forgive a converter argument (`MapEmitter.cs`, converter-only path):

```csharp
var needsBang = member.ConverterNeedsDepthCtx
                || (member.SourceIsNullableRef && GeneratedNames.IsSynthesized(member.ConverterMethod));
```

`IsSynthesized(name)` is `name.StartsWith("__DwarfMap_")`. It is a **proxy** for "this converter's parameter is
non-nullable and it null-guards internally". The proxy holds for synthesized object mappers (`__DwarfMap_Obj_`)
and deliberately excludes collection/dict helpers (`__DwarfMapColl_`/`__DwarfMapDict_`, which take a *nullable*
parameter and handle null→empty themselves, so they must NOT get a `!`).

The proxy has a hole: a **user-declared** nested map method (`MapFlat`) is not synthesized, so
`IsSynthesized("MapFlat")` is `false`, `needsBang` is `false`, and the emitter writes `MapFlat(s.Inner)` with no
`!`. Yet `MapFlat`'s parameter is non-nullable exactly like a synthesized object mapper's. The proxy conflates
"synthesized" with "non-nullable parameter", and user-declared object maps fall through the gap.

Correspondingly, member resolution never sets `NullRefIntoNonNullable` for this member (`IsDirectNullRefAssign`
returns false whenever a converter is present), so no DWARF070 fires either.

## 3. Why auto-nesting is safe

An auto-synthesized nested mapper is named `__DwarfMap_Obj_…`, so `IsSynthesized` is true, `needsBang` is true,
and the emitter already writes `__DwarfMap_Obj_…(s.Inner!)`. The synthesized helper null-guards internally. Only
the **explicitly-declared** nested-map path is broken.

## 4. Why the fix is not a one-line `!`

The tempting fix — null-forgive every non-collection converter — is wrong. A user **conversion operator**
supplied via `[MapProperty(Use = "Conv")]` may be *declared to take a nullable parameter* and be legitimately
null-tolerant:

```csharp
static string Conv(FlatSrc? s) => s?.ToString() ?? "";   // null-tolerant by design
```

Emitting `Conv(s.Inner!)` there forgives — and drops — a null the user's converter was written to accept,
changing behavior. `IsSynthesized` was conservative precisely to avoid this. So the emitter must decide from the
**converter parameter's actual nullability**, not from a name prefix.

## 5. Proposed fix

1. **Thread converter-parameter nullability onto `MemberMap`.** Resolution (`TryResolveConversion` /
   `ResolveConstructorArguments`) holds the converter `IMethodSymbol`; record a
   `ConverterParamIsNonNullableRef` flag when the resolved converter's parameter is a non-nullable reference
   type. Synthesized object mappers set it true; collection/dict helpers false; a user conversion operator sets
   it from its own declared parameter.
2. **Emitter:** `needsBang = ConverterNeedsDepthCtx || (SourceIsNullableRef && ConverterParamIsNonNullableRef)`.
   This replaces the `IsSynthesized` proxy with the fact it stood in for, so user-declared nested maps get the
   `!` and null-tolerant user converters do not.
3. **Emit DWARF070** for the nested nullable→non-nullable member, so the compile-time signal is present, exactly
   as the scalar raw-assign path already does. The existing message fits verbatim ("make the destination member
   nullable / SkipNullSourceMembers / NullSubstitute"). At runtime a genuine null then throws
   `ArgumentNullException` loudly inside the callee's `ThrowIfNull`, rather than corrupting silently.

## 6. Verification plan

- Un-skip `NestedNullableParameterTests` (both assertions) — they are the red phase.
- Golden manifest: this changes emission on a path the 973-case corpus does not cover, so the manifest should
  stay **unmoved**; if any fingerprint moves, an auto-nest or user-converter case was affected — investigate,
  never regenerate.
- Add a positive test that a null-tolerant user conversion operator (nullable parameter) does NOT gain a `!`.
- Full suite green, 0 warnings.

## 7. Scope

- In scope: the explicitly-declared nested-map member path and its diagnostic.
- Out of scope: changing DWARF070's severity or the null-strategy contract for value types; auto-nesting (already
  correct); collection/dict element nullability (separate emission path, takes nullable params by design).
