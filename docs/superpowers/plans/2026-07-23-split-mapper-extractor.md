# Split `MapperExtractor.cs` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. One implementer per
> task, sequential (never parallel — every task edits the shrinking root file). Steps use `- [ ]` tracking.

**Goal:** Break the 7,154-line `Pipeline/MapperExtractor.cs` into focused partial-class files, byte-identical output.

**Architecture:** `internal static partial class MapperExtractor` stays one type across multiple files, split by
the measured call-graph clusters. Cut-and-paste of whole methods only — no body edits, no signature changes,
no renames. Each task moves one cluster into a new partial file. Spec:
`docs/superpowers/specs/2026-07-23-split-mapper-extractor-design.md`.

**Tech Stack:** C# 14 / .NET 10 runtime, `netstandard2.0` generator (do NOT retarget). Roslyn incremental generator.

## Global Constraints

- **Behaviour-neutral by construction.** All 973 golden fingerprints must stay identical. A moved fingerprint
  means a body changed during the move — investigate, never regenerate.
- **NEVER set `DWARF_GOLDEN_UPDATE` and never edit `tests/DwarfMapper.Generator.Tests/Golden/output-manifest.txt`.**
- **Cut-paste only.** Do not reformat, re-order members within a moved block, rename, or change a signature.
  Move each method verbatim, including its XML doc comment and any `// NOTE`/attribute lines directly above it.
- Every new file begins exactly:
  ```csharp
  // SPDX-License-Identifier: GPL-2.0-only

  <only the using directives the moved code needs>

  namespace DwarfMapper.Generator.Pipeline;

  internal static partial class MapperExtractor
  {
      <moved members>
  }
  ```
- Build must stay at **0 warnings** (`TreatWarningsAsErrors`, `AnalysisMode=All`, `EnforceCodeStyleInBuild`).
  Watch IDE0005 (unnecessary using) — a new file must import only what it uses; and the ROOT file may now have
  an unused using after a cluster leaves — remove those from the root too.
- The nested data classes `PairProp`/`PairIgnore`/`PairConstructor`/`PairValue` (`MapperExtractor.cs:7111-7154`)
  are `MapperExtractor`-scoped types; move them inside the partial, not to top level.

## The per-task oracle (identical for every task)

Each task's verification is these three checks — no per-task test code, because the refactor adds no behaviour:

1. `dotnet build` → **0 Warning(s), 0 Error(s)**.
2. `dotnet test` → full suite green (baseline **5,578**; must not drop).
3. `git diff --stat -- tests/DwarfMapper.Generator.Tests/Golden/output-manifest.txt` → **empty**.

Only when all three hold: `git add` the two changed files (root + new partial) and commit
`refactor: house <cluster> in MapperExtractor.<File>.cs`.

---

### Task 1: `MapperExtractor.Members.cs`

**Move (by name, verbatim):** `ContainingTypeDeclarations`, `ResolveMembers`, `ResolveConstructorArguments`,
`InheritanceDepth`, `DetectAmbiguousInterfaceArms`, `BuildDispatchWrapperCode` (root lines ~2216–3174).

- [ ] Create the new file with the standard header; move the six methods verbatim.
- [ ] Fix usings in both files (add what the new file needs; drop any now-unused in root).
- [ ] Run the oracle (build / suite / manifest-empty). Commit.

### Task 2: `MapperExtractor.Conversions.cs`

**Move:** `TryResolveConversion`, `IsMappableObjectPair`, `BaseTypesInAssembly`, `HasDerivedTypesInCompilation`,
`AllTypesIn`, `IsAbstractOrInterfaceAutoNestSource`, `ImplementsIEnumerable`, `IsUnsupportedCollectionTarget`,
`HasImplicitConversion`, `IsNullableValue`, `IsNullableReferenceType`, `SourceMayBeNullRef`,
`NullRefIntoNonNullableRef`, `IsDirectNullRefAssign` (root ~3175–4122; leave the Before/After-hooks tuple method
at 3973 with whichever cluster it sits in after the moves — if unsure, keep in root and note it).

- [ ] Move verbatim; fix usings both files; run oracle; commit.

### Task 3: `MapperExtractor.Attributes.cs`

**Move the `Read*` attribute-reader family and helpers:** `ReadIgnores`, `ReadIgnoreSources`, `ReadStringFormats`,
`ReadFlattenRoots`, `ReadReinterpretMembers`, `ReadEnumStrategy`, `ReadNullStrategy`, `ReadCaseInsensitive`,
`ReadGenerateExtensions`, `ReadMaxDepth`, `ReadAutoNest`, `ReadAutoMatchMembers`, `ReadIgnoreObsoleteMembers`,
`ReadSkipNullSourceMembers`, `ReadAllowNonPublic`, `ReadNullCollections`, `ReadReferenceHandling`, `ReadOnCycle`,
`ReadImplicitConversions`, `ReadRequiredMapping`, `ReadNameConvention`, `ReadMethodAutoNest`, `HasReverseMap`,
`NormalizeName`, `AddConsumed`, `ComputeRequiredMustInitialize`, `ObsoleteMemberNames`, `IsObsolete`. These are
non-contiguous — gather by name. Leave `ReadPair*` for Task 4.

- [ ] Move verbatim; fix usings both files; run oracle; commit.

### Task 4: `MapperExtractor.Pairs.cs`

**Move:** `ReadPairConstructors`, `ReadPairMapProperties`, `ReadPairIgnores`, `ReadPairMapValues`,
`MatchPairIgnores`, `CollectFactoryExcludedMembers`, and the class-scoped tuple reader at ~5879, **plus** the
nested `PairProp`/`PairIgnore`/`PairConstructor`/`PairValue` data classes (7111–7154).

- [ ] Move verbatim (nested classes stay inside the partial); fix usings; run oracle; commit.

### Task 5: `MapperExtractor.Flatten.cs`

**Move:** `TryResolveSourcePath`, `ResolveUnflattenTarget`, `ApplyCollectionKeyUpserts`, `MemberTypeByName`,
`IsListOfT`, `IsExactNamedTypeHelper`, the flatten-graph directive resolver at ~4707 and `AppendFlatNodeMemberExpr`
(~5667). (root ~4314–5698 minus any attribute readers already taken in Task 3.)

- [ ] Move verbatim; fix usings; run oracle; commit.

### Task 6: `MapperExtractor.Projection.cs`

**Move:** `ResolveProjectionMembers`, `ResolveProjectionExpr`, `IsWideningOrSameWidth`, `ProjectionSourceMayBeNull`,
`ResolveProjectionNestedObjectExpr`, `ResolveProjectionCtorExpr`, `CanReach`, `TryGetSpanElement`,
`TryGetAsyncEnumerableElement`, `IsCancellationToken`, `IsQueryable` (root ~6210–6954).

- [ ] Move verbatim; fix usings; run oracle; commit.

### Task 7: `MapperExtractor.Diagnostics.cs`

**Move:** `EmitDWARF028`, `EmitImplicitConversionDiag`, `AccessibilityText`, `CollectRoundTrips`,
`ExpandWrapperMaps`, `RenderConstantLiteral`, `TryFormatConstant`, `IsEffectivelyPublic`. Whatever remains after
Tasks 1–6 that is not entry/`ExtractCore` sweeps here.

- [ ] Move verbatim; fix usings; run oracle; commit.
- [ ] Final check: root `MapperExtractor.cs` now holds only the enums, `Extract`, `ExtractGenerateMapHost`,
      `ExtractCore`, and any helper deliberately left with it. Confirm file is ~2,200 lines.

---

## Self-review notes

- Method names above were read from the live file; if a name has no match at execution, the file changed —
  stop and re-measure rather than guessing.
- Borderline helpers (the Before/After-hooks tuple reader) may land in a different file than listed; the oracle
  makes any placement correct, so prefer the dominant-caller's file and move on. Do not agonise.
- Line ranges are approximate and shift as earlier tasks shrink the file — resolve by method name, not by line.
