# Usage-space sweep — 10^1 to 10^6, measured 2026-09-07

**Why this exists.** Every performance figure in round 29 rests on exactly two points, N=1,000 and
N=100,000. That is enough to decide what to *build*; it is not enough to set a runtime *gate*. A threshold
chosen between two measurements is invented, not measured. The owner asked for logarithmic coverage, so
this sweeps six decades and gives each size-dependent optimization a measured boundary.

**Method.** AMD Ryzen 5 5600, Windows 10, .NET 10.0.11, BenchmarkDotNet 0.14, `Job.ShortRun` (3 warmup,
3 iterations), `MemoryDiagnoser`. Ran as a standalone probe outside the repository, deliberately: the
gated suite `benchmarks/DwarfMapper.Benchmarks` is pinned to exact allocation bytes at N=1000 and the
housekeeping gate asserts an exact benchmark count, so adding params there would move both.

**A caveat that bounds every number below.** The probe had to use the **in-process toolchain**, because
BenchmarkDotNet resolves its generated project against the working directory and the probe lives outside
the repo tree. In-process runs share GC and process state between benchmarks, which weakens isolation
compared with the gated suite. Two points below are non-monotonic and both carry large error bars; they
are called out individually and **must be re-measured out-of-process before any gate threshold is pinned
to them**.

## A — blit (`MemoryMarshal.Cast` + `CopyTo`) vs the scalar element loop

| N | scalar | blit | ratio |
|---:|---:|---:|---:|
| 10 | 28.2 ns | 20.5 ns | **0.73** |
| 100 | 232.3 ns | 135.0 ns | **0.58** |
| 1,000 | 2,245.8 ns | 1,313.1 ns | **0.59** |
| 10,000 | 148.8 µs | 129.5 µs | **0.87** |
| 100,000 | 1,110.6 µs | 693.7 µs | **0.63** |
| 1,000,000 | 5,509.6 µs | 3,593.3 µs | **0.65** |

**Usage space: everywhere from ten elements up. There is no crossover IN THIS RANGE, so there is no
threshold to set within it.** The blit wins at every decade from ten elements to a million, never loses, and
holds roughly 0.6x once past the smallest sizes. **This validates the current always-on design and removes
the need for a length gate on this path.**

> **CORRECTED 2026-09-09.** The sentence above originally read *"Usage space: everywhere. There is no
> crossover, so there is no threshold to set"* — an unqualified claim about all sizes, drawn from a sweep
> that **starts at N = 10**. Extending the same comparison down one decade
> ([`benchmarks/results/2026-09-09-collection-decade-sweep.md`](../../benchmarks/results/2026-09-09-collection-decade-sweep.md))
> measured **0.84x at N = 1** — the blit is slightly SLOWER with a single element (8.4 ns against the scalar
> twin's 7.0 ns, err +/-6%), because `MemoryMarshal.Cast` + `CopyTo` has setup that one element cannot
> amortise. **There is a crossover and it is below ten.**
>
> The conclusion about the CODE survives: 1.4 ns is not worth a runtime length check, and a compile-time
> gate cannot know N. What did not survive is the word "everywhere", which claimed a range the measurement
> never covered — the same "right number, wrong population" error this round recorded five times, made once
> more by the document that recorded them.

It also corrects a two-point reading: the earlier suite showed `SpanMap_Blit` at 0.21x (1k) and 0.98x
(100k), which looked like a win that evaporates above cache. Swept properly, the blit does not evaporate.

## B — span map (no allocation either side)

| N | scalar | blit | ratio |
|---:|---:|---:|---:|
| 10 | 12.7 ns | 5.3 ns | **0.41** |
| 100 | 105.4 ns | 33.4 ns | **0.32** |
| 1,000 | 1,088.3 ns | 538.0 ns | **0.49** |
| 10,000 | 10.6 µs | 6.1 µs | **0.58** |
| 100,000 | 105.2 µs | 154.5 µs | **1.47 — see below** |
| 1,000,000 | 2,920.2 µs | 1,661.3 µs | **0.57** |

**The 100,000 row is the least trustworthy number in this document and it is the one a naive reading would
set a threshold from.** It is non-monotonic (a loss at 100k sitting between wins at 10k and 1M), and its
baseline carries an error of 65,898 ns against a mean of 105,179 ns — **a 63 % error bar**. A single
anomalous point bracketed by wins on both sides is a measurement to repeat, not a boundary to pin.
Re-measure out-of-process before drawing any conclusion.

## C — `TensorPrimitives.ConvertChecked<int,long>` vs the scalar convert loop

| N | scalar | tensor | ratio |
|---:|---:|---:|---:|
| 10 | 6.5 ns | 2.5 ns | **0.39** |
| 100 | 59.5 ns | 9.1 ns | **0.15** |
| 1,000 | 603.7 ns | 100.4 ns | **0.17** |
| 10,000 | 5,781.6 ns | 1,027.7 ns | **0.18** |
| 100,000 | 54.8 µs | 11.0 µs | **0.20** |
| 1,000,000 | 536.2 µs | 131.5 µs | **0.25** |

**This overturns the round's verdict on Phase 3.3.** The plan recorded TensorPrimitives as marginal —
0.60x at 1k, neutral above cache — and ranked it last on that basis. On a homogeneous primitive array it
is **four to six times faster across five decades**, degrading only slowly as memory bandwidth takes over.

**Do not over-read it.** This measures a pure `int[]` -> `long[]` conversion, which is TensorPrimitives'
best case. The earlier figure measured the mapper's `Widen` shape, where per-element mapping work
surrounds the conversion and can dominate it. The two disagree because they measure **different
workloads, not different sizes** — so the honest conclusion is that the usage space is bounded by SHAPE
(how much non-conversion work rides along), and that boundary is still unmeasured. That is the next
measurement, and it decides whether 3.3 stays last in the queue or moves up.

## D — destination shape: class DTOs vs struct DTOs (what DWARF103 advises)

| N | classes | structs | ratio | alloc ratio |
|---:|---:|---:|---:|---:|
| 10 | 64.7 ns | 25.3 ns | **0.39** | 0.59 |
| 100 | 623.0 ns | 217.3 ns | **0.35** | — |
| 1,000 | 6,279.1 ns | 2,341.1 ns | **0.37** | — |
| 10,000 | 68.2 µs | 129.4 µs | **1.90 — see below** | — |
| 100,000 | 4,611.1 µs | 947.3 µs | **0.21** | — |
| 1,000,000 | 53,614.4 µs | 5,850.7 µs | **0.11** | — |

**The advice holds at every scale except one anomalous point.** Structs win ~0.35-0.39x below a thousand
elements and the win *grows* with size — 0.21x at 100k and **0.11x at a million**, where the class version
spends 53.6 ms against 5.9 ms. That growth is the allocation and GC cost of a million small objects, and
it is the strongest argument in the round for the Phase 2 advice.

The 10,000 row inverts, and it is the second point to distrust: `Dest_Structs` takes 55x longer going from
1k to 10k while the surrounding decades scale linearly, which is the signature of a GC or allocation
artifact in a shared process rather than a property of the shape. Re-measure out-of-process.

## What this changes

1. **Blit needs no length gate.** Measured, not assumed — it wins at every decade. The always-on design
   is correct as shipped.
2. **TensorPrimitives is not marginal on primitive arrays** and its plan ranking was set from a figure
   describing a different workload. Its usage space is bounded by shape, not size, and that boundary is
   the next thing to measure.
3. **The struct-destination advice strengthens with scale**, which is the opposite of most optimizations
   here and worth stating in `docs/PERFORMANCE.md`.
4. **Two points need re-measurement out-of-process before anything is pinned to them** — `B` at 100k and
   `D` at 10k. Both are non-monotonic, both carry large error bars, and both sit exactly where a careless
   reading would place a threshold.
