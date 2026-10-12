# Round 33 T02 — a widen pair in a List shape takes the `Vector.Widen` loop (2026-10-12)

AMD Ryzen 5 5600 (6 cores / 12 threads), Windows 10 22H2, .NET 10.0.11, X64 RyuJIT **AVX2**, BenchmarkDotNet 0.14.0
**DefaultJob**, under the heavy-test lock. The machine was in interactive use during both runs, which is visible in
some standard deviations; the medians agree with the means. **This is a Windows/AVX2 measurement: the perf lane runs
on Linux, and the Dict-vs-Mapperly figure (2.13x on Windows, ~1.14x on Linux) showed how far a Windows ratio can be
from Linux — read the ratios as this machine's, not as a portable claim.**

## Why

Round 31 T15 measured the generated `int[] → List<long>` fill **1.6x slower** than `Select(x => (long)x).ToList()`
(ShortRun, `benchmarks/results/2026-09-27-round31-t19-matrix.md`). The SIMD widen was gated to array → array
(`c641696`), so every List shape took the general value fill — `foreach (var __item in src) { __d[__i++] = __item; }`,
a write index the JIT cannot prove in range. Round 25 had given the block copy its List shapes (`97a9bed`); the widen
never followed.

## Baseline on master `ae82b11` (round 31's own arms, `Round31T15Benchmarks`, DefaultJob)

| Method                         | N    |      Mean |   StdDev | Allocated |
|------------------------------- |----- |----------:|---------:|----------:|
| Widen_SelectToList_Twin        | 16   |  19.79 ns |  1.20 ns |     232 B |
| Widen_GeneralMapping_Generated | 16   |  23.08 ns |  0.90 ns |     240 B |
| Widen_SelectToList_Twin        | 1024 | 453.55 ns | 41.08 ns |    8296 B |
| Widen_GeneralMapping_Generated | 1024 | 669.49 ns | 14.57 ns |    8304 B |

The finding reproduces: **1.48x slower than LINQ at 1,024**, 1.17x at 16.

## A/B, one process, one payload (`Round33WidenListBenchmarks`)

`*_Before` is the mapper and helper as emitted before this change, verbatim (the LegacyAmbientFacade convention);
`*_After` calls the generated mapper; `*_LinqTwin` is round 31's bar. Ratios are against `ArrayToList_LinqTwin`.

| Method               | N    |      Mean |   StdDev |    Median | Ratio | Allocated |
|--------------------- |----- |----------:|---------:|----------:|------:|----------:|
| ArrayToList_LinqTwin | 16   |  21.50 ns |  1.64 ns |  20.92 ns |  1.01 |     232 B |
| ArrayToList_Before   | 16   |  24.85 ns |  1.11 ns |  24.72 ns |  1.16 |     240 B |
| ArrayToList_After    | 16   |  17.78 ns |  0.32 ns |  17.83 ns |  0.83 |     240 B |
| ListToArray_LinqTwin | 16   |  21.56 ns |  0.45 ns |  21.54 ns |  1.01 |     224 B |
| ListToArray_Before   | 16   |  18.36 ns |  0.30 ns |  18.28 ns |  0.86 |     176 B |
| ListToArray_After    | 16   |  11.60 ns |  0.21 ns |  11.56 ns |  0.54 |     176 B |
| ListToList_LinqTwin  | 16   |  21.87 ns |  1.15 ns |  21.62 ns |  1.02 |     256 B |
| ListToList_Before    | 16   |  28.27 ns |  0.30 ns |  28.24 ns |  1.32 |     240 B |
| ListToList_After     | 16   |  17.76 ns |  0.32 ns |  17.74 ns |  0.83 |     240 B |
| ArrayToList_LinqTwin | 1024 | 458.32 ns | 25.75 ns | 445.98 ns |  1.00 |    8296 B |
| ArrayToList_Before   | 1024 | 764.23 ns | 66.73 ns | 747.17 ns |  1.67 |    8304 B |
| ArrayToList_After    | 1024 | 394.36 ns | 37.91 ns | 382.44 ns |  0.86 |    8304 B |
| ListToArray_LinqTwin | 1024 | 709.53 ns | 12.44 ns | 712.93 ns |  1.55 |    8288 B |
| ListToArray_Before   | 1024 | 802.65 ns | 43.72 ns | 786.46 ns |  1.76 |    8240 B |
| ListToArray_After    | 1024 | 372.98 ns | 22.06 ns | 370.02 ns |  0.82 |    8240 B |
| ListToList_LinqTwin  | 1024 | 473.64 ns | 13.06 ns | 472.36 ns |  1.04 |    8320 B |
| ListToList_Before    | 1024 | 982.68 ns | 28.66 ns | 983.44 ns |  2.15 |    8304 B |
| ListToList_After     | 1024 | 354.45 ns | 13.44 ns | 352.26 ns |  0.78 |    8304 B |

Before → After (mean):

| Shape                     | N = 16 | N = 1,024 |
|---------------------------|-------:|----------:|
| `int[] → List<long>`      | 1.40x  | **1.94x** |
| `List<int> → List<long>`  | 1.59x  | **2.77x** |
| `List<int> → long[]`      | 1.58x  | **2.15x** |

## What to believe

- **The threshold is cleared in every shape at both sizes** (round 31's bar: ≥ 1.3x or ≥ 20 % allocation,
  consistently). Allocation is unchanged by construction — the same sizes are allocated, only the fill differs.
- **The LINQ verdict is reversed.** The generated path now beats `Select(...).ToList()` in all three shapes; it lost
  to it in all three before.
- **Cross-run drift is real.** `ArrayToList_Before` measured 764 ns here and its twin in the baseline run 669 ns — the
  same code, two runs, a busy machine. That is why the decision rests on the same-process A/B, not on the two runs.
- **Not measured: n ≈ 65,536.** Round 25 found these kernels bandwidth-bound at ~1 MB (`2026-08-23-round25-kernels.md`),
  where the copy strategy stops mattering; nothing here suggests otherwise, and nothing here measures it.

## Correctness, measured separately

`SimdWidenListShapeRuntimeTests` (IntegrationTests): all seven widen pairs through `List<T>`, NaN/±∞ bitwise, every
length 0–65 around the vector boundary plus 1,000, seeded fuzz, null and `default` sources, in every storage pairing,
and Preserve's shared-source identity (which keeps these shapes on the registering helper). `scripts/isa-matrix.ps1`
ran that assembly green on 2026-10-12 under native, 256-bit, 128-bit, SSE-only and no-intrinsics settings — the last
of which executes the scalar tail alone: "identical results at every width, and with intrinsics off".
