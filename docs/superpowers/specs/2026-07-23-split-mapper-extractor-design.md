# Design: split `MapperExtractor.cs` into partial-class files

- **Date:** 2026-07-23
- **Status:** IMPLEMENTED and MERGED
- **Component:** `DwarfMapper.Generator` (the `[DwarfMapper]` class-model engine)

## 1. Problem

**Sub-project 4 of four** in the generator maintainability programme:

1. ~~generator testing framework~~ — landed (`4e79370`)
2. ~~shared engine core~~ — landed (`3b62b1a`)
3. ~~emission layer~~ — landed (`48b392b`)
4. **split `MapperExtractor.cs`** ← this spec

`Pipeline/MapperExtractor.cs` is **7,154 lines** — the largest file in the generator and the one every prior
sub-project routed around. It holds ~85 private static helpers behind two public entry points. A file this size
does not fit in a reviewer's or a model's working context at once; edits to it are the least reliable in the
codebase, and every earlier sub-project deferred it for that reason.

## 2. The enabling measurement

The June correctness-first spec (Phase D) recorded a blocker: `synthesized` and `nestedRegistry` are created
once per class and cross-method dedup through them prevents duplicate-member (`CS0111`) errors *in the generated
code*, "so methods are NOT independent units." That is true — **for extracting methods into separate classes**,
which would sever the parameter threading. It measured the wrong refactor.

Measured facts (roslyn-lens `get_type_overview` + `get_file_overview`, Grep tool):

- The type is already `internal static partial class MapperExtractor`, and a sibling partial file
  **already exists** — `MapperExtractor.MapConfig.cs` (287 lines). The pattern is proven in this exact type.
- It has **no static fields**. `synthesized` (`Dictionary<string, SynthesizedMethod>`) and `nestedRegistry`
  (`NestedMappingRegistry`) are **method-local**, created at `MapperExtractor.cs:186` and `:198` inside
  `ExtractCore`, and threaded to every helper **as parameters, by reference**. The dedup works because each
  helper receives the *same instance* — a property of parameter passing, not of living in one file.

A **partial-class file split therefore compiles to identical IL** and is behaviour-neutral by construction.
`CS0111` cannot be reintroduced by moving a method to another file of the same `partial class` — it fires on
duplicate *declarations*, and a cut-paste move creates none.

This is the fact that makes this sub-project low-risk, and it was measured before designing rather than assumed.

## 3. Goals / Non-goals

**Goals**

- Break `MapperExtractor.cs` into focused partial-class files, each holding one cluster of related helpers.
- Keep the split **behaviour-neutral**: the 973-case golden manifest must not move; every fingerprint identical.
- Leave the `static partial class` header, namespace, and all method signatures byte-identical — only the file
  a method lives in changes.

**Non-goals**

- **Changing any method body.** No logic edits, no signature changes, no renames. Cut and paste only.
- **Splitting `ExtractCore` itself.** It is a single ~2,154-line method (`:61`–`:2215`). Decomposing one method
  is a different, riskier task; this sub-project moves *whole methods between files*, it does not carve up a
  method body. `ExtractCore` stays intact in the root file.
- **Migrating this file's emission to `CodeWriter`.** It still contains hand-rolled `\n` emission (sub-project 3
  deferred it deliberately). That migration becomes tractable *after* the split localises the emitting helpers,
  but it is out of scope here — one behaviour-neutral refactor at a time, each guarded by the manifest.
- Extracting any helper into a *separate class*. That severs the by-reference parameter threading the dedup
  depends on; the whole point of §2 is that we do **not** do this.

## 4. Architecture

The type stays one `internal static partial class MapperExtractor` in `DwarfMapper.Generator.Pipeline`, spread
across files by responsibility. Every new file carries the same header:

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using …;                       // only the usings that file needs
namespace DwarfMapper.Generator.Pipeline;
internal static partial class MapperExtractor { … }
```

### 4.1 File boundaries (measured clusters)

| File | Holds | Approx. source lines |
|---|---|---|
| `MapperExtractor.cs` (root) | entry points `Extract`/`ExtractGenerateMapHost`, `ExtractCore` orchestration, enums | ~2,200 |
| `MapperExtractor.Members.cs` | `ResolveMembers`, `ResolveConstructorArguments`, `InheritanceDepth`, `DetectAmbiguousInterfaceArms`, `BuildDispatchWrapperCode` | ~920 |
| `MapperExtractor.Conversions.cs` | `TryResolveConversion`, `IsMappableObjectPair`, collection/enumerable + nullable/null-ref predicates, `HasImplicitConversion` | ~950 |
| `MapperExtractor.Flatten.cs` | `TryResolveSourcePath`, `ResolveUnflattenTarget`, `ApplyCollectionKeyUpserts`, flatten-graph resolution, `AppendFlatNodeMemberExpr` | ~1,380 |
| `MapperExtractor.Attributes.cs` | the `Read*` attribute-reader family (~30 small methods), `NormalizeName`, `AddConsumed` | ~700 |
| `MapperExtractor.Pairs.cs` | `ReadPair*` readers, `MatchPairIgnores`, `CollectFactoryExcludedMembers`, and the nested `PairProp`/`PairIgnore`/`PairConstructor`/`PairValue` data classes | ~430 |
| `MapperExtractor.Projection.cs` | `ResolveProjectionMembers`, `ResolveProjectionExpr`, `ResolveProjectionNestedObjectExpr`, `ResolveProjectionCtorExpr`, projection predicates | ~730 |
| `MapperExtractor.Diagnostics.cs` | `EmitDWARF028`, `EmitImplicitConversionDiag`, `AccessibilityText`, `CollectRoundTrips`, `ExpandWrapperMaps`, `RenderConstantLiteral`/`TryFormatConstant` | ~700 |
| `MapperExtractor.MapConfig.cs` (exists) | unchanged | 287 |

Boundaries follow the call-graph clusters measured in the file, not an imposed taxonomy. Exact line spans are
in the implementation plan; the table is the shape.

## 5. Testing strategy

**The golden manifest is the whole verification story — identical to sub-project 3.** Each file-extraction task
must leave all 973 fingerprints byte-identical. A moved fingerprint means a method body changed during the move
— investigate, never regenerate. `DWARF_GOLDEN_UPDATE` is never invoked anywhere in this sub-project.

Per task, the oracle is three checks:

1. `dotnet build` — 0 warnings (`TreatWarningsAsErrors`, `AnalysisMode=All`). A missing `using` in a new file
   surfaces here immediately.
2. Full suite green (baseline 5,578).
3. `git diff` on `Golden/output-manifest.txt` is empty.

No new tests are required: this refactor adds no behaviour to test, and the manifest already fingerprints every
path these helpers serve. A ratchet on file size is considered and **rejected** — an arbitrary line ceiling
would fail spuriously as the file legitimately grows; the split's durability comes from the cluster boundaries,
not a number.

## 6. Risks

| Risk | Mitigation |
|---|---|
| A move accidentally edits a body | Cut-paste only; golden manifest checked per task; never regenerated |
| A new file misses a `using` | `dotnet build` at 0 warnings per task catches it at compile time |
| A helper is moved to a file whose other members it doesn't relate to | Boundaries are the measured call-graph clusters; borderline helpers stay with their dominant caller |
| Merge churn against in-flight work | One partial file per commit, small reviewable diffs, on a dedicated branch |

## 7. Out of scope

- Splitting `ExtractCore` into smaller methods (a behavioural-risk refactor; separate future work).
- Migrating this file's hand-rolled emission to `CodeWriter` (tractable after this split; not part of it).
- The `[MapTo]` registry (`MapToGenerator`) — already small and already on the shared core.
