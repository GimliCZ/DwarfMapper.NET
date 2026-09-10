# Round 29 — Hardware Mode Implementation Plan

> **WITHDRAWN, 2026-09-07: Phase 1 (`[GenerateView<TSource,TTarget>]`) is not part of this product.**
> It was built, shipped green and then removed by owner ruling — for its failure mode, not for a defect.
> Everything this plan says about views below is kept as history and must not be executed. The argument
> that removed it, and what a future proposal would have to answer, is in
> `Issues/round29/WITHDRAWN-generated-views.md`.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give DwarfMapper the measured hardware-level wins from the round-29 research — zero-copy DTO views, a blitting span map, `Nullable<T>` on the blit proof, a transfer-model-to-struct diagnostic with a transitive code fix, dense enum-keyed members, shared immutable members, layout hygiene — without a single `unsafe` block or uninitialized allocation, and without moving any ratchet unmeasured.

**Architecture:** Every fast path keeps the locked design language: prove the property at generation time in `MapperExtractor`/`BlittableProof`, emit it behind a hardware gate in the emitters, keep the scalar path as the always-correct oracle. Views are a new endpoint kind (`[GenerateView<TSource,TTarget>]` → a nested `readonly ref struct` whose properties evaluate the create-map's member resolution lazily). The decomposition work is an analyzer diagnostic at the mapping site plus a Roslyn code fix in `DwarfMapper.CodeFixes`. Result-owned layouts (arena, bitmaps) are deferred to a design spike at the end; they change the result's shape and need their own spec.

**Tech Stack:** C# / .NET 10.0.101 (`global.json`, rollForward disable), Roslyn incremental generator on netstandard2.0, xunit, Verify snapshots, BenchmarkDotNet 0.14 with the exact-pin gate, Stryker 4.16.0, `System.Numerics.Tensors` 10.0.x (Phase 3 only, opt-in).

**Spec:** `Issues/round29/RESEARCH-hardware-mode.md` — sections 5, 8, 9, 10, 11d, 12 and 13 carry the decisions; the numbers quoted below are that document's measurements (`Issues/round29/plan*-results.md`).

## Global Constraints

- No `unsafe` code, no `Unsafe.*`, no `GC.AllocateUninitializedArray`, no `MemoryMarshal.CreateSpan` over object interiors, no non-temporal stores, no `Parallel.*` — in shipped code and in generated code (spec sections 1, 7, 8b, 13). `BannedSymbols.txt` in `src/DwarfMapper` and `src/DwarfMapper.CodeFixes` is the enforcement point; Task 0.0 extends it.
- Every generated fast path is emitted only after a generation-time proof, behind a hardware gate where hardware is involved, with the scalar path as the oracle, and its refusal explained by a near-miss diagnostic (design language, spec section 1).
- Generated code must compile warning-free under nullable enable: `GeneratedCodeIsWarningFreeTests` + `ConsumerReportedEmissionWarningsTests` stay green; a consumer cannot suppress a CS warning inside a `.g.cs`.
- Ratchet rule (repo invariant R1): no ceiling or floor moves without a re-measure in the same commit — `allocation-baseline.json` pins (exact bytes, `totalBenchmarks`), `$coverageFloors` in `scripts/housekeeping.ps1` (band: fail below floor, mandatory raise at floor + 1.0 pp), `stryker-config*.json` `break`/`low` (never lowered), `$script:PackageSizeCeilingsKb` in `scripts/gate-checks.ps1` (measured on Windows AND in the `mcr.microsoft.com/dotnet/sdk:10.0.101` container, ceiling = the larger measurement), surface-matrix ceilings in `tests/DwarfMapper.Generator.Tests/Contracts`, golden manifest (`DWARF_GOLDEN_UPDATE=1`, deliberately, never by hand).
- A new diagnostic id syncs FIVE files in one commit: `src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs`, `src/DwarfMapper.Generator/AnalyzerReleases.Unshipped.md`, `docs/diagnostics.md` (fences are `fence-exempt` illustrations), a `tests/DwarfMapper.NegativeCases/Cases/<ID>_*.cs` row pinning id AND remedy wording, `CHANGELOG.md` (`Scan9` fails the build without it); then regenerate `docs/generated/diagnostics-index.md` (`GeneratedDocsAreCurrentTests`). Next free ids: DWARF101 and up (DWARF100 is the blit near-miss).
- New public surface goes to `src/DwarfMapper/PublicAPI.Unshipped.txt` (PublicApiAnalyzers); the shipped file is frozen.
- Whole-solution builds (Debug + Release, samples included) before any "done": `dotnet build DwarfMapper.NET.sln -c Release`; samples are not covered by `dotnet test`.
- Struct transfer-model thresholds (spec 8, 8c): ≤32 B silent, 32–64 B Info, >64 B suggest `in`.
- The consumer check: every phase that changes emission ends with a pack to `artifacts/nuget` as `1.1.0-rc<N>` and a re-verification request to the FusedChat session (the round-28 protocol).

---

## File Structure

New and modified files, by responsibility (existing large files stay large; new responsibilities get new files):

| Path | Responsibility |
|---|---|
| `src/DwarfMapper/GenerateViewAttribute.cs` (new) | `[GenerateView<TSource,TTarget>]` — declares a zero-copy view endpoint on a mapper class |
| `src/DwarfMapper/MapShareAttribute.cs` (new) | `[MapShare]` — share an identical immutable reference member instead of copying it |
| `src/DwarfMapper/MapDenseEnumKeysAttribute.cs` (new) | `[MapDenseEnumKeys]` — an enum-keyed dictionary source fills an inline-array target |
| `src/DwarfMapper/BannedSymbols.txt`, `src/DwarfMapper.Generator/BannedSymbols.txt` (modify) | the no-unsafe policy as a build error |
| `src/DwarfMapper.Generator/Pipeline/BlittableProof.cs` (modify: `LayoutIdentical`) | `Nullable<T>` pairs |
| `src/DwarfMapper.Generator/Pipeline/MapEmitter.SpanMap.cs` (new; move `EmitSpanMapMethod` here) | span-map emission, blit fast path |
| `src/DwarfMapper.Generator/Pipeline/ViewEmitter.cs` (new) | emits the `readonly ref struct` view |
| `src/DwarfMapper.Generator/Pipeline/MapperExtractor.Views.cs` (new partial) | extracts `[GenerateView]` pairs into `ViewModel` |
| `src/DwarfMapper.Generator/Model/ViewModel.cs` (new) | the view's resolved members (reuses `MemberMap`) |
| `src/DwarfMapper.Generator/Pipeline/LayoutHygiene.cs` (new) | struct size/padding computation for DWARF101 and the transfer-model size rule |
| `src/DwarfMapper.Generator/Pipeline/TransferModelShape.cs` (new) | "is this class transfer-model shaped" predicate for DWARF103 |
| `src/DwarfMapper.CodeFixes/ConvertToRecordStructCodeFixProvider.cs` (new) | the transitive decompose code fix for DWARF103 |
| `src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs` (modify) | DWARF101 (padding), DWARF102 (view member not viewable), DWARF103 (transfer model could be a struct), DWARF104 (dense enum keys misuse) |
| `tests/DwarfMapper.Generator.Tests/Contracts/Endpoints.cs` (modify) | `Endpoint.View` cell + `EndpointSources` shape |
| `tests/DwarfMapper.Generator.Tests/Views/*.cs` (new) | view extraction/emission/warning-free/snapshot tests |
| `tests/DwarfMapper.IntegrationTests/ViewRuntimeTests.cs`, `SpanMapBlitRuntimeTests.cs`, `NullableNestedBlitRuntimeTests.cs`, `MapShareRuntimeTests.cs`, `DenseEnumKeysRuntimeTests.cs` (new) | runtime semantics |
| `tests/DwarfMapper.NegativeCases/Cases/DWARF10{1,2,3,4}_*.cs` (new) | id + remedy wording pins |
| `benchmarks/DwarfMapper.Benchmarks/Program.cs`, `allocation-baseline.json` (modify) | `View_*`, `SpanBlit_*`, `TreeStruct_*`, `DenseEnum_*` rows and their exact pins |
| `samples/DwarfMapper.AotSample/Program.cs`, `samples/DwarfMapper.Gallery/guides/36_Views.cs` (new) | AOT execution of a view; gallery entry |
| `docs/diagnostics.md`, `docs/howto/deploy-and-optimize.md`, `README.md`, `CHANGELOG.md`, `docs/generated/diagnostics-index.md` | documentation sync |

---

## Testing-Area Impact Matrix

This round touches every gate the repo has. Each phase lists what it moves and the exact re-measure it owes. "Owed" means: done in the same commit as the change, with the number and date in the file's comment.

| Testing area | Where it lives | Phase 0 | Phase 1 (views) | Phase 2 (decompose) | Phase 3 (share, dense keys, tensors) | Phase 4 (spikes) |
|---|---|---|---|---|---|---|
| Allocation exact pins | `benchmarks/.../allocation-baseline.json`, `-BenchSmoke` | +`SpanBlit_Dwarf`, +`NullableNested_Blit` rows, `totalBenchmarks` | +`View_Dwarf`, `View_Consume_*` (0 B pins) | +`TreeStruct_*` rows | +`DenseEnum_*`, `MapShare_*` | — |
| Coverage floors (band) | `scripts/housekeeping.ps1 $coverageFloors` | Generator ±; re-measure | Generator likely +>1.0 pp → mandatory raise | CodeFixes floor moves; re-measure | re-measure | — |
| Mutation legs | `stryker-config*.json` (generator leg mutates `BlittableProof.cs`, `LocationInfo.cs`, `ConstructorSelector.cs`, `EquatableArray.cs`) | `BlittableProof` changes → generator leg re-measure (~51 min, buffered dots — never judge by progress) | pipeline leg re-measure if `MapperExtractor.*` grows | codefixes leg re-measure | pipeline leg | — |
| Golden manifest + Verify snapshots | `tests/.../Golden/output-manifest.txt`, `Snapshots/*.verified.txt` | span-map emission changes existing snapshots → `DWARF_GOLDEN_UPDATE=1` deliberately | new snapshots only | none | dense/share change existing emission for annotated members only | — |
| Surface matrix (attribute × endpoint) | `tests/.../Contracts/Endpoints.cs`, `SurfaceCatalog.cs`, `SurfaceParityTests.cs`, `DeclaredDivergences` | — | +1 endpoint column: every attribute gets a cell; ceilings re-measured; divergences declared, not ratified | — | +2 attribute rows × all endpoints | — |
| Negative cases + diagnostic ratchet | `tests/DwarfMapper.NegativeCases` | DWARF101 rows | DWARF102 rows | DWARF103 rows (+ remedy wording) | DWARF104 rows | — |
| Diagnostic 5-file sync + generated index | descriptors, releases, docs, cases, CHANGELOG | DWARF101 | DWARF102 | DWARF103 | DWARF104 | — |
| Public API | `src/DwarfMapper/PublicAPI.Unshipped.txt` | — | `GenerateViewAttribute<,>` | — | `MapShareAttribute`, `MapDenseEnumKeysAttribute` | — |
| Package ceiling | `scripts/gate-checks.ps1` | re-measure Windows + container | re-measure | re-measure | re-measure | — |
| Package validation baseline | `EnablePackageValidation`, `PackageValidationBaselineVersion` | — | additive only | — | additive only | — |
| AOT publish + execute | `samples/DwarfMapper.AotSample` (`aot-trim-gate`) | span blit exercised | a view exercised under NativeAOT | — | dense keys exercised | — |
| ILVerify | `-ILVerify` | ref struct emission verified | ref struct emission verified | — | — | — |
| Conformance sample | `samples/DwarfMapper.Conformance/Features.cs` | F-feature for span blit | F-feature for views | — | F-features for share/dense | — |
| Warning-free generated code | `GeneratedCodeIsWarningFreeTests`, `ConsumerReportedEmissionWarningsTests` | shapes added | view shapes added to the combinatorial schema | — | shapes added | — |
| Deep / exhaustion tiers | `DWARF_DEEP=1`, `DWARF_FUZZ_FULL=1` | `DeepTier.cs` populations for new fuzzers | view fuzzer population | — | — | — |
| Docs scans | `DocFenceScanTests`, `GeneratedDocsAreCurrentTests`, `RatchetInvariantScanTests`, `Scan9` | run after every doc edit | same | same | same | — |
| Consumer | FusedChat session, `artifacts/nuget` | rc8 | rc9 | rc10 | rc11 | — |

Phase gate: a phase is complete only when `scripts/housekeeping.ps1 -Deep -Coverage -ILVerify -BenchSmoke` (exhaustion and AOT ON — `-Nightly` skips both) passes on the tree, the mutation leg(s) it touched are re-measured, and the package is re-measured on both platforms.

---

## Phase 0 — Foundations (policy gate, `Nullable<T>` proof, span-map blit, layout hygiene)

### Task 0.0: The no-unsafe policy as a build error

**Files:**
- Modify: `src/DwarfMapper/BannedSymbols.txt`, `src/DwarfMapper.Generator/BannedSymbols.txt`, `src/DwarfMapper.CodeFixes/BannedSymbols.txt`, `src/DwarfMapper.Testing/BannedSymbols.txt` (create if absent, same format)
- Test: `tests/DwarfMapper.Generator.Tests/SelfValidation/EmittedCodePolicyScanTests.cs` (new)

**Interfaces:**
- Produces: the scan test `EmittedCodePolicyScanTests.Generated_code_never_names_a_banned_api` used by every later phase's golden corpus run.

- [ ] **Step 1: Write the failing scan test** — it walks every `.verified.txt` snapshot and the golden corpus output and fails if a banned token appears in emitted code:

```csharp
// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Tests.SelfValidation
{
    public class EmittedCodePolicyScanTests
    {
        // The user's rule for round 29 (Issues/round29/RESEARCH-hardware-mode.md §1): no unsafe, no uninitialized memory,
        // no parallelism in shipped or generated code. Generated code is checked here because BannedSymbols.txt
        // only reaches the analyzers' own source, never a consumer's .g.cs.
        private static readonly string[] Banned =
        {
            "Unsafe.", "GC.AllocateUninitializedArray", "MemoryMarshal.CreateSpan", "MemoryMarshal.CreateReadOnlySpan",
            "StoreNonTemporal", "StoreAlignedNonTemporal", "Parallel.For", "Parallel.ForEach", "unsafe ", "stackalloc",
        };

        [Fact]
        public void Generated_code_never_names_a_banned_api()
        {
            var offenders = new List<string>();
            foreach (var c in GoldenCorpus.Cases())
            {
                var (_, generated) = GeneratorTestHarness.Run(c.Source);
                foreach (var b in Banned)
                    if (generated.Contains(b, StringComparison.Ordinal)) offenders.Add(c.Id + ": " + b);
            }
            Assert.True(offenders.Count == 0, "Emitted code names a banned API:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void The_scan_can_fail()
        {
            // Positive control: a synthetic source that would emit a banned token trips the same check.
            const string fake = "var x = global::System.Runtime.CompilerServices.Unsafe.As<int, uint>(ref y);";
            Assert.Contains(Banned, b => fake.Contains(b, StringComparison.Ordinal));
        }
    }
}
```

- [ ] **Step 2: Run it** — `dotnet test tests/DwarfMapper.Generator.Tests -c Release --filter FullyQualifiedName~EmittedCodePolicyScan` — Expected: PASS today (nothing emits those), positive control PASS. This is the tripwire for the round; keep it.

- [ ] **Step 3: Add the banned symbols** to each `BannedSymbols.txt` (format is `T:`/`M:` doc-id per line, one per symbol; the file already exists in `src/DwarfMapper` and `src/DwarfMapper.CodeFixes`):

```
T:System.Runtime.CompilerServices.Unsafe;round 29: no unsafe code in shipped assemblies
M:System.GC.AllocateUninitializedArray``1(System.Int32,System.Boolean);round 29: no uninitialized memory
M:System.Runtime.InteropServices.MemoryMarshal.CreateSpan``1(``0@,System.Int32);round 29: no spans over object interiors
M:System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan``1(``0@,System.Int32);round 29: no spans over object interiors
T:System.Threading.Tasks.Parallel;round 29: the mapper never touches the thread pool
```

- [ ] **Step 4: Build the whole solution** — `dotnet build DwarfMapper.NET.sln -c Release` — Expected: clean (nothing in-tree uses them).

- [ ] **Step 5: Commit**

```bash
git add src/DwarfMapper/BannedSymbols.txt src/DwarfMapper.Generator/BannedSymbols.txt src/DwarfMapper.CodeFixes/BannedSymbols.txt src/DwarfMapper.Testing/BannedSymbols.txt tests/DwarfMapper.Generator.Tests/SelfValidation/EmittedCodePolicyScanTests.cs
git commit -m "chore(policy): round-29 no-unsafe / no-uninitialized-memory rule as banned symbols and an emitted-code scan"
```

### Task 0.1: `Nullable<T>` pairs in the blit proof

**Files:**
- Modify: `src/DwarfMapper.Generator/Pipeline/BlittableProof.cs:312-360` (`LayoutIdentical`), `TryExplainNearMiss` (near-miss text)
- Test: `tests/DwarfMapper.Generator.Tests/BlitSoundnessTests.cs` (append), `tests/DwarfMapper.IntegrationTests/NullableNestedBlitRuntimeTests.cs` (new)

**Interfaces:**
- Produces: `LayoutIdentical(Nullable<A>, Nullable<B>) == LayoutIdentical(A, B)`; near-miss reason text `"an optional nested member is Nullable<T> on one side only"` when only one side is nullable.

- [ ] **Step 1: Failing generator test** (append to `BlitSoundnessTests`):

```csharp
[Fact]
public void Optional_nested_struct_member_keeps_the_root_blit()
{
    const string src = """
        using DwarfMapper;
        namespace T
        {
            public struct Addr { public int Street, City, Zip, Country; }
            public struct AddrDto { public int Street, City, Zip, Country; }
            public struct Order { public long Id; public Addr? Ship; public long Amount; }
            public struct OrderDto { public long Id; public AddrDto? Ship; public long Amount; }
            public class Src { public Order[] Items { get; set; } = System.Array.Empty<Order>(); }
            public class Dst { public OrderDto[] Items { get; set; } = System.Array.Empty<OrderDto>(); }
            [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
        }
        """;
    var generated = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);
    Assert.Contains("__DwarfBlit_", generated, StringComparison.Ordinal);
    Assert.DoesNotContain("DWARF100", GeneratorTestHarness.Run(src).Diagnostics.Select(d => d.Id));
}

[Fact]
public void Nullable_on_one_side_only_is_a_near_miss_that_names_the_member()
{
    const string src = """
        using DwarfMapper;
        namespace T
        {
            public struct Addr { public int Street, City, Zip, Country; }
            public struct Order { public long Id; public Addr? Ship; }
            public struct OrderDto { public long Id; public Addr Ship; }
            public class Src { public Order[] Items { get; set; } = System.Array.Empty<Order>(); }
            public class Dst { public OrderDto[] Items { get; set; } = System.Array.Empty<OrderDto>(); }
            [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
        }
        """;
    var d = GeneratorAssert.Reports(src, "DWARF100");
    Assert.Contains("Nullable<T> on one side only", Assert.Single(d).GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run** — `dotnet test tests/DwarfMapper.Generator.Tests -c Release --filter FullyQualifiedName~Optional_nested_struct_member_keeps` — Expected: FAIL (no `__DwarfBlit_`; the proof stops at the metadata `Nullable<T>`).

- [ ] **Step 3: Implement** in `LayoutIdentical`, before the `IsPrimitive` branch:

```csharp
// Nullable<T> is {bool hasValue; T value}, sequential, unmanaged when T is (C# 8 rule); two Nullable<T>
// instantiations have the same layout exactly when their T's do. The metadata-struct rule below would refuse
// it (no source to read [StructLayout] from), so it is decided here, by the proof over T — measured in
// Issues/round29 §10: an optional nested member keeps the root blit at 0.15x / 0.06x.
if (a is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nna &&
    b is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nnb)
{
    return LayoutIdentical(nna.TypeArguments[0], nnb.TypeArguments[0], byBytesOnly);
}
```

and in `TryExplainNearMiss`, in the per-field comparison where field types are compared, add the reason when exactly one side is `Nullable<T>`:

```csharp
if ((fa.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) != (fb.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T))
{
    reason = $"member '{fa.Name}' is Nullable<T> on one side only — an optional nested member has to be optional on both sides to keep the same bytes";
    return true;
}
```

- [ ] **Step 4: Run both tests** — Expected: PASS. Then `dotnet test tests/DwarfMapper.Generator.Tests -c Release --filter "FullyQualifiedName~Blit|FullyQualifiedName~NearMiss"` — Expected: PASS (no existing blit verdict changes: same-type Nullable pairs were already identical by the identity rule).

- [ ] **Step 5: Runtime test** (`NullableNestedBlitRuntimeTests.cs`, IntegrationTests): map an array of 3 orders where `Ship` is null for the middle one; assert `Items[1].Ship is null` and `Items[0].Ship!.Value.Zip` equals the source. Run, PASS.

- [ ] **Step 6: Mutation-leg note** — `BlittableProof.cs` is in the generator leg's mutate set; add the kill test names to the RE-MEASURED paragraph you will append in the Phase 0 gate (Task 0.4). Commit:

```bash
git add src/DwarfMapper.Generator/Pipeline/BlittableProof.cs tests/DwarfMapper.Generator.Tests/BlitSoundnessTests.cs tests/DwarfMapper.IntegrationTests/NullableNestedBlitRuntimeTests.cs
git commit -m "feat(blit): Nullable<T> pairs are layout-identical when their T's are; near-miss names a one-sided optional"
```

### Task 0.2: Span map takes the blit

**Files:**
- Create: `src/DwarfMapper.Generator/Pipeline/MapEmitter.SpanMap.cs` (move `EmitSpanMapMethod` from `MapEmitter.cs:893-945` verbatim, then change it)
- Modify: `src/DwarfMapper.Generator/Model/MapMethodModel.cs` (+ `bool SpanMapBlits = false`), `src/DwarfMapper.Generator/Pipeline/MapperExtractor.Phases.cs:2600-2625` (set it), `benchmarks/DwarfMapper.Benchmarks/Program.cs`, `benchmarks/DwarfMapper.Benchmarks/allocation-baseline.json`, `README.md` ("Zero-alloc span mapping"), `docs/howto/deploy-and-optimize.md`
- Test: `tests/DwarfMapper.Generator.Tests/SpanMapBlitTests.cs` (new), `tests/DwarfMapper.IntegrationTests/SpanMapBlitRuntimeTests.cs` (new)

**Interfaces:**
- Consumes: `BlittableProof.CanReinterpret(srcElem, tgtElem)` / `CanReinterpretEnums(...)`.
- Produces: `MapMethodModel.SpanMapBlits` (true when the element pair is layout-identical); emitted body `global::System.Runtime.InteropServices.MemoryMarshal.Cast<S, D>(src).CopyTo(dst);` after the length check.

- [ ] **Step 1: Failing generator test**:

```csharp
public class SpanMapBlitTests
{
    private const string Pair = """
        using System;
        using DwarfMapper;
        namespace T
        {
            public struct Vec3S { public float X, Y, Z; }
            public struct Vec3D { public float X, Y, Z; }
            [DwarfMapper] public partial class M { public partial void Map(ReadOnlySpan<Vec3S> src, Span<Vec3D> dst); }
        }
        """;

    [Fact]
    public void Layout_identical_span_map_is_a_block_copy()
    {
        var generated = GeneratorAssert.CompilesClean(Pair, NullableContextOptions.Enable);
        Assert.Contains("MemoryMarshal.Cast<global::T.Vec3S, global::T.Vec3D>(src).CopyTo(dst)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("for (int __i", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Length_check_stays_in_front_of_the_block_copy()
    {
        var generated = GeneratorAssert.CompilesClean(Pair, NullableContextOptions.Enable);
        var check = generated.IndexOf("dst.Length < src.Length", StringComparison.Ordinal);
        var copy = generated.IndexOf("MemoryMarshal.Cast", StringComparison.Ordinal);
        Assert.True(check >= 0 && check < copy, "the ArgumentException guard must precede the copy");
    }

    [Fact]
    public void Non_identical_layout_keeps_the_element_loop_and_explains_it()
    {
        const string src = """
            using System;
            using DwarfMapper;
            namespace T
            {
                public struct Vec3S { public float X, Y, Z; }
                [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
                public struct Vec3A { public float X, Y, Z; }
                [DwarfMapper] public partial class M { public partial void Map(ReadOnlySpan<Vec3S> src, Span<Vec3A> dst); }
            }
            """;
        var generated = GeneratorAssert.EmitsCompilableCode(src, NullableContextOptions.Enable);
        Assert.Contains("for (int __i", generated, StringComparison.Ordinal);
        GeneratorAssert.Reports(src, "DWARF100");
    }
}
```

- [ ] **Step 2: Run** — Expected: first two FAIL (loop emitted), third PASS.

- [ ] **Step 3: Implement.** In `MapperExtractor.Phases.cs` where the span-map `MapMethodModel` is built (line ~2606, the `new MapMethodModel(` with `IsSpanMap: true`), compute the element types from the two span type arguments (they are already resolved into `srcElem`/`tgtElem` locals for the element converter resolution above it) and pass:

```csharp
SpanMapBlits: elemConverter is null && BlittableProof.CanReinterpret(srcElem, tgtElem)
              || (elemConverter is not null && GeneratedNames.IsBlit(elemConverter)),
```

(`GeneratedNames.IsBlit` = name starts with `__DwarfBlit_`; add it beside `IsSynthesized`.) Report the near-miss exactly as the collection arm does at `MapperExtractor.Conversions.Arms.cs:417` (`BlittableProof.TryExplainNearMiss(srcElem, tgtElem, out var reason)` → `DiagnosticDescriptors.ArrayNearMiss`). In `MapEmitter.SpanMap.cs`, after the length-check lines:

```csharp
if (method.SpanMapBlits)
{
    // Proven at generation time (BlittableProof): same bytes, so the caller-owned buffer is filled by one
    // block move. Zero allocation and the copy floor — Issues/round29 §9: 0.20x at 1k against the element
    // loop, neutral above the cache. No hardware gate: MemoryMarshal.Cast + CopyTo is the runtime's memmove.
    sb.Append(indent).Append("    global::System.Runtime.InteropServices.MemoryMarshal.Cast<")
        .Append(method.SpanSourceElementFullName).Append(", ").Append(method.SpanTargetElementFullName)
        .Append(">(").Append(src).Append(").CopyTo(").Append(dst).AppendLine(");");
    sb.Append(indent).AppendLine("}");
    return;
}
```

(`SpanSourceElementFullName`/`SpanTargetElementFullName`: two `string` members added to `MapMethodModel` next to `SpanTargetParameterName`, filled with `Fq(srcElem)`/`Fq(tgtElem)` at the same construction site.)

- [ ] **Step 4: Run the three tests** — PASS. Run the whole Generator.Tests fast tier — Expected: the span-map golden snapshot(s) FAIL with a diff showing the block copy. Inspect the `.received.txt`, confirm the only change is the loop → `Cast().CopyTo()`, then accept: rename `.received.txt` to `.verified.txt` for those files, and regenerate the manifest: `DWARF_GOLDEN_UPDATE=1 dotnet test tests/DwarfMapper.Generator.Tests -c Release --filter FullyQualifiedName~GoldenCorpusTests`. Re-run the fast tier — PASS.

- [ ] **Step 5: Runtime test** (`SpanMapBlitRuntimeTests`): 1000 `Vec3S` into a `stackalloc`-free `Vec3D[]` buffer via the span map; assert every element; assert `ArgumentException` for a short destination; assert a non-multiple length (`997`) round-trips (the vectorization-guidelines remainder rule). PASS.

- [ ] **Step 6: Benchmark row + pin.** In `benchmarks/.../Program.cs` add `[Benchmark, BenchmarkCategory("SpanBlit")] public int SpanBlit_Dwarf()` mapping the existing `_blit` payload into a preallocated `Vec3Dst[]` field via the span map (returns `dst.Length`), and its scalar twin `SpanBlit_Scalar` using the `Vec3Ren` auto-layout twin. Run `pwsh scripts/housekeeping.ps1 -BenchSmoke` — Expected: FAIL on `totalBenchmarks` (55 → 57) and the two unpinned rows. Add the measured pins (both must be `0` B — the span map allocates nothing; if not, the emission is wrong) and `totalBenchmarks: 57` with a dated note; re-run — PASS.

- [ ] **Step 7: Docs** — README "Zero-alloc span mapping": replace "Each element runs through the full conversion pipeline" with the two-path statement (block copy when the pair is layout-identical, element loop otherwise, `DWARF100` explains); `deploy-and-optimize.md` fast-paths list: add the span-map bullet. CHANGELOG `### Added`. Run `dotnet test tests/DwarfMapper.Generator.Tests -c Release --filter "FullyQualifiedName~Scan|FullyQualifiedName~GeneratedDocs"` — PASS.

- [ ] **Step 8: Commit**

```bash
git add src/DwarfMapper.Generator/Pipeline/MapEmitter.SpanMap.cs src/DwarfMapper.Generator/Pipeline/MapEmitter.cs src/DwarfMapper.Generator/Model/MapMethodModel.cs src/DwarfMapper.Generator/Pipeline/MapperExtractor.Phases.cs src/DwarfMapper.Generator/Pipeline/GeneratedNames.cs tests/DwarfMapper.Generator.Tests/SpanMapBlitTests.cs tests/DwarfMapper.IntegrationTests/SpanMapBlitRuntimeTests.cs tests/DwarfMapper.Generator.Tests/Snapshots tests/DwarfMapper.Generator.Tests/Golden/output-manifest.txt benchmarks/DwarfMapper.Benchmarks/Program.cs benchmarks/DwarfMapper.Benchmarks/allocation-baseline.json README.md docs/howto/deploy-and-optimize.md CHANGELOG.md
git commit -m "feat(span-map): a layout-identical pair is one MemoryMarshal.Cast block copy into the caller's buffer (0 B, 0.20x at 1k)"
```

### Task 0.3: DWARF101 — layout hygiene (padding) on transfer-model structs

**Files:**
- Create: `src/DwarfMapper.Generator/Pipeline/LayoutHygiene.cs`, `tests/DwarfMapper.Generator.Tests/LayoutHygieneTests.cs`, `tests/DwarfMapper.NegativeCases/Cases/DWARF101_PaddedStruct.cs`, `tests/DwarfMapper.NegativeCases/Cases/DWARF101_PaddedStruct_Remedy.cs`
- Modify: `DiagnosticDescriptors.cs`, `AnalyzerReleases.Unshipped.md`, `docs/diagnostics.md`, `CHANGELOG.md`, `docs/generated/diagnostics-index.md`, `MapperExtractor.Conversions.Arms.cs` (report site: where an unmanaged struct pair is accepted for a collection blit or refused)

**Interfaces:**
- Produces: `LayoutHygiene.Measure(INamedTypeSymbol) → (int Size, int Padding, IReadOnlyList<string> PackedOrder)` — sequential layout per the CLR rules (natural alignment per field, struct aligned to its largest member; nested structs recursive; `Nullable<T>` = 1 + pad + T); reused by Task 2.1 for the ≤32/≤64 B thresholds.

- [ ] **Step 1: Failing unit test** for `Measure`: `struct { bool; long; byte; double; short }` → size 40, padding 15, packed order `long, double, short, byte, bool` → 24 B. `Vec3` → 12/0. `Nullable<Addr16>` → 20 (1 + 3 pad + 16).

- [ ] **Step 2: Implement `Measure`**: iterate instance fields in declaration order (`GetMembers().OfType<IFieldSymbol>()` excluding static/const), `SizeOf` by `PrimitiveSize` (reuse `BlittableProof.PrimitiveSize`, make it `internal`), enum → underlying, nested struct → recursive `Measure`, `Nullable<T>` → 1 + pad-to-align(T) + size(T); alignment = largest primitive inside; size rounds up to alignment. Packed order = fields sorted by alignment descending, stable.

- [ ] **Step 3: Descriptor** DWARF101 (Info): title "Struct layout pads more than a quarter of its size", message `"'{0}' is {1} bytes with {2} bytes of padding; declaring its fields as {3} makes it {4} bytes — smaller arrays, and a layout-identical twin can take the blit"`; report once per struct type per compilation, only for structs that are elements of a mapped collection (the mapping site), only when padding ≥ 25 % of size and ≥ 8 bytes. Wire the report in the collection arm beside the DWARF100 near-miss call.

- [ ] **Step 4: Five-file sync**: releases table row, `docs/diagnostics.md` entry (Info, fix table: reorder fields, note the blit consequence, spec §10 D: 0.57× time / 0.60× memory), NegativeCases pair (padded → id + wording; reordered → no diagnostic), CHANGELOG bullet, regenerate the index. Run the Scan tests + NegativeCases — PASS.

- [ ] **Step 5: Commit** — `feat(diag): DWARF101 names a padded transfer-model struct and the field order that packs it`.

### Task 0.4: Phase 0 gate

- [ ] `dotnet build DwarfMapper.NET.sln -c Debug` and `-c Release` — clean.
- [ ] `pwsh scripts/housekeeping.ps1 -Deep -Coverage -ILVerify -BenchSmoke` — PASS; if a floor's band demands a raise (Generator likely), set it to the truncated measurement with the dated comment in `$coverageFloors`, update the README badge.
- [ ] Generator mutation leg: `pwsh scripts/housekeeping.ps1 -Mutation` (set `TimeoutMinutes` ≥ 90 first: the leg measured 51 min at 391 mutants; do not judge progress from the dots — they are buffered). Append a dated RE-MEASURED paragraph to `stryker-config.json`'s comment naming Task 0.1's kill tests; `break`/`low` move only if floor(score) ≥ break + 1.
- [ ] Package: `dotnet pack src/DwarfMapper/DwarfMapper.csproj -c Release -o <scratch> -p:EnablePackageValidation=false` on Windows and in the container (`docker run --rm -v <tree>:/src mcr.microsoft.com/dotnet/sdk:10.0.101 bash -c "cd /src && CI=true dotnet restore --locked-mode && dotnet pack ..."`); set `'DwarfMapper'` to the larger `floor(bytes/1024)` with both numbers in the comment.
- [ ] Pack `1.1.0-rc8` to `artifacts/nuget`, message the FusedChat session with the delta (span-map blit does not affect them; `Nullable<T>` proof and DWARF101 Info may surface), record the reply in `Issues/round29/IMPLEMENTATION-LEDGER.md`.

---

## Phase 1 — DTO views (`[GenerateView<TSource,TTarget>]`) — WITHDRAWN 2026-09-07

> **Do not implement this phase.** It was implemented (`c27ec58`..`9f937b8`), measured at 0 B/op, and
> withdrawn by owner ruling on 2026-09-07: a view evaluates each member on access, so a source mutated
> between two reads yields a record that never existed at any instant — silent wrong data, in a library
> whose headline value is making silent mislinking impossible. `DWARF102` and `DWARF108` went with it and
> are not allocated. See `Issues/round29/WITHDRAWN-generated-views.md`. The text below is the original
> plan, kept so the log stays readable.

Design (spec §11b.2, §12): a view is the create map's member resolution evaluated lazily. The generator emits, inside the mapper class, `public readonly ref struct <TTarget>View` holding a `TSource` reference, with one get-only property per resolved target member: identity members return `_s.Member`; scalar converters call the same synthesized/user converter the create map uses (`__DwarfMap_EnumStr_…(_s.X)`); a nested object member returns a nested view type (emitted the same way for the nested pair); a collection member whose elements need no conversion returns the source collection as-is; a collection member whose elements need conversion is refused with DWARF102 (v1) — the view never allocates, and a converted collection would. `[MapIgnore]`, `[MapProperty]` renames, `[MapValue]` constants, `NullSubstitute` all apply (they are expressions). Views are read-only; there is no ctor binding and no update-into. Measured expectation: 0.20× at 1k, 0.04× at 100k, 0 B (§12 A/C).

### Task 1.1: The attribute and its public surface

**Files:**
- Create: `src/DwarfMapper/GenerateViewAttribute.cs`
- Modify: `src/DwarfMapper/PublicAPI.Unshipped.txt`, `src/DwarfMapper.Generator/Core/KnownNames.cs` (+ `GenerateView`)
- Test: `tests/DwarfMapper.Generator.Tests/SelfValidation/SurfaceDeclarationTests.cs` (the attribute inventory test must list it; run to see the failing inventory first)

- [ ] **Step 1**: run `SurfaceDeclarationTests` after adding the file — the inventory diff is the failing test.
- [ ] **Step 2: Attribute** (mirrors `GenerateMapAttribute<,>`):

```csharp
// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper
{
    /// <summary>
    ///     Declares a ZERO-COPY view from <typeparamref name="TSource" /> shaped as <typeparamref name="TTarget" />:
    ///     the generator emits a nested <c>readonly ref struct &lt;TTarget&gt;View</c> on the mapper class whose
    ///     properties evaluate the same member resolution a <c>Map</c> would — lazily, on access, against the
    ///     source instance. Nothing is allocated and nothing is copied; the view cannot outlive its source (it is a
    ///     <c>ref struct</c>), which is the contract that makes it free. Use it where a mapped DTO is consumed at
    ///     once — serialized, rendered, compared — and <c>Map</c> where it is stored or returned.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class GenerateViewAttribute<TSource, TTarget> : Attribute
    {
        /// <summary>Optional view type name; default is the target type's name followed by <c>View</c>.</summary>
        public string? Name { get; set; }
    }
}
```

- [ ] **Step 3**: add the two `PublicAPI.Unshipped.txt` lines the analyzer prints (`DwarfMapper.GenerateViewAttribute<TSource, TTarget>` and `.Name.get/.set`), `KnownNames.GenerateView = "GenerateViewAttribute"`, update the inventory expectation. Build the solution, run `SurfaceDeclarationTests` — PASS. Commit `feat(api): [GenerateView<TSource,TTarget>]`.

### Task 1.2: Extraction — `[GenerateView]` pairs into `ViewModel`

**Files:**
- Create: `src/DwarfMapper.Generator/Model/ViewModel.cs`, `src/DwarfMapper.Generator/Pipeline/MapperExtractor.Views.cs`
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs` (call site beside `ExtractGenerateMapPairs`, around line 393), `MapperAccumulators` (+ `List<ViewModel> Views`)
- Test: `tests/DwarfMapper.Generator.Tests/Views/ViewExtractionTests.cs`

**Interfaces:**
- Produces: `sealed record ViewModel(string ViewTypeName, string SourceTypeFullName, string TargetTypeFullName, IReadOnlyList<MemberMap> Members, IReadOnlyList<ViewModel> NestedViews, LocationInfo? Location)`; `MapperExtractor.ExtractViews(ctx, decls, policy, acc)`.

- [ ] **Step 1: Failing test** — `[GenerateView<Src, Dst>]` on a `[DwarfMapper]` class with `Dst { int Id; string Name; }` yields one `ViewModel` with two identity members and the name `DstView`; `Name = "Row"` yields `Row`; a `Dst` with an unmapped member reports DWARF001 exactly as the create map does (completeness is not weakened by laziness). Use `GeneratorTestHarness.Run` and inspect diagnostics + generated names (`Assert.Contains("readonly ref struct DstView", generated)` belongs to Task 1.3; here assert on diagnostics and on a `MapperExtractor` internal via `InternalsVisibleTo`).
- [ ] **Step 2: Implement `ExtractViews`**: find attributes by `KnownNames.GenerateView` with two type arguments (copy the `[GenerateMap]` detection at `MapperExtractor.cs:307`), then call the existing `ResolveMembers(...)` with the same arguments the create-map path passes for a `(source, target)` pair, `objInitOnly: true` (no ctor binding), and collect the `MemberMap` list. Any member whose `ConverterMethod` is a collection/dictionary helper (`GeneratedNames.IsCollection(name)`) becomes a DWARF102 report (Task 1.4) and is dropped from the view. Nested object members (`__DwarfMap_Obj_` converters) are re-resolved as nested `ViewModel`s (recursion with the same depth guard the nested registry uses; a cycle is DWARF102 "cyclic view").
- [ ] **Step 3**: run — PASS. Commit `feat(views): extract [GenerateView] pairs through the create-map member resolution`.

### Task 1.3: Emission — the `readonly ref struct`

**Files:**
- Create: `src/DwarfMapper.Generator/Pipeline/ViewEmitter.cs`
- Modify: `src/DwarfMapper.Generator/DwarfGenerator.cs` (emit views after methods, inside the mapper class), `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotSuite.Views.cs` (new snapshot cases)
- Test: `tests/DwarfMapper.Generator.Tests/Views/ViewEmissionTests.cs`, `tests/DwarfMapper.IntegrationTests/ViewRuntimeTests.cs`

**Interfaces:**
- Produces: for each `ViewModel`, inside the partial mapper class:

```csharp
public readonly ref struct DstView
{
    private readonly global::T.Src _s;
    public DstView(global::T.Src source) { global::System.ArgumentNullException.ThrowIfNull(source); _s = source; }
    public int Id => _s.Id;
    public string Name => _s.Name;
    public global::System.DateTime When => __DwarfMap_Conv_…(_s.When);      // converters reuse the mapper's helpers
    public InnerView Inner => new InnerView(_s.Inner);                          // nested view
}
public DstView View(global::T.Src source) => new DstView(source);               // factory on the mapper
```

- [ ] **Step 1: Failing emission test**: the generated text contains the struct, the property for each member, the factory method; `GeneratorAssert.CompilesClean(..., NullableContextOptions.Enable)`; `GeneratedCodeWarnings` empty.
- [ ] **Step 2: Implement `ViewEmitter.Emit(StringBuilder, ViewModel, indent)`** reusing `MapEmitter.AppendValueExpression(sb, member, "_s", ...)` for the property bodies (it already renders identity, converters, `!`, `NullSubstitute`, `[MapValue]` constants); nested views: `new <NestedViewName>(_s.<Member>)`, with `_s.<Member> is null ? default : …` when the source member is a nullable reference (a `default` view reads as "no value": emit `public bool HasValue => _s.Inner is not null;` on nested views of nullable members).
- [ ] **Step 3**: PASS; add snapshot cases (flat, converter, nested, nullable nested, `[MapProperty]` rename, `[MapIgnore]`); accept `.verified.txt`; golden manifest regenerate (`DWARF_GOLDEN_UPDATE=1`), commit.
- [ ] **Step 4: Runtime tests** (`ViewRuntimeTests`): values read through; a source mutation after view creation is visible through the view (that is the contract, pin it); enum → string converter; nested view; `HasValue` false for a null nested member; `ArgumentNullException` on a null source; a `ref struct` cannot be captured (compile-time, documented, not tested).
- [ ] **Step 5: Warning-free oracle**: add a `View` variant to `CombinatorialSchema` (one extra source builder that adds `[GenerateView<Src,Dst>]` beside the mapper) so `GeneratedCodeIsWarningFreeTests` covers every cell for views; `DeepTier.cs` population unchanged (the cells multiply, the seeds do not). Run the fast tier — PASS. Commit `feat(views): emit readonly ref struct views with lazily evaluated members`.

### Task 1.4: DWARF102 and the surface matrix cell

**Files:**
- Modify: `DiagnosticDescriptors.cs` (DWARF102 Error: "Member cannot be viewed without allocating" — collections needing element conversion, cyclic nested views; message names the member and says "use Map, or map the element type to itself"), the five sync files, `tests/DwarfMapper.NegativeCases/Cases/DWARF102_*.cs`
- Modify: `tests/DwarfMapper.Generator.Tests/Contracts/Endpoints.cs` (+ `Endpoint.View`; `EndpointSources` builds `[GenerateView<S,T>]` on the mapper), `SurfaceCatalog.cs` (every attribute row gets a View expectation: member-site directives apply; `[MapConstructor]`, `[DwarfMapperConstructor]`, update-into/`SkipNullSourceMembers` are NotApplicable with a *reason*, and the reason is re-measured live per B3's rule), `DeclaredDivergences` (new cells that diverge are DECLARED with evidence, not ratified — the round-19 ruling)
- Test: `SurfaceParityTests` re-measured ceilings

- [ ] **Step 1**: add the endpoint; run `dotnet test tests/DwarfMapper.Generator.Tests -c Release --filter "Category=SurfaceMatrix"` — Expected: FAIL on the cell-count ceiling and on unjudged cells.
- [ ] **Step 2**: judge every new cell (attribute × View): expected diagnostic or expected effect on the emitted view; write the expectations in `SurfaceCatalog`; for each divergence from the create-map cell, a `DeclaredDivergences` entry with the emitted evidence.
- [ ] **Step 3**: re-measure the ceilings (cell count, unjudged populations) and set them to the measured values with the date. Run the matrix — PASS (green at the new count). Commit `test(matrix): the View endpoint is a cell for every attribute; divergences declared`.

### Task 1.5: Benchmarks, AOT, conformance, docs, consumer

- [ ] `View_Dwarf` (create a view, read six members into the sink — mirrors `PlanProbe3.A_View_Consume`) and `View_Map_Dwarf` twin (map, then read the same six) in `Program.cs`; pins: `View_Dwarf` must be `0`; `totalBenchmarks` +2. `-BenchSmoke` PASS.
- [ ] `samples/DwarfMapper.AotSample/Program.cs`: a `[7] View under NativeAOT` check (build a view, read, compare to the map); the `aot-trim-gate` executes it.
- [ ] `samples/DwarfMapper.Conformance/Features.cs`: `F37 views` (the conformance report table is generated from it — run `GeneratedDocsAreCurrentTests`).
- [ ] `samples/DwarfMapper.Gallery/guides/36_Views.cs` + README section "Zero-copy views" (the ref-struct contract in one paragraph: consume at once, never store), `docs/howto/deploy-and-optimize.md`, `docs/diagnostics.md` DWARF102, CHANGELOG.
- [ ] Phase 1 gate (as Task 0.4, incl. the pipeline mutation leg if `MapperExtractor.*` gained code), package re-measure, `1.1.0-rc9` to the FusedChat session with a note that `ChatHistoryController`/`StreamerProfilesController` responses are the view-shaped call sites (§12).

---

## Phase 2 — Decompose to structs: DWARF103 + the transitive code fix

Design (spec §8a, §9, §10, §12): fire at the MAPPING SITE, not the type — when a collection of class elements maps to a collection of class elements and the target element is transfer-model shaped. The code fix rewrites the target class (and, transitively, its transfer-model-shaped nested classes) to `readonly record struct`, keeps auto-properties and initializers, drops `sealed`, never touches usages: `null` checks, aliasing and `list[i].X = v` become compile errors on purpose (loud over silent).

### Task 2.1: `TransferModelShape.Classify`

**Files:**
- Create: `src/DwarfMapper.Generator/Pipeline/TransferModelShape.cs`, `tests/DwarfMapper.Generator.Tests/TransferModelShapeTests.cs`

**Interfaces:**
- Produces: `TransferModelShape.Classify(INamedTypeSymbol type, Compilation c) → Verdict { Eligible(size), TooLarge(size), NotEligible(reason) }` with these rules, each a test: class (not record class with behaviour beyond auto-props), `sealed` or no derived types in the compilation, no base class other than `object`, no explicit constructor with logic (parameterless or a constructor that only assigns auto-properties from parameters is allowed), every instance member an auto-property/field, member types: unmanaged, `string`, another Eligible transfer model (recursive, cycle → NotEligible), `Nullable<>` of those, arrays/`List<T>` of those (these become reference fields; they cost the root blit, not the eligibility), no events, no `IDisposable`, no attribute named `Key`/`Table`/`Owned` and no `DbSet<T>` usage of the type anywhere in the compilation (EF entity heuristic), no interface implementations except `IEquatable<T>`; size via `LayoutHygiene.Measure` over the would-be struct: ≤32 → Eligible, 33–64 → Eligible with `SuggestIn = true`, >64 → TooLarge.

- [ ] Steps: failing tests per rule (12 tests, one shape each, including the EF heuristic and the >64 B case) → implement → PASS → commit `feat(analysis): transfer-model shape classifier`.

### Task 2.2: DWARF103 at the mapping site

**Files:**
- Modify: `DiagnosticDescriptors.cs` (DWARF103 Info, title "Collection element could be a struct", message `"'{0}' → '{1}' maps {2} class elements per call; '{1}' is transfer-model shaped ({3} B as a struct). Declared as a readonly record struct it is one allocation instead of N (measured 0.30x at 1k, 0.09x at 100k); with '{0}' also a struct, a block copy. Code fix: Convert to readonly record struct."`), `MapperExtractor.Conversions.Arms.cs` (report where the object-element collection loop is chosen: the `IsMappableObjectPair` element arm), five sync files, NegativeCases rows (one Info case, one "already a struct" no-diagnostic case, one ">64 B" case with the `in` wording)
- Test: `tests/DwarfMapper.Generator.Tests/TransferModelDiagnosticTests.cs`

- [ ] Steps: failing test (List<Order> → List<OrderDto> with an Eligible OrderDto reports DWARF103 once per pair per method; a struct target reports nothing; a target with an event reports nothing) → implement → sync → PASS → commit.

### Task 2.3: The code fix

**Files:**
- Create: `src/DwarfMapper.CodeFixes/ConvertToRecordStructCodeFixProvider.cs`, `tests/DwarfMapper.Generator.Tests/CodeFixes/ConvertToRecordStructCodeFixTests.cs`, `…/ConvertToRecordStructRefusalTests.cs`

**Interfaces:**
- Produces: `FixableDiagnosticIds = { "DWARF103" }`; title "Convert '{0}' (and {1} nested transfer models) to readonly record struct"; the fix rewrites `class X` → `readonly record struct X`, removes `sealed`, keeps members, and applies the same rewrite to every nested member type whose `Classify` is Eligible (transitively, in the same solution change), leaving usages untouched; a nested type that is NOT Eligible is left as a class and named in the fix's description.

- [ ] Steps: failing code-fix tests (the existing `RestateBaseCodeFixTests` is the template for the harness): flat class → struct; nested two levels → both rewritten; a nested EF-entity-shaped type left alone and reported; a >64 B type rewritten with a `// consider passing by `in`` comment; the fix is offered only on DWARF103 → implement → PASS → commit.
- [ ] CodeFixes coverage floor re-measure; codefixes mutation leg re-measure in the phase gate.

### Task 2.4: Docs and the honesty page

- [ ] `docs/diagnostics.md` DWARF103 entry with the hazards table (spec §8a): `default` instead of `null`, `Nullable<T>` boxing through `object`, `list[i].X = v` is CS1612, aliasing gone, value equality (a feature), `[JsonConstructor]` for System.Text.Json readonly record structs with a constructor, EF Core: complex types yes, entities and struct collections no. README "Transfer models as structs" section with the measured table from §9/§12. CHANGELOG. Scans PASS. Phase 2 gate, `1.1.0-rc10`.

---

## Phase 3 — Result-side wins that need no new result type

### Task 3.1: `[MapShare]` — share identical immutable members

- Attribute on a target member (or automatic when: same type both sides, reference type, every instance member get-only or init-only, no settable member) → the emitter assigns the source reference instead of calling the object/collection helper: `Badges = source.Badges` for `IReadOnlyList<BadgeData>` when `BadgeData` is immutable-shaped. Spec §12: removes N allocations per message in FusedChat's `SenderBadges`. Tests: emission (no helper), runtime (`ReferenceEquals`), a settable member is refused with DWARF104-style wording ("not immutable: sharing would alias mutable state"). Snapshot + pin (`MapShare_Dwarf`), docs, PublicAPI.

### Task 3.2: `[MapDenseEnumKeys]` — enum-keyed dictionary into an inline array

- Target member declared as an `[InlineArray(n)]` struct (or `TValue[]`) with `[MapDenseEnumKeys(typeof(TEnum))]`; source `Dictionary<TEnum,TValue>`/`IReadOnlyDictionary`; proof: the enum's values are within `[0, n)` (or a `[MapDenseEnumKeys(Offset = 1)]`), else DWARF104 Error naming the out-of-range member. Emission: `foreach (var kv in src) dst[(int)kv.Key - offset] = kv.Value;`. Spec §12 E: 0.20× / 0.07×, 0.09× memory. Tests, snapshot, pin (`DenseEnum_Dwarf`), NegativeCases, docs, PublicAPI, conformance feature.

### Task 3.3: `TensorPrimitives` conversions for homogeneous struct pairs (opt-in)

- `[DwarfMapper(Conversions = ConversionEngine.Tensors)]` (new enum on the options attribute) enables, for an array/list pair whose elements are unmanaged sequential structs with the SAME primitive in every field on each side and a widening/narrowing pair from spec §7's vectorized list, the emission `TensorPrimitives.ConvertChecked<TFrom,TTo>(MemoryMarshal.Cast<S,TFrom>(src), MemoryMarshal.Cast<D,TTo>(dst))` behind `Vector128.IsHardwareAccelerated` with the scalar loop as the else branch. The consumer must reference `System.Numerics.Tensors`; DWARF105 (Error) when the option is on and the reference is absent. Measured 0.60× at 1k (§9), neutral above the cache. Tests incl. non-multiple lengths; pin row; docs; the AOT sample exercises it (Tensors is AOT-safe).

- [ ] Phase 3 gate, `1.1.0-rc11`.

---

## Phase 4 — Spikes (research-quality, each ends in a design note under `Issues/round29/`, not shipped code)

- **Lens laws for update-into**: a fuzz oracle in `DwarfMapper.Testing` for GetPut (`Update(src, dest)` twice ≡ once) and PutPut; if it finds a violation, that is a round-30 finding.
- **Map fusion**: when `A→B` and `B→C` are both `[GenerateMap]` pairs in one class, emit `A→C` composed; measure whether the JIT already elides `B` (escape analysis in .NET 10 may) before building it.
- **Arena / validity-bitmap result shape**: prototype `MapArena` returning `(OrderRangeS[] Orders, LineS[] Lines)` for one shape, measure against §12 B, and write the spec for a round-30 decision.

---

## Self-review (run before handing off)

1. Spec coverage — §5 (span-map blit: T0.2; decompose diagnostic + fix: T2.x; TensorPrimitives: T3.3; docs positioning: T1.5/T2.4), §8a (decomposition verdict table → T2.1 rules; `Nullable<T>` proof → T0.1; arena → Phase 4), §8b (permuted blit/transpose — measured losers, NOT planned, stated in §9), §9 (views? no: §12), §10 (hygiene → T0.3; string leaf → T3.1 share; arena → Phase 4), §11d (views → Phase 1; result-owned layouts → Phase 4 spike; lens laws/fusion → Phase 4), §12 (views, dense enum keys, share → Phases 1 and 3), §13 (spannable classes — not adopted, no task). Gap: none.
2. Placeholder scan — every task names files, tests and commands; Phase 3/4 tasks are described at task granularity by design (they are gated behind Phase 1–2 outcomes and get their step-level expansion when reached, from the same spec sections).
3. Type consistency — `MapMethodModel.SpanMapBlits`, `SpanSourceElementFullName`, `SpanTargetElementFullName` (T0.2); `ViewModel(ViewTypeName, SourceTypeFullName, TargetTypeFullName, Members, NestedViews, Location)` (T1.2 → T1.3); `LayoutHygiene.Measure` (T0.3 → T2.1); `TransferModelShape.Classify` → `Verdict` (T2.1 → T2.2 → T2.3); diagnostic ids DWARF101–105 used consistently.
