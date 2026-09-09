<!-- SPDX-License-Identifier: GPL-2.0-only -->

# 2026-09-09 - the collection surface across seven decades, against every rival

Requested by the owner: *"remesure benchmarks at the scale we requested and presend them. We are talking
about times where blit was used."*

Every comparative collection figure this repository publishes was taken at **N = 1000 and nowhere else**.
This is the same shapes across **10^0 to 10^6**, with Mapperly, Mapster and AutoMapper **in the same
process**, so no claim below depends on comparing two runs.

| | |
|---|---|
| Host | AMD Ryzen 5 5600, Windows 10.0.19045, 12 logical cores |
| Runtime | .NET 10, RyuJIT AVX2, Release |
| Job | 1 launch, 5 warmup, 10 measured iterations, `MemoryDiagnoser` |
| Competitors | Mapperly 4.3.1, Mapster 10.0.8, AutoMapper 14.0.0 (last MIT release) |
| Suite | `CollectionSweepBenchmarks` - NOT the gated class; nothing here is pinned |
| Payload | `ObjectFactoryV2` fixture draws (nulls, boundary numerics, varied string lengths) |

**Ratios are `rival / DwarfMapper`. Above 1.00 means we are faster.** The `err` column is DwarfMapper's own
standard error as a percentage of its mean; **a difference smaller than the combined error is not a
finding**, and three rows below are explicitly disqualified on that basis.

## The instrument had to be fixed first, and that is part of the result

The first pass used `[ShortRunJob]` (3 warmup, 3 iterations) on the argument that the question is the SHAPE
of a ratio across decades rather than a third decimal at one of them. Those rows could not carry the
argument: `SweepArray_Dwarf` measured **63,045 ns +/- 66,874 ns** at N = 10,000 - an error larger than the
mean - and **6.49 ms +/- 7.24 ms** at N = 100,000.

The cause is structural to this axis rather than bad luck, and it is visible in the GC counters. Past
roughly N = 10,000 a single operation allocates megabytes, so each iteration contains whole collections
whose cost lands in the timing. `SweepBlit_Dwarf` at N = 10,000 allocates **120,060 B** - past the 85 KB
**Large Object Heap** threshold - and reports `Gen0 = Gen1 = Gen2 = 36.99`, every collection a gen2. That is
why its mean jumps **132x** between N = 1,000 and N = 10,000 while the element count rises 10x.

The cliff is real and every library pays it - all four converge to 1.00-1.06x there - but resolving a
per-element difference through it takes iterations, not three of them. Re-run at 5 warmup / 10 iterations;
the tables below are that run. The ShortRun pass is kept as the reason the job is what it is.

### Array (class elements, `FlatSrc[]` to `FlatDst[]`)

| N | DwarfMapper | err | Mapperly | Mapster | AutoMapper | allocated |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 19 ns | +/-29% | 13 ns **0.71x** | 24 ns **1.25x** | 62 ns **3.26x** | 96 B |
| 10 | 69 ns | +/-6% | 58 ns **0.85x** | 78 ns **1.13x** | 119 ns **1.74x** | 528 B |
| 100 | 619 ns | +/-17% | 571 ns **0.92x** | 733 ns **1.18x** | 617 ns **1.00x** | 4848 B |
| 1,000 | 5,724 ns | +/-1% | 4,936 ns **0.86x** | 6,374 ns **1.11x** | 5,736 ns **1.00x** | 48048 B |
| 10,000 | 75.2 us | +/-12% | 53.8 us **0.72x** | 69.0 us **0.92x** | 58.2 us **0.77x** | 480048 B |
| 100,000 | 6.03 ms | +/-10% | 5.30 ms **0.88x** | 5.53 ms **0.92x** | 5.81 ms **0.96x** | 4800096 B |
| 1,000,000 | 62.27 ms | +/-3% | 56.99 ms **0.92x** | 78.24 ms **1.26x** | 66.16 ms **1.06x** | 48000233 B |

### List (class elements, `List<FlatSrc>` to `List<FlatDst>`)

| N | DwarfMapper | err | Mapperly | Mapster | AutoMapper | allocated |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 24 ns | +/-9% | 24 ns **0.97x** | 34 ns **1.38x** | 74 ns **3.06x** | 160 B |
| 10 | 83 ns | +/-2% | 98 ns **1.17x** | 115 ns **1.38x** | 220 ns **2.63x** | 592 B |
| 100 | 896 ns | +/-15% | 804 ns **0.90x** | 638 ns **0.71x** | 1,060 ns **1.18x** | 4912 B |
| 1,000 | 6,438 ns | +/-8% | 5,994 ns **0.93x** | 5,692 ns **0.88x** | 9,249 ns **1.44x** | 48112 B |
| 10,000 | 68.0 us | +/-10% | 80.7 us **1.19x** | 71.8 us **1.06x** | 203.5 us **2.99x** | 480112 B |
| 100,000 | 5.23 ms | +/-12% | 4.94 ms **0.94x** | 4.84 ms **0.93x** | 5.96 ms **1.14x** | 4800167 B |
| 1,000,000 | 60.90 ms | +/-4% | 75.56 ms **1.24x** | 80.25 ms **1.32x** | 83.65 ms **1.37x** | 48000314 B |

### Blit (layout-identical structs, `Vec3Src[]` to `Vec3Dst[]`)

| N | DwarfMapper | err | Mapperly | Mapster | AutoMapper | allocated |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 8 ns | +/-11% | 6 ns **0.76x** | 18 ns **2.15x** | 56 ns **6.67x** | 64 B |
| 10 | 11 ns | +/-3% | 14 ns **1.32x** | 32 ns **3.05x** | 68 ns **6.41x** | 168 B |
| 100 | 46 ns | +/-4% | 100 ns **2.20x** | 122 ns **2.67x** | 178 ns **3.90x** | 1248 B |
| 1,000 | 477 ns | +/-10% | 978 ns **2.05x** | 1,064 ns **2.23x** | 1,046 ns **2.19x** | 12048 B |
| 10,000 | 53.4 us | +/-2% | 56.4 us **1.06x** | 79.0 us **1.48x** | 80.5 us **1.51x** | 120060 B |
| 100,000 | 175.2 us | +/-5% | 224.1 us **1.28x** | 204.2 us **1.17x** | 250.2 us **1.43x** | 1200392 B |
| 1,000,000 | 1.29 ms | +/-4% | 1.87 ms **1.45x** | 1.91 ms **1.48x** | 1.90 ms **1.47x** | 12000278 B |

### Blit against its OWN scalar twin (identical payload, same process)

| N | blit | err | scalar twin | ratio |
|---:|---:|---:|---:|---:|
| 1 | 8 ns | +/-6% | 7 ns | **0.84x** |
| 10 | 11 ns | +/-4% | 16 ns | **1.42x** |
| 100 | 48 ns | +/-6% | 114 ns | **2.38x** |
| 1,000 | 434 ns | +/-7% | 1,052 ns | **2.42x** |
| 10,000 | 53.6 us | +/-1% | 70.0 us | **1.30x** |
| 100,000 | 165.1 us | +/-3% | 237.2 us | **1.44x** |
| 1,000,000 | 1.35 ms | +/-3% | 2.57 ms | **1.91x** |


## What the sweep settles

### 1. The blit's advantage is a CURVE, and the published number sits near its peak

2.05-2.20x at N = 100-1,000, collapsing to **1.06x** at the LOH cliff, recovering to **1.45x** at a million.
`docs/COMPARISON.md`'s "2.25x" was measured at N = 1000 - true, and near the maximum of the curve. Quoted
bare it over-describes the large-N case by roughly 1.5x and the small-N case entirely.

Against its own scalar twin the same shape appears: 2.38-2.42x at N = 100-1,000, 1.30x at the cliff,
**1.91x** at a million. The gate's 1.5x floor is pinned at N = 1000, which is inside the best region - a
floor set there does not describe the path at 10,000, where the honest ratio is 1.30x.

### 2. NEW: at N = 1 the blit is not a win, and the previous sweep could not have seen it

**0.84x against its own scalar twin** - the blit is *slower* with one element, 8.4 ns against 7.0 ns, err
+/-6%. `Issues/round29/sweep-results.md` concluded *"usage space: everywhere. There is no crossover, so
there is no threshold to set"* - and that sweep **started at N = 10**. There IS a crossover, and it is
below 10.

The correction is to the CLAIM, not necessarily to the code: 1.4 ns is not worth a runtime length gate, and
a compile-time one cannot know N. What must change is the sentence, which currently says "everywhere" on
evidence that begins at ten elements.

### 3. The `List` gap does not exist

`2026-08-24-premerge-full-sweep.md` recorded List as 1.13x behind Mapster at N = 1000. Across seven decades
DwarfMapper scatters either side of parity with no trend (0.90-1.19x vs Mapperly, 0.71-1.38x vs Mapster) and
**leads both at N = 10^6** - 1.24x Mapperly, 1.32x Mapster. The August figure was run composition.

This also closes a hypothesis before it cost anything: the proposed fix was to extend the
`CollectionsMarshal.SetCount` + span fill to reference elements, and
`Issues/round26/FINDING-list-fill-strategy.md` had ALREADY measured that as a non-win for exactly this case
(1.00x, then 0.92x). There was no gap, and the fix for it was already known not to work.

### 4. The `Array` gap DOES exist, at every decade, and it is explained

Mapperly leads at all seven sizes, 8-29%. Cause identified by reading both generators' output and confirmed
by a four-arm probe: a per-element null ternary that DwarfMapper's own synthesized element path does not
emit and that Mapperly does not emit at all. Full write-up, including the arm that refutes the obvious fix,
in `Issues/round30/FINDING-array-null-ternary.md`. It is a de-silencing item before it is a performance one
- the null arm stores `null!` into an array whose element type forbids null.

## Rows that are NOT findings

* **Array N = 1 (0.71x) and N = 100 (0.92x)** - err +/-29% and +/-17%. Directionally consistent with the
  rest of the column, which is the only reason they are quoted at all.
* **Blit N = 1 (0.76x vs Mapperly)** - err +/-11% on an 8 ns mean, and every library is inside a few
  nanoseconds of the others there. Read the scalar-twin row instead, which is a same-shape comparison.
* **List N = 100 vs Mapster (0.71x)** - err +/-15%, and the neighbouring decades disagree in sign.

## What this does NOT establish

1. **One class shape.** Every reference-element row is `FlatSrc` -> `FlatDst`: four members, one string,
   settable properties. A wide DTO, a constructor/record destination and a nested-object element are
   unmeasured at every size (`Issues/round30/DESIGN-collection-benchmarks.md`, axis 2).
2. **One blittable size.** `Vec3` is 12 B of float. The blit's usage space along ELEMENT SIZE is still
   undeclared - only N was swept, and only for this one struct (axis 3).
3. **No refusal controls.** Nothing here proves the blit correctly REFUSES a reordered, reference-carrying
   or differently-padded struct. `[StructLayout(Auto)]` still stands in for the whole eligibility rule.
4. **Windows.** The Dict platform lesson applies: these are Windows numbers.
