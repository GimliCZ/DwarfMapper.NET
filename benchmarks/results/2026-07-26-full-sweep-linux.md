# Benchmark results — full sweep (2026-07-26, Linux)

First full sweep on Linux since `2026-07-05-flat-blit-linux.md`, and the first to cover **every** category.
Run to check for a speed regression after the derived-documentation work; see "Why this run exists" below for
why that question was answerable before a single benchmark was executed.

Every category was measured with `DefaultJob` (no `ShortRun`) — see *Rejected measurements*.

---

## Environment

```
BenchmarkDotNet v0.14.0, Pop!_OS 24.04 LTS
AMD Ryzen 5 5600, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.110
  [Host]     : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX2
GC=Concurrent Workstation
HardwareIntrinsics=AVX2,AES,BMI1,BMI2,FMA,LZCNT,PCLMUL,POPCNT VectorSize=256
```

### Conditions that were NOT ideal, recorded rather than hidden

| Condition | Value | Effect |
|---|---|---|
| CPU governor | **`powersave`**, scaling reported at 78% | absolute ns inflated vs a `performance`-governor run |
| Background load | Rider + Rider.Backend + Firefox, ~43% CPU across them | inflates absolutes; not silenced — they are the owner's live session |
| Load average at start | 0.26 (1 min) — settled after a prior 14-minute mutation battery | first chunks ran coolest |
| Load average at end | ~2.7–3.3 | later chunks ran warmer than earlier ones |

Nothing here was tuned for the benchmark: no governor change, no process killed. That keeps the run honest
about the machine it was taken on, at the cost of absolute precision. **Ratios within a chunk are the usable
figure; absolute nanoseconds are not comparable to any other file.**

---

## Methodology

**Payloads.** Every fixture is drawn from `ObjectFactoryV2` — the same source the test suites fuzz with — so
the measured distribution contains nulls (~15%), boundary numerics and varied string lengths rather than
uniform literals. Each shape uses a distinct salt so categories are not correlated draws of one another.
`RealisticPayloads.AssertRealistic` fails the run if a draw comes back degenerate. `[GlobalSetup]` is not
measured. Collection **content** is factory-drawn; collection **count** is pinned to `N` (the factory builds
1–3 element collections, which would otherwise turn an N=1000 benchmark into N≈2).

**Every mapper in a category receives the identical instance**, so a category row is like-for-like.

**`N = 1000`** for collection categories. Single-object categories still carry the `N` column because it is a
class-level `[Params]`; it does not change their work.

**Chunking.** 82 benchmark methods do not fit one foreground window in this sandbox (BenchmarkDotNet is reaped
when backgrounded — exit 144). The sweep ran as seven sequential foreground runs, each under a 555 s
command-level timeout:

| # | Categories | Notes |
|---|---|---|
| 1 | Flat | `--warmupCount 10 --iterationCount 20` (low-noise; sub-10 ns needs it) |
| 2 | Nested, Flatten | DefaultJob |
| 3 | Enum, NullMismatch | DefaultJob |
| 4 | Array, List, Seq | DefaultJob |
| 5 | Blit | DefaultJob (run alone; Blit+Widen together exceeded the window) |
| 6 | Widen, Set, Immutable | DefaultJob |
| 7 | Dict | DefaultJob |

**Consequence, and it is the main caveat of this file:** chunks ran over ~40 minutes of sustained load, so the
machine warmed as the sweep progressed. Compare *within* a chunk freely. Comparing a chunk-1 absolute against
a chunk-7 absolute is not valid.

---

## What each category actually measures

| Category | Source → Target | What it exercises | Coverage |
|---|---|---|---|
| **Flat** | `FlatSrc{int,string?,long,bool}` → `FlatDst` | the floor: four direct assignments, no conversion. `Flat_Hand` is a literal hand-written constructor call and the ratio baseline | 4 libs + hand |
| **Nested** | `NestedSrc{int, FlatSrc}` → `NestedDst` | one nested reference member, mapped by a synthesised inner map | 4 libs |
| **Flatten** | `FlOrder.Customer.Name` → `FlOrderDto.CustomerName` | dotted-path flattening (Entity→DTO, eShopOnWeb-style) | 4 libs |
| **Enum** | `BenchStatus` → `BenchStatusDto` | enum-to-enum by name | 4 libs |
| **Array** | `FlatSrc[]` → `FlatDst[]`, 1000 elements | per-element reference mapping over an array | 4 libs |
| **List** | `List<FlatSrc>` → `List<FlatDst>` | same, list-shaped | 4 libs |
| **Seq** | `IEnumerable<FlatSrc>` (a `List` at runtime) → `List<FlatDst>` | the runtime-type probe hit, the common real case | Dwarf only |
| **Blit** | `Vec3Src[]` → `Vec3Dst[]` (`struct{float X,Y,Z}`) | **the blittable claim**: unmanaged, layout-identical, same field names → whole-span bulk copy instead of a per-element loop | 4 libs |
| **Widen** | `int[]` → `long[]` | `Vector.Widen`; competitors copy element-by-element | 4 libs |
| **Dict** | `Dictionary<string,int>` → `Dictionary<string,long>` | value-type change is deliberate: with identical types Mapperly returns the SOURCE dictionary by reference, so a same-type row would measure aliasing against copying | 4 libs |
| **NullMismatch** | `string?` → `string` via `NullSubstitute` | the `DWARF070` shape; emits a real `?? "<sub>"` coalesce, so this measures the null CHECK, not a branch-free copy | Dwarf only |
| **Set** | `int[]` → `HashSet<int>` | hashing + dedup, not a linear fill | Dwarf only |
| **Immutable** | `int[]` → `ImmutableArray<int>` | builder + freeze | Dwarf only |

---

## Results

### Flat — chunk 1, low-noise (10 warmup / 20 iterations)

| Method | Mean | Error | StdDev | Ratio | Allocated |
|---|---:|---:|---:|---:|---:|
| `Flat_Hand` (baseline) | 7.124 ns | 0.3631 | 0.4181 | 1.00 | 40 B |
| `Flat_Dwarf` | **6.821 ns** | 0.2018 | 0.2159 | **0.96** | 40 B |
| `Flat_Mapperly` | 6.899 ns | 0.1763 | 0.1960 | 0.97 | 40 B |
| `Flat_Mapster` | 16.271 ns | 0.1919 | 0.1971 | 2.29 | 40 B |
| `Flat_AutoMapper` | 56.726 ns | 0.5257 | 0.6054 | 7.99 | 40 B |

DwarfMapper and Mapperly are indistinguishable from each other and from hand-written code — 0.96 and 0.97
against a baseline whose own StdDev is ±0.42. **Do not read 0.96 as "faster than hand-written."** It is a tie.

### Nested / Flatten — chunk 2

| Method | Mean | Error | StdDev | Allocated |
|---|---:|---:|---:|---:|
| `Nested_Dwarf` | **16.928 ns** | 0.2949 | 0.2759 | 112 B |
| `Nested_Mapperly` | 16.969 ns | 0.3751 | 0.4744 | 112 B |
| `Nested_Mapster` | 27.296 ns | 0.4860 | 0.4058 | 112 B |
| `Nested_AutoMapper` | 64.551 ns | 1.0452 | 0.9777 | 112 B |
| `Flatten_Dwarf` | 8.083 ns | 0.1264 | 0.1120 | 48 B |
| `Flatten_Mapperly` | **6.809 ns** | 0.1837 | 0.3540 | 48 B |
| `Flatten_Mapster` | 15.965 ns | 0.2535 | 0.2117 | 48 B |
| `Flatten_AutoMapper` | 57.483 ns | 1.0527 | 0.9847 | 48 B |

Nested is a dead tie. **Flatten goes to Mapperly** (6.81 vs 8.08 ns, ~19%) — outside the error bars, so it is
a real if small gap, not noise.

### Enum / NullMismatch — chunk 3

| Method | Mean | Error | StdDev | Allocated |
|---|---:|---:|---:|---:|
| `Enum_Dwarf` | 6.062 ns | 0.1448 | 0.1284 | 24 B |
| `Enum_Mapperly` | **5.437 ns** | 0.1525 | 0.1351 | 24 B |
| `Enum_Mapster` | 14.342 ns | 0.1632 | 0.1526 | 24 B |
| `Enum_AutoMapper` | 79.364 ns | 0.9905 | 0.9265 | 48 B |
| `NullMismatch_Dwarf` | 7.108 ns | 0.1175 | 0.1041 | 32 B |

**Enum goes to Mapperly** (~11%), again outside the error bars.

### Array / List / Seq — chunk 4

| Method | Mean | Error | StdDev | Allocated |
|---|---:|---:|---:|---:|
| `Array_Dwarf` | **7.811 µs** | 0.1551 | 0.3470 | 46.92 KB |
| `Array_Mapperly` | 8.331 µs | 0.1665 | 0.4238 | 46.92 KB |
| `Array_Mapster` | 8.707 µs | 0.1578 | 0.2410 | 46.92 KB |
| `Array_AutoMapper` | 8.077 µs | 0.1575 | 0.1547 | 46.92 KB |
| `List_Dwarf` | **8.396 µs** | 0.1628 | 0.2117 | 46.98 KB |
| `List_Mapperly` | 8.622 µs | 0.1707 | 0.2989 | 46.98 KB |
| `List_Mapster` | 8.529 µs | 0.1685 | 0.3403 | 46.98 KB |
| `List_AutoMapper` | 12.893 µs | 0.3611 | 0.9823 | 55.33 KB |
| `Seq_Dwarf` | 9.859 µs | 0.1804 | 0.1687 | 46.96 KB |

Array and List are **four-way ties within noise** for the three compile-time mappers (StdDev up to ±0.42 µs
against gaps of ~0.5 µs). DwarfMapper is nominally first in both; that is not a claim worth making. Only
AutoMapper's List row is genuinely apart, and it allocates 18% more.

### Blit — chunk 5 (run alone)

| Method | Mean | Error | StdDev | Allocated |
|---|---:|---:|---:|---:|
| `Blit_Dwarf` | **607.4 ns** | 12.19 | 34.57 | 11.77 KB |
| `Blit_Mapperly` | 1,145.5 ns | 22.81 | 34.13 | 11.77 KB |
| `Blit_Mapster` | 1,206.1 ns | 21.91 | 34.11 | 11.77 KB |
| `Blit_AutoMapper` | 1,213.4 ns | 18.11 | 19.38 | 11.77 KB |

**The headline, and the one place the lead is unambiguous: 1.89× faster than Mapperly**, 1.99× vs Mapster,
2.00× vs AutoMapper — all far outside the error bars. This is the bulk-copy fast path doing exactly what the
README claims ("~1.8–2.0×"), on the data shape it claims it for. Allocation is identical across all four: the
win is in the copy, not in allocating less.

### Widen / Set / Immutable — chunk 6

| Method | Mean | Error | StdDev | Allocated |
|---|---:|---:|---:|---:|
| `Widen_Dwarf` | **474.0 ns** | 9.50 | 12.68 | 7.86 KB |
| `Widen_Mapperly` | 639.9 ns | 12.86 | 26.84 | 7.86 KB |
| `Widen_Mapster` | 852.4 ns | 16.28 | 21.74 | 7.86 KB |
| `Widen_AutoMapper` | 955.9 ns | 19.16 | 52.14 | 7.86 KB |
| `Set_Dwarf` | 4,418.3 ns | 88.10 | 129.13 | 17.44 KB |
| `Immutable_Dwarf` | 228.9 ns | 4.58 | 9.35 | 3.95 KB |

**Widen is a real 1.35× lead over Mapperly** and 1.80× over Mapster — outside the error bars. `Vector.Widen`
earns its place.

### Dict — chunk 7

| Method | Mean | Error | StdDev | Median | Allocated |
|---|---:|---:|---:|---:|---:|
| `Dict_Dwarf` | **10.10 µs** | 0.196 | 0.226 | 10.08 µs | **30.39 KB** |
| `Dict_Mapperly` | 11.33 µs | 0.198 | 0.166 | 11.29 µs | 30.45 KB |
| `Dict_Mapster` | 23.95 µs | 0.479 | 1.310 | 23.47 µs | 99.98 KB |
| `Dict_AutoMapper` | 23.41 µs | 0.464 | 1.093 | 23.32 µs | 99.92 KB |

DwarfMapper leads Mapperly by 1.12× and the runtime mappers by ~2.3×, allocating **3.3× less** than either
(30.4 KB vs ~100 KB) — and allocation is load-independent, so that figure is trustworthy regardless of the
machine caveats.

---

## Cross-library summary (this run only)

| Category | DwarfMapper | Mapperly | Mapster | AutoMapper | Leader |
|---|---:|---:|---:|---:|---|
| Flat | 6.82 ns | 6.90 ns | 16.3 ns | 56.7 ns | tie (also ties hand-written) |
| Nested | 16.93 ns | 16.97 ns | 27.3 ns | 64.6 ns | tie |
| Flatten | 8.08 ns | **6.81 ns** | 16.0 ns | 57.5 ns | **Mapperly** |
| Enum | 6.06 ns | **5.44 ns** | 14.3 ns | 79.4 ns | **Mapperly** |
| Array (1000) | 7.81 µs | 8.33 µs | 8.71 µs | 8.08 µs | tie within noise |
| List (1000) | 8.40 µs | 8.62 µs | 8.53 µs | 12.89 µs | tie within noise |
| Blit (1000) | **607 ns** | 1,146 ns | 1,206 ns | 1,213 ns | **Dwarf, 1.89×** |
| Widen (1000) | **474 ns** | 640 ns | 852 ns | 956 ns | **Dwarf, 1.35×** |
| Dict (1000) | **10.10 µs / 30.4 KB** | 11.33 µs / 30.5 KB | 23.95 µs / 100 KB | 23.41 µs / 100 KB | **Dwarf** |

This supports the README's stance precisely as written: **parity on ordinary DTOs, a real lead only where the
data is blittable or layout-convertible.** It does not support a general "faster than Mapperly" reading —
Mapperly wins Flatten and Enum here.

---

## Two things a maintainer should know

**1. The "Dict ~2× over Mapperly" headline from `2026-07-25` does not reproduce — re-measured and settled
below.** See *Dict re-measurement*. The lead on Linux is **~1.14×**, not 2×. Not claimed in `README.md` or
`docs/COMPARISON.md`; only in that file's prose, which now carries a correction pointing here.

**2. Flatten and Enum go to Mapperly by margins outside the error bars** (~19% and ~11%). Small, and on
single-object shapes, but real rather than noise. Neither contradicts any published claim.

---

## Dict re-measurement — settling the Mapperly discrepancy

`2026-07-25` (Windows) recorded `Dict_Mapperly` at 20,050 ns against `Dict_Dwarf` at 9,964 ns and called the
resulting ~2× lead "the headline". It does not reproduce on Linux. Four independent measurements, the last two
under deliberately rising load:

| Measurement | Settings | `Dict_Dwarf` | `Dict_Mapperly` | Ratio |
|---|---|---:|---:|---:|
| Full sweep (chunk 7) | DefaultJob | 10.10 µs | 11.33 µs | **1.12×** |
| Re-measure 1 | 10 warmup / 20 iter | 10.47 µs | 11.37 µs | **1.09×** |
| Re-measure 2 | 10 warmup / 20 iter, load ~5 | 13.10 µs | 15.62 µs | **1.19×** |
| Re-measure 3 | 10 warmup / 20 iter, load ~7.5 | 12.86 µs | 14.84 µs | **1.15×** |

**Result: DwarfMapper leads Mapperly on `Dict` by ~1.14× (range 1.09–1.19), not 2×.** Absolutes climbed 25–35%
across the four as the machine loaded up; the ratio moved by 0.10. That is the textbook signature of a
load-sensitive absolute and a load-insensitive ratio, and it is why the ratio is the finding.

### What was ruled out

- **Not a Mapperly version change.** `Riok.Mapperly` has been pinned at `4.3.1` since the commit that
  introduced it; it has never been bumped.
- **Not a benchmark change.** The `2026-07-25` results file and the commit that made `Dict` a value-changing
  map (`0432ef1`) are the *same commit*, so those numbers were taken against today's exact fixture.
- **Not a difference in work done.** Allocation is identical in every run and on both platforms —
  `Dict_Dwarf` 30.39 KB, `Dict_Mapperly` 30.45 KB. Both genuinely allocate a new dictionary and convert every
  value; neither aliases. Allocation is load-independent, so this holds regardless of the machine caveats.
- **Not a shape difference.** Both are a partial `MapDict(DictSrc) → DictDst` on their own mapper class.

### What could not be ruled out

`Dict_Dwarf` measured 9,964 ns on Windows and 10.10 µs on the quietest Linux run — effectively identical.
`Dict_Mapperly` measured 20,050 ns there and 11.37 µs here. **The movement is entirely on Mapperly's side.**
Whether the Windows figure was an outlier in that run or a genuine Windows characteristic of Mapperly's
generated dictionary copy cannot be decided from Linux, and this sandbox has no Windows host. Someone with a
Windows machine should re-run `--anyCategories Dict` before either figure is cited.

**Until then, the defensible claim is the Linux one: Dict leads Mapperly by ~1.14×, and allocates 3.3× less
than Mapster/AutoMapper.** The allocation figure is the stronger half of that row and does not depend on any
of this.

## Why this run exists, and what it could not have shown

Run to check for a speed regression from the derived-documentation work merged the same day.

It could not have found one. That work changed no shipped code: `src/DwarfMapper`,
`src/DwarfMapper.Generator`, `src/DwarfMapper.CodeFixes` and `src/DwarfMapper.Testing` are byte-identical
across the merge, and the only addition under `src/` is `DwarfMapper.DocTooling` — `IsPackable=false`
build-time infrastructure that neither the runtime nor the generator references. The emitted mapping code is
therefore identical by construction. **This file is a baseline, not a verdict on that branch.**

## Rejected measurements

A first pass used `--job short` and its output was discarded, not published. It reported `Flat_Dwarf` at
14.063 ns ± **75.9 ns** — an error bar five times the mean — which presented as a 2.10× regression against
hand-written code. The DefaultJob re-run on the same commit gave 6.82 ns, ratio 0.96. Sub-10 ns benchmarks need
the full job; a ShortRun figure for them is not a weak measurement but a meaningless one.
