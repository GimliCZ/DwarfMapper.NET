<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Benchmark results

Every measurement the documentation quotes comes from a file here. Each file records its machine, runtime, job and
method, so a figure is only comparable with figures from the same file. File names keep the date and the
maintainers' task label they were written under; the subject is in the table.

**Current** files are the ones to quote. **Superseded** files stay as the record of how a conclusion was reached
(and for AutoMapper 14.0.0's figures, which are not re-measured); quote them only with their date.

| File | Subject | Status |
|---|---|---|
| [2026-11-15-round35-t25-comparison.md](2026-11-15-round35-t25-comparison.md) | DwarfMapper against current Mapperly and Mapster: speed, allocations, validation, hostile shapes | **Current** comparison |
| [2026-11-14-round35-t24-union-mapping.md](2026-11-14-round35-t24-union-mapping.md) | C# 15 union mapping: cost, and what the other libraries do with a union | **Current** |
| [2026-11-14-round35-t6-span-widen.md](2026-11-14-round35-t6-span-widen.md) | A widening span map takes the vectorized loop | **Current** |
| [2026-10-12-round33-t02-widen-list.md](2026-10-12-round33-t02-widen-list.md) | A widening pair in a `List` takes the vectorized loop | **Current** |
| [2026-10-12-round33-array-null-store-probe.md](2026-10-12-round33-array-null-store-probe.md) | Where the reference-array path's per-element cost comes from | **Current** |
| [2026-09-27-round31-ambient-scan.md](2026-09-27-round31-ambient-scan.md) | Whether the ambient collection path grows with the application | **Current** |
| [2026-09-27-round31-t19-matrix.md](2026-09-27-round31-t19-matrix.md) | Projection, registration start-up and the in-memory projection twin | **Current** |
| [2026-09-26-round31-full-matrix.md](2026-09-26-round31-full-matrix.md) | Every library against every library, before the 2026-11-15 run | Superseded by 2026-11-15 |
| [2026-09-09-collection-decade-sweep.md](2026-09-09-collection-decade-sweep.md) | Collections from 1 to 1,000,000 elements, against every rival | **Current** for size curves |
| [2026-08-26-enum-strategy-like-for-like.md](2026-08-26-enum-strategy-like-for-like.md) | Enum mapping with every library on the same strategy | **Current** |
| [2026-08-24-premerge-full-sweep.md](2026-08-24-premerge-full-sweep.md) | Full sweep, all four libraries | Superseded; the source of AutoMapper figures |
| [2026-08-23-round25-kernels.md](2026-08-23-round25-kernels.md) | SIMD kernel measurements | Superseded |
| [2026-08-23-generator-compile-cost.md](2026-08-23-generator-compile-cost.md) | Generator compile cost at 1,000 mappers | **Current** |
| [2026-08-11-dict-windows-rerun.md](2026-08-11-dict-windows-rerun.md) | Why the dictionary ratio differs by platform | **Current** (the reason the ratio is never quoted alone) |
| [2026-07-26-full-sweep-linux.md](2026-07-26-full-sweep-linux.md) | Full sweep, Linux | Superseded |
| [2026-07-25-realistic-payloads-comparable.md](2026-07-25-realistic-payloads-comparable.md) | Realistic payloads, comparable dictionary arms | Superseded |
| [2026-07-24-realistic-payloads-side-by-side.md](2026-07-24-realistic-payloads-side-by-side.md) | Realistic payloads, side by side | Superseded |
| [2026-07-24-full-sweep-windows.md](2026-07-24-full-sweep-windows.md) | Full sweep, Windows | Superseded |
| [2026-07-05-flat-blit-linux.md](2026-07-05-flat-blit-linux.md) | Flat and blit, Linux | Superseded |

Measurements of the project's own build and test process (test-host memory, test start-up, mutation legs, a consumer
build's generator share) are maintainers' records and live under `Issues/`.
