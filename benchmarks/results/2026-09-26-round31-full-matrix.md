# 2026-09-26 — round 31 full matrix: round 30 vs round 31, and every library against every library

Machine: AMD Ryzen 5 5600, 6 physical / 12 logical cores, Windows 10 22H2. .NET SDK 10.0.101, runtime
10.0.11, X64 RyuJIT AVX2. BenchmarkDotNet 0.14.0, DefaultJob (not ShortRun — see the note on smoke runs
below). Nothing else ran on the machine during the three measured runs.

## What was run, and why in this shape

Three runs, back to back, in one sitting:

1. `MapperBenchmarks` on `feat/round31` (63 rows);
2. `MapperBenchmarks` on a **real `git worktree` at `origin/master`** (`b688bb9`, the round-30 merge), built
   from source, not a remembered number;
3. `AmbientFacadeBenchmarks` on `feat/round31` — a new class, so it has no round-30 column by construction.

The round-30 column is a build rather than a citation on purpose. Every previous results file in this
directory records the machine it was taken on, because these figures are platform-dependent — the Dict
advantage over Mapperly is 2.13x on Windows and ~1.14x on Linux, and that difference describes Mapperly's
Windows behaviour rather than ours. Comparing a fresh round-31 run against a figure taken on another day, at
another load, on another SDK is how a regression gets attributed to a commit that did not cause it.

## Reading the noise before reading the numbers

**The `Array` category was measured in a noisy window in the round-31 run, and every arm in it was.** All
four libraries show ~13 % standard deviation there (Dwarf 902 ns on 7,292; Mapperly 746 on 5,854; Mapster
943; AutoMapper 724), while the `List` category next to it shows ~2 % (105–115 ns). The round-30 run
reproduces the same category-wide offset in the other direction: every Array arm is ~13 % lower
(6,316 / 5,113 / 6,546 / 5,748).

Two consequences, and they point opposite ways:

- **Do not compare `Array` against `List` from this data.** The absolute Array numbers carry a run-level
  offset, so the apparent "arrays are slower than lists" reading is an artefact. Mapperly, measured in the
  same window, shows arrays marginally *faster* than lists, which is the expected ordering.
- **Do trust the ordering WITHIN the category.** A per-run offset that moves all four arms together cancels
  in the ratio, and the ratio replicates across two independent runs: Dwarf/Mapperly on Array is 1.246x in
  round 31 and 1.235x in round 30.

This is the whole reason the round-30 column is measured rather than quoted. One run cannot tell a category
offset from a code property; two can.

### The control-arm check, stated as a table

Three of the arms in most categories are libraries this repository does not control, and `Flat_Hand` is a
hand-written literal copy that no commit here can affect. If our arm moves and theirs move with it, the RUN
moved. `SpanBlit` has a single control arm, so "inside the spread" is not a meaningful question there, and
`Nested` misses by 0.008x, which is arithmetic rather than signal.

| category | Dwarf r30/r31 | control arms (median) | control spread | our arm inside it? |
|---|---:|---:|---:|---|
| Array | 0.87x | 0.87x (n=3) | 0.87-0.92x | yes |
| Blit | 0.87x | 0.82x (n=3) | 0.80-0.92x | yes |
| Dict | 1.03x | 0.99x (n=3) | 0.95-1.04x | yes |
| Enum | 0.94x | 1.00x (n=2) | 0.99-1.02x | **no** |
| EnumByValue | 1.12x | 1.15x (n=2) | 1.06-1.23x | yes |
| Flat | 1.27x | 1.28x (n=4) | 1.20-1.35x | yes |
| Flatten | 1.21x | 0.99x (n=3) | 0.96-1.03x | **no** |
| List | 1.12x | 1.03x (n=3) | 1.02-1.03x | **no** |
| Nested | 1.07x | 1.02x (n=3) | 1.01-1.07x | **no** |
| NestedFill | 1.00x | 1.00x (n=3) | 0.99-1.01x | yes |
| NumList | 1.07x | 1.07x (n=3) | 1.05-1.12x | yes |
| SpanBlit | 1.10x | 1.13x (n=1) | 1.13-1.13x | **no** |
| Widen | 1.16x | 1.19x (n=3) | 1.09-1.28x | yes |

## Round 30 vs round 31 — every shared row

| row | r30 ns | r31 ns | r30/r31 | r30 alloc B | r31 alloc B | alloc |
|---|---:|---:|---:|---:|---:|---:|
| `Array_AutoMapper` | 5,747.62 | 6,598.27 | 0.87x | 48,048 | 48,048 | 1.00x |
| `Array_Dwarf` | 6,316.14 | 7,291.93 | 0.87x | 48,048 | 48,048 | 1.00x |
| `Array_Mapperly` | 5,113.24 | 5,854.00 | 0.87x | 48,048 | 48,048 | 1.00x |
| `Array_Mapster` | 6,545.75 | 7,145.65 | 0.92x | 48,048 | 48,048 | 1.00x |
| `AutoShare_Copy_Dwarf` | 9.73 | 12.09 | 0.80x | 48 | 48 | 1.00x |
| `AutoShare_Dwarf` | 7.58 | 6.43 | 1.18x | 24 | 24 | 1.00x |
| `BlitRatio_Array_Fast` | 478.71 | 407.17 | 1.18x | 12,048 | 12,048 | 1.00x |
| `BlitRatio_Array_Scalar` | 1,057.04 | 1,025.82 | 1.03x | 16,048 | 16,048 | 1.00x |
| `BlitRatio_List_Fast` | 445.67 | 406.73 | 1.10x | 12,112 | 12,112 | 1.00x |
| `BlitRatio_List_Scalar` | 1,071.86 | 1,043.81 | 1.03x | 16,112 | 16,112 | 1.00x |
| `Blit_AutoMapper` | 1,040.42 | 1,124.85 | 0.92x | 12,048 | 12,048 | 1.00x |
| `Blit_Dwarf` | 417.41 | 479.52 | 0.87x | 12,048 | 12,048 | 1.00x |
| `Blit_Mapperly` | 954.36 | 1,192.30 | 0.80x | 12,048 | 12,048 | 1.00x |
| `Blit_Mapster` | 1,007.12 | 1,229.90 | 0.82x | 12,048 | 12,048 | 1.00x |
| `DenseEnum_Dict_Dwarf` | 40.86 | 38.82 | 1.05x | 384 | 384 | 1.00x |
| `DenseEnum_Dwarf` | 11.37 | 11.20 | 1.02x | 40 | 40 | 1.00x |
| `Dict_AutoMapper` | 19,151.17 | 20,257.37 | 0.95x | 102,320 | 102,320 | 1.00x |
| `Dict_Dwarf` | 8,396.77 | 8,151.14 | 1.03x | 31,120 | 31,120 | 1.00x |
| `Dict_Mapperly` | 19,513.40 | 18,726.20 | 1.04x | 31,176 | 31,176 | 1.00x |
| `Dict_Mapster` | 27,094.01 | 27,419.88 | 0.99x | 102,376 | 102,376 | 1.00x |
| `EnumByValue_Dwarf` | 3.58 | 3.20 | 1.12x | 24 | 24 | 1.00x |
| `EnumByValue_Mapperly` | 4.22 | 3.43 | 1.23x | 24 | 24 | 1.00x |
| `EnumByValue_Mapster` | 13.03 | 12.28 | 1.06x | 24 | 24 | 1.00x |
| `Enum_AutoMapper` | 76.50 | 74.87 | 1.02x | 48 | 48 | 1.00x |
| `Enum_Dwarf` | 12.71 | 13.57 | 0.94x | 24 | 24 | 1.00x |
| `Enum_Mapperly` | 12.85 | 13.03 | 0.99x | 24 | 24 | 1.00x |
| `Flat_AutoMapper` | 62.01 | 51.85 | 1.20x | 40 | 40 | 1.00x |
| `Flat_Dwarf` | 6.31 | 4.98 | 1.27x | 40 | 40 | 1.00x |
| `Flat_Hand` | 6.12 | 4.54 | 1.35x | 40 | 40 | 1.00x |
| `Flat_Mapperly` | 6.32 | 5.17 | 1.22x | 40 | 40 | 1.00x |
| `Flat_Mapster` | 18.08 | 13.50 | 1.34x | 40 | 40 | 1.00x |
| `Flatten_AutoMapper` | 55.08 | 55.75 | 0.99x | 48 | 48 | 1.00x |
| `Flatten_Dwarf` | 6.40 | 5.28 | 1.21x | 48 | 48 | 1.00x |
| `Flatten_Mapperly` | 5.82 | 5.64 | 1.03x | 48 | 48 | 1.00x |
| `Flatten_Mapster` | 14.23 | 14.83 | 0.96x | 48 | 48 | 1.00x |
| `Immutable_Dwarf` | 139.39 | 146.91 | 0.95x | 4,048 | 4,048 | 1.00x |
| `List_AutoMapper` | 8,863.98 | 8,613.89 | 1.03x | 56,656 | 56,656 | 1.00x |
| `List_Dwarf` | 6,288.60 | 5,630.27 | 1.12x | 48,112 | 48,112 | 1.00x |
| `List_Mapperly` | 6,075.29 | 5,950.73 | 1.02x | 48,112 | 48,112 | 1.00x |
| `List_Mapster` | 5,790.74 | 5,603.33 | 1.03x | 48,112 | 48,112 | 1.00x |
| `MapShare_Copy_Dwarf` | 363.02 | 333.42 | 1.09x | 8,080 | 8,080 | 1.00x |
| `MapShare_Dwarf` | 5.62 | 5.16 | 1.09x | 24 | 24 | 1.00x |
| `NestedFill_AutoMapper` | 2,034.60 | 2,042.28 | 1.00x | 10,776 | 10,776 | 1.00x |
| `NestedFill_Dwarf` | 949.66 | 952.51 | 1.00x | 8,112 | 8,112 | 1.00x |
| `NestedFill_Mapperly` | 1,828.07 | 1,817.55 | 1.01x | 9,456 | 9,456 | 1.00x |
| `NestedFill_Mapster` | 1,122.78 | 1,138.29 | 0.99x | 8,112 | 8,112 | 1.00x |
| `Nested_AutoMapper` | 62.16 | 61.41 | 1.01x | 112 | 112 | 1.00x |
| `Nested_Dwarf` | 12.05 | 11.23 | 1.07x | 112 | 112 | 1.00x |
| `Nested_Mapperly` | 11.77 | 11.05 | 1.07x | 112 | 112 | 1.00x |
| `Nested_Mapster` | 20.56 | 20.14 | 1.02x | 112 | 112 | 1.00x |
| `NullMismatch_Dwarf` | 4.67 | 4.70 | 0.99x | 32 | 32 | 1.00x |
| `NumList_AutoMapper` | 2,836.42 | 2,525.62 | 1.12x | 16,656 | 16,656 | 1.00x |
| `NumList_Dwarf` | 698.34 | 653.85 | 1.07x | 8,112 | 8,112 | 1.00x |
| `NumList_Mapperly` | 952.59 | 907.06 | 1.05x | 8,112 | 8,112 | 1.00x |
| `NumList_Mapster` | 972.14 | 912.39 | 1.07x | 8,112 | 8,112 | 1.00x |
| `Seq_Dwarf` | 8,400.83 | 7,616.34 | 1.10x | 48,088 | 48,088 | 1.00x |
| `Set_Dwarf` | 4,736.27 | 4,215.26 | 1.12x | 17,856 | 17,856 | 1.00x |
| `SpanBlit_Dwarf` | 154.42 | 140.71 | 1.10x | 0 | 0 | - |
| `SpanBlit_Scalar` | 785.61 | 692.50 | 1.13x | 0 | 0 | - |
| `Widen_AutoMapper` | 815.42 | 749.34 | 1.09x | 8,048 | 8,048 | 1.00x |
| `Widen_Dwarf` | 399.73 | 346.00 | 1.16x | 8,048 | 8,048 | 1.00x |
| `Widen_Mapperly` | 570.94 | 446.21 | 1.28x | 8,048 | 8,048 | 1.00x |
| `Widen_Mapster` | 824.89 | 695.07 | 1.19x | 8,048 | 8,048 | 1.00x |

**Allocation is byte-identical on all 63 shared rows.**

## Round 31 — every category against every library (mean ns)

| category | Hand | Dwarf | Mapperly | Mapster | AutoMapper |
|---|---:|---:|---:|---:|---:|
| Array | - | 7,291.93 | 5,854.00 | 7,145.65 | 6,598.27 |
| AutoShare | - | 6.43 | - | - | - |
| AutoShare_Copy | - | 12.09 | - | - | - |
| Blit | - | 479.52 | 1,192.30 | 1,229.90 | 1,124.85 |
| DenseEnum | - | 11.20 | - | - | - |
| DenseEnum_Dict | - | 38.82 | - | - | - |
| Dict | - | 8,151.14 | 18,726.20 | 27,419.88 | 20,257.37 |
| Enum | - | 13.57 | 13.03 | - | 74.87 |
| EnumByValue | - | 3.20 | 3.43 | 12.28 | - |
| Flat | 4.54 | 4.98 | 5.17 | 13.50 | 51.85 |
| Flatten | - | 5.28 | 5.64 | 14.83 | 55.75 |
| Immutable | - | 146.91 | - | - | - |
| List | - | 5,630.27 | 5,950.73 | 5,603.33 | 8,613.89 |
| MapShare | - | 5.16 | - | - | - |
| MapShare_Copy | - | 333.42 | - | - | - |
| Nested | - | 11.23 | 11.05 | 20.14 | 61.41 |
| NestedFill | - | 952.51 | 1,817.55 | 1,138.29 | 2,042.28 |
| NullMismatch | - | 4.70 | - | - | - |
| NumList | - | 653.85 | 907.06 | 912.39 | 2,525.62 |
| Seq | - | 7,616.34 | - | - | - |
| Set | - | 4,215.26 | - | - | - |
| SpanBlit | - | 140.71 | - | - | - |
| Widen | - | 346.00 | 446.21 | 695.07 | 749.34 |

## Round 31 — every category against every library (allocated B/op)

| category | Hand | Dwarf | Mapperly | Mapster | AutoMapper |
|---|---:|---:|---:|---:|---:|
| Array | - | 48,048 | 48,048 | 48,048 | 48,048 |
| AutoShare | - | 24 | - | - | - |
| AutoShare_Copy | - | 48 | - | - | - |
| Blit | - | 12,048 | 12,048 | 12,048 | 12,048 |
| DenseEnum | - | 40 | - | - | - |
| DenseEnum_Dict | - | 384 | - | - | - |
| Dict | - | 31,120 | 31,176 | 102,376 | 102,320 |
| Enum | - | 24 | 24 | - | 48 |
| EnumByValue | - | 24 | 24 | 24 | - |
| Flat | 40 | 40 | 40 | 40 | 40 |
| Flatten | - | 48 | 48 | 48 | 48 |
| Immutable | - | 4,048 | - | - | - |
| List | - | 48,112 | 48,112 | 48,112 | 56,656 |
| MapShare | - | 24 | - | - | - |
| MapShare_Copy | - | 8,080 | - | - | - |
| Nested | - | 112 | 112 | 112 | 112 |
| NestedFill | - | 8,112 | 9,456 | 8,112 | 10,776 |
| NullMismatch | - | 32 | - | - | - |
| NumList | - | 8,112 | 8,112 | 8,112 | 16,656 |
| Seq | - | 48,088 | - | - | - |
| Set | - | 17,856 | - | - | - |
| SpanBlit | - | 0 | - | - | - |
| Widen | - | 8,048 | 8,048 | 8,048 | 8,048 |

## Round 31 — in-repo A/B pairs (one emission against its own alternative)

| pair | fast ns | scalar ns | scalar/fast |
|---|---:|---:|---:|
| BlitRatio_Array | 407.17 | 1,025.82 | 2.52x |
| BlitRatio_List | 406.73 | 1,043.81 | 2.57x |
| SpanBlit | - | 692.50 | - |

## The ambient facade (round 31 only — the class is new)

| row | mean ns | alloc B |
|---|---:|---:|
| `FacadeFlat_AutoMapper` | 56.16 | 40 |
| `FacadeFlat_DwarfDirect` | 5.12 | 40 |
| `FacadeFlat_DwarfObject` | 19.10 | 40 |
| `FacadeFlat_DwarfPair` | 13.69 | 40 |
| `FacadeFlat_DwarfPair_Before` | 18.17 | 40 |
| `FacadeFlat_Hand` | 5.22 | 40 |
| `FacadeFlat_Mapster` | 14.35 | 40 |
| `FacadeNested_AutoMapper` | 62.56 | 112 |
| `FacadeNested_DwarfDirect` | 12.02 | 112 |
| `FacadeNested_DwarfObject` | 26.01 | 112 |
| `FacadeNested_DwarfPair` | 19.55 | 112 |
| `FacadeNested_DwarfPair_Before` | 24.51 | 112 |
| `FacadeNested_Mapster` | 20.61 | 112 |
| `FacadeUpdate_AutoMapper` | 51.84 | 0 |
| `FacadeUpdate_DwarfDirect` | 1.98 | 0 |
| `FacadeUpdate_DwarfPair` | 9.08 | 0 |
| `FacadeUpdate_DwarfPair_Before` | 15.10 | 0 |
| `FacadeUpdate_Mapster` | 10.91 | 0 |

### T12 before/after

- **FacadeFlat**: before 18.17 ns -> after 13.69 ns (1.33x), saving 4.49 ns; direct call 5.12 ns, so dispatch overhead 13.05 ns -> 8.57 ns
- **FacadeNested**: before 24.51 ns -> after 19.55 ns (1.25x), saving 4.96 ns; direct call 12.02 ns, so dispatch overhead 12.49 ns -> 7.53 ns
- **FacadeUpdate**: before 15.10 ns -> after 9.08 ns (1.66x), saving 6.02 ns; direct call 1.98 ns, so dispatch overhead 13.12 ns -> 7.10 ns

## The Array gap against Mapperly is the null guards, and it is now a number

The 1.24x is real and has a mechanism, found by reading both generators' output rather than by inference.
The loops are the same shape — `new T[src.Length]`, then an indexed `for`. Two guards differ:

```csharp
// DwarfMapper
__r[__i] = (__item is null ? null! : (FlatDst)MapFlat(__item));   // per-element ternary
public partial FlatDst MapFlat(FlatSrc s) { ArgumentNullException.ThrowIfNull(s); ... }   // and again
```
```csharp
// Mapperly
target[i] = MapFlat(source[i]);
private FlatDst MapFlat(FlatSrc s) { var target = new FlatDst(); ... }    // neither guard
```

Two thousand extra branches on a 1,000-element array, and the extra basic blocks are enough to change
whether the element map clears the JIT's inlining budget. That is the resilience-first default being paid
for — a deliberate design property of this library, not a missing optimisation — and
`CollectionReadProbeBenchmarks` now attributes it arm by arm (ternary only, guard only, both, neither) so a
future decision about it rests on the split rather than on this paragraph.

## What SIMD can and cannot reach, restated because the question keeps coming back

`FlatSrc[] → FlatDst[]` is a thousand separate heap objects, each holding a `string` reference. There is
nothing contiguous to vectorise: the work is a thousand allocations plus four field copies each. That is why
all four libraries land within 25 % of one another on `Array` and `List` **and allocate the identical
48,048 B / 48,112 B** — the row is allocation-bound, and no instruction-level change moves it.

`Dict` is not SIMD either. Its 2.3–3.4x lead is pre-sizing plus not re-hashing, and its allocation lead
(31,120 B against 102,376 for Mapster and 102,320 for AutoMapper) is the durable part of that result.

SIMD reaches exactly where the elements are blittable value types: `Blit` (480 ns against 1,192 / 1,230 /
1,125) and `Widen` (346 against 446 / 695 / 749). The idea of extending it to reference-element collections
was measured in round 29 and is a ~2 % non-win; what decides those rows is destination storage, where struct
DTO arrays measured 2–25x with 43 % less memory (`Issues/round29`). None of that is new work in round 31 and
none of it is claimed here.

## The list fill for reference elements is already ruled on

Round 31's T09 gave the ambient registry's collection helper an `AsSpan` read of a `List<T>` source, which
raises the obvious question of whether the directly emitted walk should follow. It should not, and the reason
is measured: `Issues/round26/FINDING-list-fill-strategy.md` records `Src[] → List<Dst>` carrying a string at
**1.00x, then 0.92x** on a second run, because allocating the destination objects dominates. The emitter
therefore takes `CollectionsMarshal.SetCount` + a span write only when the element is a value type, which
`CollectionConverter.EmitList` states at the site.

The two are also not the same optimisation: T09's helper uses `AsSpan` to **read** the source, while the
emitter's value-element path uses `SetCount` + `AsSpan` to **write** the destination. The read side for
reference elements is the one thing round 26 did not measure, so `CollectionReadProbeBenchmarks` measures it
— `foreach` over a struct enumerator against an indexed span read, everything else held equal. The mechanism
predicts no difference worth a 1,014-case golden regeneration; the probe is there so that prediction is
checked rather than trusted.

## The two probes, and what they settled

`CollectionReadProbeBenchmarks`, same machine and job, N = 1,000, reference elements throughout.

| arm | mean | ratio to floor |
|---|---:|---:|
| `ArrayGuards_None` — Mapperly's shape, neither guard | 5.191 µs | 1.00 |
| `ArrayGuards_ThrowIfNullOnly` — the element map's own guard | 5.539 µs | 1.07 |
| `ArrayGuards_TernaryOnly` — the call-site null ternary | 6.253 µs | 1.20 |
| `ArrayGuards_Both` — what the generator emits | 6.483 µs | **1.25** |
| `ListRead_Foreach` — what the emitter writes for a List source | 6.445 µs | 1.00 |
| `ListRead_Span` — an indexed `CollectionsMarshal.AsSpan` read | 5.499 µs | **0.85** |

### Probe 2 confirms an existing ruling rather than proposing anything

The guards together cost **1.25x** over a guard-free loop, against the 1.235x / 1.246x Dwarf-versus-Mapperly
`Array` gap measured independently in the two matrix runs. Those agree closely enough that the guards account
for essentially the whole gap — which is arithmetic this repository already has. `b25ae56` withdrew exactly this
as a finding, because the call-site ternary is a RULING: `6fa7308` introduced
`NullHandling.NullableProjectRefForgiving` for this precise cell (the `Result<T>`/`Outcome<T>` shape whose Fail
parks `default!`), and removing the ternary reintroduces a throw on every failed `Result<T>`. `9520b9a` settles
the neighbouring cell the other way on purpose. So the `Array` row is a trade — mapping a null element to null
where Mapperly dereferences it — and no version of closing it keeps the behaviour.

What is new is only the precision: 1.25x on this machine rather than the earlier "8–29 %" range, and the
attribution split, which says the **call-site ternary is the expensive half** (1.20x) and the callee's
`ThrowIfNull` is not distinguishable from zero (5.539 ± 519 ns against a 5.191 ± 216 ns floor). That matches the
round-30 finding's "removing the callee's ThrowIfNull buys nothing".

### Probe 1 found a real 1.17x, and then found a defect in round 31's own T09

The prediction going in — from round 26's mechanism — was that the read side would measure ~1.0x like the write
side. It did not: 6.445 µs against 5.499 µs, a 946 ns gap against a combined standard deviation of about
200 ns. `foreach` over a `List<T>` pays a version check per `MoveNext`, and at 1,000 elements that is roughly
0.95 ns each. Round 26 measured the destination WRITE strategy; this is the source READ, and it was not covered.

The finding does not survive contact with what makes that version check exist. **`DwarfCollectionMap.ToList`,
added in this round's T09, already takes the span read — and calls `map` inside the span loop:**

```csharp
if (source is List<TSource> list)
{
    var span = CollectionsMarshal.AsSpan(list);
    for (var i = 0; i < span.Length; i++) fromList.Add(map(span[i]));
}
```

`map` is a generated mapper. It can run a user `BeforeMap`/`AfterMap` hook or a user-declared converter, and
either can mutate the source list. Under `foreach` that throws `InvalidOperationException` from the enumerator's
version check. Under the span it does not: the list swaps to a new backing array and the span keeps reading the
old one, returning a silently stale result. Nothing is memory-unsafe — the old array is still a live managed
object — but a loud failure became a quiet wrong answer, which is the same trade this repository refused on the
null ternary two rounds ago.

The array arm is unaffected: an array cannot grow, so indexing it while user code runs is sound. Only the
`List<TSource>` arm has the hazard, and only because the pre-size and the span read were taken together when only
the pre-size was needed. `Fix_ToList_ListSource_Counted` in `RegistryCollectionBenchmarks` prices the
alternative — exact pre-size from `TryGetNonEnumeratedCount`, then a version-checked walk — so the decision rests
on what the safety actually costs rather than on which way the argument is phrased.
