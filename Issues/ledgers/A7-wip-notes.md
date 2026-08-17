<!-- SPDX-License-Identifier: GPL-2.0-only -->

# A7 (D1 / D2 / D16) — findings and decisions, interrupted mid-task

**State: generator work COMPLETE and measured; documentation sync NOT done.** `dotnet build
DwarfMapper.NET.sln` is 0/0 and the surface matrix is **865/865 green**. The doc/self-validation
scans (Scan7, Scan9, `DiagnosticCoverageRatchetTests`) will FAIL until the remaining files listed
under "What is left" are written.

## The brief's premise for D2 is wrong, and it was found by running

`DWARF038` is **`ImplicitConversionApplied`**, not a refusal of `[MapProperty]`. Measured message at
CreateMap:

> `DWARF038:Warning: Member 'Name': implicit parse/format (string↔T) conversion int → string? is applied …`

So `[MapProperty("Id", "Name")]` is **honoured** at CreateMap and UpdateInto; the diagnostic is an
artifact of the probe fixture, where `Id` is `int` and `Name` is `string?`. The surface probe classifies
"any added diagnostic" as `Refused` before it compares output, which is why the cell *read* Refused.

**Consequence:** D2 is not "a diagnostic that protects three overloads is absent from the other two".
D2 is **the same shape as D1** — an unscoped member directive that the element-wise endpoints drop.
`Issues/round20/SURFACE-MATRIX-FINDINGS.md` still carries the wrong story for D2 and must be corrected.

## D16 is a SEPARATE fix, and the evidence is a shipped stack overflow

`CollectHooks` scanned every method on the mapper class and accepted anything whose signature fitted —
including the partial **mapping method declarations themselves**. Measured generated body for
`[AfterMap]` on `public partial void Update(Src s, Dst d)`:

```csharp
public partial void Update(global::Demo.Src s, global::Demo.Dst d)
{
    …
    d.Tag = s.Tag;
    Update(s, d);          // ← unconditional infinite recursion
}
```

The matrix scored that cell **Honoured**. Three different wrong answers for one mistake, purely by
signature shape: `DWARF018` at CreateMap/Projection/AsyncStream (non-void), silence at SpanMap
(fits the 2-param after-hook, never invoked), recursion at UpdateInto.

**Not a propagation gap.** Fixed in `CollectHooks`, before the signature check: a partial method with
no implementing part has no body — C# erases it and every call to it, and where the generator supplies
the missing part the call re-enters the method being generated. Neither is what `[AfterMap]` means.

## D2's remedy: refuse, not honour — and the refusal has a working replacement

Propagation was rejected for the reason `DWARF077` already states: the synthesized element mapper is
keyed by `(source, target)` and shared by every route to that pair, so one method's unscoped directive
would silently re-configure a nested mapping another method owns.

The decisive measurement is that the **pair-scoped twins already work at these endpoints**:

| Case | SpanMap | AsyncStream |
| --- | --- | --- |
| `[MapIgnore<Dst>("Id")]` on the class | **Honoured** | **Honoured** |
| `[MapProperty<Src,Dst>("Id","Name")]` on the class | applied (DWARF038 rides along) | applied |
| `[MapIgnore("Id")]` unscoped | was Silent | was Silent |
| `[MapProperty("Id","Name")]` unscoped | was Silent | was Silent |

So the diagnostic names the exact replacement text rather than withdrawing a capability.

## Severity is load-bearing: Warning, not Error

An **Error** suppresses the class's emission → the partial method is unimplemented → `CS8795` → the
cell lands in `NotCompilable`, whose ceiling is **shrink-only**. An Error would therefore have *raised*
a ratchet, which this branch forbids. `Warning` → `Refused`, which is unratcheted, and
warnings-as-errors keeps it loud in-repo. Same reasoning `DWARF088`/`DWARF089` recorded.

## What was changed

- `Diagnostics/DiagnosticDescriptors.cs` — **DWARF090** `DirectiveNotAppliedElementWise` (Warning),
  **DWARF091** `HookOnMethodWithNoBody` (Warning).
- `Pipeline/MapperExtractor.cs` — new `ReportElementWiseDirectiveGaps`, which **hoists the duplicated
  DWARF077 check** out of the span and async-stream branches (it was written twice) and adds the two
  directives this task closes. Reuses `ReadIgnores` / `ReadExplicitMaps` — the readers resolution uses
  — so it reports exactly what is dropped, and inherits their null-hardening.
- `Pipeline/MapperExtractor.Conversions.cs` — `CollectHooks` refuses a hook on a bodiless partial.
- `Contracts/DeclaredDivergences.cs` — D1, D2, D16 deleted (replaced by resolution notes).
- `Contracts/SurfaceParityTests.cs` — ceilings lowered to re-measured values.

## Malformed input (the A4 regression class) — probed, no regression

Measured at all four endpoints: `[MapIgnore(null)]`, `[MapProperty(null, null)]`,
`[MapProperty("Id", null)]` → **no diagnostic, no CS error, no generator crash**.
`[MapProperty(null)]` → `DWARF088` with the existing `…` placeholder (the A4 fix, intact). The new gate
never parses attribute arguments itself; it goes through the two hardened readers, which is why.

## Re-measured ceilings (`SurfaceParityTests`)

| Ceiling | Was | Now |
| --- | ---: | ---: |
| `DivergenceFindingCeiling` | 18 | **15** |
| `DivergentCellCeiling` | 93 | **84** (D1 4 + D2 4 + D16 1 = 9 cells closed) |
| `NotCompilableCellCeiling` | 107 | **99** (8 cells CS8795 → Refused: 3 AfterMap + 5 BeforeMap) |
| `UnaskableCellCeiling` | 44 | 44 |
| `UnhonouredButLoudCellCeiling` | 14 | 14 |
| `NoSuchSiteCellCeiling` | 137 | 137 |
| `StructurallyExcusedCellCeiling` | 12 | 12 |

No ceiling was raised. All measured by setting each to 1 and reading the assertion counts, then
restored; matrix re-run green at 865/865 with the values above.

## Behaviour change to note

`[AfterMap]` at **UpdateInto** flips `Honoured → Refused`. That "honour" was the stack overflow above,
so this is a fix, not a loss. `[BeforeMap]`/`[AfterMap]` on a partial mapping method now report one
coherent diagnostic at all five endpoints instead of `DWARF018`'s signature complaint at three.

## What is left (in order)

1. `CHANGELOG.md` — `### Added` entries for DWARF090 and DWARF091 (**Scan9 fails the build without them**).
2. `docs/diagnostics.md` — a prose section each, anchored `#dwarf090` / `#dwarf091` (Scan7).
3. `docs/generated/diagnostics-index.md` — self-heals on the first `GeneratedDocsAreCurrentTests` run;
   run it, review, commit the regenerated file.
4. `tests/DwarfMapper.NegativeCases/Cases/DWARF090_*.cs` and `DWARF091_*.cs` — required by
   `DiagnosticCoverageRatchetTests` (a **sixth** obligation the "five files" list does not cover).
   Model them on `DWARF089_MisplacedDirectiveOnCoLocatedHostMember.cs`.
5. `Issues/round20/SURFACE-MATRIX-FINDINGS.md` — resolution notes for D1/D2/D16, the corrected D2
   story above, and the ratified counts (18 → 15 findings, 93 → 84 cells).
6. Full `dotnet test` on the solution + `dotnet build DwarfMapper.NET.sln`.

No hit in `samples/`, `tests/` or `benchmarks/` for either new diagnostic — grepped for class-level
`[MapIgnore("…")]` on classes with span/async methods and for hook attributes on partial methods; there
are none, so no migration is owed.
