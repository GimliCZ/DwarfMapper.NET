# Round 31 — does the ambient collection path grow with the application? (T19 / T14 go-no-go)

Measured 2026-09-27, Windows 10, local 12-core box, `AmbientScanBenchmarks` at `f73378c` + the benchmark, BenchmarkDotNet ShortRun
(1 launch, 3 warmup, 3 iterations). **ShortRun: read the ratios and the slope, not the absolute times.** `Pairs` synthetic
pairs (six IEnumerable<S>-keyed shapes each) are registered on top of the benchmark assembly's own ~190 entries.

| Method                          | Pairs | Mean        | Error        | StdDev     | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |------ |------------:|-------------:|-----------:|------:|--------:|-------:|----------:|------------:|
| Collection_Direct               | 0     |    70.82 ns |    20.178 ns |   1.106 ns |  1.00 |    0.02 | 0.0262 |     440 B |        1.00 |
| Collection_AmbientByRuntimeType | 0     |   271.80 ns |    64.333 ns |   3.526 ns |  3.84 |    0.07 | 0.0262 |     440 B |        1.00 |
| Single_AmbientByRuntimeType     | 0     |    24.12 ns |    98.292 ns |   5.388 ns |  0.34 |    0.07 | 0.0024 |      40 B |        0.09 |
|                                 |       |             |              |            |       |         |        |           |             |
| Collection_Direct               | 100   |    79.16 ns |   193.787 ns |  10.622 ns |  1.01 |    0.16 | 0.0262 |     440 B |        1.00 |
| Collection_AmbientByRuntimeType | 100   | 1,110.49 ns | 4,298.626 ns | 235.622 ns | 14.19 |    3.03 | 0.0257 |     440 B |        1.00 |
| Single_AmbientByRuntimeType     | 100   |    18.38 ns |     5.414 ns |   0.297 ns |  0.23 |    0.03 | 0.0024 |      40 B |        0.09 |
|                                 |       |             |              |            |       |         |        |           |             |
| Collection_Direct               | 500   |    74.25 ns |    80.002 ns |   4.385 ns |  1.00 |    0.07 | 0.0262 |     440 B |        1.00 |
| Collection_AmbientByRuntimeType | 500   | 3,188.76 ns |   696.462 ns |  38.175 ns | 43.05 |    2.32 | 0.0229 |     440 B |        1.00 |
| Single_AmbientByRuntimeType     | 500   |    23.10 ns |     9.552 ns |   0.524 ns |  0.31 |    0.02 | 0.0024 |      40 B |        0.09 |
|                                 |       |             |              |            |       |         |        |           |             |
| Collection_Direct               | 1000  |    72.06 ns |    41.118 ns |   2.254 ns |  1.00 |    0.04 | 0.0262 |     440 B |        1.00 |
| Collection_AmbientByRuntimeType | 1000  | 6,305.97 ns | 3,617.119 ns | 198.266 ns | 87.57 |    3.34 | 0.0153 |     440 B |        1.00 |
| Single_AmbientByRuntimeType     | 1000  |    19.94 ns |     5.343 ns |   0.293 ns |  0.28 |    0.01 | 0.0024 |      40 B |        0.09 |

## Reading

- **Research P4 is confirmed in-repo, on the shipped registry.** `Map<List<D>>(object)` costs 272 ns with no extra pairs and
  6.3 µs with 1,000 — linear in the application's pair count (≈ 6 ns per registered pair), because a collection never
  hits an exact key and the lookup scans every interface-keyed entry, testing destination and assignability on each.
- The single-object control is flat (18–24 ns across the sweep): the exact-key path is unaffected, as expected.
- Allocation is identical in every row (440 B = the result list): the cost is pure scan time, not garbage.
- Reach: FusedChat, the one non-self-authored consumer, has no `Map<TDestination>(object)` call site (its four ambient calls
  are the two-type form T12 serves). The defect is real for any consumer that does take this path.

## After: interface entries bucketed by destination (round 31 T14)

Same machine, same job, same benchmark, re-run on the bucketed registry.

| Method                          | Pairs | Mean      | Error      | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |------ |----------:|-----------:|---------:|------:|--------:|-------:|----------:|------------:|
| Collection_Direct               | 0     |  72.71 ns |  47.109 ns | 2.582 ns |  1.00 |    0.04 | 0.0262 |     440 B |        1.00 |
| Collection_AmbientByRuntimeType | 0     | 108.96 ns | 147.143 ns | 8.065 ns |  1.50 |    0.11 | 0.0262 |     440 B |        1.00 |
| Single_AmbientByRuntimeType     | 0     |  19.38 ns |   3.015 ns | 0.165 ns |  0.27 |    0.01 | 0.0024 |      40 B |        0.09 |
|                                 |       |           |            |          |       |         |        |           |             |
| Collection_Direct               | 100   |  70.86 ns |   6.215 ns | 0.341 ns |  1.00 |    0.01 | 0.0262 |     440 B |        1.00 |
| Collection_AmbientByRuntimeType | 100   | 109.95 ns |  49.619 ns | 2.720 ns |  1.55 |    0.03 | 0.0262 |     440 B |        1.00 |
| Single_AmbientByRuntimeType     | 100   |  18.51 ns |   3.317 ns | 0.182 ns |  0.26 |    0.00 | 0.0024 |      40 B |        0.09 |
|                                 |       |           |            |          |       |         |        |           |             |
| Collection_Direct               | 500   |  70.70 ns |  25.947 ns | 1.422 ns |  1.00 |    0.02 | 0.0262 |     440 B |        1.00 |
| Collection_AmbientByRuntimeType | 500   | 109.77 ns | 100.851 ns | 5.528 ns |  1.55 |    0.07 | 0.0262 |     440 B |        1.00 |
| Single_AmbientByRuntimeType     | 500   |  19.77 ns |   9.063 ns | 0.497 ns |  0.28 |    0.01 | 0.0024 |      40 B |        0.09 |
|                                 |       |           |            |          |       |         |        |           |             |
| Collection_Direct               | 1000  |  71.14 ns |  27.666 ns | 1.516 ns |  1.00 |    0.03 | 0.0262 |     440 B |        1.00 |
| Collection_AmbientByRuntimeType | 1000  | 113.57 ns |  92.444 ns | 5.067 ns |  1.60 |    0.07 | 0.0262 |     440 B |        1.00 |
| Single_AmbientByRuntimeType     | 1000  |  20.14 ns |  16.497 ns | 0.904 ns |  0.28 |    0.01 | 0.0024 |      40 B |        0.09 |

- **Flat.** 109 → 114 ns from 0 to 1,000 extra pairs, where it was 272 ns → 6.3 µs. At 1,000 pairs that is **55×**; even
  at 0 extra pairs it is 2.5×, because the benchmark assembly's own ~190 entries are no longer scanned either.
- Remaining overhead over the direct loop: ~38 ns (1.5×) — the exact miss, the base-type walk and one bucket probe.
  A root-generated per-destination switch (T14 as specified) could only chase that residue, at the cost of a new public
  slot type and a second cache over this one; it was not built.
- Single-object control unchanged (18–20 ns); allocation unchanged (440 B, the result list).
